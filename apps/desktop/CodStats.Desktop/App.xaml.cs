using System.IO;
using System.Windows;
using System.Windows.Threading;
using CodStats.Desktop.Identity;
using CodStats.Desktop.Recognition;
using CodStats.Desktop.Storage;
using CodStats.Desktop.Sync;
using CodStats.Desktop.Ui;
using CodStats.Desktop.Updates;

namespace CodStats.Desktop;

/// <summary>Starts once per Windows user, asks for consent once, then lives in the tray.</summary>
#pragma warning disable CA1001 // WPF owns the application's lifetime; Quit and OnExit release everything.
public partial class App : Application
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FirstUpdateCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);

    private Mutex? single;
    private Tray? tray;
    private DispatcherTimer? syncTimer;
    private DispatcherTimer? updateTimer;
    private int failures;

    public AppPaths Paths { get; } = AppPaths.Default;
    public Settings Settings { get; private set; } = new();
    public DeviceIdentity Identity { get; private set; } = null!;
    public MatchStore Store { get; private set; } = null!;
    public SyncService Sync { get; private set; } = null!;
    public GameWatcher Watcher { get; } = new();
    public RecognitionLoop Recognition { get; private set; } = null!;
    public UpdateService Updates { get; private set; } = null!;

    /// <summary>True while an update downloads; the dashboard and the tray show it and do not offer it twice.</summary>
    public bool UpdateRunning { get; private set; }

    /// <summary>What the tracker is doing right now, from the process watcher or, while the game runs, the recognition.</summary>
    public TrackerState Tracker { get; private set; } = TrackerState.WaitingForGame;

    public static new App Current => (App)Application.Current;

    public event Action? DataChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);
        // One client per data folder: a second data folder (CODSTATS_DATA, for testing) may run beside the real one.
        var instance = Environment.GetEnvironmentVariable("CODSTATS_DATA") is { Length: > 0 }
            ? @"Local\CodStats.Desktop." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Paths.Root)))[..16]
            : @"Local\CodStats.Desktop";
        single = new Mutex(true, instance, out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        Paths.Ensure();
        Diagnostics.Initialise(Paths);
        Diagnostics.Log($"client {typeof(App).Assembly.GetName().Version} started");
        // The game comes first: below normal, the client only gets CPU time the game leaves unused, and still runs at
        // full speed when cores are free (the result screen stays only seconds, so the client must not be throttled).
        using (var self = System.Diagnostics.Process.GetCurrentProcess())
        {
            self.PriorityClass = System.Diagnostics.ProcessPriorityClass.BelowNormal;
        }
        Settings = Settings.Load(Paths);
        if (Settings.ConsentGivenAt is null && new ConsentWindow(Settings).ShowDialog() != true)
        {
            Shutdown();
            return;
        }
        Settings.Save(Paths);
        Autostart.Apply(Settings.StartWithWindows);

        Identity = DeviceIdentity.LoadOrCreate(Paths.Identity);
        Store = new MatchStore(Paths.Database);
        Store.Initialise();
        Sync = new SyncService(Store, Identity, CreateClient, Paths.Identity);

        var profiles = ProfileStore.Load([Path.Combine(AppContext.BaseDirectory, "profiles"), Paths.Profiles]);
        Recognition = new RecognitionLoop(profiles, Store,
            state => Dispatcher.BeginInvoke(() => SetTracker(state)),
            (_, _) => Dispatcher.BeginInvoke(async () =>
            {
                DataChanged?.Invoke();
                await SyncNowAsync();
            }),
            name => Dispatcher.BeginInvoke(() => AdoptRecognisedName(name)),
            () => Settings.NameConfirmed ? Settings.DisplayName : null);

        var version = typeof(App).Assembly.GetName().Version ?? new Version(0, 0, 0);
        Updates = new UpdateService(Settings.ServerUri, UpdateKey.PublicKey, version, Paths.Updates);
        updateTimer = new DispatcherTimer { Interval = FirstUpdateCheck };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Interval = UpdateCheckInterval;
            await CheckForUpdateAsync();
        };
        updateTimer.Start();

        tray = new Tray(this);
        Watcher.StateChanged += state => Dispatcher.BeginInvoke(async () =>
        {
            SetTracker(state);
            if (state == TrackerState.GameRunning)
            {
                Recognition.Start();
            }
            else
            {
                await Recognition.StopAsync();
            }
        });
        Sync.StatusChanged += status => Dispatcher.BeginInvoke(() => tray.ShowSync(status));
        Watcher.SetEnabled(Settings.TrackingEnabled);
        Watcher.Start();

        syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        syncTimer.Tick += async (_, _) => await SyncNowAsync();
        syncTimer.Start();

        if (!e.Args.Contains("--tray"))
        {
            ShowDashboard();
        }
        else
        {
            TrimMemory();
        }
    }

    /// <summary>
    /// Back in the tray: hand startup and window memory back to Windows. The tracker only waits for the game
    /// now, so there is no reason to hold on to it.
    /// </summary>
    public static void TrimMemory()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        NativeMethods.EmptyWorkingSet(self.Handle);
    }

    public ApiClient CreateClient() =>
        new(Settings.ServerUri, Identity);

    /// <summary>Syncs now; on failure the next attempt waits longer, up to 15 minutes.</summary>
    public async Task SyncNowAsync()
    {
        if (syncTimer is null)
        {
            return;
        }
        syncTimer.Stop();
        var status = await Sync.RunAsync(new ProfileWish(Settings.DisplayName, Settings.NameConfirmed, Settings.PublishProfile), CancellationToken.None);
        failures = status.Phase is SyncPhase.Done ? 0 : failures + 1;
        syncTimer.Interval = failures == 0 ? SyncInterval : TimeSpan.FromSeconds(Math.Min(MaxBackoff.TotalSeconds, 30 * Math.Pow(2, failures - 1)));
        if (status.Phase != SyncPhase.DeviceInvalid)
        {
            syncTimer.Start();
        }
        DataChanged?.Invoke();
    }

    /// <summary>Asks the server for a newer client; the dashboard and the tray offer it, nothing installs by itself.</summary>
    public async Task CheckForUpdateAsync()
    {
        if (!Settings.CheckForUpdates || !Updates.Enabled || UpdateRunning)
        {
            tray?.ShowUpdate(UpdateRunning ? Updates.Available : null);
            return;
        }
        var found = await Updates.CheckAsync(CancellationToken.None);
        if (found is not null)
        {
            Diagnostics.Log($"update: {found.Version} available");
        }
        tray?.ShowUpdate(found);
        DataChanged?.Invoke();
    }

    /// <summary>
    /// The player chose to update: download, check the signed SHA-256, start the installer silently and quit. The
    /// installer closes this client and starts the new one. Never while Call of Duty runs.
    /// </summary>
    public async Task InstallUpdateAsync()
    {
        if (Updates.Available is not { } update || UpdateRunning)
        {
            return;
        }
        if (GameWatcher.IsGameRunningNow())
        {
            MessageBox.Show("Call of Duty läuft gerade. Das Update startet COD Stats neu, darum bitte nach dem Spielen aktualisieren.",
                "COD Stats", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        UpdateRunning = true;
        tray?.ShowUpdate(update);
        DataChanged?.Invoke();
        try
        {
            var installer = await Updates.DownloadAsync(update, CancellationToken.None);
            Diagnostics.Log($"update: installing {update.Version}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(installer,
                "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPP=1")
            { UseShellExecute = true });
            Quit();
        }
        catch (Exception error) when (error is UpdateException or System.ComponentModel.Win32Exception)
        {
            Diagnostics.Log("update failed: " + error.Message);
            UpdateRunning = false;
            tray?.ShowUpdate(update);
            DataChanged?.Invoke();
            MessageBox.Show($"Das Update hat nicht geklappt. {error.Message}\n\nDu kannst es später noch einmal versuchen oder den Installer von der Webseite holen.",
                "COD Stats", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void SaveSettings()
    {
        Settings.Save(Paths);
        Autostart.Apply(Settings.StartWithWindows);
        Watcher.SetEnabled(Settings.TrackingEnabled);
        SetTracker(Watcher.State);
        DataChanged?.Invoke();
    }

    public void NotifyDataChanged() => DataChanged?.Invoke();

    private void SetTracker(TrackerState state)
    {
        Tracker = state;
        tray?.ShowTracker(state);
        DataChanged?.Invoke();
    }

    /// <summary>A name the recognition read in several matches. A name the player confirmed by hand stays.</summary>
    private void AdoptRecognisedName(string name)
    {
        if (Settings.NameConfirmed)
        {
            return;
        }
        Settings.DisplayName = name;
        Settings.NameConfirmed = true;
        SaveSettings();
        _ = SyncNowAsync();
    }

    public void ShowDashboard()
    {
        var window = Windows.OfType<MainWindow>().FirstOrDefault() ?? new MainWindow();
        window.Show();
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Activate();
    }

    public void ShowSettings()
    {
        var window = Windows.OfType<SettingsWindow>().FirstOrDefault() ?? new SettingsWindow();
        window.Show();
        window.Activate();
    }

    /// <summary>Starts over with a new identity, e.g. after the profile was deleted on the server.</summary>
    public void ResetIdentity()
    {
        Sync.Dispose();
        Identity.Dispose();
        Identity = DeviceIdentity.CreateNew(Paths.Identity);
        Sync = new SyncService(Store, Identity, CreateClient, Paths.Identity);
        Sync.StatusChanged += status => Dispatcher.BeginInvoke(() => tray?.ShowSync(status));
        failures = 0;
        syncTimer?.Start();
    }

    public void Quit()
    {
        tray?.Dispose();
        updateTimer?.Stop();
        Updates?.Dispose();
        Watcher.Dispose();
        Recognition.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        single?.Dispose();
        base.OnExit(e);
    }
}
