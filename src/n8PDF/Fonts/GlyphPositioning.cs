namespace n8PDF.Fonts;

/// <summary>
/// The pair kerning of an OpenType font's <c>GPOS</c> table.
/// </summary>
/// <remarks>
/// The legacy <c>kern</c> table is the older way of saying the same thing, and fonts shipped this
/// century increasingly carry only this one — Calibri has no <c>kern</c> table at all — so a
/// converter that read only the old one would silently stop kerning on most modern text.
///
/// Only what kerning needs is read: the <c>kern</c> feature's lookups, pair positioning in both of
/// its formats, and the horizontal advance of the first glyph of a pair. Everything else GPOS can
/// do — mark attachment, cursive joining, the rest — is skipped, and a table that cannot be read
/// costs the font its kerning rather than failing the conversion.
/// </remarks>
internal sealed class GlyphPositioning
{
    /// <summary>Explicit pairs, from the format that lists them one by one.</summary>
    private readonly Dictionary<int, short> _pairs = [];

    /// <summary>Class-based subtables, which is how most of a font's kerning is expressed.</summary>
    private readonly List<ClassPairs> _classPairs = [];

    private GlyphPositioning()
    {
    }

    public bool IsEmpty => _pairs.Count == 0 && _classPairs.Count == 0;

    /// <summary>
    /// The adjustment to the left glyph's advance, in design units, or zero when the pair is not
    /// kerned.
    /// </summary>
    public short GetAdjustment(ushort left, ushort right)
    {
        if (_pairs.TryGetValue((left << 16) | right, out var explicitValue)) return explicitValue;

        foreach (var table in _classPairs)
        {
            var value = table.Lookup(left, right);
            if (value != 0) return value;
        }

        return 0;
    }

    /// <summary>
    /// Reads the kerning out of a font's GPOS table, or returns null when it has none to give.
    /// </summary>
    public static GlyphPositioning? Read(byte[] data, int offset, int length)
    {
        try
        {
            var reader = new BigEndianReader(data, offset);

            reader.Skip(4); // major and minor version
            var scriptListOffset = reader.ReadUInt16();
            var featureListOffset = reader.ReadUInt16();
            var lookupListOffset = reader.ReadUInt16();

            _ = scriptListOffset;

            var lookups = KerningLookups(data, offset + featureListOffset, offset + lookupListOffset);
            if (lookups.Count == 0) return null;

            var result = new GlyphPositioning();
            foreach (var lookup in lookups) result.ReadLookup(data, lookup, offset, length);

            return result.IsEmpty ? null : result;
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// The offsets of every lookup the <c>kern</c> feature refers to.
    /// </summary>
    /// <remarks>
    /// Every feature with that tag counts, whichever script or language declared it. Picking the
    /// one for the text's own script would need the script to be known, and for kerning the
    /// difference between the tables is vanishingly rare — while missing the feature altogether
    /// because the font filed it under a language rather than the default would not be.
    /// </remarks>
    private static List<int> KerningLookups(byte[] data, int featureList, int lookupList)
    {
        var indices = new HashSet<ushort>();

        var features = new BigEndianReader(data, featureList);
        int featureCount = features.ReadUInt16();

        for (var i = 0; i < featureCount; i++)
        {
            var tag = features.ReadTag();
            int featureOffset = features.ReadUInt16();
            if (tag != "kern") continue;

            var feature = new BigEndianReader(data, featureList + featureOffset);
            feature.Skip(2); // featureParams
            int lookupCount = feature.ReadUInt16();

            for (var j = 0; j < lookupCount; j++) indices.Add(feature.ReadUInt16());
        }

        if (indices.Count == 0) return [];

        var lookups = new BigEndianReader(data, lookupList);
        int total = lookups.ReadUInt16();

        var offsets = new List<int>();
        for (var i = 0; i < total; i++)
        {
            int lookupOffset = lookups.ReadUInt16();
            if (indices.Contains((ushort)i)) offsets.Add(lookupList + lookupOffset);
        }

        return offsets;
    }

    private void ReadLookup(byte[] data, int offset, int tableStart, int tableLength)
    {
        var reader = new BigEndianReader(data, offset);

        int type = reader.ReadUInt16();
        reader.Skip(2); // lookupFlag
        int subtableCount = reader.ReadUInt16();

        var subtables = new List<int>(subtableCount);
        for (var i = 0; i < subtableCount; i++) subtables.Add(offset + reader.ReadUInt16());

        foreach (var subtable in subtables)
        {
            switch (type)
            {
                case 2:
                    ReadPairPositioning(data, subtable);
                    break;

                // An extension subtable is a type 2 subtable behind a 32-bit offset, which is how
                // a font whose tables outgrew 16 bits reaches them.
                case 9:
                {
                    var extension = new BigEndianReader(data, subtable);
                    extension.Skip(2); // posFormat
                    int extensionType = extension.ReadUInt16();
                    var target = subtable + (int)extension.ReadUInt32();

                    if (extensionType == 2 && target >= tableStart && target < tableStart + tableLength)
                        ReadPairPositioning(data, target);

                    break;
                }
            }
        }
    }

    private void ReadPairPositioning(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        int coverageOffset = reader.ReadUInt16();
        int valueFormat1 = reader.ReadUInt16();
        int valueFormat2 = reader.ReadUInt16();

        var coverage = ReadCoverage(data, offset + coverageOffset);

        if (format == 1) ReadExplicitPairs(data, offset, reader, coverage, valueFormat1, valueFormat2);
        else if (format == 2) ReadClassPairs(data, offset, reader, coverage, valueFormat1, valueFormat2);
    }

    private void ReadExplicitPairs(
        byte[] data, int offset, BigEndianReader reader,
        List<ushort> coverage, int valueFormat1, int valueFormat2)
    {
        int pairSetCount = reader.ReadUInt16();

        var pairSets = new List<int>(pairSetCount);
        for (var i = 0; i < pairSetCount; i++) pairSets.Add(offset + reader.ReadUInt16());

        var size1 = ValueRecordSize(valueFormat1);
        var size2 = ValueRecordSize(valueFormat2);

        for (var i = 0; i < pairSets.Count && i < coverage.Count; i++)
        {
            var left = coverage[i];
            var set = new BigEndianReader(data, pairSets[i]);
            int pairCount = set.ReadUInt16();

            for (var j = 0; j < pairCount; j++)
            {
                var right = set.ReadUInt16();
                var start = set.Position;

                var adjustment = ReadAdvance(set, valueFormat1);
                set.Position = start + size1 + size2;

                if (adjustment != 0) _pairs[(left << 16) | right] = adjustment;
            }
        }
    }

    private void ReadClassPairs(
        byte[] data, int offset, BigEndianReader reader,
        List<ushort> coverage, int valueFormat1, int valueFormat2)
    {
        int classDef1 = reader.ReadUInt16();
        int classDef2 = reader.ReadUInt16();
        int class1Count = reader.ReadUInt16();
        int class2Count = reader.ReadUInt16();

        if (class1Count == 0 || class2Count == 0) return;

        var size1 = ValueRecordSize(valueFormat1);
        var size2 = ValueRecordSize(valueFormat2);

        var values = new short[class1Count * class2Count];
        var any = false;

        for (var i = 0; i < class1Count; i++)
        {
            for (var j = 0; j < class2Count; j++)
            {
                var start = reader.Position;
                var adjustment = ReadAdvance(reader, valueFormat1);
                reader.Position = start + size1 + size2;

                values[i * class2Count + j] = adjustment;
                any |= adjustment != 0;
            }
        }

        if (!any) return;

        _classPairs.Add(new ClassPairs(
            [.. coverage],
            ReadClassDefinition(data, offset + classDef1),
            ReadClassDefinition(data, offset + classDef2),
            values,
            class2Count));
    }

    /// <summary>
    /// Reads the horizontal advance out of a value record, leaving the reader where it stopped.
    /// </summary>
    /// <remarks>
    /// The fields present are whichever bits the value format sets, in a fixed order, so the ones
    /// before the advance have to be stepped over and the ones after it can be ignored.
    /// </remarks>
    private static short ReadAdvance(BigEndianReader reader, int valueFormat)
    {
        if ((valueFormat & 0x0004) == 0) return 0;

        if ((valueFormat & 0x0001) != 0) reader.Skip(2); // x placement
        if ((valueFormat & 0x0002) != 0) reader.Skip(2); // y placement

        return reader.ReadInt16();
    }

    private static int ValueRecordSize(int valueFormat)
    {
        var size = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            if ((valueFormat & (1 << bit)) != 0) size += 2;
        }

        return size;
    }

    /// <summary>The glyphs a subtable applies to, in coverage-index order.</summary>
    private static List<ushort> ReadCoverage(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);
        var glyphs = new List<ushort>();

        switch (reader.ReadUInt16())
        {
            case 1:
            {
                int count = reader.ReadUInt16();
                for (var i = 0; i < count; i++) glyphs.Add(reader.ReadUInt16());
                break;
            }

            case 2:
            {
                int rangeCount = reader.ReadUInt16();
                for (var i = 0; i < rangeCount; i++)
                {
                    int first = reader.ReadUInt16();
                    int last = reader.ReadUInt16();
                    reader.Skip(2); // startCoverageIndex, which is just the running count

                    for (var glyph = first; glyph <= last && glyph <= ushort.MaxValue; glyph++)
                        glyphs.Add((ushort)glyph);
                }

                break;
            }
        }

        return glyphs;
    }

