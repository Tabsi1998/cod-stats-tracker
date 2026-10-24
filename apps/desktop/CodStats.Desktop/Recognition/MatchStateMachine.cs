using CodStats.Contracts;

namespace CodStats.Desktop.Recognition;

public enum MatchPhase
{
    /// <summary>Menu, lobby or nothing recognised.</summary>
    Idle,
    /// <summary>A loading screen or gameplay was seen; a result screen will follow.</summary>
    Playing,
    /// <summary>The result screen is up; readings are being collected.</summary>
    Collecting,
    /// <summary>The match was recorded; result screens are ignored until the next match starts.</summary>
    Recorded,
}

/// <summary>
/// Follows the screens of a session and turns several readings of a result screen into one match.
/// One frame never decides: values are voted over the readings, and a match only counts as safe when
/// the start of the match was seen, the game is known and the readings agree.
/// </summary>
public sealed class MatchStateMachine(Func<string, ProfileWords>? words = null)
{
    /// <summary>Readings collected from one result screen before it is evaluated.</summary>
    public const int ReadingsPerResult = 3;

    /// <summary>Below this a candidate is stored for the player's review instead of being uploaded.</summary>
    public const double SafeConfidence = 0.85;

    private static readonly TimeSpan MaxCollecting = TimeSpan.FromSeconds(20);

    /// <summary>
    /// After the result screen MW4 shows a highlight replay and the after-action report, which no profile knows.
    /// Once the result has been gone this long, what was read is evaluated instead of waiting for the next match.
    /// </summary>
    private static readonly TimeSpan ResultGone = TimeSpan.FromSeconds(8);

    private readonly List<Observation> readings = [];
    private readonly Func<string, ProfileWords> words = words ?? (_ => ProfileWords.None);
    private bool sawStart;
    private Game? playingGame;

    /// <summary>The newest in-match scoreboard (Tab). Never counted on its own; only a fallback when no result screen came.</summary>
    private Observation? lastScoreboard;

    public MatchPhase Phase { get; private set; } = MatchPhase.Idle;

    /// <summary>Feeds one observation. Returns a match when a result screen has been read enough times.</summary>
    public MatchCandidate? Observe(Observation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        switch (observation.Screen)
        {
            case ScreenKind.InMatch when Phase == MatchPhase.Playing:
                // The player opened the scoreboard mid-match: remember it, count nothing.
                if (observation.Fields.ContainsKey(Fields.Kills))
                {
                    lastScoreboard = observation;
                }
                playingGame = observation.Game ?? playingGame;
                return null;

            case ScreenKind.Loading or ScreenKind.InMatch:
                // A new match always ends what was being collected, or what was left without a result screen.
                var pending = Phase == MatchPhase.Collecting ? Evaluate()
                    : observation.Screen == ScreenKind.Loading ? FromScoreboard() : null;
                Phase = MatchPhase.Playing;
                sawStart = true;
                playingGame = observation.Game ?? playingGame;
                if (observation.Fields.ContainsKey(Fields.Kills))
                {
                    lastScoreboard = observation;
                }
                return pending;

            case ScreenKind.Result when Phase == MatchPhase.Recorded:
                return null;

            case ScreenKind.Result:
                if (Phase != MatchPhase.Collecting)
                {
                    readings.Clear();
                    Phase = MatchPhase.Collecting;
                }
                readings.Add(observation);
                return readings.Count >= ReadingsPerResult || observation.At - readings[0].At > MaxCollecting ? Evaluate() : null;

            case ScreenKind.Menu:
                var left = Phase == MatchPhase.Collecting ? Evaluate() : FromScoreboard();
                if (Phase != MatchPhase.Recorded)
                {
                    Phase = MatchPhase.Idle;
                    sawStart = false;
                    playingGame = null;
                }
                return left;

            case ScreenKind.Unknown when Phase == MatchPhase.Collecting && observation.At - readings[^1].At > ResultGone:
                return Evaluate();

            default:
                return null;
        }
    }

