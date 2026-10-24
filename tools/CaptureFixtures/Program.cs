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
    foreach (var line in await ocr.ReadLinesAsync(image, new Region(numbers[0], numbers[1], numbers[2], numbers[3]) { WhiteText = args.Length == 7 && args[6] == "white", MaxHeight = args.Length == 7 && int.TryParse(args[6], out var max) ? max : 0 }, CancellationToken.None))
    {
        Console.WriteLine($"x={line.Left:0.000}..{line.Left + line.Width:0.000} y={line.Top:0.000}..{line.Top + line.Height:0.000}  '{line.Text}'");
    }
    return;
}

// "shot <process name> <file.png>": one capture of a program's main window, e.g. to look at the client's dashboard.
if (args.Length == 3 && args[0] == "shot")
{
    var window = System.Diagnostics.Process.GetProcessesByName(args[1]).Select(p => p.MainWindowHandle).FirstOrDefault(h => h != IntPtr.Zero);
    using var shooter = WindowCapture.Start(window) ?? throw new InvalidOperationException("window not found");
    using var image = await shooter.GrabAsync(TimeSpan.FromSeconds(3), CancellationToken.None) ?? throw new InvalidOperationException("no frame");
    var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels.ToArray(), image.Width * 4);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(bitmap));
    using var stream = File.Create(args[2]);
    encoder.Save(stream);
    return;
}

// "leaktest <frames> <ocr>": grabs frames from the running game like the client does and prints private memory, to
// find what holds on to memory. ocr = 0 (capture only) or 1 (capture + reading with the profiles).
if (args.Length == 3 && args[0] == "leaktest")
{
    var count = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
    var withOcr = args[2] == "1";
    var reader = new ScreenReader(ProfileStore.Load([Path.Combine("apps", "desktop", "CodStats.Desktop", "profiles")]), WindowsOcr.Create()!);
    using var grabber = WindowCapture.Start(GameWatcher.FindGameWindow()) ?? throw new InvalidOperationException("no game window");
    using var self = System.Diagnostics.Process.GetCurrentProcess();
    for (var i = 1; i <= count; i++)
    {
        using (var frame = await grabber.GrabAsync(TimeSpan.FromSeconds(2), CancellationToken.None))
        {
            if (frame is not null && withOcr)
            {
                await reader.ReadAsync(frame, DateTimeOffset.Now, CancellationToken.None);
            }
        }
        MemoryGuard.AfterFrame();
        if (i % 10 == 0)
        {
            if (Environment.GetEnvironmentVariable("LEAK_GC") == "1")
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            self.Refresh();
            Console.WriteLine($"{i} frames: private {self.PrivateMemorySize64 / 1048576} MB, managed {GC.GetTotalMemory(false) / 1048576} MB, gen2 {GC.CollectionCount(2)}");
        }
        await Task.Delay(200);
    }
    return;
}

// "import <server> <client data folder> <folder> [...]": replays the recordings, stores every recognised match
// in a client database (as confirmed by the player) and syncs it through the real client code to a server.
// For a demo on a local test server; never against a public server with someone else's recordings.
if (args.Length >= 4 && args[0] == "import")
{
    var server = new Uri(args[1].TrimEnd('/') + "/");
    var paths = new CodStats.Desktop.AppPaths(Path.GetFullPath(args[2]));
    paths.Ensure();
    var store = new CodStats.Desktop.Storage.MatchStore(paths.Database);
    store.Initialise();
    var (matches, name) = await Replay(args[3..]);
    foreach (var candidate in matches)
    {
        if (RecognitionLoop.ToLocal(candidate) is { } local)
        {
            var stored = store.Add(local with { ReviewStatus = CodStats.Contracts.ReviewStatus.Confirmed });
            Console.WriteLine($"{(stored ? "stored " : "already")} {local.PlayedAt:HH:mm} {local.Game} {local.Mode} {local.Result} K={local.Kills} D={local.Deaths} A={local.Assists} S={local.Score}");
        }
    }
    using var identity = CodStats.Desktop.Identity.DeviceIdentity.LoadOrCreate(paths.Identity);
    using var sync = new CodStats.Desktop.Sync.SyncService(store, identity, () => new CodStats.Desktop.Sync.ApiClient(server, identity), paths.Identity);
    var status = await sync.RunAsync(new CodStats.Desktop.Sync.ProfileWish(name, name is not null, true), CancellationToken.None);
    Console.WriteLine($"sync: {status.Text} {status.Detail} profile={identity.ProfileId} name={name}");
    return;
}

