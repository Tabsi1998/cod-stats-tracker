using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using CodStats.Contracts;

namespace CodStats.Api.Tests;

[Collection(ApiTestGroup.Name)]
public class DeviceIdentityTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    [Fact]
    public async Task Registration_creates_a_private_profile_without_name()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var me = await device.Http.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.Equal(device.ProfileId, me!.ProfileId);
        Assert.Equal(NameStatus.Unknown, me.NameStatus);
        Assert.False(me.IsPublic);
        Assert.False(me.IsVisible);
    }

    [Fact]
    public async Task Registration_must_be_signed_with_the_registered_key()
    {
        using var signer = await TestDevice.RegisterAsync(Api);
        using var other = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        // Signed by "signer", but registers another key: no proof of possession.
        var response = await signer.Http.PostAsJsonAsync("/api/v1/devices/register",
            new RegisterDeviceRequest(Convert.ToBase64String(other.ExportSubjectPublicKeyInfo()), "test"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unsigned_requests_are_rejected()
    {
        var response = await Api.Anonymous().GetAsync("/api/v1/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_device_id_is_rejected()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var client = Api.CreateDefaultClient(new SigningHandler(device.Key, () => Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_replayed_request_is_rejected()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var recorder = new RecordingHandler();
        var client = Api.CreateDefaultClient(new SigningHandler(device.Key, () => device.DeviceId), recorder);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);

        using var replay = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        foreach (var header in recorder.Last!.Headers)
        {
            replay.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await Api.Anonymous().SendAsync(replay)).StatusCode);
    }

    [Fact]
    public async Task A_stale_timestamp_is_rejected()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var past = new FixedClock(DateTimeOffset.UtcNow.AddMinutes(-10));
        var client = Api.CreateDefaultClient(new SigningHandler(device.Key, () => device.DeviceId, past));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task A_tampered_body_is_rejected()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var tamper = new TamperingHandler();
        var client = Api.CreateDefaultClient(new SigningHandler(device.Key, () => device.DeviceId), tamper);
        var response = await client.PatchAsJsonAsync("/api/v1/me/privacy", new PrivacyRequest(false));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var me = await device.Http.GetFromJsonAsync<MeResponse>("/api/v1/me");
        Assert.False(me!.IsPublic);
    }

    [Fact]
    public async Task Recovery_moves_the_profile_and_revokes_the_old_device()
    {
        using var old = await TestDevice.RegisterAsync(Api);
        await old.UploadAsync(Matches.Mw4(5, 5));
        var key = (await (await old.Http.PostAsync("/api/v1/me/recovery-key", null)).Content.ReadFromJsonAsync<RecoveryKeyResponse>())!.RecoveryKey;

        var (moved, response) = await TestDevice.RecoverAsync(Api, key.ToLowerInvariant());
        using var _ = moved;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(old.ProfileId, moved.ProfileId);
        Assert.Equal(1, (await moved.Http.GetFromJsonAsync<SyncStatusResponse>("/api/v1/me/sync-status"))!.Matches);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.Http.GetAsync("/api/v1/me")).StatusCode);

        var (again, second) = await TestDevice.RecoverAsync(Api, key);
        using var __ = again;
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    }

    [Fact]
    public async Task Registration_is_rate_limited()
    {
        await using var strict = new ApiFactory(fixture.ConnectionString, registrationsPerHour: 2);
        using var a = await TestDevice.RegisterAsync(strict);
        using var b = await TestDevice.RegisterAsync(strict);
        await Assert.ThrowsAsync<HttpRequestException>(() => TestDevice.RegisterAsync(strict));
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>Changes the body after it was signed, like a man in the middle would.</summary>
    private sealed class TamperingHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.True(request.Headers.Contains(RequestSigning.SignatureHeader));
            request.Content = new StringContent("{\"isPublic\":true}", Encoding.UTF8, "application/json");
            return await base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
