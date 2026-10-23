using System.IO;

namespace CodStats.Desktop;

/// <summary>Where the client keeps its files: %LOCALAPPDATA%\CodStats, nothing elsewhere.</summary>
public sealed record AppPaths(string Root)
{
    public static AppPaths Default { get; } =
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodStats"));

    public string Settings => Path.Combine(Root, "settings.json");
    public string Identity => Path.Combine(Root, "identity.json");
    public string Database => Path.Combine(Root, "matches.db");

    /// <summary>Recognition profiles the player or an update placed here replace the built-in ones.</summary>
    public string Profiles => Path.Combine(Root, "profiles");

    public void Ensure() => Directory.CreateDirectory(Root);
}
