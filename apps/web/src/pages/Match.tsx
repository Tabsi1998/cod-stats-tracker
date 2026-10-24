import type { CSSProperties } from 'react'
import { Link, useParams } from 'react-router'
import { api, type PublicMatch, type Stats } from '../api'
import { dash, formatCount, formatKd, formatLongDate, formatTime, gameLabels, matchKd, modeStatLabel, outcomeLabels, outcomeOf, outcomeText } from '../format'
import { useLoad, type Load } from '../load'
import { Bracket, CommunityNote, Figure, Icon, Message, Skeleton } from '../ui'

const count = (value: number | null) => formatCount(value == null ? null : Math.round(value))

/** One match as its after-action report: the result as the banner, the final score, every value that was read. */
export function MatchPage() {
  const { id = '', matchId = '' } = useParams()
  const match = useLoad(() => api.match(id, matchId), [id, matchId])
  const player = useLoad(() => api.player(id), [id])

  const back = (
    <Link to={`/player/${id}?tab=matches`} className="inline-flex items-center gap-1.5 text-muted transition-colors duration-200 hover:text-you">
      <Icon name="back" className="size-4" />
      {player.state === 'ready' ? `Alle Matches von ${player.data.displayName}` : 'Zum Profil'}
    </Link>
  )

  if (match.state === 'loading')
    return (
      <div className="space-y-6 pt-10">
        {back}
        <Skeleton className="h-40 w-2/3" />
        <Skeleton className="h-56" />
      </div>
    )
  if (match.state === 'error')
    return (
      <div className="space-y-4 pt-10">
        {back}
        <Message>{match.notFound ? 'Dieses Match gibt es nicht oder das Profil ist nicht öffentlich.' : 'Das Match konnte nicht geladen werden.'}</Message>
      </div>
    )
  return (
    <div className="space-y-10 pt-10">
      {back}
      <Report match={match.data} playerId={id} playerName={player.state === 'ready' ? player.data.displayName : null} />
      <CommunityNote />
    </div>
  )
}

function Report({ match, playerId, playerName }: { match: PublicMatch; playerId: string; playerName: string | null }) {
  // The average of the same game: MW4 and Warzone numbers are not comparable.
  const average: Load<Stats> = useLoad(() => api.stats(playerId, match.game === 'mw4_mp' ? 'mw4' : 'warzone', {}), [playerId, match.game])
  const outcome = outcomeOf(match)
  const d = match.details
  const tone = outcome === 'win' ? 'win' : outcome === 'loss' ? 'loss' : outcome === 'left' ? 'left' : undefined
  const freeForAll = match.placement != null && match.game === 'mw4_mp'

  const tiles: { label: string; value: number | null; format: (v: number | null) => string; you?: boolean }[] = [
    { label: 'Punkte', value: match.score, format: count },
    { label: 'Abschüsse', value: match.kills, format: count, you: true },
    { label: 'Tode', value: match.deaths, format: count },
    { label: 'K/D', value: matchKd(match.kills, match.deaths), format: formatKd },
    { label: 'Assists', value: match.assists, format: count },
    { label: 'Eliminierungen', value: d?.eliminations ?? null, format: count },
  ]
  const modeStats = Object.entries(d?.modeStats ?? {})

  return (
    <article className="space-y-10">
      <header className="grid gap-8 lg:grid-cols-[1fr_auto] lg:items-end">
        <div className="rise space-y-4">
          <p className="text-muted">
            {formatLongDate(match.playedAt)} · {formatTime(match.playedAt)} Uhr · {gameLabels[match.game]}
            {playerName && <> · {playerName}</>}
          </p>
          <h1 className={`flare display text-7xl font-extrabold sm:text-9xl ${outcomeText[outcome]}`}>{outcomeLabels[outcome]}</h1>
          <div className="flex flex-wrap items-center gap-3">
            {match.mode && <span className="display bg-text px-3 py-1 text-xl font-bold tracking-[0.14em] text-bg">{match.mode}</span>}
            {match.map && <span className="display text-xl font-bold tracking-[0.14em] text-muted">{match.map}</span>}
            {freeForAll && <span className="display text-xl font-bold tracking-[0.14em] text-you">Platz {match.placement}</span>}
          </div>
          {outcome === 'left' && (
            <p className="max-w-[60ch] text-muted">
              Vor dem Ende verlassen. Die Werte stammen vom letzten Scoreboard; das Match zählt für K/D und Kills, nicht für
              die Siegquote.
            </p>
          )}
        </div>
        {d?.teamScore != null && d.enemyScore != null && (
          <div className="rise flex items-end gap-4" style={{ '--i': 1 } as CSSProperties} aria-label={`Endstand ${d.teamScore} zu ${d.enemyScore}`}>
            <ScoreFigure value={d.teamScore} className="text-win" label={freeForAll ? 'Du' : 'Dein Team'} />
            <span className="display mb-9 text-5xl font-bold text-faint" aria-hidden="true">
              |
            </span>
            <ScoreFigure value={d.enemyScore} className="text-loss" label={freeForAll ? 'Platz 1' : 'Gegner'} />
          </div>
        )}
      </header>

      <Bracket tone={tone} className="rise grid grid-cols-2 gap-x-6 gap-y-8 px-5 py-8 sm:grid-cols-3 sm:px-8 lg:grid-cols-6" style={{ '--i': 2 } as CSSProperties} label="Werte">
        {tiles.map((tile) => (
          <Figure key={tile.label} value={tile.value} format={tile.format} label={tile.label} size="lg" tone={tile.you && tile.value != null ? 'you' : undefined} />
        ))}
      </Bracket>

      {average.state === 'ready' && match.status === 'completed' && average.data.matches > 1 && <Comparison match={match} stats={average.data} />}

      <div className="grid gap-8 lg:grid-cols-[2fr_1fr]">
        <section className="rise" style={{ '--i': 3 } as CSSProperties}>
          <h2 className="display mb-4 text-2xl font-bold">Modus-Werte</h2>
          {modeStats.length > 0 ? (
            <dl className="grid grid-cols-2 border-t border-l border-line sm:grid-cols-3">
              {modeStats.map(([key, value]) => (
                <div key={key} className="border-r border-b border-line px-4 py-4">
                  <dt className="text-sm text-faint">{modeStatLabel(key)}</dt>
                  <dd className="display mt-1 text-4xl font-bold">{formatCount(value)}</dd>
                </div>
              ))}
            </dl>
          ) : (
            <p className="text-muted">Für dieses Match wurden keine Modus-Werte erfasst.</p>
          )}
        </section>
        <section className="rise" style={{ '--i': 4 } as CSSProperties}>
          <h2 className="display mb-4 text-2xl font-bold">Fortschritt</h2>
          <dl className="space-y-4 border-t border-line pt-4">
            <div>
              <dt className="text-sm text-faint">XP gesamt</dt>
              <dd className="display text-5xl font-bold text-you">
                {d?.xp != null ? (
                  <>
                    {formatCount(d.xp)} <span className="text-3xl text-muted">XP</span>
                  </>
                ) : (
                  dash
                )}
              </dd>
            </div>
            <div>
              <dt className="text-sm text-faint">Level nach dem Match</dt>
              <dd className="display text-4xl font-bold">{d?.level != null ? `Level ${d.level}` : dash}</dd>
            </div>
          </dl>
        </section>
      </div>
    </article>
  )
}

