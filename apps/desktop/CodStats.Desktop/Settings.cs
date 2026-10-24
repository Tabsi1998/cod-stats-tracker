using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CodStats.Desktop;

public sealed class Settings
{
    /// <summary>The project's own server. Self-hosters change it in the setup window or the settings.</summary>
    public string ServerUrl { get; set; } = "https://codstats.tabelander.co.at";

    /// <summary>When the player agreed to screen recognition and upload. Nothing runs before.</summary>
    public DateTimeOffset? ConsentGivenAt { get; set; }

    public bool TrackingEnabled { get; set; } = true;
    public bool PublishProfile { get; set; }
    public bool StartWithWindows { get; set; } = true;

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

    /// <summary>The server address as a URI, or null when it is not an http(s) address.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri? ServerUri =>
        Uri.TryCreate(ServerUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" ? uri : null;
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
