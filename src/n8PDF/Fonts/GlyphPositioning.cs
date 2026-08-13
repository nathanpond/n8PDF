namespace n8PDF.Fonts;

/// <summary>
/// What an OpenType font's <c>GPOS</c> table says about where glyphs go: the kerning of a pair,
/// and where a mark is drawn on the letter it belongs to.
/// </summary>
/// <remarks>
/// The legacy <c>kern</c> table is the older way of saying the first of those, and fonts shipped
/// this century increasingly carry only this one — Calibri has no <c>kern</c> table at all — so a
/// converter that read only the old one would silently stop kerning on most modern text.
///
/// A mark is a different matter: there is no old way of saying it. An accent, a Hebrew vowel
/// point, an Arabic dot — none of them has a place of its own. The font says where each attaches
/// by giving the mark an anchor and the letter an anchor, and the two are brought together. A
/// converter that ignores this draws the mark wherever the pen happened to be, which for the marks
/// that carry meaning is not a smaller mistake than drawing nothing.
///
/// What is read is the <c>kern</c>, <c>mark</c> and <c>mkmk</c> features' lookups: pair
/// positioning in both formats, marks attached to letters, and marks attached to other marks.
/// Cursive joining and the contextual lookups are not, and a table that cannot be read costs the
/// font its positioning rather than failing the conversion.
/// </remarks>
internal sealed class GlyphPositioning
{
    /// <summary>Explicit pairs, from the format that lists them one by one.</summary>
    private readonly Dictionary<int, short> _pairs = [];

    /// <summary>Class-based subtables, which is how most of a font's kerning is expressed.</summary>
    private readonly List<ClassPairs> _classPairs = [];

    /// <summary>
    /// The mark attachments, one entry for each subtable of the font that describes some.
    /// </summary>
    /// <remarks>
    /// They are kept apart rather than merged because the kinds of place a subtable names are its
    /// own: class two in one subtable and class two in another have nothing to do with each other,
    /// so a mark's class read from one and a letter's places read from another give an answer that
    /// looks reasonable and is wrong. A font that positions Latin and Hebrew in two subtables —
    /// which Times New Roman does — puts every Hebrew point in the wrong place that way.
    /// </remarks>
    private readonly List<MarkAttachment> _attachments = [];

    /// <summary>Every glyph the font treats as a mark, whichever subtable said so.</summary>
    private readonly HashSet<ushort> _markGlyphs = [];

    private GlyphPositioning()
    {
    }

    public bool IsEmpty => _pairs.Count == 0 && _classPairs.Count == 0 && _attachments.Count == 0;

    /// <summary>Whether the font treats this glyph as a mark drawn on something else.</summary>
    public bool IsMark(ushort glyph) => _markGlyphs.Contains(glyph);

    /// <summary>
    /// Where a mark goes on what it is drawn on, in design units, relative to the pen standing
    /// where that glyph began. Null where the font says nothing about the pair.
    /// </summary>
    /// <param name="onMark">
    /// The mark standing next to it, where there is one. A mark may be drawn on a mark, which is
    /// how a letter carries two.
    /// </param>
    /// <param name="component">
    /// Which part of what it is drawn on the mark belongs to, where that is a ligature. A shape
    /// standing for several letters offers a place for each of them — the vowel over the second
    /// lam of the name of God is not the vowel over the first — and a glyph that is not a
    /// ligature has the one part.
    /// </param>
    /// <returns>
    /// The movement, and whether it was measured from the mark beside it rather than from the
    /// letter. Which of the two it is drawn on is not decided by which stands nearer: it is
    /// decided by the font, and the answer is whichever of its tables speaks first.
    /// </returns>
    public (short X, short Y, bool OnMark)? GetMarkOffset(
        ushort mark, ushort? attachTo, ushort? onMark, int component = 0)
    {
        // The subtables are tried in the order the font lists them, and the first that knows
        // about both the mark and what it is drawn on is the one that decides — which is how the
        // lookups themselves are applied. A mark written after the last letter of a ligature is
        // placed on that letter by a table the font lists before the one that would have placed
        // it on the mark beside it, and following the order is what gets it there.
        foreach (var attachment in _attachments)
        {
            var target = attachment.OnMark ? onMark : attachTo;
            if (target is null) continue;

            if (attachment.Offset(mark, target.Value, component) is not { } offset) continue;

            return (offset.X, offset.Y, attachment.OnMark);
        }

        return null;
    }

