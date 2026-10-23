using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CodStats.Contracts;

/// <summary>
/// The request signature shared by client and server. See ARCHITECTURE.md, "Geräteidentität".
/// Changing anything here breaks every installed client: introduce a new scheme instead.
/// </summary>
public static class RequestSigning
{
    public const string Scheme = "COD-STATS-V1";
    public const string DeviceIdHeader = "X-Device-Id";
    public const string TimestampHeader = "X-Timestamp";
    public const string NonceHeader = "X-Nonce";
    public const string SignatureHeader = "X-Signature";

    public const int MinNonceLength = 16;
    public const int MaxNonceLength = 64;
    public static readonly TimeSpan AllowedClockSkew = TimeSpan.FromMinutes(5);

    public static string Canonical(string method, string pathAndQuery, long timestamp, string nonce, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(method);
        var bodyHash = Convert.ToHexStringLower(SHA256.HashData(body));
        return string.Join('\n',
            Scheme,
            method.ToUpperInvariant(),
            pathAndQuery,
            timestamp.ToString(CultureInfo.InvariantCulture),
            nonce,
            bodyHash);
    }

    public static string Sign(ECDsa key, string method, string pathAndQuery, long timestamp, string nonce, ReadOnlySpan<byte> body)
    {
        ArgumentNullException.ThrowIfNull(key);
        var data = Encoding.UTF8.GetBytes(Canonical(method, pathAndQuery, timestamp, nonce, body));
        return Convert.ToBase64String(key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    public static bool Verify(byte[] publicKey, string signature, string method, string pathAndQuery, long timestamp, string nonce, ReadOnlySpan<byte> body)
    {
        byte[] signatureBytes;
        try
        {
            signatureBytes = Convert.FromBase64String(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        using var key = ImportPublicKey(publicKey);
        if (key is null)
        {
            return false;
        }

        var data = Encoding.UTF8.GetBytes(Canonical(method, pathAndQuery, timestamp, nonce, body));
        return key.VerifyData(data, signatureBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>An ECDSA P-256 key from SubjectPublicKeyInfo, or null when it is anything else.</summary>
    public static ECDsa? ImportPublicKey(byte[] subjectPublicKeyInfo)
    {
        var key = ECDsa.Create();
        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var read);
            if (read != subjectPublicKeyInfo.Length || key.KeySize != 256
                || key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            {
                key.Dispose();
                return null;
            }
            return key;
        }
        catch (CryptographicException)
        {
            key.Dispose();
            return null;
        }
    }

    public static string NewNonce() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}

/// <summary>Signs every outgoing request with the device key. Used by the Windows client and the tests.</summary>
public sealed class SigningHandler(ECDsa key, Func<Guid?> deviceId, TimeProvider? clock = null) : DelegatingHandler
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds();
        var nonce = RequestSigning.NewNonce();
        var pathAndQuery = request.RequestUri!.PathAndQuery;

        request.Headers.Remove(RequestSigning.DeviceIdHeader);
        if (deviceId() is { } id)
        {
            request.Headers.Add(RequestSigning.DeviceIdHeader, id.ToString());
        }
        request.Headers.Add(RequestSigning.TimestampHeader, timestamp.ToString(CultureInfo.InvariantCulture));
        request.Headers.Add(RequestSigning.NonceHeader, nonce);
        request.Headers.Add(RequestSigning.SignatureHeader,
            RequestSigning.Sign(key, request.Method.Method, pathAndQuery, timestamp, nonce, body));
        return await base.SendAsync(request, cancellationToken);
    }
}
