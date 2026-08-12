using System.Diagnostics;
using n8PDF.Images;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests reading the picture formats a document can carry beyond PNG and JPEG: GIF, BMP and TIFF.
/// </summary>
/// <remarks>
/// Two ways round. Most of these build a file whose every pixel is known and read it back, which
/// is the only way to reach the awkward parts of each format — a bitmap written from the foot up,
/// a GIF written in four passes, a TIFF written big end first.
///
/// The other way is a second opinion, and is what says the decoders read real files rather than
/// only the ones this test wrote. macOS ships an image converter of its own, so the same picture
/// is turned into each format by <c>sips</c> and read back: what comes out has to be what the PNG
/// it was made from holds. Where those tools are missing the checks report and skip, the way the
/// font ones do.
/// </remarks>
public class ImageFormatTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const int Width = 9;
    private const int Height = 7;

    private static byte[] Sample() => ImageWriter.Sample(Width, Height);

    /// <summary>How far apart two pictures are, as the worst difference in any one sample.</summary>
    private static int Difference(byte[] expected, ImageData actual)
    {
        Assert.Equal(expected.Length, actual.Data.Length);

        var worst = 0;
        for (var i = 0; i < expected.Length; i++) worst = Math.Max(worst, Math.Abs(expected[i] - actual.Data[i]));

        return worst;
    }

    // ----- BMP -----

    /// <summary>
    /// A bitmap's rows run from the foot of the picture upwards unless its height is written as a
    /// negative number, and both ways round have to come out the same way up.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_bitmap_is_read_whichever_way_up_it_was_written(bool topDown)
    {
        var pixels = Sample();
        var image = ImageReader.Read(ImageWriter.Bmp(Width, Height, pixels, topDown: topDown));

        Assert.Equal(Width, image.Width);
        Assert.Equal(Height, image.Height);
        Assert.Equal(0, Difference(pixels, image));
    }

    /// <summary>
    /// A bitmap of eight bits a pixel or fewer is a palette and a packed index for each pixel,
    /// which is the case a reader has to unpack a byte at a time.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void A_bitmap_of_few_colours_is_read_through_its_palette(int bits)
    {
        // A picture of no more colours than the depth can name.
        var colours = Math.Min(4, 1 << bits);
        var pixels = ImageWriter.Pixels(Width, Height, (x, y) => ((x + y) % colours) switch
        {
            0 => ((byte)200, (byte)30, (byte)40),
            1 => ((byte)30, (byte)160, (byte)70),
            2 => ((byte)40, (byte)70, (byte)210),
            _ => ((byte)240, (byte)230, (byte)10)
        });

        var image = ImageReader.Read(ImageWriter.Bmp(Width, Height, pixels, bits));

        Assert.Equal(0, Difference(pixels, image));
    }

    /// <summary>
    /// Every row of a bitmap is padded out to a multiple of four bytes, which a width of nine
    /// pixels is not — a reader that steps by the row's own width reads the picture on a slant.
    /// </summary>
    [Fact]
    public void The_padding_at_the_end_of_a_row_is_not_read_as_pixels()
    {
        // Nine pixels of three bytes is twenty-seven, padded out to twenty-eight.
        var pixels = ImageWriter.Pixels(9, 3, (x, _) => ((byte)(x * 25), (byte)0, (byte)0));
        var image = ImageReader.Read(ImageWriter.Bmp(9, 3, pixels));

        Assert.Equal(0, Difference(pixels, image));
    }

    // ----- GIF -----

    [Fact]
    public void A_gif_is_read_through_its_colour_table()
    {
        var pixels = Sample();
        var image = ImageReader.Read(ImageWriter.Gif(Width, Height, pixels));

        Assert.Equal(Width, image.Width);
        Assert.Equal(Height, image.Height);
        Assert.Equal(0, Difference(pixels, image));
        Assert.False(image.HasAlpha);
    }

    /// <summary>
    /// An interlaced GIF writes its rows in four passes — every eighth row, then every eighth from
    /// the fifth, then every fourth, then the rest — and they have to go back where they belong.
    /// </summary>
    [Fact]
    public void An_interlaced_gif_is_put_back_in_order()
    {
        // A picture whose rows all differ, so that a row out of place cannot pass.
        var pixels = ImageWriter.Pixels(4, 8, (_, y) => ((byte)(y * 30), (byte)(255 - y * 30), (byte)128));

        var image = ImageReader.Read(ImageWriter.Gif(4, 8, pixels, interlaced: true));

        Assert.Equal(0, Difference(pixels, image));
    }

    /// <summary>
    /// A GIF carries transparency as one colour of its table that is not to be drawn, which
    /// becomes the mask a PDF carries separately.
    /// </summary>
    [Fact]
    public void A_transparent_colour_becomes_a_mask()
    {
        var pixels = ImageWriter.Pixels(4, 4, (x, _) =>
            x == 0 ? ((byte)200, (byte)30, (byte)40) : ((byte)30, (byte)160, (byte)70));

        // The first colour the picture uses is the one that stands at the front of the table.
        var image = ImageReader.Read(ImageWriter.Gif(4, 4, pixels, transparent: 0));

        Assert.True(image.HasAlpha);

        for (var y = 0; y < 4; y++)
        {
            Assert.Equal(0, image.Alpha![y * 4]);
            Assert.Equal(255, image.Alpha[y * 4 + 1]);
        }
    }

    // ----- TIFF -----

    /// <summary>
    /// A TIFF says in its first two bytes which end its numbers are written from, and everything
    /// after that has to be read accordingly — including the numbers written inside the tags.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tiff_is_read_from_whichever_end_it_was_written(bool little)
    {
        var pixels = Sample();
        var image = ImageReader.Read(ImageWriter.Tiff(Width, Height, pixels, little));

        Assert.Equal(Width, image.Width);
        Assert.Equal(Height, image.Height);
        Assert.Equal(0, Difference(pixels, image));
    }

    [Fact]
    public void A_packed_tiff_is_unpacked()
    {
        var pixels = Sample();
        var image = ImageReader.Read(ImageWriter.Tiff(Width, Height, pixels, packBits: true));

        Assert.Equal(0, Difference(pixels, image));
    }

    /// <summary>A TIFF of one channel is grey, and stays one channel through to the PDF.</summary>
    [Fact]
    public void A_grey_tiff_stays_grey()
    {
        var image = ImageReader.Read(ImageWriter.Tiff(Width, Height, Sample(), greyscale: true));

        Assert.Equal(ImageColorSpace.Gray, image.ColorSpace);
        Assert.Equal(Width * Height, image.Data.Length);
    }

    // ----- against another reader's idea of the same picture -----

    private static string? Convert(string format, byte[] png, string? tool = null, string? option = null)
    {
        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var source = Path.Combine(directory, "source.png");
        File.WriteAllBytes(source, png);

        var output = Path.Combine(directory, $"converted{option}.{format}");
        File.Delete(output);

        var arguments = option is null
            ? new[] { "-s", "format", format, source, "--out", output }
            : [option, source, "-out", output];

        try
        {
            using var process = Process.Start(new ProcessStartInfo(tool ?? "sips", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return null;

            process.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }

        return File.Exists(output) ? output : null;
    }

    /// <summary>
    /// The same picture, turned into each format by a converter that shares nothing with this one
    /// and read back. What it holds has to be what the PNG it was made from holds.
    /// </summary>
    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("tiff")]
    public void A_file_another_tool_wrote_reads_as_the_picture_it_was_made_from(string format)
    {
        var png = PngWriter.Write(Width, Height, Sample(), hasAlpha: false);

        if (Convert(format, png) is not { } path)
        {
            _output.WriteLine($"sips did not produce a {format}; nothing to read back.");
            return;
        }

        var expected = PngDecoder.Decode(png);
        var actual = ImageReader.Read(File.ReadAllBytes(path));

        _output.WriteLine(
            $"{format}: {actual.Width}x{actual.Height}, {new FileInfo(path).Length:N0} bytes");

        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);
        Assert.Equal(0, Difference(expected.Data, actual));
    }

    /// <summary>
    /// The same again for the ways a TIFF can be packed, which one tool writes and another asks
    /// for: LZW, PackBits and nothing at all.
    /// </summary>
    [Theory]
    [InlineData("-lzw")]
    [InlineData("-packbits")]
    [InlineData("-none")]
    public void A_tiff_packed_every_way_reads_the_same(string option)
    {
        var png = PngWriter.Write(Width, Height, Sample(), hasAlpha: false);

        if (Convert("tiff", png) is not { } plain)
        {
            _output.WriteLine("sips did not produce a TIFF; nothing to pack.");
            return;
        }

        var directory = Path.GetDirectoryName(plain)!;
        var packed = Path.Combine(directory, $"packed{option}.tiff");
        File.Delete(packed);

        try
        {
            using var process = Process.Start(new ProcessStartInfo("tiffutil",
                [option, plain, "-out", packed])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            _output.WriteLine("tiffutil was not found; the packings were not read back.");
            return;
        }

        if (!File.Exists(packed))
        {
            _output.WriteLine($"tiffutil did not produce a {option} TIFF.");
            return;
        }

        var expected = PngDecoder.Decode(png);
        var actual = ImageReader.Read(File.ReadAllBytes(packed));

        _output.WriteLine($"{option}: {new FileInfo(packed).Length:N0} bytes");

        Assert.Equal(0, Difference(expected.Data, actual));
    }

    // ----- the whole way through -----

    [Fact]
    public void Every_format_reaches_the_pdf()
    {
        var pixels = ImageWriter.Sample(16, 16);

        var builder = new DocxBuilder();
        var gif = builder.AddImagePart(ImageWriter.Gif(16, 16, pixels), "gif");
        var bmp = builder.AddImagePart(ImageWriter.Bmp(16, 16, pixels), "bmp");
        var tiff = builder.AddImagePart(ImageWriter.Tiff(16, 16, pixels), "tiff");

        builder
            .AddImageParagraph(gif, 24, 24)
            .AddImageParagraph(bmp, 24, 24)
            .AddImageParagraph(tiff, 24, 24);

        var pdf = n8PDF.Converter.Convert(builder.Build(),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        using var stream = new MemoryStream(builder.Build());
        var layout = n8PDF.Converter.LayoutDocument(stream,
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        // Three pictures on the page, each the size it was asked for.
        var images = layout.Pages[0].Images;

        Assert.Equal(3, images.Count);
        Assert.All(images, image => Assert.Equal(24, image.Width, 1));

        if (!QpdfTool.IsAvailable) return;

        var result = QpdfTool.CheckBytes(pdf, "image-formats");
        Assert.True(result.IsClean || result.HasWarningsOnly, result.Output);
    }
}
