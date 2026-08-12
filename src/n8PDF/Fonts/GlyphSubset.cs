namespace n8PDF.Fonts;

/// <summary>
/// Rebuilds a font's outline tables to hold only the glyphs a document actually used.
/// </summary>
/// <remarks>
/// A text face runs to hundreds of kilobytes and a document typically draws a hundred glyphs of
/// it, so embedding the whole thing puts most of a PDF's weight into outlines nothing draws.
///
/// Glyph numbering is left alone: the glyphs that survive keep the indices they had, and the ones
/// that do not are emptied rather than removed. That keeps every other part of the file honest —
/// with Identity-H the character codes in the content stream <em>are</em> glyph indices, and the
/// width array and the ToUnicode map are keyed by them too, so renumbering would mean rewriting
/// all three. What is left is the part that matters: <c>glyf</c> is nearly all of a font's bulk.
/// </remarks>
internal static class GlyphSubset
{
    /// <summary>The rebuilt tables, ready to be written in place of the originals.</summary>
    /// <param name="Hmtx">Metrics as far as the last glyph kept, or null to leave them alone.</param>
    /// <param name="MetricCount">What <c>hhea</c> should now say the metric count is.</param>
    internal readonly record struct Tables(byte[] Glyf, byte[] Loca, byte[]? Hmtx, int MetricCount);

    /// <summary>
    /// A <c>post</c> table of version 3.0, which is the version that carries no glyph names.
    /// </summary>
    /// <remarks>
    /// The names run to tens of kilobytes in a text face and nothing reads them here: glyphs are
    /// reached by index through Identity-H, and text is recovered through the PDF's own ToUnicode
    /// map. In Times New Roman they are a third of what a subset would otherwise weigh.
    /// </remarks>
    public static byte[] NamelessPost(byte[] source, TrueTypeFont.TableRecord post)
    {
        var table = new byte[32];
        var length = Math.Min(32, Math.Min(post.Length, source.Length - post.Offset));

        // Everything after the version — the italic angle, the underline, whether the font is
        // fixed pitch — is kept as it was.
        if (length > 0) Array.Copy(source, post.Offset, table, 0, length);

        table[0] = 0x00;
        table[1] = 0x03;
        table[2] = 0x00;
        table[3] = 0x00;

        return table;
    }

