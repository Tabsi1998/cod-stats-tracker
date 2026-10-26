using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodStats.Contracts;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Recognition;

namespace CodStats.Desktop.Tests;

public class TextParsingTests
{
    [Theory]
    [InlineData("23", 23, 1.0)]
    [InlineData(" 1 024 ", 1024, 1.0)]
    [InlineData("1O", 10, 0.8)]
    [InlineData("l5", 15, 0.8)]
    public void Counts_are_read_and_lookalikes_repaired(string text, int expected, double quality)
    {
        var (value, q) = TextParsing.Count(text);
        Assert.Equal(expected, value);
        Assert.Equal(quality, q);
    }

    [Theory]
    [InlineData("")]
    [InlineData("KILLS")]
    [InlineData("OO")]
    [InlineData("12a")]
    [InlineData("123456789")]
    public void Anything_unsure_is_no_number(string text) => Assert.Null(TextParsing.Count(text).Value);

    [Theory]
    [InlineData("#3", 3)]
    [InlineData("3rd", 3)]
    [InlineData("PLATZ 12", 12)]
    public void Placements_are_read(string text, int expected) => Assert.Equal(expected, TextParsing.Placement(text).Value);

    [Fact]
    public void Results_need_exactly_one_matching_word()
    {
        string[] win = ["VICTORY", "SIEG"], loss = ["DEFEAT", "NIEDERLAGE"], draw = ["DRAW"];
        Assert.Equal(MatchResult.Win, TextParsing.Result("Victory!", win, loss, draw));
        Assert.Equal(MatchResult.Loss, TextParsing.Result("niederlage", win, loss, draw));
        Assert.Null(TextParsing.Result("VICTORY DEFEAT", win, loss, draw));
        Assert.Null(TextParsing.Result("SCOREBOARD", win, loss, draw));
    }

    [Fact]
    public void Anchors_tolerate_one_wrong_letter_in_long_words()
    {
        Assert.True(TextParsing.ContainsAny("SC0REBOARD", ["SCOREBOARD"]));
        Assert.False(TextParsing.ContainsAny("SC0REB0ARD", ["SCOREBOARD"]));
        Assert.False(TextParsing.ContainsAny("WIM", ["WIN"]));
    }

    private static readonly string[] Weapons = ["HAN 86", "SANG 9MM", "M4", "KASTOV 762", "PPSH-41", "REACTOR U92"];

    [Theory]
    [InlineData("SANG SMM", "SANG 9MM")] // as read on a real report
    [InlineData("sang 9mm", "SANG 9MM")]
    [InlineData("HAN 86", "HAN 86")]
    [InlineData("HAN B6", "HAN 86")]
    [InlineData("KAST0V 762", "KASTOV 762")]
    [InlineData("PPSH 41", "PPSH-41")]
    [InlineData("M4", "M4")]
    [InlineData("REACTOR IJ92", "REACTOR U92")] // a U read as two strokes
    [InlineData("REACTOR 1.192", "REACTOR U92")]
    public void A_weapon_is_read_despite_look_alike_characters(string read, string weapon) =>
        Assert.Equal(weapon, TextParsing.OneOf(read, Weapons));

    [Theory]
    [InlineData("SANG")] // half a name
    [InlineData("3MM")]
    [InlineData("KASTOV 545")] // another weapon of the same family is not this one
    [InlineData("TOPLEISTUNG")]
    [InlineData("")]
    public void A_text_that_is_no_known_weapon_stays_unknown(string read) =>
        Assert.Null(TextParsing.OneOf(read, Weapons));

    [Fact]
    public void Names_that_look_alike_cannot_both_be_listed()
    {
        var profile = new RecognitionProfile
        {
            Id = "test",
            Screen = ScreenKind.AfterAction,
            Game = Game.Mw4Multiplayer,
            Aspect = 1.7778,
            Anchors = [new Anchor(new Region(0.1, 0.1, 0.1, 0.1), ["X"])],
            Fields = new Dictionary<string, FieldSpec> { [Fields.Weapon] = new(new Region(0.1, 0.1, 0.1, 0.1)) { OneOf = ["SANG 9MM", "SANG SMM"] } },
        };
        Assert.Contains(profile.Problems(), p => p.Contains("nicht unterscheidbar", StringComparison.Ordinal));
    }
}

