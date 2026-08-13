namespace n8PDF.Fonts.OpenType;

/// <summary>
/// One lookup: a kind of change, the glyphs it passes over while looking, and the subtables that
/// hold the change itself.
/// </summary>
/// <param name="Flag">
/// Which glyphs this lookup does not see. A lookup may be told to ignore the marks, or the
/// ligatures, or the base glyphs, or every mark but one attachment class — and what it ignores it
/// ignores while matching as well as while applying, which is what lets a rule about two letters
/// still fire with a vowel written between them.
/// </param>
internal sealed record Lookup(int Type, ushort Flag, ushort MarkFilteringSet, int[] Subtables);

/// <summary>
/// A font's <c>GSUB</c> or <c>GPOS</c> table, read as far as its lookups.
/// </summary>
/// <remarks>
/// The two tables have the same shape: a list of scripts naming a list of features naming a list
/// of lookups. What differs is only what the lookups do, so both are read by this and applied by
/// engines that share everything except the subtables themselves.
///
/// The lookups are not read here. A font of this size holds hundreds, a document uses a few, and
/// each is parsed where it is applied — which also keeps a malformed subtable from stopping a
/// document that never reaches it.
/// </remarks>
internal sealed class LayoutTable
{
    private LayoutTable(byte[] data, int start, int length, List<Lookup> lookups,
        List<(string Tag, int[] Lookups)> features, Dictionary<string, int[]> scripts)
    {
        Data = data;
        Start = start;
        Length = length;
        Lookups = lookups;

        _features = features;
        _scripts = scripts;

        Everything = Merge(Enumerable.Range(0, features.Count));
    }

    private readonly List<(string Tag, int[] Lookups)> _features;

    /// <summary>Which features each script declares, by the tag the font files it under.</summary>
    private readonly Dictionary<string, int[]> _scripts;

    public byte[] Data { get; }

    public int Start { get; }

    public int Length { get; }

    public IReadOnlyList<Lookup> Lookups { get; }

    /// <summary>
    /// The lookups each feature names, in the order they are to be applied, taking every script's
    /// declarations together.
    /// </summary>
    public IReadOnlyDictionary<string, int[]> Everything { get; }

    /// <summary>What has already been worked out for a set of script tags.</summary>
    private readonly Dictionary<string, (IReadOnlyDictionary<string, int[]> Features, bool Matched)>
        _selected = [];

    /// <summary>
    /// Narrows the features to the ones a script declares, and says whether the font knew the tag.
    /// </summary>
    /// <remarks>
    /// Most features can be taken from every script at once: a lookup fires only on the glyphs it
    /// covers, and those are its own script's. One cannot. The localised-forms feature is a font's
    /// way of saying that this language draws this letter differently, over the same glyphs as
    /// everything else — so taking it from every script at once draws Hindi in Marathi's letters.
    /// Which script a run is in is known by then, so it is asked for by name.
    ///
    /// The tags are tried in order, then the two spellings of "no script in particular". A font
    /// that lists none of them keeps everything, which is what fonts without a script list want.
    /// </remarks>
    public (IReadOnlyDictionary<string, int[]> Features, bool Matched) FeaturesFor(string[] tags)
    {
        var key = string.Join(',', tags);

        lock (_selected)
        {
            if (_selected.TryGetValue(key, out var found)) return found;

            foreach (var tag in tags.Concat(["DFLT", "dflt"]))
            {
                if (!_scripts.TryGetValue(tag, out var features)) continue;

                return _selected[key] = (Merge(features), Array.IndexOf(tags, tag) >= 0);
            }

            return _selected[key] = (Everything, false);
        }
    }

