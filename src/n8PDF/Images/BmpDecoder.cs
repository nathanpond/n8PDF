namespace n8PDF.Images;

/// <summary>
/// Reads a Windows bitmap.
/// </summary>
/// <remarks>
/// A BMP is a header, sometimes a palette, and then the pixels — with two habits that catch every
/// reader that assumes otherwise. The rows run bottom to top unless the height is written as a
/// negative number, and each row is padded out to a multiple of four bytes however few pixels it
/// holds. The samples themselves are in the order blue, green, red.
///
/// The alpha channel of a 32-bit bitmap is only believed where the file says it has one. Plenty of
/// them carry a fourth byte that was never written to, and reading it as transparency turns an
/// opaque picture invisible.
/// </remarks>
internal static class BmpDecoder
{
    public static bool IsBmp(byte[] data) =>
        data.Length > 54 && data[0] == 'B' && data[1] == 'M';

    public static ImageData Decode(byte[] data, long maximumPixels = ImageLimits.DefaultMaximumPixels)
    {
        if (!IsBmp(data)) throw new ImageFormatException("Not a bitmap.");

        var pixelOffset = ReadInt32(data, 10);
        var headerSize = ReadInt32(data, 14);

        // The oldest header is twelve bytes and writes its size as two-byte numbers; everything
        // since is forty bytes or more and writes them as four.
        var core = headerSize == 12;

        // The old header writes both sizes as two-byte numbers, one after the other; every
        // later one writes them as four.
        var width = core ? ReadInt16(data, 18) : ReadInt32(data, 18);
        var rawHeight = core ? ReadInt16(data, 20) : ReadInt32(data, 22);
        var bits = core ? ReadUInt16(data, 24) : ReadUInt16(data, 28);
        var compression = core ? 0 : ReadInt32(data, 30);

        // int.MinValue has no positive counterpart, so Math.Abs throws on it; refused before
        // that (#11). A negative header size would seat the palette before the buffer (#12).
        if (rawHeight == int.MinValue || headerSize < 0)
            throw new ImageFormatException("Bitmap header is malformed.");

        var height = Math.Abs(rawHeight);
        var topDown = rawHeight < 0;

        if (width <= 0 || height <= 0) throw new ImageFormatException("Bitmap declares an empty image.");

        ImageLimits.Check(width, height, maximumPixels, "bitmap");
        if (bits is not (1 or 4 or 8 or 16 or 24 or 32))
            throw new ImageFormatException($"Bitmap has {bits} bits a pixel, which is not handled.");

        var masks = Masks(data, headerSize, bits, compression);
        var palette = ReadPalette(data, headerSize, bits, core);

        var pixels = new byte[width * height * 3];
        byte[]? alpha = null;

        // The scanlines are read into one flat buffer of height*rowBytes rather than a jagged
        // array of one object per row: the flat cost is bounded by the area the pixel limit
        // already checks, where the object count of the jagged form scaled with height alone and
        // turned a 55-byte bitmap declaring itself one pixel wide and fifty million tall into
        // fifty million allocations (#10).
        var rowBytes = (width * bits + 31) / 32 * 4;

        var rows = compression is 1 or 2
            ? DecodeRuns(data, pixelOffset, width, height, bits, rowBytes)
            : ReadRows(data, pixelOffset, width, height, bits, rowBytes);

        for (var y = 0; y < height; y++)
        {
            // The rows are written from the foot of the picture upwards unless it says otherwise.
            var row = topDown ? y : height - 1 - y;
            var line = rows.AsSpan(row * rowBytes, rowBytes);

            for (var x = 0; x < width; x++)
            {
                var target = (y * width + x) * 3;
                var (r, g, b, a) = Sample(line, x, bits, palette, masks);

                pixels[target] = r;
                pixels[target + 1] = g;
                pixels[target + 2] = b;

                if (a == 255) continue;

                alpha ??= Opaque(width * height);
                alpha[y * width + x] = a;
            }
        }

        return new ImageData(width, height, pixels, ImageEncoding.Raw, ImageColorSpace.Rgb, alpha);
    }

