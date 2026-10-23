using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CodStats.Contracts;
using CodStats.Desktop.Storage;

namespace CodStats.Desktop.Ui;

/// <summary>
/// Enter a match by hand (until the recognition exists) or correct a recognised one. Both are the player's own
/// statement about their own match; a hand entry is marked "manual" so it is never mistaken for a recognised one.
/// </summary>
public partial class MatchEntryWindow : Window
{
    public const string ManualRecognizer = "manual";
    private readonly LocalMatch? existing;

    public MatchEntryWindow(LocalMatch? existing)
    {
        this.existing = existing;
        InitializeComponent();
        if (existing is null)
        {
            Title = "Match nachtragen";
            Heading.Text = "Match nachtragen";
            Hint.Text = "Trag die Werte deines eigenen Matches ein, so wie sie am Ergebnisbildschirm standen. Leere Felder bleiben unbekannt.";
            Day.SelectedDate = DateTime.Today;
            Time.Text = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);
            return;
        }

        Title = "Match korrigieren";
        Heading.Text = "Match korrigieren";
        Hint.Text = "Die korrigierten Werte ersetzen die erkannten und werden als Korrektur markiert.";
        Select(GameBox, WireNames.Of(existing.Game));
        GameBox.IsEnabled = false;
        Kills.Text = existing.Kills?.ToString(CultureInfo.InvariantCulture);
        Deaths.Text = existing.Deaths?.ToString(CultureInfo.InvariantCulture);
        Assists.Text = existing.Assists?.ToString(CultureInfo.InvariantCulture);
        Placement.Text = existing.Placement?.ToString(CultureInfo.InvariantCulture);
        Score.Text = existing.Score?.ToString(CultureInfo.InvariantCulture);
        Mode.Text = existing.Mode;
        Map.Text = existing.Map;
        if (existing.Result is { } result)
        {
            Select(ResultBox, WireNames.Of(result));
        }
        else
        {
            ResultBox.SelectedIndex = 3;
        }
        var local = existing.PlayedAt.ToLocalTime();
        Day.SelectedDate = local.Date;
        Time.Text = local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    private Game SelectedGame =>
        WireNames.TryParse<Game>((GameBox.SelectedItem as ComboBoxItem)?.Tag as string, out var game) ? game : Game.Mw4Multiplayer;

    private void OnGameChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Mw4Fields is null)
        {
            return;
        }
        var warzone = SelectedGame.IsWarzone();
        Mw4Fields.Visibility = warzone ? Visibility.Collapsed : Visibility.Visible;
        WarzoneFields.Visibility = warzone ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var game = SelectedGame;
        if (!TryNumber(Kills, out var kills) || !TryNumber(Deaths, out var deaths) || !TryNumber(Assists, out var assists)
            || !TryNumber(Placement, out var placement) || !TryNumber(Score, out var score))
        {
            ShowError("Zahlenfelder dürfen nur ganze Zahlen enthalten oder leer sein.");
            return;
        }
        if (Day.SelectedDate is not { } day || !TimeSpan.TryParseExact(Time.Text.Trim(), @"h\:mm", CultureInfo.InvariantCulture, out var time))
        {
            ShowError("Bitte Datum und Uhrzeit (hh:mm) angeben.");
            return;
        }

        var warzone = game.IsWarzone();
        MatchResult? result = null;
        if (!warzone && WireNames.TryParse<MatchResult>((ResultBox.SelectedItem as ComboBoxItem)?.Tag as string, out var chosen))
        {
            result = chosen;
        }
        var match = new LocalMatch(
            existing?.Id ?? Guid.NewGuid(),
            game,
            warzone ? (game == Game.WarzoneResurgence ? "Resurgence" : "Battle Royale") : Blank(Mode.Text),
            new DateTimeOffset(day.Date + time),
            kills,
            deaths,
            assists,
            MatchRules.NormalisedResult(game, result, warzone ? placement : null),
            warzone ? placement : null,
            warzone ? null : Blank(Map.Text),
            warzone ? null : score,
            existing?.Confidence ?? 1.0,
            existing is null ? ReviewStatus.Confirmed : ReviewStatus.Corrected,
            existing?.RecognizerVersion ?? ManualRecognizer,
            SyncState.Waiting,
            null);

        var problems = MatchRules.Validate(match.ToUpload(), DateTimeOffset.Now);
        if (problems.Count > 0)
        {
            ShowError("Das passt noch nicht: " + string.Join(", ", problems));
            return;
        }

        var store = App.Current.Store;
        if (existing is null)
        {
            if (!store.Add(match))
            {
                ShowError("Dieses Match ist schon gespeichert (gleiche Werte zur gleichen Zeit).");
                return;
            }
        }
        else
        {
            store.Correct(match);
        }
        DialogResult = true;
    }

    private static bool TryNumber(TextBox box, out int? value)
    {
        value = null;
        var text = box.Text.Trim();
        if (text.Length == 0)
        {
            return true;
        }
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            value = number;
            return true;
        }
        return false;
    }

    private static string? Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (i.Tag as string) == tag);

    private void ShowError(string text)
    {
        Error.Text = text;
        Error.Visibility = Visibility.Visible;
    }
}
