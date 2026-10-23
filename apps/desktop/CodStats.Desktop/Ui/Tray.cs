using System.Windows.Forms;
using CodStats.Desktop.Recognition;
using CodStats.Desktop.Sync;

namespace CodStats.Desktop.Ui;

/// <summary>The tray icon: the only thing visible while playing. It never pops up on its own.</summary>
public sealed class Tray : IDisposable
{
    private readonly App app;
    private readonly NotifyIcon icon;
    private readonly ToolStripMenuItem trackerItem = new() { Enabled = false };
    private readonly ToolStripMenuItem syncItem = new() { Enabled = false };
    private readonly ToolStripMenuItem trackingToggle = new("Tracking aktiv") { CheckOnClick = true };

    public Tray(App app)
    {
        this.app = app;
        var menu = new ContextMenuStrip();
        menu.Items.Add("Dashboard öffnen", null, (_, _) => app.ShowDashboard());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trackerItem);
        menu.Items.Add(syncItem);
        menu.Items.Add("Jetzt synchronisieren", null, async (_, _) => await app.SyncNowAsync());
        menu.Items.Add(new ToolStripSeparator());
        trackingToggle.Checked = app.Settings.TrackingEnabled;
        trackingToggle.CheckedChanged += (_, _) =>
        {
            app.Settings.TrackingEnabled = trackingToggle.Checked;
            app.SaveSettings();
        };
        menu.Items.Add(trackingToggle);
        menu.Items.Add("Einstellungen", null, (_, _) => app.ShowSettings());
        menu.Items.Add("Beenden", null, (_, _) => app.Quit());

        using var stream = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream;
        icon = new NotifyIcon
        {
            Icon = new System.Drawing.Icon(stream),
            ContextMenuStrip = menu,
            Visible = true,
        };
        icon.DoubleClick += (_, _) => app.ShowDashboard();
        ShowTracker(app.Tracker);
        ShowSync(app.Sync.Status);
    }

    public void ShowTracker(TrackerState state)
    {
        trackerItem.Text = TrackerStateText.Of(state);
        trackingToggle.Checked = app.Settings.TrackingEnabled;
        UpdateTooltip();
    }

    public void ShowSync(SyncStatus status)
    {
        syncItem.Text = status.Text;
        UpdateTooltip();
    }

    private void UpdateTooltip()
    {
        // NotifyIcon allows at most 127 characters.
        var text = $"COD Stats · {trackerItem.Text} · {syncItem.Text}";
        icon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
