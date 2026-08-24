namespace n8PDF.Images;

/// <summary>
/// Reads a GIF.
/// </summary>
/// <remarks>
/// A GIF is a screen and the frames drawn onto it. Only the first frame is read here — an animated
/// one has to stand still on a page, and standing still is what its first frame is for.
///
/// The pixels are indexes into a table of at most 256 colours, packed with LZW: codes of a width
/// that grows as the table fills, written low bit first. One colour of the table can be declared
/// transparent, which is the only transparency a GIF has.
/// </remarks>
internal static class GifDecoder
{
    public static bool IsGif(byte[] data) =>
        data.Length > 13 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F';

    public static ImageData Decode(byte[] data, long maximumPixels = ImageLimits.DefaultMaximumPixels)
    {
        if (!IsGif(data)) throw new ImageFormatException("Not a GIF.");

        var screenWidth = data[6] | (data[7] << 8);
        var screenHeight = data[8] | (data[9] << 8);

        if (screenWidth <= 0 || screenHeight <= 0)
            throw new ImageFormatException("GIF declares an empty screen.");

        ImageLimits.Check(screenWidth, screenHeight, maximumPixels, "GIF");

        var packed = data[10];
        var at = 13;

        byte[]? globalPalette = null;
        if ((packed & 0x80) != 0)
        {
            var count = 2 << (packed & 0x07);
            globalPalette = Palette(data, at, count);
            at += count * 3;
        }

        int? transparent = null;

        while (at < data.Length)
        {
            switch (data[at])
            {
                case 0x21:
                {
                    // An extension: the graphic control block is the one that matters, since it
                    // says which colour of the table is not to be drawn at all.
                    var label = at + 1 < data.Length ? data[at + 1] : 0;
                    at += 2;

                    if (label == 0xF9 && at < data.Length && data[at] >= 4 && at + 4 < data.Length)
                    {
                        if ((data[at + 1] & 0x01) != 0) transparent = data[at + 4];
                    }

                    at = SkipBlocks(data, at);
                    continue;
                }

                case 0x2C:
                    return Frame(data, at, screenWidth, screenHeight, globalPalette, transparent, maximumPixels);

                case 0x3B:
                default:
                    throw new ImageFormatException("GIF holds no image.");
            }
        }

        throw new ImageFormatException("GIF ends before its image.");
    }

    private static ImageData Frame(
        byte[] data, int at, int screenWidth, int screenHeight, byte[]? globalPalette, int? transparent,
        long maximumPixels)
    {
        // The ten-byte image descriptor and the code-size byte after the colour table must be
        // present; a truncated file would otherwise index past its end (#9).
        if (at + 10 > data.Length)
            throw new ImageFormatException("GIF image descriptor runs past the end of the file.");

        var left = data[at + 1] | (data[at + 2] << 8);
        var top = data[at + 3] | (data[at + 4] << 8);
        var width = data[at + 5] | (data[at + 6] << 8);
        var height = data[at + 7] | (data[at + 8] << 8);
        var packed = data[at + 9];

        at += 10;

        var palette = globalPalette;
        if ((packed & 0x80) != 0)
        {
            var count = 2 << (packed & 0x07);
            palette = Palette(data, at, count);
            at += count * 3;
        }

        if (palette is null) throw new ImageFormatException("GIF has no colour table.");
        if (width <= 0 || height <= 0) throw new ImageFormatException("GIF frame is empty.");

        // The frame's own width and height are independent 16-bit fields, unrelated to the logical
        // screen the header check bounded — so a 31-byte GIF can declare a frame of 2GB. They are
        // checked here, where the frame is read and before width*height sizes anything (#8).
        ImageLimits.Check(width, height, maximumPixels, "GIF");

        var interlaced = (packed & 0x40) != 0;

        if (at >= data.Length) throw new ImageFormatException("GIF ends before its image data.");  // (#9)

        var minimumCodeSize = data[at];
        at++;

        var indexes = Unpack(Concatenate(data, ref at), minimumCodeSize, width * height);

        // The frame may be smaller than the screen and sit somewhere inside it, so the picture is
        // the screen and the frame is drawn onto it.
        var pixels = new byte[screenWidth * screenHeight * 3];
        var alpha = new byte[screenWidth * screenHeight];

        // Anything the frame does not cover is not part of the picture at all.
        if (width != screenWidth || height != screenHeight || left != 0 || top != 0)
            Array.Fill(alpha, (byte)0);
        else
            Array.Fill(alpha, (byte)255);

        var opaque = true;
        var rows = interlaced ? InterlacedRows(height) : null;

        for (var y = 0; y < height; y++)
        {
            var row = rows is null ? y : rows[y];
            var target = top + row;
            if (target < 0 || target >= screenHeight) continue;

            for (var x = 0; x < width; x++)
            {
                var column = left + x;
                if (column < 0 || column >= screenWidth) continue;

                var index = indexes[y * width + x];
                var pixel = (target * screenWidth + column) * 3;

                if (index == transparent)
                {
                    alpha[target * screenWidth + column] = 0;
                    opaque = false;
                    continue;
                }

                var entry = index * 3;

                pixels[pixel] = entry + 2 < palette.Length ? palette[entry] : (byte)0;
                pixels[pixel + 1] = entry + 2 < palette.Length ? palette[entry + 1] : (byte)0;
                pixels[pixel + 2] = entry + 2 < palette.Length ? palette[entry + 2] : (byte)0;

                alpha[target * screenWidth + column] = 255;
            }
        }

        // A frame covering the whole screen with no transparent colour in it needs no mask.
        var covered = width == screenWidth && height == screenHeight && left == 0 && top == 0;

        return new ImageData(screenWidth, screenHeight, pixels, ImageEncoding.Raw, ImageColorSpace.Rgb,
            opaque && covered ? null : alpha);
    }

