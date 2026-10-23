using System.IO;
using System.Windows;
using CodStats.Contracts;
using CodStats.Desktop.Identity;
using CodStats.Desktop.Sync;

namespace CodStats.Desktop.Ui;

public partial class SettingsWindow : Window
{
    private readonly App app = App.Current;

    public SettingsWindow()
    {
        InitializeComponent();
        var settings = app.Settings;
        NameBox.Text = settings.DisplayName;
        NameConfirmed.IsChecked = settings.NameConfirmed;
        Publish.IsChecked = settings.PublishProfile;
        Tracking.IsChecked = settings.TrackingEnabled;
        Autostart.IsChecked = settings.StartWithWindows;
        Server.Text = settings.ServerUrl;
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var name = DisplayNames.Normalise(NameBox.Text);
        if (NameBox.Text.Trim().Length > 0 && name is null)
        {
            Status.Text = $"Der Name muss {DisplayNames.MinLength} bis {DisplayNames.MaxLength} Zeichen haben.";
            return;
        }
        var settings = app.Settings;
        var previousServer = settings.ServerUrl;
        settings.ServerUrl = Server.Text.Trim();
        if (settings.ServerUri is null)
        {
            settings.ServerUrl = previousServer;
            Status.Text = "Die Serveradresse muss mit https:// beginnen.";
            return;
        }
        settings.DisplayName = name;
        settings.NameConfirmed = name is not null && NameConfirmed.IsChecked == true;
        settings.PublishProfile = Publish.IsChecked == true;
        settings.TrackingEnabled = Tracking.IsChecked == true;
        settings.StartWithWindows = Autostart.IsChecked == true;
        app.SaveSettings();
        Status.Text = "Gespeichert. Wird synchronisiert …";
        await app.SyncNowAsync();
        Status.Text = app.Sync.Status.Text + (app.Sync.Status.Detail is { } detail ? $": {detail}" : "");
    }

    private async void OnCreateRecovery(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        using var api = app.CreateClient();
        var key = await api.CreateRecoveryKeyAsync(CancellationToken.None);
        RecoveryShown.Text = key.RecoveryKey;
        RecoveryShown.Visibility = Visibility.Visible;
        Clipboard.SetText(key.RecoveryKey);
        Status.Text = "Schlüssel erzeugt und in die Zwischenablage kopiert. Er wird nur dieses eine Mal angezeigt.";
    });

    private async void OnRecover(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var key = RecoveryInput.Text.Trim();
        if (key.Length == 0)
        {
            Status.Text = "Bitte den Wiederherstellungsschlüssel eingeben.";
            return;
        }
        if (MessageBox.Show(this, "Dieser PC übernimmt das Profil vom alten PC. Lokale Matches dieses PCs, die noch nicht hochgeladen sind, gehen dann ins übernommene Profil. Fortfahren?",
                "Profil übernehmen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        app.ResetIdentity();
        using var api = app.CreateClient();
        var registered = await api.RecoverAsync(key, app.Identity.PublicKey, CancellationToken.None);
        app.Identity.SetRegistration(registered.DeviceId, registered.ProfileId, app.Paths.Identity);
        RecoveryInput.Clear();
        Status.Text = "Profil übernommen. Der alte PC ist jetzt abgemeldet.";
        await app.SyncNowAsync();
    });

    private async void OnExport(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        using var api = app.CreateClient();
        var json = await api.ExportAsync(CancellationToken.None);
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "cod-stats-export.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) == true)
        {
            await File.WriteAllTextAsync(dialog.FileName, json);
            Status.Text = "Export gespeichert: " + dialog.FileName;
        }
    });

    private async void OnDelete(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (MessageBox.Show(this, "Profil, alle Matches und den Namensverlauf auf dem Server und auf diesem PC endgültig löschen?",
                "Alle Daten löschen", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        if (app.Identity.IsRegistered)
        {
            using var api = app.CreateClient();
            await api.DeleteAllDataAsync(CancellationToken.None);
        }
        app.Store.Clear();
        app.Settings.PublishProfile = false;
        app.SaveSettings();
        app.ResetIdentity();
        Publish.IsChecked = false;
        Status.Text = "Alles gelöscht. Beim nächsten Match legt der Client ein neues, privates Profil an.";
        app.NotifyDataChanged();
    });

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception error) when (error is ApiException or System.Net.Http.HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            Status.Text = "Das hat nicht geklappt: " + error.Message;
        }
    }
}
