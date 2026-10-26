import { useEffect, useRef, type CSSProperties, type ReactNode } from 'react'
import { useCountUp } from './motion'
import type { GameGroup } from './api'
import { groupLabels, outcomeFill, outcomeLabels, outcomeText, type Outcome } from './format'

/** One stroke family for every icon: 1.75 px, round caps, 20 px box. */
const iconPaths = {
  search: 'M9 15.5a6.5 6.5 0 1 0 0-13 6.5 6.5 0 0 0 0 13ZM13.7 13.7 18 18',
  back: 'M12.5 4.5 7 10l5.5 5.5',
  down: 'M10 4v12m0 0-4.5-4.5M10 16l4.5-4.5',
  up: 'M10 16V4m0 0-4.5 4.5M10 4l4.5 4.5',
  close: 'M5 5l10 10M15 5 5 15',
  filter: 'M3 4.5h14M5.5 10h9M8 15.5h4',
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
    <div role="radiogroup" aria-label={label} className="flex max-w-full flex-wrap border border-line bg-deep/70">
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

/**
 * A choice from a list, set like the segmented control. While it narrows something (a value is chosen) its border is
 * yellow: the current selection.
 */
export function Select({
  label,
  value,
  options,
  onChange,
  all,
}: {
  label: string
  value: string
  options: { value: string; label: string }[]
  onChange: (value: string) => void
  /** The text of the empty choice ("Alle Modi"). */
  all: string
}) {
  // A value from a shared link that this player's list does not offer is still shown, not silently dropped.
  const shown = value && !options.some((option) => option.value === value) ? [{ value, label: value }, ...options] : options
  return (
    <label className="relative flex min-w-0 items-center">
      <span className="sr-only">{label}</span>
      <select
        value={value}
        onChange={(event) => onChange(event.target.value)}
        className={`display max-w-[16rem] min-w-0 appearance-none truncate border bg-deep/70 py-2 pr-9 pl-3 text-[0.95rem] font-semibold tracking-[0.06em] outline-none transition-colors duration-200 focus:border-you ${
          value ? 'border-you text-text' : 'border-line text-muted hover:text-text'
        }`}
      >
        <option value="">{all}</option>
        {shown.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <Icon name="down" className={`pointer-events-none absolute right-3 size-3.5 ${value ? 'text-you' : 'text-faint'}`} />
    </label>
  )
}

/**
 * The change against the period before: an arrow and the difference, cyan when it got better, orange when worse.
 * Nothing when either value is unknown.
 */
export function Delta({ now, before, format, against, lowerIsBetter = false }: {
  now: number | null
  before: number | null | undefined
  format: (value: number) => string
  against: string
  lowerIsBetter?: boolean
}) {
  if (now == null || before == null) return null
  const change = now - before
  if (Math.abs(change) < 1e-9) return <span className="text-faint">gleich wie {against}</span>
  const better = lowerIsBetter ? change < 0 : change > 0
  return (
    <span className={`inline-flex items-center gap-1 ${better ? 'text-win' : 'text-loss'}`}>
      <Icon name={change > 0 ? 'up' : 'down'} className="size-3.5" />
      <span>
        {change > 0 ? '+' : '−'}
        {format(Math.abs(change))} <span className="text-faint">ggü. {against}</span>
      </span>
    </span>
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
  const list = useRef<HTMLDivElement>(null)
  const counts = options.map((option) => option.count ?? '').join()
  // On a phone the active tab may sit outside the visible strip (a shared link to "Rekorde"): bring it in.
  useEffect(() => {
    const active = list.current?.querySelector<HTMLElement>('[aria-selected="true"]')
    const strip = list.current?.parentElement
    if (active && strip) strip.scrollLeft = Math.max(0, active.offsetLeft - (strip.clientWidth - active.offsetWidth) / 2)
    // Counts arrive after the tabs and widen them, so their arrival moves the active tab too.
  }, [value, counts])
  return (
    // The tabs scroll sideways on a phone; the bar sits in the scrolled grid, so it stays under its tab.
    <div className="overflow-x-auto border-b border-line [scrollbar-width:none]">
      <div
        ref={list}
        role="tablist"
        aria-label={label}
        className="relative grid w-max min-w-full"
        style={{ gridTemplateColumns: `repeat(${options.length}, minmax(5.5rem, 1fr))` }}
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
              className={`display px-2 pt-3 pb-2.5 text-base font-bold tracking-[0.08em] whitespace-nowrap transition-colors duration-200 sm:px-3 sm:text-lg ${
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

/** The last results, oldest left: one equal cell per match in the outcome's colour. */
export function FormStrip({ results }: { results: { outcome: Outcome; id: string }[] }) {
  return (
    <div>
      <div className="grid grid-cols-10 gap-1.5" aria-hidden="true">
        {results.map((result, i) => (
          <span key={result.id} className={`cell-in block aspect-square ${outcomeFill[result.outcome]}`} style={{ '--i': i } as CSSProperties} />
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
