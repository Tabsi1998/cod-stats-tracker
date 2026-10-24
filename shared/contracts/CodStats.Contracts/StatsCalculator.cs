namespace CodStats.Contracts;

/// <summary>The values of one match that statistics are made of.</summary>
public sealed record MatchFacts(
    Guid Id,
    Game Game,
    DateTimeOffset PlayedAt,
    int? Kills,
    int? Deaths,
    int? Assists,
    MatchResult? Result,
    int? Placement,
    MatchStatus Status = MatchStatus.Completed,
    string? Mode = null,
    string? Map = null,
    int? Score = null,
    int? Eliminations = null,
    int? Xp = null);

/// <summary>Which matches a statistic covers. At most one of Last and Days/From/To should be set.</summary>
public sealed record StatsFilter(GameGroup Game = GameGroup.All, int? Last = null, int? Days = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null)
{
    public static readonly IReadOnlySet<int> AllowedLast = new HashSet<int> { 10, 25, 50, 100 };
    public static readonly IReadOnlySet<int> AllowedDays = new HashSet<int> { 7, 30 };

    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Last is { } last && !AllowedLast.Contains(last))
        {
            problems.Add("last must be 10, 25, 50 or 100");
        }
        if (Days is { } days && !AllowedDays.Contains(days))
        {
            problems.Add("days must be 7 or 30");
        }
        if (From is { } from && To is { } to && from > to)
        {
            problems.Add("from is after to");
        }
        if (Last is not null && (Days is not null || From is not null || To is not null))
        {
            problems.Add("combine last with neither days nor from/to");
        }
        if (Days is not null && (From is not null || To is not null))
        {
            problems.Add("combine days with neither from nor to");
        }
        return problems;
    }

    /// <summary>The matches this filter selects, oldest first.</summary>
    public IReadOnlyList<MatchFacts> Apply(IEnumerable<MatchFacts> matches, DateTimeOffset now)
    {
        var selected = matches.Where(match => Game.Contains(match.Game));
        if (Days is { } days)
        {
            selected = selected.Where(match => match.PlayedAt >= now.AddDays(-days));
        }
        if (From is { } from)
        {
            selected = selected.Where(match => match.PlayedAt >= from);
        }
        if (To is { } to)
        {
            selected = selected.Where(match => match.PlayedAt <= to);
        }
        var ordered = selected.OrderBy(match => match.PlayedAt).ThenBy(match => match.Id).ToList();
        return Last is { } last && ordered.Count > last ? ordered.GetRange(ordered.Count - last, last) : ordered;
    }
}

public static class StatsCalculator
{
    /// <summary>
    /// K/D over a set of matches: total kills / total deaths, counting only matches where both are known.
    /// Never the average of per-match ratios. With no deaths the ratio is the kill count; with neither, none.
    /// </summary>
    public static double? KdRatio(long kills, long deaths) =>
        deaths > 0 ? Math.Round((double)kills / deaths, 2) : kills > 0 ? kills : null;

    public static double? WinRate(int wins, int withResult) =>
        withResult > 0 ? Math.Round((double)wins / withResult, 4) : null;

