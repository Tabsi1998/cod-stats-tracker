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
}

public class MatchStateMachineTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-2);
    private static readonly ProfileWords Words = new(["VICTORY"], ["DEFEAT"], ["DRAW"]);

    private static Observation Result(int second, string kills, string deaths, string result = "VICTORY", double quality = 1.0, Game game = Game.Mw4Multiplayer, string? placement = null)
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
        return new Observation(Start.AddSeconds(second), ScreenKind.Result, game, "profile", fields);
    }

    private static MatchStateMachine Machine() => new(_ => Words);

    [Fact]
    public void A_clean_match_becomes_a_safe_candidate()
    {
        var machine = Machine();
        Assert.Null(machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer)));
        Assert.Null(machine.Observe(Result(600, "23", "11")));
        Assert.Null(machine.Observe(Result(602, "23", "11")));
        var match = machine.Observe(Result(604, "23", "11"));

        Assert.NotNull(match);
        Assert.Equal(23, match.Kills);
        Assert.Equal(11, match.Deaths);
        Assert.Equal(MatchResult.Win, match.Result);
        Assert.Empty(match.Doubts);
        Assert.True(match.Confidence >= MatchStateMachine.SafeConfidence);
        Assert.NotNull(RecognitionLoop.ToLocal(match));
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void The_same_result_screen_is_recorded_once()
    {
        var machine = Machine();
        machine.Observe(Observation.Of(Start, ScreenKind.InMatch, Game.Mw4Multiplayer));
        for (var i = 0; i < 3; i++)
        {
            machine.Observe(Result(600 + i, "5", "5"));
        }
        Assert.Equal(MatchPhase.Recorded, machine.Phase);
        Assert.Null(machine.Observe(Result(610, "5", "5")));
        Assert.Null(machine.Observe(Observation.Of(Start.AddSeconds(620), ScreenKind.Menu)));
        Assert.Null(machine.Observe(Result(630, "5", "5")));
    }

    [Fact]
    public void Disagreeing_readings_are_doubtful_and_wait_for_review()
    {
        var machine = Machine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        machine.Observe(Result(600, "23", "11"));
        machine.Observe(Result(602, "28", "11"));
        var match = machine.Observe(Result(604, "23", "11"));

        Assert.Equal(23, match!.Kills);
        Assert.Contains(match.Doubts, d => d.StartsWith(Fields.Kills, StringComparison.Ordinal));
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Without_a_seen_start_the_match_waits_for_review()
    {
        var machine = Machine();
        machine.Observe(Result(600, "9", "3"));
        machine.Observe(Result(601, "9", "3"));
        var match = machine.Observe(Result(602, "9", "3"));
        Assert.Contains("Matchbeginn nicht gesehen", match!.Doubts);
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }

    [Fact]
    public void Unverified_profiles_never_produce_safe_matches()
    {
        var machine = Machine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        machine.Observe(Result(600, "23", "11", quality: 0.5));
        machine.Observe(Result(601, "23", "11", quality: 0.5));
        var match = machine.Observe(Result(602, "23", "11", quality: 0.5));
        Assert.True(match!.Confidence < MatchStateMachine.SafeConfidence);
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
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
    public void Leaving_the_result_screen_early_still_keeps_the_match_for_review()
    {
        var machine = Machine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        machine.Observe(Result(600, "12", "4"));
        var match = machine.Observe(Observation.Of(Start.AddSeconds(605), ScreenKind.Menu));
        Assert.Equal(12, match!.Kills);
        Assert.Contains(match.Doubts, d => d.Contains("Lesung", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_game_is_not_stored()
    {
        var machine = Machine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading));
        var reading = new Observation(Start.AddSeconds(1), ScreenKind.Result, null, null, new Dictionary<string, FieldReading> { [Fields.Kills] = new("3", 1) });
        machine.Observe(reading);
        machine.Observe(reading with { At = Start.AddSeconds(2) });
        var match = machine.Observe(reading with { At = Start.AddSeconds(3) });
        Assert.Null(RecognitionLoop.ToLocal(match!));
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
    public void A_short_result_screen_is_evaluated_once_it_is_gone()
    {
        // As recorded on a real PC: the result showed for two readings, then the highlight replay (unknown screens).
        var start = DateTimeOffset.UtcNow.AddHours(-1);
        var machine = new MatchStateMachine(_ => new(["SIEG"], ["NIEDERLAGE"], []));
        machine.Observe(Observation.Of(start, ScreenKind.InMatch, Game.Mw4Multiplayer));
        var result = new Observation(start.AddMinutes(9), ScreenKind.Result, Game.Mw4Multiplayer, "r",
            new Dictionary<string, FieldReading> { [Fields.Kills] = new("19", 1), [Fields.Deaths] = new("24", 1), [Fields.Result] = new("SIEG", 1) });
        Assert.Null(machine.Observe(result));
        Assert.Null(machine.Observe(result with { At = result.At.AddSeconds(1) }));
        Assert.Null(machine.Observe(Observation.Of(result.At.AddSeconds(4), ScreenKind.Unknown)));
        var match = machine.Observe(Observation.Of(result.At.AddSeconds(10), ScreenKind.Unknown));
        Assert.Equal(19, match!.Kills);
        Assert.Equal(MatchResult.Win, match.Result);
    }
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
        MatchCandidate? match = null;
        for (var i = 0; i < 3; i++)
        {
            match = machine.Observe(new Observation(Start.AddSeconds(600 + i), ScreenKind.Result, Game.Mw4Multiplayer, "res",
                new Dictionary<string, FieldReading> { [Fields.Kills] = new("20", 1), [Fields.Deaths] = new("9", 1), [Fields.Result] = new("VICTORY", 1) }));
        }
        Assert.Equal(20, match!.Kills);
        Assert.Null(machine.Observe(Observation.Of(Start.AddSeconds(700), ScreenKind.Menu)));
    }

    [Fact]
    public void Leaving_without_result_keeps_the_last_scoreboard_for_review()
    {
        var machine = new MatchStateMachine();
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        machine.Observe(Tab(200, "8", "4"));
        machine.Observe(Tab(400, "15", "7"));
        var match = machine.Observe(Observation.Of(Start.AddSeconds(500), ScreenKind.Menu));

        Assert.Equal(15, match!.Kills);
        Assert.Contains(match.Doubts, d => d.Contains("Scoreboard", StringComparison.Ordinal));
        Assert.Equal(ReviewStatus.Pending, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }
}
