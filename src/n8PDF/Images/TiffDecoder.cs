using System.IO.Compression;

namespace n8PDF.Images;

/// <summary>
/// Reads a TIFF.
/// </summary>
/// <remarks>
/// A TIFF says almost nothing about itself in its header: two letters for which end its numbers
/// are written from, and where to find the list of tags that describes the picture. Everything
/// else — the size, how many bits a sample takes, what the samples mean, how they are packed, and
/// where in the file they are — is a tag, and a reader is a reader of tags.
///
/// What is handled here is what a picture in a document is: one image, its rows in strips, packed
/// with nothing, with LZW, with PackBits or with Deflate, in grey, colour or a palette. What is
/// not is the rest of a format that was designed to hold anything at all — tiles, separate planes,
/// the fax encodings, and JPEG inside TIFF.
/// </remarks>
internal static class TiffDecoder
{
    public static bool IsTiff(byte[] data) =>
        data.Length > 8 &&
        ((data[0] == 'I' && data[1] == 'I' && data[2] == 42 && data[3] == 0) ||
         (data[0] == 'M' && data[1] == 'M' && data[2] == 0 && data[3] == 42));

    public static ImageData Decode(
        byte[] data, long maximumPixels = ImageLimits.DefaultMaximumPixels, int nesting = 0)
    {
        if (!IsTiff(data)) throw new ImageFormatException("Not a TIFF.");

        var little = data[0] == 'I';
        var reader = new Reader(data, little);

        var tags = ReadDirectory(reader, reader.Int32(4));

        var width = (int)Value(reader, tags, 256, 0);
        var height = (int)Value(reader, tags, 257, 0);

        if (width <= 0 || height <= 0) throw new ImageFormatException("TIFF declares an empty image.");

        ImageLimits.Check(width, height, maximumPixels, "TIFF");

        var samples = (int)Value(reader, tags, 277, 1);

        // A picture has a handful of channels; an unbounded SamplesPerPixel overflows the row
        // stride negative or sizes the raw buffer in gigabytes (#29).
        if (samples is < 1 or > 16)
            throw new ImageFormatException($"TIFF declares {samples} samples a pixel, which is not a picture.");
        var bits = tags.TryGetValue(258, out var bitsTag) ? (int)Numbers(reader, bitsTag)[0] : 1;
        var compression = (int)Value(reader, tags, 259, 1);
        var photometric = (int)Value(reader, tags, 262, bits == 1 ? 0 : 1);

        // The fax encodings say nothing about colour: a set bit is black in all of them, whatever
        // a photometric tag written beside them claims.
        if (compression is 2 or 3 or 4) photometric = 0;
        var planar = (int)Value(reader, tags, 284, 1);
        var predictor = (int)Value(reader, tags, 317, 1);
        var fillOrder = (int)Value(reader, tags, 266, 1);
        var rowsPerStrip = (int)Value(reader, tags, 278, height);

        // What a fax strip says about how it was written: whether its lines are written against
        // the ones above them, and whether each begins on a byte.
        var faxOptions = (int)Value(reader, tags, compression == 4 ? 293 : 292, 0);

        if (bits is not (1 or 4 or 8 or 16))
            throw new ImageFormatException($"TIFF has {bits} bits a sample, which is not handled.");

        // A TIFF keeps its channels together or apart. Apart, each is an image of one sample in
        // its own right, and they are laid over one another at the end.
        var planes = planar == 2 ? samples : 1;
        var perPlane = planar == 2 ? 1 : samples;

        if (planar == 2 && bits < 8)
            throw new ImageFormatException("TIFF keeps its channels apart and packs them, which is not handled.");

        var tiled = tags.ContainsKey(322) && tags.ContainsKey(324);

        // A TIFF may hold a JPEG rather than pixels, and a PDF carries JPEG as it stands: so what
        // is wanted is not to decode it but to put the file back together.
        if (compression is 6 or 7)
            return Jpeg(
                data, reader, tags, width, height, samples, tiled, rowsPerStrip, maximumPixels, nesting);

        var rowBytes = (width * perPlane * bits + 7) / 8;
        var gathered = new byte[planes][];

        for (var plane = 0; plane < planes; plane++)
        {
            gathered[plane] = tiled
                ? Tiles(data, reader, tags, plane, width, height, perPlane, bits, rowBytes, Unpacker)
                : Strips(data, reader, tags, plane, width, height, rowsPerStrip, perPlane, bits, rowBytes,
                    Unpacker);
        }

        var raw = planes == 1 ? gathered[0] : Interleave(gathered, width, height, samples, bits);

        // Laying the planes over one another makes the rows as long as they would have been had
        // the channels been kept together in the first place.
        if (planes > 1) rowBytes = width * samples * bits / 8;

        // How a strip or a tile is unpacked, which is the same wherever it came from.
        byte[] Unpacker(int offset, int length, int rows, int rowLength)
        {
            var unpacked = compression is 2 or 3 or 4
                ? CcittDecoder.Decode(
                    data, offset, length, rowLength * 8 / Math.Max(1, perPlane * bits), rows,
                    twoDimensional: compression == 4 || (faxOptions & 1) != 0,
                    pureTwoDimensional: compression == 4,
                    byteAligned: compression == 2 || (faxOptions & 4) != 0)
                : Unpack(data, offset, length, compression, rowLength * rows);

            if (fillOrder == 2) Reverse(unpacked);

            if (predictor == 2)
                Undo(unpacked, rowLength * 8 / Math.Max(1, perPlane * bits), perPlane, bits, rowLength, rows);

            return unpacked;
        }

        var palette = photometric == 3 && tags.TryGetValue(320, out var mapTag)
            ? Numbers(reader, mapTag)
            : null;

        // A picture of separated inks is the one a printing press wants: not the light a screen
        // adds up to a colour but the ink a press lays down to take light away, four of them, and
        // the fourth is black because three inks together make a muddy brown rather than black.
        if (photometric == 5)
        {
            if (samples < 4) throw new ImageFormatException("TIFF of separated inks has too few channels.");

            // Which inks, where a file says: four is cyan, magenta, yellow and black, and anything
            // else is a press's own inks, which a PDF has no way to be handed as this.
            if (Value(reader, tags, 332, 1) != 1)
                throw new ImageFormatException("TIFF holds inks of its own, which are not handled.");
        }

        // An extra sample beyond the colours is transparency, which is the only kind a TIFF has.
        var extras = tags.TryGetValue(338, out var extraTag) ? Numbers(reader, extraTag) : [];
        var colourSamples = Channels(photometric);
        var hasAlpha = samples > colourSamples && extras.Count > 0 && extras[0] != 0;

        return Expand(raw, width, height, rowBytes, bits, samples, photometric, palette, hasAlpha, little);
    }

