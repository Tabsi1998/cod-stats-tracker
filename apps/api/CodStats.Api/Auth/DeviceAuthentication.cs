using CodStats.Api.Data;
using CodStats.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CodStats.Api.Auth;

/// <summary>The device that signed the current request, and its profile. Nothing else identifies a caller.</summary>
public sealed record CurrentDevice(Guid DeviceId, Guid ProfileId)
{
    private const string ItemKey = nameof(CurrentDevice);

    public static CurrentDevice From(HttpContext context) =>
        context.Items[ItemKey] as CurrentDevice
        ?? throw new InvalidOperationException("endpoint is not behind device authentication");

    internal void Attach(HttpContext context) => context.Items[ItemKey] = this;

    public static ValueTask<CurrentDevice?> BindAsync(HttpContext context) => ValueTask.FromResult<CurrentDevice?>(From(context));
}

/// <summary>
/// Rejects every request under /api/v1/me that is not signed by a known, active device. It runs as middleware,
/// before model binding, so the endpoints can bind <see cref="CurrentDevice"/> as a parameter.
/// </summary>
public sealed class DeviceAuthentication(RequestDelegate next)
{
    public const string Prefix = "/api/v1/me";
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext http, SignatureVerifier verifier, AppDb db, TimeProvider clock, ILogger<DeviceAuthentication> logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        var cancel = http.RequestAborted;

        if (!Guid.TryParse(http.Request.Headers[RequestSigning.DeviceIdHeader], out var deviceId))
        {
            await Unauthorized(http, "device id missing");
            return;
        }

        var device = await db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId && d.RevokedAt == null)
            .Select(d => new { d.Id, d.ProfileId, d.PublicKey, d.LastSeenAt })
            .SingleOrDefaultAsync(cancel);
        if (device is null)
        {
            await Unauthorized(http, "device unknown or revoked");
            return;
        }

        var problem = await verifier.VerifyAsync(http.Request, device.PublicKey, device.Id.ToString(), cancel);
        if (problem is not null)
        {
            logger.LogInformation("Rejected request of device {DeviceId}: {Problem}", device.Id, problem);
            await Unauthorized(http, problem);
            return;
        }

        var now = clock.GetUtcNow();
        if (device.LastSeenAt is null || now - device.LastSeenAt > LastSeenResolution)
        {
            await db.Devices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, now), cancel);
        }

        new CurrentDevice(device.Id, device.ProfileId).Attach(http);
        await next(http);
    }

    private static Task Unauthorized(HttpContext http, string detail) =>
        Results.Problem(title: "Unauthorized", detail: detail, statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(http);
}
