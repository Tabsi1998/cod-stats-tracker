// Records frames of the running Call of Duty window into tests/capture-fixtures (git-ignored: other players'
// names are visible). Only for building recognition profiles; the client itself never stores frames.
//
//   dotnet run --project tools/CaptureFixtures -- <output folder> [seconds between frames] [minutes]

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Recognition;

var output = args.Length > 0 ? args[0] : Path.Combine("tests", "capture-fixtures", "raw");
var interval = TimeSpan.FromSeconds(args.Length > 1 ? double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture) : 3);
var until = DateTime.Now.AddMinutes(args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 30);
Directory.CreateDirectory(output);

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
        if (frame is not null)
        {
            var thumbnail = Thumbnail(frame);
            // Skip frames that barely changed (menus, waiting), so a match is a few hundred files, not thousands.
            if (previous is null || Difference(previous, thumbnail) > 6)
            {
                var file = Path.Combine(output, $"{DateTime.Now:yyyyMMdd_HHmmss}_{frame.Width}x{frame.Height}.jpg");
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
