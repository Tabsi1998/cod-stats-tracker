using CodStats.Contracts;

namespace CodStats.Desktop.Recognition;

/// <summary>What kind of screen a captured frame shows.</summary>
public enum ScreenKind
{
    Unknown,
    Menu,
    Loading,
    InMatch,
    Result,

    /// <summary>The after-action report (MW4 "EINSATZBERICHT"): shown only once a match really ended.</summary>
    AfterAction,

    /// <summary>The lobby between matches, where the next map is voted on. Never shown after a round.</summary>
    Lobby,
}

/// <summary>The raw text OCR read for one field, and how clean it was.</summary>
public sealed record FieldReading(string Text, double Quality);

/// <summary>
/// One analysed frame: which screen, which game (from the matching profile), and the field texts read from it.
/// The frame itself is gone by the time this exists; only text leaves the capture.
/// </summary>
public sealed record Observation(
    DateTimeOffset At,
    ScreenKind Screen,
    Game? Game,
    string? ProfileId,
    IReadOnlyDictionary<string, FieldReading> Fields,
    FieldReading? OwnName = null)
{
    public static Observation Of(DateTimeOffset at, ScreenKind screen, Game? game = null) =>
        new(at, screen, game, null, new Dictionary<string, FieldReading>());
}

/// <summary>Field names shared by profiles and the state machine.</summary>
public static class Fields
{
    public const string Kills = "kills";
    public const string Deaths = "deaths";
    public const string Assists = "assists";
    public const string Score = "score";
    public const string Placement = "placement";
    public const string Result = "result";
    public const string Mode = "mode";
    public const string Map = "map";
    public const string Eliminations = "eliminations";

    /// <summary>The banner's ELIM/T-QUOTE ("1.67"): eliminations ÷ deaths, a check on both and a source of the deaths.</summary>
    public const string EliminationRatio = "eliminationRatio";

    /// <summary>
    /// The final score as "300 0": the own team's, then the enemy's (free-for-all: the player's, then the leader's).
    /// </summary>
    public const string Scoreline = "scoreline";

    /// <summary>The two scores of the report read each in its team colour (see <see cref="TextColour"/>).</summary>
    public const string TeamScore = "teamScore";
    public const string EnemyScore = "enemyScore";
    public const string Xp = "xp";
    public const string Level = "level";

    /// <summary>Players on the final scoreboard and the own place among them by score (see <see cref="OwnRowSpec.Lobby"/>).</summary>
    public const string LobbySize = "lobby.size";
    public const string LobbyRank = "lobby.rank";

    /// <summary>Mode values such as "stat.captures" come with this prefix.</summary>
    public const string StatPrefix = "stat.";

    /// <summary>The report's top weapon and its class, each as written in the profile's list (see <see cref="FieldSpec.OneOf"/>).</summary>
    public const string Weapon = "weapon";
    public const string WeaponClass = "weaponClass";

    /// <summary>What a <see cref="FieldSpec.OneOf"/> field read when it fits no entry, as "weapon.unread": for the log only.</summary>
    public const string UnreadSuffix = ".unread";
}

/// <summary>A match the state machine assembled from several result frames.</summary>
public sealed record MatchCandidate(
    Game? Game,
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
    IReadOnlyList<string> Doubts,
    string? ProfileId)
{
    /// <summary>Played to the end, or left before (values then come from the last scoreboard seen).</summary>
    public MatchStatus Status { get; init; } = MatchStatus.Completed;
    public int? Eliminations { get; init; }
    public int? TeamScore { get; init; }
    public int? EnemyScore { get; init; }
    public int? Xp { get; init; }
    public int? Level { get; init; }
    public int? LobbySize { get; init; }
    public int? LobbyRank { get; init; }

    /// <summary>Mode values from the scoreboard and the after-action report, such as captures or data uploaded.</summary>
    public IReadOnlyDictionary<string, int> ModeStats { get; init; } = new Dictionary<string, int>();
    public string? Weapon { get; init; }
    public string? WeaponClass { get; init; }

    /// <summary>
    /// Extras left out, such as a weapon missing from the list: logged, but unlike <see cref="Doubts"/> they never
    /// make the match unsure.
    /// </summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