public class DerivedValueTests
{
    [Fact]
    public void The_final_score_comes_from_the_line_or_from_both_colours()
    {
        var notes = new List<string>();
        Assert.Equal([200, 174], MatchStateMachine.Scoreline([200, 174], 200, null, notes));
        Assert.Equal([100, 95], MatchStateMachine.Scoreline(null, 100, 95, notes)); // as on 25.10.: the line unread
        Assert.Null(MatchStateMachine.Scoreline(null, 250, null, notes));
        Assert.Empty(notes);
        Assert.Null(MatchStateMachine.Scoreline([0, 174], 200, 174, notes)); // the line misread "200" as "0"
        Assert.Single(notes);
    }

    [Theory]
    [InlineData(null, 45, 1.67, 27)] // live on 25.10.: the 27 unread, the ratio says it
    [InlineData(27, 45, 1.67, 27)]
    [InlineData(null, 18, 2.00, 9)]
    [InlineData(null, 45, null, null)]
    [InlineData(5, 0, 0.00, 5)]
    public void Deaths_come_from_the_ratio_when_exactly_one_value_fits(int? deaths, int? eliminations, double? ratio, int? expected)
    {
        var doubts = new List<string>();
        Assert.Equal(expected, MatchStateMachine.Deaths(deaths, eliminations, ratio, doubts));
        Assert.Empty(doubts);
    }

    [Fact]
    public void Deaths_that_do_not_fit_the_ratio_are_a_doubt()
    {
        var doubts = new List<string>();
        Assert.Equal(21, MatchStateMachine.Deaths(21, 45, 1.67, doubts));
        Assert.Single(doubts);
        // 12 eliminations at 12.00 fit both 0 and 1 death (the game prints the eliminations without deaths): unknown.
        Assert.Null(MatchStateMachine.Deaths(null, 12, 12.00, []));
        // Several fits (50 / 100 and 50 / 101 both print 0.50): the deaths stay unknown.
        Assert.Null(MatchStateMachine.Deaths(null, 50, 0.50, []));
    }

    [Theory]
    [InlineData("1.67", 1.67)]
    [InlineData(" 0,85 ", 0.85)]
    [InlineData("12.00", 12.0)]
    [InlineData("1.6", null)]
    [InlineData("ELIM", null)]
    public void Ratios_are_read_with_two_decimals(string text, double? expected) => Assert.Equal(expected, TextParsing.Ratio(text));

    [Theory]
    [InlineData(25, null, 30, 5)] // TDM: 30 eliminations, 25 kills
    [InlineData(19, null, 31, 12)] // Stellung shows no assists column
    [InlineData(10, 7, 17, 7)]
    [InlineData(10, null, null, null)]
    [InlineData(null, null, 20, null)]
    public void Assists_are_eliminations_minus_kills(int? kills, int? assists, int? eliminations, int? expected)
    {
        var doubts = new List<string>();
        Assert.Equal(expected, MatchStateMachine.Assists(kills, assists, eliminations, doubts));
        Assert.Empty(doubts);
    }

    [Fact]
    public void Values_that_do_not_add_up_are_a_doubt()
    {
        var doubts = new List<string>();
        Assert.Equal(4, MatchStateMachine.Assists(25, 4, 30, doubts));
        Assert.Null(MatchStateMachine.Assists(25, null, 20, doubts));
        Assert.Equal(2, doubts.Count);
    }

    [Theory]
    [InlineData(3, MatchResult.Win, 25, 29, 3)]
    [InlineData(1, MatchResult.Win, 24, 22, 1)]
    [InlineData(null, MatchResult.Win, 24, 22, 1)] // first by the report's scoreline
    [InlineData(null, MatchResult.Loss, 21, 30, null)]
    [InlineData(7, MatchResult.Loss, 21, 30, 7)]
    public void A_free_for_all_place_that_fits_the_match_is_kept(int? placement, MatchResult result, int own, int other, int? expected)
    {
        var doubts = new List<string>();
        Assert.Equal(expected, MatchStateMachine.FreeForAllPlacement(placement, result, [own, other], doubts));
        Assert.Empty(doubts);
    }

    [Theory]
    [InlineData(2, MatchResult.Loss, 21, 30)] // SIEG is the top three
    [InlineData(5, MatchResult.Win, 25, 29)]
    [InlineData(1, MatchResult.Win, 25, 29)] // first, but someone has more points
    [InlineData(2, MatchResult.Win, 30, 25)] // more points than everyone else, but not first
    public void A_free_for_all_place_that_contradicts_the_match_is_dropped(int placement, MatchResult result, int own, int other)
    {
        var doubts = new List<string>();
        Assert.Null(MatchStateMachine.FreeForAllPlacement(placement, result, [own, other], doubts));
        Assert.Single(doubts);
    }
}

