import { useState } from 'react'
import { useParams } from 'react-router'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis, Bar, BarChart } from 'recharts'
import { api, type GameGroup, type Period, type Stats } from '../api'
import {
  formatCount,
  formatDate,
  formatDay,
  formatKd,
  formatPercent,
  gameLabels,
  groupLabels,
  resultLabels,
} from '../format'
import { useLoad } from '../load'
import { Card, CommunityNote, GameSwitch, Message, Segmented, StatCard } from '../ui'

type PeriodKey = 'all' | 'last10' | 'last25' | 'last50' | 'last100' | 'days7' | 'days30'

const periods: Record<PeriodKey, { label: string; period: Period }> = {
  all: { label: 'Gesamt', period: {} },
  last10: { label: '10', period: { last: 10 } },
  last25: { label: '25', period: { last: 25 } },
  last50: { label: '50', period: { last: 50 } },
  last100: { label: '100', period: { last: 100 } },
  days7: { label: '7 Tage', period: { days: 7 } },
  days30: { label: '30 Tage', period: { days: 30 } },
}

export function Player() {
  const { id = '' } = useParams()
  const [game, setGame] = useState<GameGroup>('mw4')
  const [periodKey, setPeriodKey] = useState<PeriodKey>('all')
  const profile = useLoad(() => api.player(id), [id])
  const stats = useLoad(() => api.stats(id, game, periods[periodKey].period), [id, game, periodKey])
  const matches = useLoad(() => api.matches(id, game), [id, game])

  if (profile.state === 'loading') return <Message>Profil wird geladen …</Message>
  if (profile.state === 'error')
    return <Message>{profile.notFound ? 'Dieses Profil gibt es nicht oder es ist nicht öffentlich.' : 'Das Profil konnte nicht geladen werden.'}</Message>

  const player = profile.data
  return (
    <div className="space-y-6">
      <header className="pt-4">
        <h1 className="text-3xl font-bold tracking-tight">{player.displayName}</h1>
        <p className="mt-1 text-sm text-muted">
          Dabei seit {formatDate(player.memberSince)} · {formatCount(player.matches)} Matches erfasst
          {player.previousNames.length > 0 && <> · früher: {player.previousNames.join(', ')}</>}
        </p>
      </header>

      <div className="flex flex-wrap items-center gap-3">
        <GameSwitch value={game} onChange={setGame} />
        <Segmented
          label="Zeitraum"
          value={periodKey}
          onChange={setPeriodKey}
          options={(Object.keys(periods) as PeriodKey[]).map((key) => ({
            value: key,
            label: key.startsWith('last') ? `Letzte ${periods[key].label}` : periods[key].label,
          }))}
        />
      </div>

      {game === 'all' && (
        <p className="rounded-lg border border-accent/40 bg-accent/10 px-4 py-2 text-sm">
          Übersicht: MW4 und Warzone zusammengerechnet. Für Vergleiche besser einzeln ansehen.
        </p>
      )}

      {stats.state === 'ready' ? <StatsBlock stats={stats.data} /> : <Message>Statistik wird geladen …</Message>}

      <Card>
        <h2 className="mb-3 font-semibold">Letzte Matches · {groupLabels[game]}</h2>
        {matches.state === 'ready' && matches.data.items.length === 0 && <Message>Noch keine Matches.</Message>}
        {matches.state === 'ready' && matches.data.items.length > 0 && (
          <div className="overflow-x-auto">
            <table className="tabular w-full text-sm">
              <thead className="text-left text-xs uppercase text-muted">
                <tr>
                  <th className="py-2 pr-4 font-medium">Zeitpunkt</th>
                  <th className="py-2 pr-4 font-medium">Modus</th>
                  <th className="py-2 pr-4 text-right font-medium">Kills</th>
                  <th className="py-2 pr-4 text-right font-medium">Deaths</th>
                  <th className="py-2 pr-4 text-right font-medium">Platz</th>
                  <th className="py-2 font-medium">Ergebnis</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {matches.data.items.map((match) => (
                  <tr key={match.id}>
                    <td className="py-2 pr-4 whitespace-nowrap">{formatDate(match.playedAt)}</td>
                    <td className="py-2 pr-4">
                      {match.mode ?? gameLabels[match.game]}
                      {match.map && <span className="text-muted"> · {match.map}</span>}
                    </td>
                    <td className="py-2 pr-4 text-right">{formatCount(match.kills)}</td>
                    <td className="py-2 pr-4 text-right">{formatCount(match.deaths)}</td>
                    <td className="py-2 pr-4 text-right">{formatCount(match.placement)}</td>
                    <td className={`py-2 ${match.result === 'win' ? 'text-win' : match.result === 'loss' ? 'text-loss' : 'text-muted'}`}>
                      {match.result ? resultLabels[match.result] : '–'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <CommunityNote />
    </div>
  )
}

function StatsBlock({ stats }: { stats: Stats }) {
  const series = stats.series.map((point) => ({
    day: formatDay(point.playedAt),
    kd: point.runningKdRatio,
    winRate: point.runningWinRate == null ? null : Math.round(point.runningWinRate * 1000) / 10,
    kills: point.kills,
  }))
  return (
    <>
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4">
        <StatCard label="K/D" value={formatKd(stats.kdRatio)} hint="Kills gesamt ÷ Deaths gesamt" />
        <StatCard label="Kills" value={formatCount(stats.kills)} hint={`Ø ${formatKd(stats.averageKills)} pro Match`} />
        <StatCard label="Deaths" value={formatCount(stats.deaths)} />
        <StatCard label="Matches" value={formatCount(stats.matches)} />
        <StatCard label="Winrate" value={formatPercent(stats.winRate)} hint={`${stats.wins} von ${stats.matchesWithResult}`} />
        <StatCard label="Beste Runde" value={formatCount(stats.bestKills)} hint="Kills" />
        {stats.averagePlacement != null && <StatCard label="Ø Platzierung" value={String(stats.averagePlacement)} />}
      </div>
      {series.length > 1 && (
        <div className="grid gap-6 lg:grid-cols-2">
          <Chart title="K/D-Entwicklung">
            <LineChart data={series}>
              <CartesianGrid stroke="var(--color-line)" vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-muted)" fontSize={12} />
              <YAxis stroke="var(--color-muted)" fontSize={12} width={36} />
              <Tooltip contentStyle={{ background: 'var(--color-raised)', border: 'none', borderRadius: 8 }} />
              <Line type="monotone" dataKey="kd" name="K/D" stroke="var(--color-accent)" dot={false} strokeWidth={2} />
            </LineChart>
          </Chart>
          <Chart title="Kills pro Match">
            <BarChart data={series}>
              <CartesianGrid stroke="var(--color-line)" vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-muted)" fontSize={12} />
              <YAxis stroke="var(--color-muted)" fontSize={12} width={36} allowDecimals={false} />
              <Tooltip contentStyle={{ background: 'var(--color-raised)', border: 'none', borderRadius: 8 }} />
              <Bar dataKey="kills" name="Kills" fill="var(--color-accent)" radius={[3, 3, 0, 0]} />
            </BarChart>
          </Chart>
          <Chart title="Winrate-Entwicklung (%)">
            <LineChart data={series}>
              <CartesianGrid stroke="var(--color-line)" vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-muted)" fontSize={12} />
              <YAxis stroke="var(--color-muted)" fontSize={12} width={36} domain={[0, 100]} />
              <Tooltip contentStyle={{ background: 'var(--color-raised)', border: 'none', borderRadius: 8 }} />
              <Line type="monotone" dataKey="winRate" name="Winrate %" stroke="var(--color-win)" dot={false} strokeWidth={2} />
            </LineChart>
          </Chart>
        </div>
      )}
    </>
  )
}

function Chart({ title, children }: { title: string; children: React.ReactElement }) {
  return (
    <Card>
      <h2 className="mb-3 text-sm font-semibold">{title}</h2>
      <div className="h-56">
        <ResponsiveContainer width="100%" height="100%">
          {children}
        </ResponsiveContainer>
      </div>
    </Card>
  )
}
