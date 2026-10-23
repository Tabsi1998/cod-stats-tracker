using System.Globalization;
using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api.Endpoints;

public static class Visibility
{
    /// <summary>A profile is public only when the player allowed it and confirmed the name.</summary>
    public static bool IsVisible(Profile profile) =>
        profile.IsPublic && profile.NameStatus == NameStatus.Confirmed && profile.DisplayName is not null;

    public static IQueryable<Profile> Visible(this IQueryable<Profile> profiles) =>
        profiles.Where(p => p.IsPublic && p.NameStatus == NameStatus.Confirmed && p.DisplayName != null);
}

public static class Mapping
{
    public static Match ToEntity(MatchUpload upload, Guid profileId, DateTimeOffset now) => new()
    {
        Id = upload.Id,
        ProfileId = profileId,
        Game = upload.Game,
        Mode = upload.Mode,
        PlayedAt = upload.PlayedAt.ToUniversalTime(),
        Kills = upload.Kills,
        Deaths = upload.Deaths,
        Assists = upload.Assists,
        Result = MatchRules.NormalisedResult(upload.Game, upload.Result, upload.Placement),
        Placement = upload.Placement,
        Map = upload.Map,
        Score = upload.Score,
        Confidence = upload.Confidence,
        ReviewStatus = upload.ReviewStatus,
        RecognizerVersion = upload.RecognizerVersion,
        UploadedAt = now,
        UpdatedAt = now,
    };

    public static bool SameValues(Match a, Match b) =>
        a.Game == b.Game && a.Mode == b.Mode && a.PlayedAt == b.PlayedAt && a.Kills == b.Kills && a.Deaths == b.Deaths
        && a.Assists == b.Assists && a.Result == b.Result && a.Placement == b.Placement && a.Map == b.Map && a.Score == b.Score;

    public static bool IsDuplicate(Match a, Match b) =>
        a.Game == b.Game && (a.PlayedAt - b.PlayedAt).Duration() <= TimeSpan.FromMinutes(3)
        && a.Kills == b.Kills && a.Deaths == b.Deaths && a.Placement == b.Placement && a.Result == b.Result && a.Score == b.Score;

    public static OwnMatch ToOwn(Match m) => new(m.Id, m.Game, m.Mode, m.PlayedAt, m.Kills, m.Deaths, m.Assists, m.Result,
        m.Placement, m.Map, m.Score, m.Confidence, m.ReviewStatus, m.RecognizerVersion, m.UploadedAt);

    public static PublicMatch ToPublic(Match m) => new(m.Id, m.Game, m.Mode, m.PlayedAt, m.Kills, m.Deaths, m.Assists,
        m.Result, m.Placement, m.Map, m.Score);

    public static MatchFacts ToFacts(Match m) => new(m.Id, m.Game, m.PlayedAt, m.Kills, m.Deaths, m.Assists, m.Result, m.Placement);
}

public static class Statistics
{
    /// <summary>Upper bound of matches a statistic reads; far beyond what one player records in a season.</summary>
    private const int MaxMatches = 20_000;

    public static async Task<StatsResponse> ForProfile(AppDb db, Guid profileId, StatsFilter filter, DateTimeOffset now, CancellationToken cancel)
    {
        var games = filter.Game.Members();
        var matches = await db.Matches.AsNoTracking()
            .Where(m => m.ProfileId == profileId && games.Contains(m.Game))
            .OrderByDescending(m => m.PlayedAt).Take(MaxMatches)
            .Select(m => new MatchFacts(m.Id, m.Game, m.PlayedAt, m.Kills, m.Deaths, m.Assists, m.Result, m.Placement))
            .ToListAsync(cancel);
        return StatsCalculator.Compute(filter.Game, filter.Apply(matches, now));
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
        filter = new StatsFilter(group, last, days, from, to);
        var problems = filter.Problems();
        problem = problems.Count > 0 ? string.Join("; ", problems) : null;
        return problem is null;
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