    public static StatsResponse Compute(GameGroup group, IReadOnlyList<MatchFacts> matchesOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(matchesOldestFirst);
        int kdKills = 0, kdDeaths = 0, kills = 0, deaths = 0, assists = 0, wins = 0, withResult = 0;
        int killMatches = 0, placementSum = 0, placementMatches = 0;
        int left = 0, eliminations = 0, streak = 0, longestStreak = 0;
        long scoreSum = 0, xpSum = 0;
        int scoreMatches = 0, xpMatches = 0;
        int? best = null;
        Guid? bestMatch = null;
        var series = new List<StatsPoint>(matchesOldestFirst.Count);

        foreach (var match in matchesOldestFirst)
        {
            kills += match.Kills ?? 0;
            deaths += match.Deaths ?? 0;
            assists += match.Assists ?? 0;
            eliminations += match.Eliminations ?? 0;
            if (match.Status == MatchStatus.Left)
            {
                left++;
            }
            if (match.Score is { } score)
            {
                scoreSum += score;
                scoreMatches++;
            }
            if (match.Xp is { } xp)
            {
                xpSum += xp;
                xpMatches++;
            }
            if (match.Kills is { } k)
            {
                killMatches++;
                // Oldest first: ">=" lets the newer of two equal matches win.
                if (best is null || k >= best.Value)
                {
                    best = k;
                    bestMatch = match.Id;
                }
            }
            if (match.Kills is { } kk && match.Deaths is { } dd)
            {
                kdKills += kk;
                kdDeaths += dd;
            }
            var result = MatchRules.NormalisedResult(match.Game, match.Result, match.Placement);
            if (result is not null)
            {
                withResult++;
                if (result == MatchResult.Win)
                {
                    wins++;
                }
                // A left match ends no streak and continues none: it has no result.
                streak = result == MatchResult.Win ? streak + 1 : 0;
                longestStreak = Math.Max(longestStreak, streak);
            }
            if (match.Game.IsWarzone() && match.Placement is { } place)
            {
                placementSum += place;
                placementMatches++;
            }
            series.Add(new StatsPoint(match.Id, match.Game, match.PlayedAt, match.Kills, match.Deaths, result,
                KdRatio(kdKills, kdDeaths), WinRate(wins, withResult)));
        }

        return new StatsResponse(
            group,
            group == GameGroup.All,
            matchesOldestFirst.Count,
            kills,
            deaths,
            assists,
            KdRatio(kdKills, kdDeaths),
            wins,
            withResult,
            WinRate(wins, withResult),
            killMatches > 0 ? Math.Round((double)kills / killMatches, 2) : null,
            best,
            placementMatches > 0 ? Math.Round((double)placementSum / placementMatches, 1) : null,
            matchesOldestFirst.Count > 0 ? matchesOldestFirst[0].PlayedAt : null,
            matchesOldestFirst.Count > 0 ? matchesOldestFirst[^1].PlayedAt : null,
            series,
            left,
            matchesOldestFirst.Count > 0 ? Math.Round((double)left / matchesOldestFirst.Count, 4) : null,
            eliminations,
            scoreMatches > 0 ? Math.Round((double)scoreSum / scoreMatches, 1) : null,
            xpMatches > 0 ? Math.Round((double)xpSum / xpMatches, 1) : null,
            xpSum,
            bestMatch,
            withResult > 0 ? longestStreak : null,
            Breakdown(matchesOldestFirst, m => m.Mode),
            Breakdown(matchesOldestFirst, m => m.Map));
    }

    /// <summary>
    /// The figures per mode or map, most played first. Names are compared without case, since the OCR reads them in
    /// capitals and corrections may not be.
    /// </summary>
    public static IReadOnlyList<StatsBreakdown> Breakdown(IEnumerable<MatchFacts> matches, Func<MatchFacts, string?> key)
    {
        ArgumentNullException.ThrowIfNull(matches);
        ArgumentNullException.ThrowIfNull(key);
        return matches
            .Where(m => !string.IsNullOrWhiteSpace(key(m)))
            .GroupBy(m => key(m)!.Trim().ToUpperInvariant())
            .Select(g =>
            {
                var list = g.ToList();
                var results = list.Select(m => MatchRules.NormalisedResult(m.Game, m.Result, m.Placement)).Where(r => r is not null).ToList();
                var wins = results.Count(r => r == MatchResult.Win);
                var withKills = list.Where(m => m.Kills is not null).ToList();
                var both = list.Where(m => m.Kills is not null && m.Deaths is not null).ToList();
                var scored = list.Where(m => m.Score is not null).ToList();
                return new StatsBreakdown(
                    g.Key,
                    list.Count,
                    wins,
                    results.Count,
                    WinRate(wins, results.Count),
                    withKills.Sum(m => m.Kills!.Value),
                    KdRatio(both.Sum(m => (long)m.Kills!.Value), both.Sum(m => (long)m.Deaths!.Value)),
                    withKills.Count > 0 ? Math.Round(withKills.Average(m => m.Kills!.Value), 2) : null,
                    scored.Count > 0 ? Math.Round(scored.Average(m => m.Score!.Value), 1) : null);
            })
            .OrderByDescending(b => b.Matches).ThenBy(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }
}
