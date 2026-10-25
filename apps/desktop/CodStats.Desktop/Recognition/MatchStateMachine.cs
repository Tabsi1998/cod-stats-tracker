using CodStats.Contracts;

namespace CodStats.Desktop.Recognition;

public enum MatchPhase
{
    /// <summary>Menu, lobby or nothing recognised.</summary>
    Idle,
    /// <summary>A loading screen or gameplay was seen; a result screen will follow.</summary>
    Playing,
    /// <summary>A result screen is up; readings are being collected.</summary>
    Collecting,
    /// <summary>
    /// MW4: the result screen was read and is gone. It ended a round or the match; the after-action report or the
    /// lobby says the match, the next round's gameplay says the round.
    /// </summary>
    AwaitingEnd,
    /// <summary>MW4: the after-action report is up; readings are being collected.</summary>
    Reporting,
    /// <summary>The match was recorded; result screens and reports are ignored until the next match starts.</summary>
    Recorded,
}

/// <summary>
/// Follows the screens of a session and turns the readings of a match's end into one match.
/// One frame never decides: values are voted over the readings, and a match only counts as safe when the start of
/// the match was seen, the game is known, the end was confirmed and the readings agree.
/// </summary>
/// <remarks>
/// MW4 shows the same result screen after a round (Kollateral, Front, Kapern …) as after the match; only the banner
/// word differs ("RUNDENSIEG"), and the banner is the hardest text on the screen to read. So the result screen alone
/// never ends a match: the after-action report ("EINSATZBERICHT") or the lobby must follow, which only come after a
/// real end. A player who goes to the menu instead left the match.
/// </remarks>
public sealed class MatchStateMachine(Func<string, ProfileWords>? words = null)
{
    /// <summary>Readings collected from one result screen before it is evaluated.</summary>
    public const int ReadingsPerResult = 3;

    /// <summary>Below this a candidate is stored for the player's review instead of being uploaded.</summary>
    public const double SafeConfidence = 0.85;

    private static readonly TimeSpan MaxCollecting = TimeSpan.FromSeconds(20);

    /// <summary>
    /// After the result screen MW4 shows a highlight replay. Once the result has been gone this long, it is over and
    /// the end of the match is awaited (Warzone: evaluated).
    /// </summary>
    private static readonly TimeSpan ResultGone = TimeSpan.FromSeconds(8);

    /// <summary>The report followed the result after 20–35 s in recordings (highlight replay in between).</summary>
    private static readonly TimeSpan ReportWait = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan ReportGone = TimeSpan.FromSeconds(4);

    private readonly List<Observation> results = [];
    private readonly List<Observation> reports = [];
    private readonly Func<string, ProfileWords> words = words ?? (_ => ProfileWords.None);
    private bool sawStart;
    private Game? playingGame;

    /// <summary>
    /// The newest scoreboard of the running match: the Tab scoreboard, or the result screen of the last round. Never
    /// counted on its own; it gives the values of a match the player left.
    /// </summary>
    private Observation? lastScoreboard;

    /// <summary>
    /// The newest screen of the running match that named its mode: a match left before any scoreboard was readable
    /// is still recorded as left, with its mode and unknown values.
    /// </summary>
    private Observation? matchContext;

    public MatchPhase Phase { get; private set; } = MatchPhase.Idle;

