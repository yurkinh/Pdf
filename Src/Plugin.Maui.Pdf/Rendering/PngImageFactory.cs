using System.Buffers.Binary;
using System.IO.Compression;
using Melville.Pdf.MauiGraphics.Drawing;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Plugin.Maui.Pdf.Rendering;

/// <summary>
/// Creates platform images by encoding the pixels as PNG and loading that. Works on every platform, but costs an
/// encode and a decode; used where no native factory exists yet (Windows) and as a cross-check.
/// </summary>
public sealed class PngImageFactory : IPdfImageFactory
{
    /// <summary>The shared instance.</summary>
    public static PngImageFactory Instance { get; } = new();

    /// <inheritdoc />
    public IImage Create(PdfImage image)
    {
        using var png = new MemoryStream();
        WritePng(image, png);
        png.Position = 0;
        return PlatformImage.FromStream(png, ImageFormat.Png);
    }

    /// <summary>Encode premultiplied BGRA as an 8-bit RGBA PNG (PNG stores straight, not premultiplied, alpha).</summary>
    public static void WritePng(PdfImage image, Stream output)
    {
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // colour type: RGBA
        WriteChunk(output, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + image.Width * 4]; // filter byte 0 (none), then pixels
            for (int y = 0; y < image.Height; y++)
            {
                var source = image.Pbgra.AsSpan(y * image.Width * 4, image.Width * 4);
                for (int x = 0; x < image.Width; x++)
                {
                    int s = x * 4, d = 1 + x * 4;
                    var a = source[s + 3];
                    row[d] = Unpremultiply(source[s + 2], a);
                    row[d + 1] = Unpremultiply(source[s + 1], a);
                    row[d + 2] = Unpremultiply(source[s], a);
                    row[d + 3] = a;
                }
                zlib.Write(row);
            }
        }
        WriteChunk(output, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(output, "IEND"u8, []);
    }

    private static byte Unpremultiply(byte value, byte alpha) =>
        alpha switch { 0 => (byte)0, 255 => value, _ => (byte)Math.Min(255, (value * 255 + alpha / 2) / alpha) };

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        output.Write(type);
        output.Write(data);
        var crc = Crc32.Update(Crc32.Update(0xFFFFFFFFu, type), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
        {
            var c = (uint)n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            return c;
        }).ToArray();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }
}
