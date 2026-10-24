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
        Autostart.IsChecked = settings.StartWithWindows;
    }

    /// <summary>
    /// Public is the default: the highlighted button and the Enter key. It is still an explicit choice, never a
    /// pre-ticked box, because a pre-ticked box is no consent.
    /// </summary>
    private void OnAcceptPublic(object sender, RoutedEventArgs e) => Finish(publish: true);

    private void OnAcceptPrivate(object sender, RoutedEventArgs e) => Finish(publish: false);

    private void Finish(bool publish)
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
        settings.PublishProfile = publish;
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