    /// <summary>Feeds one observation. Returns a match once its end is certain (or the player left it).</summary>
    public MatchCandidate? Observe(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        switch (observation.Screen)
        {
            case ScreenKind.InMatch when Phase == MatchPhase.Playing:
                // The player opened the scoreboard mid-match: remember it, count nothing.
                Remember(observation);
                return null;

            case ScreenKind.InMatch when Phase is MatchPhase.Collecting or MatchPhase.AwaitingEnd && !IsWarzone:
                // Gameplay after a result screen: that result ended a round. Its table is the newest scoreboard.
                lastScoreboard = results.LastOrDefault(r => r.Fields.ContainsKey(Fields.Kills)) ?? lastScoreboard;
                matchContext = results.LastOrDefault() ?? matchContext;
                results.Clear();
                Phase = MatchPhase.Playing;
                return null;

            case ScreenKind.Loading or ScreenKind.InMatch:
                // A new match ends what came before: a report being read, a result never confirmed, or a match left.
                var before = Phase switch
                {
                    MatchPhase.Reporting => Finish(MatchStatus.Completed),
                    MatchPhase.Collecting when IsWarzone => Finish(MatchStatus.Completed),
                    MatchPhase.Collecting or MatchPhase.AwaitingEnd => Finish(MatchStatus.Completed, "Matchende nicht bestätigt (kein Einsatzbericht)"),
                    MatchPhase.Playing when observation.Screen == ScreenKind.Loading => Left(),
                    _ => null,
                };
                Phase = MatchPhase.Playing;
                sawStart = true;
                playingGame = observation.Game ?? playingGame;
                Remember(observation);
                return before;

            case ScreenKind.Result when Phase == MatchPhase.Recorded:
                return null;

            case ScreenKind.Result:
                if (Phase == MatchPhase.AwaitingEnd)
                {
                    // Another result screen: the one before ended a round, possibly without gameplay seen in between.
                    lastScoreboard = results.LastOrDefault(r => r.Fields.ContainsKey(Fields.Kills)) ?? lastScoreboard;
                    results.Clear();
                }
                if (Phase != MatchPhase.Collecting)
                {
                    results.Clear();
                    Phase = MatchPhase.Collecting;
                }
                results.Add(observation);
                if (IsWarzone && (results.Count >= ReadingsPerResult || observation.At - results[0].At > MaxCollecting))
                {
                    return Finish(MatchStatus.Completed);
                }
                return null;

            case ScreenKind.AfterAction when Phase == MatchPhase.Recorded:
                // The report stays up for a while and can be opened again from the lobby.
                return null;

            case ScreenKind.AfterAction:
                if (Phase != MatchPhase.Reporting)
                {
                    reports.Clear();
                    Phase = MatchPhase.Reporting;
                }
                reports.Add(observation);
                return reports.Count >= ReadingsPerResult ? Finish(MatchStatus.Completed) : null;

            case ScreenKind.Lobby:
                // The lobby only follows a finished match: whatever was read is the match, confirmed.
                var finished = Phase switch
                {
                    MatchPhase.Reporting or MatchPhase.Collecting or MatchPhase.AwaitingEnd => Finish(MatchStatus.Completed),
                    MatchPhase.Playing => Left(),
                    _ => null,
                };
                EndSession();
                return finished;

            case ScreenKind.Menu:
                // The start menu without a report: the player left, after a round or mid-round.
                var left = Phase switch
                {
                    MatchPhase.Reporting => Finish(MatchStatus.Completed),
                    MatchPhase.Collecting when IsWarzone => Finish(MatchStatus.Completed),
                    MatchPhase.Collecting or MatchPhase.AwaitingEnd or MatchPhase.Playing => Left(),
                    _ => null,
                };
                EndSession();
                return left;

            case ScreenKind.Unknown when Phase == MatchPhase.Collecting && observation.At - results[^1].At > ResultGone:
                if (IsWarzone)
                {
                    return Finish(MatchStatus.Completed);
                }
                Phase = MatchPhase.AwaitingEnd;
                return null;

            case ScreenKind.Unknown when Phase == MatchPhase.AwaitingEnd && observation.At - results[^1].At > ReportWait:
                return Finish(MatchStatus.Completed, "Matchende nicht bestätigt (kein Einsatzbericht)");

            case ScreenKind.Unknown when Phase == MatchPhase.Reporting && observation.At - reports[^1].At > ReportGone:
                return Finish(MatchStatus.Completed);

            default:
                return null;
        }
    }

    /// <summary>The game was closed: whatever is half collected is evaluated, then everything starts over.</summary>
    public MatchCandidate? Reset()
    {
        var left = Phase switch
        {
            MatchPhase.Reporting => Finish(MatchStatus.Completed),
            MatchPhase.Collecting or MatchPhase.AwaitingEnd => Finish(MatchStatus.Completed, "Matchende nicht bestätigt (Spiel geschlossen)"),
            MatchPhase.Playing => Left(),
            _ => null,
        };
        Phase = MatchPhase.Idle;
        sawStart = false;
        playingGame = null;
        results.Clear();
        reports.Clear();
        lastScoreboard = null;
        matchContext = null;
        return left;
    }

    private bool IsWarzone => (results.Select(r => r.Game).FirstOrDefault(g => g is not null) ?? playingGame)?.IsWarzone() == true;

    private void Remember(Observation observation)
    {
        playingGame = observation.Game ?? playingGame;
        if (observation.Fields.TryGetValue(Fields.Mode, out var mode) && !string.IsNullOrWhiteSpace(mode.Text))
        {
            matchContext = observation;
        }
        if (observation.Fields.ContainsKey(Fields.Kills))
        {
            lastScoreboard = observation;
        }
    }

