using CodStats.Desktop.Recognition;

namespace CodStats.Desktop.Capture;

/// <summary>
/// One captured image in memory (BGRA, 4 bytes per pixel). Dispose clears the pixels and drops the buffer, so
/// they never outlive the analysis and are never written anywhere.
/// </summary>
/// <remarks>
/// Not from ArrayPool.Shared: a 4K frame is 33 MB, and the shared pool keeps such arrays per processor core.
/// On a real PC that grew the client to 2.7 GB while it played (issue #26).
/// </remarks>
public sealed class Frame : IDisposable
{
    private byte[]? pixels;
    private readonly bool owned = true;

    public Frame(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        pixels = new byte[width * height * 4];
    }

    /// <summary>A frame over a buffer someone else owns and reuses (the capture's one frame buffer).</summary>
    internal Frame(int width, int height, byte[] buffer)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(buffer.Length, width * height * 4);
        Width = width;
        Height = height;
        pixels = buffer;
        owned = false;
    }

    public int Width { get; }
    public int Height { get; }
    public double Aspect => (double)Width / Height;

    public Span<byte> Pixels => (pixels ?? throw new ObjectDisposedException(nameof(Frame))).AsSpan(0, Width * Height * 4);

    /// <summary>The underlying array, for copying a capture straight in without another 33 MB buffer.</summary>
    internal byte[] Buffer => pixels ?? throw new ObjectDisposedException(nameof(Frame));

    /// <summary>The pixel rectangle of a relative region, clamped to the frame.</summary>
    public (int X, int Y, int Width, int Height) ToPixels(Region region)
    {
        ArgumentNullException.ThrowIfNull(region);
        var x = Math.Clamp((int)Math.Round(region.X * Width), 0, Width - 1);
        var y = Math.Clamp((int)Math.Round(region.Y * Height), 0, Height - 1);
        var w = Math.Clamp((int)Math.Round(region.Width * Width), 1, Width - x);
        var h = Math.Clamp((int)Math.Round(region.Height * Height), 1, Height - y);
        return (x, y, w, h);
    }

    /// <summary>
    /// A copy of a region, scaled so its height is at least <paramref name="minHeight"/> pixels (OCR reads small
    /// HUD text much better when it is larger). The caller disposes it.
    /// </summary>
    public Frame Crop(Region region, int minHeight = 48, int maxDimension = int.MaxValue)
    {
        var (x, y, w, h) = ToPixels(region);
        // Up for small HUD text, down for huge banners (MaxHeight); nearest neighbour is enough for the OCR.
        var scale = region.MaxHeight > 0 && h > region.MaxHeight
            ? (double)region.MaxHeight / h
            : Math.Clamp(Math.Ceiling((double)Math.Max(minHeight, region.MinPixelHeight) / h), 1, 4);
        // Never larger than the OCR accepts.
        scale = Math.Min(scale, Math.Max(1.0, (double)maxDimension / Math.Max(w, h)));
        var crop = new Frame(Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
        var source = Pixels;
        var target = crop.Pixels;
        for (var row = 0; row < crop.Height; row++)
        {
            var sourceRow = (y + Math.Min(h - 1, (int)(row / scale))) * Width * 4;
            var targetRow = row * crop.Width * 4;
            for (var column = 0; column < crop.Width; column++)
            {
                source.Slice(sourceRow + (x + Math.Min(w - 1, (int)(column / scale))) * 4, 4).CopyTo(target.Slice(targetRow + column * 4, 4));
            }
        }
        if (region.WhiteText)
        {
            KeepWhiteText(crop.Pixels);
        }
        return crop;
    }

    private const byte WhiteThreshold = 215;

    private static void KeepWhiteText(Span<byte> bgra)
    {
        for (var i = 0; i < bgra.Length; i += 4)
        {
            var white = bgra[i] >= WhiteThreshold && bgra[i + 1] >= WhiteThreshold && bgra[i + 2] >= WhiteThreshold;
            var value = white ? (byte)0 : (byte)255;
            bgra[i] = value;
            bgra[i + 1] = value;
            bgra[i + 2] = value;
            bgra[i + 3] = 255;
        }
    }

    public void Dispose()
    {
        if (pixels is not null && owned)
        {
            Array.Clear(pixels);
        }
        pixels = null;
    }
}
