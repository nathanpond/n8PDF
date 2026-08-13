namespace n8PDF.Fonts.OpenType;

/// <summary>
/// What applying a font's lookups has in common, whichever table they came from.
/// </summary>
/// <remarks>
/// <c>GSUB</c> and <c>GPOS</c> are the same machine pointed at different ends of the problem. Both
/// walk a run of glyphs; both skip what the lookup says it cannot see; both have rules that match
/// what stands before and after; and in both, a matched rule does not act itself but names other
/// lookups of the same table to run at places within the match. Only the acting differs, so only
/// the acting is written twice.
/// </remarks>
internal abstract class LookupEngine(LayoutTable table, GlyphClasses? classes)
{
    protected const int MaxNesting = 8;

    protected LayoutTable Table { get; } = table;

    protected GlyphClasses? Classes { get; } = classes;

    /// <summary>The lookup type that is defined to run from the end of the run backwards.</summary>
    protected virtual int ReverseType => -1;

    /// <summary>Which lookup type stands for a subtable behind a wider offset.</summary>
    protected abstract int ExtensionType { get; }

    /// <summary>
    /// The features this run may use: the ones its script declares.
    /// </summary>
    /// <remarks>
    /// Held here rather than on the table because a table belongs to a font and a selection
    /// belongs to a run. One document may set Hindi and Marathi in the same face on the same line,
    /// and they do not draw the same letters.
    /// </remarks>
    private IReadOnlyDictionary<string, int[]> _features = table.Everything;

    public bool Has(string feature) => _features.ContainsKey(feature);

    /// <summary>
    /// Whether lookups may match across a syllable boundary.
    /// </summary>
    /// <remarks>
    /// For the scripts that are shaped a syllable at a time they may not. A font's rule about a
    /// consonant followed by a vowel sign is a rule about one syllable, and letting it reach into
    /// the next would join two words that merely stand next to each other.
    /// </remarks>
    public bool WithinSyllables { get; set; }

    /// <summary>
    /// Whether what stands outside the run being matched should be taken to be whatever a rule
    /// wants it to be. Used only for asking a font what it could do with a pair of glyphs.
    /// </summary>
    private bool _assumeContext;

    /// <summary>Narrows the features to the ones a script declares.</summary>
    public bool SelectScript(params string[] tags)
    {
        var (features, matched) = Table.FeaturesFor(tags);

        _features = features;

        return matched;
    }

    /// <summary>Applies every lookup a feature names, to the glyphs the mask allows.</summary>
    public void Apply(List<ShapeItem> buffer, string feature, uint mask = uint.MaxValue)
    {
        if (!_features.TryGetValue(feature, out var lookups)) return;

        foreach (var index in lookups) ApplyLookup(buffer, index, mask);
    }

    /// <summary>
    /// Applies a group of features together, in the order the font lists their lookups rather than
    /// in the order the features are named.
    /// </summary>
    /// <remarks>
    /// Some features are meant to be applied as a group and some one at a time, and which is which
    /// is part of each script's rules. Within a group the order is the font's: a face is free to
    /// file its rules for two features in one run of lookups and expect them run in that order,
    /// and several do. Applying feature by feature would run the second feature's first lookup
    /// before the first feature's last one.
    /// </remarks>
    public void Apply(List<ShapeItem> buffer, IEnumerable<(string Feature, uint Mask)> group)
    {
        var lookups = new SortedDictionary<int, uint>();

        foreach (var (feature, mask) in group)
        {
            if (!_features.TryGetValue(feature, out var indices)) continue;

            foreach (var index in indices)
            {
                lookups[index] = lookups.TryGetValue(index, out var already) ? already | mask : mask;
            }
        }

        foreach (var (index, mask) in lookups) ApplyLookup(buffer, index, mask);
    }

    /// <summary>The same, where every feature of the group applies everywhere.</summary>
    public void Apply(List<ShapeItem> buffer, IEnumerable<string> group) =>
        Apply(buffer, group.Select(feature => (feature, uint.MaxValue)));