    private void EndSession()
    {
        if (Phase != MatchPhase.Recorded)
        {
            Phase = MatchPhase.Idle;
            sawStart = false;
            playingGame = null;
        }
        results.Clear();
        reports.Clear();
        lastScoreboard = null;
        matchContext = null;
    }

    /// <summary>
    /// The player left before the end: the last scoreboard (Tab, or the last round's result) gives the values. It
    /// has no result and is never counted as a loss or win.
    /// </summary>
    private MatchCandidate? Left()
    {
        var board = results.LastOrDefault(r => r.Fields.ContainsKey(Fields.Kills)) ?? lastScoreboard ?? results.LastOrDefault() ?? matchContext;
        if (board is null)
        {
            results.Clear();
            reports.Clear();
            lastScoreboard = null;
            return null;
        }
        results.Clear();
        results.AddRange(Enumerable.Repeat(board, ReadingsPerResult));
        reports.Clear();
        return Finish(MatchStatus.Left);
    }

    /// <summary>
    /// One match from the result screen (kills, deaths, assists and the mode values of the table) and the
    /// after-action report (result, mode, map, score line, XP, level, tiles). The table wins for the player's own
    /// numbers, the report for everything about the match.
    /// </summary>
    private MatchCandidate? Finish(MatchStatus status, string? doubt = null)
    {
        if (results.Count == 0 && reports.Count == 0)
        {
            return null;
        }
        var doubts = new List<string>();
        if (doubt is not null)
        {
            doubts.Add(doubt);
        }
        var all = results.Concat(reports).ToList();
        var game = Vote(all.Select(r => r.Game), out var gameAgreement) ?? playingGame;
        if (game is null)
        {
            doubts.Add("Spiel nicht erkannt");
        }
        else if (playingGame is { } seen && seen != game)
        {
            doubts.Add("Spiel beim Start und am Ende verschieden erkannt");
        }
        if (!sawStart)
        {
            doubts.Add("Matchbeginn nicht gesehen");
        }
        // Enough readings of one screen, or both screens seen: two screens are independent readings, and where they
        // share a value (kills, score) a disagreement is a doubt of its own.
        var wellRead = Math.Max(results.Count, reports.Count) >= ReadingsPerResult || (results.Count > 0 && reports.Count > 0);
        if (status == MatchStatus.Completed && !wellRead && doubt is null)
        {
            doubts.Add($"nur {results.Count} Lesung(en) des Ergebnisses und {reports.Count} des Einsatzberichts");
        }

        var kills = Count(Fields.Kills, TextParsing.Count, doubts, out var killsConfidence);
        var deaths = Count(Fields.Deaths, TextParsing.Count, doubts, out _);
        var assists = Count(Fields.Assists, TextParsing.Count, doubts, out _);
        var score = Count(Fields.Score, TextParsing.Count, doubts, out _);
        var eliminations = Count(Fields.Eliminations, TextParsing.Count, doubts, out _);
        assists = Assists(kills, assists, eliminations, doubts);
        var lobby = Lobby();
        var placement = Count(Fields.Placement, TextParsing.Placement, doubts, out var placementConfidence);
        var xp = Count(Fields.Xp, TextParsing.Count, doubts, out _);
        var level = Count(Fields.Level, TextParsing.Count, doubts, out _);
        var scoreline = Text(reports, Fields.Scoreline) is { } line ? TextParsing.Numbers(line, 2) : null;

        MatchResult? result = null;
        double resultAgreement = 0;
        if (status == MatchStatus.Completed)
        {
            // The report's banner is read more reliably than the result screen's; it decides when it was read.
            result = VoteResult(reports, out resultAgreement);
            if (result is null)
            {
                result = VoteResult(results, out resultAgreement);
            }
        }
        var mode = Text(reports, Fields.Mode) ?? Text(results, Fields.Mode);
        var map = Text(reports, Fields.Map) ?? Text(results, Fields.Map);
        var notes = new List<string>();
        var weapon = Text(reports, Fields.Weapon);
        if (weapon is null && UnreadWeapon(Text(reports, Fields.Weapon + Fields.UnreadSuffix)) is { } unread)
        {
            notes.Add($"Waffe nicht in der Liste: '{unread}'");
        }
        if (game?.IsWarzone() != true && TextParsing.ContainsAny(mode ?? "", FreeForAllModes))
        {
            placement = FreeForAllPlacement(placement, result, scoreline, doubts);
        }

        // The values a statistic depends on: kills, placement in Warzone, the result of a finished MW4 match. A left
        // match needs none of them: that it was left is what it says, its values may be unreadable (yellow zeros).
        var essentials = new List<double>();
        if (status == MatchStatus.Completed)
        {
            essentials.Add(kills is null ? 0 : killsConfidence);
        }
        if (game?.IsWarzone() == true)
        {
            essentials.Add(placement is null ? 0 : placementConfidence);
        }
        else if (status == MatchStatus.Completed)
        {
            essentials.Add(result is null ? 0 : resultAgreement);
            if (result is null)
            {
                doubts.Add("Ergebnis nicht lesbar");
            }
        }
        if (kills is null && status == MatchStatus.Completed)
        {
            doubts.Add("Kills nicht lesbar");
        }

        var confidence = (essentials.Count > 0 ? essentials.Min() : 1) * (game is null ? 0.5 : gameAgreement) * (sawStart ? 1 : 0.7)
            * (wellRead ? 1 : 0.8);
        if (doubt is not null && status == MatchStatus.Completed)
        {
            confidence = Math.Min(confidence, SafeConfidence / 2);
        }
        var warzone = game?.IsWarzone() == true;
        var candidate = new MatchCandidate(game, mode, (results.Count > 0 ? results : reports)[0].At, kills, deaths, assists,
            game is { } g ? MatchRules.NormalisedResult(g, result, warzone ? placement : null) : result,
            placement, map, warzone ? null : score, Math.Round(confidence, 3), doubts, Vote(all.Select(r => r.ProfileId), out _))
        {
            Status = status,
            Eliminations = eliminations,
            TeamScore = scoreline?[0],
            EnemyScore = scoreline?[1],
            Xp = xp,
            Level = level,
            LobbySize = lobby?.Size,
            LobbyRank = lobby?.Rank,
            ModeStats = ModeStats(),
            Weapon = weapon,
            WeaponClass = Text(reports, Fields.WeaponClass),
            Notes = notes,
        };

        results.Clear();
        reports.Clear();
        lastScoreboard = null;
        matchContext = null;
        Phase = MatchPhase.Recorded;
        sawStart = false;
        return candidate;
    }

