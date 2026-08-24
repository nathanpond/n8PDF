namespace n8PDF.Fonts.Aat;

/// <summary>
/// Apple's own kind of lookup table: a value for a glyph, in one of six shapes.
/// </summary>
/// <remarks>
/// The same table serves everywhere in these fonts — which class a glyph belongs to for a state
/// machine, what glyph it should be swapped for, how far it should be moved. Which of the six
/// shapes a font uses is a matter of how its glyphs happen to be numbered: a contiguous run is an
/// array, scattered glyphs are a sorted list to be searched, and the rest are somewhere between.
/// </remarks>
internal static class AatLookup
{
    /// <summary>The value for a glyph, or null where the table says nothing about it.</summary>
    public static int? Value(byte[] data, int offset, ushort glyph, int glyphCount)
    {
        if (offset <= 0 || offset + 2 > data.Length) return null;

        var format = Read16(data, offset);

        return format switch
        {
            0 => Simple(data, offset, glyph, glyphCount),
            2 => Segments(data, offset, glyph, single: true),
            4 => Segments(data, offset, glyph, single: false),
            6 => Single(data, offset, glyph),
            8 => Trimmed(data, offset, glyph),
            10 => Trimmed(data, offset, glyph, wide: true),
            _ => null
        };
    }

    /// <summary>An array with a value for every glyph in the font.</summary>
    private static int? Simple(byte[] data, int offset, ushort glyph, int glyphCount)
    {
        if (glyph >= glyphCount) return null;

        var at = offset + 2 + glyph * 2;

        return at + 2 <= data.Length ? Read16(data, at) : null;
    }

    /// <summary>
    /// Runs of glyphs, searched: each says the glyphs it covers and either one value for all of
    /// them or where the values for each of them begin.
    /// </summary>
    private static int? Segments(byte[] data, int offset, ushort glyph, bool single)
    {
        var unitSize = Read16(data, offset + 2);
        var units = Read16(data, offset + 4);

        var at = offset + 12;

        // A binary search would do, and the tables carry the numbers for one; walking is simpler
        // and these tables hold tens of runs rather than thousands.
        for (var i = 0; i < units; i++)
        {
            var entry = at + i * unitSize;
            if (entry + 6 > data.Length) break;

            var last = Read16(data, entry);
            var first = Read16(data, entry + 2);

            if (last == 0xFFFF && first == 0xFFFF) break;
            if (glyph < first || glyph > last) continue;

            if (single) return Read16(data, entry + 4);

            // The run says where its values start, as a distance from the beginning of the
            // lookup table rather than from anywhere nearer.
            var values = offset + Read16(data, entry + 4);
            var value = values + (glyph - first) * 2;

            return value + 2 <= data.Length ? Read16(data, value) : null;
        }

        return null;
    }

    /// <summary>A sorted list of single glyphs and their values.</summary>
    private static int? Single(byte[] data, int offset, ushort glyph)
    {
        var unitSize = Read16(data, offset + 2);
        var units = Read16(data, offset + 4);

        var at = offset + 12;

        var low = 0;
        var high = units - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var entry = at + middle * unitSize;

            if (entry + 4 > data.Length) return null;

            var found = Read16(data, entry);

            if (glyph < found) high = middle - 1;
            else if (glyph > found) low = middle + 1;
            else return Read16(data, entry + 2);
        }

        return null;
    }

    /// <summary>One run of glyphs, with a value for each.</summary>
    private static int? Trimmed(byte[] data, int offset, ushort glyph, bool wide = false)
    {
        // The wider form says how large each value is; the other's are two bytes each.
        var size = wide ? Read16(data, offset + 2) : 2;
        var at = offset + (wide ? 4 : 2);

        var first = Read16(data, at);
        var count = Read16(data, at + 2);

        if (glyph < first || glyph >= first + count) return null;

        var value = at + 4 + (glyph - first) * size;
        if (value + size > data.Length) return null;

        return size switch
        {
            1 => data[value],
            2 => Read16(data, value),
            4 => (data[value] << 24) | (data[value + 1] << 16) | (data[value + 2] << 8) | data[value + 3],
            _ => null
        };
    }

    public static ushort Read16(byte[] data, int at) =>
        at < 0 || at >= data.Length - 1 ? (ushort)0 : (ushort)((data[at] << 8) | data[at + 1]);  // (#186)

    public static uint Read32(byte[] data, int at) =>
        at < 0 || at > data.Length - 4
            ? 0u
            : ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];  // (#186)
}
