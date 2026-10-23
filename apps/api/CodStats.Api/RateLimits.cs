using System.Threading.RateLimiting;
using CodStats.Contracts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CodStats.Api;

public sealed class RateLimitOptions
{
    public int RegistrationsPerHour { get; set; } = 10;
    public int DeviceRequestsPerMinute { get; set; } = 120;
    public int PublicRequestsPerMinute { get; set; } = 300;
}

public static class RateLimits
{
    public const string Registration = "registration";
    public const string Device = "device";
    public const string Public = "public";

    public static IServiceCollection AddCodStatsRateLimits(this IServiceCollection services)
    {
        // Limits are read per request, so configuration (and the tests' overrides) apply without a restart.
        static RateLimitOptions Limits(HttpContext context) =>
            context.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitOptions>>().CurrentValue;

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Registration, context => RateLimitPartition.GetFixedWindowLimiter(
                Ip(context), _ => Window(Limits(context).RegistrationsPerHour, TimeSpan.FromHours(1))));
            // The device id header is not verified yet at this point; a forged one only spends the forger's own budget.
            options.AddPolicy(Device, context => RateLimitPartition.GetFixedWindowLimiter(
                context.Request.Headers[RequestSigning.DeviceIdHeader].ToString() is { Length: > 0 } id ? "d:" + id : Ip(context),
                _ => Window(Limits(context).DeviceRequestsPerMinute, TimeSpan.FromMinutes(1))));
            options.AddPolicy(Public, context => RateLimitPartition.GetFixedWindowLimiter(
                Ip(context), _ => Window(Limits(context).PublicRequestsPerMinute, TimeSpan.FromMinutes(1))));
        });
    }

    private static string Ip(HttpContext context) => "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");

    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) =>
        new() { PermitLimit = permits, Window = window, QueueLimit = 0 };
}
