using System.Text.Json.Serialization;

namespace CodStats.Contracts;

/// <summary>A supported game and mode family. Everything else is ignored.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<Game>))]
public enum Game
{
    [JsonStringEnumMemberName("mw4_mp")] Mw4Multiplayer,
    [JsonStringEnumMemberName("wz_br")] WarzoneBattleRoyale,
    [JsonStringEnumMemberName("wz_resurgence")] WarzoneResurgence,
}

/// <summary>What a statistic covers. <see cref="All"/> is the combined overview and must be labelled as such.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<GameGroup>))]
public enum GameGroup
{
    [JsonStringEnumMemberName("all")] All,
    [JsonStringEnumMemberName("mw4")] Mw4,
    [JsonStringEnumMemberName("warzone")] Warzone,
}

[JsonConverter(typeof(JsonStringEnumConverter<MatchResult>))]
public enum MatchResult
{
    [JsonStringEnumMemberName("win")] Win,
    [JsonStringEnumMemberName("loss")] Loss,
    [JsonStringEnumMemberName("draw")] Draw,
}

/// <summary>How far a recognised match can be trusted. Only <see cref="Pending"/> stays on the PC.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReviewStatus>))]
public enum ReviewStatus
{
    [JsonStringEnumMemberName("pending")] Pending,
    [JsonStringEnumMemberName("auto")] Auto,
    [JsonStringEnumMemberName("confirmed")] Confirmed,
    [JsonStringEnumMemberName("corrected")] Corrected,
}

/// <summary>How sure the client is that the display name belongs to this player.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<NameStatus>))]
public enum NameStatus
{
    [JsonStringEnumMemberName("unknown")] Unknown,
    [JsonStringEnumMemberName("uncertain")] Uncertain,
    [JsonStringEnumMemberName("confirmed")] Confirmed,
}

[JsonConverter(typeof(JsonStringEnumConverter<UploadOutcome>))]
public enum UploadOutcome
{
    [JsonStringEnumMemberName("created")] Created,
    [JsonStringEnumMemberName("unchanged")] Unchanged,
    [JsonStringEnumMemberName("duplicate")] Duplicate,
    [JsonStringEnumMemberName("rejected")] Rejected,
}

[JsonConverter(typeof(JsonStringEnumConverter<LeaderboardMetric>))]
public enum LeaderboardMetric
{
    [JsonStringEnumMemberName("kd")] KdRatio,
    [JsonStringEnumMemberName("kills")] Kills,
    [JsonStringEnumMemberName("matches")] Matches,
    [JsonStringEnumMemberName("winrate")] WinRate,
}

public static class Games
{
    public static bool IsWarzone(this Game game) => game is Game.WarzoneBattleRoyale or Game.WarzoneResurgence;

    public static bool Contains(this GameGroup group, Game game) => group switch
    {
        GameGroup.All => true,
        GameGroup.Mw4 => game == Game.Mw4Multiplayer,
        GameGroup.Warzone => game.IsWarzone(),
        _ => false,
    };

    public static IReadOnlyList<Game> Members(this GameGroup group) =>
        Enum.GetValues<Game>().Where(game => group.Contains(game)).ToArray();
}
