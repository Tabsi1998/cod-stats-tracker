using System.Security.Cryptography;
using System.Text;
using CodStats.Api.Auth;
using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this RouteGroupBuilder api)
    {
        var devices = api.MapGroup("/devices").WithTags("Devices").RequireRateLimiting(RateLimits.Registration);
        devices.MapPost("/register", Register);
        devices.MapPost("/recover", Recover);
    }

    /// <summary>Registers a new device and creates its profile. The request must be signed with the key it registers.</summary>
    private static async Task<IResult> Register(RegisterDeviceRequest body, HttpRequest request, AppDb db,
        SignatureVerifier verifier, TimeProvider clock, CancellationToken cancel)
    {
        var key = ParseKey(body.PublicKey);
        if (key is null)
        {
            return Results.Problem(detail: "publicKey must be an ECDSA P-256 SubjectPublicKeyInfo in Base64", statusCode: 400);
        }
        var problem = await verifier.VerifyAsync(request, key, SignatureVerifier.KeySigner(key), cancel);
        if (problem is not null)
        {
            return Results.Problem(detail: problem, statusCode: 401);
        }
        if (await db.Devices.AnyAsync(d => d.PublicKey == key, cancel))
        {
            return Results.Problem(detail: "this key is already registered", statusCode: 409);
        }

        var now = clock.GetUtcNow();
        var profile = new Profile { Id = Guid.CreateVersion7(), NameStatus = NameStatus.Unknown, CreatedAt = now, UpdatedAt = now };
        var device = new Device { Id = Guid.CreateVersion7(), Profile = profile, PublicKey = key, ClientVersion = Short(body.ClientVersion), CreatedAt = now, LastSeenAt = now };
        db.Add(device);
        await db.SaveChangesAsync(cancel);
        return Results.Ok(new RegisterDeviceResponse(device.Id, profile.Id));
    }

    /// <summary>Moves a profile to a new PC with its recovery key. All other devices of the profile are revoked.</summary>
    private static async Task<IResult> Recover(RecoverDeviceRequest body, HttpRequest request, AppDb db,
        SignatureVerifier verifier, TimeProvider clock, CancellationToken cancel)
    {
        var key = ParseKey(body.PublicKey);
        if (key is null)
        {
            return Results.Problem(detail: "publicKey must be an ECDSA P-256 SubjectPublicKeyInfo in Base64", statusCode: 400);
        }
        var problem = await verifier.VerifyAsync(request, key, SignatureVerifier.KeySigner(key), cancel);
        if (problem is not null)
        {
            return Results.Problem(detail: problem, statusCode: 401);
        }

        var hash = RecoveryKeys.Hash(body.RecoveryKey);
        var profile = hash is null ? null : await db.Profiles.SingleOrDefaultAsync(p => p.RecoveryKeyHash == hash, cancel);
        if (profile is null)
        {
            return Results.Problem(detail: "recovery key not valid", statusCode: 404);
        }
        if (await db.Devices.AnyAsync(d => d.PublicKey == key, cancel))
        {
            return Results.Problem(detail: "this key is already registered", statusCode: 409);
        }

        var now = clock.GetUtcNow();
        await db.Devices.Where(d => d.ProfileId == profile.Id && d.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RevokedAt, now), cancel);
        profile.RecoveryKeyHash = null;
        profile.UpdatedAt = now;
        var device = new Device { Id = Guid.CreateVersion7(), ProfileId = profile.Id, PublicKey = key, ClientVersion = Short(body.ClientVersion), CreatedAt = now, LastSeenAt = now };
        db.Add(device);
        await db.SaveChangesAsync(cancel);
        return Results.Ok(new RegisterDeviceResponse(device.Id, profile.Id));
    }

    private static byte[]? ParseKey(string? base64)
    {
        try
        {
            var bytes = Convert.FromBase64String(base64 ?? "");
            using var key = RequestSigning.ImportPublicKey(bytes);
            return key is null ? null : bytes;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Short(string? version) => version is { Length: > 0 and <= 32 } ? version : null;
}

/// <summary>Recovery keys: 256 random bits, shown as Base32 groups, stored only as SHA-256.</summary>
public static class RecoveryKeys
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var text = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                text.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0)
        {
            text.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }
        return string.Join('-', text.ToString().Chunk(4).Select(c => new string(c)));
    }

    public static byte[]? Hash(string? key)
    {
        var normalised = new string((key ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return normalised.Length == 52 ? SHA256.HashData(Encoding.ASCII.GetBytes(normalised)) : null;
    }
}
