using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Melville.Pdf.LowLevel.Filters.FilterProcessing;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Writers;
using Melville.Pdf.ReferenceDocuments.Graphics.Images.Jpeg;
using Xunit;

namespace Melville.Pdf.DataModelTests.Standard.S7_4Filters;

public class S7_4_8DctDecodeFilter
{
    private static byte[] JpegBytes()
    {
        using var resource = typeof(JpegImage).Assembly
            .GetManifestResourceStream("Melville.Pdf.ReferenceDocuments.Graphics.Images.JPEG.jpg")!;
        using var copy = new MemoryStream();
        resource.CopyTo(copy);
        return copy.ToArray();
    }

    private static async Task<byte[]> DecodeAsync(byte[] jpeg)
    {
        var stream = new DictionaryBuilder()
            .WithFilter(FilterName.DCTDecode)
            .AsStream(new MemoryStream(jpeg), StreamFormat.DiskRepresentation);
        await using var decoded = await stream.StreamContentAsync();
        using var pixels = new MemoryStream();
        await decoded.CopyToAsync(pixels);
        return pixels.ToArray();
    }

    [Fact]
    public async Task DecodesTheSameWhenManyDecodesRunAtOnceAsync()
    {
        // The decoded pixels live in a pooled array. Returning it to the pool while the decoded stream still
        // reads it lets another decode rent and overwrite it: concurrent decodes then corrupt each other.
        var jpeg = JpegBytes();
        var expected = await DecodeAsync(jpeg);
        Assert.Equal(256 * 256 * 3, expected.Length);

        for (int round = 0; round < 10; round++)
        {
            var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => DecodeAsync(jpeg))));
            foreach (var result in results) Assert.Equal(expected, result);
        }
    }
}
