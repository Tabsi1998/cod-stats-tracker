import type { CSSProperties, ReactNode } from 'react'
import { useCountUp } from './motion'
import type { GameGroup } from './api'
import { groupLabels, outcomeFill, outcomeLabels, outcomeText, type Outcome } from './format'

/** One stroke family for every icon: 1.75 px, round caps, 20 px box. */
const iconPaths = {
  search: 'M9 15.5a6.5 6.5 0 1 0 0-13 6.5 6.5 0 0 0 0 13ZM13.7 13.7 18 18',
  back: 'M12.5 4.5 7 10l5.5 5.5',
  down: 'M10 4v12m0 0-4.5-4.5M10 16l4.5-4.5',
  next: 'M7.5 4.5 13 10l-5.5 5.5',
  download: 'M10 3v10m0 0-4-4m4 4 4-4M4 16.5h12',
  trophy: 'M6.5 3.5h7v4a3.5 3.5 0 0 1-7 0v-4ZM6.5 5H3.5v1.5a3 3 0 0 0 3 3M13.5 5h3v1.5a3 3 0 0 1-3 3M10 11v3.5M7 17h6',
  target: 'M10 17a7 7 0 1 0 0-14 7 7 0 0 0 0 14ZM10 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6ZM10 1.5v3M10 15.5v3M1.5 10h3M15.5 10h3',
} as const

export function Icon({ name, className = 'size-5' }: { name: keyof typeof iconPaths; className?: string }) {
  return (
    <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round" className={className} aria-hidden="true">
      <path d={iconPaths[name]} />
    </svg>
  )
}

/**
 * A figure as the after-action report sets it: the number large, its label beneath.
 * The number counts up once; `format` turns the running value into text (unknown stays "–").
 */
export function Figure({
  value,
  format,
  label,
  hint,
  size = 'md',
  tone,
}: {
  value: number | null
  format: (value: number | null) => string
  label: string
  hint?: ReactNode
  size?: 'xl' | 'lg' | 'md'
  tone?: Outcome | 'you'
}) {
  const shown = useCountUp(value)
  const sizes = { xl: 'text-6xl sm:text-8xl', lg: 'text-5xl sm:text-6xl', md: 'text-4xl' }
  const color = tone === 'you' ? 'text-you' : tone ? outcomeText[tone] : 'text-text'
  const text = format(shown == null ? null : value != null && Number.isInteger(value) ? Math.round(shown) : shown)
  // A unit stays on the number's line and steps back in size, as the game sets "XP" beside the total.
  const unit = text.match(/^(.*?)\s?(%)$/)
  return (
    <div className="min-w-0">
      <div className={`display font-bold whitespace-nowrap tabular ${sizes[size]} ${color}`}>
        {unit ? (
          <>
            {unit[1]}
            <span className="ml-1 text-[0.55em]">{unit[2]}</span>
          </>
        ) : (
          text
        )}
      </div>
      <div className="display mt-2 text-sm font-semibold tracking-[0.12em] text-muted">{label}</div>
      {hint && <div className="mt-1 text-sm text-faint">{hint}</div>}
    </div>
  )
}

/** The signature frame: four thin corners around a cluster. */
export function Bracket({
  children,
  className = '',
  tone,
  as: Tag = 'section',
  style,
  label,
}: {
  children: ReactNode
  className?: string
  tone?: 'win' | 'loss' | 'you' | 'left'
  as?: 'section' | 'div' | 'article'
  style?: CSSProperties
  label?: string
}) {
  return (
    <Tag className={`bracket ${tone ? `bracket-${tone}` : ''} ${className}`} style={style} aria-label={label}>
      {children}
    </Tag>
  )
}

/** A panel for supporting content: a raised plate with one hairline, no rounded card. */
export function Panel({ children, className = '', title, action }: { children: ReactNode; className?: string; title?: string; action?: ReactNode }) {
  return (
    <section className={`border-t border-line bg-surface/60 ${className}`}>
      {(title || action) && (
        <div className="flex items-baseline justify-between gap-4 px-4 pt-4 sm:px-5">
          {title && <h2 className="display text-xl font-bold tracking-wide">{title}</h2>}
          {action}
        </div>
      )}
      <div className="p-4 sm:p-5">{children}</div>
    </section>
  )
}

