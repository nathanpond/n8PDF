namespace n8PDF.Fonts;

/// <summary>
/// A parsed <c>cmap</c> subtable: Unicode code point to glyph index. Only the formats that
/// actually occur in the fonts Word documents reference are supported (0, 4, 6 and 12).
/// </summary>
internal sealed class CharacterMap
{
    private readonly Dictionary<int, ushort> _map;

    /// <summary>
    /// True for symbol-encoded subtables (platform 3, encoding 0), whose code points live in the
    /// 0xF000 private-use block. Wingdings and Symbol are the common cases in Word documents.
    /// </summary>
    public bool IsSymbolEncoded { get; }

    private CharacterMap(Dictionary<int, ushort> map, bool isSymbolEncoded)
    {
        _map = map;
        IsSymbolEncoded = isSymbolEncoded;
    }

    public int Count => _map.Count;

    public ushort GetGlyph(int codePoint)
    {
        if (_map.TryGetValue(codePoint, out var glyph))
            return glyph;

        // Symbol fonts map their glyphs into the private-use area; a document that asks for
        // 'J' in Wingdings really means 0xF04A.
        if (IsSymbolEncoded && codePoint is >= 0x20 and <= 0xff && _map.TryGetValue(0xf000 + codePoint, out glyph))
            return glyph;

        return 0;
    }

    /// <summary>
    /// Parses the cmap table and selects the best available subtable. Preference order is
    /// full Unicode first, then the basic multilingual plane, then symbol and Mac encodings.
    /// </summary>
    public static CharacterMap Parse(byte[] data, int tableOffset)
    {
        var reader = new BigEndianReader(data, tableOffset);
        reader.ReadUInt16(); // version
        int subtableCount = reader.ReadUInt16();

        var best = -1;
        var bestScore = -1;
        var bestIsSymbol = false;

        for (var i = 0; i < subtableCount; i++)
        {
            int platformId = reader.ReadUInt16();
            int encodingId = reader.ReadUInt16();
            var offset = (int)reader.ReadUInt32();

            var score = (platformId, encodingId) switch
            {
                (3, 10) => 5, // Windows, full Unicode
                (0, 4) => 5,  // Unicode 2.0+, full range
                (0, 6) => 5,
                (3, 1) => 4,  // Windows, BMP — by far the most common
                (0, 3) => 4,
                (0, 2) => 3,
                (0, 1) => 3,
                (0, 0) => 3,
                (3, 0) => 2,  // Windows symbol
                (1, 0) => 1,  // Mac Roman
                _ => 0
            };

            if (score <= bestScore) continue;

            bestScore = score;
            best = tableOffset + offset;
            bestIsSymbol = platformId == 3 && encodingId == 0;
        }

        if (best < 0)
            throw new FontFormatException("The font has no usable cmap subtable.");

        return new CharacterMap(ParseSubtable(data, best), bestIsSymbol);
    }

    private static Dictionary<int, ushort> ParseSubtable(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);
        int format = reader.ReadUInt16();

        return format switch
        {
            0 => ParseFormat0(ref reader),
            4 => ParseFormat4(data, offset),
            6 => ParseFormat6(ref reader),
            12 => ParseFormat12(ref reader),
            _ => throw new FontFormatException($"Unsupported cmap subtable format {format}.")
        };
    }

    /// <summary>Byte encoding table: a flat array of 256 glyph indices.</summary>
    private static Dictionary<int, ushort> ParseFormat0(ref BigEndianReader reader)
    {
        reader.ReadUInt16(); // length
        reader.ReadUInt16(); // language

        var map = new Dictionary<int, ushort>(256);
        for (var i = 0; i < 256; i++)
        {
            var glyph = reader.ReadByte();
            if (glyph != 0) map[i] = glyph;
        }

        return map;
    }

    /// <summary>
    /// Segment mapping to delta values — the workhorse format for BMP coverage. Segments are
    /// parallel arrays, and a non-zero idRangeOffset means the glyph comes from a shared array
    /// at an offset computed relative to the idRangeOffset slot itself.
    /// </summary>
    private static Dictionary<int, ushort> ParseFormat4(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset + 6);
        int segCountX2 = reader.ReadUInt16();
        var segCount = segCountX2 / 2;

        reader.Skip(6); // searchRange, entrySelector, rangeShift

        var endCodes = new ushort[segCount];
        for (var i = 0; i < segCount; i++) endCodes[i] = reader.ReadUInt16();

        reader.ReadUInt16(); // reservedPad

        var startCodes = new ushort[segCount];
        for (var i = 0; i < segCount; i++) startCodes[i] = reader.ReadUInt16();

        var idDeltas = new short[segCount];
        for (var i = 0; i < segCount; i++) idDeltas[i] = reader.ReadInt16();

        var idRangeOffsetPositions = new int[segCount];
        var idRangeOffsets = new ushort[segCount];
        for (var i = 0; i < segCount; i++)
        {
            idRangeOffsetPositions[i] = reader.Position;
            idRangeOffsets[i] = reader.ReadUInt16();
        }

        var map = new Dictionary<int, ushort>(512);
        for (var segment = 0; segment < segCount; segment++)
        {
            int start = startCodes[segment];
            int end = endCodes[segment];

            // The final segment is the required 0xFFFF terminator and carries no real mapping.
            if (start == 0xffff) continue;

            for (var code = start; code <= end && code <= 0xffff; code++)
            {
                ushort glyph;
                if (idRangeOffsets[segment] == 0)
                {
                    glyph = (ushort)((code + idDeltas[segment]) & 0xffff);
                }
                else
                {
                    var glyphPosition = idRangeOffsetPositions[segment] + idRangeOffsets[segment] +
                                        (code - start) * 2;
                    if (glyphPosition + 1 >= data.Length) continue;

                    glyph = (ushort)((data[glyphPosition] << 8) | data[glyphPosition + 1]);
                    if (glyph != 0) glyph = (ushort)((glyph + idDeltas[segment]) & 0xffff);
                }

                if (glyph != 0) map[code] = glyph;
            }
        }

        return map;
    }

    /// <summary>Trimmed table mapping: a contiguous run of code points.</summary>
    private static Dictionary<int, ushort> ParseFormat6(ref BigEndianReader reader)
    {
        reader.ReadUInt16(); // length
        reader.ReadUInt16(); // language
        int firstCode = reader.ReadUInt16();
        int entryCount = reader.ReadUInt16();

        var map = new Dictionary<int, ushort>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var glyph = reader.ReadUInt16();
            if (glyph != 0) map[firstCode + i] = glyph;
        }

        return map;
    }

    /// <summary>Segmented coverage: the format that reaches beyond the BMP.</summary>
    private static Dictionary<int, ushort> ParseFormat12(ref BigEndianReader reader)
    {
        reader.ReadUInt16(); // reserved
        reader.ReadUInt32(); // length
        reader.ReadUInt32(); // language
        var groupCount = (int)reader.ReadUInt32();

        var map = new Dictionary<int, ushort>(groupCount * 4);
        for (var i = 0; i < groupCount; i++)
        {
            var start = (int)reader.ReadUInt32();
            var end = (int)reader.ReadUInt32();
            var startGlyph = (int)reader.ReadUInt32();

            // Guard against a corrupt group claiming an absurd range.
            if (end < start || end - start > 0x10ffff) continue;

            for (var code = start; code <= end; code++)
            {
                var glyph = startGlyph + (code - start);
                if (glyph is > 0 and <= 0xffff) map[code] = (ushort)glyph;
            }
        }

        return map;
    }
}
