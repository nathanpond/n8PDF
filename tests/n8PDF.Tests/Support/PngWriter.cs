using System.IO.Compression;
using System.Text;

namespace n8PDF.Tests.Support;

/// <summary>
/// Writes small PNG images for tests.
/// </summary>
/// <remarks>
/// Generated rather than committed as binary assets so the pixels are visible in the test that
/// uses them: an assertion about what came out the other end can be read against what went in.
/// This is a writer, and the library only ever reads, so the two share no code and a
/// misunderstanding in one cannot hide the same misunderstanding in the other.
/// </remarks>
public static class PngWriter
{
    /// <summary>Writes an 8-bit RGB or RGBA image. Pixels are row-major, top row first.</summary>
    public static byte[] Write(int width, int height, byte[] pixels, bool hasAlpha)
    {
        var channels = hasAlpha ? 4 : 3;
        if (pixels.Length != width * height * channels)
            throw new ArgumentException("Pixel buffer does not match the declared size.", nameof(pixels));

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        WriteInt32(header, 0, width);
        WriteInt32(header, 4, height);
        header[8] = 8;                          // bit depth
        header[9] = (byte)(hasAlpha ? 6 : 2);   // colour type: truecolour, with alpha or without
        WriteChunk(output, "IHDR", header);

        // Every row is prefixed with its filter type; 0 means the bytes are stored as they are.
        var raw = new byte[height * (1 + width * channels)];
        for (var y = 0; y < height; y++)
        {
            var target = y * (1 + width * channels);
            raw[target] = 0;
            Array.Copy(pixels, y * width * channels, raw, target + 1, width * channels);
        }

        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw, 0, raw.Length);

        WriteChunk(output, "IDAT", deflated.ToArray());
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    /// <summary>A solid rectangle, the simplest thing whose colour can be checked after decoding.</summary>
    public static byte[] Solid(int width, int height, byte red, byte green, byte blue)
    {
        var pixels = new byte[width * height * 3];
        for (var i = 0; i < width * height; i++)
        {
            pixels[i * 3] = red;
            pixels[i * 3 + 1] = green;
            pixels[i * 3 + 2] = blue;
        }

        return Write(width, height, pixels, hasAlpha: false);
    }

    /// <summary>Two diagonal halves, so that orientation is visible in the output.</summary>
    public static byte[] Diagonal(int size)
    {
        var pixels = new byte[size * size * 3];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 3;
                var upper = x >= y;

                pixels[i] = upper ? (byte)220 : (byte)30;
                pixels[i + 1] = upper ? (byte)60 : (byte)90;
                pixels[i + 2] = upper ? (byte)60 : (byte)200;
            }
        }

        return Write(size, size, pixels, hasAlpha: false);
    }

    /// <summary>A square whose left half is transparent, for exercising the soft mask.</summary>
    public static byte[] HalfTransparent(int size)
    {
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                pixels[i] = 20;
                pixels[i + 1] = 160;
                pixels[i + 2] = 80;
                pixels[i + 3] = x < size / 2 ? (byte)0 : (byte)255;
            }
        }

        return Write(size, size, pixels, hasAlpha: true);
    }

    private static void WriteChunk(Stream output, string type, byte[] body)
    {
        var length = new byte[4];
        WriteInt32(length, 0, body.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(body);

        // The CRC covers the type and the body, but not the length.
        var crc = Crc32(typeBytes, body);
        var crcBytes = new byte[4];
        WriteInt32(crcBytes, 0, unchecked((int)crc));
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        var crc = 0xffffffffu;

        foreach (var b in first) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        foreach (var b in second) crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);

        return crc ^ 0xffffffffu;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
