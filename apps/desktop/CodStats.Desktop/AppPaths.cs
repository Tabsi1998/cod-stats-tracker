using System.IO;

namespace CodStats.Desktop;

/// <summary>Where the client keeps its files: %LOCALAPPDATA%\CodStats, nothing elsewhere.</summary>
public sealed record AppPaths(string Root)
{
    /// <summary>%LOCALAPPDATA%\CodStats, or CODSTATS_DATA for tests and a second profile during development.</summary>
    public static AppPaths Default { get; } =
        new(Environment.GetEnvironmentVariable("CODSTATS_DATA") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodStats"));

    public string Settings => Path.Combine(Root, "settings.json");
    public string Identity => Path.Combine(Root, "identity.json");
    public string Database => Path.Combine(Root, "matches.db");

    /// <summary>Recognition profiles the player or an update placed here replace the built-in ones.</summary>
    public string Profiles => Path.Combine(Root, "profiles");

    /// <summary>Downloaded installers of client updates; emptied before each new download.</summary>
    public string Updates => Path.Combine(Root, "updates");

    public void Ensure() => Directory.CreateDirectory(Root);
}
