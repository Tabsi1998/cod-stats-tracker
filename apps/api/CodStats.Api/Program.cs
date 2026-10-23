using CodStats.Api;
using CodStats.Api.Auth;
using CodStats.Api.Data;
using CodStats.Api.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = SignatureVerifier.MaxBodyBytes);

builder.Services.AddDbContext<AppDb>(options => options
    .UseNpgsql(config.GetConnectionString("Database")
        ?? throw new InvalidOperationException("ConnectionStrings:Database is not set"))
    .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SignatureVerifier>();
builder.Services.AddScoped<DeviceAuthenticationFilter>();
builder.Services.AddHostedService<NonceCleanup>();
builder.Services.Configure<LeaderboardOptions>(config.GetSection("Leaderboards"));
builder.Services.AddCodStatsRateLimits(config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions());
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Behind Caddy in Docker the client address arrives in X-Forwarded-For.
var trustForwarded = config.GetValue<bool>("TrustForwardedHeaders");
if (trustForwarded)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<AppDb>().Database.MigrateAsync();
}

if (trustForwarded)
{
    app.UseForwardedHeaders();
}
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.MapOpenApi();
app.MapGet("/health", async (AppDb db, CancellationToken cancel) =>
    await db.Database.CanConnectAsync(cancel) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503))
    .ExcludeFromDescription();

var api = app.MapGroup("/api/v1");
api.MapDeviceEndpoints();
api.MapMeEndpoints();
api.MapPublicEndpoints();

await app.RunAsync();

/// <summary>Entry point, visible to the integration tests.</summary>
public partial class Program;
