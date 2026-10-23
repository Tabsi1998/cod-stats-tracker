using System.Globalization;
using System.Security.Cryptography;
using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CodStats.Api.Auth;

/// <summary>Checks the signature headers of a request: timestamp window, single-use nonce, ECDSA signature.</summary>
public sealed class SignatureVerifier(AppDb db, TimeProvider clock)
{
    public const int MaxBodyBytes = 256 * 1024;

    /// <summary>Null when the request is signed correctly by <paramref name="publicKey"/>, otherwise why not.</summary>
    public async Task<string?> VerifyAsync(HttpRequest request, byte[] publicKey, string signer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var headers = request.Headers;
        string? timestampText = headers[RequestSigning.TimestampHeader];
        string? nonce = headers[RequestSigning.NonceHeader];
        string? signature = headers[RequestSigning.SignatureHeader];
        if (timestampText is null || nonce is null || signature is null)
        {
            return "signature headers missing";
        }
        if (!long.TryParse(timestampText, NumberStyles.None, CultureInfo.InvariantCulture, out var timestamp))
        {
            return "timestamp invalid";
        }
        if (nonce.Length is < RequestSigning.MinNonceLength or > RequestSigning.MaxNonceLength || !nonce.All(char.IsAsciiLetterOrDigit))
        {
            return "nonce invalid";
        }

        var now = clock.GetUtcNow();
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Clamp(timestamp, 0, 253402300799));
        if ((now - signedAt).Duration() > RequestSigning.AllowedClockSkew)
        {
            return "timestamp outside the allowed window; check the PC clock";
        }

        var body = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            return "body too large";
        }

        var pathAndQuery = request.PathBase + request.Path + request.QueryString;
        if (!RequestSigning.Verify(publicKey, signature, request.Method, pathAndQuery, timestamp, nonce, body))
        {
            return "signature invalid";
        }

        // The nonce is spent only after the signature proved the request genuine,
        // so nobody can burn another device's nonces.
        db.UsedNonces.Add(new UsedNonce
        {
            Signer = signer,
            Nonce = nonce,
            ExpiresAt = signedAt + RequestSigning.AllowedClockSkew + TimeSpan.FromMinutes(1),
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return "nonce already used";
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
        return null;
    }

    /// <summary>A signer name for a key that has no device yet (registration, recovery).</summary>
    public static string KeySigner(byte[] publicKey) => "key:" + Convert.ToHexStringLower(SHA256.HashData(publicKey));

    private static async Task<byte[]?> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering(MaxBodyBytes, MaxBodyBytes + 1);
        request.Body.Position = 0;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
        request.Body.Position = 0;
        return buffer.ToArray();
    }
}
