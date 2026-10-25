using System.Diagnostics;
using System.Text.RegularExpressions;

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
public sealed partial class GameWatcher : IDisposable
{
    private readonly Func<IEnumerable<string>> runningProcesses;
    private readonly System.Threading.Timer timer;
    private bool enabled = true;

    public GameWatcher(Func<IEnumerable<string>>? runningProcesses = null)
    {
        this.runningProcesses = runningProcesses ?? RunningProcessNames;
        timer = new System.Threading.Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public TrackerState State { get; private set; } = TrackerState.WaitingForGame;

    public event Action<TrackerState>? StateChanged;

    /// <summary>
    /// The CoD launcher builds name their executable after the year's title: Warzone ran as cod25-cod.exe
    /// (Black Ops 7) when checked on a real PC in October 2026, so MW4 is expected as cod26-cod.exe.
    /// The pattern also covers the years after it, so a new title needs no new client.
    /// </summary>
    public static bool IsCodProcess(string name) => CodProcess().IsMatch(name);

    /// <summary>The main window of the running CoD process, or zero.</summary>
    public static IntPtr FindGameWindow()
    {
        var window = IntPtr.Zero;
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (window == IntPtr.Zero && IsCodProcess(process.ProcessName) && process.MainWindowHandle != IntPtr.Zero)
                {
                    window = process.MainWindowHandle;
                }
            }
        }
        return window;
    }

    /// <summary>Looks now, also while tracking is off: an update must not restart the client mid-game.</summary>
    public static bool IsGameRunningNow() => RunningProcessNames().Any(IsCodProcess);

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
            return runningProcesses().Any(IsCodProcess);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static List<string> RunningProcessNames()
    {
        var names = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                names.Add(process.ProcessName);
            }
        }
        return names;
    }

    [GeneratedRegex(@"^cod(\d{2}-cod)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CodProcess();

    public void Dispose() => timer.Dispose();
}