    /// <summary>Whether a font says anything at all under this feature.</summary>
    public bool HasLookups(string feature) => _features.ContainsKey(feature);

    protected void ApplyLookup(List<ShapeItem> buffer, int index, uint mask)
    {
        var lookup = Table.Lookups[index];

        // A reverse chaining lookup is defined to run backwards, and its result depends on it: it
        // substitutes while looking at glyphs it has already decided about.
        if (lookup.Type == ReverseType)
        {
            for (var i = buffer.Count - 1; i >= 0; i--)
            {
                if ((buffer[i].Mask & mask) == 0 || Skipped(lookup, buffer[i])) continue;

                ApplyAt(buffer, i, lookup, 0);
            }

            return;
        }

        var at = 0;

        while (at < buffer.Count)
        {
            if ((buffer[at].Mask & mask) != 0 && !Skipped(lookup, buffer[at]))
            {
                var next = ApplyAt(buffer, at, lookup, 0);

                if (next > at)
                {
                    at = next;
                    continue;
                }
            }

            at++;
        }
    }

    /// <summary>
    /// One lookup at one position. The subtables are tried in the order the font lists them and
    /// the first that matches decides.
    /// </summary>
    /// <returns>The position to carry on from, or the one given where nothing matched.</returns>
    protected int ApplyAt(List<ShapeItem> buffer, int at, Lookup lookup, int depth)
    {
        foreach (var subtable in lookup.Subtables)
        {
            var type = lookup.Type;
            var offset = subtable;

            if (type == ExtensionType)
            {
                if (LayoutReaders.Extension(Table.Data, subtable) is not { } extension) continue;
                if (!Table.Contains(extension.Offset)) continue;

                type = extension.Type;
                offset = extension.Offset;
            }

            var next = Apply(buffer, at, type, offset, lookup, depth);
            if (next > at) return next;
        }

        return at;
    }

    /// <summary>One subtable of one kind at one position.</summary>
    protected abstract int Apply(
        List<ShapeItem> buffer, int at, int type, int offset, Lookup lookup, int depth);

    /// <summary>Whether a lookup is blind to this glyph.</summary>
    protected bool Skipped(Lookup lookup, ShapeItem item)
    {
        if (lookup.Flag == 0 || Classes is null) return false;

        var kind = Classes.ClassOf(item.Glyph);

        if ((lookup.Flag & 0x0002) != 0 && kind == GlyphClasses.Base) return true;
        if ((lookup.Flag & 0x0004) != 0 && kind == GlyphClasses.Ligature) return true;
        if ((lookup.Flag & 0x0008) != 0 && kind == GlyphClasses.Mark) return true;

        if (kind != GlyphClasses.Mark) return false;

        if ((lookup.Flag & 0x0010) != 0 && !Classes.InMarkSet(lookup.MarkFilteringSet, item.Glyph))
            return true;

        var attachment = lookup.Flag >> 8;

        return attachment != 0 && Classes.MarkAttachClass(item.Glyph) != attachment;
    }

    /// <summary>The next position a lookup can see, which may be several along.</summary>
    protected int Next(List<ShapeItem> buffer, int from, Lookup lookup)
    {
        var at = from + 1;
        while (at < buffer.Count && Skipped(lookup, buffer[at])) at++;

        if (WithinSyllables && at < buffer.Count && buffer[at].Syllable != buffer[from].Syllable)
            return buffer.Count;

        return at;
    }

    protected int Previous(List<ShapeItem> buffer, int from, Lookup lookup)
    {
        var at = from - 1;
        while (at >= 0 && Skipped(lookup, buffer[at])) at--;

        if (WithinSyllables && at >= 0 && buffer[at].Syllable != buffer[from].Syllable) return -1;

        return at;
    }