public class MatchStateMachineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-2);
    private static readonly ProfileWords Words = new(["VICTORY"], ["DEFEAT"], ["DRAW"]);

    private static Observation Result(int second, string kills, string deaths, string result = "VICTORY", double quality = 1.0,
        Game? game = Game.Mw4Multiplayer, string? placement = null, Dictionary<string, string>? more = null)
    {
        var fields = new Dictionary<string, FieldReading>
        {
            [Fields.Kills] = new(kills, quality),
            [Fields.Deaths] = new(deaths, quality),
            [Fields.Result] = new(result, quality),
        };
        if (placement is not null)
        {
            fields[Fields.Placement] = new(placement, quality);
        }
        foreach (var (name, text) in more ?? [])
        {
            fields[name] = new(text, quality);
        }
        return new Observation(Start.AddSeconds(second), ScreenKind.Result, game, "profile", fields);
    }

    private static Observation Report(int second, string result = "VICTORY", Dictionary<string, string>? more = null)
    {
        var fields = new Dictionary<string, FieldReading>
        {
            [Fields.Result] = new(result, 1),
            [Fields.Mode] = new("KAPERN", 1),
            [Fields.Map] = new("MUNITION", 1),
            [Fields.Scoreline] = new("300 0", 1),
            [Fields.Xp] = new("12410", 1),
            [Fields.Level] = new("26", 1),
        };
        foreach (var (name, text) in more ?? [])
        {
            fields[name] = new(text, 1);
        }
        return new Observation(Start.AddSeconds(second), ScreenKind.AfterAction, Game.Mw4Multiplayer, "report", fields);
    }

    private static Observation Screen(int second, ScreenKind screen) => Observation.Of(Start.AddSeconds(second), screen, Game.Mw4Multiplayer);

    private static MatchStateMachine Machine() => new(_ => Words);

    /// <summary>Feeds observations and returns the matches they produced.</summary>
    private static List<MatchCandidate> Feed(MatchStateMachine machine, params Observation[] observations) =>
        observations.Select(machine.Observe).OfType<MatchCandidate>().ToList();

    [Fact]
    public void A_match_ends_with_its_after_action_report()
    {
        var machine = Machine();
        var matches = Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "23", "11"), Result(601, "23", "11"), Result(602, "23", "11"),
            Screen(612, ScreenKind.Unknown),
            Report(630), Report(631), Report(632));

        var match = Assert.Single(matches);
        Assert.Equal(23, match.Kills);
        Assert.Equal(11, match.Deaths);
        Assert.Equal(MatchResult.Win, match.Result);
        Assert.Equal(MatchStatus.Completed, match.Status);
        Assert.Equal("KAPERN", match.Mode);
        Assert.Equal("MUNITION", match.Map);
        Assert.Equal((300, 0), (match.TeamScore, match.EnemyScore));
        Assert.Equal(12410, match.Xp);
        Assert.Equal(26, match.Level);
        Assert.Empty(match.Doubts);
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void A_report_closed_at_once_is_confirmed_by_the_lobby()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.InMatch),
            Result(600, "23", "11"), Result(601, "23", "11"), Result(602, "23", "11"),
            Report(630), Screen(632, ScreenKind.Lobby)));
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Without_a_report_the_lobby_confirms_the_end()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "23", "11"), Result(601, "23", "11"), Result(602, "23", "11"),
            Screen(612, ScreenKind.Unknown), Screen(640, ScreenKind.Lobby)));
        Assert.Equal(MatchResult.Win, match.Result);
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void A_round_result_followed_by_gameplay_is_no_match()
    {
        // Kollateral, Front, Kapern: the round's result screen looks like the match's ("RUNDENSIEG" is often misread).
        var machine = Machine();
        Assert.Empty(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(300, "3", "5"), Result(301, "3", "5"), Result(302, "3", "5"),
            Screen(312, ScreenKind.Unknown), Screen(330, ScreenKind.InMatch)));
        Assert.Equal(MatchPhase.Playing, machine.Phase);

        var match = Assert.Single(Feed(machine,
            Result(600, "8", "8"), Result(601, "8", "8"), Result(602, "8", "8"),
            Report(630), Report(631), Report(632)));
        Assert.Equal(8, match.Kills);
        Assert.Empty(match.Doubts);
    }

    [Fact]
    public void Leaving_after_a_round_is_a_left_match_without_result()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(300, "3", "5", result: "DEFEAT"), Result(301, "3", "5", result: "DEFEAT"),
            Screen(312, ScreenKind.Unknown), Screen(400, ScreenKind.Menu)));

        Assert.Equal(MatchStatus.Left, match.Status);
        Assert.Null(match.Result);
        Assert.Equal(3, match.Kills);
        // That it was left is certain; it goes up on its own and counts for K/D, not for the win rate.
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Leaving_with_unreadable_values_is_still_a_left_match()
    {
        // Kollateral, left after the first round with 0/0: the own row's zeros are yellow on yellow and unreadable.
        var machine = Machine();
        var round = new Observation(Start.AddSeconds(300), ScreenKind.Result, Game.Mw4Multiplayer, "profile",
            new Dictionary<string, FieldReading> { [Fields.Mode] = new("KOLLATERAL", 1), [Fields.Result] = new("NNIEDERLAGE", 1) });
        var match = Assert.Single(Feed(machine, Screen(0, ScreenKind.Loading), round, Screen(312, ScreenKind.Unknown),
            Screen(320, ScreenKind.InMatch), Screen(370, ScreenKind.Menu)));

        Assert.Equal(MatchStatus.Left, match.Status);
        Assert.Equal("KOLLATERAL", match.Mode);
        Assert.Null(match.Kills);
        Assert.Null(match.Result);
    }

    [Fact]
    public void The_report_is_recorded_once()
    {
        var machine = Machine();
        Feed(machine, Screen(0, ScreenKind.InMatch), Result(600, "5", "5"), Result(601, "5", "5"), Result(602, "5", "5"),
            Report(630), Report(631), Report(632));
        Assert.Equal(MatchPhase.Recorded, machine.Phase);
        // The report stays up, is opened again from the lobby, the result screen may be seen again.
        Assert.Empty(Feed(machine, Report(633), Screen(640, ScreenKind.Lobby), Report(650), Result(660, "5", "5"), Screen(700, ScreenKind.Menu)));
    }

    [Fact]
    public void Disagreeing_readings_are_doubtful_and_wait_for_review()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "23", "11"), Result(601, "28", "11"), Result(602, "23", "11"),
            Report(630), Report(631), Report(632)));
        Assert.Equal(23, match.Kills);
        Assert.Contains(match.Doubts, d => d.StartsWith(Fields.Kills, StringComparison.Ordinal));
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Kills_differing_between_table_and_report_are_doubtful()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "8", "8"), Result(601, "8", "8"), Result(602, "8", "8"),
            Report(630, more: new() { [Fields.Kills] = "9" }), Report(631, more: new() { [Fields.Kills] = "9" }), Report(632, more: new() { [Fields.Kills] = "9" })));
        Assert.Equal(8, match.Kills);
        Assert.Contains(match.Doubts, d => d.Contains("Einsatzbericht", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_seen_start_the_match_waits_for_review()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine, Result(600, "9", "3"), Result(601, "9", "3"), Result(602, "9", "3"), Screen(640, ScreenKind.Lobby)));
        Assert.Contains("Matchbeginn nicht gesehen", match.Doubts);
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void A_report_alone_counts_for_review()
    {
        // The client was started while the report was up.
        var machine = Machine();
        var match = Assert.Single(Feed(machine, Report(10, more: new() { [Fields.Kills] = "25", [Fields.Deaths] = "22" }),
            Report(11, more: new() { [Fields.Kills] = "25", [Fields.Deaths] = "22" }), Report(12, more: new() { [Fields.Kills] = "25", [Fields.Deaths] = "22" })));
        Assert.Equal(25, match.Kills);
        Assert.Equal(22, match.Deaths);
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void An_unconfirmed_end_waits_for_review()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "23", "11"), Result(601, "23", "11"), Result(602, "23", "11"),
            Screen(612, ScreenKind.Unknown), Screen(800, ScreenKind.Unknown)));
        Assert.Contains(match.Doubts, d => d.Contains("nicht bestätigt", StringComparison.Ordinal));
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Unverified_profiles_never_produce_safe_matches()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "23", "11", quality: 0.5), Result(601, "23", "11", quality: 0.5), Result(602, "23", "11", quality: 0.5),
            Screen(640, ScreenKind.Lobby)));
        Assert.True(match.Confidence < MatchStateMachine.SafeConfidence);
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Free_for_all_keeps_its_placement_and_the_top_three_win()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "25", "22", placement: "3"), Result(601, "25", "22", placement: "3"), Result(602, "25", "22", placement: "3"),
            Report(630), Screen(640, ScreenKind.Lobby)));
        Assert.Equal(3, match.Placement);
        Assert.Equal(MatchResult.Win, match.Result);
        Assert.Empty(MatchRules.Validate(RecognitionLoop.ToLocal(match)!.ToUpload(), DateTimeOffset.Now));
    }

    [Fact]
    public void Mode_values_come_from_table_and_report()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Screen(0, ScreenKind.Loading),
            Result(600, "8", "8", more: new() { ["stat.captures"] = "2" }), Result(601, "8", "8", more: new() { ["stat.captures"] = "2" }),
            Result(602, "8", "8", more: new() { ["stat.captures"] = "2" }),
            Report(630, more: new() { ["stat.data_uploaded"] = "77", ["stat.captures"] = "3" }),
            Report(631, more: new() { ["stat.data_uploaded"] = "77" }), Report(632, more: new() { ["stat.data_uploaded"] = "77" })));
        Assert.Equal(2, match.ModeStats["captures"]);
        Assert.Equal(77, match.ModeStats["data_uploaded"]);
    }

    [Fact]
    public void Warzone_win_comes_from_placement_one()
    {
        var machine = Machine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.WarzoneBattleRoyale));
        for (var i = 0; i < 2; i++)
        {
            machine.Observe(Result(1500 + i, "7", "0", result: "", game: Game.WarzoneBattleRoyale, placement: "#1"));
        }
        var match = machine.Observe(Result(1502, "7", "0", result: "", game: Game.WarzoneBattleRoyale, placement: "#1"));
        Assert.Equal(1, match!.Placement);
        Assert.Equal(MatchResult.Win, match.Result);
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Unknown_game_is_not_stored()
    {
        var machine = Machine();
        var match = Assert.Single(Feed(machine,
            Observation.Of(Start, ScreenKind.Loading),
            Result(1, "3", "1", game: null), Result(2, "3", "1", game: null), Result(3, "3", "1", game: null),
            Observation.Of(Start.AddSeconds(40), ScreenKind.Lobby)));
        Assert.Null(RecognitionLoop.ToLocal(match));
    }
}

