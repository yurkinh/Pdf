using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Drawing;

/// <summary>
/// A decoded image as premultiplied BGRA pixels (the format IPdfBitmap and shaders produce).
/// The platform image is created on first use for each <see cref="IPdfImageFactory"/> and kept.
/// </summary>
public sealed class PdfImage
{
    private readonly ConditionalWeakTable<IPdfImageFactory, IImage> platformImages = new();

    /// <summary>Width in pixels.</summary>
    public int Width { get; }

    /// <summary>Height in pixels.</summary>
    public int Height { get; }

    /// <summary>Premultiplied BGRA bytes, row major, Width * 4 bytes per row.</summary>
    public byte[] Pbgra { get; }

    /// <summary>Wrap existing premultiplied BGRA pixels.</summary>
    public PdfImage(int width, int height, byte[] pbgra)
    {
        if (pbgra.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than width * height * 4.", nameof(pbgra));
        Width = width;
        Height = height;
        Pbgra = pbgra;
    }

    /// <summary>
    /// Allocate a pixel buffer and let <paramref name="fill"/> write it through a pointer. The buffer is on
    /// the pinned object heap, so the pointer stays valid across awaits inside <paramref name="fill"/>.
    /// </summary>
    public static async ValueTask<PdfImage> CreateAsync(int width, int height, Func<nint, ValueTask> fill)
    {
        var pixels = GC.AllocateArray<byte>(width * height * 4, pinned: true);
        await fill(Address(pixels)).ConfigureAwait(false);
        return new PdfImage(width, height, pixels);
    }

    /// <summary>Synchronous variant of <see cref="CreateAsync"/>.</summary>
    public static PdfImage Create(int width, int height, Action<nint> fill)
    {
        var pixels = GC.AllocateArray<byte>(width * height * 4, pinned: true);
        fill(Address(pixels));
        return new PdfImage(width, height, pixels);
    }

    private static unsafe nint Address(byte[] pinned) =>
        (nint)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(pinned));

    /// <summary>
    /// A resized copy. Bilinear filtering smooths; otherwise each output pixel takes the nearest source pixel,
    /// which keeps scanned or pixel-art images crisp. Works on premultiplied values, so edges against
    /// transparency stay correct.
    /// </summary>
    public PdfImage Resize(int width, int height, bool bilinear)
    {
        var result = GC.AllocateArray<byte>(width * height * 4, pinned: true);
        var source = Pbgra;
        int sw = Width, sh = Height;
        for (int y = 0; y < height; y++)
        {
            var sy = (y + 0.5) * sh / height - 0.5;
            for (int x = 0; x < width; x++)
            {
                var sx = (x + 0.5) * sw / width - 0.5;
                var o = (y * width + x) * 4;
                if (!bilinear)
                {
                    var i = (Math.Clamp((int)Math.Round(sy), 0, sh - 1) * sw + Math.Clamp((int)Math.Round(sx), 0, sw - 1)) * 4;
                    result[o] = source[i]; result[o + 1] = source[i + 1]; result[o + 2] = source[i + 2]; result[o + 3] = source[i + 3];
                    continue;
                }
                int x0 = Math.Clamp((int)Math.Floor(sx), 0, sw - 1), y0 = Math.Clamp((int)Math.Floor(sy), 0, sh - 1);
                int x1 = Math.Min(x0 + 1, sw - 1), y1 = Math.Min(y0 + 1, sh - 1);
                var fx = Math.Clamp(sx - x0, 0, 1);
                var fy = Math.Clamp(sy - y0, 0, 1);
                for (int c = 0; c < 4; c++)
                {
                    var top = source[(y0 * sw + x0) * 4 + c] * (1 - fx) + source[(y0 * sw + x1) * 4 + c] * fx;
                    var bottom = source[(y1 * sw + x0) * 4 + c] * (1 - fx) + source[(y1 * sw + x1) * 4 + c] * fx;
                    result[o + c] = (byte)Math.Round(top * (1 - fy) + bottom * fy);
                }
            }
        }
        return new PdfImage(width, height, result);
    }

    /// <summary>A copy reduced by averaging blocks of pixels, for images far larger than they are drawn.</summary>
    public PdfImage Shrink(int width, int height)
    {
        var result = GC.AllocateArray<byte>(width * height * 4, pinned: true);
        for (int y = 0; y < height; y++)
        {
            int y0 = y * Height / height, y1 = Math.Max(y0 + 1, (y + 1) * Height / height);
            for (int x = 0; x < width; x++)
            {
                int x0 = x * Width / width, x1 = Math.Max(x0 + 1, (x + 1) * Width / width);
                long b = 0, g = 0, r = 0, a = 0;
                for (int sy = y0; sy < y1; sy++)
                for (int sx = x0; sx < x1; sx++)
                {
                    var i = (sy * Width + sx) * 4;
                    b += Pbgra[i]; g += Pbgra[i + 1]; r += Pbgra[i + 2]; a += Pbgra[i + 3];
                }
                var n = (x1 - x0) * (y1 - y0);
                var o = (y * width + x) * 4;
                result[o] = (byte)(b / n); result[o + 1] = (byte)(g / n); result[o + 2] = (byte)(r / n); result[o + 3] = (byte)(a / n);
            }
        }
        return new PdfImage(width, height, result);
    }

    /// <summary>The image for a factory, created once.</summary>
    public IImage PlatformImage(IPdfImageFactory factory) =>
        platformImages.GetValue(factory, f => f.Create(this));

    /// <summary>The average colour, used where a patterned fill cannot be drawn (strokes).</summary>
    public Color AverageColor()
    {
        long b = 0, g = 0, r = 0, a = 0;
        var count = (long)Width * Height;
        if (count == 0) return Colors.Transparent;
        for (int i = 0; i < count * 4; i += 4)
        {
            b += Pbgra[i]; g += Pbgra[i + 1]; r += Pbgra[i + 2]; a += Pbgra[i + 3];
        }
        if (a == 0) return Colors.Transparent;
        // Sums of premultiplied channels divided by the alpha sum give the unpremultiplied average.
        return Color.FromRgba((float)r / a, (float)g / a, (float)b / a, (float)a / (count * 255));
    }
}

/// <summary>Creates platform images (Maui.Graphics IImage) from decoded pixels.</summary>
public interface IPdfImageFactory
{
    /// <summary>Create the platform image for these pixels.</summary>
    IImage Create(PdfImage image);
}
