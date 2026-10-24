// Types mirror shared/contracts/CodStats.Contracts/Dtos.cs; the API only offers read-only public endpoints here.

export type Game = 'mw4_mp' | 'wz_br' | 'wz_resurgence'
export type GameGroup = 'all' | 'mw4' | 'warzone'
export type MatchResult = 'win' | 'loss' | 'draw'
export type MatchStatus = 'completed' | 'left'
export type LeaderboardMetric = 'kd' | 'kills' | 'matches' | 'winrate'

export interface PlayerSummary {
  id: string
  displayName: string
  matches: number
  kdRatio: number | null
  lastMatchAt: string | null
}

export interface PlayerProfile {
  id: string
  displayName: string
  previousNames: string[]
  memberSince: string
  matches: number
  lastMatchAt: string | null
}

export interface StatsPoint {
  matchId: string
  game: Game
  playedAt: string
  kills: number | null
  deaths: number | null
  result: MatchResult | null
  runningKdRatio: number | null
  runningWinRate: number | null
}

export interface Stats {
  game: GameGroup
  isCombined: boolean
  matches: number
  kills: number
  deaths: number
  assists: number
  kdRatio: number | null
  wins: number
  matchesWithResult: number
  winRate: number | null
  averageKills: number | null
  bestKills: number | null
  averagePlacement: number | null
  firstMatchAt: string | null
  lastMatchAt: string | null
  series: StatsPoint[]
  leftMatches: number
  leaveRate: number | null
  eliminations: number
  averageScore: number | null
  averageXp: number | null
  totalXp: number
  bestMatchId: string | null
  longestWinStreak: number | null
  byMode: StatsBreakdown[] | null
  byMap: StatsBreakdown[] | null
}

/** The figures of the matches in one mode or on one map. */
export interface StatsBreakdown {
  name: string
  matches: number
  wins: number
  matchesWithResult: number
  winRate: number | null
  kills: number
  kdRatio: number | null
  averageKills: number | null
  averageScore: number | null
}

/** Values beyond the basics, read from the result screen and the after-action report. */
export interface MatchDetails {
  eliminations: number | null
  teamScore: number | null
  enemyScore: number | null
  xp: number | null
  level: number | null
  modeStats: Record<string, number> | null
}

export interface PublicMatch {
  id: string
  game: Game
  mode: string | null
  playedAt: string
  kills: number | null
  deaths: number | null
  assists: number | null
  result: MatchResult | null
  placement: number | null
  map: string | null
  score: number | null
  status: MatchStatus
  details: MatchDetails | null
}

export interface Page<T> {
  items: T[]
  total: number
  offset: number
  limit: number
}

export interface LeaderboardEntry {
  rank: number
  playerId: string
  displayName: string
  value: number
  matches: number
}

export interface Leaderboard {
  game: GameGroup
  metric: LeaderboardMetric
  minMatches: number
  entries: LeaderboardEntry[]
}

export interface PlatformStats {
  players: number
  matches: number
  kills: number
  lastMatchAt: string | null
}

/** A period choice of the stats filter, as query parameters. */
export type Period = { last: 10 | 25 | 50 | 100 } | { days: 7 | 30 } | { from?: string; to?: string } | Record<string, never>

export class NotFoundError extends Error {}

async function get<T>(path: string): Promise<T> {
  const response = await fetch(path, { headers: { Accept: 'application/json' } })
  if (response.status === 404) throw new NotFoundError(path)
  if (!response.ok) throw new Error(`${response.status} ${response.statusText}`)
  return (await response.json()) as T
}

function query(values: Record<string, string | number | undefined>): string {
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(values)) {
    if (value !== undefined && value !== '') params.set(key, String(value))
  }
  const text = params.toString()
  return text ? `?${text}` : ''
}

export const installerUrl = '/downloads/CodStats-Setup-win-x64.exe'

/** Whether the server offers the installer; the button stays hidden until it does. */
export async function installerAvailable(): Promise<boolean> {
  const response = await fetch(installerUrl, { method: 'HEAD' })
  return response.ok && !(response.headers.get('content-type') ?? '').includes('text/html')
}

const base = '/api/v1/public'

export const api = {
  platformStats: () => get<PlatformStats>(`${base}/platform-stats`),
  search: (q: string) => get<PlayerSummary[]>(`${base}/players/search${query({ q })}`),
  players: (offset = 0) => get<Page<PlayerSummary>>(`${base}/players${query({ offset, limit: 50 })}`),
  player: (id: string) => get<PlayerProfile>(`${base}/players/${encodeURIComponent(id)}`),
  stats: (id: string, game: GameGroup, period: Period) =>
    get<Stats>(`${base}/players/${encodeURIComponent(id)}/stats${query({ game, ...period })}`),
  matches: (id: string, game: GameGroup, offset = 0, limit = 25) =>
    get<Page<PublicMatch>>(`${base}/players/${encodeURIComponent(id)}/matches${query({ game, offset, limit })}`),
  match: (id: string, matchId: string) =>
    get<PublicMatch>(`${base}/players/${encodeURIComponent(id)}/matches/${encodeURIComponent(matchId)}`),
  leaderboard: (game: GameGroup, metric: LeaderboardMetric, mode?: string) =>
    get<Leaderboard>(`${base}/leaderboards${query({ game, metric, mode })}`),
}
