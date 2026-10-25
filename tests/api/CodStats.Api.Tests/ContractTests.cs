using System.Security.Cryptography;
using System.Text;
using CodStats.Contracts;

namespace CodStats.Api.Tests;

public class SigningTests
{
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"a\":1}");

    [Fact]
    public void Signature_verifies_with_the_matching_key()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = RequestSigning.Sign(key, "POST", "/api/v1/me/matches/batch", 1_700_000_000, "abcdefabcdefabcdef", Body);
        Assert.True(RequestSigning.Verify(key.ExportSubjectPublicKeyInfo(), signature, "post", "/api/v1/me/matches/batch", 1_700_000_000, "abcdefabcdefabcdef", Body));
    }

    [Theory]
    [InlineData("GET", "/api/v1/me/matches/batch", 1_700_000_000, "abcdefabcdefabcdef", "{\"a\":1}")]
    [InlineData("POST", "/api/v1/me/data", 1_700_000_000, "abcdefabcdefabcdef", "{\"a\":1}")]
    [InlineData("POST", "/api/v1/me/matches/batch", 1_700_000_001, "abcdefabcdefabcdef", "{\"a\":1}")]
    [InlineData("POST", "/api/v1/me/matches/batch", 1_700_000_000, "bbcdefabcdefabcdef", "{\"a\":1}")]
    [InlineData("POST", "/api/v1/me/matches/batch", 1_700_000_000, "abcdefabcdefabcdef", "{\"a\":2}")]
    public void Any_changed_part_breaks_the_signature(string method, string path, long timestamp, string nonce, string body)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = RequestSigning.Sign(key, "POST", "/api/v1/me/matches/batch", 1_700_000_000, "abcdefabcdefabcdef", Body);
        Assert.False(RequestSigning.Verify(key.ExportSubjectPublicKeyInfo(), signature, method, path, timestamp, nonce, Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void Another_key_does_not_verify()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var signature = RequestSigning.Sign(key, "GET", "/api/v1/me", 1, "abcdefabcdefabcdef", []);
        Assert.False(RequestSigning.Verify(other.ExportSubjectPublicKeyInfo(), signature, "GET", "/api/v1/me", 1, "abcdefabcdefabcdef", []));
    }

    [Fact]
    public void Only_P256_keys_are_accepted()
    {
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        Assert.Null(RequestSigning.ImportPublicKey(p384.ExportSubjectPublicKeyInfo()));
        Assert.Null(RequestSigning.ImportPublicKey([1, 2, 3]));
    }
}

public class StatsTests
{
    private static MatchFacts Mw4(int? kills, int? deaths, MatchResult? result, int minute) =>
        new(Guid.NewGuid(), Game.Mw4Multiplayer, DateTimeOffset.UnixEpoch.AddMinutes(minute), kills, deaths, null, result, null);