    /// <summary>What the weapon block held besides its heading (the block is empty until the report loads it).</summary>
    private static string? UnreadWeapon(string? block) =>
        block?.Split(" / ").Where(l => !TextParsing.ContainsAny(l, ["TOPLEISTUNG", "TOP PERFORMANCE"])).ToList() is { Count: > 0 } rest
            ? string.Join(" / ", rest) : null;

    private static readonly string[] FreeForAllModes = ["FREI FÜR ALLE", "FREE-FOR-ALL", "JEDER GEGEN JEDEN"];

    /// <summary>
    /// The place in free-for-all, checked against what else the match showed: the banner says SIEG for the top three,
    /// and the report's scoreline is the player's score next to the best other one, so a higher own score is first
    /// place and a lower one is not. A place that contradicts either was misread and stays unknown.
    /// </summary>
    internal static int? FreeForAllPlacement(int? placement, MatchResult? result, IReadOnlyList<int>? scoreline, List<string> doubts)
    {
        if (placement is { } place && result is MatchResult.Win or MatchResult.Loss && (place <= 3) != (result == MatchResult.Win))
        {
            doubts.Add($"Platz {place} passt nicht zu {(result == MatchResult.Win ? "Sieg" : "Niederlage")}");
            return null;
        }
        if (scoreline is [var own, var other] && own != other)
        {
            if (placement is { } read && (read == 1) != (own > other))
            {
                doubts.Add($"Platz {read} passt nicht zum Endstand {own}:{other}");
                return null;
            }
            if (placement is null && own > other && result != MatchResult.Loss)
            {
                return 1;
            }
        }
        return placement;
    }

    /// <summary>
    /// MW4 counts eliminations as kills plus assists and shows an assists column in a few modes only (TDM, Front).
    /// Without the column the assists are the difference; with it the difference is a check of all three values.
    /// </summary>
    internal static int? Assists(int? kills, int? assists, int? eliminations, List<string> doubts)
    {
        if (kills is not { } k || eliminations is not { } e)
        {
            return assists;
        }
        if (e < k)
        {
            doubts.Add("Eliminierungen kleiner als Abschüsse");
            return assists;
        }
        if (assists is { } a && a != e - k)
        {
            doubts.Add("Assists passen nicht zu Eliminierungen − Abschüsse");
        }
        return assists ?? e - k;
    }

