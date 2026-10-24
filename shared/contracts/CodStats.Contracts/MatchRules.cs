namespace CodStats.Contracts;

/// <summary>Plausibility rules for a match. The server enforces them; the client checks before it uploads.</summary>
public static class MatchRules
{
    public const int MaxCount = 200;
    public const int MaxPlacement = 150;
    public const int MaxScore = 100_000;
    public const int MaxTextLength = 64;
    public static readonly TimeSpan MaxFuture = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(180);

    /// <summary>The problems of a match, empty when it is plausible.</summary>
    public static IReadOnlyList<string> Validate(MatchUpload match, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(match);
        var problems = new List<string>();
        if (match.Id == Guid.Empty)
        {
            problems.Add("id is empty");
        }
        if (!Enum.IsDefined(match.Game))
        {
            problems.Add("game is not supported");
        }
        if (!Enum.IsDefined(match.ReviewStatus))
        {
            problems.Add("reviewStatus is not known");
        }
        else if (match.ReviewStatus == ReviewStatus.Pending)
        {
            problems.Add("pending matches stay on the PC until the player reviews them");
        }
        if (double.IsNaN(match.Confidence) || match.Confidence is < 0 or > 1)
        {
            problems.Add("confidence must be between 0 and 1");
        }
        if (match.RecognizerVersion is { Length: > MaxTextLength })
        {
            problems.Add($"recognizerVersion is longer than {MaxTextLength} characters");
        }
        problems.AddRange(ValidateValues(match.Game, match.Mode, match.PlayedAt, match.Kills, match.Deaths,
            match.Assists, match.Result, match.Placement, match.Map, match.Score, now));
        return problems;
    }

    public static IReadOnlyList<string> Validate(Game game, MatchCorrectionRequest correction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(correction);
        return ValidateValues(game, correction.Mode, correction.PlayedAt, correction.Kills, correction.Deaths,
            correction.Assists, correction.Result, correction.Placement, correction.Map, correction.Score, now);
    }

    /// <summary>The result a Warzone match has from its placement; other games keep theirs.</summary>
    public static MatchResult? NormalisedResult(Game game, MatchResult? result, int? placement) =>
        game.IsWarzone() && placement is { } place ? (place == 1 ? MatchResult.Win : MatchResult.Loss) : result;

    private static List<string> ValidateValues(Game game, string? mode, DateTimeOffset playedAt, int? kills, int? deaths,
        int? assists, MatchResult? result, int? placement, string? map, int? score, DateTimeOffset now)
    {
        var problems = new List<string>();
        CheckCount(problems, "kills", kills);
        CheckCount(problems, "deaths", deaths);
        CheckCount(problems, "assists", assists);
        if (score is < 0 or > MaxScore)
        {
            problems.Add($"score must be between 0 and {MaxScore}");
        }
        CheckText(problems, "mode", mode);
        CheckText(problems, "map", map);

        if (playedAt > now + MaxFuture)
        {
            problems.Add("playedAt is in the future");
        }
        if (playedAt < now - MaxAge)
        {
            problems.Add($"playedAt is older than {MaxAge.TotalDays} days");
        }

        if (result is { } known && !Enum.IsDefined(known))
        {
            problems.Add("result is not known");
        }

        if (game.IsWarzone())
        {
            if (placement is < 1 or > MaxPlacement)
            {
                problems.Add($"placement must be between 1 and {MaxPlacement}");
            }
            if (result == MatchResult.Draw)
            {
                problems.Add("warzone has no draws");
            }
            if (placement is { } place && result is { } outcome && (place == 1) != (outcome == MatchResult.Win))
            {
                problems.Add("result does not match placement");
            }
        }
        else if (placement is not null)
        {
            problems.Add("placement only exists in warzone");
        }
        return problems;
    }

    private static void CheckCount(List<string> problems, string name, int? value)
    {
        if (value is < 0 or > MaxCount)
        {
            problems.Add($"{name} must be between 0 and {MaxCount}");
        }
    }

    private static void CheckText(List<string> problems, string name, string? value)
    {
        if (value is null)
        {
            return;
        }
        if (value.Length > MaxTextLength)
        {
            problems.Add($"{name} is longer than {MaxTextLength} characters");
        }
        if (value.Any(char.IsControl))
        {
            problems.Add($"{name} contains control characters");
        }
    }
}

/// <summary>Rules for the display name the client recognised.</summary>
public static class DisplayNames
{
    public const int MinLength = 2;
    public const int MaxLength = 40;

    /// <summary>
    /// The name without a leading clan tag: the game shows "[TLS] Tabsi98", the player is "Tabsi98". A clan change
    /// must not look like a name change.
    /// </summary>
    public static string WithoutClanTag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();
        if (trimmed.StartsWith('[') && trimmed.IndexOf(']', StringComparison.Ordinal) is > 1 and <= 9 and var end)
        {
            return trimmed[(end + 1)..].Trim();
        }
        return trimmed;
    }

    /// <summary>The trimmed name, or null when it cannot be a display name.</summary>
    public static string? Normalise(string? name)
    {
        var trimmed = name?.Trim();
        if (trimmed is null || trimmed.Length is < MinLength or > MaxLength || trimmed.Any(char.IsControl))
        {
            return null;
        }
        return trimmed;
    }
}
