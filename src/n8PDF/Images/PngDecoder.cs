using System.IO.Compression;

namespace n8PDF.Images;

/// <summary>
/// Decodes PNG to raw samples.
/// </summary>
/// <remarks>
/// PDF has no PNG filter, so unlike JPEG a PNG cannot be passed through: it has to be unpacked
/// to samples and recompressed. Written from scratch on top of <c>ZLibStream</c>, which supplies
/// the inflate step and nothing else — the chunk structure, the per-row filters and the colour
/// models are all handled here.
/// </remarks>
public static class PngDecoder
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool IsPng(byte[] data)
    {
        if (data.Length < Signature.Length) return false;

        for (var i = 0; i < Signature.Length; i++)
        {
            if (data[i] != Signature[i]) return false;
        }

        return true;
    }

    public static ImageData Decode(byte[] data)
    {
        if (!IsPng(data)) throw new ImageFormatException("Not a PNG file.");

        var position = Signature.Length;

        var width = 0;
        var height = 0;
        var bitDepth = 8;
        var colorType = 6;
        byte[]? palette = null;
        byte[]? paletteAlpha = null;
        var interlaced = false;
        var compressed = new MemoryStream();

        while (position + 8 <= data.Length)
        {
            var length = ReadInt32(data, position);
            var type = System.Text.Encoding.ASCII.GetString(data, position + 4, 4);
            var body = position + 8;

            if (length < 0 || body + length > data.Length)
                throw new ImageFormatException($"PNG chunk '{type}' runs past the end of the file.");

            switch (type)
            {
                case "IHDR":
                    width = ReadInt32(data, body);
                    height = ReadInt32(data, body + 4);
                    bitDepth = data[body + 8];
                    colorType = data[body + 9];
                    interlaced = data[body + 12] != 0;
                    break;

                case "PLTE":
                    palette = new byte[length];
                    Array.Copy(data, body, palette, 0, length);
                    break;

                case "tRNS":
                    // For palette images this is one alpha byte per palette entry. The greyscale
                    // and truecolour forms specify a single transparent value, which is rarer and
                    // not handled.
                    if (colorType == 3)
                    {
                        paletteAlpha = new byte[length];
                        Array.Copy(data, body, paletteAlpha, 0, length);
                    }

                    break;

                case "IDAT":
                    compressed.Write(data, body, length);
                    break;

                case "IEND":
                    position = data.Length;
                    continue;
            }

            // Chunk layout is length, type, body, then a four-byte CRC.
            position = body + length + 4;
        }

        if (width <= 0 || height <= 0)
            throw new ImageFormatException("PNG has no valid IHDR.");

        var samples = Inflate(compressed.ToArray());

        return Unfilter(samples, width, height, bitDepth, colorType, palette, paletteAlpha, interlaced);
    }

    private static byte[] Inflate(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var inflate = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>
    /// Reverses the per-row filters and expands the result into 8-bit samples.
    /// </summary>
    /// <remarks>
    /// Every PNG row is prefixed by a filter byte, and the filters are defined over the bytes of
    /// the previous row, so decoding is inherently sequential.
    /// </remarks>
    private static ImageData Unfilter(
        byte[] samples, int width, int height, int bitDepth, int colorType,
        byte[]? palette, byte[]? paletteAlpha, bool interlaced)
    {
        var channels = colorType switch
        {
            0 => 1, // greyscale
            2 => 3, // truecolour
            3 => 1, // palette index
            4 => 2, // greyscale with alpha
            6 => 4, // truecolour with alpha
            _ => throw new ImageFormatException($"Unsupported PNG colour type {colorType}.")
        };

        if (bitDepth is not (1 or 2 or 4 or 8 or 16))
            throw new ImageFormatException($"Unsupported PNG bit depth {bitDepth}.");

        // Fewer than eight bits a sample is only ever a palette index or a shade of grey; the
        // colour types that carry three channels or an alpha have at least a byte each.
        if (bitDepth < 8 && colorType is not (0 or 3))
            throw new ImageFormatException($"Unsupported PNG bit depth {bitDepth} for colour type {colorType}.");

        var bitsPerPixel = channels * bitDepth;
        var rowBytes = (width * bitsPerPixel + 7) / 8;

        var raw = interlaced
            ? Weave(samples, width, height, bitsPerPixel)
            : Rows(samples, 0, width, height, bitsPerPixel);

        return Expand(raw, width, height, bitDepth, colorType, channels, rowBytes, palette, paletteAlpha);
    }

    /// <summary>
    /// The seven passes an interlaced PNG is written in, each named by where in a block of eight
    /// by eight pixels it starts and how far apart the pixels it carries are.
    /// </summary>
    private static readonly (int X, int Y, int StepX, int StepY)[] Passes =
    [
        (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4),
        (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)
    ];

    /// <summary>
    /// Puts an interlaced image back together.
    /// </summary>
    /// <remarks>
    /// An interlaced PNG is not one picture but seven, each a coarser or finer sieve of the whole
    /// — the first every eighth pixel of every eighth row, the last every pixel of every other
    /// row. Each is written as an image in its own right, with its own rows and its own filters
    /// over them, so each has to be unfiltered on its own before its pixels are put where they
    /// belong. A pass whose sieve catches nothing of a small picture is not written at all.
    /// </remarks>
    private static byte[] Weave(byte[] samples, int width, int height, int bitsPerPixel)
    {
        var rowBytes = (width * bitsPerPixel + 7) / 8;
        var raw = new byte[rowBytes * height];
        var at = 0;

        foreach (var (startX, startY, stepX, stepY) in Passes)
        {
            var passWidth = (width - startX + stepX - 1) / stepX;
            var passHeight = (height - startY + stepY - 1) / stepY;

            if (passWidth <= 0 || passHeight <= 0) continue;

            var pass = Rows(samples, at, passWidth, passHeight, bitsPerPixel);
            at += ((passWidth * bitsPerPixel + 7) / 8 + 1) * passHeight;

            var passRowBytes = (passWidth * bitsPerPixel + 7) / 8;

            for (var y = 0; y < passHeight; y++)
            for (var x = 0; x < passWidth; x++)
            {
                CopyPixel(
                    pass, y * passRowBytes, x,
                    raw, (startY + y * stepY) * rowBytes, startX + x * stepX, bitsPerPixel);
            }
        }

        return raw;
    }

    /// <summary>One pixel from one row to another, whether it is bytes or a few bits.</summary>
    private static void CopyPixel(
        byte[] source, int sourceRow, int sourceX, byte[] target, int targetRow, int targetX, int bitsPerPixel)
    {
        if (bitsPerPixel >= 8)
        {
            var size = bitsPerPixel / 8;
            Array.Copy(source, sourceRow + sourceX * size, target, targetRow + targetX * size, size);

            return;
        }

        var value = ReadPackedSample(source, sourceRow, sourceX, bitsPerPixel);

        var perByte = 8 / bitsPerPixel;
        var at = targetRow + targetX / perByte;
        var shift = 8 - bitsPerPixel * (targetX % perByte + 1);

        target[at] = (byte)((target[at] & ~(((1 << bitsPerPixel) - 1) << shift)) | (value << shift));
    }

    /// <summary>
    /// Reverses the filters of one image's rows, which is every row prefixed by the filter it was
    /// written with, over the row before it.
    /// </summary>
    private static byte[] Rows(byte[] samples, int offset, int width, int height, int bitsPerPixel)
    {
        var rowBytes = (width * bitsPerPixel + 7) / 8;
        var filterStride = Math.Max(1, bitsPerPixel / 8);

        if (samples.Length - offset < (rowBytes + 1) * height)
            throw new ImageFormatException("PNG image data is shorter than its header describes.");

        var raw = new byte[rowBytes * height];
        var previous = new byte[rowBytes];

        for (var y = 0; y < height; y++)
        {
            var filter = samples[offset + y * (rowBytes + 1)];
            var source = offset + y * (rowBytes + 1) + 1;
            var target = y * rowBytes;

            for (var i = 0; i < rowBytes; i++)
            {
                int value = samples[source + i];
                int left = i >= filterStride ? raw[target + i - filterStride] : 0;
                int up = previous[i];
                int upLeft = i >= filterStride ? previous[i - filterStride] : 0;

                raw[target + i] = filter switch
                {
                    0 => (byte)value,
                    1 => (byte)(value + left),
                    2 => (byte)(value + up),
                    3 => (byte)(value + (left + up) / 2),
                    4 => (byte)(value + Paeth(left, up, upLeft)),
                    _ => throw new ImageFormatException($"Unknown PNG row filter {filter}.")
                };
            }

            Array.Copy(raw, target, previous, 0, rowBytes);
        }

        return raw;
    }

    private static ImageData Expand(
        byte[] raw, int width, int height, int bitDepth, int colorType, int channels, int rowBytes,
        byte[]? palette, byte[]? paletteAlpha)
    {
        // Palette images become full RGB: PDF can carry an indexed colour space, but expanding is
        // simpler and the size difference is absorbed by Flate.
        if (colorType == 3)
        {
            if (palette is null) throw new ImageFormatException("Palette PNG has no PLTE chunk.");

            var rgb = new byte[width * height * 3];
            byte[]? alpha = paletteAlpha is not null ? new byte[width * height] : null;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var index = ReadPackedSample(raw, y * rowBytes, x, bitDepth);
                    var entry = index * 3;

                    var target = (y * width + x) * 3;
                    rgb[target] = entry + 2 < palette.Length ? palette[entry] : (byte)0;
                    rgb[target + 1] = entry + 2 < palette.Length ? palette[entry + 1] : (byte)0;
                    rgb[target + 2] = entry + 2 < palette.Length ? palette[entry + 2] : (byte)0;

                    if (alpha is not null)
                        alpha[y * width + x] = index < paletteAlpha!.Length ? paletteAlpha[index] : (byte)255;
                }
            }

            return new ImageData(width, height, rgb, ImageEncoding.Raw, ImageColorSpace.Rgb, alpha);
        }

        // Grey written in fewer than eight bits a pixel: the samples are packed several to the
        // byte, like a palette's indexes, and each is spread back out over the whole range — one
        // bit of grey is black and white rather than black and very nearly black.
        if (bitDepth < 8)
        {
            var shades = new byte[width * height];
            var top = (1 << bitDepth) - 1;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                    shades[y * width + x] = (byte)(ReadPackedSample(raw, y * rowBytes, x, bitDepth) * 255 / top);
            }

            return new ImageData(width, height, shades, ImageEncoding.Raw, ImageColorSpace.Gray);
        }

        // 16-bit samples are reduced to 8; PDF supports 16 but nothing here needs the precision.
        var step = bitDepth == 16 ? 2 : 1;
        var hasAlpha = colorType is 4 or 6;
        var colorChannels = colorType switch { 0 or 4 => 1, _ => 3 };

        var pixels = new byte[width * height * colorChannels];
        byte[]? mask = hasAlpha ? new byte[width * height] : null;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var source = y * rowBytes + x * channels * step;
                var target = (y * width + x) * colorChannels;

                for (var c = 0; c < colorChannels; c++)
                    pixels[target + c] = raw[source + c * step];

                if (mask is not null)
                    mask[y * width + x] = raw[source + colorChannels * step];
            }
        }

        return new ImageData(width, height, pixels, ImageEncoding.Raw,
            colorChannels == 1 ? ImageColorSpace.Gray : ImageColorSpace.Rgb, mask);
    }

    /// <summary>Reads one sample from a row where several are packed into each byte.</summary>
    private static int ReadPackedSample(byte[] raw, int rowStart, int x, int bitDepth)
    {
        if (bitDepth == 8) return raw[rowStart + x];

        var perByte = 8 / bitDepth;
        var b = raw[rowStart + x / perByte];
        var shift = 8 - bitDepth * (x % perByte + 1);

        return (b >> shift) & ((1 << bitDepth) - 1);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);

        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int ReadInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
