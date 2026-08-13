namespace n8PDF.Fonts;

/// <summary>
/// What an OpenType font's <c>GSUB</c> table says a glyph should be swapped for.
/// </summary>
/// <remarks>
/// Positioning moves glyphs; substitution changes which glyphs they are. A script that joins its
/// letters needs the second: the four shapes an Arabic letter takes are four glyphs, and which one
/// is drawn is decided by the neighbours rather than by the character. The font files them under
/// the features <c>init</c>, <c>medi</c>, <c>fina</c> and <c>isol</c>, one glyph swapped for
/// another, and under <c>rlig</c> the ligatures a script insists on — in Arabic, lam followed by
/// alef, which may not be written as two.
///
/// What is read is single substitution in both its formats and ligature substitution, each behind
/// the extension offsets a large font needs. The contextual and chaining lookups are not: they are
/// how the finer typography is expressed, and a font that needs them for a letter's basic shape
/// would be an unusual one.
/// </remarks>
internal sealed class GlyphSubstitution
{
    /// <summary>One glyph for another, by feature.</summary>
    private readonly Dictionary<string, Dictionary<ushort, ushort>> _single = [];

    /// <summary>
    /// Several glyphs for one, by feature: the first glyph, then the rest of the run, and whether
    /// the lookup that holds it looks past marks while matching.
    /// </summary>
    private readonly Dictionary<string, List<Ligature_>> _ligatures = [];

    /// <param name="IgnoresMarks">
    /// Whether the marks between the components are passed over rather than breaking the match.
    /// The font says so per lookup, and both answers are needed: the lookup that writes lam, lam
    /// and heh as the name of God must reach across the vowels written over them, while the one
    /// that combines a shadda with the vowel beside it is matching marks and must not.
    /// </param>
    private readonly record struct Ligature_(ushort[] Glyphs, ushort Result, bool IgnoresMarks);

    private GlyphSubstitution()
    {
    }

    public bool IsEmpty => _single.Count == 0 && _ligatures.Count == 0;

    /// <summary>What a feature says this glyph should be drawn as, or the glyph itself.</summary>
    public ushort Substitute(string feature, ushort glyph) =>
        _single.TryGetValue(feature, out var table) && table.TryGetValue(glyph, out var found)
            ? found
            : glyph;

    /// <summary>
    /// The ligature a feature makes of the glyphs starting here, and which of them it takes. The
    /// longest match wins, which is what keeps a three-glyph ligature from being read as a
    /// two-glyph one and a leftover.
    /// </summary>
    /// <param name="isMark">
    /// Whether a glyph is a mark, for the lookups that match past them. The components of a
    /// ligature need not stand next to each other: a vowel written over a lam does not stop the
    /// lam being part of a ligature, any more than it stops it joining to the letter after.
    /// Which glyphs are taken is therefore returned rather than how many, since what is skipped
    /// stays where it is.
    /// </param>
    public (ushort Glyph, int[] Taken)? Ligature(
        string feature, IReadOnlyList<ushort> glyphs, int at, Func<ushort, bool> isMark)
    {
        if (!_ligatures.TryGetValue(feature, out var candidates)) return null;

        (ushort Glyph, int[] Taken)? best = null;

        foreach (var (sequence, result, ignoresMarks) in candidates)
        {
            if (sequence.Length == 0 || sequence[0] != glyphs[at]) continue;
            if (best is { } found && found.Taken.Length >= sequence.Length) continue;

            var taken = new int[sequence.Length];
            taken[0] = at;

            var component = 1;
            var index = at + 1;

            while (component < sequence.Length && index < glyphs.Count)
            {
                if (ignoresMarks && isMark(glyphs[index]))
                {
                    index++;
                    continue;
                }

                if (glyphs[index] != sequence[component]) break;

                taken[component++] = index++;
            }

            if (component == sequence.Length) best = (result, taken);
        }

        return best;
    }

