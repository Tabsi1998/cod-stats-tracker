import { describe, expect, it } from 'vitest'
import { axisLabel, dash, formatCount, formatKd, formatMetric, formatPercent } from './format'

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
