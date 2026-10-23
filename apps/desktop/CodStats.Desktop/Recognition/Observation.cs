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
    string? ProfileId);
