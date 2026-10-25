import { useState, type CSSProperties } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, type GameGroup, type Page, type Period, type PublicMatch, type Stats, type StatsBreakdown } from '../api'
import {
  axisLabel,
  dash,
  formatAverage,
  formatCount,
  formatDate,
  formatKd,
  formatPercent,
  formatTime,
  gameLabels,
  matchKd,
  outcomeOf,
} from '../format'
import { useLoad, type Load } from '../load'
import { prefersReducedMotion } from '../motion'
import { Bracket, CommunityNote, Figure, FormStrip, GameSwitch, Icon, Message, OutcomeTag, Segmented, Skeleton, Tabs } from '../ui'

type PeriodKey = 'all' | 'last10' | 'last25' | 'last50' | 'last100' | 'days7' | 'days30'

const periods: Record<PeriodKey, { label: string; period: Period }> = {
  all: { label: 'Gesamt', period: {} },
  last10: { label: 'Letzte 10', period: { last: 10 } },
  last25: { label: 'Letzte 25', period: { last: 25 } },
  last50: { label: 'Letzte 50', period: { last: 50 } },
  last100: { label: 'Letzte 100', period: { last: 100 } },
  days7: { label: '7 Tage', period: { days: 7 } },
  days30: { label: '30 Tage', period: { days: 30 } },
}

type TabKey = 'overview' | 'matches' | 'modes' | 'maps'
const tabKeys: TabKey[] = ['overview', 'matches', 'modes', 'maps']

const count = (value: number | null) => formatCount(value == null ? null : Math.round(value))
const ratio = (value: number | null) => formatKd(value)
const percent = (value: number | null) => formatPercent(value)

export function Player() {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const tab = tabKeys.includes(params.get('tab') as TabKey) ? (params.get('tab') as TabKey) : 'overview'
  const [game, setGame] = useState<GameGroup>('mw4')
  const [periodKey, setPeriodKey] = useState<PeriodKey>('all')
  const profile = useLoad(() => api.player(id), [id])
  const stats = useLoad(() => api.stats(id, game, periods[periodKey].period), [id, game, periodKey])
  const recent = useLoad(() => api.matches(id, game, 0, 20), [id, game])

  if (profile.state === 'loading')
    return (
      <div className="space-y-6 pt-10">
        <Skeleton className="h-16 w-2/3" />
        <Skeleton className="h-64" />
      </div>
    )
  if (profile.state === 'error')
    return <Message>{profile.notFound ? 'Dieses Profil gibt es nicht oder es ist nicht öffentlich.' : 'Das Profil konnte nicht geladen werden.'}</Message>

  const player = profile.data
  const setTab = (next: TabKey) => setParams(next === 'overview' ? {} : { tab: next }, { replace: true })

  return (
    <div className="space-y-8 pt-10">
      <header className="rise space-y-3">
        <h1 className="display text-6xl font-extrabold break-words sm:text-7xl">{player.displayName}</h1>
        <p className="text-muted">
          Dabei seit {formatDate(player.memberSince)} · {formatCount(player.matches)} Matches erfasst · zuletzt {formatDate(player.lastMatchAt)}
        </p>
        {player.previousNames.length > 0 && <p className="text-sm text-faint">Früher: {player.previousNames.join(', ')}</p>}
      </header>

      <div className="rise flex flex-wrap items-center gap-3" style={{ '--i': 1 } as CSSProperties}>
        <GameSwitch value={game} onChange={setGame} />
        <Segmented
          label="Zeitraum"
          value={periodKey}
          onChange={setPeriodKey}
          options={(Object.keys(periods) as PeriodKey[]).map((key) => ({ value: key, label: periods[key].label }))}
        />
      </div>

      {game === 'all' && (
        <p className="bg-you/10 px-4 py-2.5 text-sm">
          Übersicht: MW4 und Warzone zusammengerechnet. Für Vergleiche besser einzeln ansehen.
        </p>
      )}

      <Debrief stats={stats} recent={recent} playerId={id} />

      <Tabs
        label="Ansicht"
        value={tab}
        onChange={setTab}
        options={[
          { value: 'overview', label: 'Übersicht' },
          { value: 'matches', label: 'Matches' },
          { value: 'modes', label: 'Modi', count: stats.state === 'ready' ? (stats.data.byMode?.length ?? 0) : undefined },
          { value: 'maps', label: 'Karten', count: stats.state === 'ready' ? (stats.data.byMap?.length ?? 0) : undefined },
        ]}
      />

      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} key={tab}>
        {tab === 'overview' && (stats.state === 'ready' ? <Overview stats={stats.data} playerId={id} /> : <Skeleton className="h-72" />)}
        {tab === 'matches' && <MatchList playerId={id} game={game} />}
        {tab === 'modes' &&
          (stats.state === 'ready' ? (
            <Breakdown rows={stats.data.byMode ?? []} kind="Modus" empty="Noch keine Matches mit erkanntem Modus." />
          ) : (
            <Skeleton className="h-72" />
          ))}
        {tab === 'maps' &&
          (stats.state === 'ready' ? (
            <Breakdown rows={stats.data.byMap ?? []} kind="Karte" empty="Noch keine Matches mit erkannter Karte. Karten liest der Client ab Version 0.2.0." />
          ) : (
            <Skeleton className="h-72" />
          ))}
      </div>

      <CommunityNote />
    </div>
  )
}

