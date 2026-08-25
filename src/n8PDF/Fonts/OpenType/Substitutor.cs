namespace n8PDF.Fonts.OpenType;

/// <summary>
/// Applies a font's <c>GSUB</c> lookups: what a run of glyphs is turned into before it is placed.
/// </summary>
/// <remarks>
/// Substitution is where most of a complex script lives. A letter that changes shape by its
/// neighbours, two letters written as one, one letter written as two, a consonant written under
/// the one before it: each is a lookup saying that these glyphs, in this company, are really those
/// glyphs. The company is the hard part, and the contextual rules that express it are in
/// <see cref="LookupEngine"/> along with everything else the two tables share.
/// </remarks>
internal sealed class Substitutor(LayoutTable table, GlyphClasses? classes)
    : LookupEngine(table, classes)
{
    protected override int ReverseType => 8;

    protected override int ExtensionType => 7;

    protected override int Apply(
        List<ShapeItem> buffer, int at, int type, int offset, Lookup lookup, int depth) =>
        type switch
        {
            1 => Single(buffer, at, offset),
            2 => Multiple(buffer, at, offset),
            3 => Alternate(buffer, at, offset),
            4 => Ligature(buffer, at, offset, lookup),
            5 => Contextual(buffer, at, offset, lookup, depth),
            6 => Chaining(buffer, at, offset, lookup, depth),
            8 => ReverseChaining(buffer, at, offset, lookup) ? at + 1 : at,
            _ => at
        };

    /// <summary>One glyph for another.</summary>
    private int Single(List<ShapeItem> buffer, int at, int offset)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        int coverage = reader.ReadUInt16();

        var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
        if (index < 0) return at;

        if (format == 1)
        {
            var delta = reader.ReadInt16();

            buffer[at].Glyph = (ushort)(buffer[at].Glyph + delta);
            buffer[at].Substituted = true;

            return at + 1;
        }

        if (format != 2) return at;

        int count = reader.ReadUInt16();
        if (index >= count) return at;

        buffer[at].Glyph = LayoutReaders.ReadUInt16At(data, offset + 6 + index * 2);
        buffer[at].Substituted = true;

        return at + 1;
    }

    /// <summary>
    /// One glyph for several: a vowel sign stored as one character and written as two pieces, one
    /// either side of the consonant it belongs to.
    /// </summary>
    private int Multiple(List<ShapeItem> buffer, int at, int offset)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return at;

        int coverage = reader.ReadUInt16();
        int count = reader.ReadUInt16();

        var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
        if (index < 0 || index >= count) return at;

        var sequence = offset + LayoutReaders.ReadUInt16At(data, offset + 6 + index * 2);
        int glyphCount = LayoutReaders.ReadUInt16At(data, sequence);

        // A sequence of nothing deletes the glyph, which is how a font drops a character its
        // context has made unnecessary.
        if (glyphCount == 0)
        {
            buffer.RemoveAt(at);
            return at;
        }

        var item = buffer[at];

        item.Glyph = LayoutReaders.ReadUInt16At(data, sequence + 2);
        item.Substituted = true;
        item.Multiplied = glyphCount > 1;

        // A Multiple lookup turns one glyph into up to 65535, and the plans run many passes each
        // re-covering the last one's output, so unbounded this expands one character into
        // billions of glyphs. The shaped buffer is capped: once it is full the expansion stops
        // (#184). No real run approaches the bound.
        if (buffer.Count + glyphCount > ShapingLimits.MaxGlyphs)
            return at + 1;

        for (var i = 1; i < glyphCount; i++)
        {
            // Every piece stands for the same character, so they share its cluster.
            buffer.Insert(at + i, new ShapeItem(
                LayoutReaders.ReadUInt16At(data, sequence + 2 + i * 2), item.Cluster, item.Mask)
            {
                Category = item.Category,
                Position = item.Position,
                Syllable = item.Syllable,
                Substituted = true,
                Multiplied = true
            });
        }

        return at + glyphCount;
    }

    /// <summary>
    /// One glyph for one of several, of which the first is taken: choosing between them is a
    /// matter of taste, and a document has no way to express which it wants.
    /// </summary>
    private int Alternate(List<ShapeItem> buffer, int at, int offset)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return at;

        int coverage = reader.ReadUInt16();
        int count = reader.ReadUInt16();

        var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
        if (index < 0 || index >= count) return at;

        var set = offset + LayoutReaders.ReadUInt16At(data, offset + 6 + index * 2);
        if (LayoutReaders.ReadUInt16At(data, set) == 0) return at;

        buffer[at].Glyph = LayoutReaders.ReadUInt16At(data, set + 2);
        buffer[at].Substituted = true;

        return at + 1;
    }

    /// <summary>Several glyphs written as one.</summary>
    private int Ligature(List<ShapeItem> buffer, int at, int offset, Lookup lookup)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return at;

        int coverage = reader.ReadUInt16();
        int setCount = reader.ReadUInt16();

        var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
        if (index < 0 || index >= setCount) return at;

        var set = offset + LayoutReaders.ReadUInt16At(data, offset + 6 + index * 2);
        int ligatures = LayoutReaders.ReadUInt16At(data, set);

        for (var i = 0; i < ligatures; i++)
        {
            var entry = set + LayoutReaders.ReadUInt16At(data, set + 2 + i * 2);

            var result = LayoutReaders.ReadUInt16At(data, entry);
            int components = LayoutReaders.ReadUInt16At(data, entry + 2);

            if (components is < 1 or > 64) continue;

            var matched = new int[components];
            matched[0] = at;

            var component = 1;
            var position = at;

            while (component < components)
            {
                position = Next(buffer, position, lookup);
                if (position >= buffer.Count) break;

                if (buffer[position].Glyph !=
                    LayoutReaders.ReadUInt16At(data, entry + 2 + component * 2))
                {
                    break;
                }

                matched[component++] = position;
            }

            if (component < components) continue;

            return WriteLigature(buffer, matched, result);
        }

        return at;
    }

    /// <summary>
    /// Replaces the glyphs a ligature was made of with the one it is, keeping whatever the match
    /// reached across where it was.
    /// </summary>
    private int WriteLigature(List<ShapeItem> buffer, int[] matched, ushort result)
    {
        var first = buffer[matched[0]];

        // Where in the text the letters it was made of came from, so that what is drawn as one
        // shape can still be read back as the several characters it stands for.
        var merged = new List<int>();

        foreach (var position in matched)
        {
            if (buffer[position].Merged is { } already) merged.AddRange(already);
            else merged.Add(buffer[position].Cluster);
        }

        first.Glyph = result;
        first.Merged = [.. merged];
        first.Substituted = true;
        first.Ligated = true;

        // A mark the match reached across was written over one of the letters the shape now stands
        // for, and the font offers a place for each of them. Which letter is how many components
        // stand before the mark; a mark past the last of them was written over the last.
        for (var i = matched[0] + 1; i < buffer.Count; i++)
        {
            if (Array.IndexOf(matched, i) >= 0) continue;
            if (i > matched[^1] && Classes?.IsMark(buffer[i].Glyph) != true) break;

            var component = 0;
            while (component + 1 < matched.Length && matched[component + 1] < i) component++;

            buffer[i].Component = component;
        }

        for (var i = matched.Length - 1; i >= 1; i--) buffer.RemoveAt(matched[i]);

        return matched[0] + 1;
    }

    /// <summary>
    /// One glyph for another, decided by what stands around it and applied from the end of the run
    /// backwards.
    /// </summary>
    private bool ReverseChaining(List<ShapeItem> buffer, int at, int offset, Lookup lookup)
    {
        var data = Table.Data;
        var cursor = offset;

        int format = LayoutReaders.ReadUInt16At(data, cursor);
        if (format != 1) return false;

        var coverage = offset + LayoutReaders.ReadUInt16At(data, cursor + 2);
        cursor += 4;

        var index = LayoutReaders.CoverageIndex(data, coverage, buffer[at].Glyph);
        if (index < 0) return false;

        int backtrack = LayoutReaders.ReadUInt16At(data, cursor);
        var backtrackAt = cursor + 2;
        cursor = backtrackAt + backtrack * 2;

        int lookahead = LayoutReaders.ReadUInt16At(data, cursor);
        var lookaheadAt = cursor + 2;
        cursor = lookaheadAt + lookahead * 2;

        int count = LayoutReaders.ReadUInt16At(data, cursor);
        var substitutes = cursor + 2;

        if (index >= count) return false;

        bool Covered(int coverageAt, ushort glyph) =>
            LayoutReaders.CoverageIndex(
                data, offset + LayoutReaders.ReadUInt16At(data, coverageAt), glyph) >= 0;

        if (!MatchBackward(buffer, at, lookup, backtrack,
                (n, glyph) => Covered(backtrackAt + n * 2, glyph)))
        {
            return false;
        }

        var after = at;

        for (var n = 0; n < lookahead; n++)
        {
            after = Next(buffer, after, lookup);

            if (after >= buffer.Count)
            {
                if (AssumesContext) break;
                return false;
            }

            if (!Covered(lookaheadAt + n * 2, buffer[after].Glyph)) return false;
        }

        buffer[at].Glyph = LayoutReaders.ReadUInt16At(data, substitutes + index * 2);
        buffer[at].Substituted = true;

        return true;
    }
}
