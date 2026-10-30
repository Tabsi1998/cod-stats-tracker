namespace CodStats.Contracts;

/// <summary>Plausibility rules for a match. The server enforces them; the client checks before it uploads.</summary>
public static class MatchRules
{
    public const int MaxCount = 200;
    public const int MaxPlacement = 150;
    public const int MaxScore = 100_000;
    public const int MaxTextLength = 64;
    public const int MaxNameLength = 32;
    public const int MaxXp = 1_000_000;
    public const int MaxLevel = 1_000;
    public const int MaxModeStats = 12;
    public const int MaxModeStatKeyLength = 32;
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
        if (!Enum.IsDefined(match.Status))
        {
            problems.Add("status is not known");
        }
        else if (match.Status == MatchStatus.Left && match.Result is not null)
        {
            problems.Add("a left match has no result");
        }
        if (match.Details is { } details)
        {
            problems.AddRange(ValidateDetails(details));
        }
        return problems;
    }

    public static IReadOnlyList<string> ValidateDetails(MatchDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var problems = new List<string>();
        CheckCount(problems, "eliminations", details.Eliminations);
        foreach (var (name, value) in new[] { ("teamScore", details.TeamScore), ("enemyScore", details.EnemyScore) })
        {
            if (value is < 0 or > MaxScore)
            {
                problems.Add($"{name} must be between 0 and {MaxScore}");
            }
        }
        if (details.Xp is < 0 or > MaxXp)
        {
            problems.Add($"xp must be between 0 and {MaxXp}");
        }
        if (details.Level is < 1 or > MaxLevel)
        {
            problems.Add($"level must be between 1 and {MaxLevel}");
        }
        if (details.LobbySize is null != details.LobbyRank is null)
        {
            problems.Add("lobbySize and lobbyRank come together");
        }
        else if (details.LobbySize is { } size && details.LobbyRank is { } rank)
        {
            if (size is < 2 or > MaxPlacement)
            {
                problems.Add($"lobbySize must be between 2 and {MaxPlacement}");
            }
            if (rank < 1 || rank > size)
            {
                problems.Add("lobbyRank must be between 1 and lobbySize");
            }
        }
        CheckName(problems, "weapon", details.Weapon);
        CheckName(problems, "weaponClass", details.WeaponClass);
        if (details.ModeStats is { } stats)
        {
            if (stats.Count > MaxModeStats)
            {
                problems.Add($"at most {MaxModeStats} modeStats");
            }
            foreach (var (key, value) in stats)
            {
                if (key.Length is 0 or > MaxModeStatKeyLength || !key.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
                {
                    problems.Add($"modeStats key '{Shorten(key)}' must be 1–{MaxModeStatKeyLength} of a-z, 0-9, _");
                }
                if (value is < 0 or > MaxScore)
                {
                    problems.Add($"modeStats value of '{Shorten(key)}' must be between 0 and {MaxScore}");
                }
            }
        }
        return problems;
    }

    private static string Shorten(string text) => text.Length > MaxModeStatKeyLength ? text[..MaxModeStatKeyLength] + "…" : text;

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
        else if (placement is < 1 or > MaxPlacement)
        {
            // Free-for-all has a placement too.
            problems.Add($"placement must be between 1 and {MaxPlacement}");
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

    /// <summary>A weapon or class name: letters, digits and " .-/", at most <see cref="MaxNameLength"/> characters.</summary>
    private static void CheckName(List<string> problems, string name, string? value)
    {
        if (value is not null && (value.Length is 0 or > MaxNameLength || !value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '/')))
        {
            problems.Add($"{name} must be 1–{MaxNameLength} letters, digits, spaces or .-/");
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
        else if (value.Any(c => char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        {
            // Zero-width and direction marks: they would split one map or mode into look-alike filter entries.
            problems.Add($"{name} contains invisible characters");
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

    /// <summary>
    /// The trimmed name in Unicode normal form C, or null when it cannot be a display name. Invisible characters
    /// (zero-width, direction marks), private-use and broken surrogates are refused: "Tabsi98" with a zero-width space
    /// looks exactly like "Tabsi98" and would get past the one-name-one-profile rule.
    /// </summary>
    public static string? Normalise(string? name)
    {
        var trimmed = name?.Trim();
        if (trimmed is null)
        {
            return null;
        }
        try
        {
            trimmed = trimmed.Normalize(System.Text.NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Some platforms refuse to normalise a lone surrogate.
            return null;
        }
        if (trimmed.Length is < MinLength or > MaxLength)
        {
            return null;
        }
        // A broken surrogate comes out of the enumeration as the replacement character, which is no name either.
        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (rune == System.Text.Rune.ReplacementChar || System.Text.Rune.GetUnicodeCategory(rune) is System.Globalization.UnicodeCategory.Control
                or System.Globalization.UnicodeCategory.Format or System.Globalization.UnicodeCategory.PrivateUse
                or System.Globalization.UnicodeCategory.Surrogate or System.Globalization.UnicodeCategory.LineSeparator
                or System.Globalization.UnicodeCategory.ParagraphSeparator)
            {
                return null;
            }
        }
        return trimmed;
    }
}
