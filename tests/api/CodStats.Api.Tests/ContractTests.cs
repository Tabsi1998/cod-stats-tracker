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
    };

    [Theory]
    [InlineData("  ShadowPlayer  ", "ShadowPlayer")]
    [InlineData("A", null)]
    [InlineData("bad\tname", null)]
    public void Display_names_are_trimmed_and_checked(string input, string? expected) =>
        Assert.Equal(expected, DisplayNames.Normalise(input));
}
