using System.IO.Compression;
using n8PDF.Images;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// PngDecoder against hostile input (#2): a decompression bomb — a tiny declared image whose
/// IDAT inflates to gigabytes — is refused against the size its dimensions allow, not held in
/// memory. Tested at the decoder so the fix is proven where the <see cref="ImageReader"/> net
/// (#48) cannot swallow it.
/// </summary>
public class PngHostileTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static void Be32(List<byte> to, int v)
    {
        to.Add((byte)(v >> 24)); to.Add((byte)(v >> 16)); to.Add((byte)(v >> 8)); to.Add((byte)v);
    }

    private static void Chunk(List<byte> to, string type, byte[] body)
    {
        Be32(to, body.Length);
        to.AddRange(System.Text.Encoding.ASCII.GetBytes(type));
        to.AddRange(body);
        Be32(to, 0); // CRC — the decoder does not check it
    }

    private static byte[] ZlibOfZeros(int count)
    {
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(new byte[count], 0, count);
        return raw.ToArray();
    }

    /// <summary>A zlib stream whose header asks for a preset dictionary it cannot be given.</summary>
    private static byte[] PresetDictionaryZlib()
    {
        // CMF 0x78 is deflate over a 32K window; FLG 0x3f sets FDICT (bit 5) and carries an
        // FCHECK that makes the pair a multiple of 31, so the header passes its own check and
        // zlib gets as far as asking. What follows stands in for the DICTID and the data.
        return [0x78, 0x3f, 0x00, 0x00, 0x00, 0x01, 0x63, 0x00, 0x00, 0x00, 0x00];
    }

    [Fact]
    public void A_decompression_bomb_is_refused_against_the_declared_size()
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        // IHDR: 4x4, 8-bit, colour type 6 (RGBA), no interlace.
        var ihdr = new List<byte>();
        Be32(ihdr, 4); Be32(ihdr, 4);
        ihdr.Add(8); ihdr.Add(6); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);
        Chunk(png, "IHDR", ihdr.ToArray());

        // IDAT that decompresses to 16 MB — far past what 4x4 pixels allow.
        Chunk(png, "IDAT", ZlibOfZeros(16 * 1024 * 1024));
        Chunk(png, "IEND", []);

        _output.WriteLine($"bomb is {png.Count} bytes, declares 4x4");

        // At the decoder: a clean ImageFormatException, not an OOM and not a giant buffer.
        var ex = Record.Exception(() => PngDecoder.Decode(png.ToArray()));
        Assert.IsType<ImageFormatException>(ex);

        // And through the public net: the picture is left out, the caller gets null.
        Assert.Null(ImageReader.TryRead(png.ToArray()));
    }

    [Fact]
    public void A_real_small_png_still_decodes()
    {
        // A 2x2 RGBA image, one filter byte per row, actually decodes — the cap does not bite it.
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new List<byte>();
        Be32(ihdr, 2); Be32(ihdr, 2);
        ihdr.Add(8); ihdr.Add(6); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);
        Chunk(png, "IHDR", ihdr.ToArray());

        // Two rows, each: filter byte 0 then 2 pixels * 4 bytes.
        var raw = new byte[2 * (1 + 2 * 4)];
        using var comp = new MemoryStream();
        using (var z = new ZLibStream(comp, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw, 0, raw.Length);
        Chunk(png, "IDAT", comp.ToArray());
        Chunk(png, "IEND", []);

        var image = PngDecoder.Decode(png.ToArray());
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
    }

    [Fact]
    public void A_short_ihdr_fails_cleanly()   // #4
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Chunk(png, "IHDR", new byte[5]);   // fewer than the 13 IHDR bytes
        Assert.IsType<ImageFormatException>(Record.Exception(() => PngDecoder.Decode(png.ToArray())));
    }

    [Fact]
    public void A_palette_at_sixteen_bits_does_not_divide_by_zero()   // #6
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new List<byte>();
        Be32(ihdr, 2); Be32(ihdr, 2);
        ihdr.Add(16); ihdr.Add(3); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);   // 16-bit palette
        Chunk(png, "IHDR", ihdr.ToArray());
        Chunk(png, "PLTE", new byte[3]);
        Chunk(png, "IDAT", ZlibOfZeros(64));
        Chunk(png, "IEND", []);
        Assert.IsType<ImageFormatException>(Record.Exception(() => PngDecoder.Decode(png.ToArray())));
    }

    [Fact]
    public void A_corrupt_idat_fails_cleanly()   // #7
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new List<byte>();
        Be32(ihdr, 2); Be32(ihdr, 2);
        ihdr.Add(8); ihdr.Add(6); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);
        Chunk(png, "IHDR", ihdr.ToArray());
        Chunk(png, "IDAT", [0x78, 0x9c, 0xFF, 0xFF, 0xFF, 0xFF]);   // zlib header then garbage
        Chunk(png, "IEND", []);
        var ex = Record.Exception(() => PngDecoder.Decode(png.ToArray()));
        Assert.IsType<ImageFormatException>(ex);
        Assert.Null(ImageReader.TryRead(png.ToArray()));
    }

    [Fact]
    public void An_idat_asking_for_a_preset_dictionary_fails_cleanly()   // #296
    {
        // FDICT is the one malformation zlib answers with Z_NEED_DICT rather than a data error,
        // and .NET raises ZLibException for it — an IOException, outside the family #7's catch
        // named. Two bytes of a PNG therefore used to take the whole conversion down.
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new List<byte>();
        Be32(ihdr, 2); Be32(ihdr, 2);
        ihdr.Add(8); ihdr.Add(6); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);
        Chunk(png, "IHDR", ihdr.ToArray());
        Chunk(png, "IDAT", PresetDictionaryZlib());
        Chunk(png, "IEND", []);

        var ex = Record.Exception(() => PngDecoder.Decode(png.ToArray()));
        Assert.IsType<ImageFormatException>(ex);
        Assert.Null(ImageReader.TryRead(png.ToArray()));
    }

    [Fact]
    public void The_minimised_fuzz_unit_from_the_image_target_is_read_as_null()   // #296
    {
        // libFuzzer's own reduction of the crash, kept verbatim: the input the scheduled job
        // wrote out. The corpus itself is not in git — it lives in the Actions cache and is
        // rebuilt by `dotnet run -- seed` — so fuzz/Program.cs seeds this same unit, and this
        // holds the assertion where the suite can see it.
        var unit = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAAECAQAAAgAAAAAAAAADElEQVR4P5xjsmAcAAAARAAB//8dCFMA");
        _output.WriteLine($"the minimised unit is {unit.Length} bytes");

        Assert.Null(ImageReader.TryRead(unit));
    }

    [Fact]
    public void A_chunk_length_near_int_max_does_not_pass_the_bounds_check()   // #3
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Be32(png, 0x7FFFFFFF);                          // chunk length near int.MaxValue
        png.AddRange("IDAT"u8.ToArray());
        png.AddRange(new byte[8]);
        Assert.IsType<ImageFormatException>(Record.Exception(() => PngDecoder.Decode(png.ToArray())));
    }
}