public class NameTrackerTests
{
    [Fact]
    public void A_name_is_confirmed_after_three_different_matches()
    {
        var names = new NameTracker();
        for (var i = 0; i < 2; i++)
        {
            names.NextMatch();
            Assert.False(names.Observe(new FieldReading("ShadowPlayer", 1)));
        }
        names.NextMatch();
        Assert.True(names.Observe(new FieldReading(" ShadowPlayer ", 1)));
        Assert.Equal("ShadowPlayer", names.Confirmed);
    }

    [Fact]
    public void Many_readings_in_one_match_count_once()
    {
        var names = new NameTracker();
        names.NextMatch();
        for (var i = 0; i < 10; i++)
        {
            names.Observe(new FieldReading("ShadowPlayer", 1));
        }
        Assert.Null(names.Confirmed);
        Assert.Equal("ShadowPlayer", names.Candidate);
    }

    [Fact]
    public void Weak_readings_and_rival_names_keep_it_uncertain()
    {
        var names = new NameTracker();
        for (var i = 0; i < 3; i++)
        {
            names.NextMatch();
            names.Observe(new FieldReading("Alpha", 0.5));
        }
        Assert.Null(names.Confirmed);

        for (var i = 0; i < 3; i++)
        {
            names.NextMatch();
            names.Observe(new FieldReading("Alpha", 1));
            names.Observe(new FieldReading("Bravo", 1));
        }
        Assert.Null(names.Confirmed);
    }
}

