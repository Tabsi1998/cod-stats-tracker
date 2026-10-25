import { useState, type CSSProperties } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router'
import { Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { api, type FilterQuery, type Stats, type StatsBreakdown } from '../api'
import { formCurve, pointOutcome } from '../analysis'
import { activeFilters, periodLabels, previousLabels, previousQuery, readFilters, toQuery, writeFilters, type Filters } from '../filters'
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
import { Bracket, CommunityNote, Delta, Figure, FormStrip, Icon, Message, OutcomeTag, Skeleton, Tabs } from '../ui'
import { LobbyPlaces, MapModeMatrix, ModeValues, RateBar, Records, Sessions, WhenPlayed } from './Analyses'
import { FilterBar } from './FilterBar'

type TabKey = 'overview' | 'matches' | 'modes' | 'maps' | 'weapons' | 'sessions' | 'records'
const tabKeys: TabKey[] = ['overview', 'matches', 'modes', 'maps', 'weapons', 'sessions', 'records']

const count = (value: number | null) => formatCount(value == null ? null : Math.round(value))
const ratio = (value: number | null) => formatKd(value)
const percent = (value: number | null) => formatPercent(value)

export function Player() {
  const { id = '' } = useParams()
  const [params, setParams] = useSearchParams()
  const tab = tabKeys.includes(params.get('tab') as TabKey) ? (params.get('tab') as TabKey) : 'overview'
  const filters = readFilters(params)
  // One moment per filter choice, so "today" and "7 days" do not shift while the page is open.
  const [now] = useState(() => new Date())
  const query = toQuery(filters, now)
  const before = previousQuery(filters, now)
  const key = JSON.stringify(query)
  const profile = useLoad(() => api.player(id), [id])
  const facets = useLoad(() => api.facets(id, filters.game), [id, filters.game])
  const stats = useLoad(() => api.stats(id, query), [id, key])
  const previous = useLoad(() => (before ? api.stats(id, before) : Promise.resolve(null)), [id, JSON.stringify(before)])

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
  const setFilters = (next: Filters) => setParams(writeFilters(params, next), { replace: true })
  const setTab = (next: TabKey) => {
    const nextParams = new URLSearchParams(params)
    if (next === 'overview') nextParams.delete('tab')
    else nextParams.set('tab', next)
    setParams(nextParams, { replace: true })
  }
  /** Narrow to what was clicked in a breakdown, and show the overview of it. */
  const drill = (patch: Partial<Filters>) => {
    const nextParams = writeFilters(params, { ...filters, ...patch })
    nextParams.delete('tab')
    setParams(nextParams)
    window.scrollTo({ top: 0, behavior: prefersReducedMotion() ? 'auto' : 'smooth' })
  }
  const ready = stats.state === 'ready' ? stats.data : null
  const narrowed = activeFilters(filters) > 0 || filters.period !== 'all'

  return (
    <div className="space-y-8 pt-10">
      <header className="rise space-y-3">
        <h1 className="display text-6xl font-extrabold break-words sm:text-7xl">{player.displayName}</h1>
        <p className="text-muted">
          Dabei seit {formatDate(player.memberSince)} · {formatCount(player.matches)} Matches erfasst · zuletzt {formatDate(player.lastMatchAt)}
        </p>
        {player.previousNames.length > 0 && <p className="text-sm text-faint">Früher: {player.previousNames.join(', ')}</p>}
      </header>

      <FilterBar filters={filters} facets={facets} onChange={setFilters} />

      {filters.game === 'all' && (
        <p className="bg-you/10 px-4 py-2.5 text-sm">
          Übersicht: MW4 und Warzone zusammengerechnet. Für Vergleiche besser einzeln ansehen.
        </p>
      )}

      <Debrief
        stats={stats}
        previous={previous.state === 'ready' ? previous.data : null}
        against={previousLabels[filters.period]}
        playerId={id}
        narrowed={narrowed}
        periodLabel={periodLabels[filters.period]}
      />

      <Tabs
        label="Ansicht"
        value={tab}
        onChange={setTab}
        options={[
          { value: 'overview', label: 'Übersicht' },
          { value: 'matches', label: 'Matches', count: ready?.matches },
          { value: 'modes', label: 'Modi', count: ready ? (ready.byMode?.length ?? 0) : undefined },
          { value: 'maps', label: 'Karten', count: ready ? (ready.byMap?.length ?? 0) : undefined },
          { value: 'weapons', label: 'Waffen', count: ready ? (ready.byWeapon?.length ?? 0) : undefined },
          { value: 'sessions', label: 'Sessions' },
          { value: 'records', label: 'Rekorde' },
        ]}
      />

      <div role="tabpanel" id={`panel-${tab}`} aria-labelledby={`tab-${tab}`} key={tab}>
        {tab === 'matches' ? (
          <MatchList playerId={id} query={query} />
        ) : !ready ? (
          stats.state === 'error' ? null : <Skeleton className="h-72" />
        ) : (
          <>
            {tab === 'overview' && <Overview stats={ready} playerId={id} />}
            {tab === 'modes' && (
              <div className="space-y-12">
                <Breakdown rows={ready.byMode ?? []} kind="Modus" empty="Noch keine Matches mit erkanntem Modus." onPick={(mode) => drill({ mode })} />
                <ModeValues modes={ready.byMode ?? []} />
              </div>
            )}
            {tab === 'maps' && (
              <div className="space-y-12">
                <Breakdown
                  rows={ready.byMap ?? []}
                  kind="Karte"
                  empty="Noch keine Matches mit erkannter Karte. Karten liest der Client ab Version 0.2.0."
                  onPick={(map) => drill({ map })}
                />
                <MapModeMatrix cells={ready.byMapMode ?? []} onPick={(map, mode) => drill({ map, mode })} />
              </div>
            )}
            {tab === 'weapons' && (
              <div className="space-y-12">
                <Breakdown
                  rows={ready.byWeapon ?? []}
                  kind="Top-Waffe"
                  empty="Noch keine Matches mit erkannter Top-Waffe. Der Client liest sie aus dem Einsatzbericht ab der nächsten Version."
                  onPick={(weapon) => drill({ weapon })}
                />
                {(ready.byWeaponClass?.length ?? 0) > 0 && (
                  <Breakdown rows={ready.byWeaponClass ?? []} kind="Waffenklasse" empty="" onPick={(weaponClass) => drill({ weaponClass })} />
                )}
                <p className="max-w-[65ch] text-sm text-faint">
                  Gezählt wird die Top-Waffe, die der Einsatzbericht unter „Topleistung“ zeigt: die Waffe, mit der du in diesem Match am
                  meisten erreicht hast. Abschüsse pro Waffe folgen.
                </p>
              </div>
            )}
            {tab === 'sessions' && <Sessions series={ready.series} playerId={id} />}
            {tab === 'records' && <Records stats={ready} playerId={id} />}
          </>
        )}
      </div>

      <CommunityNote />
    </div>
  )
}

/** The service record at a glance: the four numbers that say how good someone is, and their form. */
function Debrief({
  stats,
  previous,
  against,
  playerId,
  narrowed,
  periodLabel,
}: {
  stats: Load<Stats>
  previous: Stats | null
  against: string | undefined
  playerId: string
  narrowed: boolean
  periodLabel: string
}) {
  if (stats.state === 'loading') return <Skeleton className="h-72" />
  if (stats.state === 'error') return <Message>Die Statistik konnte nicht geladen werden.</Message>
  const s = stats.data
  if (s.matches === 0)
    return <Message>{narrowed ? 'Mit diesen Filtern gibt es keine Matches. Filter lockern oder einen anderen Zeitraum wählen.' : 'Noch keine Matches.'}</Message>

  const form = s.series.slice(-20)
  // Only compare with a period before that had matches; an empty one would turn every value into a jump.
  const base = previous && previous.matches > 0 ? previous : null
  const delta = (now: number | null, then: number | null | undefined, format: (value: number) => string) =>
    base && against ? <Delta now={now} before={then} format={format} against={against} /> : null
  return (
    <Bracket
      className="rise grid gap-8 bg-surface/50 px-5 py-7 shadow-[0_24px_48px_-28px_rgb(0_0_0/0.9)] sm:px-8 lg:grid-cols-[1.1fr_1fr]"
      style={{ '--i': 2 } as CSSProperties}
      label="Bilanz"
    >
      <div className="grid grid-cols-2 gap-x-6 gap-y-8">
        <Figure
          value={s.kdRatio}
          format={ratio}
          label="K/D"
          size="xl"
          tone="you"
          hint={delta(s.kdRatio, base?.kdRatio, (v) => formatKd(v)) ?? 'Kills gesamt ÷ Tode gesamt'}
        />
        <Figure
          value={s.winRate}
          format={percent}
          label="Siegquote"
          size="xl"
          tone={s.winRate == null ? undefined : s.winRate >= 0.5 ? 'win' : 'loss'}
          hint={
            delta(s.winRate, base?.winRate, (v) => `${formatAverage(v * 100)} Pkt.`) ?? `${formatCount(s.wins)} von ${formatCount(s.matchesWithResult)} gewonnen`
          }
        />
        <Figure
          value={s.kills}
          format={count}
          label="Kills"
          size="lg"
          hint={delta(s.averageKills, base?.averageKills, (v) => `${formatAverage(v)} pro Match`) ?? `${formatAverage(s.averageKills)} pro Match`}
        />
        <Figure
          value={s.matches}
          format={count}
          label="Matches"
          size="lg"
          hint={s.leftMatches > 0 ? `davon ${formatCount(s.leftMatches)} verlassen` : narrowed ? periodLabel : 'keins verlassen'}
        />
      </div>
      <div className="flex flex-col gap-8">
        <div>
          <div className="mb-3 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h2 className="display text-lg font-bold tracking-[0.1em] text-muted">Form · letzte {form.length} Matches</h2>
            <Legend />
          </div>
          <FormStrip results={form.map((point) => ({ id: point.matchId, outcome: pointOutcome(point) }))} />
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
  const form = formCurve(stats.series)
  const series = stats.series.map((point, i) => ({
    id: point.matchId,
    day: label(point.playedAt),
    kd: point.runningKdRatio,
    form: form[i],
    winRate: point.runningWinRate == null ? null : Math.round(point.runningWinRate * 1000) / 10,
    kills: point.kills,
    result: pointOutcome(point),
    level: point.level ?? null,
  }))
  const levels = series.filter((point) => point.level != null)
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
          <Chart title="K/D und Form" note="gelb: gesamt bis dahin · weiß: letzte 10 Matches">
            <LineChart data={series} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
              <CartesianGrid stroke="var(--color-line)" strokeOpacity={0.5} vertical={false} />
              <XAxis dataKey="day" stroke="var(--color-faint)" fontSize={13} tickLine={false} axisLine={false} />
              <YAxis stroke="var(--color-faint)" fontSize={13} width={40} tickLine={false} axisLine={false} />
              <Tooltip
                {...tooltip}
                formatter={(value, name) => [formatKd(Number(value)), name === 'form' ? 'Form (10 Matches)' : 'K/D gesamt']}
              />
              <Line type="monotone" dataKey="form" stroke="var(--color-text)" strokeOpacity={0.7} dot={false} strokeWidth={1.5} animationDuration={900} isAnimationActive={animate} />
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

      {levels.length > 2 && (
        <Chart
          title="Fortschritt"
          note={`Level ${levels[0].level} → ${levels[levels.length - 1].level}${
            levels[levels.length - 1].level! > levels[0].level! ? ` · +${levels[levels.length - 1].level! - levels[0].level!}` : ''
          } · ${formatCount(stats.totalXp)} XP`}
        >
          <LineChart data={levels} margin={{ top: 8, right: 8, bottom: 0, left: 0 }}>
            <CartesianGrid stroke="var(--color-line)" strokeOpacity={0.5} vertical={false} />
            <XAxis dataKey="day" stroke="var(--color-faint)" fontSize={13} tickLine={false} axisLine={false} />
            <YAxis stroke="var(--color-faint)" fontSize={13} width={40} allowDecimals={false} domain={['dataMin', 'dataMax']} tickLine={false} axisLine={false} />
            <Tooltip {...tooltip} formatter={(value) => [String(value), 'Level']} />
            <Line type="stepAfter" dataKey="level" stroke="var(--color-you)" dot={false} strokeWidth={2.5} animationDuration={900} isAnimationActive={animate} />
          </LineChart>
        </Chart>
      )}

      <WhenPlayed series={stats.series} />
      <LobbyPlaces series={stats.series} />
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

/** Every match of the filters, newest first, each row opening its debrief. Loads in pages of 25. */
function MatchList({ playerId, query }: { playerId: string; query: FilterQuery }) {
  const [pages, setPages] = useState(1)
  const key = JSON.stringify(query)
  const list = useLoad(() => api.matches(playerId, query, 0, 25 * pages), [playerId, key, pages])
  const navigate = useNavigate()

  if (list.state === 'loading' && pages === 1) return <Skeleton className="h-96" />
  if (list.state === 'error') return <Message>Die Matches konnten nicht geladen werden.</Message>
  const page = list.state === 'ready' ? list.data : null
  if (page && page.items.length === 0) return <Message>Keine Matches für diese Filter.</Message>

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
                    {d?.weapon && ` · ${d.weapon}`}
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
                    {d?.weapon && <span className="block text-sm text-faint">{d.weapon}</span>}
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

/** Strengths per mode, map or weapon: sortable, the win rate drawn as a bar; a name narrows the filters to it. */
function Breakdown({ rows, kind, empty, onPick }: { rows: StatsBreakdown[]; kind: string; empty: string; onPick: (name: string) => void }) {
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
              <td className="py-3 pr-4">
                <button
                  type="button"
                  onClick={() => onPick(row.name)}
                  title={`Nur ${row.name} ansehen`}
                  className="group display inline-flex items-center gap-2 text-left text-xl font-bold transition-colors duration-200 hover:text-you"
                >
                  {row.name}
                  <Icon name="filter" className="size-4 text-faint opacity-0 transition-opacity duration-200 group-hover:opacity-100 group-focus-visible:opacity-100" />
                </button>
              </td>
              <td className="py-3 pr-4 text-right text-lg">{formatCount(row.matches)}</td>
              <td className="py-3 pr-4 text-right">
                <span className="inline-flex items-center justify-end gap-3">
                  <RateBar rate={row.winRate} index={i} className="hidden w-24 sm:block" />
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
