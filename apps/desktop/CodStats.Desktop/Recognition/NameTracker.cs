namespace CodStats.Desktop.Recognition;

/// <summary>
/// Decides when the recognised own name is trustworthy: the same name, read cleanly, in several different
/// matches. One reading is never enough; a name that keeps changing stays "uncertain".
/// </summary>
public sealed class NameTracker
{
    public const int MatchesNeeded = 3;
    public const double MinQuality = 0.8;

    private readonly Dictionary<string, (string Shown, HashSet<int> Matches)> seen = new(StringComparer.Ordinal);
    private int match;

    /// <summary>The name once it is certain, otherwise null.</summary>
    public string? Confirmed { get; private set; }

    /// <summary>The best guess so far, for display as "uncertain".</summary>
    public string? Candidate =>
        seen.OrderByDescending(e => e.Value.Matches.Count).Select(e => e.Value.Shown).FirstOrDefault();

    /// <summary>Call when a new match begins, so readings from one match count once.</summary>
    public void NextMatch() => match++;

    /// <summary>Feeds an own-name reading. Returns true when this reading confirmed a (new) name.</summary>
    public bool Observe(FieldReading? reading)
    {
        var shown = reading is null ? null : CodStats.Contracts.DisplayNames.WithoutClanTag(reading.Text);
        if (reading is null || reading.Quality < MinQuality || CodStats.Contracts.DisplayNames.Normalise(shown) is not { } name)
        {
            return false;
        }
        var key = TextParsing.Normalise(name);
        if (key.Length < 2)
        {
            return false;
        }
        if (!seen.TryGetValue(key, out var entry))
        {
            entry = (name, []);
            seen[key] = entry;
        }
        entry.Matches.Add(match);

        if (entry.Matches.Count >= MatchesNeeded && Confirmed != entry.Shown)
        {
            // Another name seen as often means two people play on this PC: not certain.
            var rival = seen.Where(e => e.Key != key).Any(e => e.Value.Matches.Count >= MatchesNeeded);
            if (rival)
            {
                Confirmed = null;
                return false;
            }
            Confirmed = entry.Shown;
            return true;
        }
        return false;
    }
}