public class ProfileTests
{
    private const string Valid = """
        {
          "id": "test-result-16x9",
          "game": "mw4_mp",
          "screen": "result",
          "aspect": 1.7778,
          "verified": true,
          "anchors": [ { "region": { "x": 0.4, "y": 0.05, "width": 0.2, "height": 0.08 }, "anyOf": ["VICTORY", "DEFEAT"] } ],
          "fields": {
            "kills": { "region": { "x": 0.6, "y": 0.4, "width": 0.05, "height": 0.04 } }
          },
          "winWords": ["VICTORY"], "lossWords": ["DEFEAT"]
        }
        """;

    [Fact]
    public void Profiles_load_and_invalid_ones_are_reported()
    {
        using var temp = new TempFolder();
        var builtIn = Directory.CreateDirectory(Path.Combine(temp.Path, "builtin")).FullName;
        var user = Directory.CreateDirectory(Path.Combine(temp.Path, "user")).FullName;
        File.WriteAllText(Path.Combine(builtIn, "a.json"), Valid);
        File.WriteAllText(Path.Combine(builtIn, "broken.json"), Valid.Replace("\"width\": 0.05", "\"width\": 1.5", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(user, "a.json"), Valid.Replace("\"verified\": true", "\"verified\": false", StringComparison.Ordinal));
        var warnings = new List<string>();

        var profiles = ProfileStore.Load([builtIn, user], warnings.Add);

        var profile = Assert.Single(profiles);
        Assert.False(profile.Verified);
        Assert.Equal(Game.Mw4Multiplayer, profile.Game);
        Assert.Equal(ScreenKind.Result, profile.Screen);
        Assert.Single(warnings);
        Assert.True(profile.FitsAspect(2560.0 / 1440));
        Assert.False(profile.FitsAspect(2560.0 / 1080));
    }

    [Fact]
    public async Task Screen_reader_uses_anchors_then_reads_fields()
    {
        var profile = System.Text.Json.JsonSerializer.Deserialize<RecognitionProfile>(Valid, ProfileStore.Json)!;
        var reader = new ScreenReader([profile], new FakeText(region => region.X == 0.4 ? "VICTORY" : "17"));
        using var frame = new Frame(1920, 1080);

        var observation = await reader.ReadAsync(frame, DateTimeOffset.Now, CancellationToken.None);

        Assert.Equal(ScreenKind.Result, observation.Screen);
        Assert.Equal("17", observation.Fields[Fields.Kills].Text);

        var other = new ScreenReader([profile], new FakeText(_ => "LOADOUT"));
        Assert.Equal(ScreenKind.Unknown, (await other.ReadAsync(frame, DateTimeOffset.Now, CancellationToken.None)).Screen);
        using var ultrawide = new Frame(3440, 1440);
        Assert.Equal(ScreenKind.Unknown, (await reader.ReadAsync(ultrawide, DateTimeOffset.Now, CancellationToken.None)).Screen);
    }

    private sealed class FakeText(Func<Region, string> text) : ITextReader
    {
        public Task<string> ReadAsync(Frame frame, Region region, CancellationToken cancel) => Task.FromResult(text(region));

        public Task<IReadOnlyList<TextLine>> ReadLinesAsync(Frame frame, Region region, CancellationToken cancel) =>
            Task.FromResult<IReadOnlyList<TextLine>>([new TextLine(text(region), region.X, region.Y, region.Width, region.Height)]);
    }
}

public class FrameTests
{
    [Fact]
    public void Crop_copies_the_region_and_scales_small_text_up()
    {
        using var frame = new Frame(100, 50);
        // Mark pixel (60, 20) red.
        frame.Pixels[(20 * 100 + 60) * 4 + 2] = 255;
        using var crop = frame.Crop(new Region(0.6, 0.4, 0.1, 0.2), minHeight: 20);
        Assert.Equal(20, crop.Height);
        Assert.Equal(20, crop.Width);
        Assert.Equal(255, crop.Pixels[2]);
        Assert.Equal(255, crop.Pixels[(1 * 20 + 1) * 4 + 2]);
        Assert.Equal(0, crop.Pixels[(0 * 20 + 2) * 4 + 2]);
    }
}

/// <summary>Real Windows OCR on rendered text: proves the OCR path works, independent of any game.</summary>
public class WindowsOcrTests
{
    [Fact]
    public async Task Reads_rendered_numbers_and_words()
    {
        var ocr = WindowsOcr.Create();
        Assert.NotNull(ocr);
        using var frame = Render(["VICTORY", "KILLS 23", "DEATHS 11"], 640, 360);

        var title = await ocr.ReadAsync(frame, new Region(0, 0, 1, 0.33), CancellationToken.None);
        var kills = await ocr.ReadAsync(frame, new Region(0, 0.33, 1, 0.33), CancellationToken.None);

        Assert.True(TextParsing.ContainsAny(title, ["VICTORY"]), $"read '{title}'");
        Assert.Contains("23", kills, StringComparison.Ordinal);
    }

