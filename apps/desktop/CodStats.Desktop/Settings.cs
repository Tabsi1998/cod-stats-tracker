using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CodStats.Desktop;

public sealed class Settings
{
    /// <summary>
    /// The server, fixed in code: players never type an address. A self-hosted server is a change here and a new
    /// build. CODSTATS_SERVER overrides it for development against a local stack only.
    /// </summary>
    public const string Server = "https://codstats.tabelander.co.at";

    /// <summary>When the player agreed to screen recognition and upload. Nothing runs before.</summary>
    public DateTimeOffset? ConsentGivenAt { get; set; }

    public bool TrackingEnabled { get; set; } = true;
    public bool PublishProfile { get; set; }
    public bool StartWithWindows { get; set; } = true;

    /// <summary>Look for a newer client on the server. Installing always waits for the player.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>The CoD display name, recognised or entered once by the player.</summary>
    public string? DisplayName { get; set; }

    /// <summary>True only when the player confirmed the name. Unconfirmed names are never published.</summary>
    public bool NameConfirmed { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load(AppPaths paths)
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(paths.Settings)) ?? new Settings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save(AppPaths paths)
    {
        paths.Ensure();
        var temp = paths.Settings + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, paths.Settings, overwrite: true);
    }

    /// <summary>The server's address with a trailing slash.</summary>
    public static Uri ServerUri =>
        Environment.GetEnvironmentVariable("CODSTATS_SERVER") is { Length: > 0 } development
        && Uri.TryCreate(development.TrimEnd('/') + "/", UriKind.Absolute, out var local) && local.Scheme is "https" or "http"
            ? local
            : new Uri(Server + "/");
}

/// <summary>Start with Windows through the current user's Run key; no admin rights, no scheduled task.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "CodStats";

    public static void Apply(bool enabled)
    {
        // A second client on a test data folder (CODSTATS_DATA) must not touch the real client's autostart entry.
        if (Environment.GetEnvironmentVariable("CODSTATS_DATA") is { Length: > 0 })
        {
            return;
        }
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
        {
            key.SetValue(Name, $"\"{exe}\" --tray");
        }
        else
        {
            key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
