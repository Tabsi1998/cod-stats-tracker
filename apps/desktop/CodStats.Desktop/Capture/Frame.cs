using System.Buffers;
using CodStats.Desktop.Recognition;

namespace CodStats.Desktop.Capture;

/// <summary>
/// One captured image in memory (BGRA, 4 bytes per pixel). The buffer is rented and returned on Dispose, so
/// pixels never outlive the analysis and are never written anywhere.
/// </summary>
public sealed class Frame : IDisposable
{
    private byte[]? pixels;

    public Frame(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        pixels = ArrayPool<byte>.Shared.Rent(width * height * 4);
        // The shared pool may hand out an array someone else returned without clearing it.
        Pixels.Clear();
    }

    public int Width { get; }
    public int Height { get; }
    public double Aspect => (double)Width / Height;

    public Span<byte> Pixels => (pixels ?? throw new ObjectDisposedException(nameof(Frame))).AsSpan(0, Width * Height * 4);

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
    public Frame Crop(Region region, int minHeight = 48)
    {
        var (x, y, w, h) = ToPixels(region);
        // Up for small HUD text, down for huge banners (MaxHeight); nearest neighbour is enough for the OCR.
        var scale = region.MaxHeight > 0 && h > region.MaxHeight
            ? (double)region.MaxHeight / h
            : Math.Clamp(Math.Ceiling((double)minHeight / h), 1, 4);
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
        if (pixels is not null)
        {
            // Clear before returning: the pool must not hand out game pixels to anyone else.
            Array.Clear(pixels);
            ArrayPool<byte>.Shared.Return(pixels);
            pixels = null;
        }
    }
}
