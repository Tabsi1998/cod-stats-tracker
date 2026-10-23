using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace CodStats.Desktop;

public sealed class Settings
{
    public string ServerUrl { get; set; } = "https://stats.example.com";

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
