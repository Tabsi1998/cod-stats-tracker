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

    /// <summary>English first (the game's HUD language is most often English), else the user's languages.</summary>
    public static WindowsOcr? Create()
    {
        var engine = OcrEngine.TryCreateFromLanguage(new Language("en-US")) ?? OcrEngine.TryCreateFromUserProfileLanguages();
        return engine is null ? null : new WindowsOcr(engine);
    }

    public string Language => engine.RecognizerLanguage.LanguageTag;

    public async Task<string> ReadAsync(Frame frame, Region region, CancellationToken cancel)
    {
        ArgumentNullException.ThrowIfNull(frame);
        using var crop = frame.Crop(region);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(crop.Pixels.ToArray().AsBuffer(), BitmapPixelFormat.Bgra8, crop.Width, crop.Height, BitmapAlphaMode.Premultiplied);
        var result = await engine.RecognizeAsync(bitmap).AsTask(cancel);
        return string.Join(' ', result.Lines.Select(line => line.Text));
    }
}
