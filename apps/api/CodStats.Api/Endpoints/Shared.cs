using System.Globalization;
using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api.Endpoints;

public static class Visibility
{
    /// <summary>
    /// A profile is shown when its name is confirmed and it is public, or it was public once and still has matches
    /// shown from that time.
    /// </summary>
    public static IQueryable<Profile> Visible(this IQueryable<Profile> profiles) =>
        profiles.Where(p => p.NameStatus == NameStatus.Confirmed && p.DisplayName != null && (p.IsPublic || p.Matches.Any(m => m.IsPublic)));

    /// <summary>Matches the website may show.</summary>
    public static IQueryable<Match> Shown(this IQueryable<Match> matches) => matches.Where(m => m.IsPublic);
}

public static class Mapping
{
    public static Match ToEntity(MatchUpload upload, Guid profileId, bool isPublic, DateTimeOffset now) => new()
    {
        Id = upload.Id,
        ProfileId = profileId,
        IsPublic = isPublic,
        Game = upload.Game,
        Mode = upload.Mode,
        PlayedAt = Seconds(upload.PlayedAt),
        Kills = upload.Kills,
        Deaths = upload.Deaths,
        Assists = upload.Assists,
        Result = MatchRules.NormalisedResult(upload.Game, upload.Result, upload.Placement),
        Placement = upload.Placement,
        Map = upload.Map,
        Score = upload.Score,
        Status = upload.Status,
        Eliminations = upload.Details?.Eliminations,
        TeamScore = upload.Details?.TeamScore,
        EnemyScore = upload.Details?.EnemyScore,
        Xp = upload.Details?.Xp,
        Level = upload.Details?.Level,
        ModeStats = upload.Details?.ModeStats is { Count: > 0 } stats ? new Dictionary<string, int>(stats) : null,
        LobbySize = upload.Details?.LobbySize,
        LobbyRank = upload.Details?.LobbyRank,
        Weapon = upload.Details?.Weapon,
        WeaponClass = upload.Details?.WeaponClass,
        Confidence = upload.Confidence,
        ReviewStatus = upload.ReviewStatus,
        RecognizerVersion = upload.RecognizerVersion,
        UploadedAt = now,
        UpdatedAt = now,
    };

    /// <summary>
    /// Match times in whole seconds, UTC. PostgreSQL keeps microseconds and .NET ticks, so finer values would make a
    /// repeated upload look like a different match.
    /// </summary>
    public static DateTimeOffset Seconds(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

    public static bool SameValues(Match a, Match b) =>
        a.Game == b.Game && a.Mode == b.Mode && a.PlayedAt == b.PlayedAt && a.Kills == b.Kills && a.Deaths == b.Deaths
        && a.Assists == b.Assists && a.Result == b.Result && a.Placement == b.Placement && a.Map == b.Map && a.Score == b.Score
        && a.Status == b.Status && a.Eliminations == b.Eliminations && a.TeamScore == b.TeamScore && a.EnemyScore == b.EnemyScore
        && a.Xp == b.Xp && a.Level == b.Level && a.LobbySize == b.LobbySize && a.LobbyRank == b.LobbyRank
        && a.Weapon == b.Weapon && a.WeaponClass == b.WeaponClass
        && (a.ModeStats ?? []).Count == (b.ModeStats ?? []).Count && !(a.ModeStats ?? []).Except(b.ModeStats ?? []).Any();

    public static bool IsDuplicate(Match a, Match b) =>
        a.Game == b.Game && (a.PlayedAt - b.PlayedAt).Duration() <= TimeSpan.FromMinutes(3)
        && a.Kills == b.Kills && a.Deaths == b.Deaths && a.Placement == b.Placement && a.Result == b.Result && a.Score == b.Score;

    public static OwnMatch ToOwn(Match m) => new(m.Id, m.Game, m.Mode, m.PlayedAt, m.Kills, m.Deaths, m.Assists, m.Result,
        m.Placement, m.Map, m.Score, m.Confidence, m.ReviewStatus, m.RecognizerVersion, m.UploadedAt, m.Status, Details(m));

    public static PublicMatch ToPublic(Match m) => new(m.Id, m.Game, m.Mode, m.PlayedAt, m.Kills, m.Deaths, m.Assists,
        m.Result, m.Placement, m.Map, m.Score, m.Status, Details(m));

    /// <summary>The extra values, or null when the match has none (uploaded by an older client).</summary>
    public static MatchDetails? Details(Match m)
    {
        var details = new MatchDetails(m.Eliminations, m.TeamScore, m.EnemyScore, m.Xp, m.Level, m.ModeStats, m.LobbySize, m.LobbyRank,
            m.Weapon, m.WeaponClass);
        return details.IsEmpty ? null : details;
    }

    public static MatchFacts ToFacts(Match m) => new(m.Id, m.Game, m.PlayedAt, m.Kills, m.Deaths, m.Assists, m.Result, m.Placement,
        m.Status, m.Mode, m.Map, m.Score, m.Eliminations, m.Xp, m.LobbySize, m.LobbyRank, m.Weapon, m.WeaponClass, m.Level, m.ModeStats);
}

public static class Statistics
{
    /// <summary>Upper bound of matches a statistic reads; far beyond what one player records in a season.</summary>
    private const int MaxMatches = 20_000;