    /// <summary>
    /// Builds the outline tables for a set of glyphs, or returns null when the font is not one
    /// this can subset — a CFF face keeps its outlines somewhere else entirely.
    /// </summary>
    public static Tables? Build(TrueTypeFont font, IReadOnlyCollection<ushort> glyphs)
    {
        if (font.HasCffOutlines) return null;
        if (!font.Tables.TryGetValue("glyf", out var glyf)) return null;
        if (!font.Tables.TryGetValue("loca", out var loca)) return null;
        if (!font.Tables.TryGetValue("head", out var head)) return null;

        try
        {
            var source = font.SourceData;
            var longLoca = new BigEndianReader(source, head.Offset + 50).ReadInt16() != 0;

            var offsets = ReadLoca(source, loca, font.GlyphCount, longLoca);
            if (offsets is null) return null;

            var wanted = Closure(source, glyf, offsets, glyphs, font.GlyphCount);
            var tables = Rebuild(source, glyf, offsets, wanted, font.GlyphCount);

            return TrimMetrics(font, wanted, tables);
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>Where each glyph's outline starts, with one extra entry marking the end.</summary>
    private static int[]? ReadLoca(byte[] source, TrueTypeFont.TableRecord loca, int glyphCount, bool longLoca)
    {
        var entries = glyphCount + 1;
        var needed = longLoca ? entries * 4 : entries * 2;
        if (loca.Length < needed) return null;

        var reader = new BigEndianReader(source, loca.Offset);
        var offsets = new int[entries];

        for (var i = 0; i < entries; i++)
        {
            // Short offsets are stored halved, which is why they need a font's outlines to sit on
            // even boundaries.
            offsets[i] = longLoca ? (int)reader.ReadUInt32() : reader.ReadUInt16() * 2;
        }

        return offsets;
    }

    /// <summary>
    /// The glyphs to keep: the ones asked for, plus everything they are built out of.
    /// </summary>
    /// <remarks>
    /// A composite glyph is a list of references to other glyphs — an accented letter is usually
    /// the letter and the accent placed against each other — and those components may be
    /// composites themselves. Dropping one because no character maps to it would leave the glyph
    /// that needs it drawing a hole.
    /// </remarks>
    private static HashSet<ushort> Closure(
        byte[] source, TrueTypeFont.TableRecord glyf, int[] offsets,
        IReadOnlyCollection<ushort> glyphs, int glyphCount)
    {
        // Glyph zero is what a reader falls back on, and a font without it is malformed.
        var wanted = new HashSet<ushort> { 0 };
        var pending = new Stack<ushort>();

        foreach (var glyph in glyphs)
        {
            if (glyph < glyphCount && wanted.Add(glyph)) pending.Push(glyph);
        }

        pending.Push(0);

        while (pending.Count > 0)
        {
            var glyph = pending.Pop();

            var start = offsets[glyph];
            var end = offsets[glyph + 1];
            if (end - start < 10) continue;

            var reader = new BigEndianReader(source, glyf.Offset + start);
            if (reader.ReadInt16() >= 0) continue;

            reader.Skip(8); // the bounding box

            while (true)
            {
                var flags = reader.ReadUInt16();
                var component = reader.ReadUInt16();

                if (component < glyphCount && wanted.Add(component)) pending.Push(component);

                reader.Skip((flags & 0x0001) != 0 ? 4 : 2); // the placement arguments

                if ((flags & 0x0008) != 0) reader.Skip(2);       // one scale
                else if ((flags & 0x0040) != 0) reader.Skip(4);  // x and y scales
                else if ((flags & 0x0080) != 0) reader.Skip(8);  // a two-by-two transform

                if ((flags & 0x0020) == 0) break;                // no more components
            }
        }

        return wanted;
    }

    /// <summary>
    /// Writes the outlines of the glyphs being kept, and a location table that gives every other
    /// glyph an empty one.
    /// </summary>
    /// <remarks>
    /// The rebuilt locations are always in the long format, whatever the font used. A short one
    /// halves its offsets and so cannot describe a table whose length is odd, and settling on the
    /// wider form costs two bytes a glyph and removes the question.
    /// </remarks>
    private static Tables Rebuild(
        byte[] source, TrueTypeFont.TableRecord glyf, int[] offsets,
        HashSet<ushort> wanted, int glyphCount)
    {
        var outlines = new MemoryStream();
        var loca = new byte[(glyphCount + 1) * 4];

        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            WriteUInt32(loca, glyph * 4, (uint)outlines.Length);

            if (!wanted.Contains((ushort)glyph)) continue;

            var start = offsets[glyph];
            var length = offsets[glyph + 1] - start;
            if (length <= 0) continue;

            var available = Math.Min(length, glyf.Length - start);
            if (available <= 0) continue;

            outlines.Write(source, glyf.Offset + start, available);

            // Each outline starts on a four-byte boundary, which is what the format asks for and
            // what keeps the offsets of the glyphs after it aligned.
            while (outlines.Length % 4 != 0) outlines.WriteByte(0);
        }

        WriteUInt32(loca, glyphCount * 4, (uint)outlines.Length);

        return new Tables(outlines.ToArray(), loca, null, 0);
    }

    /// <summary>
    /// Cuts the metrics table off after the last glyph being kept.
    /// </summary>
    /// <remarks>
    /// A font states how many glyphs carry their own advance; every glyph past that shares the
    /// last one's. The glyphs past the end here are the empty ones, so what they are said to be
    /// wide never shows — and in a face of three thousand glyphs used for a line of text, most of
    /// the table is describing glyphs that are no longer there.
    /// </remarks>
    private static Tables TrimMetrics(TrueTypeFont font, HashSet<ushort> wanted, Tables tables)
    {
        if (!font.Tables.TryGetValue("hmtx", out var hmtx)) return tables;
        if (!font.Tables.TryGetValue("hhea", out var hhea)) return tables;

        var source = font.SourceData;
        int metricCount = new BigEndianReader(source, hhea.Offset + 34).ReadUInt16();

        var highest = 0;
        foreach (var glyph in wanted) highest = Math.Max(highest, glyph);

        var keep = Math.Min(metricCount, highest + 1);
        if (keep <= 0 || keep >= metricCount) return tables;

        var length = keep * 4;
        if (hmtx.Offset + length > source.Length) return tables;

        var trimmed = new byte[length];
        Array.Copy(source, hmtx.Offset, trimmed, 0, length);

        return tables with { Hmtx = trimmed, MetricCount = keep };
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