    private static byte[] Opaque(int count)
    {
        var alpha = new byte[count];
        Array.Fill(alpha, (byte)255);

        return alpha;
    }

    /// <summary>
    /// Which bits of a pixel hold which colour. A bitmap only says where it is not the usual
    /// order, and the alpha mask is what makes a fourth channel worth reading.
    /// </summary>
    private readonly record struct ChannelMasks(uint Red, uint Green, uint Blue, uint Alpha)
    {
        public bool HasAlpha => Alpha != 0;
    }

    private static ChannelMasks Masks(byte[] data, int headerSize, int bits, int compression)
    {
        // The masks follow a forty-byte header, and live inside a longer one.
        if (compression == 3 && headerSize == 40 && 14 + 40 + 12 <= data.Length)
        {
            return new ChannelMasks(
                (uint)ReadInt32(data, 54), (uint)ReadInt32(data, 58), (uint)ReadInt32(data, 62), 0);
        }

        if (headerSize >= 108 && 14 + 56 <= data.Length)
        {
            return new ChannelMasks(
                (uint)ReadInt32(data, 54), (uint)ReadInt32(data, 58),
                (uint)ReadInt32(data, 62), (uint)ReadInt32(data, 66));
        }

        // Sixteen bits a pixel means five to a channel where nothing says otherwise.
        return bits == 16
            ? new ChannelMasks(0x7C00, 0x03E0, 0x001F, 0)
            : default;
    }

    private static byte[]? ReadPalette(byte[] data, int headerSize, int bits, bool core)
    {
        if (bits > 8) return null;

        var start = 14 + headerSize;
        if (start < 0 || start >= data.Length) return null;  // a bad header size seats it wild (#12)

        var entrySize = core ? 3 : 4;
        var count = Math.Min(1 << bits, Math.Max(0, (data.Length - start) / entrySize));

        var palette = new byte[count * 3];

        for (var i = 0; i < count; i++)
        {
            var at = start + i * entrySize;

            // Stored blue first, like the pixels themselves.
            palette[i * 3] = data[at + 2];
            palette[i * 3 + 1] = data[at + 1];
            palette[i * 3 + 2] = data[at];
        }

        return palette;
    }

    /// <summary>The pixel rows, each padded out to a multiple of four bytes.</summary>
    private static byte[] ReadRows(byte[] data, int offset, int width, int height, int bits, int rowBytes)
    {
        var rows = new byte[(long)height * rowBytes <= int.MaxValue ? height * rowBytes : 0];

        for (var y = 0; y < height; y++)
        {
            // The pixel offset is a raw 32-bit field; computed in long it cannot overflow to a
            // small positive that reaches Array.Copy, and a negative start is skipped (#13).
            var start = (long)offset + (long)y * rowBytes;
            if (start >= 0 && start < data.Length)
                Array.Copy(data, (int)start, rows, y * rowBytes, (int)Math.Min(rowBytes, data.Length - start));
        }

        return rows;
    }

    /// <summary>
    /// Unpacks the run-length encodings, which write a bitmap as counts and colours rather than as
    /// pixels. A run is a count and an index; a count of nothing introduces either the end of a
    /// line, the end of the picture, a jump, or a run of pixels written out one by one.
    /// </summary>
    private static byte[] DecodeRuns(byte[] data, int offset, int width, int height, int bits, int rowBytes)
    {
        var rows = new byte[(long)height * rowBytes <= int.MaxValue ? height * rowBytes : 0];

        var x = 0;
        var line = 0;
        var at = offset;

        // at >= 0 guards a negative pixel offset, which would otherwise index the array below its
        // start on the first read (#14).
        while (at >= 0 && at + 1 < data.Length && line < height)
        {
            var count = data[at];
            var value = data[at + 1];
            at += 2;

            if (count > 0)
            {
                for (var i = 0; i < count && x < width; i++, x++)
                    Put(rows.AsSpan(line * rowBytes, rowBytes), x, bits, bits == 4 ? Nibble(value, i) : value);

                continue;
            }

            switch (value)
            {
                case 0:
                    x = 0;
                    line++;
                    continue;

                case 1:
                    return rows;

                case 2:
                    if (at + 1 >= data.Length) return rows;

                    x += data[at];
                    line += data[at + 1];
                    at += 2;
                    continue;

                default:
                {
                    // A literal run, padded out to an even number of bytes.
                    var pixels = value;
                    var used = bits == 4 ? (pixels + 1) / 2 : pixels;

                    for (var i = 0; i < pixels && x < width; i++, x++)
                    {
                        var index = bits == 4
                            ? Nibble(data[Math.Min(at + i / 2, data.Length - 1)], i)
                            : data[Math.Min(at + i, data.Length - 1)];

                        Put(rows.AsSpan(line * rowBytes, rowBytes), x, bits, index);
                    }

                    at += used + (used & 1);
                    continue;
                }
            }
        }

        return rows;
    }