    // ----- the tags -----

    private readonly record struct Tag(int Type, int Count, int Offset, byte[] Inline);

    private static Dictionary<int, Tag> ReadDirectory(Reader reader, int at)
    {
        var tags = new Dictionary<int, Tag>();
        if (at <= 0 || at + 2 > reader.Length) return tags;

        var count = reader.UInt16(at);
        at += 2;

        for (var i = 0; i < count && at + 12 <= reader.Length; i++, at += 12)
        {
            var id = reader.UInt16(at);
            var type = reader.UInt16(at + 2);
            var values = reader.Int32(at + 4);

            tags[id] = new Tag(type, values, reader.Int32(at + 8), reader.Slice(at + 8, 4));
        }

        return tags;
    }

    /// <summary>The numbers a tag holds, whether they fit in the entry itself or lie elsewhere.</summary>
    private static List<long> Numbers(Reader reader, Tag tag)
    {
        var size = tag.Type switch { 1 or 2 or 6 or 7 => 1, 3 or 8 => 2, _ => 4 };
        var total = (long)size * tag.Count;

        // The count is a raw 32-bit field. The values must lie within the file — inline in the
        // four-byte entry, or at an offset the file's own length bounds — so the number read is
        // bounded by that rather than pre-sizing a list from the word of the file (#27).
        var count = total <= 4
            ? Math.Min(Math.Max(0, tag.Count), 4 / size)
            : Math.Min(Math.Max(0, tag.Count), Math.Max(0, (reader.Length - tag.Offset) / size));

        var values = new List<long>(count);

        for (var i = 0; i < count; i++)
        {
            var at = total <= 4 ? i * size : tag.Offset + i * size;
            var source = total <= 4 ? tag.Inline : null;

            values.Add(size switch
            {
                1 => source is not null ? source[at] : reader.Byte(at),
                2 => source is not null ? reader.UInt16(source, at) : reader.UInt16(at),
                _ => source is not null ? reader.Int32(source, at) : reader.Int32(at)
            });
        }

        return values;
    }

