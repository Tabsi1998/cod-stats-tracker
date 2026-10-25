import type { CSSProperties } from 'react'
import { Link, useParams } from 'react-router'
import { api, type PublicMatch, type Stats, type StatsBreakdown } from '../api'
import { dash, formatAverage, formatCount, formatKd, formatLongDate, formatPercent, formatTime, gameLabels, matchKd, modeStatLabel, outcomeLabels, outcomeOf, outcomeText } from '../format'
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
  const anyValue = tiles.some((tile) => tile.value != null)
  const modeStats = Object.entries(d?.modeStats ?? {})
  const mode = match.mode?.toUpperCase()
  const sameMode = average.state === 'ready' && mode ? average.data.byMode?.find((row) => row.name === mode) : undefined

  return (
    <article className="space-y-12">
      <header className="grid gap-8 lg:grid-cols-[1fr_auto] lg:items-end">
        <div className="rise space-y-4">
          <h1 className={`flare display text-7xl font-extrabold sm:text-9xl ${outcomeText[outcome]}`}>{outcomeLabels[outcome]}</h1>
          <div className="flex flex-wrap items-center gap-3">
            {match.mode && <span className="display bg-text px-3 py-1 text-xl font-bold tracking-[0.14em] text-bg">{match.mode}</span>}
            {match.map && <span className="display text-xl font-bold tracking-[0.14em] text-muted">{match.map}</span>}
            {freeForAll && <span className="display text-xl font-bold tracking-[0.14em] text-you">Platz {match.placement}</span>}
          </div>
          <p className="text-muted">
            {formatLongDate(match.playedAt)} · {formatTime(match.playedAt)} Uhr · {gameLabels[match.game]}
            {playerName && <> · {playerName}</>}
          </p>
          {outcome === 'left' && (
            <p className="max-w-[60ch] text-muted">
              Vor dem Ende verlassen. Das Match zählt nicht für die Siegquote
              {anyValue ? '; die Werte stammen vom letzten Scoreboard und zählen für K/D und Kills.' : '.'}
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

      {anyValue ? (
        <Bracket
          tone={tone}
          className="rise grid grid-cols-2 gap-x-6 gap-y-8 bg-surface/50 px-5 py-8 shadow-[0_24px_48px_-28px_rgb(0_0_0/0.9)] sm:grid-cols-3 sm:px-8 lg:grid-cols-6"
          style={{ '--i': 2 } as CSSProperties}
          label="Werte"
        >
          {tiles.map((tile) => (
            <Figure key={tile.label} value={tile.value} format={tile.format} label={tile.label} size="lg" tone={tile.you && tile.value != null ? 'you' : undefined} />
          ))}
        </Bracket>
      ) : (
        <p className="border-y border-line py-5 text-lg text-muted">
          Keine Werte lesbar: Beim Verlassen konnte der Client keine Zahlen von der Anzeige ablesen.
        </p>
      )}

      {sameMode && sameMode.matches > 1 && match.status === 'completed' && <Comparison match={match} baseline={sameMode} />}

      {d?.lobbySize != null && d.lobbyRank != null && <Lobby size={d.lobbySize} rank={d.lobbyRank} />}

      {(modeStats.length > 0 || d?.xp != null || d?.level != null || d?.weapon != null) && (
        <div className="grid gap-10 lg:grid-cols-[2fr_1fr]">
          <section>
            <h2 className="display mb-4 text-2xl font-bold">Modus-Werte</h2>
            {modeStats.length > 0 ? (
              <dl className="flex flex-wrap gap-x-12 gap-y-6 border-t border-line pt-5">
                {modeStats.map(([key, value]) => (
                  <div key={key} className="flex flex-col-reverse">
                    <dt className="display mt-2 text-sm font-semibold tracking-[0.12em] text-muted">{modeStatLabel(key)}</dt>
                    <dd className="display text-5xl font-bold">{formatCount(value)}</dd>
                  </div>
                ))}
              </dl>
            ) : (
              <p className="border-t border-line pt-5 text-muted">Für dieses Match wurden keine Modus-Werte erfasst.</p>
            )}
          </section>
          <section>
            <h2 className="display mb-4 text-2xl font-bold">Fortschritt</h2>
            <dl className="flex flex-wrap gap-x-12 gap-y-6 border-t border-line pt-5">
              <div className="flex flex-col-reverse">
                <dt className="display mt-2 text-sm font-semibold tracking-[0.12em] text-muted">XP gesamt</dt>
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
              <div className="flex flex-col-reverse">
                <dt className="display mt-2 text-sm font-semibold tracking-[0.12em] text-muted">Level danach</dt>
                <dd className="display text-5xl font-bold">{d?.level ?? dash}</dd>
              </div>
              {d?.weapon != null && (
                <div className="flex flex-col-reverse">
                  <dt className="display mt-2 text-sm font-semibold tracking-[0.12em] text-muted">
                    Top-Waffe{d.weaponClass != null && <span className="text-faint"> · {d.weaponClass}</span>}
                  </dt>
                  <dd className="display text-5xl font-bold">{d.weapon}</dd>
                </div>
              )}
            </dl>
          </section>
        </div>
      )}
    </article>
  )
}

/** This match against the player's average in the same mode: points and kills differ too much between modes. */
function Comparison({ match, baseline }: { match: PublicMatch; baseline: StatsBreakdown }) {
  const rows: { label: string; value: number | null; average: number | null; format: (v: number) => string }[] = [
    { label: 'Kills', value: match.kills, average: baseline.averageKills, format: (v) => formatAverage(v) },
    { label: 'K/D', value: matchKd(match.kills, match.deaths), average: baseline.kdRatio, format: (v) => formatKd(v) },
    { label: 'Punkte', value: match.score, average: baseline.averageScore, format: (v) => formatCount(Math.round(v)) },
  ]
  const shown = rows.filter((row) => row.value != null && row.average != null && row.average > 0)
  if (shown.length === 0) return null
  return (
    <section>
      <h2 className="display mb-4 text-2xl font-bold">
        Gegenüber deinem Schnitt in {baseline.name}{' '}
        <span className="font-sans text-base font-medium tracking-normal text-faint normal-case">({baseline.matches} Matches)</span>
      </h2>
      <dl className="flex flex-wrap gap-x-12 gap-y-6 border-t border-line pt-5">
        {shown.map((row) => {
          const delta = (row.value! - row.average!) / row.average!
          const even = Math.abs(delta) < 0.05
          return (
            <div key={row.label} className="flex flex-col-reverse">
              <dt className="mt-2 text-sm text-faint">
                {row.label}: {row.format(row.value!)} statt im Schnitt {row.format(row.average!)}
              </dt>
              <dd className={`display text-5xl font-bold ${even ? 'text-muted' : delta > 0 ? 'text-win' : 'text-loss'}`}>
                {even ? '±' : delta > 0 ? '+' : '−'}
                {Math.round(Math.abs(delta) * 100)}
                <span className="ml-0.5 text-3xl">%</span>
              </dd>
            </div>
          )
        })}
      </dl>
    </section>
  )
}

/**
 * The player's place on the final scoreboard, both teams together, by points. One cell per player, best left;
 * nobody else is named: only how many ended ahead and behind.
 */
function Lobby({ size, rank }: { size: number; rank: number }) {
  const behind = size - rank
  return (
    <section>
      <h2 className="display mb-4 text-2xl font-bold">In der Lobby</h2>
      <div className="flex flex-wrap items-end gap-x-12 gap-y-6 border-t border-line pt-5">
        <div className="flex flex-col-reverse">
          <p className="display mt-2 text-sm font-semibold tracking-[0.12em] text-muted">Platz nach Punkten</p>
          <p className="display text-5xl font-bold">
            <span className="text-you">{rank}</span>
            <span className="text-3xl text-muted"> von {size}</span>
          </p>
        </div>
        <p className="max-w-[40ch] text-muted">
          {behind === 0
            ? 'Alle anderen hatten mehr Punkte.'
            : `${behind} von ${size - 1} Mitspielern hatten höchstens so viele Punkte wie du (${formatPercent(behind / (size - 1))}).`}
        </p>
      </div>
      <ol className="mt-5 flex gap-1" aria-label={`Platz ${rank} von ${size}, beste links`}>
        {Array.from({ length: size }, (_, i) => (
          <li key={i} className={`h-3 min-w-1 flex-1 ${i + 1 === rank ? 'bg-you' : i + 1 < rank ? 'bg-line' : 'bg-line/40'}`} />
        ))}
      </ol>
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
