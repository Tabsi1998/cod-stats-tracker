using System.Diagnostics;

namespace CodStats.Desktop.Recognition;

public enum TrackerState
{
    TrackingOff,
    WaitingForGame,
    GameRunning,
    Mw4Detected,
    WarzoneDetected,
    MatchRunning,
    Evaluating,
    Uncertain,
    RecognitionUnavailable,
}

public static class TrackerStateText
{
    public static string Of(TrackerState state) => state switch
    {
        TrackerState.TrackingOff => "Tracking deaktiviert",
        TrackerState.WaitingForGame => "Wartet auf Spiel",
        TrackerState.GameRunning => "Call of Duty läuft (Erkennung folgt)",
        TrackerState.Mw4Detected => "MW4 erkannt",
        TrackerState.WarzoneDetected => "Warzone erkannt",
        TrackerState.MatchRunning => "Match läuft",
        TrackerState.Evaluating => "Ergebnis wird ausgewertet",
        TrackerState.Uncertain => "Erkennung unsicher, bitte Match prüfen",
        TrackerState.RecognitionUnavailable => "Call of Duty läuft, Erkennung noch nicht verfügbar",
        _ => state.ToString(),
    };
}

/// <summary>
/// Notices when Call of Duty starts and stops by looking at process names every few seconds. That is all it does
/// while no game runs: no capture, no window access. Which mode runs (MW4 or Warzone) needs the screen and is
/// decided by the <see cref="RecognitionLoop"/>, which this watcher starts and stops.
/// </summary>
public sealed class GameWatcher : IDisposable
{
    /// <summary>Process names of the CoD launcher builds (without .exe). To be checked against MW4 on a real PC.</summary>
    public static readonly IReadOnlyList<string> DefaultProcessNames = ["cod", "cod26", "codmw4", "ModernWarfare"];

    private readonly IReadOnlyList<string> processNames;
    private readonly Func<IEnumerable<string>> runningProcesses;
    private readonly System.Threading.Timer timer;
    private bool enabled = true;

    public GameWatcher(IReadOnlyList<string>? processNames = null, Func<IEnumerable<string>>? runningProcesses = null)
    {
        this.processNames = processNames ?? DefaultProcessNames;
        this.runningProcesses = runningProcesses ?? (() => Process.GetProcesses().Select(p => { using (p) { return p.ProcessName; } }));
        timer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TrackerState State { get; private set; } = TrackerState.WaitingForGame;

    public IReadOnlyList<string> ProcessNames => processNames;

    public event Action<TrackerState>? StateChanged;

    public void Start() => timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(5));

    public void SetEnabled(bool value)
    {
        enabled = value;
        Poll();
    }

    internal void Poll()
    {
        var next = !enabled ? TrackerState.TrackingOff
            : IsGameRunning() ? TrackerState.GameRunning
            : TrackerState.WaitingForGame;
        if (next != State)
        {
            State = next;
            StateChanged?.Invoke(next);
        }
    }

    private bool IsGameRunning()
    {
        try
        {
            return runningProcesses().Any(name => processNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose() => timer.Dispose();
}