    private static int Nibble(byte value, int index) => (index & 1) == 0 ? value >> 4 : value & 0x0f;

    private static void Put(Span<byte> row, int x, int bits, int value)
    {
        if (bits == 8)
        {
            if (x < row.Length) row[x] = (byte)value;
            return;
        }

        var at = x / 2;
        if (at >= row.Length) return;

        row[at] = (x & 1) == 0
            ? (byte)((row[at] & 0x0f) | ((value & 0x0f) << 4))
            : (byte)((row[at] & 0xf0) | (value & 0x0f));
    }

    private static (byte R, byte G, byte B, byte A) Sample(
        ReadOnlySpan<byte> line, int x, int bits, byte[]? palette, ChannelMasks masks)
    {
        switch (bits)
        {
            case 1 or 4 or 8:
            {
                var index = Packed(line, x, bits);
                var at = index * 3;

                return palette is not null && at + 2 < palette.Length
                    ? (palette[at], palette[at + 1], palette[at + 2], (byte)255)
                    : ((byte)0, (byte)0, (byte)0, (byte)255);
            }

            case 16:
            {
                var value = (uint)(line[x * 2] | (line[x * 2 + 1] << 8));
                return Channels(value, masks);
            }

            case 24:
            {
                var at = x * 3;
                return (line[at + 2], line[at + 1], line[at], 255);
            }

            default:
            {
                var at = x * 4;
                var value = (uint)(line[at] | (line[at + 1] << 8) | (line[at + 2] << 16) | (line[at + 3] << 24));

                if (masks.HasAlpha) return Channels(value, masks);

                // No alpha mask: the fourth byte means nothing and is left alone.
                return (line[at + 2], line[at + 1], line[at], 255);
            }
        }
    }

    private static (byte R, byte G, byte B, byte A) Channels(uint value, ChannelMasks masks) =>
        (Channel(value, masks.Red), Channel(value, masks.Green), Channel(value, masks.Blue),
            masks.HasAlpha ? Channel(value, masks.Alpha) : (byte)255);

    /// <summary>
    /// One channel of a packed pixel, spread back out over a whole byte: five bits of red are a
    /// number from nought to thirty-one, and thirty-one is white rather than a very dark grey.
    /// </summary>
    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0) return 0;

        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var width = System.Numerics.BitOperations.PopCount(mask);
        var raw = (value & mask) >> shift;

        var top = (1u << width) - 1;

        return (byte)(top == 0 ? 0 : raw * 255 / top);
    }

    private static int Packed(ReadOnlySpan<byte> row, int x, int bits)
    {
        if (bits == 8) return x < row.Length ? row[x] : 0;

        var perByte = 8 / bits;
        var at = x / perByte;
        if (at >= row.Length) return 0;

        var shift = 8 - bits * (x % perByte + 1);

        return (row[at] >> shift) & ((1 << bits) - 1);
    }

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static short ReadInt16(byte[] data, int offset) =>
        (short)(data[offset] | (data[offset + 1] << 8));

    private static int ReadUInt16(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8);
}
