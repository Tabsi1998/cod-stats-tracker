using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CodStats.Api.Endpoints;

public sealed class LeaderboardOptions
{
    /// <summary>Players need at least this many matches in the game group to be ranked.</summary>
    public int MinMatches { get; set; } = 20;

    public int Size { get; set; } = 100;
}

/// <summary>Read-only endpoints for the website. They return only visible profiles and never device or review data.</summary>
public static class PublicEndpoints
{
    public static void MapPublicEndpoints(this RouteGroupBuilder api)
    {
        var pub = api.MapGroup("/public").WithTags("Public (read-only)").RequireRateLimiting(RateLimits.Public);
        pub.MapGet("/players", ListPlayers);
        pub.MapGet("/players/search", Search);
        pub.MapGet("/players/{id:guid}", GetPlayer);
        pub.MapGet("/players/{id:guid}/stats", GetPlayerStats);
        pub.MapGet("/players/{id:guid}/matches", GetPlayerMatches);
        pub.MapGet("/players/{id:guid}/matches/{matchId:guid}", GetPlayerMatch);
        pub.MapGet("/leaderboards", GetLeaderboard);
        pub.MapGet("/platform-stats", GetPlatformStats);
    }

    private static async Task<IResult> ListPlayers(AppDb db, int? offset, int? limit, CancellationToken cancel)
    {
        var (skip, take) = QueryFilters.Paging(offset, limit);
        var visible = db.Profiles.AsNoTracking().Visible();
        var total = await visible.CountAsync(cancel);
        var items = await Summaries(db, visible.OrderBy(p => p.DisplayName).ThenBy(p => p.Id).Skip(skip).Take(take), cancel);
        return Results.Ok(new Page<PlayerSummary>(items, total, skip, take));
    }

    /// <summary>Finds visible profiles by display name. Equal names stay separate profiles.</summary>
    private static async Task<IResult> Search(AppDb db, string? q, CancellationToken cancel)
    {
        var term = q?.Trim();
        if (term is null || term.Length is < 2 or > DisplayNames.MaxLength)
        {
            return Results.Problem(detail: $"q must be 2–{DisplayNames.MaxLength} characters", statusCode: 400);
        }
        var escaped = term.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        var pattern = "%" + escaped + "%";
        var found = db.Profiles.AsNoTracking().Visible()
            .Where(p => EF.Functions.ILike(p.DisplayName!, pattern, "\\"))
            .OrderBy(p => EF.Functions.ILike(p.DisplayName!, escaped, "\\") ? 0 : 1)
            .ThenBy(p => p.DisplayName!.Length)
            .ThenBy(p => p.Id)
            .Take(QueryFilters.MaxLimit);
        return Results.Ok(await Summaries(db, found, cancel));
    }

    private static async Task<IResult> GetPlayer(Guid id, AppDb db, CancellationToken cancel)
    {
        var profile = await db.Profiles.AsNoTracking().Visible().SingleOrDefaultAsync(p => p.Id == id, cancel);
        if (profile is null)
        {
            return Results.NotFound();
        }
        var previous = await db.NameHistory.AsNoTracking()
            .Where(n => n.ProfileId == id && n.Name != profile.DisplayName)
            .OrderByDescending(n => n.LastSeenAt).Select(n => n.Name).ToListAsync(cancel);
        var matches = db.Matches.Shown().Where(m => m.ProfileId == id);
        var count = await matches.CountAsync(cancel);
        var last = await matches.MaxAsync(m => (DateTimeOffset?)m.PlayedAt, cancel);
        return Results.Ok(new PlayerProfile(profile.Id, profile.DisplayName!, previous, profile.CreatedAt, count, last));
    }

    private static async Task<IResult> GetPlayerStats(Guid id, AppDb db, TimeProvider clock, HttpRequest request, CancellationToken cancel)
    {
        if (!await db.Profiles.Visible().AnyAsync(p => p.Id == id, cancel))
        {
            return Results.NotFound();
        }
        if (!QueryFilters.TryStats(request.Query, out var filter, out var problem))
        {
            return Results.Problem(detail: problem, statusCode: 400);
        }
        return Results.Ok(await Statistics.ForProfile(db, id, filter, clock.GetUtcNow(), shownOnly: true, cancel));
    }

    private static async Task<IResult> GetPlayerMatches(Guid id, AppDb db, string? game, int? offset, int? limit, CancellationToken cancel)
    {
        if (!await db.Profiles.Visible().AnyAsync(p => p.Id == id, cancel))
        {
            return Results.NotFound();
        }
        if (!QueryFilters.TryGameGroup(game, out var group))
        {
            return Results.Problem(detail: "game must be all, mw4 or warzone", statusCode: 400);
        }
        var (skip, take) = QueryFilters.Paging(offset, limit);
        var games = group.Members();
        var query = db.Matches.Shown().AsNoTracking().Where(m => m.ProfileId == id && games.Contains(m.Game));
        var total = await query.CountAsync(cancel);
        var items = await query.OrderByDescending(m => m.PlayedAt).ThenByDescending(m => m.Id).Skip(skip).Take(take).ToListAsync(cancel);
        return Results.Ok(new Page<PublicMatch>(items.Select(Mapping.ToPublic).ToList(), total, skip, take));
    }

