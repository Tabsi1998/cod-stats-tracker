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

/// <summary>Rejects every request under /api/v1/me that is not signed by a known, active device.</summary>
public sealed class DeviceAuthenticationFilter(SignatureVerifier verifier, AppDb db, TimeProvider clock, ILogger<DeviceAuthenticationFilter> logger) : IEndpointFilter
{
    private static readonly TimeSpan LastSeenResolution = TimeSpan.FromMinutes(5);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var cancel = http.RequestAborted;

        if (!Guid.TryParse(http.Request.Headers[RequestSigning.DeviceIdHeader], out var deviceId))
        {
            return Unauthorized("device id missing");
        }

        var device = await db.Devices.AsNoTracking()
            .Where(d => d.Id == deviceId && d.RevokedAt == null)
            .Select(d => new { d.Id, d.ProfileId, d.PublicKey, d.LastSeenAt })
            .SingleOrDefaultAsync(cancel);
        if (device is null)
        {
            return Unauthorized("device unknown or revoked");
        }

        var problem = await verifier.VerifyAsync(http.Request, device.PublicKey, device.Id.ToString(), cancel);
        if (problem is not null)
        {
            logger.LogInformation("Rejected request of device {DeviceId}: {Problem}", device.Id, problem);
            return Unauthorized(problem);
        }

        var now = clock.GetUtcNow();
        if (device.LastSeenAt is null || now - device.LastSeenAt > LastSeenResolution)
        {
            await db.Devices.Where(d => d.Id == device.Id).ExecuteUpdateAsync(s => s.SetProperty(d => d.LastSeenAt, now), cancel);
        }

        new CurrentDevice(device.Id, device.ProfileId).Attach(http);
        return await next(context);
    }

    private static IResult Unauthorized(string detail) =>
        Results.Problem(title: "Unauthorized", detail: detail, statusCode: StatusCodes.Status401Unauthorized);
}
