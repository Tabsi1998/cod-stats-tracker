import { useState } from 'react'
import { Link } from 'react-router'
import { api, type GameGroup, type LeaderboardMetric } from '../api'
import { formatCount, formatMetric, groupLabels, metricLabels } from '../format'
import { useLoad } from '../load'
import { Card, CommunityNote, GameSwitch, Message, Segmented } from '../ui'

export function Leaderboards() {
  const [game, setGame] = useState<GameGroup>('mw4')
  const [metric, setMetric] = useState<LeaderboardMetric>('kd')
  const board = useLoad(() => api.leaderboard(game, metric), [game, metric])

  return (
    <div className="space-y-6">
      <h1 className="pt-4 text-3xl font-bold tracking-tight">Rankings</h1>
      <div className="flex flex-wrap gap-3">
        <GameSwitch value={game} onChange={setGame} />
        <Segmented
          label="Wertung"
          value={metric}
          onChange={setMetric}
          options={(Object.keys(metricLabels) as LeaderboardMetric[]).map((key) => ({ value: key, label: metricLabels[key] }))}
        />
      </div>
      <Card>
        <h2 className="mb-1 font-semibold">
          {metricLabels[metric]} · {groupLabels[game]}
        </h2>
        {board.state === 'ready' && (
          <p className="mb-3 text-xs text-muted">Nur Spieler mit mindestens {board.data.minMatches} erfassten Matches.</p>
        )}
        {board.state === 'loading' && <Message>Wird geladen …</Message>}
        {board.state === 'error' && <Message>Rankings konnten nicht geladen werden.</Message>}
        {board.state === 'ready' && board.data.entries.length === 0 && <Message>Noch niemand erfüllt die Mindestanzahl.</Message>}
        {board.state === 'ready' && board.data.entries.length > 0 && (
          <table className="tabular w-full text-sm">
            <thead className="text-left text-xs uppercase text-muted">
              <tr>
                <th className="w-12 py-2 font-medium">#</th>
                <th className="py-2 font-medium">Spieler</th>
                <th className="py-2 text-right font-medium">Matches</th>
                <th className="py-2 text-right font-medium">Wert</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-line">
              {board.data.entries.map((entry) => (
                <tr key={entry.playerId}>
                  <td className="py-2 text-muted">{entry.rank}</td>
                  <td className="py-2">
                    <Link to={`/player/${entry.playerId}`} className="hover:text-accent">
                      {entry.displayName}
                    </Link>
                  </td>
                  <td className="py-2 text-right text-muted">{formatCount(entry.matches)}</td>
                  <td className="py-2 text-right font-medium">{formatMetric(metric, entry.value)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Card>
      <CommunityNote />
    </div>
  )
}