    private Dictionary<string, int[]> Merge(IEnumerable<int> features)
    {
        var found = new Dictionary<string, SortedSet<int>>();

        foreach (var index in features)
        {
            if (index < 0 || index >= _features.Count) continue;

            var (tag, lookups) = _features[index];

            if (!found.TryGetValue(tag, out var indices)) found[tag] = indices = [];

            foreach (var lookup in lookups) indices.Add(lookup);
        }

        // In lookup-list order, which is the order a feature's lookups are applied in whatever
        // order the feature happens to name them.
        return found
            .Where(pair => pair.Value.Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }

    public bool Has(string feature) => Everything.ContainsKey(feature);

    /// <summary>Whether an offset lies inside this table, which a malformed one may not.</summary>
    public bool Contains(int offset) => offset >= Start && offset < Start + Length;

    public static LayoutTable? Read(byte[] data, int offset, int length)
    {
        try
        {
            var reader = new BigEndianReader(data, offset);

            reader.Skip(4); // major and minor version
            int scriptListOffset = reader.ReadUInt16();
            int featureListOffset = reader.ReadUInt16();
            int lookupListOffset = reader.ReadUInt16();

            var lookups = ReadLookups(data, offset + lookupListOffset);
            var features = ReadFeatures(data, offset + featureListOffset, lookups.Count);
            var scripts = ReadScripts(data, offset + scriptListOffset);

            return features.Count == 0
                ? null
                : new LayoutTable(data, offset, length, lookups, features, scripts);
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every feature the font declares, in the order it declares them: a tag, and the lookups it
    /// names. The same tag appears once for every script and language that asks for it, which is
    /// why this is a list rather than a table.
    /// </summary>
    private static List<(string Tag, int[] Lookups)> ReadFeatures(
        byte[] data, int featureList, int lookupCount)
    {
        var reader = new BigEndianReader(data, featureList);
        int count = reader.ReadUInt16();

        var records = new (string Tag, int Offset)[count];

        for (var i = 0; i < count; i++)
        {
            var tag = reader.ReadTag();
            records[i] = (tag, featureList + reader.ReadUInt16());
        }

        var features = new List<(string, int[])>(count);

        foreach (var (tag, offset) in records)
        {
            var feature = new BigEndianReader(data, offset);
            feature.Skip(2); // featureParams
            int lookups = feature.ReadUInt16();

            var indices = new List<int>(lookups);

            for (var i = 0; i < lookups; i++)
            {
                int index = feature.ReadUInt16();
                if (index < lookupCount) indices.Add(index);
            }

            features.Add((tag, [.. indices]));
        }

        return features;
    }

    /// <summary>
    /// Which features each script declares, taking the default language of each: a document says
    /// nothing this reader could use to choose between languages.
    /// </summary>
    private static Dictionary<string, int[]> ReadScripts(byte[] data, int scriptList)
    {
        var scripts = new Dictionary<string, int[]>();

        var reader = new BigEndianReader(data, scriptList);
        int count = reader.ReadUInt16();

        var records = new (string Tag, int Offset)[count];

        for (var i = 0; i < count; i++)
        {
            var tag = reader.ReadTag();
            records[i] = (tag, scriptList + reader.ReadUInt16());
        }

        foreach (var (tag, offset) in records)
        {
            var script = new BigEndianReader(data, offset);

            int defaultLangSys = script.ReadUInt16();
            if (defaultLangSys == 0) continue;

            var langSys = new BigEndianReader(data, offset + defaultLangSys);

            langSys.Skip(2); // lookupOrder, which is reserved
            langSys.ReadUInt16(); // requiredFeatureIndex

            int featureCount = langSys.ReadUInt16();
            var features = new int[featureCount];

            for (var i = 0; i < featureCount; i++) features[i] = langSys.ReadUInt16();

            scripts[tag] = features;
        }

        return scripts;
    }

    private static List<Lookup> ReadLookups(byte[] data, int lookupList)
    {
        var reader = new BigEndianReader(data, lookupList);
        int count = reader.ReadUInt16();

        var offsets = new int[count];
        for (var i = 0; i < count; i++) offsets[i] = lookupList + reader.ReadUInt16();

        var lookups = new List<Lookup>(count);

        foreach (var offset in offsets)
        {
            var lookup = new BigEndianReader(data, offset);

            int type = lookup.ReadUInt16();
            var flag = lookup.ReadUInt16();
            int subtableCount = lookup.ReadUInt16();

            var subtables = new int[subtableCount];
            for (var i = 0; i < subtableCount; i++) subtables[i] = offset + lookup.ReadUInt16();

            // The mark filtering set is written after the subtable offsets, and only when the flag
            // that uses it is set.
            var filteringSet = (flag & 0x0010) != 0 ? lookup.ReadUInt16() : (ushort)0;

            lookups.Add(new Lookup(type, flag, filteringSet, subtables));
        }

        return lookups;
    }
}

/// <summary>The pieces of a layout table that both engines read the same way.</summary>
internal static class LayoutReaders
{
    /// <summary>
    /// Where a glyph sits in a coverage table, or -1 where the table does not cover it. The index
    /// is what everything else in a subtable is keyed by.
    /// </summary>
    public static int CoverageIndex(byte[] data, int offset, ushort glyph)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        int count = reader.ReadUInt16();

        if (format == 1)
        {
            // A sorted list of glyphs, searched rather than walked: a coverage table may hold
            // thousands, and a lookup asks about every glyph of every run.
            var low = 0;
            var high = count - 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                var at = ReadUInt16At(data, offset + 4 + middle * 2);

                if (glyph < at) high = middle - 1;
                else if (glyph > at) low = middle + 1;
                else return middle;
            }

            return -1;
        }

        if (format != 2) return -1;

        var first = 0;
        var last = count - 1;

        while (first <= last)
        {
            var middle = (first + last) / 2;
            var at = offset + 4 + middle * 6;

            var start = ReadUInt16At(data, at);
            var end = ReadUInt16At(data, at + 2);

            if (glyph < start) last = middle - 1;
            else if (glyph > end) first = middle + 1;
            else return ReadUInt16At(data, at + 4) + (glyph - start);
        }

        return -1;
    }

    /// <summary>Which class a glyph is in, which is nought where the table does not say.</summary>
    public static int ClassOf(byte[] data, int offset, ushort glyph)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();

        if (format == 1)
        {
            int start = reader.ReadUInt16();
            int count = reader.ReadUInt16();

            return glyph >= start && glyph < start + count
                ? ReadUInt16At(data, offset + 6 + (glyph - start) * 2)
                : 0;
        }

        if (format != 2) return 0;

        int ranges = reader.ReadUInt16();

        var low = 0;
        var high = ranges - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;
            var at = offset + 4 + middle * 6;

            var first = ReadUInt16At(data, at);
            var last = ReadUInt16At(data, at + 2);

            if (glyph < first) high = middle - 1;
            else if (glyph > last) low = middle + 1;
            else return ReadUInt16At(data, at + 4);
        }

        return 0;
    }

    /// <summary>
    /// One anchor: a point on a glyph. Three formats, of which the two beyond the first add a
    /// hinting refinement that means nothing at the sizes a PDF is drawn at.
    /// </summary>
    public static (short X, short Y)? Anchor(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format is < 1 or > 3) return null;

        return (reader.ReadInt16(), reader.ReadInt16());
    }

    public static ushort ReadUInt16At(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    /// <summary>
    /// A subtable behind a wider offset, which is how a font whose tables outgrew sixteen bits
    /// reaches them. The kind it stands in for is written inside it.
    /// </summary>
    public static (int Type, int Offset)? Extension(byte[] data, int offset)
    {
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return null;

        int type = reader.ReadUInt16();

        return (type, offset + (int)reader.ReadUInt32());
    }
}
