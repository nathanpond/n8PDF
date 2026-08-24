using n8PDF.Images;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// BmpDecoder against hostile input (#10): a bitmap declaring itself one pixel wide and tens of
/// millions tall no longer asks for one CLR object per row — the scanlines are one flat buffer,
/// bounded by the area the pixel limit already checks.
/// </summary>
public class BmpHostileTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static void Le32(List<byte> to, int v)
    {
        to.Add((byte)v); to.Add((byte)(v >> 8)); to.Add((byte)(v >> 16)); to.Add((byte)(v >> 24));
    }

    private static byte[] Bitmap(int width, int height, int bits)
    {
        var b = new List<byte> { (byte)'B', (byte)'M' };
        Le32(b, 0);            // file size (unchecked)
        Le32(b, 0);            // reserved
        Le32(b, 54);           // pixel offset
        Le32(b, 40);           // BITMAPINFOHEADER size
        Le32(b, width);
        Le32(b, height);
        b.Add(1); b.Add(0);    // planes
        b.Add((byte)bits); b.Add(0);
        Le32(b, 0);            // compression BI_RGB
        Le32(b, 0);            // image size
        Le32(b, 0); Le32(b, 0); // resolution
        Le32(b, 0); Le32(b, 0); // palette counts
        return b.ToArray();
    }

    [Fact]
    public void A_one_pixel_wide_giant_is_refused_by_area_not_allocated_per_row()
    {
        // 1 wide, 60,000,000 tall — past the 50M pixel limit; refused before any row buffer.
        var bmp = Bitmap(1, 60_000_000, 24);
        _output.WriteLine($"{bmp.Length}-byte bitmap declares 1 x 60,000,000");

        Assert.IsType<ImageFormatException>(Record.Exception(() => BmpDecoder.Decode(bmp)));
        Assert.Null(ImageReader.TryRead(bmp));
    }

    [Fact]
    public void A_real_bitmap_still_decodes()
    {
        var pixels = ImageWriter.Sample(8, 8);
        var image = BmpDecoder.Decode(ImageWriter.Bmp(8, 8, pixels));
        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
    }
}
