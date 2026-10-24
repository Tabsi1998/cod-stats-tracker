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

[Collection(ApiTestGroup.Name)]
public class MatchVisibilityTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    private static async Task SetPublic(TestDevice device, bool isPublic) =>
        (await device.Http.PatchAsJsonAsync("/api/v1/me/privacy", new PrivacyRequest(isPublic))).EnsureSuccessStatusCode();

    private static async Task<int> ShownMatches(TestDevice device) =>
        (await device.Http.GetFromJsonAsync<Page<PublicMatch>>($"/api/v1/public/players/{device.ProfileId}/matches"))!.Total;

    [Fact]
    public async Task Going_private_keeps_what_was_shown_and_hides_only_new_matches()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Switcher" + Random.Shared.Next(100_000));
        await device.UploadAsync(Matches.Mw4(10, 5));

        await SetPublic(device, false);
        await device.UploadAsync(Matches.Mw4(20, 5));

        Assert.Equal(1, await ShownMatches(device));
        var stats = await device.Http.GetFromJsonAsync<StatsResponse>($"/api/v1/public/players/{device.ProfileId}/stats");
        Assert.Equal(10, stats!.Kills);
        // The player's own view still has everything.
        var own = await device.Http.GetFromJsonAsync<StatsResponse>("/api/v1/me/stats");
        Assert.Equal(30, own!.Kills);
        Assert.True((await device.Http.GetFromJsonAsync<MeResponse>("/api/v1/me"))!.IsVisible);
    }

    [Fact]
    public async Task Going_public_shows_the_whole_history()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Latecomer" + Random.Shared.Next(100_000));
        await SetPublic(device, false);
        await device.UploadAsync(Matches.Mw4(7, 3), Matches.Mw4(8, 4));
        Assert.Equal(HttpStatusCode.NotFound, (await device.Http.GetAsync($"/api/v1/public/players/{device.ProfileId}")).StatusCode);

        await SetPublic(device, true);
        Assert.Equal(2, await ShownMatches(device));
    }
}

[Collection(ApiTestGroup.Name)]
public class UniqueNameTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    [Fact]
    public async Task A_confirmed_name_belongs_to_one_profile_only()
    {
        var name = "Unique" + Random.Shared.Next(100_000);
        using var first = await TestDevice.PublicPlayerAsync(Api, name);
        using var second = await TestDevice.RegisterAsync(Api);

        var taken = await second.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Confirmed));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);

        // Unconfirmed it is allowed: such a profile is never shown.
        var uncertain = await second.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Uncertain));
        uncertain.EnsureSuccessStatusCode();

        var found = await first.Http.GetFromJsonAsync<List<PlayerSummary>>($"/api/v1/public/players/search?q={name}");
        Assert.Single(found!);
    }

    [Fact]
    public async Task Names_differing_only_in_case_are_different_players()
    {
        // In Call of Duty "Tabsi98" and "TAbsi98" are two accounts.
        var name = "case" + Random.Shared.Next(100_000);
        using var lower = await TestDevice.PublicPlayerAsync(Api, name);
        using var upper = await TestDevice.PublicPlayerAsync(Api, name.ToUpperInvariant());
        Assert.NotEqual(lower.ProfileId, upper.ProfileId);
    }

    [Fact]
    public async Task The_owner_can_confirm_their_own_name_again()
    {
        var name = "Owner" + Random.Shared.Next(100_000);
        using var device = await TestDevice.PublicPlayerAsync(Api, name);
        var again = await device.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Confirmed));
        again.EnsureSuccessStatusCode();
    }
}