    /// <summary>
    /// The lobby, from the final scoreboard only (a scoreboard mid-match is not the lobby's result). It is extra: an
    /// unsure reading never makes the match unsure, the lobby just stays unknown. Size and place come from the same
    /// frame and count together; at least two frames and two thirds of those that read it must agree.
    /// </summary>
    private (int Size, int Rank)? Lobby()
    {
        var read = results
            .Select(r => (Size: TextParsing.Count(r.Fields.GetValueOrDefault(Fields.LobbySize)?.Text).Value,
                Rank: TextParsing.Count(r.Fields.GetValueOrDefault(Fields.LobbyRank)?.Text).Value))
            .Where(l => l.Size is not null && l.Rank is not null)
            .Select(l => $"{l.Size}/{l.Rank}")
            .ToList();
        var winner = Vote(read, out var agreement);
        if (winner is null || read.Count(r => r == winner) < 2 || agreement < 2.0 / 3)
        {
            return null;
        }
        var parts = winner.Split('/');
        return (int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
    }

    private MatchResult? VoteResult(List<Observation> readings, out double agreement) =>
        Vote(readings.Select(r => r.Fields.GetValueOrDefault(Fields.Result) is { } f && words(r.ProfileId ?? "") is var w
            ? TextParsing.Result(f.Text, w.Win, w.Loss, w.Draw) : null), out agreement);

    /// <summary>
    /// A number voted over the readings that have it: the result screen's first, the report's when the result screen
    /// has none. When both have it and they differ, that is a doubt.
    /// </summary>
    private int? Count(string field, Func<string?, (int? Value, double Quality)> parse, List<string> doubts, out double confidence)
    {
        var table = VoteCount(results, field, parse, doubts, out var tableConfidence);
        var report = VoteCount(reports, field, parse, doubts, out var reportConfidence);
        if (table is not null && report is not null && table != report)
        {
            doubts.Add($"{field}: Ergebnis und Einsatzbericht verschieden");
        }
        confidence = table is not null ? tableConfidence : reportConfidence;
        return table ?? report;
    }

    private static int? VoteCount(List<Observation> readings, string field, Func<string?, (int? Value, double Quality)> parse, List<string> doubts, out double confidence)
    {
        var parsed = readings
            .Select(r => r.Fields.GetValueOrDefault(field))
            .Where(f => f is not null)
            .Select(f => parse(f!.Text) is var p && p.Value is not null ? (p.Value, Quality: p.Quality * f.Quality) : ((int?)null, Quality: 0.0))
            .ToList();
        if (parsed.Count == 0)
        {
            confidence = 0;
            return null;
        }
        var winner = Vote(parsed.Select(p => p.Item1), out var agreement);
        if (winner is null)
        {
            confidence = 0;
            return null;
        }
        if (agreement < 1)
        {
            doubts.Add($"{field}: Lesungen uneinig");
        }
        confidence = agreement * parsed.Where(p => p.Item1 == winner).Average(p => p.Quality);
        return winner;
    }

    /// <summary>Mode values ("stat.captures"): the result screen's table first, then the report's tiles.</summary>
    private Dictionary<string, int> ModeStats()
    {
        var stats = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var readings in new[] { results, reports })
        {
            foreach (var field in readings.SelectMany(r => r.Fields.Keys).Where(k => k.StartsWith(Fields.StatPrefix, StringComparison.Ordinal)).Distinct())
            {
                var value = Vote(readings.Select(r => r.Fields.GetValueOrDefault(field) is { } f ? TextParsing.Count(f.Text).Value : null), out _);
                if (value is { } known)
                {
                    stats.TryAdd(field[Fields.StatPrefix.Length..], known);
                }
            }
        }
        return stats;
    }

    private static string? Text(List<Observation> readings, string field) =>
        Vote(readings.Select(r => r.Fields.GetValueOrDefault(field)?.Text?.Trim()).Select(t => string.IsNullOrEmpty(t) ? null : t), out _);

    /// <summary>The most frequent non-null value and its share of all readings; null without a strict majority.</summary>
    private static T? Vote<T>(IEnumerable<T?> values, out double agreement)
    {
        var all = values.ToList();
        var groups = all.Where(v => v is not null).GroupBy(v => v).OrderByDescending(g => g.Count()).ToList();
        if (groups.Count == 0 || (groups.Count > 1 && groups[0].Count() == groups[1].Count()))
        {
            agreement = 0;
            return default;
        }
        agreement = (double)groups[0].Count() / all.Count;
        return groups[0].Key;
    }
}

/// <summary>Words of a profile that the state machine needs to read the result field.</summary>
public sealed record ProfileWords(IReadOnlyList<string> Win, IReadOnlyList<string> Loss, IReadOnlyList<string> Draw)
{
    public static readonly ProfileWords None = new([], [], []);
}
