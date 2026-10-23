// End-to-end check against a running stack (docker compose): two simulated clients register, name themselves,
// upload matches, and the public endpoints show the result. Test data only; run it against a test stack.
//
//   dotnet run --project tests/e2e/CodStats.E2E -- http://localhost:8080

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using CodStats.Contracts;

var baseUrl = new Uri((args.Length > 0 ? args[0] : "http://localhost:8080").TrimEnd('/') + "/");
var failures = new List<string>();
var name = "E2E" + Guid.NewGuid().ToString("N")[..8];

void Check(bool condition, string what)
{
    Console.WriteLine($"{(condition ? "ok  " : "FAIL")} {what}");
    if (!condition)
    {
        failures.Add(what);
    }
}

using var anonymous = new HttpClient { BaseAddress = baseUrl };
using var health = await anonymous.GetAsync("health");
Check(health.IsSuccessStatusCode, "health endpoint answers");

using var first = await Client.RegisterAsync(baseUrl);
using var second = await Client.RegisterAsync(baseUrl);
Check(first.ProfileId != second.ProfileId, "two devices get two profiles");

foreach (var client in new[] { first, second })
{
    (await client.Http.PutAsJsonAsync("api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Confirmed))).EnsureSuccessStatusCode();
    (await client.Http.PatchAsJsonAsync("api/v1/me/privacy", new PrivacyRequest(true))).EnsureSuccessStatusCode();
}

var now = DateTimeOffset.UtcNow;
MatchUpload Mw4(int kills, int deaths, MatchResult result, int minutesAgo) =>
    new(Guid.NewGuid(), Game.Mw4Multiplayer, "Team Deathmatch", now.AddMinutes(-minutesAgo), kills, deaths, 2, result, null, "E2E", 3000, 0.97, ReviewStatus.Auto, "e2e");
var matches = new[]
{
    Mw4(10, 1, MatchResult.Win, 90),
    Mw4(0, 10, MatchResult.Loss, 60),
    Mw4(5, 5, MatchResult.Win, 30),
    new MatchUpload(Guid.NewGuid(), Game.WarzoneBattleRoyale, "Battle Royale", now.AddMinutes(-10), 6, 1, null, null, 1, null, null, 0.95, ReviewStatus.Auto, "e2e"),
};
var upload = await (await first.Http.PostAsJsonAsync("api/v1/me/matches/batch", new MatchBatchRequest(matches))).Content.ReadFromJsonAsync<MatchBatchResponse>();
Check(upload!.Results.All(r => r.Outcome == UploadOutcome.Created), "four matches created");
var again = await (await first.Http.PostAsJsonAsync("api/v1/me/matches/batch", new MatchBatchRequest(matches))).Content.ReadFromJsonAsync<MatchBatchResponse>();
Check(again!.Results.All(r => r.Outcome == UploadOutcome.Unchanged), "repeated upload is idempotent");

var found = await anonymous.GetFromJsonAsync<List<PlayerSummary>>($"api/v1/public/players/search?q={name}");
Check(found!.Count == 2, "search finds both profiles with the same name separately");

var stats = await anonymous.GetFromJsonAsync<StatsResponse>($"api/v1/public/players/{first.ProfileId}/stats?game=mw4");
Check(stats!.KdRatio == 0.94 && stats.Matches == 3, $"MW4 K/D is 15/16 = 0.94 over 3 matches (got {stats.KdRatio} over {stats.Matches})");
var warzone = await anonymous.GetFromJsonAsync<StatsResponse>($"api/v1/public/players/{first.ProfileId}/stats?game=warzone");
Check(warzone!.Wins == 1, "Warzone win from placement 1");

var board = await anonymous.GetFromJsonAsync<LeaderboardResponse>("api/v1/public/leaderboards?game=mw4&metric=kd");
// The player has 3 MW4 matches: listed when the server's minimum allows it, absent otherwise.
var listed = board!.Entries.Any(e => e.PlayerId == first.ProfileId);
Check(listed == (board.MinMatches <= 3), $"leaderboard respects the minimum of {board.MinMatches} matches (listed: {listed})");

var text = await anonymous.GetStringAsync($"api/v1/public/players/{first.ProfileId}/matches");
Check(!text.Contains(first.DeviceId!.Value.ToString(), StringComparison.OrdinalIgnoreCase) && !text.Contains("confidence", StringComparison.OrdinalIgnoreCase),
    "public matches carry no device id or review data");

using var unsigned = await anonymous.GetAsync("api/v1/me");
Check(unsigned.StatusCode == HttpStatusCode.Unauthorized, "unsigned /me is refused");

(await first.Http.DeleteAsync("api/v1/me/data")).EnsureSuccessStatusCode();
(await second.Http.DeleteAsync("api/v1/me/data")).EnsureSuccessStatusCode();
using var gone = await anonymous.GetAsync($"api/v1/public/players/{first.ProfileId}");
Check(gone.StatusCode == HttpStatusCode.NotFound, "deleted profile is gone");

var web = await anonymous.GetStringAsync("");
Check(web.Contains("<div id=\"root\">", StringComparison.Ordinal), "website is served");
var spa = await anonymous.GetStringAsync($"player/{Guid.NewGuid()}");
Check(spa.Contains("<div id=\"root\">", StringComparison.Ordinal), "deep links fall back to the website");

Console.WriteLine(failures.Count == 0 ? "\nEnd-to-end: all checks passed" : $"\nEnd-to-end: {failures.Count} check(s) failed");
return failures.Count == 0 ? 0 : 1;

internal sealed class Client : IDisposable
{
    private readonly ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private Client(Uri baseUrl)
    {
        Http = new HttpClient(new SigningHandler(key, () => DeviceId) { InnerHandler = new HttpClientHandler() }) { BaseAddress = baseUrl };
    }

    public HttpClient Http { get; }
    public Guid? DeviceId { get; private set; }
    public Guid ProfileId { get; private set; }

    public static async Task<Client> RegisterAsync(Uri baseUrl)
    {
        var client = new Client(baseUrl);
        var response = await client.Http.PostAsJsonAsync("api/v1/devices/register",
            new RegisterDeviceRequest(Convert.ToBase64String(client.key.ExportSubjectPublicKeyInfo()), "e2e"));
        response.EnsureSuccessStatusCode();
        var registered = (await response.Content.ReadFromJsonAsync<RegisterDeviceResponse>())!;
        client.DeviceId = registered.DeviceId;
        client.ProfileId = registered.ProfileId;
        return client;
    }

    public void Dispose()
    {
        Http.Dispose();
        key.Dispose();
    }
}
