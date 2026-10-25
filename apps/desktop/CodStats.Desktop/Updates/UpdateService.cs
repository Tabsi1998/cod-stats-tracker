using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace CodStats.Desktop.Updates;

/// <summary>
/// Looks on the server for a newer client and fetches its installer. It never installs on its own: the player
/// decides (dashboard or tray), and only a signed manifest and an installer with the signed SHA-256 count.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string ManifestFile = "CodStats-update.json";

    /// <summary>The installer is about 60 MB; anything far larger is not ours and must not fill the disk.</summary>
    private const long MaxInstallerBytes = 300L * 1024 * 1024;

    private readonly HttpClient http;
    private readonly string publicKey;
    private readonly Version current;
    private readonly string folder;

    public UpdateService(Uri server, string publicKey, Version current, string folder, HttpMessageHandler? transport = null)
    {
        ArgumentNullException.ThrowIfNull(server);
        this.publicKey = publicKey;
        this.current = current;
        this.folder = folder;
        http = new HttpClient(transport ?? new HttpClientHandler()) { BaseAddress = new Uri(server, "downloads/"), Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CodStats/" + current.ToString(3));
    }

    /// <summary>False in builds without a release key: then there is nothing to trust and the check is skipped.</summary>
    public bool Enabled => publicKey.Length > 0;

    /// <summary>The newer release found by the last check, or null.</summary>
    public UpdateManifest? Available { get; private set; }

    /// <summary>
    /// Asks the server for the signed manifest. Null when there is none, it is not newer, or it does not verify
    /// (a bad signature is logged, never shown as an update).
    /// </summary>
    public async Task<UpdateManifest?> CheckAsync(CancellationToken cancel)
    {
        if (!Enabled)
        {
            return null;
        }
        var manifest = await GetBytes(ManifestFile, cancel);
        var signature = await GetBytes(ManifestFile + ".sig", cancel);
        if (manifest is null || signature is null)
        {
            Available = null;
            return null;
        }
        var verified = UpdateVerifier.Verify(manifest, System.Text.Encoding.ASCII.GetString(signature), publicKey);
        if (verified is null)
        {
            Diagnostics.Log("update: manifest on the server does not verify, ignored");
        }
        Available = verified is not null && UpdateVerifier.IsNewer(verified, current) ? verified : null;
        return Available;
    }

    /// <summary>
    /// Downloads the installer into the updates folder and returns its path once its SHA-256 matches the manifest.
    /// Anything else is deleted and reported as <see cref="UpdateException"/>.
    /// </summary>
    public async Task<string> DownloadAsync(UpdateManifest manifest, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(folder);
        // Installers of earlier updates are not needed any more.
        foreach (var old in Directory.EnumerateFiles(folder))
        {
            TryDelete(old);
        }
        var target = Path.Combine(folder, $"CodStats-Setup-{manifest.Version}.exe");
        var partial = target + ".part";
        try
        {
            using var response = await http.GetAsync(manifest.Installer, HttpCompletionOption.ResponseHeadersRead, cancel);
            if (!response.IsSuccessStatusCode)
            {
                throw new UpdateException($"Download fehlgeschlagen ({(int)response.StatusCode}).");
            }
            if (response.Content.Headers.ContentLength > MaxInstallerBytes)
            {
                throw new UpdateException("Die Update-Datei ist unerwartet groß.");
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancel))
            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    total += read;
                    if (total > MaxInstallerBytes)
                    {
                        throw new UpdateException("Die Update-Datei ist unerwartet groß.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancel);
                }
            }
            if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new UpdateException("Die Update-Datei passt nicht zur Prüfsumme und wurde verworfen.");
            }
            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException)
        {
            throw new UpdateException("Download fehlgeschlagen: " + error.Message, error);
        }
        finally
        {
            TryDelete(partial);
        }
    }

    private async Task<byte[]?> GetBytes(string name, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync(name, cancel);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            // Manifest and signature are a few hundred bytes.
            if (response.Content.Headers.ContentLength > 64 * 1024)
            {
                return null;
            }
            return await response.Content.ReadAsByteArrayAsync(cancel);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A file still open by a running installer stays; the next update clears it.
        }
    }

    public void Dispose() => http.Dispose();
}

public sealed class UpdateException : Exception
{
    public UpdateException()
    {
    }

    public UpdateException(string message) : base(message)
    {
    }

    public UpdateException(string message, Exception inner) : base(message, inner)
    {
    }
}
