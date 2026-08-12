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
    /// <param name="GlyphCount">
    /// What <c>maxp</c> should now say, when the glyphs were numbered again. Zero leaves it alone.
    /// </param>
    /// <param name="Cmap">A rebuilt character map, for the same case.</param>
    internal readonly record struct Tables(
        byte[] Glyf, byte[] Loca, byte[]? Hmtx, int MetricCount, int GlyphCount = 0, byte[]? Cmap = null);

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
    /// <summary>
    /// Rebuilds the outline tables with the glyphs numbered again from nothing, in the order
    /// given, so that a font holding a hundred glyphs is a hundred glyphs long.
    /// </summary>
    /// <param name="order">
    /// The glyphs to keep, in the order they will have. Position zero is <c>.notdef</c> and is
    /// added here; anything a kept glyph is built out of is appended to the end.
    /// </param>
    /// <param name="characters">What each glyph is reached by, for the rebuilt character map.</param>
    public static Tables? Renumber(
        TrueTypeFont font, IReadOnlyList<ushort> order,
        IReadOnlyList<(int CodePoint, ushort Glyph)> characters, bool dropHinting = false)
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

            // The numbering: position zero is .notdef, then the glyphs as they were asked for,
            // then whatever those are built out of.
            var numbering = new Dictionary<ushort, ushort> { [0] = 0 };
            var kept = new List<ushort> { 0 };

            foreach (var glyph in order)
            {
                if (glyph < font.GlyphCount && numbering.TryAdd(glyph, (ushort)kept.Count)) kept.Add(glyph);
            }

            for (var i = 0; i < kept.Count; i++)
            {
                foreach (var component in Components(source, glyf, offsets, kept[i]))
                {
                    if (component < font.GlyphCount && numbering.TryAdd(component, (ushort)kept.Count))
                        kept.Add(component);
                }
            }

            return RebuildRenumbered(font, source, glyf, offsets, kept, numbering, characters, dropHinting);
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    public static Tables? Build(TrueTypeFont font, IReadOnlyCollection<ushort> glyphs, bool dropHinting = false)
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
            var tables = Rebuild(source, glyf, offsets, wanted, font.GlyphCount, dropHinting);

            return TrimMetrics(font, wanted, tables);
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the kept glyphs in their new order, with everything that refers to a glyph by
    /// number brought along.
    /// </summary>
    /// <remarks>
    /// A composite names the glyphs it is built from by index, so those references are rewritten
    /// as the outline is copied. Everything else that is numbered by glyph — the locations, the
    /// metrics, the count in <c>maxp</c> — is simply shorter.
    /// </remarks>
    private static Tables RebuildRenumbered(
        TrueTypeFont font, byte[] source, TrueTypeFont.TableRecord glyf, int[] offsets,
        List<ushort> kept, Dictionary<ushort, ushort> numbering,
        IReadOnlyList<(int CodePoint, ushort Glyph)> characters, bool dropHinting)
    {
        var outlines = new MemoryStream();
        var loca = new byte[(kept.Count + 1) * 4];
        var hmtx = new byte[kept.Count * 4];

        for (var index = 0; index < kept.Count; index++)
        {
            WriteUInt32(loca, index * 4, (uint)outlines.Length);

            var glyph = kept[index];

            // Both metrics move with the glyph. The advance is what the text is spaced by; the
            // side bearing is where the outline sits against it, and a renderer positions the
            // points by the difference between it and the outline's own left edge — so a glyph
            // given a side bearing of zero draws shifted by however far in it used to start.
            var advance = font.GetAdvanceWidth(glyph);
            var bearing = LeftSideBearing(font, source, glyph);

            hmtx[index * 4] = (byte)(advance >> 8);
            hmtx[index * 4 + 1] = (byte)advance;
            hmtx[index * 4 + 2] = (byte)(bearing >> 8);
            hmtx[index * 4 + 3] = (byte)bearing;

            var start = offsets[glyph];
            var length = offsets[glyph + 1] - start;
            if (length <= 0) continue;

            var available = Math.Min(length, glyf.Length - start);
            if (available <= 0) continue;

            var outline = new ReadOnlySpan<byte>(source, glyf.Offset + start, available);
            if (dropHinting) outline = WithoutInstructions(outline);

            var copied = outline.ToArray();
            Renumber(copied, numbering);

            outlines.Write(copied);
        }

        WriteUInt32(loca, kept.Count * 4, (uint)outlines.Length);

        var cmap = BuildCmap(characters, numbering);

        return new Tables(outlines.ToArray(), loca, hmtx, kept.Count, kept.Count, cmap);
    }

    /// <summary>
    /// A glyph's left side bearing, from wherever the metrics table keeps it.
    /// </summary>
    /// <remarks>
    /// The table holds full metrics for the first so many glyphs and only side bearings for the
    /// rest, which is how a font with many glyphs of one width stays small.
    /// </remarks>
    private static short LeftSideBearing(TrueTypeFont font, byte[] source, ushort glyph)
    {
        if (!font.Tables.TryGetValue("hmtx", out var hmtx)) return 0;
        if (!font.Tables.TryGetValue("hhea", out var hhea)) return 0;

        int metrics = new BigEndianReader(source, hhea.Offset + 34).ReadUInt16();
        if (metrics == 0) return 0;

        var at = glyph < metrics
            ? hmtx.Offset + glyph * 4 + 2
            : hmtx.Offset + metrics * 4 + (glyph - metrics) * 2;

        return at + 2 <= source.Length && at + 2 <= hmtx.Offset + hmtx.Length
            ? new BigEndianReader(source, at).ReadInt16()
            : (short)0;
    }

    /// <summary>Rewrites the glyph numbers a composite refers to, in place.</summary>
    private static void Renumber(byte[] outline, Dictionary<ushort, ushort> numbering)
    {
        if (outline.Length < 10) return;
        if ((short)((outline[0] << 8) | outline[1]) >= 0) return;

        var at = 10;

        while (at + 4 <= outline.Length)
        {
            var flags = (outline[at] << 8) | outline[at + 1];
            var component = (ushort)((outline[at + 2] << 8) | outline[at + 3]);

            if (numbering.TryGetValue(component, out var renumbered))
            {
                outline[at + 2] = (byte)(renumbered >> 8);
                outline[at + 3] = (byte)renumbered;
            }

            at += 4;
            at += (flags & 0x0001) != 0 ? 4 : 2;

            if ((flags & 0x0008) != 0) at += 2;
            else if ((flags & 0x0040) != 0) at += 4;
            else if ((flags & 0x0080) != 0) at += 8;

            if ((flags & 0x0020) == 0) break;
        }
    }

    /// <summary>The glyphs a composite is built from, or nothing for a simple one.</summary>
    private static IEnumerable<ushort> Components(
        byte[] source, TrueTypeFont.TableRecord glyf, int[] offsets, ushort glyph)
    {
        var start = offsets[glyph];
        if (offsets[glyph + 1] - start < 10) yield break;

        var reader = new BigEndianReader(source, glyf.Offset + start);
        if (reader.ReadInt16() >= 0) yield break;

        reader.Skip(8);

        while (true)
        {
            var flags = reader.ReadUInt16();
            yield return reader.ReadUInt16();

            reader.Skip((flags & 0x0001) != 0 ? 4 : 2);

            if ((flags & 0x0008) != 0) reader.Skip(2);
            else if ((flags & 0x0040) != 0) reader.Skip(4);
            else if ((flags & 0x0080) != 0) reader.Skip(8);

            if ((flags & 0x0020) == 0) yield break;
        }
    }

    /// <summary>
    /// A character map for the renumbered font.
    /// </summary>
    /// <remarks>
    /// Nothing in the PDF reads it — with Identity-H the code in the content stream is the glyph
    /// number, and the text comes back through the ToUnicode map — but a font is expected to have
    /// one, and a renumbered font carrying the original's would point at glyphs that have moved.
    /// The grouped format is used because it can say what this needs to say in a few bytes.
    /// </remarks>
    private static byte[] BuildCmap(
        IReadOnlyList<(int CodePoint, ushort Glyph)> characters, Dictionary<ushort, ushort> numbering)
    {
        var entries = characters
            .Where(entry => entry.CodePoint > 0 && numbering.ContainsKey(entry.Glyph))
            .Select(entry => (entry.CodePoint, Glyph: numbering[entry.Glyph]))
            .GroupBy(entry => entry.CodePoint)
            .Select(group => group.First())
            .OrderBy(entry => entry.CodePoint)
            .ToList();

        // Runs of characters whose glyphs run alongside them collapse into one group.
        var groups = new List<(int First, int Last, ushort Glyph)>();

        foreach (var (codePoint, glyph) in entries)
        {
            if (groups.Count > 0)
            {
                var last = groups[^1];
                if (codePoint == last.Last + 1 && glyph == last.Glyph + (last.Last - last.First) + 1)
                {
                    groups[^1] = (last.First, codePoint, last.Glyph);
                    continue;
                }
            }

            groups.Add((codePoint, codePoint, glyph));
        }

        var subtable = new MemoryStream();
        WriteUInt16(subtable, 12);                       // format
        WriteUInt16(subtable, 0);                        // reserved
        WriteUInt32(subtable, (uint)(16 + groups.Count * 12));
        WriteUInt32(subtable, 0);                        // language
        WriteUInt32(subtable, (uint)groups.Count);

        foreach (var (first, last, glyph) in groups)
        {
            WriteUInt32(subtable, (uint)first);
            WriteUInt32(subtable, (uint)last);
            WriteUInt32(subtable, glyph);
        }

        var table = new MemoryStream();
        WriteUInt16(table, 0);                           // version
        WriteUInt16(table, 1);                           // one subtable
        WriteUInt16(table, 3);                           // Windows
        WriteUInt16(table, 10);                          // full Unicode
        WriteUInt32(table, 12);                          // where the subtable starts
        subtable.WriteTo(table);

        return table.ToArray();
    }

    private static void WriteUInt16(Stream output, int value)
    {
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
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
    /// wider form costs two bytes a glyph and removes the question — including the question of
    /// padding each outline out to an even boundary, which a reader otherwise reports as bytes it
    /// was given and did not need.
    /// </remarks>
    private static Tables Rebuild(
        byte[] source, TrueTypeFont.TableRecord glyf, int[] offsets,
        HashSet<ushort> wanted, int glyphCount, bool dropHinting)
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

            var outline = new ReadOnlySpan<byte>(source, glyf.Offset + start, available);

            if (dropHinting) outline = WithoutInstructions(outline);

            outlines.Write(outline);
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
    ///
    /// What every glyph does keep is a side bearing, two bytes of it, because the table's length
    /// is fixed by the glyph count and a reader that knows this will say so. An empty glyph has
    /// no side bearing to speak of, so theirs are zero.
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

        var metrics = keep * 4;
        if (hmtx.Offset + metrics > source.Length) return tables;

        // Full metrics as far as the last glyph kept, then a side bearing for each glyph after it.
        var trimmed = new byte[metrics + (font.GlyphCount - keep) * 2];
        if (trimmed.Length >= hmtx.Length) return tables;

        Array.Copy(source, hmtx.Offset, trimmed, 0, metrics);

        return tables with { Hmtx = trimmed, MetricCount = keep };
    }

    /// <summary>
    /// A glyph's outline with its hinting instructions taken out.
    /// </summary>
    /// <remarks>
    /// The instructions of a simple glyph sit between the contour ends and the points, with their
    /// own length before them; a composite says whether it has any in the flags of its last
    /// component. Nothing else in the outline refers to them, so removing them is a matter of
    /// finding where they start and saying there are none.
    ///
    /// Only the shapes are lost, never the outlines: hinting nudges points onto the pixel grid at
    /// small sizes on a low-resolution screen, and says nothing about where the curves go.
    /// </remarks>
    private static ReadOnlySpan<byte> WithoutInstructions(ReadOnlySpan<byte> outline)
    {
        if (outline.Length < 10) return outline;

        var contours = (short)((outline[0] << 8) | outline[1]);

        return contours >= 0
            ? SimpleWithoutInstructions(outline, contours)
            : CompositeWithoutInstructions(outline);
    }

    private static ReadOnlySpan<byte> SimpleWithoutInstructions(ReadOnlySpan<byte> outline, int contours)
    {
        // Ten bytes of header, then two per contour end, then the instruction length.
        var at = 10 + contours * 2;
        if (at + 2 > outline.Length) return outline;

        var length = (outline[at] << 8) | outline[at + 1];
        if (length == 0 || at + 2 + length > outline.Length) return outline;

        var result = new byte[outline.Length - length];

        outline[..(at + 2)].CopyTo(result);
        result[at] = 0;
        result[at + 1] = 0;
        outline[(at + 2 + length)..].CopyTo(result.AsSpan(at + 2));

        return result;
    }

    private static ReadOnlySpan<byte> CompositeWithoutInstructions(ReadOnlySpan<byte> outline)
    {
        var at = 10;
        var flagsAt = 0;

        while (true)
        {
            if (at + 4 > outline.Length) return outline;

            flagsAt = at;
            var flags = (outline[at] << 8) | outline[at + 1];
            at += 4; // the flags and the component

            at += (flags & 0x0001) != 0 ? 4 : 2; // the placement arguments

            if ((flags & 0x0008) != 0) at += 2;
            else if ((flags & 0x0040) != 0) at += 4;
            else if ((flags & 0x0080) != 0) at += 8;

            if ((flags & 0x0020) == 0)
            {
                // The last component says whether instructions follow it.
                if ((flags & 0x0100) == 0) return outline;
                break;
            }
        }

        if (at + 2 > outline.Length) return outline;

        var result = outline[..(at + 2)].ToArray();

        // Clearing the bit is what says there are none; the length that followed goes with them.
        // The flags are a sixteen-bit value, and this one lives in the upper byte.
        result[flagsAt] &= 0xfe;
        Array.Resize(ref result, at);

        return result;
    }

    private static void WriteUInt32(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }
}