export function Segmented<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
  label: string
}) {
  return (
    <div role="radiogroup" aria-label={label} className="flex max-w-full overflow-x-auto border border-line bg-deep/70">
      {options.map((option) => {
        const active = option.value === value
        return (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={active}
            onClick={() => onChange(option.value)}
            className={`display relative shrink-0 px-3.5 py-2 text-[0.95rem] font-semibold tracking-[0.06em] transition-colors duration-200 ${
              active ? 'bg-raised text-text' : 'text-muted hover:bg-surface hover:text-text'
            }`}
          >
            {option.label}
            {active && <span aria-hidden="true" className="absolute inset-x-2 bottom-0 h-0.5 bg-you" />}
          </button>
        )
      })}
    </div>
  )
}

export function GameSwitch({ value, onChange }: { value: GameGroup; onChange: (value: GameGroup) => void }) {
  return (
    <Segmented
      label="Spiel"
      value={value}
      onChange={onChange}
      options={(['mw4', 'warzone', 'all'] as const).map((group) => ({ value: group, label: groupLabels[group] }))}
    />
  )
}

/** Tabs that switch a view in place; the yellow bar slides to the active tab. */
export function Tabs<T extends string>({
  value,
  options,
  onChange,
  label,
}: {
  value: T
  options: { value: T; label: string; count?: number }[]
  onChange: (value: T) => void
  label: string
}) {
  const index = Math.max(0, options.findIndex((option) => option.value === value))
  return (
    <div
      role="tablist"
      aria-label={label}
      className="relative grid overflow-x-auto border-b border-line sm:max-w-2xl"
      style={{ gridTemplateColumns: `repeat(${options.length}, minmax(5rem, 1fr))` }}
    >
      {options.map((option) => {
        const active = option.value === value
        return (
          <button
            key={option.value}
            type="button"
            role="tab"
            id={`tab-${option.value}`}
            aria-selected={active}
            aria-controls={`panel-${option.value}`}
            onClick={() => onChange(option.value)}
            className={`display px-2 pt-3 pb-2.5 text-base font-bold tracking-[0.08em] transition-colors duration-200 sm:px-3 sm:text-lg ${
              active ? 'text-text' : 'text-muted hover:text-text'
            }`}
          >
            {option.label}
            {option.count != null && <span className="ml-2 font-sans text-sm font-medium tracking-normal text-faint normal-case">{option.count}</span>}
          </button>
        )
      })}
      <span
        aria-hidden="true"
        className="absolute bottom-0 left-0 h-0.5 bg-you transition-transform duration-500 ease-[var(--ease-out-expo)]"
        style={{ width: `${100 / options.length}%`, transform: `translateX(${index * 100}%)` }}
      />
    </div>
  )
}

/** The outcome as a tag in the game's colours. */
export function OutcomeTag({ outcome, className = '' }: { outcome: Outcome; className?: string }) {
  const tones: Record<Outcome, string> = {
    win: 'bg-win/15 text-win',
    loss: 'bg-loss/15 text-loss',
    draw: 'bg-draw/15 text-draw',
    left: 'bg-left/15 text-left',
    unknown: 'bg-line/40 text-muted',
  }
  return <span className={`display inline-block px-2 py-0.5 text-sm font-bold tracking-[0.1em] ${tones[outcome]} ${className}`}>{outcomeLabels[outcome]}</span>
}

/** The last results, oldest left: one cell per match, height by kills when known. */
export function FormStrip({ results }: { results: { outcome: Outcome; kills: number | null; id: string }[] }) {
  const most = Math.max(1, ...results.map((r) => r.kills ?? 0))
  return (
    <div>
      <div className="flex h-24 items-end gap-1" aria-hidden="true">
        {results.map((result, i) => (
          <span
            key={result.id}
            className={`cell-in block max-w-12 min-w-[6px] flex-1 ${outcomeFill[result.outcome]}`}
            style={{ height: `${result.kills == null ? 30 : 30 + 70 * (result.kills / most)}%`, '--i': i } as CSSProperties}
          />
        ))}
      </div>
      <p className="sr-only">
        Letzte {results.length} Matches: {results.map((r) => outcomeLabels[r.outcome]).join(', ')}
      </p>
    </div>
  )
}

export function Message({ children }: { children: ReactNode }) {
  return <p className="py-10 text-center text-muted">{children}</p>
}

/** Placeholder blocks while data loads, in the shape of what comes. */
export function Skeleton({ className = '' }: { className?: string }) {
  return <div aria-hidden="true" className={`animate-pulse bg-raised/70 ${className}`} />
}

export function CommunityNote() {
  return (
    <p className="max-w-[70ch] text-sm text-faint">
      Community-erfasst: Die Werte stammen aus der Bilderkennung auf den PCs der Spieler und sind nicht von Activision
      bestätigt. Was nicht lesbar war, steht als „–“ da.
    </p>
  )
}