/** This match against the player's own average: what made it a good or a bad one. */
function Comparison({ match, stats }: { match: PublicMatch; stats: Stats }) {
  const allKd = stats.kdRatio
  const rows: { label: string; value: number | null; average: number | null; format: (v: number) => string }[] = [
    { label: 'Kills', value: match.kills, average: stats.averageKills, format: (v) => formatKd(v) },
    { label: 'K/D', value: matchKd(match.kills, match.deaths), average: allKd, format: (v) => formatKd(v) },
    { label: 'Punkte', value: match.score, average: stats.averageScore, format: (v) => formatCount(Math.round(v)) },
    { label: 'XP', value: match.details?.xp ?? null, average: stats.averageXp, format: (v) => formatCount(Math.round(v)) },
  ]
  const shown = rows.filter((row) => row.value != null && row.average != null && row.average > 0)
  if (shown.length === 0) return null
  return (
    <section className="rise" style={{ '--i': 3 } as CSSProperties}>
      <h2 className="display mb-4 text-2xl font-bold">Gegenüber deinem Schnitt</h2>
      <dl className="grid grid-cols-2 gap-px bg-line sm:grid-cols-4">
        {shown.map((row) => {
          const delta = (row.value! - row.average!) / row.average!
          const up = delta >= 0
          return (
            <div key={row.label} className="bg-bg px-4 py-4">
              <dt className="text-sm text-faint">
                {row.label} · Ø {row.format(row.average!)}
              </dt>
              <dd className={`display mt-1 text-4xl font-bold ${Math.abs(delta) < 0.05 ? 'text-muted' : up ? 'text-win' : 'text-loss'}`}>
                {up ? '+' : '−'}
                {Math.round(Math.abs(delta) * 100)}
                <span className="ml-0.5 text-2xl">%</span>
              </dd>
            </div>
          )
        })}
      </dl>
    </section>
  )
}

function ScoreFigure({ value, className, label }: { value: number; className: string; label: string }) {
  return (
    <div className="text-center">
      <div className={`display text-8xl font-extrabold tabular sm:text-9xl ${className}`}>{value}</div>
      <div className="display mt-1 text-sm font-semibold tracking-[0.14em] text-muted">{label}</div>
    </div>
  )
}