    /// <summary>
    /// Whether a feature would change this sequence of glyphs standing on their own.
    /// </summary>
    /// <remarks>
    /// This is how the shaper asks the font a question it cannot answer itself: whether these two
    /// letters have a joined form, whether that r would become a repha. The specification is
    /// written in terms of what the font can do — a consonant "that has a below-base form" — so
    /// there is no way round asking. The sequence is put through the feature's lookups on its own,
    /// with nothing either side, and what comes out is compared with what went in.
    /// </remarks>
    /// <param name="assumeContext">
    /// Whether a rule that wants something before or after the sequence should be taken to have
    /// found it. Some of the questions are about a pair standing alone — would these two letters
    /// become a repha — and some are about a pair wherever it stands, which is a different
    /// question and a different answer.
    /// </param>
    public bool WouldSubstitute(string feature, bool assumeContext, params ushort[] glyphs)
    {
        if (glyphs.Length == 0 || !_features.TryGetValue(feature, out var lookups)) return false;

        var probe = new List<ShapeItem>(glyphs.Length);
        foreach (var glyph in glyphs) probe.Add(new ShapeItem(glyph, 0, uint.MaxValue));

        var syllables = WithinSyllables;

        WithinSyllables = false;
        _assumeContext = assumeContext;

        foreach (var index in lookups) ApplyLookup(probe, index, uint.MaxValue);

        WithinSyllables = syllables;
        _assumeContext = false;

        if (probe.Count != glyphs.Length) return true;

        for (var i = 0; i < glyphs.Length; i++)
        {
            if (probe[i].Glyph != glyphs[i]) return true;
        }

        return false;
    }

    /// <summary>
    /// Matches a sequence forward from a position, passing over what the lookup cannot see, and
    /// gives back where each of them landed.
    /// </summary>
    protected int[]? MatchForward(
        List<ShapeItem> buffer, int at, Lookup lookup, int count, Func<int, ushort, bool> matches)
    {
        var positions = new int[count + 1];
        positions[0] = at;

        var position = at;

        for (var i = 0; i < count; i++)
        {
            position = Next(buffer, position, lookup);
            if (position >= buffer.Count) return null;
            if (!matches(i, buffer[position].Glyph)) return null;

            positions[i + 1] = position;
        }

        return positions;
    }

    protected bool MatchBackward(
        List<ShapeItem> buffer, int at, Lookup lookup, int count, Func<int, ushort, bool> matches)
    {
        var position = at;

        for (var i = 0; i < count; i++)
        {
            position = Previous(buffer, position, lookup);

            if (position < 0) return _assumeContext;
            if (!matches(i, buffer[position].Glyph)) return false;
        }

        return true;
    }

    /// <summary>Whether what follows the run is taken to be whatever a rule asked for.</summary>
    protected bool AssumesContext => _assumeContext;

    /// <summary>
    /// Applies the lookups a matched rule names, at the places within the match it names them.
    /// </summary>
    /// <remarks>
    /// A rule does not itself act: it says which of the font's other lookups to run and where.
    /// Those may lengthen or shorten the run, so what has already been matched is moved along by
    /// the difference rather than trusted afterwards.
    /// </remarks>
    private int ApplyRule(
        List<ShapeItem> buffer, int[] positions, int records, int recordsAt, int depth, int end)
    {
        if (depth >= MaxNesting) return positions[0] + 1;

        var data = Table.Data;
        var last = end;

        for (var i = 0; i < records; i++)
        {
            int sequence = LayoutReaders.ReadUInt16At(data, recordsAt + i * 4);
            int index = LayoutReaders.ReadUInt16At(data, recordsAt + i * 4 + 2);

            if (sequence >= positions.Length || index >= Table.Lookups.Count) continue;

            var target = positions[sequence];
            if (target >= buffer.Count) continue;

            var before = buffer.Count;

            ApplyAt(buffer, target, Table.Lookups[index], depth + 1);

            var delta = buffer.Count - before;
            if (delta == 0) continue;

            for (var j = 0; j < positions.Length; j++)
            {
                if (positions[j] > target) positions[j] += delta;
            }

            last += delta;
        }

        return Math.Max(positions[0] + 1, last);
    }

