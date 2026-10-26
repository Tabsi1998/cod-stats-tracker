import type { CSSProperties } from 'react'
import { Link } from 'react-router'
import type { MapModeCell, Stats, StatsBreakdown, StatsPoint, StatsRecord } from '../api'
import { dayparts, lobbyPlaces, pointOutcome, sessions, tallyKd, tallyWinRate, weekdays, whenPlayed, type Tally } from '../analysis'
import { dash, formatAverage, formatCount, formatDate, formatDay, formatKd, formatPercent, formatTime, modeStatLabel, outcomeFill } from '../format'
import { Bracket, Figure, Icon, Message } from '../ui'

/** A win-rate bar as the breakdowns draw it: cyan share over an orange track, growing once from the left. */
export function RateBar({ rate, index, className = 'w-24' }: { rate: number | null; index: number; className?: string }) {
  return (
    <span aria-hidden="true" className={`block h-1.5 ${className} ${rate == null ? 'bg-line/40' : 'bg-loss/40'}`}>
      {rate != null && <span className="bar-in block h-full bg-win" style={{ width: `${rate * 100}%`, '--i': index } as CSSProperties} />}
    </span>
  )
}

function SectionTitle({ title, note }: { title: string; note?: string }) {
  return (
    <div className="mb-4 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
      <h2 className="display text-2xl font-bold">{title}</h2>
      {note && <p className="text-sm text-faint">{note}</p>}
    </div>
  )
}

