namespace CodStats.Contracts;

// Device identity

/// <param name="PublicKey">SubjectPublicKeyInfo of an ECDSA P-256 key, Base64.</param>
public sealed record RegisterDeviceRequest(string PublicKey, string? ClientVersion);

public sealed record RegisterDeviceResponse(Guid DeviceId, Guid ProfileId);

public sealed record RecoverDeviceRequest(string RecoveryKey, string PublicKey, string? ClientVersion);

public sealed record RecoveryKeyResponse(string RecoveryKey);

// Own profile

public sealed record MeResponse(
    Guid ProfileId,
    Guid DeviceId,
    string? DisplayName,
    NameStatus NameStatus,
    bool IsPublic,
    bool IsVisible,
    bool HasRecoveryKey,
    DateTimeOffset CreatedAt);

/// <param name="Status">uncertain or confirmed. A profile becomes visible only with a confirmed name.</param>
public sealed record SetDisplayNameRequest(string Name, NameStatus Status);

public sealed record PrivacyRequest(bool IsPublic);

public sealed record SyncStatusResponse(int Matches, DateTimeOffset? LastUploadAt, DateTimeOffset? LastMatchAt);

// Matches

/// <summary>
/// Values beyond the basics: what the result screen and the after-action report show. All optional; older clients
/// send none.
/// </summary>
/// <param name="TeamScore">The own team's final score (free-for-all: the player's).</param>
/// <param name="EnemyScore">The enemy team's final score (free-for-all: the leader's).</param>
/// <param name="ModeStats">Mode values by key, such as "captures" or "data_uploaded".</param>
/// <param name="LobbySize">Players on the final scoreboard, both teams. Only this number, never their names or values.</param>
/// <param name="LobbyRank">The player's place on that scoreboard by score (1 = most points; a tie counts as the better place).</param>
public sealed record MatchDetails(
    int? Eliminations = null,
    int? TeamScore = null,
    int? EnemyScore = null,
    int? Xp = null,
    int? Level = null,
    IReadOnlyDictionary<string, int>? ModeStats = null,
    int? LobbySize = null,
    int? LobbyRank = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Eliminations is null && TeamScore is null && EnemyScore is null && Xp is null && Level is null
        && (ModeStats is null || ModeStats.Count == 0) && LobbySize is null && LobbyRank is null;
}

/// <summary>One recognised match as the client uploads it. Unknown values are null, never 0.</summary>
/// <param name="Status">Played to the end or left; a left match has no result. Older clients send none (completed).</param>
public sealed record MatchUpload(
    Guid Id,
    Game Game,
    string? Mode,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    int? Assists,
    MatchResult? Result,
    int? Placement,
    string? Map,
    int? Score,
    double Confidence,
    ReviewStatus ReviewStatus,
    string? RecognizerVersion,
    MatchStatus Status = MatchStatus.Completed,
    MatchDetails? Details = null);

public sealed record MatchBatchRequest(IReadOnlyList<MatchUpload> Matches);

public sealed record MatchBatchItemResult(Guid Id, UploadOutcome Outcome, string? Reason);

public sealed record MatchBatchResponse(IReadOnlyList<MatchBatchItemResult> Results);

/// <summary>A correction by the player. Replaces all editable values; the match becomes "corrected".</summary>
public sealed record MatchCorrectionRequest(
    string? Mode,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    int? Assists,
    MatchResult? Result,
    int? Placement,
    string? Map,
    int? Score);

public sealed record PublicMatch(
    Guid Id,
    Game Game,
    string? Mode,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    int? Assists,
    MatchResult? Result,
    int? Placement,
    string? Map,
    int? Score,
    MatchStatus Status = MatchStatus.Completed,
    MatchDetails? Details = null);

public sealed record OwnMatch(
    Guid Id,
    Game Game,
    string? Mode,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    int? Assists,
    MatchResult? Result,
    int? Placement,
    string? Map,
    int? Score,
    double Confidence,
    ReviewStatus ReviewStatus,
    string? RecognizerVersion,
    DateTimeOffset UploadedAt,
    MatchStatus Status = MatchStatus.Completed,
    MatchDetails? Details = null);

public sealed record Page<T>(IReadOnlyList<T> Items, int Total, int Offset, int Limit);

// Statistics

/// <param name="KdRatio">Total kills / total deaths over matches where both are known; kills when deaths are 0.</param>
/// <param name="WinRate">Wins / matches with a known result, 0–1. Left matches have no result and do not count.</param>
/// <param name="LeftMatches">Matches the player left before the end; they count for kills and K/D, not for the win rate.</param>
/// <param name="LeaveRate">Left matches / all matches, 0–1.</param>
/// <param name="BestMatchId">The match with the most kills (on a tie the newer one).</param>
/// <param name="ByMode">The same figures per mode, most played first.</param>
/// <param name="ByMap">The same figures per map, most played first.</param>
public sealed record StatsResponse(
    GameGroup Game,
    bool IsCombined,
    int Matches,
    int Kills,
    int Deaths,
    int Assists,
    double? KdRatio,
    int Wins,
    int MatchesWithResult,
    double? WinRate,
    double? AverageKills,
    int? BestKills,
    double? AveragePlacement,
    DateTimeOffset? FirstMatchAt,
    DateTimeOffset? LastMatchAt,
    IReadOnlyList<StatsPoint> Series,
    int LeftMatches = 0,
    double? LeaveRate = null,
    int Eliminations = 0,
    double? AverageScore = null,
    double? AverageXp = null,
    long TotalXp = 0,
    Guid? BestMatchId = null,
    int? LongestWinStreak = null,
    IReadOnlyList<StatsBreakdown>? ByMode = null,
    IReadOnlyList<StatsBreakdown>? ByMap = null,
    int LobbyMatches = 0,
    double? LobbyShare = null);

/// <summary>Figures of the matches in one mode or on one map.</summary>
public sealed record StatsBreakdown(
    string Name,
    int Matches,
    int Wins,
    int MatchesWithResult,
    double? WinRate,
    int Kills,
    double? KdRatio,
    double? AverageKills,
    double? AverageScore);

/// <summary>One match in time order, with the running totals the charts draw.</summary>
public sealed record StatsPoint(
    Guid MatchId,
    Game Game,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    MatchResult? Result,
    double? RunningKdRatio,
    double? RunningWinRate);

// Public directory

public sealed record PlayerSummary(Guid Id, string DisplayName, int Matches, double? KdRatio, DateTimeOffset? LastMatchAt);

public sealed record PlayerProfile(
    Guid Id,
    string DisplayName,
    IReadOnlyList<string> PreviousNames,
    DateTimeOffset MemberSince,
    int Matches,
    DateTimeOffset? LastMatchAt);

public sealed record LeaderboardEntry(int Rank, Guid PlayerId, string DisplayName, double Value, int Matches);

public sealed record LeaderboardResponse(
    GameGroup Game,
    LeaderboardMetric Metric,
    int MinMatches,
    IReadOnlyList<LeaderboardEntry> Entries);

public sealed record PlatformStatsResponse(int Players, int Matches, long Kills, DateTimeOffset? LastMatchAt);

// Export

public sealed record ExportResponse(
    MeResponse Profile,
    IReadOnlyList<NameHistoryEntry> Names,
    IReadOnlyList<OwnMatch> Matches,
    IReadOnlyList<DeviceInfo> Devices,
    DateTimeOffset ExportedAt);

public sealed record NameHistoryEntry(string Name, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);

public sealed record DeviceInfo(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset? LastSeenAt, DateTimeOffset? RevokedAt);
