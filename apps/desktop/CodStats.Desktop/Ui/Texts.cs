using System.Globalization;
using CodStats.Contracts;
using CodStats.Desktop.Storage;

namespace CodStats.Desktop.Ui;

public static class Texts
{
    public const string Dash = "–";

    public static string Game(Game game) => game switch
    {
        Contracts.Game.Mw4Multiplayer => "MW4 Multiplayer",
        Contracts.Game.WarzoneBattleRoyale => "Warzone BR",
        Contracts.Game.WarzoneResurgence => "Warzone Resurgence",
        _ => game.ToString(),
    };

    public static string Result(MatchResult? result) => result switch
    {
        MatchResult.Win => "Sieg",
        MatchResult.Loss => "Niederlage",
        MatchResult.Draw => "Unentschieden",
        _ => Dash,
    };

    public static string Count(int? value) => value?.ToString("N0", CultureInfo.CurrentCulture) ?? Dash;
    public static string Ratio(double? value) => value?.ToString("0.00", CultureInfo.CurrentCulture) ?? Dash;
    public static string Percent(double? value) => value is { } v ? (v * 100).ToString("0.#", CultureInfo.CurrentCulture) + " %" : Dash;

    public static string State(LocalMatch match) => match.SyncState switch
    {
        _ when match.ReviewStatus == ReviewStatus.Pending => "Wartet auf deine Prüfung",
        SyncState.Synced => "Synchronisiert",
        SyncState.Waiting => "Wartet auf Upload",
        SyncState.CorrectionWaiting => "Korrektur wartet auf Upload",
        SyncState.Rejected => "Vom Server abgelehnt: " + match.SyncError,
        _ => match.SyncState.ToString(),
    };
}
