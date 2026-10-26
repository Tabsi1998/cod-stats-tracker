import type { MatchResult, StatsPoint } from './api'
import { matchKd } from './format'

/** Kills, deaths and results of a group of matches, with K/D summed and left matches outside the win rate. */
export interface Tally {
  matches: number
  wins: number
  withResult: number
  kills: number
  deaths: number
  /** Kills and deaths only of matches where both are known, as K/D needs. */
  kdKills: number
  kdDeaths: number
}

const empty = (): Tally => ({ matches: 0, wins: 0, withResult: 0, kills: 0, deaths: 0, kdKills: 0, kdDeaths: 0 })

function add(tally: Tally, point: StatsPoint): Tally {
  tally.matches++
  tally.kills += point.kills ?? 0
  tally.deaths += point.deaths ?? 0
  if (point.kills != null && point.deaths != null) {
    tally.kdKills += point.kills
    tally.kdDeaths += point.deaths
  }
  if (point.result != null && point.status !== 'left') {
    tally.withResult++
    if (point.result === 'win') tally.wins++
  }
  return tally
}

export const tallyKd = (t: Tally) => (t.kdKills === 0 && t.kdDeaths === 0 ? null : matchKd(t.kdKills, t.kdDeaths))
export const tallyWinRate = (t: Tally) => (t.withResult > 0 ? t.wins / t.withResult : null)

export function tally(points: StatsPoint[]): Tally {
  return points.reduce(add, empty())
}

/** A gaming session: matches with less than the gap between them. */
export interface Session extends Tally {
  start: string
  end: string
  points: StatsPoint[]
  /** The match with the most kills (the newer on a tie). */
  bestMatchId: string | null
  bestKills: number | null
  levelUps: number
  xp: number
}

/**
 * Matches grouped into sessions, newest session first: a pause of more than `gapMinutes` between two matches starts a
 * new one. Input is oldest first, as the statistics series comes.
 */
export function sessions(series: StatsPoint[], gapMinutes = 45): Session[] {
  const groups: StatsPoint[][] = []
  for (const point of series) {
    const last = groups.at(-1)
    const previous = last?.at(-1)
    if (last && previous && new Date(point.playedAt).getTime() - new Date(previous.playedAt).getTime() <= gapMinutes * 60_000) last.push(point)
    else groups.push([point])
  }
  return groups
    .map((points) => {
      let best: StatsPoint | null = null
      for (const point of points) if (point.kills != null && (best?.kills == null || point.kills >= best.kills)) best = point
      const levels = points.map((p) => p.level).filter((level): level is number => level != null)
      return {
        ...tally(points),
        start: points[0].playedAt,
        end: points[points.length - 1].playedAt,
        points,
        bestMatchId: best?.matchId ?? null,
        bestKills: best?.kills ?? null,
        levelUps: levels.length > 1 ? Math.max(0, levels[levels.length - 1] - levels[0]) : 0,
        xp: points.reduce((sum, p) => sum + (p.xp ?? 0), 0),
      }
    })
    .reverse()
}

export const weekdays = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'] as const
export const dayparts = [
  { label: 'Morgens', hint: '6–12 Uhr', from: 6, to: 12 },
  { label: 'Nachmittags', hint: '12–18 Uhr', from: 12, to: 18 },
  { label: 'Abends', hint: '18–24 Uhr', from: 18, to: 24 },
  { label: 'Nachts', hint: '0–6 Uhr', from: 0, to: 6 },
] as const

/** The matches per weekday (Monday first) and per part of the day, in the viewer's time zone. */
export function whenPlayed(series: StatsPoint[]): { weekdays: Tally[]; dayparts: Tally[] } {
  const byDay = weekdays.map(empty)
  const byPart = dayparts.map(empty)
  for (const point of series) {
    const at = new Date(point.playedAt)
    add(byDay[(at.getDay() + 6) % 7], point)
    const hour = at.getHours()
    add(byPart[dayparts.findIndex((part) => hour >= part.from && hour < part.to)], point)
  }
  return { weekdays: byDay, dayparts: byPart }
}

/**
 * The form: K/D over the last `window` matches at each point (summed, like every K/D), next to the running K/D.
 * Null until a match with kills and deaths is in the window.
 */
export function formCurve(series: StatsPoint[], window = 10): (number | null)[] {
  return series.map((_, index) => tallyKd(tally(series.slice(Math.max(0, index - window + 1), index + 1))))
}

/** Places in the lobby, read on the final scoreboard: first, top three, upper and lower half. */
export function lobbyPlaces(series: StatsPoint[]): { first: number; podium: number; upper: number; lower: number; total: number } {
  const places = { first: 0, podium: 0, upper: 0, lower: 0, total: 0 }
  for (const { lobbyRank: rank, lobbySize: size } of series) {
    if (rank == null || size == null || size < 2 || rank < 1 || rank > size) continue
    places.total++
    if (rank === 1) places.first++
    else if (rank <= 3) places.podium++
    else if (rank <= size / 2) places.upper++
    else places.lower++
  }
  return places
}

/** The outcome colour of a point, as the form strip shows it. */
export const pointOutcome = (point: Pick<StatsPoint, 'status' | 'result'>): MatchResult | 'left' | 'unknown' =>
  point.status === 'left' ? 'left' : (point.result ?? 'unknown')
