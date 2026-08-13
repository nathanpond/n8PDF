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

    /// <summary>
    /// A TIFF may be written in rectangles rather than in rows. Every tile is the full size the
    /// tags declare however little of the picture it covers, so the ones at the right and the foot
    /// carry padding that has to be left behind rather than read as pixels.
    /// </summary>
    [Fact]
    public void A_tiled_tiff_is_put_back_together_from_its_tiles()
    {
        // Forty by thirty-six in tiles of sixteen is nine tiles, and neither edge divides evenly.
        const int width = 40;
        const int height = 36;

        var pixels = ImageWriter.Pixels(width, height,
            (x, y) => ((byte)(x * 6), (byte)(y * 7), (byte)((x ^ y) * 3)));

        var image = ImageReader.Read(ImageWriter.TiledTiff(width, height, pixels, 16, 16));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(0, Difference(pixels, image));
    }

    /// <summary>
    /// A TIFF may also keep its channels apart: three pictures of one sample each rather than one
    /// picture of three, which have to be laid over one another to make pixels again.
    /// </summary>
    [Fact]
    public void A_tiff_that_keeps_its_channels_apart_is_laid_back_together()
    {
        const int width = 11;
        const int height = 9;

        var pixels = ImageWriter.Pixels(width, height,
            (x, y) => ((byte)(x * 20), (byte)(y * 25), (byte)((x + y) * 10)));

        var image = ImageReader.Read(ImageWriter.PlanarTiff(width, height, pixels));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(0, Difference(pixels, image));
    }

    /// <summary>
    /// And both again through another reader, which is what says the files are the format's rather
    /// than this pair of routines' idea of it — a tile size that is not a multiple of sixteen is
    /// a file the format does not allow, and the other reader is what says so.
    /// </summary>
    [Theory]
    [InlineData("tiled")]
    [InlineData("planar")]
    public void A_tiff_of_either_layout_reads_the_same_to_another_reader(string layout)
    {
        const int width = 40;
        const int height = 36;

        var pixels = ImageWriter.Pixels(width, height,
            (x, y) => ((byte)(x * 6), (byte)(y * 7), (byte)((x ^ y) * 3)));

        var tiff = layout == "tiled"
            ? ImageWriter.TiledTiff(width, height, pixels, 16, 16)
            : ImageWriter.PlanarTiff(width, height, pixels);

        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var source = Path.Combine(directory, $"{layout}.tiff");
        File.WriteAllBytes(source, tiff);

        var converted = Path.Combine(directory, $"{layout}.png");
        File.Delete(converted);

        try
        {
            using var process = Process.Start(new ProcessStartInfo("sips",
                ["-s", "format", "png", source, "--out", converted])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            _output.WriteLine("sips was not found; the file was not read back by anything else.");
            return;
        }

        if (!File.Exists(converted))
        {
            _output.WriteLine($"sips would not read the {layout} TIFF.");
            return;
        }

        var theirs = PngDecoder.Decode(File.ReadAllBytes(converted));

        _output.WriteLine($"{layout}: {tiff.Length:N0} bytes, read back as {theirs.Width}x{theirs.Height}");

        Assert.Equal(width, theirs.Width);
        Assert.Equal(height, theirs.Height);
        Assert.Equal(0, Difference(pixels, theirs));
    }

    /// <summary>
    /// The one place a JPEG is decoded rather than handed on. Everywhere else a PDF is given the
    /// file as it stands, but a picture written as several JPEGs cannot be: they are separate
    /// files, and the only way to make one picture of them is to decode each.
    /// </summary>
    [Fact]
    public void A_tiff_holding_a_jpeg_in_several_strips_joins_them()
    {
        const int width = 32;
        const int height = 24;
        const int rows = 8;

        var pixels = ImageWriter.Pixels(width, height,
            (x, y) => ((byte)(x * 7), (byte)(y * 9), (byte)((x + y) * 3)));

        if (Strips(pixels, width, height, rows) is not { } strips)
        {
            _output.WriteLine("sips did not make the strips; nothing to join.");
            return;
        }

        var image = ImageReader.Read(ImageWriter.StrippedJpegTiff(width, height, rows, strips));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);

        // Decoded rather than handed on, since there is no other way to join them.
        Assert.Equal(ImageEncoding.Raw, image.Encoding);
        Assert.Equal(ImageColorSpace.Rgb, image.ColorSpace);

        // The picture it holds, to within what the encoding itself costs. A strip put in the wrong
        // place would be out by far more than that.
        var worst = 0;
        for (var i = 0; i < pixels.Length; i++) worst = Math.Max(worst, Math.Abs(pixels[i] - image.Data[i]));

        _output.WriteLine($"{strips.Count} strips joined, {worst} from the picture they were made of");

        Assert.True(worst < 30, $"the joined picture is {worst} away from the one that went in");
    }

    /// <summary>The bands of a picture, each made into a JPEG of its own by another program.</summary>
    private static List<byte[]>? Strips(byte[] pixels, int width, int height, int rows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var strips = new List<byte[]>();

        for (var band = 0; band < height / rows; band++)
        {
            var slice = new byte[width * rows * 3];
            Array.Copy(pixels, band * rows * width * 3, slice, 0, slice.Length);

            var png = Path.Combine(directory, $"band-{band}.png");
            File.WriteAllBytes(png, PngWriter.Write(width, rows, slice, hasAlpha: false));

            var jpeg = Path.Combine(directory, $"band-{band}.jpg");
            File.Delete(jpeg);

            try
            {
                using var process = Process.Start(new ProcessStartInfo("sips",
                    ["-s", "format", "jpeg", png, "--out", jpeg])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                process?.WaitForExit(30_000);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
            {
                return null;
            }

            if (!File.Exists(jpeg)) return null;

            strips.Add(File.ReadAllBytes(jpeg));
        }

        return strips;
    }

    /// <summary>
    /// The decoder behind that, on its own and against the one macOS uses. Two decoders never
    /// agree to the sample — they round the same sums differently — but they agree to a few levels
    /// of 255, and anything read wrongly is out by far more.
    /// </summary>
    [Theory]
    [InlineData(40, 24)]
    [InlineData(17, 9)]
    [InlineData(8, 8)]
    public void A_jpeg_decodes_to_what_another_decoder_makes_of_it(int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var pixels = ImageWriter.Pixels(width, height,
            (x, y) => ((byte)(x * 6), (byte)(y * 9), (byte)((x + y) * 4)));

        var png = Path.Combine(directory, "decode-source.png");
        File.WriteAllBytes(png, PngWriter.Write(width, height, pixels, hasAlpha: false));

        var jpeg = Path.Combine(directory, "decode-source.jpg");
        var back = Path.Combine(directory, "decode-back.png");
        File.Delete(jpeg);
        File.Delete(back);

        try
        {
            using (var toJpeg = Process.Start(new ProcessStartInfo("sips",
                       ["-s", "format", "jpeg", png, "--out", jpeg])
                   { RedirectStandardOutput = true, RedirectStandardError = true }))
            {
                toJpeg?.WaitForExit(30_000);
            }

            if (!File.Exists(jpeg))
            {
                _output.WriteLine("sips did not make a JPEG; nothing to decode.");
                return;
            }

            using var toPng = Process.Start(new ProcessStartInfo("sips",
                ["-s", "format", "png", jpeg, "--out", back])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            toPng?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            _output.WriteLine("sips was not found; the JPEG was not decoded by anything else.");
            return;
        }

        if (!File.Exists(back))
        {
            _output.WriteLine("sips did not decode its own JPEG.");
            return;
        }

        var theirs = PngDecoder.Decode(File.ReadAllBytes(back));
        var ours = JpegDecoder.Decode(File.ReadAllBytes(jpeg));

        Assert.Equal(theirs.Width, ours.Width);
        Assert.Equal(theirs.Height, ours.Height);

        var worst = 0;
        var total = 0.0;

        for (var i = 0; i < Math.Min(ours.Data.Length, theirs.Data.Length); i++)
        {
            var difference = Math.Abs(ours.Data[i] - theirs.Data[i]);

            worst = Math.Max(worst, difference);
            total += difference;
        }

        _output.WriteLine(
            $"{width}x{height}: worst {worst}, mean {total / Math.Max(1, ours.Data.Length):0.##}");

        Assert.True(worst <= 12, $"the two decoders differ by {worst}");
    }

    /// <summary>
    /// A progressive JPEG is written as several passes over the whole picture rather than block by
    /// block: the coarsest waves of all of it first, then the rest, and a number's high bits may
    /// arrive in one pass and its low bits in another. So the numbers are gathered and turned into
    /// pixels only at the end.
    /// </summary>
    /// <remarks>
    /// These are read against real ones rather than any this could write. A progressive file is
    /// where a JPEG is most easily read nearly right — a pass misread leaves a picture that is
    /// still a picture, only softer or blockier than it should be — so what it is worth testing
    /// against is files written by encoders that had no idea this existed, decoded by a reader
    /// that shares nothing with it. macOS ships several; where it does not, this reports and skips.
    /// </remarks>
    [Fact]
    public void A_progressive_jpeg_is_read_as_another_decoder_reads_it()
    {
        var found = ProgressiveJpegs().Take(3).ToList();

        if (found.Count == 0)
        {
            _output.WriteLine("No progressive JPEG was found on this machine to read.");
            return;
        }

        foreach (var path in found)
        {
            var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
            Directory.CreateDirectory(directory);

            var back = Path.Combine(directory, "progressive.png");
            File.Delete(back);

            try
            {
                using var process = Process.Start(new ProcessStartInfo("sips",
                    ["-s", "format", "png", path, "--out", back])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                process?.WaitForExit(60_000);
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
            {
                _output.WriteLine("sips was not found; the progressive JPEGs were not read back.");
                return;
            }

            if (!File.Exists(back)) continue;

            var theirs = PngDecoder.Decode(File.ReadAllBytes(back));
            var ours = JpegDecoder.Decode(File.ReadAllBytes(path));

            Assert.Equal(theirs.Width, ours.Width);
            Assert.Equal(theirs.Height, ours.Height);

            var channels = Math.Min(ours.ComponentCount, theirs.ComponentCount);
            var worst = 0;
            var total = 0.0;

            for (var i = 0; i < ours.Width * ours.Height; i++)
            for (var c = 0; c < channels; c++)
            {
                var difference = Math.Abs(
                    ours.Data[i * ours.ComponentCount + c] - theirs.Data[i * theirs.ComponentCount + c]);

                worst = Math.Max(worst, difference);
                total += difference;
            }

            var mean = total / (ours.Width * ours.Height * channels);

            _output.WriteLine(
                $"{Path.GetFileName(path)} {ours.Width}x{ours.Height}: worst {worst}, mean {mean:0.###}");

            // A pass misread shows as a picture that is softer or blockier, which is worth far
            // more than the rounding two decoders differ by.
            Assert.True(worst <= 12, $"{Path.GetFileName(path)} differs by {worst}");
            Assert.True(mean < 1, $"{Path.GetFileName(path)} differs by {mean:0.###} on average");
        }
    }

    /// <summary>The progressive JPEGs this machine happens to have, if it has any.</summary>
    private static IEnumerable<string> ProgressiveJpegs()
    {
        string[] roots =
        [
            "/System/Applications", "/System/Library/CoreServices", "/System/Library/Desktop Pictures"
        ];

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            IEnumerable<string> files;

            try
            {
                files = Directory.EnumerateFiles(root, "*.jpg", SearchOption.AllDirectories);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in files)
            {
                if (IsProgressive(path)) yield return path;
            }
        }
    }

    /// <summary>Whether a JPEG says it is written in passes rather than block by block.</summary>
    private static bool IsProgressive(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);

            var data = new byte[(int)Math.Min(200_000, stream.Length)];
            stream.ReadExactly(data);

            if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8) return false;

            var at = 2;

            while (at + 3 < data.Length)
            {
                if (data[at] != 0xff)
                {
                    at++;
                    continue;
                }

                var marker = data[at + 1];

                if (marker is 0xc0 or 0xc1 or 0xda) return false;
                if (marker == 0xc2) return true;

                if (marker is 0xd8 or 0x01 || marker is >= 0xd0 and <= 0xd7)
                {
                    at += 2;
                    continue;
                }

                at += 2 + ((data[at + 2] << 8) | data[at + 3]);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>The kinds of JPEG this still does not decode are reported rather than half-read.</summary>
    [Fact]
    public void A_jpeg_this_cannot_decode_is_reported()
    {
        // A header that says the picture is coded arithmetically, which almost nothing writes.
        byte[] arithmetic =
        [
            0xff, 0xd8,
            0xff, 0xc9, 0x00, 0x11, 0x08, 0x00, 0x10, 0x00, 0x10, 0x03,
            0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
            0xff, 0xd9
        ];

        Assert.Throws<ImageFormatException>(() => JpegDecoder.Decode(arithmetic));
    }

    // ----- sixteen bits a sample -----

    /// <summary>
    /// A picture written with sixteen bits a sample keeps them. Reducing one to eight would throw
    /// away exactly what it was written that way to keep — and a PDF carries either, so there is
    /// nothing to be gained by it.
    /// </summary>
    [Fact]
    public void A_picture_of_sixteen_bits_a_sample_keeps_them()
    {
        const int width = 4;
        const int height = 3;

        // Values whose lower half differs while their upper half does not: reduced to eight bits
        // these would all be the same colour.
        var samples = new ushort[width * height * 3];

        for (var i = 0; i < samples.Length; i++) samples[i] = (ushort)(0x1200 + i * 7);

        var image = ImageReader.Read(PngWriter.WriteDeep(width, height, samples, hasAlpha: false));

        Assert.Equal(16, image.BitsPerComponent);
        Assert.Equal(samples.Length * 2, image.Data.Length);

        for (var i = 0; i < samples.Length; i++)
        {
            // A PDF and a PNG both write the bigger half of a sample first.
            var value = (image.Data[i * 2] << 8) | image.Data[i * 2 + 1];

            Assert.Equal(samples[i], value);
        }
    }

    /// <summary>The transparency of such a picture is kept at the same precision.</summary>
    [Fact]
    public void The_mask_of_a_deep_picture_is_deep_too()
    {
        const int size = 4;

        var samples = new ushort[size * size * 4];

        for (var i = 0; i < size * size; i++)
        {
            samples[i * 4] = 0x8000;
            samples[i * 4 + 1] = 0x4000;
            samples[i * 4 + 2] = 0x2000;
            samples[i * 4 + 3] = (ushort)(i * 0x0101);
        }

        var image = ImageReader.Read(PngWriter.WriteDeep(size, size, samples, hasAlpha: true));

        Assert.True(image.HasAlpha);
        Assert.Equal(size * size * 2, image.Alpha!.Length);

        for (var i = 0; i < size * size; i++)
        {
            var value = (image.Alpha[i * 2] << 8) | image.Alpha[i * 2 + 1];

            Assert.Equal(i * 0x0101, value);
        }
    }

    /// <summary>
    /// And it reaches the PDF at that precision, drawn by a reader that shares nothing with this
    /// one: a picture written at sixteen bits and described as eight comes out as noise, so what
    /// says the two agree is the page being the colour it should be.
    /// </summary>
    [Fact]
    public void A_deep_picture_reaches_the_page_as_the_colour_it_is()
    {
        const int size = 8;

        var samples = new ushort[size * size * 3];

        for (var i = 0; i < size * size; i++)
        {
            samples[i * 3] = 0x2000;
            samples[i * 3 + 1] = 0x9000;
            samples[i * 3 + 2] = 0x4000;
        }

        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.WriteDeep(size, size, samples, hasAlpha: false));
        builder.AddImageParagraph(id, 72, 72);

        var pdf = n8PDF.Converter.Convert(builder.Build(),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        Assert.Contains("/BitsPerComponent 16", System.Text.Encoding.Latin1.GetString(pdf));

        if (PdfRasterizer.Render(pdf, scale: 2) is not { } rendered)
        {
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // The picture sits at the top left of the text area, and is one colour throughout.
        var colour = rendered.At(72 + 36, 72 + 36, 2);

        _output.WriteLine($"the page is {colour} where the picture is");

        Assert.InRange(colour.R, 0x20 - 8, 0x20 + 8);
        Assert.InRange(colour.G, 0x90 - 8, 0x90 + 8);
        Assert.InRange(colour.B, 0x40 - 8, 0x40 + 8);
    }

    /// <summary>
    /// A TIFF of sixteen bits a sample keeps them too, and reads them the way round its own file
    /// is written — which is the half of this that could go wrong without showing: a picture read
    /// from the wrong end still looks like a picture, only the wrong one.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tiff_of_sixteen_bits_a_sample_keeps_them(bool little)
    {
        const int width = 4;
        const int height = 3;

        var samples = new ushort[width * height * 3];
        for (var i = 0; i < samples.Length; i++) samples[i] = (ushort)(0x1200 + i * 7);

        var image = ImageReader.Read(ImageWriter.DeepTiff(width, height, samples, little));

        Assert.Equal(16, image.BitsPerComponent);
        Assert.Equal(samples.Length * 2, image.Data.Length);

        for (var i = 0; i < samples.Length; i++)
        {
            // Whatever way round the file was written, a PDF wants the bigger half first.
            var value = (image.Data[i * 2] << 8) | image.Data[i * 2 + 1];

            Assert.Equal(samples[i], value);
        }
    }

    /// <summary>
    /// And the same file read by another program, which is what says a sample is being read from
    /// the end it was written at: reading one backwards gives a picture whose colours are wrong
    /// rather than a file that will not open.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_deep_tiff_is_the_same_picture_to_another_reader(bool little)
    {
        const int width = 8;
        const int height = 6;

        // Colours whose halves differ, so that reading them backwards is a different picture.
        var samples = new ushort[width * height * 3];

        for (var i = 0; i < width * height; i++)
        {
            samples[i * 3] = 0x2010;
            samples[i * 3 + 1] = 0x90f0;
            samples[i * 3 + 2] = 0x4080;
        }

        var tiff = ImageWriter.DeepTiff(width, height, samples, little);

        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var source = Path.Combine(directory, $"deep-{little}.tiff");
        File.WriteAllBytes(source, tiff);

        var converted = Path.Combine(directory, $"deep-{little}.png");
        File.Delete(converted);

        try
        {
            using var process = Process.Start(new ProcessStartInfo("sips",
                ["-s", "format", "png", source, "--out", converted])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            _output.WriteLine("sips was not found; the file was not read back by anything else.");
            return;
        }

        if (!File.Exists(converted))
        {
            _output.WriteLine("sips would not read the deep TIFF.");
            return;
        }

        var theirs = PngDecoder.Decode(File.ReadAllBytes(converted));
        var ours = ImageReader.Read(tiff);

        _output.WriteLine(
            $"{(little ? "little" : "big")} end first: they read {theirs.BitsPerComponent} bits a sample, " +
            $"we read {ours.BitsPerComponent}");

        // Whichever precision the other reader kept, the top half of each sample must agree.
        var size = theirs.BitsPerComponent / 8;

        for (var i = 0; i < width * height * 3; i++)
        {
            Assert.Equal(ours.Data[i * 2], theirs.Data[i * size]);
        }
    }

    // ----- a JPEG inside a TIFF -----

    /// <summary>A JPEG of the sample picture, made by a program that can make one.</summary>
    private static byte[]? Jpeg(int width, int height)
    {
        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var png = Path.Combine(directory, "jpeg-source.png");
        File.WriteAllBytes(png, PngWriter.Write(width, height,
            ImageWriter.Pixels(width, height, (x, y) => ((byte)(x * 6), (byte)(y * 7), (byte)128)),
            hasAlpha: false));

        var jpeg = Path.Combine(directory, "jpeg-source.jpg");
        File.Delete(jpeg);

        try
        {
            using var process = Process.Start(new ProcessStartInfo("sips",
                ["-s", "format", "jpeg", png, "--out", jpeg])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }

        return File.Exists(jpeg) ? File.ReadAllBytes(jpeg) : null;
    }

    /// <summary>
    /// A TIFF may hold a JPEG rather than pixels, and a PDF carries a JPEG as the file it already
    /// is — so it is put back together rather than decoded, and comes out as the JPEG it was.
    /// </summary>
    /// <remarks>
    /// The newer way keeps the tables every scan shares in a tag of their own so a picture in many
    /// strips need not repeat them, which makes the file the tables without their end followed by
    /// the scan without its beginning. A file that keeps them together is the same picture written
    /// the simpler way, and both have to come back whole.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_tiff_holding_a_jpeg_gives_back_the_jpeg(bool separateTables)
    {
        const int width = 32;
        const int height = 24;

        if (Jpeg(width, height) is not { } jpeg)
        {
            _output.WriteLine("sips did not make a JPEG; nothing to put inside a TIFF.");
            return;
        }

        var image = ImageReader.Read(ImageWriter.JpegTiff(width, height, jpeg, separateTables));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);

        // Handed on as the JPEG it is, not unpacked into samples: a PDF carries one as it stands.
        Assert.Equal(ImageEncoding.Jpeg, image.Encoding);
        Assert.Equal(ImageColorSpace.Rgb, image.ColorSpace);

        _output.WriteLine(
            $"{(separateTables ? "tables apart" : "tables together")}: {image.Data.Length:N0} bytes of JPEG");
    }

    /// <summary>
    /// And what comes out is a JPEG another program will read: a file put back together wrongly
    /// parses as far as its header and then falls apart, which reading its size would not show.
    /// </summary>
    [Fact]
    public void The_jpeg_a_tiff_gives_back_is_one_another_program_reads()
    {
        const int width = 32;
        const int height = 24;

        if (Jpeg(width, height) is not { } jpeg)
        {
            _output.WriteLine("sips did not make a JPEG; nothing to put inside a TIFF.");
            return;
        }

        var image = ImageReader.Read(ImageWriter.JpegTiff(width, height, jpeg));

        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        var ours = Path.Combine(directory, "rebuilt.jpg");
        File.WriteAllBytes(ours, image.Data);

        var converted = Path.Combine(directory, "rebuilt.png");
        File.Delete(converted);

        try
        {
            using var process = Process.Start(new ProcessStartInfo("sips",
                ["-s", "format", "png", ours, "--out", converted])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            _output.WriteLine("sips was not found; the rebuilt JPEG was not read back.");
            return;
        }

        Assert.True(File.Exists(converted), "the JPEG put back together was not one sips would read");

        var theirs = PngDecoder.Decode(File.ReadAllBytes(converted));
        var original = PngDecoder.Decode(File.ReadAllBytes(Path.Combine(directory, "jpeg-source.png")));

        Assert.Equal(width, theirs.Width);
        Assert.Equal(height, theirs.Height);

        // JPEG is lossy, so this is the picture it was rather than the picture exactly.
        var worst = 0;
        for (var i = 0; i < Math.Min(theirs.Data.Length, original.Data.Length); i++)
            worst = Math.Max(worst, Math.Abs(theirs.Data[i] - original.Data[i]));

        _output.WriteLine($"the rebuilt JPEG is the picture to within {worst}");

        Assert.True(worst < 40, $"the rebuilt JPEG is {worst} away from the picture it should hold");
    }

    /// <summary>
    /// The older way of holding a JPEG keeps the whole file in a tag of its own. It is deprecated,
    /// and it is the one thing here no other program would make for this to check against: sips
    /// will not read a file written that way at all, so what is asserted is only that the file
    /// this reads gives its JPEG back.
    /// </summary>
    [Fact]
    public void The_older_way_of_holding_a_jpeg_gives_it_back_too()
    {
        const int width = 32;
        const int height = 24;

        if (Jpeg(width, height) is not { } jpeg)
        {
            _output.WriteLine("sips did not make a JPEG; nothing to put inside a TIFF.");
            return;
        }

        var image = ImageReader.Read(ImageWriter.OldJpegTiff(width, height, jpeg));

        Assert.Equal(ImageEncoding.Jpeg, image.Encoding);
        Assert.Equal(jpeg.Length, image.Data.Length);
    }

    // ----- the fax encodings -----

    /// <summary>
    /// A page of black on white with runs of several lengths, and a block that makes the lines
    /// differ from one another — which is what a line written against the one above it needs.
    /// </summary>
    private static byte[] Bilevel(int width, int height) =>
        [.. Enumerable.Range(0, width * height).Select(i =>
        {
            var (x, y) = (i % width, i / width);

            return (byte)((x / 3 + y) % 2 == 0 || (x > 20 && x < 30 && y > 2 && y < 8) ? 1 : 0);
        })];

    private static void AssertReadsAsBilevel(byte[] tiff, byte[] pixels, int width, int height)
    {
        var image = ImageReader.Read(tiff);

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.Equal(ImageColorSpace.Gray, image.ColorSpace);

        for (var i = 0; i < width * height; i++)
        {
            var black = image.Data[i] < 128;

            Assert.True(black == (pixels[i] != 0),
                $"pixel {i % width},{i / width} came out {(black ? "black" : "white")}");
        }
    }

    /// <summary>
    /// The three fax encodings: a line written on its own, lines written either way with a bit
    /// each saying which, and lines written against one another throughout.
    /// </summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void A_fax_is_read_from_the_runs_it_was_written_as(int compression, bool byteAligned)
    {
        const int width = 40;
        const int height = 12;

        var pixels = Bilevel(width, height);

        AssertReadsAsBilevel(
            CcittWriter.Tiff(pixels, width, height, compression, byteAligned), pixels, width, height);
    }

    /// <summary>
    /// The plainest group 4 there is, where every line spells out its runs rather than saying how
    /// far they have moved. It is legal and it is what a reader has to get right first, but it is
    /// not what a fax looks like — which is why the test above matters more than this one.
    /// </summary>
    [Fact]
    public void A_group_four_page_written_the_long_way_reads_the_same()
    {
        const int width = 40;
        const int height = 12;

        var pixels = Bilevel(width, height);

        AssertReadsAsBilevel(
            CcittWriter.Tiff(pixels, width, height, 4, plain: true), pixels, width, height);
    }

    /// <summary>
    /// A run longer than sixty-three is written as two codes — one for the multiple of sixty-four
    /// below it and one for the remainder — and a run longer than 1728 needs one of the codes both
    /// colours share.
    /// </summary>
    [Fact]
    public void The_long_runs_are_read_as_the_pairs_of_codes_they_are_written_as()
    {
        const int width = 2000;
        const int height = 3;

        // A white run of 1900 and a black one of 100, which reaches past every table there is.
        var pixels = new byte[width * height];

        for (var y = 0; y < height; y++)
        for (var x = 1900; x < width; x++)
            pixels[y * width + x] = 1;

        AssertReadsAsBilevel(CcittWriter.Tiff(pixels, width, height, 2), pixels, width, height);
        AssertReadsAsBilevel(CcittWriter.Tiff(pixels, width, height, 4), pixels, width, height);
    }

    /// <summary>
    /// The fax encodings say nothing about colour: a set bit is black in all of them, whatever a
    /// photometric tag written beside them claims.
    /// </summary>
    [Fact]
    public void A_fax_is_black_where_its_bits_are_set()
    {
        var pixels = new byte[8 * 2];
        for (var i = 0; i < 8; i++) pixels[i] = 1;

        var image = ImageReader.Read(CcittWriter.Tiff(pixels, 8, 2, 4));

        // The first row was written black and the second white.
        Assert.True(image.Data[0] < 128);
        Assert.True(image.Data[8] > 128);
    }

    /// <summary>
    /// And the same files read by another program, which is what says the code tables are the
    /// standard's rather than merely this library's own. The tables are shared with the writer on
    /// purpose: a file written with them is handed to something else to read, and if a code in
    /// them were wrong that program would not be able to.
    /// </summary>
    [Theory]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public void A_fax_this_wrote_is_read_the_same_by_another_program(int compression, bool byteAligned)
    {
        const int width = 40;
        const int height = 12;

        var pixels = Bilevel(width, height);
        var tiff = CcittWriter.Tiff(pixels, width, height, compression, byteAligned);

        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-image-tests");
        Directory.CreateDirectory(directory);

        var source = Path.Combine(directory, $"fax-{compression}.tiff");
        File.WriteAllBytes(source, tiff);

        var converted = Path.Combine(directory, $"fax-{compression}.png");
        File.Delete(converted);

        try
        {
            using var process = Process.Start(new ProcessStartInfo("sips",
                ["-s", "format", "png", source, "--out", converted])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            process?.WaitForExit(30_000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            _output.WriteLine("sips was not found; the fax was not read back by anything else.");
            return;
        }

        if (!File.Exists(converted))
        {
            _output.WriteLine($"sips would not read the group {compression} fax.");
            return;
        }

        var theirs = PngDecoder.Decode(File.ReadAllBytes(converted));

        _output.WriteLine($"group {compression}: {tiff.Length:N0} bytes, read back as " +
                          $"{theirs.Width}x{theirs.Height} {theirs.ColorSpace}");

        Assert.Equal(width, theirs.Width);
        Assert.Equal(height, theirs.Height);

        for (var i = 0; i < width * height; i++)
        {
            var black = theirs.Data[i * theirs.ComponentCount] < 128;

            Assert.True(black == (pixels[i] != 0),
                $"pixel {i % width},{i / width} came out {(black ? "black" : "white")} in the other reader");
        }
    }

    /// <summary>
    /// Grey written in fewer than eight bits a pixel, which is what another program writes a fax
    /// back out as — and what this could not read until it was asked to.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void A_png_of_few_bits_a_pixel_is_read_as_the_shades_it_names(int bits)
    {
        var top = (1 << bits) - 1;
        var shades = new byte[8 * 4];

        for (var i = 0; i < shades.Length; i++) shades[i] = (byte)(i % (top + 1) * 255 / top);

        var image = ImageReader.Read(PngWriter.WriteGrey(8, 4, shades, bits));

        Assert.Equal(ImageColorSpace.Gray, image.ColorSpace);
        Assert.Equal(shades.Length, image.Data.Length);
        Assert.Equal(shades, image.Data);
    }

    // ----- interlaced PNG -----

    /// <summary>
    /// An interlaced PNG is the same picture written seven times over, each pass a coarser or
    /// finer sieve of it, so the one thing that must be true is that it reads as the picture the
    /// plain one holds — at every size, including the ones where whole passes catch nothing.
    /// </summary>
    [Theory]
    [InlineData(9, 7)]
    [InlineData(1, 1)]
    [InlineData(3, 2)]
    [InlineData(16, 16)]
    [InlineData(5, 9)]
    public void An_interlaced_png_reads_as_the_picture_the_plain_one_holds(int width, int height)
    {
        var pixels = ImageWriter.Pixels(width, height,
            (x, y) => ((byte)(x * 17 + y), (byte)(y * 23), (byte)((x ^ y) * 8)));

        var plain = ImageReader.Read(PngWriter.Write(width, height, pixels, hasAlpha: false));
        var interlaced = ImageReader.Read(PngWriter.WriteInterlaced(width, height, pixels, hasAlpha: false));

        Assert.Equal(plain.Width, interlaced.Width);
        Assert.Equal(plain.Height, interlaced.Height);
        Assert.Equal(0, Difference(plain.Data, interlaced));
    }

    /// <summary>Transparency comes through the passes with everything else.</summary>
    [Fact]
    public void An_interlaced_png_keeps_its_transparency()
    {
        const int size = 8;

        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var at = (y * size + x) * 4;

            pixels[at] = (byte)(x * 30);
            pixels[at + 1] = (byte)(y * 30);
            pixels[at + 2] = 90;
            pixels[at + 3] = (byte)(x < size / 2 ? 0 : 255);
        }

        var image = ImageReader.Read(PngWriter.WriteInterlaced(size, size, pixels, hasAlpha: true));

        Assert.True(image.HasAlpha);

        for (var y = 0; y < size; y++)
        {
            Assert.Equal(0, image.Alpha![y * size]);
            Assert.Equal(255, image.Alpha[y * size + size - 1]);
        }
    }

    /// <summary>
    /// A pass of an interlaced PNG of four bits a pixel does not end on a byte, so its pixels have
    /// to be put back a few bits at a time rather than copied.
    /// </summary>
    [Fact]
    public void An_interlaced_png_of_four_bits_a_pixel_is_put_back_a_few_bits_at_a_time()
    {
        const int width = 11;
        const int height = 6;

        var palette = new byte[]
        {
            200, 30, 40,
            30, 160, 70,
            40, 70, 210,
            240, 230, 10
        };

        var indexes = new byte[width * height];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            indexes[y * width + x] = (byte)((x + y) % 4);

        var image = ImageReader.Read(PngWriter.WriteInterlacedPalette(width, height, indexes, palette));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var entry = indexes[y * width + x] * 3;
            var at = (y * width + x) * 3;

            Assert.Equal(palette[entry], image.Data[at]);
            Assert.Equal(palette[entry + 1], image.Data[at + 1]);
            Assert.Equal(palette[entry + 2], image.Data[at + 2]);
        }
    }

    /// <summary>
    /// And a second opinion on the file itself: another reader is given the interlaced PNG and
    /// asked what it holds, so that what is tested is the format rather than this pair of
    /// routines agreeing with each other.
    /// </summary>
    [Fact]
    public void Another_reader_makes_the_same_picture_of_an_interlaced_png()
    {
        var pixels = Sample();
        var interlaced = PngWriter.WriteInterlaced(Width, Height, pixels, hasAlpha: false);

        if (Convert("bmp", interlaced) is not { } path)
        {
            _output.WriteLine("sips did not read the interlaced PNG; nothing to compare.");
            return;
        }

        var theirs = ImageReader.Read(File.ReadAllBytes(path));

        _output.WriteLine($"interlaced: {interlaced.Length:N0} bytes, read back as {theirs.Width}x{theirs.Height}");

        Assert.Equal(Width, theirs.Width);
        Assert.Equal(Height, theirs.Height);
        Assert.Equal(0, Difference(pixels, theirs));
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
