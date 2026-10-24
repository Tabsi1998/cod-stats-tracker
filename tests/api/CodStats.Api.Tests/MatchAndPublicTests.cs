using System.Net;
using System.Net.Http.Json;
using CodStats.Contracts;

namespace CodStats.Api.Tests;

[Collection(ApiTestGroup.Name)]
public class MatchUploadTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    [Fact]
    public async Task Upload_is_idempotent()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var match = Matches.Mw4(20, 10);
        Assert.Equal(UploadOutcome.Created, (await device.UploadAsync(match)).Results[0].Outcome);
        Assert.Equal(UploadOutcome.Unchanged, (await device.UploadAsync(match)).Results[0].Outcome);
        Assert.Equal(1, (await device.Http.GetFromJsonAsync<SyncStatusResponse>("/api/v1/me/sync-status"))!.Matches);
    }

    [Fact]
    public async Task Same_id_with_other_values_is_rejected()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var match = Matches.Mw4(20, 10);
        await device.UploadAsync(match);
        var result = (await device.UploadAsync(match with { Kills = 99 })).Results[0];
        Assert.Equal(UploadOutcome.Rejected, result.Outcome);
    }

    [Fact]
    public async Task Another_profiles_match_id_cannot_be_overwritten()
    {
        using var owner = await TestDevice.RegisterAsync(Api);
        using var attacker = await TestDevice.RegisterAsync(Api);
        var match = Matches.Mw4(20, 10);
        await owner.UploadAsync(match);
        Assert.Equal(UploadOutcome.Rejected, (await attacker.UploadAsync(match)).Results[0].Outcome);
        Assert.Equal(0, (await attacker.Http.GetFromJsonAsync<SyncStatusResponse>("/api/v1/me/sync-status"))!.Matches);
        Assert.Equal(1, (await owner.Http.GetFromJsonAsync<SyncStatusResponse>("/api/v1/me/sync-status"))!.Matches);
    }

    [Fact]
    public async Task The_same_result_under_a_new_id_is_a_duplicate()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var first = Matches.Warzone(7, 1, 3);
        var again = first with { Id = Guid.NewGuid(), PlayedAt = first.PlayedAt.AddSeconds(40) };
        var results = (await device.UploadAsync(first, again)).Results;
        Assert.Equal([UploadOutcome.Created, UploadOutcome.Duplicate], results.Select(r => r.Outcome));
    }

    [Fact]
    public async Task Pending_and_implausible_matches_are_rejected_individually()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var results = (await device.UploadAsync(
            Matches.Mw4(10, 5),
            Matches.Mw4(10, 5) with { ReviewStatus = ReviewStatus.Pending },
            Matches.Mw4(500, 5))).Results;
        Assert.Equal([UploadOutcome.Created, UploadOutcome.Rejected, UploadOutcome.Rejected], results.Select(r => r.Outcome));
    }

    [Fact]
    public async Task A_correction_replaces_values_and_marks_the_match()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var match = Matches.Mw4(10, 5);
        await device.UploadAsync(match);
        var response = await device.Http.PatchAsJsonAsync($"/api/v1/me/matches/{match.Id}",
            new MatchCorrectionRequest(match.Mode, match.PlayedAt, 12, 5, 3, MatchResult.Loss, null, match.Map, match.Score));
        var corrected = await response.Content.ReadFromJsonAsync<OwnMatch>();
        Assert.Equal(12, corrected!.Kills);
        Assert.Equal(ReviewStatus.Corrected, corrected.ReviewStatus);
    }

    [Fact]
    public async Task Another_profiles_match_cannot_be_corrected()
    {
        using var owner = await TestDevice.RegisterAsync(Api);
        using var attacker = await TestDevice.RegisterAsync(Api);
        var match = Matches.Mw4(10, 5);
        await owner.UploadAsync(match);
        var response = await attacker.Http.PatchAsJsonAsync($"/api/v1/me/matches/{match.Id}",
            new MatchCorrectionRequest(null, match.PlayedAt, 0, 50, null, MatchResult.Loss, null, null, null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Own_stats_are_split_by_game_and_use_summed_kd()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        await device.UploadAsync(Matches.Mw4(10, 1), Matches.Mw4(0, 10, MatchResult.Loss), Matches.Warzone(4, 1, 1));
        var mw4 = await device.Http.GetFromJsonAsync<StatsResponse>("/api/v1/me/stats?game=mw4");
        var warzone = await device.Http.GetFromJsonAsync<StatsResponse>("/api/v1/me/stats?game=warzone");
        var all = await device.Http.GetFromJsonAsync<StatsResponse>("/api/v1/me/stats");
        Assert.Equal(0.91, mw4!.KdRatio);
        Assert.Equal(2, mw4.Matches);
        Assert.Equal(1, warzone!.Wins);
        Assert.True(all!.IsCombined);
        Assert.Equal(3, all.Matches);
    }

    [Fact]
    public async Task Bad_stats_filters_are_refused()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        Assert.Equal(HttpStatusCode.BadRequest, (await device.Http.GetAsync("/api/v1/me/stats?last=7")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await device.Http.GetAsync("/api/v1/me/stats?game=fortnite")).StatusCode);
    }

    [Fact]
    public async Task Export_contains_everything_and_delete_removes_everything()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, "Delete" + Guid.NewGuid().ToString("N")[..8]);
        await device.UploadAsync(Matches.Mw4(3, 3));
        var export = await device.Http.GetFromJsonAsync<ExportResponse>("/api/v1/me/export");
        Assert.Single(export!.Matches);
        Assert.Single(export.Names);
        Assert.Single(export.Devices);

        Assert.Equal(HttpStatusCode.NoContent, (await device.Http.DeleteAsync("/api/v1/me/data")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Api.Anonymous().GetAsync($"/api/v1/public/players/{device.ProfileId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await device.Http.GetAsync("/api/v1/me")).StatusCode);
    }
}

[Collection(ApiTestGroup.Name)]
public class PublicTests(ApiFixture fixture)
{
    private ApiFactory Api => fixture.Api;

    private static string UniqueName(string prefix) => prefix + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task Profiles_are_hidden_until_public_and_confirmed()
    {
        using var device = await TestDevice.RegisterAsync(Api);
        var url = $"/api/v1/public/players/{device.ProfileId}";
        var name = UniqueName("Hidden");

        await device.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Uncertain));
        await device.Http.PatchAsJsonAsync("/api/v1/me/privacy", new PrivacyRequest(true));
        Assert.Equal(HttpStatusCode.NotFound, (await Api.Anonymous().GetAsync(url)).StatusCode);

        await device.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(name, NameStatus.Confirmed));
        Assert.Equal(HttpStatusCode.OK, (await Api.Anonymous().GetAsync(url)).StatusCode);

        await device.Http.PatchAsJsonAsync("/api/v1/me/privacy", new PrivacyRequest(false));
        Assert.Equal(HttpStatusCode.NotFound, (await Api.Anonymous().GetAsync(url)).StatusCode);
        Assert.Empty((await Api.Anonymous().GetFromJsonAsync<List<PlayerSummary>>($"/api/v1/public/players/search?q={name}"))!);
    }

    [Fact]
    public async Task Search_treats_wildcards_literally()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, UniqueName("Wild"));
        var found = await Api.Anonymous().GetFromJsonAsync<List<PlayerSummary>>("/api/v1/public/players/search?q=%25%25");
        Assert.DoesNotContain(found!, p => p.Id == device.ProfileId);
    }

    [Fact]
    public async Task A_name_change_keeps_profile_and_history()
    {
        var oldName = UniqueName("Old");
        var newName = UniqueName("New");
        using var device = await TestDevice.PublicPlayerAsync(Api, oldName);
        await device.UploadAsync(Matches.Mw4(5, 5));
        await device.Http.PutAsJsonAsync("/api/v1/me/display-name", new SetDisplayNameRequest(newName, NameStatus.Confirmed));

        var profile = await Api.Anonymous().GetFromJsonAsync<PlayerProfile>($"/api/v1/public/players/{device.ProfileId}");
        Assert.Equal(newName, profile!.DisplayName);
        Assert.Equal([oldName], profile.PreviousNames);
        Assert.Equal(1, profile.Matches);
    }

    [Fact]
    public async Task Public_responses_leak_no_private_fields()
    {
        using var device = await TestDevice.PublicPlayerAsync(Api, UniqueName("Leak"));
        await device.UploadAsync(Matches.Mw4(5, 5));
        var client = Api.Anonymous();
        var texts = new[]
        {
            await client.GetStringAsync($"/api/v1/public/players/{device.ProfileId}"),
            await client.GetStringAsync($"/api/v1/public/players/{device.ProfileId}/matches"),
            await client.GetStringAsync("/api/v1/public/players"),
        };
        foreach (var text in texts)
        {
            Assert.DoesNotContain(device.DeviceId!.Value.ToString(), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("confidence", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("publicKey", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("recovery", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Leaderboards_need_minimum_matches_and_rank_by_summed_kd()
    {
        using var regular = await TestDevice.PublicPlayerAsync(Api, UniqueName("Rank"));
        using var newcomer = await TestDevice.PublicPlayerAsync(Api, UniqueName("Rank"));
        using var hidden = await TestDevice.RegisterAsync(Api);
        await regular.UploadAsync(Matches.Mw4(10, 1), Matches.Mw4(0, 10), Matches.Mw4(1, 0));
        await newcomer.UploadAsync(Matches.Mw4(50, 1));
        await hidden.UploadAsync(Matches.Mw4(90, 1), Matches.Mw4(90, 1), Matches.Mw4(90, 1));

        var board = await Api.Anonymous().GetFromJsonAsync<LeaderboardResponse>("/api/v1/public/leaderboards?game=mw4&metric=kd");
        Assert.Equal(ApiFactory.LeaderboardMinMatches, board!.MinMatches);
        var entry = Assert.Single(board.Entries, e => e.PlayerId == regular.ProfileId);
        Assert.Equal(1.0, entry.Value); // 11 kills / 11 deaths
        Assert.DoesNotContain(board.Entries, e => e.PlayerId == newcomer.ProfileId);
        Assert.DoesNotContain(board.Entries, e => e.PlayerId == hidden.ProfileId);

        var warzone = await Api.Anonymous().GetFromJsonAsync<LeaderboardResponse>("/api/v1/public/leaderboards?game=warzone&metric=kills");
        Assert.DoesNotContain(warzone!.Entries, e => e.PlayerId == regular.ProfileId);
    }

    [Fact]
    public async Task Platform_stats_count_only_visible_profiles()
    {
        var client = Api.Anonymous();
        var before = await client.GetFromJsonAsync<PlatformStatsResponse>("/api/v1/public/platform-stats");
        using var visible = await TestDevice.PublicPlayerAsync(Api, UniqueName("Count"));
        using var hidden = await TestDevice.RegisterAsync(Api);
        await visible.UploadAsync(Matches.Mw4(7, 1));
        await hidden.UploadAsync(Matches.Mw4(9, 1));
        var after = await client.GetFromJsonAsync<PlatformStatsResponse>("/api/v1/public/platform-stats");
        Assert.Equal(before!.Players + 1, after!.Players);
        Assert.Equal(before.Matches + 1, after.Matches);
        Assert.Equal(before.Kills + 7, after.Kills);
    }

    [Fact]
    public async Task OpenApi_document_is_served()
    {
        var text = await Api.Anonymous().GetStringAsync("/openapi/v1.json");
        Assert.Contains("/api/v1/public/leaderboards", text, StringComparison.Ordinal);
    }
}
