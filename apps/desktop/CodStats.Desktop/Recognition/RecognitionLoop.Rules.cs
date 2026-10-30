using CodStats.Contracts;
using CodStats.Desktop.Storage;

namespace CodStats.Desktop.Recognition;

/// <summary>
/// The decisions of the recognition loop that need neither Windows nor a game: they run in the platform-independent
/// recognition tests as well.
/// </summary>
public sealed partial class RecognitionLoop
{
    public const string RecognizerVersion = "ocr-0.2";

    /// <summary>The match to store, "auto" only when everything is certain; null when not even the game is known.</summary>
    public static LocalMatch? ToLocal(MatchCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Game is not { } game)
        {
            return null;
        }
        var match = new LocalMatch(Guid.NewGuid(), game, candidate.Mode, candidate.PlayedAt, candidate.Kills, candidate.Deaths,
            candidate.Assists, candidate.Result, candidate.Placement, candidate.Map, candidate.Score,
            Math.Clamp(candidate.Confidence, 0, 1), ReviewStatus.Pending, RecognizerVersion, SyncState.Waiting, null, candidate.Status,
            new MatchDetails(candidate.Eliminations, candidate.TeamScore, candidate.EnemyScore, candidate.Xp, candidate.Level,
                candidate.ModeStats.Count > 0 ? candidate.ModeStats : null, candidate.LobbySize, candidate.LobbyRank,
                candidate.Weapon, candidate.WeaponClass));
        var safe = candidate.Confidence >= MatchStateMachine.SafeConfidence && candidate.Doubts.Count == 0
            && MatchRules.Validate(match.ToUpload() with { ReviewStatus = ReviewStatus.Auto }, DateTimeOffset.Now).Count == 0;
        return safe ? match with { ReviewStatus = ReviewStatus.Auto } : match;
    }

    /// <summary>
    /// Whether an observation began a new match, so the name tracker counts the readings of each match once. Gameplay
    /// after a round's result is the same match (Kollateral shows several result screens), a loading screen or gameplay
    /// after the end of a match is a new one.
    /// </summary>
    internal static bool StartsNewMatch(MatchPhase before, MatchPhase after, ScreenKind screen, bool ended) =>
        after == MatchPhase.Playing
        && (screen == ScreenKind.Loading || before is MatchPhase.Idle or MatchPhase.Recorded || (before != MatchPhase.Playing && ended));

    /// <summary>Seconds without any frame after which the capture starts over, even on the same window.</summary>
    internal const int MaxMissedFrames = 10;

    /// <summary>
    /// Whether to capture anew: the game shows another main window than the one captured (it starts with a small splash
    /// window and then opens the real one; the old capture then delivers nothing, without an error), or no frame came for
    /// <see cref="MaxMissedFrames"/> tries in a row.
    /// </summary>
    internal static bool NeedsNewCapture(IntPtr captured, IntPtr current, int missed) =>
        (current != IntPtr.Zero && current != captured) || missed >= MaxMissedFrames;
}
