import { describe, expect, it } from 'vitest'
import type { StatsPoint } from './api'
import { formCurve, lobbyPlaces, sessions, tally, tallyKd, tallyWinRate, whenPlayed } from './analysis'
import { activeFilters, previousQuery, readFilters, toQuery, writeFilters } from './filters'

let id = 0
function point(at: string, kills: number | null, deaths: number | null, result: StatsPoint['result'], extra: Partial<StatsPoint> = {}): StatsPoint {
  id++
  return { matchId: `m${id}`, game: 'mw4_mp', playedAt: at, kills, deaths, result, runningKdRatio: null, runningWinRate: null, ...extra }
}

describe('analysis', () => {
  it('sums K/D over matches and leaves left matches out of the win rate', () => {
    const t = tally([point('2026-10-24T18:00:00', 10, 5, 'win'), point('2026-10-24T18:20:00', 2, null, 'loss'), point('2026-10-24T18:40:00', 4, 5, null, { status: 'left' })])
    expect(tallyKd(t)).toBe(1.4) // (10 + 4) / (5 + 5); the match without deaths is not in K/D
    expect(tallyWinRate(t)).toBe(0.5)
    expect(t.kills).toBe(16)
  })

  it('splits sessions at a pause longer than the gap, newest first', () => {
    const series = [
      point('2026-10-24T18:00:00', 10, 5, 'win', { level: 20 }),
      point('2026-10-24T18:30:00', 25, 5, 'win', { level: 21 }),
      point('2026-10-24T19:10:00', 25, 9, 'loss', { level: 23 }),
      point('2026-10-25T14:00:00', 3, 9, 'loss'),
    ]
    const found = sessions(series, 45)
    expect(found.map((s) => s.matches)).toEqual([1, 3])
    expect(found[1].bestMatchId).toBe(series[2].matchId) // the newer of two with 25 kills
    expect(found[1].levelUps).toBe(3)
    expect(found[1].wins).toBe(2)
  })

  it('buckets matches by weekday and part of the day in local time', () => {
    // 24.10.2026 is a Saturday.
    const when = whenPlayed([point('2026-10-24T20:00:00', 1, 1, 'win'), point('2026-10-24T02:00:00', 1, 1, 'loss')])
    expect(when.weekdays[5].matches).toBe(2)
    expect(when.dayparts.map((d) => d.matches)).toEqual([0, 0, 1, 1])
  })

  it('draws the form as K/D over the last matches', () => {
    const series = [point('2026-10-24T18:00:00', 10, 10, 'win'), point('2026-10-24T18:10:00', 30, 10, 'win'), point('2026-10-24T18:20:00', 0, 10, 'loss')]
    expect(formCurve(series, 2)).toEqual([1, 2, 1.5])
  })

  it('counts lobby places and skips implausible ones', () => {
    const series = [
      point('2026-10-24T18:00:00', 1, 1, 'win', { lobbyRank: 1, lobbySize: 12 }),
      point('2026-10-24T18:10:00', 1, 1, 'win', { lobbyRank: 3, lobbySize: 12 }),
      point('2026-10-24T18:20:00', 1, 1, 'win', { lobbyRank: 5, lobbySize: 12 }),
      point('2026-10-24T18:30:00', 1, 1, 'win', { lobbyRank: 12, lobbySize: 12 }),
      point('2026-10-24T18:40:00', 1, 1, 'win', { lobbyRank: 13, lobbySize: 12 }),
    ]
    expect(lobbyPlaces(series)).toEqual({ first: 1, podium: 1, upper: 1, lower: 1, total: 4 })
  })
})

describe('filters', () => {
  it('reads and writes the URL, leaving defaults out and the tab alone', () => {
    const filters = readFilters(new URLSearchParams('tab=modes&period=days7&mode=KAPERN&outcome=left&game=nonsense'))
    expect(filters).toEqual({ game: 'mw4', period: 'days7', mode: 'KAPERN', outcome: 'left' })
    expect(activeFilters(filters)).toBe(2)
    const written = writeFilters(new URLSearchParams('tab=modes'), { ...filters, period: 'all', mode: undefined })
    expect(written.toString()).toBe('tab=modes&outcome=left')
  })

  it('turns the outcome into result or status and today into local midnight', () => {
    const now = new Date(2026, 9, 25, 15, 30)
    expect(toQuery({ game: 'mw4', period: 'all', outcome: 'win' }, now)).toMatchObject({ result: 'win', status: undefined })
    expect(toQuery({ game: 'mw4', period: 'all', outcome: 'left' }, now)).toMatchObject({ result: undefined, status: 'left' })
    expect(toQuery({ game: 'mw4', period: 'today' }, now).from).toBe(new Date(2026, 9, 25).toISOString())
  })

  it('compares with the period right before, or not at all', () => {
    const now = new Date(2026, 9, 25, 15, 30)
    const before = previousQuery({ game: 'mw4', period: 'days7', mode: 'KAPERN' }, now)!
    expect(before.mode).toBe('KAPERN')
    expect(before.days).toBeUndefined()
    expect(new Date(String(before.from)).getTime()).toBe(now.getTime() - 14 * 86_400_000)
    expect(new Date(String(before.to)).getTime()).toBe(now.getTime() - 7 * 86_400_000)
    expect(previousQuery({ game: 'mw4', period: 'today' }, now)!.from).toBe(new Date(2026, 9, 24).toISOString())
    expect(previousQuery({ game: 'mw4', period: 'last10' }, now)).toBeNull()
  })
})
