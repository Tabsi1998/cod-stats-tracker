using System.Globalization;
using System.IO;

namespace CodStats.Desktop;

/// <summary>
/// A small text log in the data folder (client.log, at most ~1 MB plus one older file), so a player can send it
/// when recognition misbehaves. It holds states, timings and errors; never pixels, never other players' names.
/// </summary>
public static class Diagnostics
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();
    private static string? path;

    public static void Initialise(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        path = Path.Combine(paths.Root, "client.log");
    }

    public static void Log(string message)
    {
        if (path is null)
        {
            return;
        }
        lock (Gate)
        {
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, path + ".1", overwrite: true);
                }
                File.AppendAllText(path, $"{DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {message}{Environment.NewLine}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the client.
            }
        }
    }
}
