using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CodStats.Contracts;
using CodStats.Desktop.Recognition;
using CodStats.Desktop.Storage;
using CodStats.Desktop.Sync;

namespace CodStats.Desktop.Ui;

/// <summary>The player's own dashboard. It reads only the local database: own matches, nothing from other players.</summary>
public partial class MainWindow : Window
{
    private readonly App app = App.Current;
    private readonly Action refresh;
    private List<LocalMatch> shown = [];

    public MainWindow()
    {
        InitializeComponent();
        // The version in the title bar: which client runs is the first question after every update.
        var version = typeof(MainWindow).Assembly.GetName().Version;
        Title = version is null ? "COD Stats" : $"COD Stats {version.Major}.{version.Minor}.{version.Build}";
        refresh = () => Dispatcher.BeginInvoke(Refresh);
        app.DataChanged += refresh;

        app.Sync.StatusChanged += OnSync;
        Closed += (_, _) =>
        {
            app.DataChanged -= refresh;
            app.Sync.StatusChanged -= OnSync;
            // The dashboard is closed, not hidden: its memory can go back once it is gone.
            Dispatcher.BeginInvoke(App.TrimMemory, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
        Loaded += (_, _) => Refresh();
    }

    private void OnSync(SyncStatus status) => Dispatcher.BeginInvoke(Refresh);

    private GameGroup Group => TabWarzone.IsChecked == true ? GameGroup.Warzone : TabAll.IsChecked == true ? GameGroup.All : GameGroup.Mw4;

    private StatsFilter Filter()
    {
        var tag = Period.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;
        RangePanel.Visibility = tag == "range" ? Visibility.Visible : Visibility.Collapsed;
        return tag?.Split(':') switch
        {
            ["last", var n] => new StatsFilter(Group, Last: int.Parse(n, CultureInfo.InvariantCulture)),
            ["days", var n] => new StatsFilter(Group, Days: int.Parse(n, CultureInfo.InvariantCulture)),
            ["range"] => new StatsFilter(Group,
                From: From.SelectedDate is { } from ? new DateTimeOffset(from.Date) : null,
                To: To.SelectedDate is { } to ? new DateTimeOffset(to.Date.AddDays(1).AddTicks(-1)) : null),
            _ => new StatsFilter(Group),
        };
    }

    private void Refresh()
    {
        if (!IsLoaded)
        {
            return;
        }
        var settings = app.Settings;
        PlayerName.Text = settings.DisplayName ?? "Spielername noch unbekannt";
        IdentityChip.Visibility = settings.NameConfirmed ? Visibility.Collapsed : Visibility.Visible;
        IdentityText.Text = settings.DisplayName is null ? "Spieleridentität unsicher" : "Name nicht bestätigt, wird nicht veröffentlicht";
        TrackerText.Text = TrackerStateText.Of(app.Tracker);
        var update = app.Settings.CheckForUpdates || app.UpdateRunning ? app.Updates.Available : null;
        UpdateNote.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateText.Text = update is null ? ""
            : app.UpdateRunning ? $"Version {update.Version} wird geladen und geprüft. COD Stats startet danach neu."
            : $"Version {update.Version} ist da. Das Update dauert einen Moment, deine Daten bleiben erhalten.";
        UpdateButton.IsEnabled = !app.UpdateRunning;
        RecognitionNote.Visibility = app.Recognition.HasProfiles ? Visibility.Collapsed : Visibility.Visible;
        SyncText.Text = app.Sync.Status.Text + (app.Sync.Status.LastSuccess is { } last ? $" (zuletzt {last:HH:mm})" : "");

        var filter = Filter();
        CombinedNote.Visibility = filter.Game == GameGroup.All ? Visibility.Visible : Visibility.Collapsed;
        var all = app.Store.All();
        // Matches waiting for the player's review are shown in the list but not counted.
        var counted = all.Where(m => m.ReviewStatus != ReviewStatus.Pending && m.SyncState != SyncState.Rejected).Select(m => m.ToFacts());
        var stats = StatsCalculator.Compute(filter.Game, filter.Apply(counted, DateTimeOffset.Now));

        Cards.Children.Clear();
        AddCard("K/D", Texts.Ratio(stats.KdRatio), "Kills gesamt ÷ Deaths gesamt");
        AddCard("KILLS", Texts.Count(stats.Kills), $"Ø {Texts.Ratio(stats.AverageKills)} pro Match");
        AddCard("DEATHS", Texts.Count(stats.Deaths), null);
        AddCard("MATCHES", Texts.Count(stats.Matches), null);
        AddCard("WINRATE", Texts.Percent(stats.WinRate), $"{stats.Wins} von {stats.MatchesWithResult}");
        AddCard("LETZTES MATCH", stats.LastMatchAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? Texts.Dash,
            stats.Series.Count > 0 ? $"{Texts.Count(stats.Series[^1].Kills)} Kills, {Texts.Result(stats.Series[^1].Result)}" : null);

        KdChart.Values = stats.Series.Select(p => p.RunningKdRatio).ToList();
        KillsChart.Values = stats.Series.Select(p => (double?)p.Kills).ToList();
        WinChart.Values = stats.Series.Select(p => p.RunningWinRate).ToList();

        shown = all.Where(m => filter.Game.Contains(m.Game)).OrderByDescending(m => m.PlayedAt).Take(25).ToList();
        Recent.ItemsSource = shown.Select(m => new
        {
            When = m.PlayedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
            Game = Texts.Game(m.Game),
            Kills = Texts.Count(m.Kills),
            Deaths = Texts.Count(m.Deaths),
            Placement = Texts.Count(m.Placement),
            Result = m.Status == MatchStatus.Left ? "Verlassen" : Texts.Result(m.Result),
            State = Texts.State(m),
        }).ToList();
        Empty.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AddCard(string label, string value, string? hint)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Label") });
        panel.Children.Add(new TextBlock { Text = value, FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0) });
        if (hint is not null)
        {
            panel.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = (System.Windows.Media.Brush)FindResource("Muted") });
        }
        Cards.Children.Add(new Border { Style = (Style)FindResource("Card"), Child = panel, Margin = new Thickness(5) });
    }

    private void OnFilter(object sender, RoutedEventArgs e) => Refresh();

    private async void OnSync(object sender, RoutedEventArgs e) => await app.SyncNowAsync();

    private void OnSettings(object sender, RoutedEventArgs e) => app.ShowSettings();

    private async void OnUpdate(object sender, RoutedEventArgs e) => await app.InstallUpdateAsync();

    private void OnAddMatch(object sender, RoutedEventArgs e)
    {
        if (new MatchEntryWindow(null) { Owner = this }.ShowDialog() == true)
        {
            app.NotifyDataChanged();
            _ = app.SyncNowAsync();
        }
    }

    private void OnEditMatch(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (Recent.SelectedIndex is var index && index >= 0 && index < shown.Count
            && new MatchEntryWindow(shown[index]) { Owner = this }.ShowDialog() == true)
        {
            app.NotifyDataChanged();
            _ = app.SyncNowAsync();
        }
    }
}
