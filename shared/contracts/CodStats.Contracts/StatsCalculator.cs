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
    int? Placement);

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
        int? best = null;
        var series = new List<StatsPoint>(matchesOldestFirst.Count);

        foreach (var match in matchesOldestFirst)
        {
            kills += match.Kills ?? 0;
            deaths += match.Deaths ?? 0;
            assists += match.Assists ?? 0;
            if (match.Kills is { } k)
            {
                killMatches++;
                best = best is null ? k : Math.Max(best.Value, k);
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
            series);
    }
}