    /// <summary>Reads the substitutions a font describes, or null where it describes none.</summary>
    public static GlyphSubstitution? Read(byte[] data, int offset, int length)
    {
        try
        {
            var reader = new BigEndianReader(data, offset);

            reader.Skip(4); // major and minor version
            reader.ReadUInt16(); // scriptList, which is passed over for the reason GPOS's is
            int featureListOffset = reader.ReadUInt16();
            int lookupListOffset = reader.ReadUInt16();

            var result = new GlyphSubstitution();

            result.ReadFeatures(data, offset + featureListOffset, offset + lookupListOffset,
                offset, length);

            return result.IsEmpty ? null : result;
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>The features worth reading, and the lookups each names.</summary>
    private static readonly string[] Wanted = ["isol", "init", "medi", "fina", "rlig", "liga", "ccmp"];

    private void ReadFeatures(byte[] data, int featureList, int lookupList, int tableStart, int tableLength)
    {
        var features = new BigEndianReader(data, featureList);
        int featureCount = features.ReadUInt16();

        var wanted = new List<(string Tag, int Offset)>();

        for (var i = 0; i < featureCount; i++)
        {
            var tag = features.ReadTag();
            int featureOffset = features.ReadUInt16();

            if (Array.IndexOf(Wanted, tag) >= 0) wanted.Add((tag, featureList + featureOffset));
        }

        if (wanted.Count == 0) return;

        var lookups = new BigEndianReader(data, lookupList);
        int total = lookups.ReadUInt16();

        var offsets = new int[total];
        for (var i = 0; i < total; i++) offsets[i] = lookupList + lookups.ReadUInt16();

        foreach (var (tag, offset) in wanted)
        {
            var feature = new BigEndianReader(data, offset);
            feature.Skip(2); // featureParams
            int lookupCount = feature.ReadUInt16();

            for (var i = 0; i < lookupCount; i++)
            {
                int index = feature.ReadUInt16();
                if (index < offsets.Length) ReadLookup(data, offsets[index], tag, tableStart, tableLength);
            }
        }
    }

    private void ReadLookup(byte[] data, int offset, string tag, int tableStart, int tableLength)
    {
        var reader = new BigEndianReader(data, offset);

        int type = reader.ReadUInt16();

        // Bit 3 of the flags: match past the marks rather than stopping at them.
        var ignoresMarks = (reader.ReadUInt16() & 0x0008) != 0;

        int subtableCount = reader.ReadUInt16();

        var subtables = new List<int>(subtableCount);
        for (var i = 0; i < subtableCount; i++) subtables.Add(offset + reader.ReadUInt16());

        foreach (var subtable in subtables)
        {
            switch (type)
            {
                case 1:
                    ReadSingle(data, subtable, tag);
                    break;

                case 4:
                    ReadLigatures(data, subtable, tag, ignoresMarks);
                    break;

                // An extension subtable is another subtable behind a wider offset, which is how a
                // font whose tables outgrew sixteen bits reaches them.
                case 7:
                {
                    var extension = new BigEndianReader(data, subtable);
                    extension.Skip(2); // substFormat
                    int extensionType = extension.ReadUInt16();
                    var target = subtable + (int)extension.ReadUInt32();

                    if (target < tableStart || target >= tableStart + tableLength) break;

                    if (extensionType == 1) ReadSingle(data, target, tag);
                    else if (extensionType == 4) ReadLigatures(data, target, tag, ignoresMarks);

                    break;
                }
            }
        }
    }

    /// <summary>
    /// One glyph for another: either every covered glyph moved by the same amount through the
    /// font's numbering, or a glyph named for each.
    /// </summary>
    private void ReadSingle(byte[] data, int offset, string tag)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        int coverageOffset = reader.ReadUInt16();

        var covered = ReadCoverage(data, offset + coverageOffset);
        if (covered.Count == 0) return;

        var table = Table(_single, tag);

        if (format == 1)
        {
            var delta = reader.ReadInt16();

            foreach (var glyph in covered) table.TryAdd(glyph, (ushort)(glyph + delta));

            return;
        }

        if (format != 2) return;

        int count = reader.ReadUInt16();

        for (var i = 0; i < count && i < covered.Count; i++) table.TryAdd(covered[i], reader.ReadUInt16());
    }

    /// <summary>Several glyphs written as one, which is what a ligature is.</summary>
    private void ReadLigatures(byte[] data, int offset, string tag, bool ignoresMarks)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return;

        int coverageOffset = reader.ReadUInt16();
        int setCount = reader.ReadUInt16();

        var covered = ReadCoverage(data, offset + coverageOffset);

        var sets = new int[setCount];
        for (var i = 0; i < setCount; i++) sets[i] = offset + reader.ReadUInt16();

        var ligatures = Ligatures(tag);

        for (var i = 0; i < setCount && i < covered.Count; i++)
        {
            var set = new BigEndianReader(data, sets[i]);
            int ligatureCount = set.ReadUInt16();

            var entries = new int[ligatureCount];
            for (var j = 0; j < ligatureCount; j++) entries[j] = sets[i] + set.ReadUInt16();

            foreach (var entry in entries)
            {
                var ligature = new BigEndianReader(data, entry);

                var result = ligature.ReadUInt16();
                int componentCount = ligature.ReadUInt16();

                if (componentCount is < 1 or > 64) continue;

                var glyphs = new ushort[componentCount];
                glyphs[0] = covered[i];

                for (var j = 1; j < componentCount; j++) glyphs[j] = ligature.ReadUInt16();

                ligatures.Add(new Ligature_(glyphs, result, ignoresMarks));
            }
        }
    }

    private Dictionary<ushort, ushort> Table(Dictionary<string, Dictionary<ushort, ushort>> tables, string tag)
    {
        if (tables.TryGetValue(tag, out var found)) return found;

        return tables[tag] = [];
    }

    private List<Ligature_> Ligatures(string tag)
    {
        if (_ligatures.TryGetValue(tag, out var found)) return found;

        return _ligatures[tag] = [];
    }

    private static List<ushort> ReadCoverage(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);
        var glyphs = new List<ushort>();

        int format = reader.ReadUInt16();
        int count = reader.ReadUInt16();

        if (format == 1)
        {
            for (var i = 0; i < count; i++) glyphs.Add(reader.ReadUInt16());
        }
        else if (format == 2)
        {
            for (var i = 0; i < count; i++)
            {
                int start = reader.ReadUInt16();
                int end = reader.ReadUInt16();
                reader.Skip(2); // startCoverageIndex

                for (var glyph = start; glyph <= end && glyph <= ushort.MaxValue; glyph++)
                    glyphs.Add((ushort)glyph);
            }
        }

        return glyphs;
    }
}
