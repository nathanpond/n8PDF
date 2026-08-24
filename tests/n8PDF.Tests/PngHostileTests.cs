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
}
