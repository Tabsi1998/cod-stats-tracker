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

    /// <summary>
    /// Every capture also scaled down to 2560x1440 and 1920x1080: same layout, smaller text. A downscaled 4K image is
    /// not exactly what the game renders at that size, but it shows where small text stops being readable.
    /// </summary>
    private static readonly int[] Widths = [3840, 2560, 1920];

    public static TheoryData<string, string, int> Fixtures()
    {
        var data = new TheoryData<string, string, int>();
        foreach (var expected in Directory.EnumerateFiles(Path.Combine(Root, "tests", "capture-fixtures"), "expected.json", SearchOption.AllDirectories))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(expected));
            foreach (var entry in document.RootElement.EnumerateObject().Where(e => !e.Name.StartsWith('_')))
            {
                foreach (var width in Widths)
                {
                    data.Add(Path.GetRelativePath(Root, Path.Combine(Path.GetDirectoryName(expected)!, entry.Name)), entry.Value.GetRawText(), width);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Profiles_read_real_captures(string image, string expectedJson, int width)
    {
        var path = Path.Combine(Root, image);
        if (!File.Exists(path))
        {
            return; // Image only on the PC it was captured on.
        }
        if (width > BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0].PixelWidth)
        {
            return; // Scaling a capture up says nothing about a real higher resolution.
        }
        var expected = JsonDocument.Parse(expectedJson).RootElement;
        var profiles = ProfileStore.Load([Path.Combine(Root, "apps", "desktop", "CodStats.Desktop", "profiles")]);
        var ocr = WindowsOcr.Create()!;
        using var frame = Load(path, width);

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
        Number("assists", Fields.Assists);
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

    private static Frame Load(string path, int width)
    {
        var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        BitmapSource image = decoder.Frames[0];
        if (image.PixelWidth != width)
        {
            var scale = (double)width / image.PixelWidth;
            image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        }
        var source = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
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