    /// <summary>A glyph-to-class map. Anything unlisted is class zero.</summary>
    private static Dictionary<ushort, ushort> ReadClassDefinition(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);
        var classes = new Dictionary<ushort, ushort>();

        switch (reader.ReadUInt16())
        {
            case 1:
            {
                int start = reader.ReadUInt16();
                int count = reader.ReadUInt16();

                for (var i = 0; i < count; i++)
                {
                    var value = reader.ReadUInt16();
                    if (value != 0 && start + i <= ushort.MaxValue) classes[(ushort)(start + i)] = value;
                }

                break;
            }

            case 2:
            {
                int rangeCount = reader.ReadUInt16();
                for (var i = 0; i < rangeCount; i++)
                {
                    int first = reader.ReadUInt16();
                    int last = reader.ReadUInt16();
                    var value = reader.ReadUInt16();

                    if (value == 0) continue;

                    for (var glyph = first; glyph <= last && glyph <= ushort.MaxValue; glyph++)
                        classes[(ushort)glyph] = value;
                }

                break;
            }
        }

        return classes;
    }

    /// <summary>
    /// One class-based subtable: which glyphs it covers on the left, what class each side of a
    /// pair falls into, and the adjustment for every combination of the two.
    /// </summary>
    private sealed class ClassPairs(
        HashSet<ushort> coverage,
        Dictionary<ushort, ushort> leftClasses,
        Dictionary<ushort, ushort> rightClasses,
        short[] values,
        int class2Count)
    {
        public short Lookup(ushort left, ushort right)
        {
            if (!coverage.Contains(left)) return 0;

            int leftClass = leftClasses.GetValueOrDefault(left);
            int rightClass = rightClasses.GetValueOrDefault(right);

            var index = leftClass * class2Count + rightClass;
            return index >= 0 && index < values.Length ? values[index] : (short)0;
        }
    }
}