    /// <param name="shownOnly">For the website: only the matches it may show. The player's own view has all.</param>
    public static async Task<StatsResponse> ForProfile(AppDb db, Guid profileId, StatsFilter filter, DateTimeOffset now, bool shownOnly,
        CancellationToken cancel)
    {
        var source = shownOnly ? db.Matches.Shown() : db.Matches;
        var matches = await source.AsNoTracking()
            .Where(m => m.ProfileId == profileId)
            .Newest(filter, now).Take(MaxMatches)
            .Select(m => new MatchFacts(m.Id, m.Game, m.PlayedAt, m.Kills, m.Deaths, m.Assists, m.Result, m.Placement,
                m.Status, m.Mode, m.Map, m.Score, m.Eliminations, m.Xp, m.LobbySize, m.LobbyRank, m.Weapon, m.WeaponClass,
                m.Level, m.ModeStats))
            .ToListAsync(cancel);
        return StatsCalculator.Compute(filter.Game, filter.Apply(matches, now));
    }
}

public static class MatchQueries
{
    private static readonly Game[] Warzone = [Game.WarzoneBattleRoyale, Game.WarzoneResurgence];

    /// <summary>
    /// The filter in SQL, as <see cref="StatsFilter.Apply"/> does it in memory, except Last (the caller takes the newest
    /// matches). The result is the normalised one: in Warzone the placement decides when there is one.
    /// </summary>
    public static IQueryable<Match> Filtered(this IQueryable<Match> matches, StatsFilter filter, DateTimeOffset now)
    {
        var games = filter.Game.Members();
        matches = matches.Where(m => games.Contains(m.Game));
        if (filter.Days is { } days)
        {
            var since = now.AddDays(-days);
            matches = matches.Where(m => m.PlayedAt >= since);
        }
        if (filter.From is { } from)
        {
            matches = matches.Where(m => m.PlayedAt >= from);
        }
        if (filter.To is { } to)
        {
            matches = matches.Where(m => m.PlayedAt <= to);
        }
        if (filter.Mode is { } mode)
        {
            var pattern = QueryFilters.Literal(mode);
            matches = matches.Where(m => m.Mode != null && EF.Functions.ILike(m.Mode, pattern, "\\"));
        }
        if (filter.Map is { } map)
        {
            var pattern = QueryFilters.Literal(map);
            matches = matches.Where(m => m.Map != null && EF.Functions.ILike(m.Map, pattern, "\\"));
        }
        if (filter.Weapon is { } weapon)
        {
            var pattern = QueryFilters.Literal(weapon);
            matches = matches.Where(m => m.Weapon != null && EF.Functions.ILike(m.Weapon, pattern, "\\"));
        }
        if (filter.WeaponClass is { } weaponClass)
        {
            var pattern = QueryFilters.Literal(weaponClass);
            matches = matches.Where(m => m.WeaponClass != null && EF.Functions.ILike(m.WeaponClass, pattern, "\\"));
        }
        if (filter.Status is { } status)
        {
            matches = matches.Where(m => m.Status == status);
        }
        matches = filter.Result switch
        {
            MatchResult.Win => matches.Where(m => Warzone.Contains(m.Game) && m.Placement != null
                ? m.Placement == 1 : m.Result == MatchResult.Win),
            MatchResult.Loss => matches.Where(m => Warzone.Contains(m.Game) && m.Placement != null
                ? m.Placement != 1 : m.Result == MatchResult.Loss),
            MatchResult.Draw => matches.Where(m => !(Warzone.Contains(m.Game) && m.Placement != null) && m.Result == MatchResult.Draw),
            _ => matches,
        };
        return matches;
    }

    /// <summary>The filtered matches, newest first, limited to the newest <see cref="StatsFilter.Last"/>.</summary>
    public static IQueryable<Match> Newest(this IQueryable<Match> matches, StatsFilter filter, DateTimeOffset now)
    {
        var newest = matches.Filtered(filter, now).OrderByDescending(m => m.PlayedAt).ThenByDescending(m => m.Id);
        return filter.Last is { } last ? newest.Take(last) : newest;
    }
}

public static class QueryFilters
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    public static (int Skip, int Take) Paging(int? offset, int? limit) =>
        (Math.Max(0, offset ?? 0), Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit));

    public static bool TryGameGroup(string? text, out GameGroup group)
    {
        group = GameGroup.All;
        return string.IsNullOrEmpty(text) || WireNames.TryParse(text, out group);
    }

    public static bool TryStats(IQueryCollection query, out StatsFilter filter, out string? problem)
    {
        filter = new StatsFilter();
        problem = null;
        if (!TryGameGroup(query["game"], out var group))
        {
            problem = "game must be all, mw4 or warzone";
            return false;
        }
        if (!TryInt(query["last"], out var last) || !TryInt(query["days"], out var days)
            || !TryDate(query["from"], out var from) || !TryDate(query["to"], out var to))
        {
            problem = "last, days, from or to is not a valid value";
            return false;
        }
        if (!TryEnum<MatchResult>(query["result"], out var result) || !TryEnum<MatchStatus>(query["status"], out var status))
        {
            problem = "result must be win, loss or draw; status completed or left";
            return false;
        }
        filter = new StatsFilter(group, last, days, from, to, Text(query["mode"]), Text(query["map"]), Text(query["weapon"]),
            Text(query["weaponClass"]), result, status);
        var problems = filter.Problems();
        problem = problems.Count > 0 ? string.Join("; ", problems) : null;
        return problem is null;
    }

    /// <summary>The text as a LIKE pattern that matches only itself (no case): wildcards are taken literally.</summary>
    public static string Literal(string text) =>
        text.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static string? Text(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static bool TryEnum<T>(string? text, out T? value) where T : struct, Enum
    {
        value = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }
        if (WireNames.TryParse<T>(text, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool TryInt(string? text, out int? value)
    {
        value = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    private static bool TryDate(string? text, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }
}
