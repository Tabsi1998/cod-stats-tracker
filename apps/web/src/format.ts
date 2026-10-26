import type { Game, GameGroup, LeaderboardMetric, MatchResult, PublicMatch } from './api'

const number = new Intl.NumberFormat('de-AT')
const decimal = new Intl.NumberFormat('de-AT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const date = new Intl.DateTimeFormat('de-AT', { dateStyle: 'medium', timeStyle: 'short' })
const day = new Intl.DateTimeFormat('de-AT', { day: '2-digit', month: '2-digit' })

export const dash = '–'

export const formatCount = (value: number | null | undefined) => (value == null ? dash : number.format(value))
export const formatKd = (value: number | null | undefined) => (value == null ? dash : decimal.format(value))
const oneDecimal = new Intl.NumberFormat('de-AT', { maximumFractionDigits: 1 })
/** Averages of counts: one decimal at most ("23" or "20,3"). */
export const formatAverage = (value: number | null | undefined) => (value == null ? dash : oneDecimal.format(value))
export const formatPercent = (value: number | null | undefined) =>
  value == null ? dash : `${new Intl.NumberFormat('de-AT', { maximumFractionDigits: 1 }).format(value * 100)} %`
export const formatDate = (iso: string | null | undefined) => (iso ? date.format(new Date(iso)) : dash)
export const formatDay = (iso: string) => day.format(new Date(iso))
const time = new Intl.DateTimeFormat('de-AT', { hour: '2-digit', minute: '2-digit' })

/** Axis labels: the day, or the time of day when all points fall on the same day (otherwise every label reads the same). */
export function axisLabel(isoTimes: string[]): (iso: string) => string {
  const days = new Set(isoTimes.map((iso) => formatDay(iso)))
  return days.size <= 1 ? (iso) => time.format(new Date(iso)) : formatDay
}

export const groupLabels: Record<GameGroup, string> = {
  mw4: 'MW4',
  warzone: 'Warzone',
  all: 'Übersicht',
}

export const gameLabels: Record<Game, string> = {
  mw4_mp: 'MW4 Multiplayer',
  wz_br: 'Warzone Battle Royale',
  wz_resurgence: 'Warzone Resurgence',
}

export const resultLabels: Record<MatchResult, string> = { win: 'Sieg', loss: 'Niederlage', draw: 'Unentschieden' }

/** How a match ended, for colour and label: the game's own words. */
export type Outcome = MatchResult | 'left' | 'unknown'

export function outcomeOf(match: Pick<PublicMatch, 'status' | 'result'>): Outcome {
  if (match.status === 'left') return 'left'
  return match.result ?? 'unknown'
}

export const outcomeLabels: Record<Outcome, string> = { ...resultLabels, left: 'Verlassen', unknown: 'Ohne Ergebnis' }

/** Text colour per outcome (Tailwind classes from the theme). */
export const outcomeText: Record<Outcome, string> = {
  win: 'text-win',
  loss: 'text-loss',
  draw: 'text-draw',
  left: 'text-left',
  unknown: 'text-muted',
}

/** Background per outcome, for form cells and tags. */
export const outcomeFill: Record<Outcome, string> = {
  win: 'bg-win',
  loss: 'bg-loss',
  draw: 'bg-draw',
  left: 'bg-left',
  unknown: 'bg-line',
}

/** Mode values as the after-action report names them. Unknown keys are shown in readable form. */
const modeStatLabels: Record<string, string> = {
  captures: 'Erobert',
  data_uploaded: 'Daten hochgeladen',
  confirms: 'Bestätigt',
  denies: 'Verweigert',
  defends: 'Verteidigt',
  objective_time: 'Zielzeit',
  plants: 'Gelegt',
  defuses: 'Entschärft',
}

export function modeStatLabel(key: string): string {
  const known = modeStatLabels[key]
  if (known) return known
  const words = key.replaceAll('_', ' ').trim()
  return words.charAt(0).toUpperCase() + words.slice(1)
}

/** K/D of one match; null when either value is unknown. With 0 deaths it is the kill count, as in the totals. */
export function matchKd(kills: number | null, deaths: number | null): number | null {
  if (kills == null || deaths == null) return null
  return deaths > 0 ? Math.round((kills / deaths) * 100) / 100 : kills
}

const weekday = new Intl.DateTimeFormat('de-AT', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })
const clock = new Intl.DateTimeFormat('de-AT', { hour: '2-digit', minute: '2-digit' })
export const formatLongDate = (iso: string) => weekday.format(new Date(iso))
export const formatTime = (iso: string) => clock.format(new Date(iso))

export const metricLabels: Record<LeaderboardMetric, string> = {
  kd: 'Höchste K/D',
  kills: 'Meiste Kills',
  matches: 'Meiste Matches',
  winrate: 'Höchste Winrate',
}

export function formatMetric(metric: LeaderboardMetric, value: number): string {
  switch (metric) {
    case 'kd':
      return formatKd(value)
    case 'winrate':
      return formatPercent(value)
    default:
      return formatCount(value)
  }
}
