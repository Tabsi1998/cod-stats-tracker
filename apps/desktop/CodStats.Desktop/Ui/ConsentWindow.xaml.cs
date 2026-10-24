using System.Windows;

namespace CodStats.Desktop.Ui;

public partial class ConsentWindow : Window
{
    private readonly Settings settings;

    public ConsentWindow(Settings settings)
    {
        this.settings = settings;
        InitializeComponent();
        Server.Text = settings.ServerUrl;
        Publish.IsChecked = settings.PublishProfile;
        Autostart.IsChecked = settings.StartWithWindows;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Capture.IsChecked != true)
        {
            ShowError("Ohne Zustimmung zur Erfassung kann der Client nichts tun.");
            return;
        }
        settings.ServerUrl = Server.Text.Trim();
        if (settings.ServerUri is null)
        {
            ShowError("Die Serveradresse muss mit https:// beginnen, z. B. https://codstats.tabelander.co.at");
            return;
        }
        settings.PublishProfile = Publish.IsChecked == true;
        settings.StartWithWindows = Autostart.IsChecked == true;
        settings.ConsentGivenAt = DateTimeOffset.Now;
        DialogResult = true;
    }

    private void ShowError(string text)
    {
        Error.Text = text;
        Error.Visibility = Visibility.Visible;
    }
}