    [Fact]
    public void Longest_win_streak_skips_left_matches_and_breaks_on_losses()
    {
        MatchFacts Left(int minute) => Mw4(5, 5, null, minute) with { Status = MatchStatus.Left };
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [
            Mw4(5, 5, MatchResult.Win, 1), Mw4(5, 5, MatchResult.Win, 2), Mw4(5, 5, MatchResult.Loss, 3),
            Mw4(5, 5, MatchResult.Win, 4), Left(5), Mw4(5, 5, MatchResult.Win, 6), Mw4(5, 5, MatchResult.Win, 7),
        ]);
        Assert.Equal(3, stats.LongestWinStreak);
        Assert.Equal(1, stats.LeftMatches);
        Assert.Equal(5.0 / 6, stats.WinRate!.Value, 3);
    }

    [Fact]
    public void Best_match_is_the_newest_with_the_most_kills_and_averages_skip_unknowns()
    {
        var first = Mw4(30, 5, MatchResult.Win, 1) with { Score = 4000, Xp = 10_000 };
        var second = Mw4(30, 9, MatchResult.Loss, 2) with { Score = 2000 };
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [first, second, Mw4(3, 9, MatchResult.Loss, 3)]);
        Assert.Equal(second.Id, stats.BestMatchId);
        Assert.Equal(3000, stats.AverageScore);
        Assert.Equal(10_000, stats.AverageXp);
    }

    [Fact]
    public void Lobby_share_is_summed_over_matches_not_averaged()
    {
        // Place 1 of 2 (1 of 1 other behind) and place 12 of 12 (0 of 11 behind): 1 / 12 others, not the mean of 100 % and 0 %.
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [
            Mw4(5, 5, MatchResult.Win, 1) with { LobbySize = 2, LobbyRank = 1 },
            Mw4(5, 5, MatchResult.Loss, 2) with { LobbySize = 12, LobbyRank = 12 },
            Mw4(5, 5, MatchResult.Loss, 3),
        ]);
        Assert.Equal(2, stats.LobbyMatches);
        Assert.Equal(0.0833, stats.LobbyShare);
        Assert.Null(StatsCalculator.Compute(GameGroup.Mw4, [Mw4(5, 5, MatchResult.Win, 1)]).LobbyShare);
    }

    [Fact]
    public void Breakdown_groups_names_regardless_of_case()
    {
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [
            Mw4(10, 5, MatchResult.Win, 1) with { Mode = "Kapern" }, Mw4(10, 10, MatchResult.Loss, 2) with { Mode = "KAPERN" },
            Mw4(1, 1, MatchResult.Win, 3) with { Mode = null },
        ]);
        var kapern = Assert.Single(stats.ByMode!);
        Assert.Equal("KAPERN", kapern.Name);
        Assert.Equal(2, kapern.Matches);
        Assert.Equal(1.33, kapern.KdRatio);
    }

    [Fact]
    public void Kd_is_total_kills_over_total_deaths_not_the_average_of_ratios()
    {
        // Match ratios 10.0 and 0.0 average to 5.0; the correct K/D is 10 / 11.
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [Mw4(10, 1, MatchResult.Win, 1), Mw4(0, 10, MatchResult.Loss, 2)]);
        Assert.Equal(0.91, stats.KdRatio);
        Assert.Equal(10, stats.Kills);
        Assert.Equal(11, stats.Deaths);
    }

    [Fact]
    public void Matches_with_unknown_deaths_do_not_distort_the_kd()
    {
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [Mw4(10, 5, null, 1), Mw4(30, null, null, 2)]);
        Assert.Equal(2.0, stats.KdRatio);
        Assert.Equal(40, stats.Kills);
        Assert.Equal(20.0, stats.AverageKills);
        Assert.Equal(30, stats.BestKills);
    }

    [Fact]
    public void Kd_without_deaths_is_the_kill_count_and_empty_is_null()
    {
        Assert.Equal(7, StatsCalculator.KdRatio(7, 0));
        Assert.Null(StatsCalculator.KdRatio(0, 0));
        Assert.Null(StatsCalculator.Compute(GameGroup.All, []).KdRatio);
    }

    [Fact]
    public void Win_rate_counts_only_matches_with_a_known_result()
    {
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [Mw4(1, 1, MatchResult.Win, 1), Mw4(1, 1, MatchResult.Loss, 2), Mw4(1, 1, null, 3)]);
        Assert.Equal(0.5, stats.WinRate);
        Assert.Equal(2, stats.MatchesWithResult);
    }

    [Fact]
    public void Warzone_win_comes_from_placement()
    {
        var win = new MatchFacts(Guid.NewGuid(), Game.WarzoneBattleRoyale, DateTimeOffset.UnixEpoch, 5, 1, null, null, 1);
        var loss = new MatchFacts(Guid.NewGuid(), Game.WarzoneResurgence, DateTimeOffset.UnixEpoch.AddHours(1), 2, 1, null, null, 12);
        var stats = StatsCalculator.Compute(GameGroup.Warzone, [win, loss]);
        Assert.Equal(1, stats.Wins);
        Assert.Equal(6.5, stats.AveragePlacement);
    }

    [Fact]
    public void Running_kd_in_the_series_uses_sums_too()
    {
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [Mw4(10, 1, null, 1), Mw4(0, 10, null, 2)]);
        Assert.Equal([10.0, 0.91], stats.Series.Select(p => p.RunningKdRatio!.Value));
    }

    [Fact]
    public void Filter_last_takes_the_newest_and_keeps_time_order()
    {
        var matches = Enumerable.Range(0, 30).Select(i => Mw4(i, 1, null, i)).Reverse().ToList();
        var selected = new StatsFilter(GameGroup.Mw4, Last: 10).Apply(matches, DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.Equal(Enumerable.Range(20, 10), selected.Select(m => m.Kills!.Value));
    }

    [Fact]
    public void Filter_separates_games_and_days()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(40);
        MatchFacts[] matches =
        [
            new(Guid.NewGuid(), Game.Mw4Multiplayer, now.AddDays(-3), 1, 1, null, null, null),
            new(Guid.NewGuid(), Game.Mw4Multiplayer, now.AddDays(-20), 1, 1, null, null, null),
            new(Guid.NewGuid(), Game.WarzoneBattleRoyale, now.AddDays(-1), 1, 1, null, null, 3),
        ];
        Assert.Single(new StatsFilter(GameGroup.Mw4, Days: 7).Apply(matches, now));
        Assert.Equal(2, new StatsFilter(GameGroup.Mw4, Days: 30).Apply(matches, now).Count);
        Assert.Equal(3, new StatsFilter(GameGroup.All).Apply(matches, now).Count);
    }

    [Theory]
    [InlineData(5, null)]
    [InlineData(null, 14)]
    public void Filter_rejects_values_outside_the_offered_choices(int? last, int? days) =>
        Assert.NotEmpty(new StatsFilter(Last: last, Days: days).Problems());

    [Fact]
    public void Filter_by_mode_map_weapon_result_and_status_without_case()
    {
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        MatchFacts[] matches =
        [
            Mw4(10, 5, MatchResult.Win, 1) with { Mode = "ABSCHUSS BESTÄTIGT", Map = "COAL", Weapon = "HAN 86", WeaponClass = "STURMGEWEHR" },
            Mw4(10, 5, MatchResult.Loss, 2) with { Mode = "Abschuss bestätigt", Map = "MUNITION", Weapon = "SANG 9MM", WeaponClass = "PISTOLE" },
            Mw4(3, 3, null, 3) with { Mode = "KAPERN", Map = "COAL", Status = MatchStatus.Left },
            new(Guid.NewGuid(), Game.WarzoneBattleRoyale, DateTimeOffset.UnixEpoch.AddMinutes(4), 2, 1, null, null, 1),
        ];
        Assert.Equal(2, new StatsFilter(Mode: " abschuss bestätigt ").Apply(matches, now).Count);
        Assert.Equal(2, new StatsFilter(Map: "coal").Apply(matches, now).Count);
        Assert.Single(new StatsFilter(Weapon: "han 86").Apply(matches, now));
        Assert.Single(new StatsFilter(WeaponClass: "Pistole").Apply(matches, now));
        // A Warzone win is first place, even without a stored result.
        Assert.Equal(2, new StatsFilter(Result: MatchResult.Win).Apply(matches, now).Count);
        Assert.Single(new StatsFilter(Status: MatchStatus.Left).Apply(matches, now));
        Assert.Single(new StatsFilter(Mode: "abschuss bestätigt", Result: MatchResult.Loss).Apply(matches, now));
        Assert.NotEmpty(new StatsFilter(Result: MatchResult.Win, Status: MatchStatus.Left).Problems());
        Assert.NotEmpty(new StatsFilter(Mode: new string('x', 65)).Problems());
    }

    [Fact]
    public void Weapons_maps_by_mode_mode_values_and_records()
    {
        var kc1 = Mw4(20, 10, MatchResult.Win, 1) with
        {
            Mode = "ABSCHUSS BESTÄTIGT",
            Map = "COAL",
            Weapon = "HAN 86",
            WeaponClass = "STURMGEWEHR",
            Score = 3000,
            ModeStats = new Dictionary<string, int> { ["confirms"] = 12, ["denies"] = 4 },
        };
        var kc2 = Mw4(10, 10, MatchResult.Loss, 2) with
        {
            Mode = "ABSCHUSS BESTÄTIGT",
            Map = "COAL",
            Weapon = "HAN 86",
            WeaponClass = "STURMGEWEHR",
            Score = 2000,
            ModeStats = new Dictionary<string, int> { ["confirms"] = 6 },
        };
        var cap = Mw4(8, 2, MatchResult.Win, 3) with
        {
            Mode = "KAPERN",
            Map = "MUNITION",
            Weapon = "SANG 9MM",
            WeaponClass = "PISTOLE",
            Score = 1225,
            ModeStats = new Dictionary<string, int> { ["captures"] = 2 },
        };
        var stats = StatsCalculator.Compute(GameGroup.Mw4, [kc1, kc2, cap]);

        var han = stats.ByWeapon![0];
        Assert.Equal(("HAN 86", 2, 1.5), (han.Name, han.Matches, han.KdRatio!.Value));
        Assert.Equal(["STURMGEWEHR", "PISTOLE"], stats.ByWeaponClass!.Select(b => b.Name));

        var cell = stats.ByMapMode![0];
        Assert.Equal(("COAL", "ABSCHUSS BESTÄTIGT", 2, 0.5), (cell.Map, cell.Mode, cell.Matches, cell.WinRate!.Value));

        // Averages over the matches that have the value: confirms 9 per Kill Confirmed match, not 6 over all three.
        var confirms = stats.ModeStats!.Single(s => s.Key == "confirms");
        Assert.Equal((18L, 2, 9.0, 12), (confirms.Total, confirms.Matches, confirms.Average, confirms.Best));
        var byMode = stats.ByMode!.Single(b => b.Name == "ABSCHUSS BESTÄTIGT");
        Assert.Equal(9.0, byMode.ModeStats!.Single(s => s.Key == "confirms").Average);
        Assert.Equal(20, byMode.Deaths);

        var records = stats.Records!.ToDictionary(r => r.Kind);
        Assert.Equal((20.0, kc1.Id), (records["kills"].Value, records["kills"].MatchId));
        Assert.Equal((4.0, cap.Id), (records["kd"].Value, records["kd"].MatchId));
        Assert.Equal(12, records["stat.confirms"].Value);
        Assert.Equal(cap.Id, records["stat.captures"].MatchId);

        var point = stats.Series[^1];
        Assert.Equal(("KAPERN", "MUNITION", "SANG 9MM", 1225), (point.Mode, point.Map, point.Weapon, point.Score));
    }
}

