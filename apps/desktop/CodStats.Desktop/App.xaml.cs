using System.Windows;
using System.Windows.Threading;
using CodStats.Desktop.Identity;
using CodStats.Desktop.Recognition;
using CodStats.Desktop.Storage;
using CodStats.Desktop.Sync;
using CodStats.Desktop.Ui;

namespace CodStats.Desktop;

/// <summary>Starts once per Windows user, asks for consent once, then lives in the tray.</summary>
#pragma warning disable CA1001 // WPF owns the application's lifetime; Quit and OnExit release everything.
public partial class App : Application
{
    private static readonly TimeSpan SyncInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(15);

    private Mutex? single;
    private Tray? tray;
    private DispatcherTimer? syncTimer;
    private int failures;

    public AppPaths Paths { get; } = AppPaths.Default;
    public Settings Settings { get; private set; } = new();
    public DeviceIdentity Identity { get; private set; } = null!;
    public MatchStore Store { get; private set; } = null!;
    public SyncService Sync { get; private set; } = null!;
    public GameWatcher Watcher { get; } = new();

    public static new App Current => (App)Application.Current;

    public event Action? DataChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnStartup(e);
        single = new Mutex(true, @"Local\CodStats.Desktop", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        Paths.Ensure();
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

        tray = new Tray(this);
        Watcher.StateChanged += state => Dispatcher.BeginInvoke(() => tray.ShowTracker(state));
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
    }

    public ApiClient CreateClient() =>
        new(Settings.ServerUri ?? throw new InvalidOperationException("Serveradresse ist ungültig"), Identity);

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

    public void SaveSettings()
    {
        Settings.Save(Paths);
        Autostart.Apply(Settings.StartWithWindows);
        Watcher.SetEnabled(Settings.TrackingEnabled);
        tray?.ShowTracker(Watcher.State);
        DataChanged?.Invoke();
    }

    public void NotifyDataChanged() => DataChanged?.Invoke();

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
        Watcher.Dispose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        single?.Dispose();
        base.OnExit(e);
    }
}
