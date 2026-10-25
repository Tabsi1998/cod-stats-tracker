using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodStats.Desktop.Updates;

/// <summary>
/// What a release offers: its version and the installer with its SHA-256. The release workflow writes it as
/// CodStats-update.json and signs it; the server only passes it on (deploy/downloads).
/// </summary>
public sealed record UpdateManifest(string Version, string Installer, string Sha256)
{
    /// <summary>The version as numbers, for comparing. Only plain x.y.z releases are offered as updates.</summary>
    public Version Number => System.Version.Parse(Version);
}

/// <summary>
/// Checks a manifest before the client trusts it. The signature is what makes an update safe: with the release key
/// only in GitHub, even someone in control of the server cannot hand a different program to every player.
/// </summary>
public static partial class UpdateVerifier
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The manifest, if <paramref name="signature"/> (Base64 of a DER ECDSA P-256 SHA-256 signature, as
    /// <c>openssl dgst -sha256 -sign</c> writes it) is valid for these exact bytes and the content is sane; else null.
    /// </summary>
    public static UpdateManifest? Verify(byte[] manifest, string signature, string publicKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
            if (!key.VerifyData(manifest, Convert.FromBase64String(signature.Trim()), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            {
                return null;
            }
            var parsed = JsonSerializer.Deserialize<UpdateManifest>(manifest, Json);
            return parsed is not null && IsSane(parsed) ? parsed : null;
        }
        catch (Exception error) when (error is FormatException or CryptographicException or JsonException)
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="candidate"/> is a later release than the running client.</summary>
    public static bool IsNewer(UpdateManifest candidate, Version current)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(current);
        var running = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return candidate.Number > running;
    }

    /// <summary>Signed or not, the file name ends up in a path and a URL: a plain name only, no folders.</summary>
    private static bool IsSane(UpdateManifest manifest) =>
        manifest.Version is not null && PlainVersion().IsMatch(manifest.Version)
        && manifest.Installer is not null && InstallerName().IsMatch(manifest.Installer)
        && manifest.Sha256 is not null && Sha256Hex().IsMatch(manifest.Sha256);

    [GeneratedRegex(@"^\d{1,4}\.\d{1,4}\.\d{1,4}$")]
    private static partial Regex PlainVersion();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.exe$")]
    private static partial Regex InstallerName();

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Hex();
}