    /// <summary>
    /// The one number a tag holds. A number smaller than the four bytes the entry keeps for it is
    /// written into the front of them — which is the high half of the field where the file writes
    /// its numbers big end first, so this has to be read the file's own way round rather than
    /// taken as it lies.
    /// </summary>
    private static long Value(Reader reader, Dictionary<int, Tag> tags, int id, long fallback) =>
        tags.TryGetValue(id, out var tag) && tag.Count > 0
            ? tag.Type switch
            {
                // A single BYTE/SBYTE sits in the first of the four inline bytes whichever way the
                // file runs; taken as the whole field it reads value<<24 in a big-endian file and
                // a count of 3 becomes fifty million (#28).
                1 or 6 when tag.Inline.Length > 0 => tag.Inline[0],
                3 or 8 => reader.UInt16(tag.Inline, 0),
                _ => tag.Offset
            }
            : fallback;

    /// <summary>
    /// A TIFF whose pixels are a JPEG.
    /// </summary>
    /// <remarks>
    /// The picture is not unpacked here at all. A PDF carries a JPEG as the file it already is —
    /// decoding one only to encode it again would cost quality for nothing — so what this does is
    /// put the file back together, which a TIFF has taken apart in one of two ways.
    ///
    /// The older way keeps a whole JPEG in a tag of its own and the tags around it describe it.
    /// The newer way keeps the tables that every scan of the picture shares in one tag and the
    /// scan itself in the strip, so that a picture in many strips need not repeat them: the file
    /// is the one without its end, then the other without its beginning.
    ///
    /// A picture in more than one strip is the one case where a JPEG here is decoded rather than
    /// handed on: the strips are separate files, and nothing but their pixels is common to them.
    /// </remarks>
    private static ImageData Jpeg(
        byte[] data, Reader reader, Dictionary<int, Tag> tags, int width, int height, int samples,
        bool tiled, int rowsPerStrip, long maximumPixels, int nesting)
    {
        // The older way: the whole file, in a tag.
        var wholeAt = (int)Value(reader, tags, 513, 0);
        var wholeLength = (int)Value(reader, tags, 514, 0);

        if (wholeAt > 0 && wholeLength > 0 && wholeAt + wholeLength <= data.Length)
            return Whole(data[wholeAt..(wholeAt + wholeLength)], maximumPixels, nesting);

        var offsets = tags.TryGetValue(tiled ? 324 : 273, out var offsetTag) ? Numbers(reader, offsetTag) : [];
        var counts = tags.TryGetValue(tiled ? 325 : 279, out var countTag) ? Numbers(reader, countTag) : [];

        if (offsets.Count == 0) throw new ImageFormatException("TIFF says where none of its pixels are.");

        // A picture in more than one piece has to be decoded to be joined: the pieces are
        // separate files, and nothing but pixels is common to them.
        if (offsets.Count > 1 || (!tiled && rowsPerStrip < height))
        {
            return Join(
                data, reader, tags, offsets, counts, width, height, tiled, rowsPerStrip, Rebuild);
        }

        var offset = (int)offsets[0];
        var length = Math.Min(
            counts.Count > 0 ? (int)counts[0] : data.Length - offset, Math.Max(0, data.Length - offset));

        if (offset <= 0 || length <= 0) throw new ImageFormatException("TIFF holds an empty JPEG.");

        return Whole(Rebuild(data[offset..(offset + length)]), maximumPixels, nesting);

        // A JPEG whole, which a PDF carries as the file it already is.
        static ImageData Whole(byte[] jpeg, long maximumPixels, int nesting)
        {
            // Counted, because what a strip holds is decided by looking at it: the check below
            // that it really was a JPEG runs only once this has returned, which is too late to
            // stop a TIFF holding a TIFF. See ImageLimits.MaximumNesting.
            var image = ImageReader.TryRead(jpeg, maximumPixels, nesting + 1);

            if (image is null || image.Encoding != ImageEncoding.Jpeg)
                throw new ImageFormatException("TIFF holds something that is not a JPEG after all.");

            return image;
        }

        // A strip is not a whole file where the tables every strip shares are kept apart from it:
        // the file is those without their end, then the strip without its beginning.
        byte[] Rebuild(byte[] scan)
        {
            if (!tags.TryGetValue(347, out var tablesTag) || tablesTag.Count <= 2) return scan;

            var tables = Bytes(data, reader, tablesTag);

            return tables.Length > 2 && EndsWithMarker(tables, 0xd9) &&
                   scan.Length > 2 && StartsWithMarker(scan, 0xd8)
                ? [.. tables[..^2], .. scan[2..]]
                : scan;
        }
    }

