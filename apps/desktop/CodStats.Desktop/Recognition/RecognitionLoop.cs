using System.Diagnostics;
using CodStats.Contracts;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Storage;

namespace CodStats.Desktop.Recognition;

/// <summary>
/// Runs only while Call of Duty runs: grabs a frame every couple of seconds, reads it with the profiles, feeds the
/// state machine and stores finished matches. Safe matches go up automatically; doubtful ones wait for the player.
/// </summary>
public sealed class RecognitionLoop(
    IReadOnlyList<RecognitionProfile> profiles,
    MatchStore store,
    Action<TrackerState> report,
    Action<MatchCandidate, LocalMatch> recorded,
    Action<string> nameConfirmed) : IDisposable
{
    public const string RecognizerVersion = "ocr-0.1";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// MW4 shows its result scoreboard only for about four seconds (seen on a real PC): read it quickly enough
    /// for the three readings the state machine wants.
    /// </summary>
    private static readonly TimeSpan CollectingInterval = TimeSpan.FromMilliseconds(500);

    private readonly MatchStateMachine machine = new(id => profiles.FirstOrDefault(p => p.Id == id)?.Words ?? ProfileWords.None);
    private readonly NameTracker names = new();
    private CancellationTokenSource? stop;
    private Task? running;

    public bool HasProfiles => profiles.Count > 0;

    public void Start()
    {
        if (running is not null)
        {
            return;
        }
        if (!HasProfiles || WindowsOcr.Create() is not { } ocr || !WindowCapture.IsSupported)
        {
            report(TrackerState.RecognitionUnavailable);
            return;
        }
        stop = new CancellationTokenSource();
        running = Task.Run(() => RunAsync(new ScreenReader(profiles, ocr), stop.Token));
    }

    public async Task StopAsync()
    {
        if (stop is null || running is null)
        {
            return;
        }
        await stop.CancelAsync();
        try
        {
            await running;
        }
        catch (OperationCanceledException)
        {
        }
        stop.Dispose();
        stop = null;
        running = null;
        if (machine.Reset() is { } left)
        {
            Record(left);
        }
    }

    private async Task RunAsync(ScreenReader reader, CancellationToken cancel)
    {
        WindowCapture? capture = null;
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                capture ??= WindowCapture.Start(GameWatcher.FindGameWindow());
                if (capture is null)
                {
                    await Task.Delay(Interval, cancel);
                    continue;
                }
                using (var frame = await capture.GrabAsync(TimeSpan.FromSeconds(1), cancel))
                {
                    if (frame is not null)
                    {
                        var observation = await reader.ReadAsync(frame, DateTimeOffset.Now, cancel);
                        Handle(observation);
                    }
                }
                await Task.Delay(machine.Phase == MatchPhase.Collecting ? CollectingInterval : Interval, cancel);
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
        {
            // The window went away mid-capture or capture is refused; the watcher restarts us with the game.
            report(TrackerState.Uncertain);
        }
        finally
        {
            capture?.Dispose();
        }
    }

    internal void Handle(Observation observation)
    {
        var before = machine.Phase;
        var candidate = machine.Observe(observation);
        if (before != MatchPhase.Playing && machine.Phase == MatchPhase.Playing)
        {
            names.NextMatch();
        }
        if (names.Observe(observation.OwnName) && names.Confirmed is { } name)
        {
            nameConfirmed(name);
        }

        report(observation.Screen switch
        {
            ScreenKind.Result => TrackerState.Evaluating,
            ScreenKind.InMatch or ScreenKind.Loading => TrackerState.MatchRunning,
            _ when observation.Game?.IsWarzone() == true => TrackerState.WarzoneDetected,
            _ when observation.Game == Game.Mw4Multiplayer => TrackerState.Mw4Detected,
            _ => TrackerState.GameRunning,
        });

        if (candidate is not null)
        {
            Record(candidate);
        }
    }

    private void Record(MatchCandidate candidate)
    {
        var match = ToLocal(candidate);
        if (match is null)
        {
            report(TrackerState.Uncertain);
            return;
        }
        if (store.Add(match))
        {
            recorded(candidate, match);
        }
        report(match.ReviewStatus == ReviewStatus.Pending ? TrackerState.Uncertain : TrackerState.GameRunning);
    }

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
            Math.Clamp(candidate.Confidence, 0, 1), ReviewStatus.Pending, RecognizerVersion, SyncState.Waiting, null);
        var safe = candidate.Confidence >= MatchStateMachine.SafeConfidence && candidate.Doubts.Count == 0
            && MatchRules.Validate(match.ToUpload() with { ReviewStatus = ReviewStatus.Auto }, DateTimeOffset.Now).Count == 0;
        return safe ? match with { ReviewStatus = ReviewStatus.Auto } : match;
    }

    public void Dispose() => stop?.Dispose();
}
