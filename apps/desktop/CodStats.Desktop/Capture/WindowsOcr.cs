using System.Runtime.InteropServices.WindowsRuntime;
using CodStats.Desktop.Recognition;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CodStats.Desktop.Capture;

/// <summary>
/// The OCR built into Windows 10/11: no extra download, runs locally. It reports no per-word confidence, so the
/// state machine judges readings by how clean they parse and whether several frames agree.
/// </summary>
public sealed class WindowsOcr : ITextReader
{
    private readonly OcrEngine engine;

    private WindowsOcr(OcrEngine engine) => this.engine = engine;

    /// <summary>
    /// The player's Windows language first: the game follows it (German HUD on a German PC, with the umlaut in
    /// "ABSCHÜSSE"). English when that language has no OCR installed.
    /// </summary>
    public static WindowsOcr? Create()
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages() ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"));
        return engine is null ? null : new WindowsOcr(engine);
    }

    public string Language => engine.RecognizerLanguage.LanguageTag;

    public async Task<string> ReadAsync(Frame frame, Region region, CancellationToken cancel)
    {
        var (result, _, _) = await RecognizeAsync(frame, region, cancel);
        return string.Join(' ', result.Lines.Select(line => line.Text));
    }

    public async Task<IReadOnlyList<TextLine>> ReadLinesAsync(Frame frame, Region region, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(region);
        var (result, cropWidth, cropHeight) = await RecognizeAsync(frame, region, cancel);
        return result.Lines.Select(line =>
        {
            var left = line.Words.Min(word => word.BoundingRect.X);
            var right = line.Words.Max(word => word.BoundingRect.X + word.BoundingRect.Width);
            var top = line.Words.Min(word => word.BoundingRect.Y);
            var bottom = line.Words.Max(word => word.BoundingRect.Y + word.BoundingRect.Height);
            // Back from crop pixels (possibly scaled up) to the frame's 0..1 coordinates.
            return new TextLine(line.Text,
                region.X + left / cropWidth * region.Width, region.Y + top / cropHeight * region.Height,
                (right - left) / cropWidth * region.Width, (bottom - top) / cropHeight * region.Height);
        }).ToList();
    }

    private async Task<(OcrResult Result, int CropWidth, int CropHeight)> RecognizeAsync(Frame frame, Region region, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var crop = frame.Crop(region);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(crop.Pixels.ToArray().AsBuffer(), BitmapPixelFormat.Bgra8, crop.Width, crop.Height, BitmapAlphaMode.Premultiplied);
        return (await engine.RecognizeAsync(bitmap).AsTask(cancel), crop.Width, crop.Height);
    }
}