    /// <summary>
    /// Joins a picture written as several JPEGs, one to a strip or to a tile.
    /// </summary>
    /// <remarks>
    /// This is the one place a JPEG is decoded rather than handed on as it stands. There is no
    /// choice about it: the pieces are separate files with nothing in common but the picture they
    /// are parts of, and a PDF has no way to be given several of them as one image.
    /// </remarks>
    private static ImageData Join(
        byte[] data, Reader reader, Dictionary<int, Tag> tags, List<long> offsets, List<long> counts,
        int width, int height, bool tiled, int rowsPerStrip, Func<byte[], byte[]> rebuild)
    {
        var tileWidth = tiled ? (int)Value(reader, tags, 322, 0) : width;
        var tileHeight = tiled ? (int)Value(reader, tags, 323, 0) : rowsPerStrip;

        if (tileWidth <= 0 || tileHeight <= 0)
            throw new ImageFormatException("TIFF holds a JPEG in pieces of no size.");

        var across = tiled ? (width + tileWidth - 1) / tileWidth : 1;

        byte[]? pixels = null;
        var components = 3;
        var colour = ImageColorSpace.Rgb;

        for (var piece = 0; piece < offsets.Count; piece++)
        {
            var offset = (int)offsets[piece];
            if (offset < 0 || offset >= data.Length) continue;

            var length = Math.Min(
                piece < counts.Count ? (int)counts[piece] : data.Length - offset, data.Length - offset);

            if (length <= 0) continue;

            var part = JpegDecoder.Decode(rebuild(data[offset..(offset + length)]));

            if (pixels is null)
            {
                components = part.ComponentCount;
                colour = part.ColorSpace;
                pixels = new byte[width * height * components];
            }

            if (part.ComponentCount != components)
                throw new ImageFormatException("The pieces of the TIFF's JPEG are not alike.");

            var left = tiled ? piece % across * tileWidth : 0;
            var top = tiled ? piece / across * tileHeight : piece * rowsPerStrip;

            for (var y = 0; y < part.Height && top + y < height; y++)
            {
                var columns = Math.Min(part.Width, width - left);
                if (columns <= 0) break;

                Array.Copy(
                    part.Data, y * part.Width * components,
                    pixels, ((top + y) * width + left) * components,
                    columns * components);
            }
        }

        if (pixels is null) throw new ImageFormatException("TIFF holds a JPEG this could not read.");

        return new ImageData(width, height, pixels, ImageEncoding.Raw, colour);
    }

    private static bool StartsWithMarker(byte[] data, byte marker) =>
        data.Length > 1 && data[0] == 0xff && data[1] == marker;

    private static bool EndsWithMarker(byte[] data, byte marker) =>
        data.Length > 1 && data[^2] == 0xff && data[^1] == marker;

