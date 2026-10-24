import { describe, expect, it } from 'vitest'
import { axisLabel, dash, formatAverage, formatCount, formatKd, formatMetric, formatPercent, matchKd, modeStatLabel, outcomeOf } from './format'

describe('format', () => {
  it('shows missing values as a dash, never as 0', () => {
    expect(formatKd(null)).toBe(dash)
    expect(formatCount(undefined)).toBe(dash)
    expect(formatPercent(null)).toBe(dash)
  })

  it('formats K/D with two decimals in Austrian style', () => {
    expect(formatKd(0.9090909)).toBe('0,91')
    expect(formatKd(2)).toBe('2,00')
  })

  it('formats rates as percent', () => {
    expect(formatPercent(0.5)).toBe('50 %')
    expect(formatPercent(0.4567)).toBe('45,7 %')
  })

  it('formats leaderboard values by metric', () => {
    expect(formatMetric('kd', 1.5)).toBe('1,50')
    expect(formatMetric('winrate', 0.25)).toBe('25 %')
    expect(formatMetric('kills', 1234)).toMatch(/^1\D234$/)
  })

  it('labels a single day by time and several days by date', () => {
    const sameDay = ['2026-10-24T10:06:00Z', '2026-10-24T10:34:00Z']
    expect(axisLabel(sameDay)(sameDay[1])).toMatch(/^\d{2}:\d{2}$/)
    const days = ['2026-10-23T10:00:00Z', '2026-10-24T10:00:00Z']
    expect(axisLabel(days)(days[1])).toMatch(/^24\.10\.$/)
  })
})

describe('match details', () => {
  it('names the outcome: left beats any result, no result is unknown', () => {
    expect(outcomeOf({ status: 'left', result: null })).toBe('left')
    expect(outcomeOf({ status: 'completed', result: 'win' })).toBe('win')
    expect(outcomeOf({ status: 'completed', result: null })).toBe('unknown')
  })

  it('computes a match K/D only from known values, kills when there were no deaths', () => {
    expect(matchKd(8, 8)).toBe(1)
    expect(matchKd(25, 12)).toBe(2.08)
    expect(matchKd(5, 0)).toBe(5)
    expect(matchKd(null, 3)).toBeNull()
  })

  it('shows averages with one decimal at most', () => {
    expect(formatAverage(20.2857)).toBe('20,3')
    expect(formatAverage(23)).toBe('23')
    expect(formatAverage(null)).toBe(dash)
  })

  it('labels mode values as the report does and keeps unknown keys readable', () => {
    expect(modeStatLabel('data_uploaded')).toBe('Daten hochgeladen')
    expect(modeStatLabel('objective_time')).toBe('Zielzeit')
    expect(modeStatLabel('bombs_carried')).toBe('Bombs carried')
  })
})
