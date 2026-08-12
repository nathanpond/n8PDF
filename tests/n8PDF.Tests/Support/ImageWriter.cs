namespace n8PDF.Tests.Support;

/// <summary>
/// Writes the picture formats the converter reads, so that reading them can be tested against a
/// file whose every pixel is known.
/// </summary>
/// <remarks>
/// These are only as much of each format as it takes to make a file a reader has to work for: a
/// bitmap of one, four, eight or twenty-four bits a pixel, written from the foot up or the top
/// down; a GIF that is interlaced, or has a colour it treats as transparent; a TIFF at either end
/// and packed each of the ways it can be. Nothing here is a general encoder.
/// </remarks>
public static class ImageWriter
{
    /// <summary>A picture of the given size, as red, green and blue bytes.</summary>
    public static byte[] Pixels(int width, int height, Func<int, int, (byte R, byte G, byte B)> colour)
    {
        var pixels = new byte[width * height * 3];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var (r, g, b) = colour(x, y);
            var at = (y * width + x) * 3;

            pixels[at] = r;
            pixels[at + 1] = g;
            pixels[at + 2] = b;
        }

        return pixels;
    }

    /// <summary>Four colours in a pattern, which fits into a palette of any size worth having.</summary>
    public static byte[] Sample(int width, int height) =>
        Pixels(width, height, (x, y) => ((x + y) % 4) switch
        {
            0 => ((byte)200, (byte)30, (byte)40),
            1 => ((byte)30, (byte)160, (byte)70),
            2 => ((byte)40, (byte)70, (byte)210),
            _ => ((byte)240, (byte)230, (byte)10)
        });

    // ----- BMP -----

    /// <summary>
    /// A bitmap of the given depth. Depths of eight and under take a palette, which is built from
    /// the colours the picture uses.
    /// </summary>
    public static byte[] Bmp(int width, int height, byte[] pixels, int bits = 24, bool topDown = false)
    {
        var palette = bits <= 8 ? BuildPalette(pixels, 1 << bits) : [];
        var rowBytes = (width * bits + 31) / 32 * 4;

        var rows = new List<byte>();

        for (var y = 0; y < height; y++)
        {
            var source = topDown ? y : height - 1 - y;
            var row = new byte[rowBytes];

            for (var x = 0; x < width; x++)
            {
                var at = (source * width + x) * 3;

                if (bits == 24)
                {
                    row[x * 3] = pixels[at + 2];
                    row[x * 3 + 1] = pixels[at + 1];
                    row[x * 3 + 2] = pixels[at];
                    continue;
                }

                var index = IndexOf(palette, pixels[at], pixels[at + 1], pixels[at + 2]);

                if (bits == 8)
                {
                    row[x] = (byte)index;
                    continue;
                }

                var perByte = 8 / bits;
                var shift = 8 - bits * (x % perByte + 1);
                row[x / perByte] |= (byte)((index & ((1 << bits) - 1)) << shift);
            }

            rows.AddRange(row);
        }

        var paletteBytes = new List<byte>();
        foreach (var (r, g, b) in palette)
        {
            paletteBytes.Add(b);
            paletteBytes.Add(g);
            paletteBytes.Add(r);
            paletteBytes.Add(0);
        }

        var offset = 14 + 40 + paletteBytes.Count;

        var file = new List<byte> { (byte)'B', (byte)'M' };
        file.AddRange(Int32(offset + rows.Count));
        file.AddRange(Int32(0));
        file.AddRange(Int32(offset));

        file.AddRange(Int32(40));
        file.AddRange(Int32(width));
        file.AddRange(Int32(topDown ? -height : height));
        file.AddRange(Int16(1));
        file.AddRange(Int16(bits));
        file.AddRange(Int32(0));
        file.AddRange(Int32(rows.Count));
        file.AddRange(Int32(2835));
        file.AddRange(Int32(2835));
        file.AddRange(Int32(palette.Count));
        file.AddRange(Int32(0));

        file.AddRange(paletteBytes);
        file.AddRange(rows);

        return [.. file];
    }

    // ----- GIF -----

    /// <summary>
    /// A GIF of the picture given, with the colours it uses as its table. A transparent index
    /// makes the colour at that place in the table one the reader is to leave alone.
    /// </summary>
    public static byte[] Gif(
        int width, int height, byte[] pixels, bool interlaced = false, int? transparent = null)
    {
        var palette = BuildPalette(pixels, 256);

        // A GIF's table is a power of two long, and at least four.
        var size = 2;
        while (1 << size < Math.Max(4, palette.Count)) size++;

        var entries = 1 << size;

        var file = new List<byte>("GIF89a"u8.ToArray())
        {
            (byte)(width & 0xff), (byte)(width >> 8),
            (byte)(height & 0xff), (byte)(height >> 8),
            (byte)(0x80 | (size - 1)),
            0,
            0
        };

        for (var i = 0; i < entries; i++)
        {
            var (r, g, b) = i < palette.Count ? palette[i] : ((byte)0, (byte)0, (byte)0);
            file.Add(r);
            file.Add(g);
            file.Add(b);
        }

        if (transparent is { } index)
        {
            file.AddRange(new byte[] { 0x21, 0xF9, 4, 0x01, 0, 0, (byte)index, 0 });
        }

        file.Add(0x2C);
        file.AddRange([0, 0, 0, 0]);
        file.AddRange(new[] { (byte)(width & 0xff), (byte)(width >> 8), (byte)(height & 0xff), (byte)(height >> 8) });
        file.Add((byte)(interlaced ? 0x40 : 0x00));

        var indexes = new List<byte>();
        var rows = interlaced ? InterlacedRows(height) : [.. Enumerable.Range(0, height)];

        foreach (var y in rows)
        {
            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 3;
                indexes.Add((byte)IndexOf(palette, pixels[at], pixels[at + 1], pixels[at + 2]));
            }
        }

        var codeSize = Math.Max(2, size);
        file.Add((byte)codeSize);
        file.AddRange(SubBlocks(Codes(indexes, codeSize)));
        file.Add(0x3B);

        return [.. file];
    }

    private static int[] InterlacedRows(int height)
    {
        var rows = new List<int>();

        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var y = start; y < height; y += step) rows.Add(y);
        }

        return [.. rows];
    }

    /// <summary>
    /// The pixels as LZW codes, written the simplest way a reader will accept: every pixel as its
    /// own code, with the table cleared before it could ever grow. It is not compression, but it
    /// is the same stream to read.
    /// </summary>
    private static byte[] Codes(List<byte> indexes, int codeSize)
    {
        var clear = 1 << codeSize;
        var end = clear + 1;

        var bits = new List<byte>();
        var buffer = 0;
        var count = 0;
        var width = codeSize + 1;
        var next = end + 1;

        void Write(int code)
        {
            buffer |= code << count;
            count += width;

            while (count >= 8)
            {
                bits.Add((byte)(buffer & 0xff));
                buffer >>= 8;
                count -= 8;
            }
        }

        Write(clear);

        foreach (var index in indexes)
        {
            Write(index);
            next++;

            // Cleared before the table could grow the codes any wider, which keeps every code the
            // width it began at.
            if (next < (1 << width) - 1) continue;

            Write(clear);
            next = end + 1;
        }

        Write(end);

        if (count > 0) bits.Add((byte)(buffer & 0xff));

        return [.. bits];
    }

    private static byte[] SubBlocks(byte[] data)
    {
        var blocks = new List<byte>();

        for (var at = 0; at < data.Length; at += 255)
        {
            var length = Math.Min(255, data.Length - at);

            blocks.Add((byte)length);
            blocks.AddRange(data[at..(at + length)]);
        }

        blocks.Add(0);

        return [.. blocks];
    }

    // ----- TIFF -----

    /// <summary>
    /// A TIFF of the picture given, written from whichever end is asked for and packed either not
    /// at all or with PackBits.
    /// </summary>
    public static byte[] Tiff(
        int width, int height, byte[] pixels, bool little = true, bool packBits = false,
        bool greyscale = false)
    {
        var samples = greyscale ? 1 : 3;
        var rowBytes = width * samples;

        var raw = new List<byte>();

        for (var y = 0; y < height; y++)
        {
            var row = new List<byte>();

            for (var x = 0; x < width; x++)
            {
                var at = (y * width + x) * 3;

                if (greyscale)
                {
                    // The usual weighting of the three channels into one.
                    row.Add((byte)((pixels[at] * 299 + pixels[at + 1] * 587 + pixels[at + 2] * 114) / 1000));
                    continue;
                }

                row.Add(pixels[at]);
                row.Add(pixels[at + 1]);
                row.Add(pixels[at + 2]);
            }

            raw.AddRange(packBits ? PackBits([.. row]) : row);
        }

        var tags = new List<(int Id, int Type, int Count, int Value)>
        {
            (256, 3, 1, width),
            (257, 3, 1, height),
            (258, 3, 1, 8),
            (259, 3, 1, packBits ? 32773 : 1),
            (262, 3, 1, greyscale ? 1 : 2),
            (273, 4, 1, 0),
            (277, 3, 1, samples),
            (278, 3, 1, height),
            (279, 4, 1, raw.Count),
            (284, 3, 1, 1)
        };

        // The directory sits after the pixels, which keeps the offsets easy to work out.
        var pixelsAt = 8;
        var directoryAt = pixelsAt + raw.Count;

        var file = new List<byte>();
        file.AddRange(little ? "II"u8.ToArray() : "MM"u8.ToArray());
        file.AddRange(Number(42, 2, little));
        file.AddRange(Number(directoryAt, 4, little));
        file.AddRange(raw);

        file.AddRange(Number(tags.Count, 2, little));

        foreach (var (id, type, count, value) in tags)
        {
            file.AddRange(Number(id, 2, little));
            file.AddRange(Number(type, 2, little));
            file.AddRange(Number(count, 4, little));

            // A number shorter than the four bytes kept for it is written into the front of them,
            // which is the high end where the file is written big end first.
            if (id == 273) file.AddRange(Number(pixelsAt, 4, little));

            // A number shorter than the field goes into the front of it either way round: the
            // first two bytes, which are the high ones where the file is written big end first.
            else if (type == 3) file.AddRange([.. Number(value, 2, little), 0, 0]);
            else file.AddRange(Number(value, 4, little));
        }

        file.AddRange(Number(0, 4, little));

        return [.. file];
    }

    private static byte[] PackBits(byte[] row)
    {
        var packed = new List<byte>();
        var at = 0;

        while (at < row.Length)
        {
            var run = 1;
            while (at + run < row.Length && run < 128 && row[at + run] == row[at]) run++;

            if (run > 1)
            {
                packed.Add((byte)(sbyte)(1 - run));
                packed.Add(row[at]);
                at += run;
                continue;
            }

            var literal = 1;
            while (at + literal < row.Length && literal < 128 &&
                   (at + literal + 1 >= row.Length || row[at + literal] != row[at + literal + 1]))
            {
                literal++;
            }

            packed.Add((byte)(literal - 1));
            packed.AddRange(row[at..(at + literal)]);
            at += literal;
        }

        return [.. packed];
    }

    private static byte[] Number(int value, int size, bool little)
    {
        var bytes = new byte[size];

        for (var i = 0; i < size; i++)
            bytes[little ? i : size - 1 - i] = (byte)(value >> (8 * i));

        return bytes;
    }

    // ----- shared -----

    private static List<(byte R, byte G, byte B)> BuildPalette(byte[] pixels, int limit)
    {
        var palette = new List<(byte, byte, byte)>();

        for (var at = 0; at + 2 < pixels.Length; at += 3)
        {
            var colour = (pixels[at], pixels[at + 1], pixels[at + 2]);

            if (!palette.Contains(colour) && palette.Count < limit) palette.Add(colour);
        }

        return palette;
    }

    private static int IndexOf(List<(byte R, byte G, byte B)> palette, byte r, byte g, byte b)
    {
        var index = palette.IndexOf((r, g, b));

        return index < 0 ? 0 : index;
    }

    private static byte[] Int32(int value) =>
        [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static byte[] Int16(int value) => [(byte)value, (byte)(value >> 8)];
}
