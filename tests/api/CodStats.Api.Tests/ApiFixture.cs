using System.Net.Http.Json;
using System.Security.Cryptography;
using CodStats.Contracts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace CodStats.Api.Tests;

/// <summary>One PostgreSQL container for all tests; every test makes its own devices, so tests do not share profiles.</summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public ApiFactory Api { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await database.StartAsync();
        Api = new ApiFactory(database.GetConnectionString(), registrationsPerHour: 10_000);
    }

    public async Task DisposeAsync()
    {
        await Api.DisposeAsync();
        await database.DisposeAsync();
    }

    public string ConnectionString => database.GetConnectionString();
}

[CollectionDefinition(Name)]
public sealed class ApiTestGroup : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}

public sealed class ApiFactory(string connectionString, int registrationsPerHour, int requestsPerAddressPerMinute = 1_000_000)
    : WebApplicationFactory<Program>
{
    public const int LeaderboardMinMatches = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = connectionString,
            ["Leaderboards:MinMatches"] = LeaderboardMinMatches.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimits:RegistrationsPerHour"] = registrationsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimits:DeviceRequestsPerMinute"] = "100000",
            ["RateLimits:PublicRequestsPerMinute"] = "100000",
            ["RateLimits:RequestsPerAddressPerMinute"] = requestsPerAddressPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));
    }

    public HttpClient Anonymous() => CreateClient();
}

/// <summary>A simulated Windows client: its own key, its own signed HttpClient.</summary>
public sealed class TestDevice : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private TestDevice(ApiFactory api)
    {
        Http = api.CreateDefaultClient(new SigningHandler(key, () => DeviceId));
    }

    public HttpClient Http { get; }
    public Guid? DeviceId { get; private set; }
    public Guid ProfileId { get; private set; }
    public string PublicKey => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());
    public ECDsa Key => key;

    public static async Task<TestDevice> RegisterAsync(ApiFactory api)
    {
        var device = new TestDevice(api);
        var response = await device.Http.PostAsJsonAsync("/api/v1/devices/register", new RegisterDeviceRequest(device.PublicKey, "test"));
        response.EnsureSuccessStatusCode();
        var registered = (await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>())!;
        device.DeviceId = registered.DeviceId;
        device.ProfileId = registered.ProfileId;
        return device;
    }

    public static async Task<(TestDevice Device, HttpResponseMessage Response)> RecoverAsync(ApiFactory api, string recoveryKey)
    {
        var device = new TestDevice(api);
        var response = await device.Http.PostAsJsonAsync("/api/v1/devices/recover", new RecoverDeviceRequest(recoveryKey, device.PublicKey, "test"));
        if (response.IsSuccessStatusCode)
        {
            var registered = (await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>())!;
            device.DeviceId = registered.DeviceId;
            device.ProfileId = registered.ProfileId;
        }
        return (device, response);
    }

    /// <summary>A device that names itself with a confirmed display name and allows publishing.</summary>
    public static async Task<TestDevice> PublicPlayerAsync(ApiFactory api, string name)
    {
        var device = await RegisterAsync(api);
        (await device.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Confirmed))).EnsureSuccessStatusCode();
        (await device.Http.PatchAsJsonAsync("/api/v1/me/privacy", new PrivacyRequest(true))).EnsureSuccessStatusCode();
        return device;
    }

    public async Task<MatchBatchResponse> UploadAsync(params MatchUpload[] matches)
    {
        var response = await Http.PostAsJsonAsync("/api/v1/me/matches/batch", new MatchBatchRequest(matches));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MatchBatchResponse>())!;
    }

    public void Dispose()
    {
        Http.Dispose();
        key.Dispose();
    }
}

public static class Matches
{
    private static int counter;

    /// <summary>Test data only: a MW4 match some minutes ago, spaced so no two count as duplicates.</summary>
    public static MatchUpload Mw4(int? kills, int? deaths, MatchResult? result = MatchResult.Win, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), Game.Mw4Multiplayer, "Team Deathmatch", at ?? Next(), kills, deaths, 3, result, null, "Test Map", 2500,
            0.97, ReviewStatus.Auto, "test");

    public static MatchUpload Warzone(int? kills, int? deaths, int placement, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), Game.WarzoneBattleRoyale, "Battle Royale Quads", at ?? Next(), kills, deaths, null, null, placement,
            null, null, 0.95, ReviewStatus.Auto, "test");

    private static DateTimeOffset Next() =>
        DateTimeOffset.UtcNow.AddMinutes(-10 * Interlocked.Increment(ref counter) % 100_000).AddSeconds(-Random.Shared.Next(1, 59));
}