    /// <summary>The bytes a tag holds, which lie elsewhere in the file where there are many.</summary>
    private static byte[] Bytes(byte[] data, Reader reader, Tag tag)
    {
        if (tag.Count <= 4) return tag.Inline[..Math.Min(tag.Count, 4)];

        var at = tag.Offset;
        if (at < 0 || at >= data.Length) return [];

        return data[at..Math.Min(data.Length, at + tag.Count)];
    }

    /// <summary>
    /// Gathers one plane written in strips: runs of whole rows, each packed on its own.
    /// </summary>
    /// <remarks>
    /// Where the channels are kept apart, every plane has a full set of strips of its own and they
    /// follow one another, so a plane's strips begin where the planes before it left off.
    /// </remarks>
    private static byte[] Strips(
        byte[] data, Reader reader, Dictionary<int, Tag> tags, int plane, int width, int height,
        int rowsPerStrip, int perPlane, int bits, int rowBytes, Func<int, int, int, int, byte[]> unpack)
    {
        var offsets = tags.TryGetValue(273, out var offsetTag) ? Numbers(reader, offsetTag) : [];
        var counts = tags.TryGetValue(279, out var countTag) ? Numbers(reader, countTag) : [];

        if (offsets.Count == 0) throw new ImageFormatException("TIFF says where none of its pixels are.");

        var raw = new byte[rowBytes * height];
        var perPlaneStrips = Math.Max(1, (height + rowsPerStrip - 1) / rowsPerStrip);
        var written = 0;

        for (var strip = 0; strip < perPlaneStrips && written < raw.Length; strip++)
        {
            var index = plane * perPlaneStrips + strip;
            if (index >= offsets.Count) break;

            var offset = (int)offsets[index];
            if (offset < 0 || offset >= data.Length) break;

            var length = Math.Min(
                index < counts.Count ? (int)counts[index] : data.Length - offset, data.Length - offset);

            var rows = Math.Min(rowsPerStrip, height - strip * rowsPerStrip);
            if (rows <= 0) break;

            var unpacked = unpack(offset, length, rows, rowBytes);

            var take = Math.Min(unpacked.Length, raw.Length - written);
            Array.Copy(unpacked, 0, raw, written, take);
            written += take;
        }

        return raw;
    }

    /// <summary>
    /// Gathers one plane written in tiles: rectangles rather than rows, in reading order.
    /// </summary>
    /// <remarks>
    /// Every tile is the full size the tags declare however little of the picture it covers, so a
    /// tile at the right or the foot carries padding that is not part of the image and has to be
    /// left behind rather than copied.
    /// </remarks>
    private static byte[] Tiles(
        byte[] data, Reader reader, Dictionary<int, Tag> tags, int plane, int width, int height,
        int perPlane, int bits, int rowBytes, Func<int, int, int, int, byte[]> unpack)
    {
        var tileWidth = (int)Value(reader, tags, 322, 0);
        var tileHeight = (int)Value(reader, tags, 323, 0);

        if (tileWidth <= 0 || tileHeight <= 0)
            throw new ImageFormatException("TIFF is written in tiles of no size.");

        var offsets = tags.TryGetValue(324, out var offsetTag) ? Numbers(reader, offsetTag) : [];
        var counts = tags.TryGetValue(325, out var countTag) ? Numbers(reader, countTag) : [];

        if (offsets.Count == 0) throw new ImageFormatException("TIFF says where none of its tiles are.");

        var across = (width + tileWidth - 1) / tileWidth;
        var down = (height + tileHeight - 1) / tileHeight;
        var tileRowBytes = (tileWidth * perPlane * bits + 7) / 8;

        var raw = new byte[rowBytes * height];

        for (var row = 0; row < down; row++)
        for (var column = 0; column < across; column++)
        {
            var index = plane * across * down + row * across + column;
            if (index >= offsets.Count) continue;

            var offset = (int)offsets[index];
            if (offset < 0 || offset >= data.Length) continue;

            var length = Math.Min(
                index < counts.Count ? (int)counts[index] : data.Length - offset, data.Length - offset);

            var unpacked = unpack(offset, length, tileHeight, tileRowBytes);

            // Only the part of the tile that is inside the picture.
            var rows = Math.Min(tileHeight, height - row * tileHeight);
            var columns = Math.Min(tileWidth, width - column * tileWidth);

            for (var y = 0; y < rows; y++)
            {
                var from = y * tileRowBytes;
                var to = (row * tileHeight + y) * rowBytes;

                if (bits >= 8)
                {
                    var bytes = columns * perPlane * bits / 8;
                    var at = column * tileWidth * perPlane * bits / 8;

                    if (from + bytes <= unpacked.Length && to + at + bytes <= raw.Length)
                        Array.Copy(unpacked, from, raw, to + at, bytes);

                    continue;
                }

                // Packed samples do not begin on a byte where a tile does not, so they go across
                // one at a time.
                for (var x = 0; x < columns * perPlane; x++)
                {
                    var value = Sample(unpacked, from, x, bits);
                    Put(raw, to, column * tileWidth * perPlane + x, bits, value);
                }
            }
        }

        return raw;
    }

