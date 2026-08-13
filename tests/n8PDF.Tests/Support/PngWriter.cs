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

    /// <summary>
    /// The same, written interlaced: as seven pictures rather than one, each a coarser or finer
    /// sieve of the whole, so that a reader that shows something before it has everything can.
    /// </summary>
    /// <remarks>
    /// Each pass is an image in its own right — its own rows, each with its own filter byte — and
    /// a pass whose sieve catches nothing of a small picture is left out altogether rather than
    /// written empty.
    /// </remarks>
    public static byte[] WriteInterlaced(int width, int height, byte[] pixels, bool hasAlpha)
    {
        var channels = hasAlpha ? 4 : 3;
        if (pixels.Length != width * height * channels)
            throw new ArgumentException("Pixel buffer does not match the declared size.", nameof(pixels));

        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        WriteInt32(header, 0, width);
        WriteInt32(header, 4, height);
        header[8] = 8;
        header[9] = (byte)(hasAlpha ? 6 : 2);
        header[12] = 1;                          // interlaced
        WriteChunk(output, "IHDR", header);

        (int X, int Y, int StepX, int StepY)[] passes =
        [
            (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
            (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)
        ];

        var raw = new List<byte>();

        foreach (var (startX, startY, stepX, stepY) in passes)
        {
            var passWidth = (width - startX + stepX - 1) / stepX;
            var passHeight = (height - startY + stepY - 1) / stepY;

            if (passWidth <= 0 || passHeight <= 0) continue;

            for (var y = 0; y < passHeight; y++)
            {
                raw.Add(0);

                for (var x = 0; x < passWidth; x++)
                {
                    var source = ((startY + y * stepY) * width + startX + x * stepX) * channels;

                    for (var c = 0; c < channels; c++) raw.Add(pixels[source + c]);
                }
            }
        }

        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write([.. raw], 0, raw.Count);

        WriteChunk(output, "IDAT", deflated.ToArray());
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    /// <summary>
    /// An interlaced PNG of four bits a pixel through a palette, which is the case where a pass's
    /// pixels are not whole bytes and have to be put back a few bits at a time.
    /// </summary>
    public static byte[] WriteInterlacedPalette(
        int width, int height, byte[] indexes, byte[] palette)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        WriteInt32(header, 0, width);
        WriteInt32(header, 4, height);
        header[8] = 4;      // four bits a pixel
        header[9] = 3;      // through a palette
        header[12] = 1;     // interlaced
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "PLTE", palette);

        (int X, int Y, int StepX, int StepY)[] passes =
        [
            (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
            (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)
        ];

        var raw = new List<byte>();

        foreach (var (startX, startY, stepX, stepY) in passes)
        {
            var passWidth = (width - startX + stepX - 1) / stepX;
            var passHeight = (height - startY + stepY - 1) / stepY;

            if (passWidth <= 0 || passHeight <= 0) continue;

            var rowBytes = (passWidth * 4 + 7) / 8;

            for (var y = 0; y < passHeight; y++)
            {
                raw.Add(0);

                var row = new byte[rowBytes];

                for (var x = 0; x < passWidth; x++)
                {
                    var index = indexes[(startY + y * stepY) * width + startX + x * stepX];

                    row[x / 2] |= (byte)((index & 0x0f) << ((x & 1) == 0 ? 4 : 0));
                }

                raw.AddRange(row);
            }
        }

        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write([.. raw], 0, raw.Count);

        WriteChunk(output, "IDAT", deflated.ToArray());
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    /// <summary>
    /// Grey written in fewer than eight bits a pixel, which is how a black and white page is kept.
    /// The shades given are rounded to what the depth can name.
    /// </summary>
    public static byte[] WriteGrey(int width, int height, byte[] shades, int bits)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        var header = new byte[13];
        WriteInt32(header, 0, width);
        WriteInt32(header, 4, height);
        header[8] = (byte)bits;
        header[9] = 0;                      // grey, with no palette and no alpha
        WriteChunk(output, "IHDR", header);

        var top = (1 << bits) - 1;
        var rowBytes = (width * bits + 7) / 8;
        var raw = new List<byte>();

        for (var y = 0; y < height; y++)
        {
            raw.Add(0);

            var row = new byte[rowBytes];

            for (var x = 0; x < width; x++)
            {
                var value = shades[y * width + x] * top / 255;
                var perByte = 8 / bits;
                var shift = 8 - bits * (x % perByte + 1);

                row[x / perByte] |= (byte)((value & top) << shift);
            }

            raw.AddRange(row);
        }

        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write([.. raw], 0, raw.Count);

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