    /// <summary>A rule about what a glyph is followed by.</summary>
    protected int Contextual(List<ShapeItem> buffer, int at, int offset, Lookup lookup, int depth)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();

        switch (format)
        {
            case 1:
            case 2:
            {
                int coverage = reader.ReadUInt16();

                var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
                if (index < 0) return at;

                var classDef = format == 2 ? offset + reader.ReadUInt16() : 0;

                int setCount = reader.ReadUInt16();

                var setIndex = format == 1
                    ? index
                    : LayoutReaders.ClassOf(data, classDef, buffer[at].Glyph);

                if (setIndex >= setCount) return at;

                var setOffset = LayoutReaders.ReadUInt16At(
                    data, offset + (format == 1 ? 6 : 8) + setIndex * 2);

                if (setOffset == 0) return at;

                var set = offset + setOffset;
                int rules = LayoutReaders.ReadUInt16At(data, set);

                for (var i = 0; i < rules; i++)
                {
                    var rule = set + LayoutReaders.ReadUInt16At(data, set + 2 + i * 2);

                    int glyphCount = LayoutReaders.ReadUInt16At(data, rule);
                    int records = LayoutReaders.ReadUInt16At(data, rule + 2);

                    if (glyphCount == 0) continue;

                    var positions = MatchForward(buffer, at, lookup, glyphCount - 1, (n, glyph) =>
                    {
                        var wanted = LayoutReaders.ReadUInt16At(data, rule + 4 + n * 2);

                        return format == 1
                            ? glyph == wanted
                            : LayoutReaders.ClassOf(data, classDef, glyph) == wanted;
                    });

                    if (positions is null) continue;

                    return ApplyRule(buffer, positions, records, rule + 4 + (glyphCount - 1) * 2,
                        depth, positions[^1] + 1);
                }

                return at;
            }

            case 3:
            {
                int glyphCount = reader.ReadUInt16();
                int records = reader.ReadUInt16();

                if (glyphCount == 0) return at;

                var coverages = offset + 6;

                if (LayoutReaders.CoverageIndex(data,
                        offset + LayoutReaders.ReadUInt16At(data, coverages), buffer[at].Glyph) < 0)
                {
                    return at;
                }

                var positions = MatchForward(buffer, at, lookup, glyphCount - 1, (n, glyph) =>
                    LayoutReaders.CoverageIndex(data,
                        offset + LayoutReaders.ReadUInt16At(data, coverages + (n + 1) * 2), glyph) >= 0);

                if (positions is null) return at;

                return ApplyRule(buffer, positions, records, coverages + glyphCount * 2, depth,
                    positions[^1] + 1);
            }
        }