public class MatchRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 20, 0, 0, TimeSpan.Zero);

    private static MatchUpload Valid() =>
        new(Guid.NewGuid(), Game.Mw4Multiplayer, "Domination", Now.AddMinutes(-20), 25, 12, 4, MatchResult.Win, null, "Map", 4000, 0.9, ReviewStatus.Auto, "1");

    [Fact]
    public void A_normal_match_is_plausible() => Assert.Empty(MatchRules.Validate(Valid(), Now));

    [Fact]
    public void Missing_values_are_allowed_as_null() =>
        Assert.Empty(MatchRules.Validate(Valid() with { Kills = null, Deaths = null, Assists = null, Result = null, Map = null, Score = null }, Now));

    [Theory]
    [MemberData(nameof(Implausible))]
    public void Implausible_matches_are_rejected(MatchUpload match) => Assert.NotEmpty(MatchRules.Validate(match, Now));

    public static TheoryData<MatchUpload> Implausible() => new()
    {
        Valid() with { Kills = 999 },
        Valid() with { Deaths = -1 },
        Valid() with { Score = 1_000_000 },
        Valid() with { Confidence = 1.5 },
        Valid() with { PlayedAt = Now.AddHours(1) },
        Valid() with { PlayedAt = Now.AddDays(-200) },
        Valid() with { Placement = 0 }, // free-for-all places start at 1
        Valid() with { ReviewStatus = ReviewStatus.Pending },
        Valid() with { Mode = new string('x', 65) },
        Valid() with { Map = "a\u0000b" },
        Valid() with { Id = Guid.Empty },
        Valid() with { Game = Game.WarzoneBattleRoyale, Placement = 1, Result = MatchResult.Loss },
        Valid() with { Game = Game.WarzoneBattleRoyale, Placement = 0 },
        Valid() with { Game = Game.WarzoneResurgence, Placement = 4, Result = MatchResult.Draw },
        Valid() with { Status = MatchStatus.Left },
        Valid() with { Details = new(Xp: -1) },
        Valid() with { Details = new(Level: 0) },
        Valid() with { Details = new(TeamScore: 1_000_000) },
        Valid() with { Details = new(ModeStats: new Dictionary<string, int> { ["Captures!"] = 2 }) },
        Valid() with { Details = new(ModeStats: new Dictionary<string, int> { ["captures"] = -2 }) },
        Valid() with { Details = new(ModeStats: Enumerable.Range(0, 13).ToDictionary(i => $"s{i}", i => i)) },
        Valid() with { Details = new(LobbySize: 12) },
        Valid() with { Details = new(LobbySize: 12, LobbyRank: 13) },
        Valid() with { Details = new(LobbySize: 1, LobbyRank: 1) },
        Valid() with { Details = new(LobbySize: 151, LobbyRank: 1) },
        Valid() with { Details = new(LobbySize: 12, LobbyRank: 0) },
        Valid() with { Details = new(Weapon: "") },
        Valid() with { Details = new(Weapon: new string('M', 33)) },
        Valid() with { Details = new(Weapon: "<script>") },
        Valid() with { Details = new(WeaponClass: "STURM\nGEWEHR") },
    };

    [Fact]
    public void A_match_with_all_details_is_plausible() =>
        Assert.Empty(MatchRules.Validate(Valid() with
        {
            Details = new(11, 300, 0, 12410, 26, new Dictionary<string, int> { ["captures"] = 2, ["data_uploaded"] = 77 }, 12, 3,
                "WZ.55 STRIGA", "SCHARFSCHÜTZENGEWEHR"),
        }, Now));

    [Fact]
    public void A_left_match_without_result_is_plausible() =>
        Assert.Empty(MatchRules.Validate(Valid() with { Status = MatchStatus.Left, Result = null }, Now));

    [Theory]
    [InlineData("  ShadowPlayer  ", "ShadowPlayer")]
    [InlineData("A", null)]
    [InlineData("bad\tname", null)]
    public void Display_names_are_trimmed_and_checked(string input, string? expected) =>
        Assert.Equal(expected, DisplayNames.Normalise(input));
}
