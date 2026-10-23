import type { ReactNode } from 'react'
import type { GameGroup } from './api'
import { groupLabels } from './format'

export function Card({ children, className = '' }: { children: ReactNode; className?: string }) {
  return <section className={`rounded-xl border border-line bg-surface p-5 ${className}`}>{children}</section>
}

export function StatCard({ label, value, hint }: { label: string; value: string; hint?: string }) {
  return (
    <div className="rounded-xl border border-line bg-surface px-4 py-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted">{label}</div>
      <div className="tabular mt-1 text-2xl font-semibold">{value}</div>
      {hint && <div className="mt-0.5 text-xs text-muted">{hint}</div>}
    </div>
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
    <div role="radiogroup" aria-label={label} className="inline-flex rounded-lg border border-line bg-surface p-1">
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={option.value === value}
          onClick={() => onChange(option.value)}
          className={`rounded-md px-3 py-1.5 text-sm transition-colors ${
            option.value === value ? 'bg-raised text-text' : 'text-muted hover:text-text'
          }`}
        >
          {option.label}
        </button>
      ))}
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

export function Message({ children }: { children: ReactNode }) {
  return <p className="py-8 text-center text-muted">{children}</p>
}

export function CommunityNote() {
  return (
    <p className="text-xs text-muted">
      Community-erfasst: Die Werte stammen aus der Bilderkennung auf den PCs der Spieler und sind nicht von Activision
      bestätigt.
    </p>
  )
}