    /// <summary>
    /// The rows of an interlaced frame in the order they are written: every eighth from the top,
    /// then every eighth from the fifth, then every fourth, then the rest.
    /// </summary>
    private static int[] InterlacedRows(int height)
    {
        var rows = new int[height];
        var at = 0;

        foreach (var (start, step) in new[] { (0, 8), (4, 8), (2, 4), (1, 2) })
        {
            for (var y = start; y < height; y += step) rows[at++] = y;
        }

        return rows;
    }

    private static byte[] Palette(byte[] data, int at, int count)
    {
        var palette = new byte[count * 3];
        var available = Math.Max(0, Math.Min(palette.Length, data.Length - at));

        Array.Copy(data, at, palette, 0, available);

        return palette;
    }

    /// <summary>Joins the sub-blocks a GIF writes its data in, each led by its own length.</summary>
    private static byte[] Concatenate(byte[] data, ref int at)
    {
        var joined = new List<byte>();

        while (at < data.Length)
        {
            var length = data[at];
            at++;

            if (length == 0) break;

            for (var i = 0; i < length && at < data.Length; i++, at++) joined.Add(data[at]);
        }

        return [.. joined];
    }

    private static int SkipBlocks(byte[] data, int at)
    {
        while (at < data.Length)
        {
            var length = data[at];
            at++;

            if (length == 0) break;

            at += length;
        }

        return at;
    }

    /// <summary>
    /// Unpacks the LZW codes into colour indexes.
    /// </summary>
    /// <remarks>
    /// The table starts as the colours themselves plus two codes of its own — one to clear it and
    /// one to end — and grows by a word at a time, each the last word and the first letter of the
    /// next. The code width grows with it. The one case that needs care is a code that is not in
    /// the table yet, which can only be the last word with its own first letter added.
    /// </remarks>
    private static byte[] Unpack(byte[] compressed, int minimumCodeSize, int count)
    {
        if (minimumCodeSize is < 2 or > 11) throw new ImageFormatException("GIF has an unreadable code size.");

        var clear = 1 << minimumCodeSize;
        var end = clear + 1;

        var prefix = new int[4096];
        var suffix = new byte[4096];
        var first = new byte[4096];

        void Reset()
        {
            for (var i = 0; i < clear; i++)
            {
                prefix[i] = -1;
                suffix[i] = (byte)i;
                first[i] = (byte)i;
            }
        }

        Reset();

        var next = end + 1;
        var codeSize = minimumCodeSize + 1;
        var previous = -1;

        var output = new byte[count];
        var written = 0;

        var bits = 0;
        var bitCount = 0;
        var at = 0;
        var stack = new byte[4096];

        while (written < count)
        {
            while (bitCount < codeSize)
            {
                if (at >= compressed.Length) return output;

                bits |= compressed[at++] << bitCount;
                bitCount += 8;
            }

            var code = bits & ((1 << codeSize) - 1);
            bits >>= codeSize;
            bitCount -= codeSize;

            if (code == clear)
            {
                Reset();
                next = end + 1;
                codeSize = minimumCodeSize + 1;
                previous = -1;
                continue;
            }

            if (code == end) break;

            var current = code;
            var depth = 0;

            // A code the table has not reached yet stands for the last word and its own opening
            // letter, which is the one case the table cannot answer for itself.
            if (code >= next)
            {
                if (previous < 0) break;

                stack[depth++] = first[previous];
                current = previous;
            }

            while (current >= 0 && depth < stack.Length)
            {
                stack[depth++] = suffix[current];
                current = prefix[current];
            }

            for (var i = depth - 1; i >= 0 && written < count; i--) output[written++] = stack[i];

            if (previous >= 0 && next < 4096)
            {
                prefix[next] = previous;
                suffix[next] = first[code < next ? code : previous];
                first[next] = first[previous];
                next++;

                if (next == (1 << codeSize) && codeSize < 12) codeSize++;
            }

            previous = code;
        }

        return output;
    }
}
