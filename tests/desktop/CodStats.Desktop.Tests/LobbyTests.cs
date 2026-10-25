using CodStats.Contracts;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Recognition;

namespace CodStats.Desktop.Tests;

/// <summary>
/// The lobby from the final scoreboard: two tables as the OCR returns them (lines with positions), test data only.
/// Each table has a name column at 0.05 from its left edge and a score column under "PUNKTE".
/// </summary>
public class LobbyTests
{
    private const double RowHeight = 0.03;
    private static readonly TableSpec Left = new(new Region(0.05, 0.55, 0.45, 0.31), 0.03);
    private static readonly TableSpec Right = new(new Region(0.51, 0.55, 0.45, 0.31), 0.03);

    private static readonly OwnRowSpec Row = new([Left, Right], RowHeight,
        new Dictionary<string, IReadOnlyList<string>> { [Fields.Score] = ["PUNKTE"], [Fields.Kills] = ["ABSCHÜSSE"] })
    { Lobby = true };

    /// <summary>A table: heading line, then one name and one score per row; a null score leaves its cell unread.</summary>
    private static List<TextLine> Table(TableSpec table, params (string Name, string? Score)[] rows)
    {
        var x = table.Region.X;
        var lines = new List<TextLine> { new("NAME", x + 0.05, 0.555, 0.05, 0.015), new("PUNKTE", x + 0.30, 0.555, 0.06, 0.015) };
        for (var i = 0; i < rows.Length; i++)
        {
            var top = 0.59 + i * RowHeight;
            lines.Add(new TextLine(rows[i].Name, x + 0.05, top, 0.1, 0.015));
            if (rows[i].Score is { } score)
            {
                lines.Add(new TextLine(score, x + 0.301, top, 0.04, 0.015));
            }
        }
        return lines;
    }

    private static (int Size, int Rank)? Lobby(List<TextLine> left, List<TextLine> right, string own = "Tabsi98")
    {
        var name = left.Concat(right).Single(l => l.Text == own);
        var ownTable = left.Contains(name) ? Left : Right;
        return ScreenReader.Lobby([(Left, left), (Right, right)], Row, ownTable, name);
    }

    [Fact]
    public void Size_and_place_by_score_across_both_teams()
    {
        var left = Table(Left, ("Anna", "3200"), ("Tabsi98", "2010"), ("Ben", "900"));
        var right = Table(Right, ("Cleo", "2500"), ("Dan", "1800"), ("Eve", "1.200"));

        Assert.Equal((6, 3), Lobby(left, right));
    }

    [Fact]
    public void A_tie_counts_as_the_better_place()
    {
        var left = Table(Left, ("Tabsi98", "2000"), ("Ben", "900"));
        var right = Table(Right, ("Cleo", "2000"), ("Dan", "100"));

        Assert.Equal((4, 1), Lobby(left, right));
    }

    [Fact]
    public void An_unread_score_leaves_the_lobby_unknown()
    {
        var left = Table(Left, ("Anna", "3200"), ("Tabsi98", "2010"));
        var right = Table(Right, ("Cleo", null), ("Dan", "1800"));

        Assert.Null(Lobby(left, right));
    }

    [Fact]
    public void A_score_that_is_no_number_or_a_missing_heading_leaves_it_unknown()
    {
        var left = Table(Left, ("Anna", "3200"), ("Tabsi98", "2010"));
        Assert.Null(Lobby(left, Table(Right, ("Cleo", "2S0O?"))));

        var noHeading = Table(Right, ("Cleo", "2500")).Where(l => l.Text != "PUNKTE").ToList();
        Assert.Null(Lobby(left, noHeading));
    }

    [Fact]
    public void The_reader_puts_only_numbers_into_the_fields()
    {
        var left = Table(Left, ("Anna", "3200"), ("Tabsi98", "2010"));
        var right = Table(Right, ("Cleo", "2500"));
        var fields = Read(left, right);

        Assert.Equal("3", fields[Fields.LobbySize].Text);
        Assert.Equal("3", fields[Fields.LobbyRank].Text);
        Assert.DoesNotContain(fields.Values, f => f.Text.Contains("Anna", StringComparison.Ordinal) || f.Text.Contains("Cleo", StringComparison.Ordinal));
    }

    private static IReadOnlyDictionary<string, FieldReading> Read(List<TextLine> left, List<TextLine> right)
    {
        var profile = new RecognitionProfile
        {
            Id = "lobby-test",
            Game = Game.Mw4Multiplayer,
            Screen = ScreenKind.Result,
            Aspect = 16 / 9.0,
            Anchors = [],
            OwnRow = Row,
        };
        var reader = new Lines(region => region == Left.Region ? left : region == Right.Region ? right : []);
        using var frame = new Frame(16, 9);
        return new ScreenReader([profile], reader, () => "Tabsi98").ReadAsync(frame, DateTimeOffset.UtcNow, CancellationToken.None)
            .GetAwaiter().GetResult().Fields;
    }

    private sealed class Lines(Func<Region, IReadOnlyList<TextLine>> lines) : ITextReader
    {
        public Task<string> ReadAsync(Frame frame, Region region, CancellationToken cancel) =>
            Task.FromResult(string.Join(' ', lines(region).Select(l => l.Text)));

        public Task<IReadOnlyList<TextLine>> ReadLinesAsync(Frame frame, Region region, CancellationToken cancel) => Task.FromResult(lines(region));
    }
}

public class LobbyVoteTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddHours(-1);

    private static Observation Result(int second, string? size, string? rank)
    {
        var fields = new Dictionary<string, FieldReading>
        {
            [Fields.Kills] = new("20", 1),
            [Fields.Deaths] = new("9", 1),
            [Fields.Result] = new("VICTORY", 1),
        };
        if (size is not null && rank is not null)
        {
            fields[Fields.LobbySize] = new(size, 1);
            fields[Fields.LobbyRank] = new(rank, 1);
        }
        return new Observation(Start.AddSeconds(second), ScreenKind.Result, Game.Mw4Multiplayer, "res", fields);
    }

    private static MatchCandidate Play(params Observation[] results)
    {
        var machine = new MatchStateMachine(_ => new(["VICTORY"], [], []));
        machine.Observe(Observation.Of(Start, ScreenKind.Loading, Game.Mw4Multiplayer));
        foreach (var result in results)
        {
            machine.Observe(result);
        }
        return machine.Observe(Observation.Of(Start.AddSeconds(700), ScreenKind.Lobby, Game.Mw4Multiplayer))!;
    }

    [Fact]
    public void Agreeing_frames_give_the_lobby()
    {
        var match = Play(Result(600, "12", "3"), Result(601, "12", "3"), Result(602, "11", "3"));

        Assert.Equal((12, 3), (match.LobbySize, match.LobbyRank));
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
        Assert.Equal(12, RecognitionLoop.ToLocal(match)!.ToUpload().Details!.LobbySize);
    }

    [Fact]
    public void Disagreeing_frames_leave_the_lobby_unknown_but_the_match_safe()
    {
        var match = Play(Result(600, "12", "3"), Result(601, "11", "2"), Result(602, null, null));

        Assert.Null(match.LobbySize);
        Assert.Null(match.LobbyRank);
        Assert.Equal(ReviewStatus.Auto, RecognitionLoop.ToLocal(match)!.ReviewStatus);
    }
}