    /// <summary>Writes one packed sample into a row.</summary>
    private static void Put(byte[] raw, int rowStart, int index, int bits, int value)
    {
        var perByte = 8 / bits;
        var at = rowStart + index / perByte;
        if (at >= raw.Length) return;

        var shift = 8 - bits * (index % perByte + 1);
        var mask = ((1 << bits) - 1) << shift;

        raw[at] = (byte)((raw[at] & ~mask) | ((value << shift) & mask));
    }

    /// <summary>
    /// Lays the planes of an image over one another, so that what was one channel at a time
    /// becomes one pixel at a time.
    /// </summary>
    private static byte[] Interleave(byte[][] planes, int width, int height, int samples, int bits)
    {
        var size = bits / 8;
        var raw = new byte[width * height * samples * size];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        for (var c = 0; c < samples; c++)
        {
            var from = (y * width + x) * size;
            var to = ((y * width + x) * samples + c) * size;

            if (c >= planes.Length || from + size > planes[c].Length) continue;

            Array.Copy(planes[c], from, raw, to, size);
        }

        return raw;
    }

    // ----- the pixels -----

    private static byte[] Unpack(byte[] data, int offset, int length, int compression, int expected) =>
        compression switch
        {
            1 => data[offset..(offset + length)],
            5 => Lzw(data, offset, length, expected),
            32773 => PackBits(data, offset, length, expected),
            8 or 32946 => Inflate(data, offset, length, expected),
            _ => throw new ImageFormatException(
                $"TIFF is packed with method {compression}, which is not handled.")
        };

    private static byte[] Inflate(byte[] data, int offset, int length, int expected)
    {
        using var input = new MemoryStream(data, offset, length);
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();

        // A strip decompresses to the rows it covers, no more; reading it a block at a time and
        // stopping past that bound is what refuses a small Deflate strip that inflates to
        // gigabytes (#30). A little slack over the expected size absorbs a final partial block.
        var cap = (long)Math.Max(0, expected) + 65536;
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > cap)
                throw new ImageFormatException("TIFF strip decompresses past the size its rows allow.");

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>
    /// PackBits: a count of nought to 127 introduces that many literal bytes plus one, and a count
    /// read as a negative number introduces one byte to be repeated.
    /// </summary>
    private static byte[] PackBits(byte[] data, int offset, int length, int expected)
    {
        var output = new List<byte>(expected);
        var at = offset;
        var end = offset + length;

        while (at < end && output.Count < expected)
        {
            var control = (sbyte)data[at++];

            if (control >= 0)
            {
                for (var i = 0; i <= control && at < end; i++) output.Add(data[at++]);
                continue;
            }

            if (control == -128 || at >= end) continue;

            var value = data[at++];
            for (var i = 0; i < 1 - control; i++) output.Add(value);
        }

        return [.. output];
    }