// "replay <folder> [<folder> ...]": the recorded frames in time order through the real state machine, as the
// client would see them while playing; prints every match it would store.
if (args.Length >= 2 && args[0] == "replay")
{
    var profiles = ProfileStore.Load([Path.Combine("apps", "desktop", "CodStats.Desktop", "profiles")]);
    var reader = new ScreenReader(profiles, WindowsOcr.Create()!);
    var machine = new MatchStateMachine(id => profiles.FirstOrDefault(p => p.Id == id)?.Words ?? ProfileWords.None);
    var names = new NameTracker();
    var files = args[1..].SelectMany(folder => Directory.EnumerateFiles(folder, "*.jpg"))
        .OrderBy(f => Path.GetFileName(f)[..15], StringComparer.Ordinal);
    var previousPhase = machine.Phase;
    foreach (var file in files)
    {
        var at = DateTime.ParseExact(Path.GetFileName(file)[..15], "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        using var image = LoadImage(file);
        var observation = await reader.ReadAsync(image, new DateTimeOffset(at), CancellationToken.None);
        var before = machine.Phase;
        var candidate = machine.Observe(observation);
        if (before != MatchPhase.Playing && machine.Phase == MatchPhase.Playing)
        {
            names.NextMatch();
        }
        names.Observe(observation.OwnName is { } own ? own with { Quality = 1 } : null);
        if (machine.Phase != previousPhase)
        {
            Console.WriteLine($"{at:HH:mm:ss} {previousPhase} -> {machine.Phase} ({observation.Screen})");
            previousPhase = machine.Phase;
        }
        if (candidate is not null)
        {
            var local = RecognitionLoop.ToLocal(candidate);
            Console.WriteLine($"  MATCH {candidate.Game} {candidate.Mode} {candidate.Result} K={candidate.Kills} D={candidate.Deaths} A={candidate.Assists} S={candidate.Score} " +
                $"conf={candidate.Confidence:0.00} -> {local?.ReviewStatus.ToString() ?? "not stored"} doubts: {string.Join("; ", candidate.Doubts)}");
        }
    }
    Console.WriteLine($"name: confirmed={names.Confirmed ?? "-"} candidate={names.Candidate ?? "-"}");
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
    // CODSTATS_WIDTH scales images down, to see how a capture would read at 1440p or 1080p.
    var decoder = BitmapDecoder.Create(new Uri(Path.GetFullPath(path)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    BitmapSource image = decoder.Frames[0];
    if (int.TryParse(Environment.GetEnvironmentVariable("CODSTATS_WIDTH"), out var width) && width != image.PixelWidth)
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

static async Task<(List<MatchCandidate> Matches, string? Name)> Replay(string[] folders)
{
    var profiles = ProfileStore.Load([Path.Combine("apps", "desktop", "CodStats.Desktop", "profiles")]);
    var reader = new ScreenReader(profiles, WindowsOcr.Create()!);
    var machine = new MatchStateMachine(id => profiles.FirstOrDefault(p => p.Id == id)?.Words ?? ProfileWords.None);
    var names = new NameTracker();
    var matches = new List<MatchCandidate>();
    var files = folders.SelectMany(folder => Directory.EnumerateFiles(folder, "*.jpg"))
        .OrderBy(f => Path.GetFileName(f)[..15], StringComparer.Ordinal);
    foreach (var file in files)
    {
        var at = DateTime.ParseExact(Path.GetFileName(file)[..15], "yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        using var image = LoadImage(file);
        var observation = await reader.ReadAsync(image, new DateTimeOffset(at), CancellationToken.None);
        var before = machine.Phase;
        if (machine.Observe(observation) is { } candidate)
        {
            matches.Add(candidate);
        }
        if (before != MatchPhase.Playing && machine.Phase == MatchPhase.Playing)
        {
            names.NextMatch();
        }
        names.Observe(observation.OwnName);
    }
    if (machine.Reset() is { } last)
    {
        matches.Add(last);
    }
    return (matches, names.Confirmed);
}