    /// <summary>One match of a visible player, for the match detail page.</summary>
    private static async Task<IResult> GetPlayerMatch(Guid id, Guid matchId, AppDb db, CancellationToken cancel)
    {
        if (!await db.Profiles.Visible().AnyAsync(p => p.Id == id, cancel))
        {
            return Results.NotFound();
        }
        var match = await db.Matches.Shown().AsNoTracking().SingleOrDefaultAsync(m => m.Id == matchId && m.ProfileId == id, cancel);
        return match is null ? Results.NotFound() : Results.Ok(Mapping.ToPublic(match));
    }

    private static async Task<IResult> GetLeaderboard(AppDb db, IOptions<LeaderboardOptions> options, string? game, string? metric,
        int? minMatches, string? mode, CancellationToken cancel)
    {
        if (!QueryFilters.TryGameGroup(game, out var group))
        {
            return Results.Problem(detail: "game must be all, mw4 or warzone", statusCode: 400);
        }
        var chosen = LeaderboardMetric.KdRatio;
        if (!string.IsNullOrEmpty(metric) && !WireNames.TryParse(metric, out chosen))
        {
            return Results.Problem(detail: "metric must be kd, kills, matches or winrate", statusCode: 400);
        }
        if (mode is { Length: > MatchRules.MaxTextLength })
        {
            return Results.Problem(detail: $"mode is longer than {MatchRules.MaxTextLength} characters", statusCode: 400);
        }
        // A caller may ask for a stricter minimum, never for a looser one.
        var minimum = Math.Max(options.Value.MinMatches, minMatches ?? 0);
        var games = group.Members();

        var matches = db.Matches.Shown().AsNoTracking().Where(m => games.Contains(m.Game));
        if (!string.IsNullOrWhiteSpace(mode))
        {
            // Case-insensitive, with wildcards taken literally: modes are read in capitals, corrections may not be.
            var wanted = mode.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
            matches = matches.Where(m => m.Mode != null && EF.Functions.ILike(m.Mode, wanted, "\\"));
        }
        var rows = await matches
            .Join(db.Profiles.Visible(), m => m.ProfileId, p => p.Id, (m, p) => new { m, p.DisplayName })
            .GroupBy(x => new { x.m.ProfileId, x.DisplayName })
            .Select(g => new
            {
                g.Key.ProfileId,
                g.Key.DisplayName,
                Matches = g.Count(),
                Kills = g.Sum(x => x.m.Kills ?? 0),
                KdKills = g.Sum(x => x.m.Kills != null && x.m.Deaths != null ? x.m.Kills.Value : 0),
                KdDeaths = g.Sum(x => x.m.Kills != null && x.m.Deaths != null ? x.m.Deaths.Value : 0),
                Wins = g.Count(x => x.m.Result == MatchResult.Win),
                WithResult = g.Count(x => x.m.Result != null),
            })
            .Where(r => r.Matches >= minimum)
            .ToListAsync(cancel);

        var ranked = rows
            .Select(r => new
            {
                r.ProfileId,
                r.DisplayName,
                r.Matches,
                Value = chosen switch
                {
                    LeaderboardMetric.Kills => r.Kills,
                    LeaderboardMetric.Matches => r.Matches,
                    LeaderboardMetric.WinRate => StatsCalculator.WinRate(r.Wins, r.WithResult),
                    _ => StatsCalculator.KdRatio(r.KdKills, r.KdDeaths),
                },
            })
            .Where(r => r.Value is not null)
            .OrderByDescending(r => r.Value).ThenByDescending(r => r.Matches).ThenBy(r => r.ProfileId)
            .Take(options.Value.Size)
            .Select((r, index) => new LeaderboardEntry(index + 1, r.ProfileId, r.DisplayName!, r.Value!.Value, r.Matches))
            .ToList();
        return Results.Ok(new LeaderboardResponse(group, chosen, minimum, ranked));
    }

    private static async Task<IResult> GetPlatformStats(AppDb db, CancellationToken cancel)
    {
        var visible = db.Profiles.Visible().Select(p => p.Id);
        var players = await visible.CountAsync(cancel);
        var matches = db.Matches.Shown().Where(m => visible.Contains(m.ProfileId));
        var count = await matches.CountAsync(cancel);
        var kills = await matches.SumAsync(m => (long)(m.Kills ?? 0), cancel);
        var last = await matches.MaxAsync(m => (DateTimeOffset?)m.PlayedAt, cancel);
        return Results.Ok(new PlatformStatsResponse(players, count, kills, last));
    }

    private static async Task<List<PlayerSummary>> Summaries(AppDb db, IQueryable<Profile> profiles, CancellationToken cancel)
    {
        var rows = await profiles.Select(p => new
        {
            p.Id,
            p.DisplayName,
            Matches = db.Matches.Count(m => m.ProfileId == p.Id && m.IsPublic),
            KdKills = db.Matches.Where(m => m.ProfileId == p.Id && m.IsPublic && m.Kills != null && m.Deaths != null).Sum(m => m.Kills!.Value),
            KdDeaths = db.Matches.Where(m => m.ProfileId == p.Id && m.IsPublic && m.Kills != null && m.Deaths != null).Sum(m => m.Deaths!.Value),
            Last = db.Matches.Where(m => m.ProfileId == p.Id && m.IsPublic).Max(m => (DateTimeOffset?)m.PlayedAt),
        }).ToListAsync(cancel);
        return rows.Select(r => new PlayerSummary(r.Id, r.DisplayName!, r.Matches, StatsCalculator.KdRatio(r.KdKills, r.KdDeaths), r.Last)).ToList();
    }
}