    /// <summary>One subtable's worth of marks and the places they are drawn in.</summary>
    private sealed class MarkAttachment(
        bool onMark,
        Dictionary<ushort, (ushort Class, short X, short Y)> marks,
        Dictionary<ushort, (short X, short Y)?[][]> places)
    {
        public bool OnMark { get; } = onMark;

        public (short X, short Y)? Offset(ushort mark, ushort attachTo, int component)
        {
            if (!marks.TryGetValue(mark, out var held)) return null;
            if (!places.TryGetValue(attachTo, out var parts) || parts.Length == 0) return null;

            // A mark past the last part of a ligature belongs to that last part: the vowel written
            // after the final letter of a shape is written on that letter.
            var offered = parts[Math.Clamp(component, 0, parts.Length - 1)];

            if (held.Class >= offered.Length || offered[held.Class] is not { } place) return null;

            return ((short)(place.X - held.X), (short)(place.Y - held.Y));
        }
    }

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

            var lookups = LookupsOf(data, offset + featureListOffset, offset + lookupListOffset,
                ["kern", "mark", "mkmk"]);

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
    /// The offsets of every lookup the named features refer to.
    /// </summary>
    /// <remarks>
    /// Every feature with one of those tags counts, whichever script or language declared it.
    /// Picking the one for the text's own script would need the script to be known here, and the
    /// difference between the tables is vanishingly rare — while missing a feature altogether
    /// because the font filed it under a language rather than the default would not be. What
    /// keeps a script's lookups off another script's text is that a lookup only fires on the
    /// glyphs it covers, and those are that script's own.
    /// </remarks>
    private static List<int> LookupsOf(byte[] data, int featureList, int lookupList, string[] tags)
    {
        var indices = new HashSet<ushort>();

        var features = new BigEndianReader(data, featureList);
        int featureCount = features.ReadUInt16();

        for (var i = 0; i < featureCount; i++)
        {
            var tag = features.ReadTag();
            int featureOffset = features.ReadUInt16();
            if (Array.IndexOf(tags, tag) < 0) continue;

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

                case 4:
                    ReadMarkAttachment(data, subtable, onMark: false);
                    break;

                case 5:
                    ReadMarkAttachment(data, subtable, onMark: false, toLigature: true);
                    break;

                case 6:
                    ReadMarkAttachment(data, subtable, onMark: true);
                    break;

                // An extension subtable is a type 2 subtable behind a 32-bit offset, which is how
                // a font whose tables outgrew 16 bits reaches them.
                case 9:
                {
                    var extension = new BigEndianReader(data, subtable);
                    extension.Skip(2); // posFormat
                    int extensionType = extension.ReadUInt16();
                    var target = subtable + (int)extension.ReadUInt32();

                    if (target < tableStart || target >= tableStart + tableLength) break;

                    switch (extensionType)
                    {
                        case 2:
                            ReadPairPositioning(data, target);
                            break;

                        case 4:
                            ReadMarkAttachment(data, target, onMark: false);
                            break;

                        case 5:
                            ReadMarkAttachment(data, target, onMark: false, toLigature: true);
                            break;

                        case 6:
                            ReadMarkAttachment(data, target, onMark: true);
                            break;
                    }

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
    /// <summary>
    /// Reads marks attached to letters, or marks attached to other marks: the two are the same
    /// table under different names. One side holds the marks and the place each wants; the other
    /// holds, for every glyph a mark may go on, a place for each kind of mark.
    /// </summary>
    /// <param name="toLigature">
    /// Whether what the marks go on is a ligature, whose table holds not one place for each kind
    /// of mark but one for each letter the shape stands for.
    /// </param>
    private void ReadMarkAttachment(byte[] data, int offset, bool onMark, bool toLigature = false)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return;

        int markCoverageOffset = reader.ReadUInt16();
        int baseCoverageOffset = reader.ReadUInt16();
        int classCount = reader.ReadUInt16();
        int markArrayOffset = reader.ReadUInt16();
        int baseArrayOffset = reader.ReadUInt16();

        var markGlyphs = ReadCoverage(data, offset + markCoverageOffset);
        var baseGlyphs = ReadCoverage(data, offset + baseCoverageOffset);

        var marks = new Dictionary<ushort, (ushort Class, short X, short Y)>();
        var places = new Dictionary<ushort, (short X, short Y)?[][]>();

        // The marks: each says which kind of place it wants and where its own anchor sits.
        var markArray = new BigEndianReader(data, offset + markArrayOffset);
        int markCount = markArray.ReadUInt16();

        for (var i = 0; i < markCount && i < markGlyphs.Count; i++)
        {
            int markClass = markArray.ReadUInt16();
            int anchorOffset = markArray.ReadUInt16();

            if (anchorOffset == 0) continue;
            if (ReadAnchor(data, offset + markArrayOffset + anchorOffset) is not { } anchor) continue;

            marks[markGlyphs[i]] = ((ushort)markClass, anchor.X, anchor.Y);
            _markGlyphs.Add(markGlyphs[i]);
        }

        // And what they are drawn on: a place for each kind of mark, or nothing where that glyph
        // offers none of that kind. A ligature says it once for each letter it stands for, behind
        // a further offset, since which place a mark wants depends on which letter it was written
        // over.
        var baseArray = new BigEndianReader(data, offset + baseArrayOffset);
        int baseCount = baseArray.ReadUInt16();

        if (toLigature)
        {
            var attachOffsets = new int[baseCount];
            for (var i = 0; i < baseCount; i++) attachOffsets[i] = baseArray.ReadUInt16();

            for (var i = 0; i < baseCount && i < baseGlyphs.Count; i++)
            {
                if (attachOffsets[i] == 0) continue;

                var attachAt = offset + baseArrayOffset + attachOffsets[i];
                var attach = new BigEndianReader(data, attachAt);

                int componentCount = attach.ReadUInt16();
                if (componentCount is < 1 or > 64) continue;

                var parts = new (short X, short Y)?[componentCount][];

                for (var part = 0; part < componentCount; part++)
                {
                    parts[part] = ReadAnchors(data, ref attach, attachAt, classCount);
                }

                places[baseGlyphs[i]] = parts;
            }
        }
        else
        {
            for (var i = 0; i < baseCount && i < baseGlyphs.Count; i++)
            {
                places[baseGlyphs[i]] =
                    [ReadAnchors(data, ref baseArray, offset + baseArrayOffset, classCount)];
            }
        }

        if (marks.Count > 0 && places.Count > 0)
            _attachments.Add(new MarkAttachment(onMark, marks, places));
    }

    /// <summary>One place for each kind of mark, read where the reader stands.</summary>
    private static (short X, short Y)?[] ReadAnchors(
        byte[] data, ref BigEndianReader reader, int from, int classCount)
    {
        var offered = new (short X, short Y)?[classCount];

        for (var c = 0; c < classCount; c++)
        {
            int anchorOffset = reader.ReadUInt16();
            if (anchorOffset == 0) continue;

            offered[c] = ReadAnchor(data, from + anchorOffset);
        }

        return offered;
    }

    /// <summary>
    /// One anchor: a point on a glyph. Three formats, of which the two beyond the first add a
    /// hinting refinement that means nothing at the sizes a PDF is drawn at.
    /// </summary>
    private static (short X, short Y)? ReadAnchor(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format is < 1 or > 3) return null;

        return (reader.ReadInt16(), reader.ReadInt16());
    }

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