    /// <summary>The game was closed: whatever is half collected is evaluated, then everything starts over.</summary>
    public MatchCandidate? Reset()
    {
        var left = Phase == MatchPhase.Collecting ? Evaluate() : FromScoreboard();
        Phase = MatchPhase.Idle;
        sawStart = false;
        playingGame = null;
        readings.Clear();
        return left;
    }

    /// <summary>
    /// The match ended without a result screen (left early, crash, alt-tab): the last scoreboard becomes a match
    /// for the player to review. It is never uploaded on its own, because a mid-match scoreboard is not final.
    /// </summary>
    private MatchCandidate? FromScoreboard()
    {
        if (Phase != MatchPhase.Playing || lastScoreboard is null)
        {
            lastScoreboard = null;
            return null;
        }
        readings.Clear();
        readings.AddRange(Enumerable.Repeat(lastScoreboard, ReadingsPerResult));
        lastScoreboard = null;
        var candidate = Evaluate();
        return candidate is null ? null : candidate with
        {
            Confidence = Math.Min(candidate.Confidence, SafeConfidence / 2),
            Doubts = [.. candidate.Doubts, "Ergebnisbildschirm nicht gesehen, letzter Scoreboard-Stand"],
        };
    }

    private MatchCandidate? Evaluate()
    {
        if (readings.Count == 0)
        {
            return null;
        }
        var doubts = new List<string>();
        var game = Vote(readings.Select(r => r.Game), out var gameAgreement) ?? playingGame;
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
        if (readings.Count < ReadingsPerResult)
        {
            doubts.Add($"nur {readings.Count} Lesung(en) des Ergebnisses");
        }

        var profile = Vote(readings.Select(r => r.ProfileId), out _);
        var profileWords = profile is null ? ProfileWords.None : words(profile);
        var kills = VoteCount(Fields.Kills, TextParsing.Count, doubts, out var killsConfidence);
        var deaths = VoteCount(Fields.Deaths, TextParsing.Count, doubts, out var deathsConfidence);
        var assists = VoteCount(Fields.Assists, TextParsing.Count, doubts, out _);
        var score = VoteCount(Fields.Score, TextParsing.Count, doubts, out _);
        var placement = VoteCount(Fields.Placement, TextParsing.Placement, doubts, out var placementConfidence);
        var result = Vote(readings.Select(r => r.Fields.GetValueOrDefault(Fields.Result) is { } f
            ? TextParsing.Result(f.Text, profileWords.Win, profileWords.Loss, profileWords.Draw) : null), out var resultAgreement);
        var mode = VoteText(Fields.Mode);
        var map = VoteText(Fields.Map);

        // The values a statistic depends on: kills always, placement in Warzone, the result in MW4.
        var essentials = new List<double> { kills is null ? 0 : killsConfidence };
        if (game?.IsWarzone() == true)
        {
            essentials.Add(placement is null ? 0 : placementConfidence);
        }
        else
        {
            essentials.Add(result is null ? 0 : resultAgreement);
        }
        if (kills is null)
        {
            doubts.Add("Kills nicht lesbar");
        }

        var confidence = essentials.Min() * (game is null ? 0.5 : gameAgreement) * (sawStart ? 1 : 0.7)
            * (readings.Count >= ReadingsPerResult ? 1 : 0.8);
        var candidate = new MatchCandidate(game, mode, readings[0].At, kills, deaths, assists,
            game is { } g ? MatchRules.NormalisedResult(g, result, g.IsWarzone() ? placement : null) : result,
            game?.IsWarzone() == true ? placement : null, map, game?.IsWarzone() == true ? null : score,
            Math.Round(confidence, 3), doubts, profile);

        readings.Clear();
        lastScoreboard = null;
        Phase = MatchPhase.Recorded;
        sawStart = false;
        return candidate;
    }

    private int? VoteCount(string field, Func<string?, (int? Value, double Quality)> parse, List<string> doubts, out double confidence)
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

    private string? VoteText(string field) =>
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