    /// <summary>
    /// TIFF's LZW, which is the same idea as a GIF's and written the other way round: the codes
    /// are packed high bit first, and the width grows one code sooner.
    /// </summary>
    private static byte[] Lzw(byte[] data, int offset, int length, int expected)
    {
        const int clear = 256;
        const int end = 257;

        var table = new List<byte[]>(4096);

        void Reset()
        {
            table.Clear();
            for (var i = 0; i < 256; i++) table.Add([(byte)i]);

            table.Add([]);
            table.Add([]);
        }

        Reset();

        var output = new List<byte>(expected);
        var codeSize = 9;
        byte[]? previous = null;

        var bitAt = 0;
        var bits = length * 8;

        while (bitAt + codeSize <= bits && output.Count < expected)
        {
            var code = 0;
            for (var i = 0; i < codeSize; i++, bitAt++)
            {
                var bit = (data[offset + bitAt / 8] >> (7 - bitAt % 8)) & 1;
                code = (code << 1) | bit;
            }

            if (code == clear)
            {
                Reset();
                codeSize = 9;
                previous = null;
                continue;
            }

            if (code == end) break;

            byte[] word;

            if (code < table.Count)
            {
                word = table[code];
                if (previous is not null) table.Add([.. previous, word[0]]);
            }
            else
            {
                if (previous is null) break;

                word = [.. previous, previous[0]];
                table.Add(word);
            }

            output.AddRange(word);
            previous = word;

            // One sooner than a GIF's: the width grows before the last code of it is used.
            codeSize = table.Count switch
            {
                >= 4095 => 12,
                >= 2047 => Math.Max(codeSize, 12),
                _ => table.Count >= 1023 ? 11 : table.Count >= 511 ? 10 : 9
            };

            if (table.Count is >= 2047 and < 4095) codeSize = 12;
        }

        return [.. output];
    }

    /// <summary>
    /// Undoes horizontal differencing, where each sample was written as the difference from the
    /// one before it along the row rather than as itself.
    /// </summary>
    private static void Undo(byte[] rows, int width, int samples, int bits, int rowBytes, int height)
    {
        if (bits != 8) return;

        for (var y = 0; y < height; y++)
        {
            var start = y * rowBytes;

            for (var x = samples; x < width * samples; x++)
            {
                if (start + x >= rows.Length) break;

                rows[start + x] = (byte)(rows[start + x] + rows[start + x - samples]);
            }
        }
    }

