import type { CSSProperties } from 'react'
import type { Facets, FacetValue } from '../api'
import { activeFilters, outcomeFilterLabels, periodLabels, type Filters, type Outcome, type PeriodKey } from '../filters'
import { formatCount } from '../format'
import type { Load } from '../load'
import { GameSwitch, Icon, Segmented, Select } from '../ui'

const choices = (values: FacetValue[] | undefined) =>
  (values ?? []).map((facet) => ({ value: facet.name, label: `${facet.name} · ${formatCount(facet.matches)}` }))

/**
 * Game and period on the first line, what narrows the matches on the second: mode, map, weapon, class and outcome,
 * each listing only what this player has played, with its count. A chosen filter carries the yellow border.
 */
export function FilterBar({ filters, facets, onChange }: { filters: Filters; facets: Load<Facets>; onChange: (next: Filters) => void }) {
  const f = facets.state === 'ready' ? facets.data : null
  const set = (patch: Partial<Filters>) => onChange({ ...filters, ...patch })
  const active = activeFilters(filters)
  return (
    <div className="rise space-y-3" style={{ '--i': 1 } as CSSProperties}>
      <div className="flex flex-wrap items-center gap-3">
        <GameSwitch value={filters.game} onChange={(game) => onChange({ game, period: filters.period })} />
        <Segmented
          label="Zeitraum"
          value={filters.period}
          onChange={(period: PeriodKey) => set({ period })}
          options={(Object.keys(periodLabels) as PeriodKey[]).map((key) => ({ value: key, label: periodLabels[key] }))}
        />
      </div>
      <div className="flex flex-wrap items-center gap-2 sm:gap-3" role="group" aria-label="Filter">
        <Icon name="filter" className="hidden size-5 text-faint sm:block" />
        <Select label="Modus" all="Alle Modi" value={filters.mode ?? ''} options={choices(f?.modes)} onChange={(mode) => set({ mode: mode || undefined })} />
        <Select label="Karte" all="Alle Karten" value={filters.map ?? ''} options={choices(f?.maps)} onChange={(map) => set({ map: map || undefined })} />
        {((f?.weapons.length ?? 0) > 0 || filters.weapon) && (
          <Select label="Waffe" all="Alle Waffen" value={filters.weapon ?? ''} options={choices(f?.weapons)} onChange={(weapon) => set({ weapon: weapon || undefined })} />
        )}
        {((f?.weaponClasses.length ?? 0) > 0 || filters.weaponClass) && (
          <Select
            label="Waffenklasse"
            all="Alle Klassen"
            value={filters.weaponClass ?? ''}
            options={choices(f?.weaponClasses)}
            onChange={(weaponClass) => set({ weaponClass: weaponClass || undefined })}
          />
        )}
        <Select
          label="Ergebnis"
          all="Alle Ergebnisse"
          value={filters.outcome ?? ''}
          options={(Object.keys(outcomeFilterLabels) as Outcome[]).map((key) => ({ value: key, label: outcomeFilterLabels[key] }))}
          onChange={(outcome) => set({ outcome: (outcome || undefined) as Outcome | undefined })}
        />
        {active > 0 && (
          <button
            type="button"
            onClick={() => onChange({ game: filters.game, period: filters.period })}
            className="display inline-flex items-center gap-1.5 px-2 py-2 text-[0.95rem] font-semibold tracking-[0.06em] text-muted transition-colors duration-200 hover:text-you"
          >
            <Icon name="close" className="size-4" />
            {active === 1 ? 'Filter aufheben' : `${active} Filter aufheben`}
          </button>
        )}
      </div>
    </div>
  )
}
