// Records frames of the running Call of Duty window into tests/capture-fixtures (git-ignored: other players'
// names are visible). Only for building recognition profiles; the client itself never stores frames.
//
//   dotnet run --project tools/CaptureFixtures -- <output folder> [seconds between frames] [minutes]

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Recognition;

// "ocr <image> <x> <y> <width> <height>": what Windows OCR reads in a region of a saved image, with line positions.
if (args.Length is 6 or 7 && args[0] == "ocr")
{
    var numbers = args[2..6].Select(a => double.Parse(a, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
    using var image = LoadImage(args[1]);
    var ocr = WindowsOcr.Create()!;
    Console.WriteLine($"OCR language: {ocr.Language}");
    foreach (var line in await ocr.ReadLinesAsync(image, new Region(numbers[0], numbers[1], numbers[2], numbers[3]) { WhiteText = args.Length == 7 }, CancellationToken.None))
    {
        Console.WriteLine($"x={line.Left:0.000}..{line.Left + line.Width:0.000} y={line.Top:0.000}..{line.Top + line.Height:0.000}  '{line.Text}'");
    }
    return;
}

// "scan <folder>": every image of a folder through the profiles, one line each.
if (args.Length == 2 && args[0] == "scan")
{
    var profiles = ProfileStore.Load([Path.Combine("apps", "desktop", "CodStats.Desktop", "profiles")]);
    var reader = new ScreenReader(profiles, WindowsOcr.Create()!);
    foreach (var file in Directory.EnumerateFiles(args[1], "*.jpg").Order())
    {
        using var image = LoadImage(file);
        var observation = await reader.ReadAsync(image, DateTimeOffset.Now, CancellationToken.None);
        var fields = string.Join(" ", observation.Fields.Where(f => f.Key != "result").Select(f => $"{f.Key}={f.Value.Text}"));
        Console.WriteLine($"{Path.GetFileName(file)} {observation.Screen} {fields}");
    }
    return;
}

// "read <image> [profiles folder]": what the profiles make of a saved image.
if (args.Length >= 2 && args[0] == "read")
{
    using var image = LoadImage(args[1]);
    var profiles = ProfileStore.Load([args.Length > 2 ? args[2] : Path.Combine("apps", "desktop", "CodStats.Desktop", "profiles")], Console.WriteLine);
    var observation = await new ScreenReader(profiles, WindowsOcr.Create()!).ReadAsync(image, DateTimeOffset.Now, CancellationToken.None);
    Console.WriteLine($"{observation.Screen} {observation.Game} {observation.ProfileId} own='{observation.OwnName?.Text}'");
    foreach (var (name, field) in observation.Fields)
    {
        Console.WriteLine($"  {name} = '{field.Text}'");
    }
    return;
}

var output = args.Length > 0 ? args[0] : Path.Combine("tests", "capture-fixtures", "raw");
var interval = TimeSpan.FromSeconds(args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3);
var until = DateTime.Now.AddMinutes(args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 30);
Directory.CreateDirectory(output);

// Frames the profiles already know as gameplay are kept only every 20 s; everything else (menus, results, new
// layouts) whenever it changes, because that is what new profiles are built from.
var knownProfiles = ProfileStore.Load([Path.Combine("apps", "desktop", "CodStats.Desktop", "profiles")]);
var screens = new ScreenReader(knownProfiles, WindowsOcr.Create()!);
var lastGameplay = DateTime.MinValue;
WindowCapture? capture = null;
byte[]? previous = null;
var saved = 0;
while (DateTime.Now < until)
{
    capture ??= WindowCapture.Start(GameWatcher.FindGameWindow());
    if (capture is null)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} waiting for the CoD window");
        await Task.Delay(TimeSpan.FromSeconds(5));
        continue;
    }
    try
    {
        using var frame = await capture.GrabAsync(TimeSpan.FromSeconds(2), CancellationToken.None);
        if (frame is null)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} no frame within 2 s");
        }
        else
        {
            var screen = (await screens.ReadAsync(frame, DateTimeOffset.Now, CancellationToken.None)).Screen;
            if (screen == ScreenKind.InMatch && DateTime.Now - lastGameplay < TimeSpan.FromSeconds(20))
            {
                await Task.Delay(interval);
                continue;
            }
            if (screen == ScreenKind.InMatch)
            {
                lastGameplay = DateTime.Now;
            }
            var thumbnail = Thumbnail(frame);
            // Skip frames that barely changed (menus, waiting), so a match is a few hundred files, not thousands.
            if (previous is null || Difference(previous, thumbnail) > 6)
            {
                var file = Path.Combine(output, $"{DateTime.Now:yyyyMMdd_HHmmss}_{screen}_{frame.Width}x{frame.Height}.jpg");
                SaveJpeg(frame, file);
                previous = thumbnail;
                saved++;
                Console.WriteLine($"{DateTime.Now:HH:mm:ss} saved {Path.GetFileName(file)} ({saved})");
            }

        }
    }
    catch (Exception error) when (error is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException)
    {
        Console.WriteLine($"{DateTime.Now:HH:mm:ss} capture lost ({error.Message}), retrying");
        capture.Dispose();
        capture = null;
    }
    await Task.Delay(interval);
}
capture?.Dispose();
Console.WriteLine($"done, {saved} frames in {output}");

static byte[] Thumbnail(Frame frame)
{
    const int Size = 32;
    var result = new byte[Size * Size];
    var pixels = frame.Pixels;
    for (var y = 0; y < Size; y++)
    {
        for (var x = 0; x < Size; x++)
        {
            var i = ((y * frame.Height / Size) * frame.Width + x * frame.Width / Size) * 4;
            result[y * Size + x] = (byte)((pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3);
        }
    }
    return result;
}

static double Difference(byte[] a, byte[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Average();

static void SaveJpeg(Frame frame, string file)
{
    var bitmap = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, frame.Pixels.ToArray(), frame.Width * 4);
    var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(file);
    encoder.Save(stream);
}

static Frame LoadImage(string path)
{
    var decoder = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    var source = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
    var frame = new Frame(source.PixelWidth, source.PixelHeight);
    var pixels = new byte[frame.Pixels.Length];
    source.CopyPixels(pixels, source.PixelWidth * 4, 0);
    pixels.CopyTo(frame.Pixels);
    return frame;
}
