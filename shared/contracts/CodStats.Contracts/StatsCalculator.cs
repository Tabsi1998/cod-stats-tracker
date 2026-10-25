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
    int? Xp = null,
    int? LobbySize = null,
    int? LobbyRank = null,
    string? Weapon = null,
    string? WeaponClass = null,
    int? Level = null,
    IReadOnlyDictionary<string, int>? ModeStats = null);

/// <summary>
/// Which matches a statistic covers. At most one of Last and Days/From/To should be set. Mode, map, weapon and class
/// compare without case (the OCR reads capitals, corrections may not be); Result is the normalised one (a Warzone win
/// is first place), Status tells finished from left matches.
/// </summary>
public sealed record StatsFilter(GameGroup Game = GameGroup.All, int? Last = null, int? Days = null,
    DateTimeOffset? From = null, DateTimeOffset? To = null, string? Mode = null, string? Map = null, string? Weapon = null,
    string? WeaponClass = null, MatchResult? Result = null, MatchStatus? Status = null)
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
        foreach (var (name, text) in new[] { ("mode", Mode), ("map", Map), ("weapon", Weapon), ("weaponClass", WeaponClass) })
        {
            if (text is { Length: > MatchRules.MaxTextLength })
            {
                problems.Add($"{name} is longer than {MatchRules.MaxTextLength} characters");
            }
        }
        if (Result is not null && Status == MatchStatus.Left)
        {
            problems.Add("a left match has no result");
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
        selected = selected.Where(match => Same(match.Mode, Mode) && Same(match.Map, Map) && Same(match.Weapon, Weapon)
            && Same(match.WeaponClass, WeaponClass));
        if (Result is { } result)
        {
            selected = selected.Where(match => MatchRules.NormalisedResult(match.Game, match.Result, match.Placement) == result);
        }
        if (Status is { } status)
        {
            selected = selected.Where(match => match.Status == status);
        }
        var ordered = selected.OrderBy(match => match.PlayedAt).ThenBy(match => match.Id).ToList();
        return Last is { } last && ordered.Count > last ? ordered.GetRange(ordered.Count - last, last) : ordered;
    }

    /// <summary>True without a wanted value; otherwise the same name, trimmed and without case.</summary>
    private static bool Same(string? value, string? wanted) =>
        string.IsNullOrWhiteSpace(wanted) || string.Equals(value?.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// Share of the other players in the player's lobbies who ended with fewer points: summed over the matches
    /// (players behind / other players), like K/D, never the average of per-match shares.
    /// </summary>
    public static double? LobbyShare(long behind, long others) =>
        others > 0 ? Math.Round((double)behind / others, 4) : null;

    public static StatsResponse Compute(GameGroup group, IReadOnlyList<MatchFacts> matchesOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(matchesOldestFirst);
        int kdKills = 0, kdDeaths = 0, kills = 0, deaths = 0, assists = 0, wins = 0, withResult = 0;
        int killMatches = 0, placementSum = 0, placementMatches = 0;
        int left = 0, eliminations = 0, streak = 0, longestStreak = 0;
        long scoreSum = 0, xpSum = 0, lobbyBehind = 0, lobbyOthers = 0;
        int lobbyMatches = 0;
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
            if (match.LobbySize is { } size && match.LobbyRank is { } rank && size > 1 && rank >= 1 && rank <= size)
            {
                lobbyMatches++;
                lobbyBehind += size - rank;
                lobbyOthers += size - 1;
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
                KdRatio(kdKills, kdDeaths), WinRate(wins, withResult))
            {
                Status = match.Status,
                Mode = match.Mode,
                Map = match.Map,
                Assists = match.Assists,
                Score = match.Score,
                Xp = match.Xp,
                Level = match.Level,
                Weapon = match.Weapon,
                LobbySize = match.LobbySize,
                LobbyRank = match.LobbyRank,
            });
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
            Breakdown(matchesOldestFirst, m => m.Map),
            lobbyMatches,
            LobbyShare(lobbyBehind, lobbyOthers))
        {
            ByWeapon = Breakdown(matchesOldestFirst, m => m.Weapon),
            ByWeaponClass = Breakdown(matchesOldestFirst, m => m.WeaponClass),
            ByMapMode = MapModes(matchesOldestFirst),
            ModeStats = ModeStatSummaries(matchesOldestFirst),
            Records = Records(matchesOldestFirst),
        };
    }

    /// <summary>Win rate and K/D for every map and mode played together, most played first.</summary>
    public static IReadOnlyList<MapModeCell> MapModes(IEnumerable<MatchFacts> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches
            .Where(m => !string.IsNullOrWhiteSpace(m.Map) && !string.IsNullOrWhiteSpace(m.Mode))
            .GroupBy(m => (Map: m.Map!.Trim().ToUpperInvariant(), Mode: m.Mode!.Trim().ToUpperInvariant()))
            .Select(g =>
            {
                var figures = Figures(g.ToList());
                return new MapModeCell(g.Key.Map, g.Key.Mode, figures.Matches, figures.Wins, figures.WithResult,
                    WinRate(figures.Wins, figures.WithResult), figures.Kd);
            })
            .OrderByDescending(c => c.Matches).ThenBy(c => c.Map, StringComparer.Ordinal).ThenBy(c => c.Mode, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Each mode value (confirms, captures …) over the matches that have it: total, average per such match and best.
    /// Matches without the value do not lower the average: another mode simply has no such column.
    /// </summary>
    public static IReadOnlyList<ModeStatSummary> ModeStatSummaries(IEnumerable<MatchFacts> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        return matches
            .SelectMany(m => m.ModeStats ?? new Dictionary<string, int>())
            .GroupBy(s => s.Key, StringComparer.Ordinal)
            .Select(g => new ModeStatSummary(g.Key, g.Sum(s => (long)s.Value), g.Count(), Math.Round(g.Average(s => s.Value), 2), g.Max(s => s.Value)))
            .OrderByDescending(s => s.Matches).ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The best single match per value: kills, K/D (at least one kill), score, eliminations, assists, XP and every
    /// mode value. On a tie the newer match holds the record.
    /// </summary>
    public static IReadOnlyList<StatsRecord> Records(IReadOnlyList<MatchFacts> matchesOldestFirst)
    {
        ArgumentNullException.ThrowIfNull(matchesOldestFirst);
        var best = new Dictionary<string, StatsRecord>(StringComparer.Ordinal);
        void Offer(string kind, double? value, MatchFacts match)
        {
            if (value is { } v && (!best.TryGetValue(kind, out var held) || v >= held.Value))
            {
                best[kind] = new StatsRecord(kind, v, match.Id, match.PlayedAt, match.Mode, match.Map);
            }
        }
        foreach (var match in matchesOldestFirst)
        {
            Offer("kills", match.Kills, match);
            Offer("kd", match.Kills is > 0 && match.Deaths is { } deaths ? KdRatio(match.Kills.Value, deaths) : null, match);
            Offer("score", match.Score, match);
            Offer("eliminations", match.Eliminations, match);
            Offer("assists", match.Assists, match);
            Offer("xp", match.Xp, match);
            foreach (var (key, value) in match.ModeStats ?? new Dictionary<string, int>())
            {
                Offer("stat." + key, value, match);
            }
        }
        return best.Values.OrderBy(r => r.Kind, StringComparer.Ordinal).ToList();
    }

    private static (int Matches, int Wins, int WithResult, double? Kd) Figures(List<MatchFacts> list)
    {
        var results = list.Select(m => MatchRules.NormalisedResult(m.Game, m.Result, m.Placement)).Where(r => r is not null).ToList();
        var both = list.Where(m => m.Kills is not null && m.Deaths is not null).ToList();
        return (list.Count, results.Count(r => r == MatchResult.Win), results.Count,
            KdRatio(both.Sum(m => (long)m.Kills!.Value), both.Sum(m => (long)m.Deaths!.Value)));
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
                    scored.Count > 0 ? Math.Round(scored.Average(m => m.Score!.Value), 1) : null)
                {
                    Deaths = list.Sum(m => m.Deaths ?? 0),
                    Assists = list.Sum(m => m.Assists ?? 0),
                    LeftMatches = list.Count(m => m.Status == MatchStatus.Left),
                    LastPlayedAt = list.Max(m => m.PlayedAt),
                    ModeStats = ModeStatSummaries(list),
                };
            })
            .OrderByDescending(b => b.Matches).ThenBy(b => b.Name, StringComparer.Ordinal)
            .ToList();
    }
}
