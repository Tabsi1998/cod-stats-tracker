using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodStats.Contracts;
using CodStats.Desktop.Capture;
using CodStats.Desktop.Recognition;

namespace CodStats.Desktop.Tests;

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