/** The service record at a glance: the four numbers that say how good someone is, and their form. */
function Debrief({ stats, recent, playerId }: { stats: Load<Stats>; recent: Load<Page<PublicMatch>>; playerId: string }) {
  if (stats.state === 'loading') return <Skeleton className="h-72" />
  if (stats.state === 'error') return <Message>Die Statistik konnte nicht geladen werden.</Message>
  const s = stats.data
  if (s.matches === 0) return <Message>In diesem Zeitraum gibt es noch keine Matches.</Message>

  const form = recent.state === 'ready' ? [...recent.data.items].reverse() : []
  return (
    <Bracket
      className="rise grid gap-8 bg-surface/50 px-5 py-7 shadow-[0_24px_48px_-28px_rgb(0_0_0/0.9)] sm:px-8 lg:grid-cols-[1.1fr_1fr]"
      style={{ '--i': 2 } as CSSProperties}
      label="Bilanz"
    >
      <div className="grid grid-cols-2 gap-x-6 gap-y-8">
        <Figure value={s.kdRatio} format={ratio} label="K/D" size="xl" tone="you" hint="Kills gesamt ÷ Tode gesamt" />
        <Figure
          value={s.winRate}
          format={percent}
          label="Siegquote"
          size="xl"
          tone={s.winRate == null ? undefined : s.winRate >= 0.5 ? 'win' : 'loss'}
          hint={`${formatCount(s.wins)} von ${formatCount(s.matchesWithResult)} gewonnen`}
        />
        <Figure value={s.kills} format={count} label="Kills" size="lg" hint={`${formatAverage(s.averageKills)} pro Match`} />
        <Figure
          value={s.matches}
          format={count}
          label="Matches"
          size="lg"
          hint={s.leftMatches > 0 ? `davon ${formatCount(s.leftMatches)} verlassen` : 'keins verlassen'}
        />
      </div>
      <div className="flex flex-col gap-8">
        <div>
          <div className="mb-3 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h2 className="display text-lg font-bold tracking-[0.1em] text-muted">Form · letzte {form.length || ''} Matches</h2>
            <Legend />
          </div>
          {form.length > 0 ? (
            <FormStrip results={form.map((m) => ({ id: m.id, outcome: outcomeOf(m) }))} />
          ) : (
            <Skeleton className="h-16" />
          )}
          <p className="mt-2 text-sm text-faint">Älteste links, neueste rechts.</p>
        </div>
        <dl className="grid grid-cols-3 gap-4 border-t border-line pt-5">
          <MiniStat label="Längste Serie" value={s.longestWinStreak == null ? dash : `${s.longestWinStreak} Siege`} />
          <MiniStat
            label="Beste Runde"
            value={s.bestKills == null ? dash : `${s.bestKills} Kills`}
            to={s.bestMatchId ? `/player/${playerId}/match/${s.bestMatchId}` : undefined}
          />
          <MiniStat label="Verlassen" value={formatPercent(s.leaveRate)} />
        </dl>
      </div>
    </Bracket>
  )
}

