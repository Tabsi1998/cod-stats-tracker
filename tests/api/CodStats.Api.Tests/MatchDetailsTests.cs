using System.Net;
using System.Net.Http.Json;
using System.Text;
using CodStats.Contracts;

namespace CodStats.Api.Tests;

[Collection(ApiTestGroup.Name)]
public class MatchDetailsTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    private static readonly MatchDetails Report = new(11, 300, 0, 12410, 26, new Dictionary<string, int> { ["captures"] = 2, ["data_uploaded"] = 77 });

    [Fact]
    public async Task All_values_of_the_report_are_stored_and_shown_publicly()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Details" + Random.Shared.Next(100_000));
        var match = Matches.Mw4(8, 8) with { Mode = "KAPERN", Map = "MUNITION", Details = Report };
        Assert.Equal(UploadOutcome.Created, (await device.UploadAsync(match)).Results[0].Outcome);
        // The same upload again is the same match, details included.
        Assert.Equal(UploadOutcome.Unchanged, (await device.UploadAsync(match)).Results[0].Outcome);

        var shown = await device.Http.GetFromJsonAsync<PublicMatch>($"/api/v1/public/players/{device.ProfileId}/matches/{match.Id}");
        Assert.Equal(MatchStatus.Completed, shown!.Status);
        Assert.Equal(11, shown.Details!.Eliminations);
        Assert.Equal((300, 0), (shown.Details.TeamScore, shown.Details.EnemyScore));
        Assert.Equal(12410, shown.Details.Xp);
        Assert.Equal(26, shown.Details.Level);
        Assert.Equal(77, shown.Details.ModeStats!["data_uploaded"]);
    }

    [Fact]
    public async Task An_upload_of_an_older_client_without_the_new_fields_still_works()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Oldclient" + Random.Shared.Next(100_000));
        // Exactly what v0.1.x sends: no status, no details.
        var json = $$"""
            {"matches":[{"id":"{{Guid.NewGuid()}}","game":"mw4_mp","mode":"STELLUNG","playedAt":"{{DateTimeOffset.UtcNow.AddMinutes(-30):O}}",
            "kills":19,"deaths":24,"assists":null,"result":"win","placement":null,"map":null,"score":4115,
            "confidence":0.97,"reviewStatus":"auto","recognizerVersion":"ocr-0.1"}]}
            """;
        var response = await device.Http.PostAsync("/api/v1/me/matches/batch", new StringContent(json, Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        Assert.Equal(UploadOutcome.Created, (await response.Content.ReadFromJsonAsync<MatchBatchResponse>())!.Results[0].Outcome);

        var page = await device.Http.GetFromJsonAsync<Page<PublicMatch>>($"/api/v1/public/players/{device.ProfileId}/matches");
        Assert.Equal(MatchStatus.Completed, page!.Items[0].Status);
        Assert.Null(page.Items[0].Details);
    }

    [Fact]
    public async Task Left_matches_count_for_kd_but_not_for_the_win_rate()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Leaver" + Random.Shared.Next(100_000));
        await device.UploadAsync(
            Matches.Mw4(10, 5, MatchResult.Win),
            Matches.Mw4(10, 5, MatchResult.Loss),
            Matches.Mw4(10, 10, result: null) with { Status = MatchStatus.Left });

        var stats = await device.Http.GetFromJsonAsync<StatsResponse>($"/api/v1/public/players/{device.ProfileId}/stats");
        Assert.Equal(3, stats!.Matches);
        Assert.Equal(1, stats.LeftMatches);
        Assert.Equal(0.3333, stats.LeaveRate);
        Assert.Equal(0.5, stats.WinRate);
        Assert.Equal(1.5, stats.KdRatio);
    }

    [Fact]
    public async Task A_left_match_with_a_result_is_refused()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var result = (await device.UploadAsync(Matches.Mw4(3, 5, MatchResult.Loss) with { Status = MatchStatus.Left })).Results[0];
        Assert.Equal(UploadOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task Stats_are_broken_down_by_mode_and_map()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Breakdown" + Random.Shared.Next(100_000));
        await device.UploadAsync(
            Matches.Mw4(20, 10, MatchResult.Win) with { Mode = "KAPERN", Map = "MUNITION" },
            Matches.Mw4(10, 10, MatchResult.Loss) with { Mode = "KAPERN", Map = "COAL" },
            Matches.Mw4(30, 10, MatchResult.Win) with { Mode = "STELLUNG", Map = "COAL" });

        var stats = await device.Http.GetFromJsonAsync<StatsResponse>($"/api/v1/public/players/{device.ProfileId}/stats");
        var kapern = stats!.ByMode!.Single(b => b.Name == "KAPERN");
        Assert.Equal(2, kapern.Matches);
        Assert.Equal(0.5, kapern.WinRate);
        Assert.Equal(1.5, kapern.KdRatio);
        Assert.Equal("COAL", stats.ByMap![0].Name);
        Assert.Equal(2, stats.ByMap[0].Matches);
    }

    [Fact]
    public async Task A_match_of_another_or_hidden_player_is_not_found()
    {
        using var visible = await TestDevice.PublicPlayerAsync(Api, "Owner" + Random.Shared.Next(100_000));
        using var hidden = await TestDevice.RegisterAsync(Api);
        var own = Matches.Mw4(5, 5);
        var secret = Matches.Mw4(6, 6);
        await visible.UploadAsync(own);
        await hidden.UploadAsync(secret);

        Assert.Equal(HttpStatusCode.NotFound, (await visible.Http.GetAsync($"/api/v1/public/players/{visible.ProfileId}/matches/{secret.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await visible.Http.GetAsync($"/api/v1/public/players/{hidden.ProfileId}/matches/{secret.Id}")).StatusCode);
    }

    [Fact]
    public async Task Leaderboards_can_be_limited_to_one_mode()
    {
        using var player = await TestDevice.PublicPlayerAsync(Api, "Moderank" + Random.Shared.Next(100_000));
        var mode = "MODE" + Random.Shared.Next(100_000);
        await player.UploadAsync(Enumerable.Range(0, ApiFactory.LeaderboardMinMatches).Select(_ => Matches.Mw4(10, 5) with { Mode = mode }).ToArray());

        var board = await player.Http.GetFromJsonAsync<LeaderboardResponse>($"/api/v1/public/leaderboards?game=mw4&mode={mode.ToLowerInvariant()}");
        var entry = Assert.Single(board!.Entries);
        Assert.Equal(player.ProfileId, entry.PlayerId);
    }
}