    private static void Reverse(byte[] data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            var value = data[i];
            var flipped = 0;

            for (var bit = 0; bit < 8; bit++) flipped = (flipped << 1) | ((value >> bit) & 1);

            data[i] = (byte)flipped;
        }
    }

    /// <summary>How many channels a picture of the given kind holds, beyond any transparency.</summary>
    private static int Channels(int photometric) => photometric switch
    {
        2 => 3,
        5 => 4,
        _ => 1
    };

    /// <summary>
    /// Turns the samples into what a PDF carries.
    /// </summary>
    /// <remarks>
    /// Sixteen-bit samples are kept at sixteen, written the way a PDF writes them, which is the
    /// bigger half of each first however the file itself had them. A picture through a palette is
    /// the one thing reduced to eight: the index is what its depth describes, and the colours it
    /// looks up are a table of at most a few hundred entries whose lower halves no document has
    /// ever needed.
    /// </remarks>
    private static ImageData Expand(
        byte[] raw, int width, int height, int rowBytes, int bits, int samples, int photometric,
        List<long>? palette, bool hasAlpha, bool little)
    {
        var colour = Channels(photometric);
        var components = photometric == 3 ? 3 : colour;

        // A palette is a lookup rather than a sample, so it comes out at a byte a channel however
        // deep the index into it was.
        var deep = bits == 16 && photometric != 3;
        var size = deep ? 2 : 1;

        var pixels = new byte[width * height * components * size];
        byte[]? alpha = hasAlpha ? new byte[width * height * size] : null;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var target = (y * width + x) * components * size;

                if (photometric == 3)
                {
                    // A palette holds its three channels one after another, each a whole
                    // sixteen-bit number however few of them are used.
                    var index = Sample(raw, y * rowBytes, x * samples, bits, little);
                    var count = (palette?.Count ?? 0) / 3;

                    for (var c = 0; c < 3; c++)
                    {
                        var at = c * count + index;
                        pixels[target + c] = palette is not null && at < palette.Count
                            ? (byte)(palette[at] >> 8)
                            : (byte)0;
                    }
                }
                else
                {
                    for (var c = 0; c < colour; c++)
                    {
                        var value = Sample(raw, y * rowBytes, x * samples + c, bits, little);

                        // Where nought is white the samples run the other way round.
                        if (photometric == 0) value = (deep ? 0xffff : (1 << bits) - 1) - value;

                        Write(pixels, target + c * size, value, deep, bits);
                    }
                }

                if (alpha is not null)
                {
                    var value = Sample(raw, y * rowBytes, x * samples + colour, bits, little);

                    Write(alpha, (y * width + x) * size, value, deep, bits);
                }
            }
        }

        return new ImageData(width, height, pixels, ImageEncoding.Raw, components switch
            {
                1 => ImageColorSpace.Gray,
                4 => ImageColorSpace.Cmyk,
                _ => ImageColorSpace.Rgb
            }, alpha)
        {
            BitsPerComponent = deep ? 16 : 8
        };
    }

    /// <summary>
    /// Writes one sample out: as it stands where the picture is deep, and spread over a whole byte
    /// where it is not — one bit of grey is black and white rather than black and nearly black.
    /// </summary>
    private static void Write(byte[] target, int at, int value, bool deep, int bits)
    {
        if (deep)
        {
            target[at] = (byte)(value >> 8);
            target[at + 1] = (byte)value;

            return;
        }

        target[at] = Scale(value, bits);
    }

    private static byte Scale(int value, int bits) => bits switch
    {
        16 => (byte)(value >> 8),
        8 => (byte)value,
        4 => (byte)(value * 255 / 15),
        _ => (byte)(value == 0 ? 0 : 255)
    };

    /// <summary>
    /// One sample of a row. A sample of two bytes is written the same way round as every other
    /// number in the file, so which way that is has to be known to read one: a file written little
    /// end first and read big end first gives a picture of noise, or — where only the top half of
    /// each sample was ever used — a picture made of the wrong halves.
    /// </summary>
    private static int Sample(byte[] raw, int rowStart, int index, int bits, bool little = false)
    {
        switch (bits)
        {
            case 8:
            {
                var at = rowStart + index;
                return at < raw.Length ? raw[at] : 0;
            }

            case 16:
            {
                var at = rowStart + index * 2;
                if (at + 1 >= raw.Length) return 0;

                return little ? raw[at] | (raw[at + 1] << 8) : (raw[at] << 8) | raw[at + 1];
            }

            default:
            {
                var perByte = 8 / bits;
                var at = rowStart + index / perByte;
                if (at >= raw.Length) return 0;

                var shift = 8 - bits * (index % perByte + 1);

                return (raw[at] >> shift) & ((1 << bits) - 1);
            }
        }
    }

    /// <summary>Reads numbers from whichever end the file writes them from.</summary>
    private sealed class Reader(byte[] data, bool little)
    {
        public int Length => data.Length;

        public byte Byte(int at) => at < data.Length ? data[at] : (byte)0;

        public int UInt16(int at) => UInt16(data, at);

        public int UInt16(byte[] source, int at)
        {
            if (at + 1 >= source.Length) return 0;

            return little
                ? source[at] | (source[at + 1] << 8)
                : (source[at] << 8) | source[at + 1];
        }

        public int Int32(int at) => Int32(data, at);

        public int Int32(byte[] source, int at)
        {
            if (at + 3 >= source.Length) return 0;

            return little
                ? source[at] | (source[at + 1] << 8) | (source[at + 2] << 16) | (source[at + 3] << 24)
                : (source[at] << 24) | (source[at + 1] << 16) | (source[at + 2] << 8) | source[at + 3];
        }

        public byte[] Slice(int at, int length)
        {
            var slice = new byte[length];
            var available = Math.Max(0, Math.Min(length, data.Length - at));

            Array.Copy(data, at, slice, 0, available);

            return slice;
        }
    }
}