function Legend() {
  const items = [
    ['bg-win', 'Sieg'],
    ['bg-loss', 'Niederlage'],
    ['bg-left', 'Verlassen'],
  ] as const
  return (
    <ul className="flex gap-3 text-sm text-faint">
      {items.map(([fill, label]) => (
        <li key={label} className="flex items-center gap-1.5">
          <span className={`size-2.5 ${fill}`} aria-hidden="true" />
          {label}
        </li>
      ))}
    </ul>
  )
}

function MiniStat({ label, value, to }: { label: string; value: string; to?: string }) {
  return (
    <div className="min-w-0">
      <dt className="text-sm text-faint">{label}</dt>
      <dd className="display mt-1 text-2xl font-bold">
        {to ? (
          <Link to={to} className="decoration-you underline-offset-4 hover:text-you hover:underline">
            {value}
          </Link>
        ) : (
          value
        )}
      </dd>
    </div>
  )
}

function Overview({ stats, playerId }: { stats: Stats; playerId: string }) {
  const navigate = useNavigate()
  // The charts draw themselves in, unless the player asked the system for less motion.
  const animate = !prefersReducedMotion()
  const label = axisLabel(stats.series.map((point) => point.playedAt))
  const series = stats.series.map((point) => ({
    id: point.matchId,
    day: label(point.playedAt),
    kd: point.runningKdRatio,
    winRate: point.runningWinRate == null ? null : Math.round(point.runningWinRate * 1000) / 10,
    kills: point.kills,
    result: point.result,
  }))
  const details: [string, string][] = [
    ['Tode', formatCount(stats.deaths)],
    ['Assists', formatCount(stats.assists)],
    ['Eliminierungen', formatCount(stats.eliminations)],
    ['Punkte pro Match', formatCount(stats.averageScore == null ? null : Math.round(stats.averageScore))],
    ['XP pro Match', formatCount(stats.averageXp == null ? null : Math.round(stats.averageXp))],
  ]
  if (stats.averagePlacement != null) details.push(['Platzierung im Schnitt', formatAverage(stats.averagePlacement)])
  if (stats.lobbyMatches > 0) details.push(['Lobby hinter dir', formatPercent(stats.lobbyShare)])

  return (
    <div className="space-y-12">
      <Bracket className="grid gap-8 bg-surface/50 px-5 py-6 shadow-[0_24px_48px_-28px_rgb(0_0_0/0.9)] sm:px-8 lg:grid-cols-[auto_1fr] lg:items-end" label="Weitere Werte">
        <Figure value={stats.totalXp} format={count} label="XP gesamt" size="lg" tone="you" />
        <dl className="grid grid-cols-2 gap-x-8 gap-y-4 sm:grid-cols-3 lg:flex lg:justify-end lg:gap-x-10">
          {details.map(([name, value]) => (
            <div key={name} className="flex flex-col-reverse">
              <dt className="text-sm text-faint">{name}</dt>
              <dd className="display text-3xl font-bold">{value}</dd>
            </div>
          ))}
        </dl>
      </Bracket>

      {series.length > 1 ? (
        <div className="grid gap-10 lg:grid-cols-2">
          <Chart wide title="Kills pro Match" note="Farbe = Ergebnis, Klick öffnet das Match">
            <BarChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid stroke="var(--color-line)" strokeOpacity={0.5} vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-faint)" fontSize={13} tickLine={false} axisLine={false} />
              <YAxis stroke="var(--color-faint)" fontSize={13} width={40} allowDecimals={false} tickLine={false} axisLine={false} />
              <Tooltip {...tooltip} cursor={{ fill: 'var(--color-raised)' }} formatter={(value) => [formatCount(Number(value)), 'Kills']} />
              <Bar
                dataKey="kills"
                animationDuration={900} isAnimationActive={animate}
                cursor="pointer"
                onClick={(entry: { payload?: { id: string } }) => entry.payload && navigate(`/player/${playerId}/match/${entry.payload.id}`)}
              >
                {series.map((point) => (
                  <Cell
                    key={point.id}
                    fill={point.result === 'win' ? 'var(--color-win)' : point.result === 'loss' ? 'var(--color-loss)' : 'var(--color-left)'}
                  />
                ))}
              </Bar>
            </BarChart>
          </Chart>
          <Chart title="K/D-Verlauf" note="laufend, über alle Matches bis dahin">
            <LineChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid stroke="var(--color-line)" strokeOpacity={0.5} vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-faint)" fontSize={13} tickLine={false} axisLine={false} />
              <YAxis stroke="var(--color-faint)" fontSize={13} width={40} tickLine={false} axisLine={false} />
              <Tooltip {...tooltip} formatter={(value) => [formatKd(Number(value)), 'K/D']} />
              <Line type="monotone" dataKey="kd" stroke="var(--color-you)" dot={false} strokeWidth={2.5} animationDuration={900} isAnimationActive={animate} />
            </LineChart>
          </Chart>
          <Chart title="Siegquote-Verlauf" note="in %, verlassene Matches zählen nicht">
            <LineChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid stroke="var(--color-line)" strokeOpacity={0.5} vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-faint)" fontSize={13} tickLine={false} axisLine={false} />
              <YAxis stroke="var(--color-faint)" fontSize={13} width={40} domain={[0, 100]} tickLine={false} axisLine={false} />
              <Tooltip {...tooltip} formatter={(value) => [`${value} %`, 'Siegquote']} />
              <Line type="monotone" dataKey="winRate" stroke="var(--color-win)" dot={false} strokeWidth={2.5} animationDuration={900} isAnimationActive={animate} />
            </LineChart>
          </Chart>
        </div>
      ) : (
        <Message>Verläufe erscheinen ab zwei Matches im gewählten Zeitraum.</Message>
      )}
    </div>
  )
}

