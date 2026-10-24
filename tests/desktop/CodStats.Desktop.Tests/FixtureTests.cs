using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodStats.Contracts;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Recognition;

namespace CodStats.Desktop.Tests;

/// <summary>
/// The shipped profiles against real game captures in tests/capture-fixtures. The images are not in the repository
/// (other players' names are visible), so on CI and on PCs without them these tests check nothing and say so.
/// </summary>
public class FixtureTests
{
    private static readonly string Root = FindRoot();

    public static TheoryData<string, string> Fixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var expected in Directory.EnumerateFiles(Path.Combine(Root, "tests", "capture-fixtures"), "expected.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(expected));
            foreach (var entry in document.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('_')))
            {
                data.Add(Path.GetRelativePath(Root, Path.Combine(Path.GetDirectoryName(expected)!, entry.Name)), entry.Value.GetRawText());
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Profiles_read_real_captures(string image, string expectedJson)
    {
        var path = Path.Combine(Root, image);
        if (!File.Exists(path))
        {
            return; // Image only on the PC it was captured on.
        }
        var expected = JsonDocument.Parse(expectedJson).RootElement;
        var profiles = ProfileStore.Load([Path.Combine(Root, "apps", "desktop", "CodStats.Desktop", "profiles")]);
        var ocr = WindowsOcr.Create()!;
        using var frame = Load(path);

        var observation = await new ScreenReader(profiles, ocr).ReadAsync(frame, DateTimeOffset.Now, CancellationToken.None);

        Assert.Equal(expected.GetProperty("screen").GetString(), Name(observation.Screen));
        if (expected.TryGetProperty("game", out var game))
        {
            Assert.Equal(game.GetString(), observation.Game is { } g ? WireNames.Of(g) : null);
        }
        string Field(string name) => observation.Fields.TryGetValue(name, out var f) ? f.Text : "";
        void Number(string key, string field)
        {
            if (expected.TryGetProperty(key, out var value))
            {
                Assert.True(value.GetInt32() == TextParsing.Count(Field(field)).Value, $"{key}: expected {value}, read '{Field(field)}'");
            }
        }
        Number("kills", Fields.Kills);
        Number("deaths", Fields.Deaths);
        Number("score", Fields.Score);
        if (expected.TryGetProperty("result", out var result))
        {
            var words = profiles.First(p => p.Id == observation.ProfileId).Words;
            Assert.Equal(result.GetString(), TextParsing.Result(Field(Fields.Result), words.Win, words.Loss, words.Draw) is { } r ? WireNames.Of(r) : null);
        }
        if (expected.TryGetProperty("mode", out var mode))
        {
            Assert.Equal(mode.GetString(), Field(Fields.Mode));
        }
        if (expected.TryGetProperty("ownName", out var ownName))
        {
            Assert.Equal(ownName.GetString(), DisplayNames.WithoutClanTag(observation.OwnName!.Text));
        }
    }

    private static string Name(ScreenKind screen) => screen switch
    {
        ScreenKind.InMatch => "inMatch",
        _ => screen.ToString().ToLowerInvariant(),
    };

    private static Frame Load(string path)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        var frame = new Frame(source.PixelWidth, source.PixelHeight);
        var pixels = new byte[frame.Pixels.Length];
        source.CopyPixels(pixels, source.PixelWidth * 4, 0);
        pixels.CopyTo(frame.Pixels);
        return frame;
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "cod-stats-tracker.slnx")))
        {
            directory = directory.Parent;
        }
        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