    /// <summary>Draws white lines of text on a dark background, like a HUD, into a BGRA frame.</summary>
    internal static Frame Render(string[] lines, int width, int height)
    {
        Frame? result = null;
        var thread = new Thread(() =>
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 22, 28)), null, new Rect(0, 0, width, height));
                for (var i = 0; i < lines.Length; i++)
                {
                    var text = new FormattedText(lines[i], System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"), height / 6.0, Brushes.White, 1.0);
                    context.DrawText(text, new Point(20, i * height / lines.Length + height / 12.0));
                }
            }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var frame = new Frame(width, height);
            var pixels = new byte[width * height * 4];
            bitmap.CopyPixels(pixels, width * 4, 0);
            pixels.CopyTo(frame.Pixels);
            result = frame;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result!;
    }
}

/// <summary>
/// Real Windows Graphics Capture of a real window, then OCR. Needs an interactive desktop, so it does nothing on
/// CI runners (CI=true); locally it proves the whole capture path without a game.
/// </summary>
public class WindowCaptureTests
{
    [Fact]
    public async Task Captures_a_window_and_reads_its_text()
    {
        if (Environment.GetEnvironmentVariable("CI") == "true" || !WindowCapture.IsSupported)
        {
            return;
        }
        var ready = new TaskCompletionSource<IntPtr>();
        var done = new TaskCompletionSource();
        var thread = new Thread(() =>
        {
            var window = new Window
            {
                Width = 480,
                Height = 270,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Topmost = true,
                Background = new SolidColorBrush(Color.FromRgb(20, 22, 28)),
                Content = new System.Windows.Controls.TextBlock { Text = "VICTORY 23", FontSize = 56, Foreground = Brushes.White, Margin = new Thickness(24) },
            };
            window.ContentRendered += (_, _) => ready.TrySetResult(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            _ = done.Task.ContinueWith(_ => window.Dispatcher.InvokeShutdown(), TaskScheduler.Default);
            window.Show();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            var handle = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var capture = WindowCapture.Start(handle);
            Assert.NotNull(capture);
            using var frame = await capture.GrabAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(frame);
            Assert.InRange(frame.Width, 400, 1200);

            var text = await WindowsOcr.Create()!.ReadAsync(frame, new Region(0, 0, 1, 0.6), CancellationToken.None);
            Assert.True(TextParsing.ContainsAny(text, ["VICTORY"]) && text.Contains("23", StringComparison.Ordinal), $"read '{text}'");
        }
        finally
        {
            done.TrySetResult();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}

public class ResultTimingTests
{
    [Fact]
    public void A_short_result_screen_is_kept_until_the_report_comes()
    {
        // As recorded on a real PC: the result showed for two readings, then the highlight replay (unknown
        // screens), then the after-action report about 25 seconds later.
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var machine = new MatchStateMachine(_ => new(["SIEG"], ["NIEDERLAGE"], []));
        machine.Observe(Observation.Of(start, ScreenKind.InMatch, Game.Mw4Multiplayer));
        var result = new Observation(start.AddMinutes(9), ScreenKind.Result, Game.Mw4Multiplayer, "r",
            new Dictionary<string, FieldReading> { [Fields.Kills] = new("19", 1), [Fields.Deaths] = new("24", 1), [Fields.Result] = new("SIEG", 1) });
        var report = new Observation(result.At.AddSeconds(25), ScreenKind.AfterAction, Game.Mw4Multiplayer, "a",
            new Dictionary<string, FieldReading> { [Fields.Result] = new("SIEG", 1) });
        Assert.Null(machine.Observe(result));
        Assert.Null(machine.Observe(result with { At = result.At.AddSeconds(1) }));
        Assert.Null(machine.Observe(Observation.Of(result.At.AddSeconds(10), ScreenKind.Unknown)));
        Assert.Equal(MatchPhase.AwaitingEnd, machine.Phase);
        Assert.Null(machine.Observe(report));
        Assert.Null(machine.Observe(report with { At = report.At.AddSeconds(1) }));
        var match = machine.Observe(report with { At = report.At.AddSeconds(2) });
        Assert.Equal(19, match!.Kills);
        Assert.Equal(MatchResult.Win, match.Result);
    }
}

public class WindowResizeTests
{
    [Fact]
    public async Task Capture_follows_a_window_that_grows_after_capture_started()
    {
        // The game starts in one size and goes to its full resolution later; frames must follow (issue #35).
        if (Environment.GetEnvironmentVariable("CI") == "true" || !WindowCapture.IsSupported)
        {
            return;
        }
        var ready = new TaskCompletionSource<IntPtr>();
        var done = new TaskCompletionSource();
        Window? window = null;
        var thread = new Thread(() =>
        {
            window = new Window
            {
                Width = 320,
                Height = 180,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                Topmost = true,
                Background = new SolidColorBrush(Color.FromRgb(20, 22, 28)),
                Content = new System.Windows.Controls.TextBlock { Text = "LOBBY", FontSize = 40, Foreground = Brushes.White },
            };
            // A game redraws all the time, and Windows only delivers frames when something changed: keep redrawing.
            var ticks = 0;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            timer.Tick += (_, _) => ((System.Windows.Controls.TextBlock)window.Content).Text = $"LOBBY {++ticks}";
            timer.Start();
            window.ContentRendered += (_, _) => ready.TrySetResult(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            _ = done.Task.ContinueWith(_ => window.Dispatcher.InvokeShutdown(), TaskScheduler.Default);
            window.Show();
            System.Windows.Threading.Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            var handle = await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var capture = WindowCapture.Start(handle);
            Assert.NotNull(capture);
            using (var first = await capture.GrabAsync(TimeSpan.FromSeconds(5), CancellationToken.None))
            {
                Assert.NotNull(first);
                Assert.InRange(first.Width, 300, 800);
            }

            await window!.Dispatcher.InvokeAsync(() =>
            {
                window.Width = 960;
                window.Height = 540;
            });

            // A few grabs: the first after the resize may still be in flight at the old size.
            var width = 0;
            for (var i = 0; i < 10 && width < 900; i++)
            {
                await Task.Delay(150);
                using var frame = await capture.GrabAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
                width = frame?.Width ?? width;
            }
            Assert.True(width >= 900, $"frame stayed {width} px wide after the window grew");
        }
        finally
        {
            done.TrySetResult();
            thread.Join(TimeSpan.FromSeconds(5));
        }
    }
}

public class SameNameTests
{
    [Theory]
    [InlineData("[TLS]Tabsi98", "[TLS]Tabsi98", true)]
    [InlineData("[TLSITabsi98", "[TLS]Tabsi98", true)]   // "]" read as "I" at 1080p
    [InlineData("[TLS] Tabsi98", "Tabsi98", true)]
    [InlineData("Tabsi98", "Tabsi99", false)]
    [InlineData("[TLS]Tabsi98", "[TLS]Day", false)]
    [InlineData("Day", "Holiday", false)]
    public void Banner_and_table_names_match_despite_misread_tags(string banner, string table, bool same) =>
        Assert.Equal(same, ScreenReader.SameName(banner, table));
}

public class ScoreboardTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-1);

    private static Observation Tab(int second, string kills, string deaths) => new(Start.AddSeconds(second), ScreenKind.InMatch,
        Game.Mw4Multiplayer, "tab", new Dictionary<string, FieldReading> { [Fields.Kills] = new(kills, 1), [Fields.Deaths] = new(deaths, 1) });

    [Fact]
    public void Opening_the_scoreboard_mid_match_counts_nothing()
    {
        var machine = new MatchStateMachine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        for (var i = 1; i <= 10; i++)
        {
            Assert.Null(machine.Observe(Tab(i * 30, (i * 2).ToString(System.Globalization.CultureInfo.InvariantCulture), "3")));
        }
        Assert.Equal(MatchPhase.Playing, machine.Phase);
    }

    [Fact]
    public void A_proper_result_screen_wins_over_the_scoreboard()
    {
        var machine = new MatchStateMachine(_ => new(["VICTORY"], [], []));
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        machine.Observe(Tab(300, "12", "5"));
        for (var i = 0; i < 3; i++)
        {
            machine.Observe(new Observation(Start.AddSeconds(600 + i), ScreenKind.Result, Game.Mw4Multiplayer, "res",
                new Dictionary<string, FieldReading> { [Fields.Kills] = new("20", 1), [Fields.Deaths] = new("9", 1), [Fields.Result] = new("VICTORY", 1) }));
        }
        var match = machine.Observe(Observation.Of(Start.AddSeconds(640), ScreenKind.Lobby, Game.Mw4Multiplayer));
        Assert.Equal(20, match!.Kills);
        Assert.Null(machine.Observe(Observation.Of(Start.AddSeconds(700), ScreenKind.Menu)));
    }

    [Fact]
    public void Leaving_mid_match_keeps_the_last_scoreboard_as_left_match()
    {
        var machine = new MatchStateMachine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        machine.Observe(Tab(200, "8", "4"));
        machine.Observe(Tab(400, "15", "7"));
        var match = machine.Observe(Observation.Of(Start.AddSeconds(500), ScreenKind.Menu));

        Assert.Equal(15, match!.Kills);
        Assert.Equal(MatchStatus.Left, match.Status);
        Assert.Null(match.Result);
        var local = RecognitionLoop.ToLocal(match)!;
        Assert.Equal(ReviewStatus.Auto, local.ReviewStatus);
        Assert.Equal(MatchStatus.Left, local.ToUpload().Status);
    }
}

public class MemoryGuardTests
{
    [Fact]
    public void Memory_stays_bounded_while_frames_come_in()
    {
        // As in a real 4K session: a 33 MB frame plus a few large crops every two seconds (issue #26).
        long highest = 0;
        for (var i = 0; i < 120; i++)
        {
            using (var frame = new Frame(3840, 2160))
            {
                frame.Pixels[i] = 1;
                using var crop = frame.Crop(new Region(0.05, 0.55, 0.45, 0.31) { MinPixelHeight = 670 });
            }
            MemoryGuard.AfterFrame();
            highest = Math.Max(highest, GC.GetTotalMemory(false));
        }
        Assert.True(highest < MemoryGuard.Limit + 128L * 1024 * 1024, $"managed memory reached {highest / 1048576} MB");
    }
}
