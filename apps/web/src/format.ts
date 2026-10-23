import type { Game, GameGroup, LeaderboardMetric, MatchResult } from './api'

const number = new Intl.NumberFormat('de-AT')
const decimal = new Intl.NumberFormat('de-AT', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const date = new Intl.DateTimeFormat('de-AT', { dateStyle: 'medium', timeStyle: 'short' })
const day = new Intl.DateTimeFormat('de-AT', { day: '2-digit', month: '2-digit' })

export const dash = '–'

export const formatCount = (value: number | null | undefined) => (value == null ? dash : number.format(value))
export const formatKd = (value: number | null | undefined) => (value == null ? dash : decimal.format(value))
export const formatPercent = (value: number | null | undefined) =>
  value == null ? dash : `${new Intl.NumberFormat('de-AT', { maximumFractionDigits: 1 }).format(value * 100)} %`
export const formatDate = (iso: string | null | undefined) => (iso ? date.format(new Date(iso)) : dash)
export const formatDay = (iso: string) => day.format(new Date(iso))

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