/** Rows of a time bucket: how many matches, K/D and win rate. Empty buckets stay in place with dashes. */
function TallyRows({ rows }: { rows: { label: string; hint?: string; tally: Tally }[] }) {
  const most = Math.max(...rows.map((row) => row.tally.matches), 1)
  return (
    <table className="w-full text-left">
      <thead>
        <tr className="display border-b border-line text-sm tracking-[0.12em] text-faint">
          <th className="py-2 pr-3 font-semibold" />
          <th className="py-2 pr-3 text-right font-semibold">Matches</th>
          <th className="py-2 pr-3 text-right font-semibold">K/D</th>
          <th className="py-2 text-right font-semibold">Siegquote</th>
        </tr>
      </thead>
      <tbody>
        {rows.map((row, i) => {
          const best = row.tally.matches === most && most > 1
          return (
            <tr key={row.label} className={`border-b border-line/60 ${row.tally.matches === 0 ? 'text-faint' : ''}`}>
              <td className="py-2.5 pr-3">
                <span className="display text-lg font-bold">{row.label}</span>
                {row.hint && <span className="ml-2 text-sm text-faint">{row.hint}</span>}
              </td>
              <td className={`py-2.5 pr-3 text-right ${best ? 'text-you' : ''}`}>{row.tally.matches ? formatCount(row.tally.matches) : dash}</td>
              <td className="py-2.5 pr-3 text-right font-semibold">{formatKd(tallyKd(row.tally))}</td>
              <td className="py-2.5 text-right">
                <span className="inline-flex items-center justify-end gap-3">
                  <RateBar rate={tallyWinRate(row.tally)} index={i} className="hidden w-16 sm:block" />
                  <span className="w-14">{formatPercent(tallyWinRate(row.tally))}</span>
                </span>
              </td>
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

/** When the player plays and how it goes then: weekday and part of the day, in the viewer's time zone. */
export function WhenPlayed({ series }: { series: StatsPoint[] }) {
  if (series.length < 3) return null
  const when = whenPlayed(series)
  return (
    <section>
      <SectionTitle title="Wann du spielst" note="Wochentag und Tageszeit, in deiner Zeitzone" />
      <div className="grid gap-8 lg:grid-cols-2">
        <TallyRows rows={weekdays.map((label, i) => ({ label, tally: when.weekdays[i] }))} />
        <TallyRows rows={dayparts.map((part, i) => ({ label: part.label, hint: part.hint, tally: when.dayparts[i] }))} />
      </div>
    </section>
  )
}

/** Where the player ends in the lobby by score: first, top three, upper and lower half, as one bar. */
export function LobbyPlaces({ series }: { series: StatsPoint[] }) {
  const places = lobbyPlaces(series)
  if (places.total === 0) return null
  const parts = [
    { key: 'first', label: 'Platz 1', value: places.first, fill: 'bg-you' },
    { key: 'podium', label: 'Platz 2–3', value: places.podium, fill: 'bg-win' },
    { key: 'upper', label: 'Obere Hälfte', value: places.upper, fill: 'bg-win/45' },
    { key: 'lower', label: 'Untere Hälfte', value: places.lower, fill: 'bg-loss/60' },
  ]
  return (
    <section>
      <SectionTitle title="In der Lobby" note={`Platz nach Punkten auf dem End-Scoreboard, ${formatCount(places.total)} Matches`} />
      <div className="flex h-4 w-full overflow-hidden bg-line/40" aria-hidden="true">
        {parts.map((part, i) =>
          part.value > 0 ? (
            <span key={part.key} className={`bar-in block h-full ${part.fill}`} style={{ width: `${(part.value / places.total) * 100}%`, '--i': i } as CSSProperties} />
          ) : null,
        )}
      </div>
      <dl className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-4">
        {parts.map((part) => (
          <div key={part.key} className="flex flex-col-reverse">
            <dt className="flex items-center gap-2 text-sm text-faint">
              <span className={`size-2.5 ${part.fill}`} aria-hidden="true" />
              {part.label}
            </dt>
            <dd className="display text-3xl font-bold">
              {formatPercent(part.value / places.total)}
              <span className="ml-2 text-base font-semibold text-faint">{formatCount(part.value)}×</span>
            </dd>
          </div>
        ))}
      </dl>
    </section>
  )
}

/** Mode values per mode: average per match where the mode has the column, best match and total. */
export function ModeValues({ modes }: { modes: StatsBreakdown[] }) {
  const withValues = modes.filter((mode) => (mode.modeStats?.length ?? 0) > 0)
  if (withValues.length === 0)
    return <Message>Noch keine Modus-Werte. Bestätigt, Zielzeit, Erobert und Co. liest der Client aus der Endtabelle und dem Einsatzbericht.</Message>
  return (
    <section>
      <SectionTitle title="Modus-Werte" note="Ø pro Match, in dem der Modus den Wert zeigt" />
      <div className="divide-y divide-line/60 border-y border-line/60">
        {withValues.map((mode) => (
          <div key={mode.name} className="grid gap-4 py-5 lg:grid-cols-[14rem_1fr] lg:items-baseline">
            <div>
              <h3 className="display text-xl font-bold">{mode.name}</h3>
              <p className="text-sm text-faint">{mode.matches === 1 ? '1 Match' : `${formatCount(mode.matches)} Matches`}</p>
            </div>
            <dl className="flex flex-wrap gap-x-10 gap-y-4">
              {mode.modeStats!.map((stat) => (
                <div key={stat.key} className="flex flex-col-reverse">
                  <dt className="display mt-1 text-sm font-semibold tracking-[0.12em] text-muted">
                    {modeStatLabel(stat.key)} <span className="font-sans font-normal tracking-normal text-faint normal-case">Ø</span>
                  </dt>
                  <dd className="display text-4xl font-bold">
                    {formatAverage(stat.average)}
                    <span className="ml-3 font-sans text-sm font-normal text-faint">
                      Bestwert {formatCount(stat.best)} · gesamt {formatCount(stat.total)}
                    </span>
                  </dd>
                </div>
              ))}
            </dl>
          </div>
        ))}
      </div>
    </section>
  )
}

/**
 * Every map against every mode: the win rate in each cell played, with the number of matches. A cell opens that
 * combination as a filter.
 */
export function MapModeMatrix({ cells, onPick }: { cells: MapModeCell[]; onPick: (map: string, mode: string) => void }) {
  if (cells.length === 0) return null
  const maps = [...new Set(cells.map((c) => c.map))]
  const modes = [...new Set(cells.map((c) => c.mode))]
  const at = new Map(cells.map((c) => [`${c.map}|${c.mode}`, c]))
  return (
    <section>
      <SectionTitle title="Karte × Modus" note="Siegquote und Matches je Kombination, Klick filtert" />
      <div className="overflow-x-auto">
        <table className="w-full min-w-[36rem] text-left">
          <thead>
            <tr className="display border-b border-line text-sm tracking-[0.12em] text-faint">
              <th className="py-2 pr-3 font-semibold">Karte</th>
              {modes.map((mode) => (
                <th key={mode} className="max-w-[9rem] py-2 pr-3 align-bottom font-semibold">
                  {mode}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {maps.map((map, row) => (
              <tr key={map} className="border-b border-line/60">
                <td className="display py-2 pr-3 text-lg font-bold whitespace-nowrap">{map}</td>
                {modes.map((mode, column) => {
                  const cell = at.get(`${map}|${mode}`)
                  if (!cell) return <td key={mode} className="py-2 pr-3 text-faint">{dash}</td>
                  return (
                    <td key={mode} className="py-1 pr-3">
                      <button
                        type="button"
                        onClick={() => onPick(map, mode)}
                        title={`${map} · ${mode} filtern`}
                        className="group w-full min-w-[5.5rem] px-2 py-1.5 text-left transition-colors duration-200 hover:bg-surface"
                      >
                        <span className="flex items-baseline justify-between gap-2">
                          <span className="display text-lg font-bold group-hover:text-you">{formatPercent(cell.winRate)}</span>
                          <span className="text-sm text-faint">{formatCount(cell.matches)}×</span>
                        </span>
                        <RateBar rate={cell.winRate} index={row + column} className="mt-1 w-full" />
                      </button>
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

const clock = (iso: string) => formatTime(iso)

/** Gaming sessions, newest first: each with its result row, K/D and best match. */
export function Sessions({ series, playerId }: { series: StatsPoint[]; playerId: string }) {
  const found = sessions(series)
  if (found.length === 0) return <Message>Keine Matches in diesem Zeitraum.</Message>
  return (
    <div className="space-y-4">
      <p className="max-w-[65ch] text-sm text-faint">Eine Session sind Matches mit höchstens 45 Minuten Pause dazwischen.</p>
      <ol className="divide-y divide-line/60 border-y border-line/60">
        {found.map((session, i) => {
          const minutes = Math.round((new Date(session.end).getTime() - new Date(session.start).getTime()) / 60_000)
          const kd = tallyKd(session)
          return (
            <li key={session.start} className="rise grid gap-4 py-5 lg:grid-cols-[13rem_1fr_auto] lg:items-center" style={{ '--i': Math.min(i, 10) } as CSSProperties}>
              <div>
                <p className="display text-xl font-bold">{formatDate(session.start).replace(/,? \d\d:\d\d$/, '')}</p>
                <p className="text-sm text-faint">
                  {clock(session.start)}–{clock(session.end)}
                  {minutes > 0 && ` · ${minutes >= 60 ? `${Math.floor(minutes / 60)} h ${minutes % 60} min` : `${minutes} min`}`}
                </p>
              </div>
              <div className="min-w-0">
                <div className="flex flex-wrap gap-1" aria-label={`${session.matches} Matches`}>
                  {session.points.map((point) => (
                    <Link
                      key={point.matchId}
                      to={`/player/${playerId}/match/${point.matchId}`}
                      title={`${point.mode ?? 'Match'} · ${formatTime(point.playedAt)}`}
                      className={`block size-5 transition-transform duration-200 hover:scale-110 ${outcomeFill[pointOutcome(point)]}`}
                    />
                  ))}
                </div>
                <p className="mt-2 text-sm text-faint">
                  {formatCount(session.wins)} Siege aus {formatCount(session.withResult)}
                  {session.levelUps > 0 && ` · +${session.levelUps} Level`}
                  {session.xp > 0 && ` · ${formatCount(session.xp)} XP`}
                </p>
              </div>
              <dl className="flex gap-8 lg:justify-end">
                <div className="flex flex-col-reverse">
                  <dt className="display text-sm font-semibold tracking-[0.12em] text-muted">K/D</dt>
                  <dd className={`display text-4xl font-bold ${kd != null && kd >= 1 ? 'text-you' : ''}`}>{formatKd(kd)}</dd>
                </div>
                <div className="flex flex-col-reverse">
                  <dt className="display text-sm font-semibold tracking-[0.12em] text-muted">Matches</dt>
                  <dd className="display text-4xl font-bold">{formatCount(session.matches)}</dd>
                </div>
                {session.bestMatchId && (
                  <div className="flex flex-col-reverse">
                    <dt className="display text-sm font-semibold tracking-[0.12em] text-muted">Bestes Match</dt>
                    <dd className="display text-4xl font-bold">
                      <Link to={`/player/${playerId}/match/${session.bestMatchId}`} className="decoration-you underline-offset-4 hover:text-you hover:underline">
                        {formatCount(session.bestKills)}
                        <span className="ml-1 text-xl text-faint">K</span>
                      </Link>
                    </dd>
                  </div>
                )}
              </dl>
            </li>
          )
        })}
      </ol>
    </div>
  )
}

const recordLabels: Record<string, string> = {
  kills: 'Meiste Kills',
  kd: 'Beste K/D',
  score: 'Meiste Punkte',
  eliminations: 'Meiste Eliminierungen',
  assists: 'Meiste Assists',
  xp: 'Meiste XP',
}
const recordOrder = ['kills', 'kd', 'score', 'eliminations', 'assists', 'xp']

/** The best single match per value, each opening its match. */
export function Records({ stats, playerId }: { stats: Stats; playerId: string }) {
  const records = [...(stats.records ?? [])].sort(
    (a, b) => (recordOrder.indexOf(a.kind) + 1 || 99) - (recordOrder.indexOf(b.kind) + 1 || 99) || a.kind.localeCompare(b.kind),
  )
  if (records.length === 0) return <Message>Noch keine Rekorde in diesem Zeitraum.</Message>
  const [main, ...rest] = records
  return (
    <div className="space-y-10">
      <div className="grid gap-8 lg:grid-cols-[auto_1fr]">
        <Bracket tone="you" className="bg-surface/50 px-6 py-6 shadow-[0_24px_48px_-28px_rgb(0_0_0/0.9)] sm:px-8" label={recordLabel(main)}>
          <RecordFigure record={main} playerId={playerId} size="xl" />
        </Bracket>
        <p className="max-w-[55ch] self-end text-muted">
          Bestwerte aus einem einzelnen Match{stats.longestWinStreak != null && `, dazu die längste Siegesserie: ${stats.longestWinStreak}`}. Bei
          Gleichstand zählt das neuere Match.
        </p>
      </div>
      <div className="grid grid-cols-2 gap-x-6 gap-y-10 border-t border-line pt-6 sm:grid-cols-3 lg:grid-cols-4">
        {rest.map((record) => (
          <RecordFigure key={record.kind} record={record} playerId={playerId} size="md" />
        ))}
      </div>
    </div>
  )
}

function recordLabel(record: StatsRecord): string {
  return recordLabels[record.kind] ?? `Rekord ${modeStatLabel(record.kind.replace(/^stat\./, ''))}`
}

function RecordFigure({ record, playerId, size }: { record: StatsRecord; playerId: string; size: 'xl' | 'md' }) {
  const format = record.kind === 'kd' ? formatKd : (value: number | null) => formatCount(value == null ? null : Math.round(value))
  return (
    <Link to={`/player/${playerId}/match/${record.matchId}`} className="group block">
      <Figure
        value={record.value}
        format={format}
        label={recordLabel(record)}
        size={size}
        tone={size === 'xl' ? 'you' : undefined}
        hint={
          <span className="group-hover:text-you">
            {[record.mode, record.map, formatDay(record.playedAt)].filter(Boolean).join(' · ')}
            <Icon name="next" className="ml-1 inline size-3.5 align-[-2px] transition-transform duration-200 group-hover:translate-x-0.5" />
          </span>
        }
      />
    </Link>
  )
}