const tooltip = {
  contentStyle: { background: 'var(--color-raised)', border: '1px solid var(--color-line)', borderRadius: 0, fontFamily: 'var(--font-sans)' },
  labelStyle: { color: 'var(--color-muted)' },
  itemStyle: { color: 'var(--color-text)' },
}

function Chart({ title, note, children, wide = false }: { title: string; note: string; children: React.ReactElement; wide?: boolean }) {
  return (
    <section className={wide ? 'lg:col-span-2' : ''}>
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-x-4">
        <h2 className="display text-2xl font-bold">{title}</h2>
        <p className="text-sm text-faint">{note}</p>
      </div>
      <div className="h-64">
        <ResponsiveContainer width="100%" height="100%">
          {children}
        </ResponsiveContainer>
      </div>
    </section>
  )
}

/** Every match, newest first, each row opening its debrief. Loads in pages of 25. */
function MatchList({ playerId, game }: { playerId: string; game: GameGroup }) {
  const [pages, setPages] = useState(1)
  const list = useLoad(() => api.matches(playerId, game, 0, 25 * pages), [playerId, game, pages])
  const navigate = useNavigate()

  if (list.state === 'loading' && pages === 1) return <Skeleton className="h-96" />
  if (list.state === 'error') return <Message>Die Matches konnten nicht geladen werden.</Message>
  const page = list.state === 'ready' ? list.data : null
  if (page && page.items.length === 0) return <Message>Noch keine Matches.</Message>

  return (
    <div className="space-y-4">
      {/* Phones: one row per match with what matters most, result and K/D, always in view. */}
      <ul className="divide-y divide-line/60 border-y border-line/60 sm:hidden">
        {page?.items.map((match) => {
          const d = match.details
          return (
            <li key={match.id}>
              <Link to={`/player/${playerId}/match/${match.id}`} className="flex items-center gap-3 py-3 active:bg-surface">
                <span className="min-w-0 flex-1">
                  <span className="flex items-center gap-2">
                    <span className="display truncate text-lg font-bold">{match.mode ?? gameLabels[match.game]}</span>
                    <OutcomeTag outcome={outcomeOf(match)} className="shrink-0 text-xs" />
                  </span>
                  <span className="block truncate text-sm text-faint">
                    {formatDate(match.playedAt)}
                    {match.map && ` · ${match.map}`}
                    {d?.teamScore != null && d.enemyScore != null && ` · ${d.teamScore}:${d.enemyScore}`}
                  </span>
                </span>
                <span className="shrink-0 text-right">
                  <span className="display block text-2xl font-bold">{formatKd(matchKd(match.kills, match.deaths))}</span>
                  <span className="text-sm text-faint">
                    {formatCount(match.kills)}/{formatCount(match.deaths)}
                  </span>
                </span>
                <Icon name="next" className="size-4 shrink-0 text-faint" />
              </Link>
            </li>
          )
        })}
      </ul>
      <div className="hidden overflow-x-auto sm:block">
        <table className="w-full min-w-[44rem] text-left">
          <thead>
            <tr className="display border-b border-line text-sm tracking-[0.12em] text-faint">
              <th className="py-2 pr-4 font-semibold">Wann</th>
              <th className="py-2 pr-4 font-semibold">Modus · Karte</th>
              <th className="py-2 pr-4 font-semibold">Ergebnis</th>
              <th className="py-2 pr-4 text-right font-semibold">Endstand</th>
              <th className="py-2 pr-4 text-right font-semibold">K</th>
              <th className="py-2 pr-4 text-right font-semibold">T</th>
              <th className="py-2 pr-4 text-right font-semibold">K/D</th>
              <th className="py-2 pr-4 text-right font-semibold">Punkte</th>
              <th className="py-2 text-right font-semibold">XP</th>
            </tr>
          </thead>
          <tbody>
            {page?.items.map((match) => {
              const href = `/player/${playerId}/match/${match.id}`
              const d = match.details
              return (
                <tr
                  key={match.id}
                  onClick={() => navigate(href)}
                  className="group cursor-pointer border-b border-line/60 transition-colors duration-200 hover:bg-surface"
                >
                  <td className="py-3 pr-4 whitespace-nowrap">
                    <Link to={href} className="outline-none focus-visible:underline">
                      {formatDate(match.playedAt).replace(/,? \d\d:\d\d$/, '')}
                    </Link>
                    <span className="ml-2 text-faint">{formatTime(match.playedAt)}</span>
                  </td>
                  <td className="py-3 pr-4 whitespace-nowrap">
                    <span className="display text-lg font-bold">{match.mode ?? gameLabels[match.game]}</span>
                    {match.map && <span className="ml-2 text-muted">{match.map}</span>}
                  </td>
                  <td className="py-3 pr-4">
                    <OutcomeTag outcome={outcomeOf(match)} />
                    {match.placement != null && <span className="ml-2 text-muted">Platz {match.placement}</span>}
                  </td>
                  <td className="display py-3 pr-4 text-right text-lg font-bold whitespace-nowrap">
                    {d?.teamScore != null && d.enemyScore != null ? (
                      <>
                        <span className="text-win">{d.teamScore}</span>
                        <span className="px-1 text-faint">:</span>
                        <span className="text-loss">{d.enemyScore}</span>
                      </>
                    ) : (
                      <span className="text-faint">{dash}</span>
                    )}
                  </td>
                  <td className="py-3 pr-4 text-right text-lg">{formatCount(match.kills)}</td>
                  <td className="py-3 pr-4 text-right text-lg">{formatCount(match.deaths)}</td>
                  <td className="py-3 pr-4 text-right text-lg font-semibold">{formatKd(matchKd(match.kills, match.deaths))}</td>
                  <td className="py-3 pr-4 text-right text-muted">{formatCount(match.score)}</td>
                  <td className="py-3 text-right text-muted">
                    <span className="inline-flex items-center gap-2">
                      {formatCount(d?.xp)}
                      <Icon name="next" className="size-4 text-faint transition-transform duration-200 group-hover:translate-x-0.5 group-hover:text-you" />
                    </span>
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {page && page.items.length < page.total && (
        <button
          type="button"
          onClick={() => setPages(pages + 1)}
          disabled={list.state === 'loading'}
          className="display w-full border border-line py-3 text-lg font-bold tracking-[0.1em] text-muted transition-colors duration-200 hover:border-you hover:text-text disabled:opacity-50"
        >
          {list.state === 'loading' ? 'Lädt …' : `Weitere Matches (${formatCount(page.total - page.items.length)} übrig)`}
        </button>
      )}
    </div>
  )
}

type SortKey = 'matches' | 'winRate' | 'kdRatio' | 'averageKills' | 'averageScore'

/** Strengths per mode or map: sortable, the win rate drawn as a bar. */
function Breakdown({ rows, kind, empty }: { rows: StatsBreakdown[]; kind: string; empty: string }) {
  const [sort, setSort] = useState<SortKey>('matches')
  if (rows.length === 0) return <Message>{empty}</Message>
  const sorted = [...rows].sort((a, b) => (b[sort] ?? -1) - (a[sort] ?? -1) || a.name.localeCompare(b.name))
  const columns: { key: SortKey; label: string }[] = [
    { key: 'matches', label: 'Matches' },
    { key: 'winRate', label: 'Siegquote' },
    { key: 'kdRatio', label: 'K/D' },
    { key: 'averageKills', label: 'Kills/Match' },
    { key: 'averageScore', label: 'Punkte/Match' },
  ]
  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[40rem] text-left">
        <thead>
          <tr className="display border-b border-line text-sm tracking-[0.12em] text-faint">
            <th className="py-2 pr-4 font-semibold">{kind}</th>
            {columns.map((column) => (
              <th key={column.key} className="py-2 pr-4 text-right font-semibold last:pr-0" aria-sort={sort === column.key ? 'descending' : 'none'}>
                <button
                  type="button"
                  onClick={() => setSort(column.key)}
                  className={`tracking-[0.12em] uppercase transition-colors duration-200 hover:text-text ${sort === column.key ? 'text-you' : ''}`}
                >
                  {column.label}
                  {sort === column.key && <Icon name="down" className="ml-1 inline size-3.5 align-[-2px]" />}
                </button>
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {sorted.map((row, i) => (
            <tr key={row.name} className="border-b border-line/60">
              <td className="display py-3 pr-4 text-xl font-bold">{row.name}</td>
              <td className="py-3 pr-4 text-right text-lg">{formatCount(row.matches)}</td>
              <td className="py-3 pr-4 text-right">
                <span className="inline-flex items-center justify-end gap-3">
                  <span aria-hidden="true" className={`hidden h-1.5 w-24 sm:block ${row.winRate == null ? '' : 'bg-loss/40'}`}>
                    {row.winRate != null && (
                      <span className="bar-in block h-full bg-win" style={{ width: `${row.winRate * 100}%`, '--i': i } as CSSProperties} />
                    )}
                  </span>
                  <span className="w-16 text-lg">{formatPercent(row.winRate)}</span>
                </span>
              </td>
              <td className="py-3 pr-4 text-right text-lg font-semibold">{formatKd(row.kdRatio)}</td>
              <td className="py-3 pr-4 text-right text-lg">{formatAverage(row.averageKills)}</td>
              <td className="py-3 text-right text-lg text-muted">{formatCount(row.averageScore == null ? null : Math.round(row.averageScore))}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
