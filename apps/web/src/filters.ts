import type { FilterQuery, GameGroup } from './api'

/** The period choices; "today" starts at the viewer's local midnight. */
export type PeriodKey = 'all' | 'today' | 'days7' | 'days30' | 'last10' | 'last25' | 'last50'

export const periodLabels: Record<PeriodKey, string> = {
  all: 'Gesamt',
  today: 'Heute',
  days7: '7 Tage',
  days30: '30 Tage',
  last10: 'Letzte 10',
  last25: 'Letzte 25',
  last50: 'Letzte 50',
}

/** Win, loss or left: the result filter, as one choice (a left match has no result). */
export type Outcome = 'win' | 'loss' | 'left'

export const outcomeFilterLabels: Record<Outcome, string> = { win: 'Siege', loss: 'Niederlagen', left: 'Verlassen' }

/** What the profile shows: every choice lives in the URL, so a filtered view can be shared. */
export interface Filters {
  game: GameGroup
  period: PeriodKey
  mode?: string
  map?: string
  weapon?: string
  weaponClass?: string
  outcome?: Outcome
}

const games: GameGroup[] = ['mw4', 'warzone', 'all']
const periods = Object.keys(periodLabels) as PeriodKey[]
const outcomes = Object.keys(outcomeFilterLabels) as Outcome[]
const names = ['mode', 'map', 'weapon', 'weaponClass'] as const

export function readFilters(params: URLSearchParams): Filters {
  const game = params.get('game') as GameGroup
  const period = params.get('period') as PeriodKey
  const outcome = params.get('outcome') as Outcome
  const filters: Filters = {
    game: games.includes(game) ? game : 'mw4',
    period: periods.includes(period) ? period : 'all',
    outcome: outcomes.includes(outcome) ? outcome : undefined,
  }
  for (const name of names) {
    const value = params.get(name)?.trim()
    if (value) filters[name] = value.slice(0, 64)
  }
  return filters
}

/** The filters written into the URL's parameters; defaults are left out, other parameters (the tab) kept. */
export function writeFilters(params: URLSearchParams, filters: Filters): URLSearchParams {
  const next = new URLSearchParams(params)
  const values: Record<string, string | undefined> = {
    game: filters.game === 'mw4' ? undefined : filters.game,
    period: filters.period === 'all' ? undefined : filters.period,
    mode: filters.mode,
    map: filters.map,
    weapon: filters.weapon,
    weaponClass: filters.weaponClass,
    outcome: filters.outcome,
  }
  for (const [key, value] of Object.entries(values)) {
    if (value) next.set(key, value)
    else next.delete(key)
  }
  return next
}

/** How many filters narrow the matches beyond game and period. */
export const activeFilters = (filters: Filters) => [...names, 'outcome' as const].filter((name) => filters[name] != null).length

function startOfDay(now: Date): Date {
  const day = new Date(now)
  day.setHours(0, 0, 0, 0)
  return day
}

/** The API query of the filters at a moment in time. */
export function toQuery(filters: Filters, now: Date = new Date()): FilterQuery {
  const query: FilterQuery = {
    game: filters.game,
    mode: filters.mode,
    map: filters.map,
    weapon: filters.weapon,
    weaponClass: filters.weaponClass,
    result: filters.outcome === 'win' || filters.outcome === 'loss' ? filters.outcome : undefined,
    status: filters.outcome === 'left' ? 'left' : undefined,
  }
  switch (filters.period) {
    case 'today':
      return { ...query, from: startOfDay(now).toISOString() }
    case 'days7':
      return { ...query, days: 7 }
    case 'days30':
      return { ...query, days: 30 }
    case 'last10':
      return { ...query, last: 10 }
    case 'last25':
      return { ...query, last: 25 }
    case 'last50':
      return { ...query, last: 50 }
    default:
      return query
  }
}

/**
 * The same filters over the period right before, to compare with: yesterday for today, the 7 days before the last 7.
 * Null for "all" and the last-N choices, which have no period before them.
 */
export function previousQuery(filters: Filters, now: Date = new Date()): FilterQuery | null {
  const base = toQuery({ ...filters, period: 'all' }, now)
  const day = 24 * 60 * 60 * 1000
  switch (filters.period) {
    case 'today': {
      const today = startOfDay(now)
      const yesterday = new Date(today)
      yesterday.setDate(today.getDate() - 1)
      return { ...base, from: yesterday.toISOString(), to: new Date(today.getTime() - 1).toISOString() }
    }
    case 'days7':
    case 'days30': {
      const days = filters.period === 'days7' ? 7 : 30
      return {
        ...base,
        from: new Date(now.getTime() - 2 * days * day).toISOString(),
        to: new Date(now.getTime() - days * day).toISOString(),
      }
    }
    default:
      return null
  }
}

/** The words of the period before, for "vs. …" hints. */
export const previousLabels: Partial<Record<PeriodKey, string>> = { today: 'gestern', days7: 'den 7 Tagen davor', days30: 'den 30 Tagen davor' }
