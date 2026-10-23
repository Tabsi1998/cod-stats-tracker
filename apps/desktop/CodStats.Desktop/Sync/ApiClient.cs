using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using CodStats.Contracts;
using CodStats.Desktop.Identity;

namespace CodStats.Desktop.Sync;

/// <summary>The device's private connection to the server: only /devices and /me, all signed.</summary>
public sealed class ApiClient : IDisposable
{
    private readonly HttpClient http;

    public ApiClient(Uri server, DeviceIdentity identity, HttpMessageHandler? transport = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var signing = new SigningHandler(identity.Key, () => identity.DeviceId) { InnerHandler = transport ?? new HttpClientHandler() };
        http = new HttpClient(signing) { BaseAddress = new Uri(server, "api/v1/"), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("CodStats/" + typeof(ApiClient).Assembly.GetName().Version?.ToString(3));
    }

    public Task<RegisterDeviceResponse> RegisterAsync(string publicKey, CancellationToken cancel) =>
        Send<RegisterDeviceResponse>(HttpMethod.Post, "devices/register", new RegisterDeviceRequest(publicKey, Version), cancel);

    public Task<RegisterDeviceResponse> RecoverAsync(string recoveryKey, string publicKey, CancellationToken cancel) =>
        Send<RegisterDeviceResponse>(HttpMethod.Post, "devices/recover", new RecoverDeviceRequest(recoveryKey, publicKey, Version), cancel);

    public Task<MeResponse> MeAsync(CancellationToken cancel) => Send<MeResponse>(HttpMethod.Get, "me", null, cancel);

    public Task<MeResponse> SetDisplayNameAsync(string name, NameStatus status, CancellationToken cancel) =>
        Send<MeResponse>(HttpMethod.Put, "me/display-name", new SetDisplayNameRequest(name, status), cancel);

    public Task<MeResponse> SetPrivacyAsync(bool isPublic, CancellationToken cancel) =>
        Send<MeResponse>(HttpMethod.Patch, "me/privacy", new PrivacyRequest(isPublic), cancel);

    public Task<MatchBatchResponse> UploadAsync(IReadOnlyList<MatchUpload> matches, CancellationToken cancel) =>
        Send<MatchBatchResponse>(HttpMethod.Post, "me/matches/batch", new MatchBatchRequest(matches), cancel);

    public Task<OwnMatch> CorrectAsync(Guid id, MatchCorrectionRequest correction, CancellationToken cancel) =>
        Send<OwnMatch>(HttpMethod.Patch, $"me/matches/{id}", correction, cancel);

    public Task<RecoveryKeyResponse> CreateRecoveryKeyAsync(CancellationToken cancel) =>
        Send<RecoveryKeyResponse>(HttpMethod.Post, "me/recovery-key", null, cancel);

    public async Task<string> ExportAsync(CancellationToken cancel)
    {
        using var response = await SendRaw(HttpMethod.Get, "me/export", null, cancel);
        return await response.Content.ReadAsStringAsync(cancel);
    }

    public async Task DeleteAllDataAsync(CancellationToken cancel)
    {
        using var response = await SendRaw(HttpMethod.Delete, "me/data", null, cancel);
    }

    private static string Version => typeof(ApiClient).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken cancel)
    {
        using var response = await SendRaw(method, path, body, cancel);
        return await response.Content.ReadFromJsonAsync<T>(cancel) ?? throw new ApiException(response.StatusCode, "empty response");
    }

    private async Task<HttpResponseMessage> SendRaw(HttpMethod method, string path, object? body, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType());
        }
        var response = await http.SendAsync(request, cancel);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancel);
            response.Dispose();
            throw new ApiException(response.StatusCode, detail);
        }
        return response;
    }

    public void Dispose() => http.Dispose();
}

public sealed class ApiException(HttpStatusCode status, string detail) : Exception($"{(int)status}: {detail}")
{
    public HttpStatusCode Status { get; } = status;
}
