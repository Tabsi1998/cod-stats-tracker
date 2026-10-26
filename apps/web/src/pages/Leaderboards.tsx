import { useState, type CSSProperties } from 'react'
import { Link } from 'react-router'
import { api, type GameGroup, type LeaderboardMetric } from '../api'
import { formatCount, formatMetric, groupLabels, metricLabels } from '../format'
import { useLoad } from '../load'
import { Bracket, CommunityNote, GameSwitch, Message, Segmented, Select, Skeleton } from '../ui'

type Days = 'all' | '30' | '7'
const dayLabels: Record<Days, string> = { all: 'Gesamt', '30': '30 Tage', '7': '7 Tage' }

export function Leaderboards() {
  const [game, setGame] = useState<GameGroup>('mw4')
  const [metric, setMetric] = useState<LeaderboardMetric>('kd')
  const [mode, setMode] = useState('')
  const [map, setMap] = useState('')
  const [days, setDays] = useState<Days>('all')
  const facets = useLoad(() => api.allFacets(game), [game])
  const board = useLoad(
    () => api.leaderboard(game, metric, { mode: mode || undefined, map: map || undefined, days: days === 'all' ? undefined : (Number(days) as 7 | 30) }),
    [game, metric, mode, map, days],
  )
  const choices = (values: { name: string; matches: number }[] | undefined) =>
    (values ?? []).map((facet) => ({ value: facet.name, label: `${facet.name} · ${formatCount(facet.matches)}` }))
  const scope = [mode, map, days === 'all' ? '' : dayLabels[days]].filter(Boolean).join(' · ')
  const top = board.state === 'ready' ? Math.max(...board.data.entries.map((e) => e.value), 0) : 0

  return (
    <div className="space-y-8 pt-10">
      <h1 className="display text-5xl font-extrabold">Rankings</h1>
      <div className="flex flex-wrap items-center gap-3">
        <GameSwitch
          value={game}
          onChange={(next) => {
            setGame(next)
            setMode('')
            setMap('')
          }}
        />
        <Segmented
          label="Wertung"
          value={metric}
          onChange={setMetric}
          options={(Object.keys(metricLabels) as LeaderboardMetric[]).map((key) => ({ value: key, label: metricLabels[key] }))}
        />
        <Segmented label="Zeitraum" value={days} onChange={setDays} options={(['all', '30', '7'] as const).map((key) => ({ value: key, label: dayLabels[key] }))} />
        <Select label="Modus" all="Alle Modi" value={mode} options={choices(facets.state === 'ready' ? facets.data.modes : undefined)} onChange={setMode} />
        <Select label="Karte" all="Alle Karten" value={map} options={choices(facets.state === 'ready' ? facets.data.maps : undefined)} onChange={setMap} />
      </div>

      <Bracket className="px-1 py-2 sm:px-3" label={`${metricLabels[metric]}, ${groupLabels[game]}`}>
        <div className="flex flex-wrap items-baseline justify-between gap-2 px-3 pt-3">
          <h2 className="display text-2xl font-bold">
            {metricLabels[metric]} · {scope || groupLabels[game]}
          </h2>
          {board.state === 'ready' && <p className="text-sm text-faint">ab {board.data.minMatches} erfassten Matches</p>}
        </div>
        {board.state === 'loading' && (
          <div className="space-y-px p-3">
            {[0, 1, 2, 3, 4].map((i) => (
              <Skeleton key={i} className="h-12" />
            ))}
          </div>
        )}
        {board.state === 'error' && <Message>Rankings konnten nicht geladen werden.</Message>}
        {board.state === 'ready' && board.data.entries.length === 0 && (
          <Message>Noch niemand hat genug Matches{scope ? ` (${scope})` : ''} für ein Ranking.</Message>
        )}
        {board.state === 'ready' && board.data.entries.length > 0 && (
          <ol className="mt-2">
            {board.data.entries.map((entry, i) => {
              const podium = entry.rank <= 3
              return (
                <li key={entry.playerId} className="rise" style={{ '--i': Math.min(i, 12) } as CSSProperties}>
                  <Link
                    to={`/player/${entry.playerId}`}
                    className="group relative grid grid-cols-[3rem_1fr_auto] items-center gap-3 px-3 py-3 transition-colors duration-200 hover:bg-surface sm:grid-cols-[4rem_1fr_7rem_8rem]"
                  >
                    <span className={`display text-3xl font-extrabold ${entry.rank === 1 ? 'text-you' : podium ? 'text-text' : 'text-faint'}`}>
                      {entry.rank}
                    </span>
                    <span className="min-w-0">
                      <span className="display block truncate text-xl font-bold group-hover:text-you">{entry.displayName}</span>
                      <span aria-hidden="true" className="mt-1.5 block h-1 bg-line/50">
                        <span
                          className={`bar-in block h-full ${entry.rank === 1 ? 'bg-you' : 'bg-win/70'}`}
                          style={{ width: `${top > 0 ? (entry.value / top) * 100 : 0}%`, '--i': Math.min(i, 12) } as CSSProperties}
                        />
                      </span>
                    </span>
                    <span className="hidden text-right text-muted sm:block">{formatCount(entry.matches)} Matches</span>
                    <span className="display text-right text-3xl font-bold">{formatMetric(metric, entry.value)}</span>
                  </Link>
                </li>
              )
            })}
          </ol>
        )}
      </Bracket>
      <CommunityNote />
    </div>
  )
}
