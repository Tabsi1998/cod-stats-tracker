import { Link } from 'react-router'
import { api } from '../api'
import { formatCount, formatKd } from '../format'
import { SearchBox } from './Search'
import { useLoad } from '../load'
import { Card, CommunityNote, Message, StatCard } from '../ui'

export function Home() {
  const platform = useLoad(api.platformStats, [])
  const top = useLoad(() => api.leaderboard('all', 'kd'), [])

  return (
    <div className="space-y-8">
      <section className="space-y-4 pt-6">
        <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">Deine CoD-Statistiken, automatisch erfasst.</h1>
        <p className="max-w-2xl text-muted">
          Der Windows-Client liest am Matchende deine eigenen Werte aus MW4 Multiplayer und Warzone und überträgt sie
          hierher. Kein Konto, kein Passwort. Veröffentlicht wird nur, wer zustimmt.
        </p>
        <SearchBox />
      </section>

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <StatCard label="Spieler" value={platform.state === 'ready' ? formatCount(platform.data.players) : '…'} />
        <StatCard label="Erfasste Matches" value={platform.state === 'ready' ? formatCount(platform.data.matches) : '…'} />
        <StatCard label="Erfasste Kills" value={platform.state === 'ready' ? formatCount(platform.data.kills) : '…'} />
      </div>

      <div className="grid gap-6 md:grid-cols-2">
        <Card>
          <div className="mb-3 flex items-baseline justify-between">
            <h2 className="font-semibold">Top K/D (Übersicht)</h2>
            <Link to="/leaderboards" className="text-sm text-accent hover:underline">
              Alle Rankings
            </Link>
          </div>
          {top.state === 'ready' && top.data.entries.length === 0 && (
            <Message>Noch niemand hat genug Matches ({top.data.minMatches}) für ein Ranking.</Message>
          )}
          {top.state === 'ready' && (
            <ol className="divide-y divide-line">
              {top.data.entries.slice(0, 5).map((entry) => (
                <li key={entry.playerId} className="flex items-center justify-between py-2">
                  <Link to={`/player/${entry.playerId}`} className="hover:text-accent">
                    <span className="tabular mr-3 text-muted">{entry.rank}.</span>
                    {entry.displayName}
                  </Link>
                  <span className="tabular font-medium">{formatKd(entry.value)}</span>
                </li>
              ))}
            </ol>
          )}
          {top.state === 'error' && <Message>Rankings konnten nicht geladen werden.</Message>}
        </Card>

        <Card>
          <h2 className="mb-3 font-semibold">Windows-Client</h2>
          <p className="mb-4 text-sm text-muted">
            Einmal installieren, zustimmen und vergessen. Der Client startet mit Windows, wartet im Tray auf das Spiel und
            synchronisiert deine Matches automatisch.
          </p>
          <a
            href="/downloads/CodStatsSetup.exe"
            className="inline-block rounded-lg bg-accent px-4 py-2 font-medium text-bg hover:brightness-110"
          >
            Für Windows herunterladen
          </a>
          <p className="mt-3 text-xs text-muted">Windows 10/11, 64 Bit. Kein Zugriff auf den Spielspeicher.</p>
        </Card>
      </div>

      <CommunityNote />
    </div>
  )
}