        return at;
    }

    /// <summary>A rule about what a glyph is preceded and followed by, which is most of them.</summary>
    protected int Chaining(List<ShapeItem> buffer, int at, int offset, Lookup lookup, int depth)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();

        switch (format)
        {
            case 1:
            case 2:
            {
                int coverage = reader.ReadUInt16();

                var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
                if (index < 0) return at;

                var backtrackClasses = 0;
                var inputClasses = 0;
                var lookaheadClasses = 0;

                if (format == 2)
                {
                    backtrackClasses = offset + reader.ReadUInt16();
                    inputClasses = offset + reader.ReadUInt16();
                    lookaheadClasses = offset + reader.ReadUInt16();
                }

                int setCount = reader.ReadUInt16();

                var setIndex = format == 1
                    ? index
                    : LayoutReaders.ClassOf(data, inputClasses, buffer[at].Glyph);

                if (setIndex >= setCount) return at;

                var setsAt = offset + (format == 1 ? 6 : 12);
                var setOffset = LayoutReaders.ReadUInt16At(data, setsAt + setIndex * 2);
                if (setOffset == 0) return at;

                var set = offset + setOffset;
                int rules = LayoutReaders.ReadUInt16At(data, set);

                for (var i = 0; i < rules; i++)
                {
                    var rule = set + LayoutReaders.ReadUInt16At(data, set + 2 + i * 2);
                    var cursor = rule;

                    int backtrack = LayoutReaders.ReadUInt16At(data, cursor);
                    var backtrackAt = cursor + 2;
                    cursor = backtrackAt + backtrack * 2;

                    int input = LayoutReaders.ReadUInt16At(data, cursor);
                    var inputAt = cursor + 2;
                    cursor = inputAt + (input > 0 ? (input - 1) * 2 : 0);

                    int lookahead = LayoutReaders.ReadUInt16At(data, cursor);
                    var lookaheadAt = cursor + 2;
                    cursor = lookaheadAt + lookahead * 2;

                    int records = LayoutReaders.ReadUInt16At(data, cursor);
                    var recordsAt = cursor + 2;

                    if (input == 0) continue;

                    bool Matches(int classDef, int wanted, ushort glyph) =>
                        format == 1
                            ? glyph == wanted
                            : LayoutReaders.ClassOf(data, classDef, glyph) == wanted;

                    if (!MatchBackward(buffer, at, lookup, backtrack, (n, glyph) =>
                            Matches(backtrackClasses,
                                LayoutReaders.ReadUInt16At(data, backtrackAt + n * 2), glyph)))
                    {
                        continue;
                    }

                    var positions = MatchForward(buffer, at, lookup, input - 1, (n, glyph) =>
                        Matches(inputClasses, LayoutReaders.ReadUInt16At(data, inputAt + n * 2), glyph));

                    if (positions is null) continue;

                    var after = positions[^1];
                    var ahead = true;

                    for (var n = 0; n < lookahead && ahead; n++)
                    {
                        after = Next(buffer, after, lookup);

                        ahead = after >= buffer.Count
                            ? AssumesContext
                            : Matches(lookaheadClasses,
                                LayoutReaders.ReadUInt16At(data, lookaheadAt + n * 2),
                                buffer[after].Glyph);
                    }

                    if (!ahead) continue;

                    return ApplyRule(buffer, positions, records, recordsAt, depth, positions[^1] + 1);
                }

                return at;
            }

            case 3:
            {
                var cursor = offset + 2;

                int backtrack = LayoutReaders.ReadUInt16At(data, cursor);
                var backtrackAt = cursor + 2;
                cursor = backtrackAt + backtrack * 2;

                int input = LayoutReaders.ReadUInt16At(data, cursor);
                var inputAt = cursor + 2;
                cursor = inputAt + input * 2;

                int lookahead = LayoutReaders.ReadUInt16At(data, cursor);
                var lookaheadAt = cursor + 2;
                cursor = lookaheadAt + lookahead * 2;

                int records = LayoutReaders.ReadUInt16At(data, cursor);
                var recordsAt = cursor + 2;

                if (input == 0) return at;

                bool Covered(int coverageAt, ushort glyph) =>
                    LayoutReaders.CoverageIndex(
                        data, offset + LayoutReaders.ReadUInt16At(data, coverageAt), glyph) >= 0;

                if (!Covered(inputAt, buffer[at].Glyph)) return at;

                if (!MatchBackward(buffer, at, lookup, backtrack,
                        (n, glyph) => Covered(backtrackAt + n * 2, glyph)))
                {
                    return at;
                }

                var positions = MatchForward(buffer, at, lookup, input - 1,
                    (n, glyph) => Covered(inputAt + (n + 1) * 2, glyph));

                if (positions is null) return at;

                var after = positions[^1];

                for (var n = 0; n < lookahead; n++)
                {
                    after = Next(buffer, after, lookup);

                    if (after >= buffer.Count)
                    {
                        if (AssumesContext) break;
                        return at;
                    }

                    if (!Covered(lookaheadAt + n * 2, buffer[after].Glyph)) return at;
                }

                return ApplyRule(buffer, positions, records, recordsAt, depth, positions[^1] + 1);
            }
        }

        return at;
    }
}
