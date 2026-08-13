namespace n8PDF.Fonts.OpenType;

/// <summary>
/// Applies a font's <c>GPOS</c> lookups: where each glyph goes once it is settled what they are.
/// </summary>
/// <remarks>
/// Four things happen here. A glyph may be nudged on its own; a pair may be drawn closer together,
/// which is kerning; a mark may be fastened to the letter it belongs to, or to the mark already on
/// it; and any of the three may be conditional on what stands around, which is the same contextual
/// machinery substitution uses.
///
/// Attachment is recorded rather than applied as it is found. A mark is placed against a glyph
/// which may itself still be moved — a mark on a mark on a letter is three glyphs deep — so each
/// remembers what it hangs from, and the offsets are added up once at the end, in the order that
/// puts every mark on top of a base that has already stopped moving.
/// </remarks>
internal sealed class Positioner(LayoutTable table, GlyphClasses? classes)
    : LookupEngine(table, classes)
{
    protected override int ExtensionType => 9;

    protected override int Apply(
        List<ShapeItem> buffer, int at, int type, int offset, Lookup lookup, int depth) =>
        type switch
        {
            1 => SingleAdjustment(buffer, at, offset),
            2 => PairAdjustment(buffer, at, offset, lookup),
            4 => MarkAttachment(buffer, at, offset, lookup, MarkTarget.Base),
            5 => MarkAttachment(buffer, at, offset, lookup, MarkTarget.Ligature),
            6 => MarkAttachment(buffer, at, offset, lookup, MarkTarget.Mark),
            7 => Contextual(buffer, at, offset, lookup, depth),
            8 => Chaining(buffer, at, offset, lookup, depth),
            _ => at
        };

    private enum MarkTarget
    {
        Base,
        Ligature,
        Mark
    }

    // ----- value records -----

    private static int ValueSize(int format)
    {
        var size = 0;
        for (var bit = 0; bit < 8; bit++)
        {
            if ((format & (1 << bit)) != 0) size += 2;
        }

        return size;
    }

    /// <summary>
    /// One value record applied to a glyph. The device tables the last four bits point at are a
    /// hinting refinement that means nothing at the sizes a PDF is drawn at.
    /// </summary>
    private static void ApplyValue(byte[] data, int offset, int format, ShapeItem item)
    {
        var reader = new BigEndianReader(data, offset);

        if ((format & 0x0001) != 0) item.XOffset += reader.ReadInt16();
        if ((format & 0x0002) != 0) item.YOffset += reader.ReadInt16();
        if ((format & 0x0004) != 0) item.Advance += reader.ReadInt16();
    }

    // ----- the adjustments themselves -----

    private int SingleAdjustment(List<ShapeItem> buffer, int at, int offset)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        int coverage = reader.ReadUInt16();
        int valueFormat = reader.ReadUInt16();

        var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
        if (index < 0) return at;

        if (format == 1)
        {
            ApplyValue(data, offset + 6, valueFormat, buffer[at]);
            return at + 1;
        }

        if (format != 2) return at;

        int count = reader.ReadUInt16();
        if (index >= count) return at;

        ApplyValue(data, offset + 8 + index * ValueSize(valueFormat), valueFormat, buffer[at]);

        return at + 1;
    }

    private int PairAdjustment(List<ShapeItem> buffer, int at, int offset, Lookup lookup)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        int coverage = reader.ReadUInt16();
        int firstFormat = reader.ReadUInt16();
        int secondFormat = reader.ReadUInt16();

        var index = LayoutReaders.CoverageIndex(data, offset + coverage, buffer[at].Glyph);
        if (index < 0) return at;

        var second = Next(buffer, at, lookup);
        if (second >= buffer.Count) return at;

        var firstSize = ValueSize(firstFormat);
        var secondSize = ValueSize(secondFormat);

        if (format == 1)
        {
            int setCount = reader.ReadUInt16();
            if (index >= setCount) return at;

            var set = offset + LayoutReaders.ReadUInt16At(data, offset + 10 + index * 2);
            int pairs = LayoutReaders.ReadUInt16At(data, set);

            var stride = 2 + firstSize + secondSize;

            // The pairs are sorted by the second glyph, and a font that kerns a common letter
            // lists hundreds of them.
            var low = 0;
            var high = pairs - 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                var entry = set + 2 + middle * stride;
                var glyph = LayoutReaders.ReadUInt16At(data, entry);

                if (buffer[second].Glyph < glyph) high = middle - 1;
                else if (buffer[second].Glyph > glyph) low = middle + 1;
                else
                {
                    ApplyValue(data, entry + 2, firstFormat, buffer[at]);
                    ApplyValue(data, entry + 2 + firstSize, secondFormat, buffer[second]);

                    // Where the second glyph was adjusted too, it has had its turn.
                    return secondSize > 0 ? second + 1 : second;
                }
            }

            return at;
        }

        if (format != 2) return at;

        var firstClasses = offset + reader.ReadUInt16();
        var secondClasses = offset + reader.ReadUInt16();

        int firstCount = reader.ReadUInt16();
        int secondCount = reader.ReadUInt16();

        var firstClass = LayoutReaders.ClassOf(data, firstClasses, buffer[at].Glyph);
        var secondClass = LayoutReaders.ClassOf(data, secondClasses, buffer[second].Glyph);

        if (firstClass >= firstCount || secondClass >= secondCount) return at;

        var record = offset + 16 +
                     (firstClass * secondCount + secondClass) * (firstSize + secondSize);

        ApplyValue(data, record, firstFormat, buffer[at]);
        ApplyValue(data, record + firstSize, secondFormat, buffer[second]);

        return secondSize > 0 ? second + 1 : second;
    }

    /// <summary>
    /// Fastens a mark to what it is written on: a letter, one letter of a shape standing for
    /// several, or the mark already there.
    /// </summary>
    private int MarkAttachment(
        List<ShapeItem> buffer, int at, int offset, Lookup lookup, MarkTarget target)
    {
        var data = Table.Data;
        var reader = new BigEndianReader(data, offset);

        int format = reader.ReadUInt16();
        if (format != 1) return at;

        int markCoverage = reader.ReadUInt16();
        int baseCoverage = reader.ReadUInt16();
        int classCount = reader.ReadUInt16();
        int markArray = offset + reader.ReadUInt16();
        int baseArray = offset + reader.ReadUInt16();

        var markIndex = LayoutReaders.CoverageIndex(data, offset + markCoverage, buffer[at].Glyph);
        if (markIndex < 0) return at;

        // What it is written on. A mark on a mark is written on whatever stands immediately before
        // it that the lookup can see; anything else is written on the nearest thing before it that
        // is not a mark, however many marks are already there.
        var on = at - 1;

        if (target == MarkTarget.Mark)
        {
            while (on >= 0 && Skipped(lookup, buffer[on])) on--;
            if (on < 0 || Classes?.IsMark(buffer[on].Glyph) != true) return at;
        }
        else
        {
            while (on >= 0 && (Classes?.IsMark(buffer[on].Glyph) ?? false)) on--;
            if (on < 0) return at;
        }

        var baseIndex = LayoutReaders.CoverageIndex(data, offset + baseCoverage, buffer[on].Glyph);
        if (baseIndex < 0) return at;

        int markClass = LayoutReaders.ReadUInt16At(data, markArray + 2 + markIndex * 4);
        if (markClass >= classCount) return at;

        var markAnchorOffset = LayoutReaders.ReadUInt16At(data, markArray + 4 + markIndex * 4);
        if (markAnchorOffset == 0) return at;

        if (LayoutReaders.Anchor(data, markArray + markAnchorOffset) is not { } mark) return at;

        // Where on the other glyph this kind of mark goes. A ligature says it once for each letter
        // it stands for, and which of those a mark wants is settled by which letter it was written
        // over.
        int anchorOffset;

        if (target == MarkTarget.Ligature)
        {
            var attachOffset = LayoutReaders.ReadUInt16At(data, baseArray + 2 + baseIndex * 2);
            if (attachOffset == 0) return at;

            var attach = baseArray + attachOffset;
            int components = LayoutReaders.ReadUInt16At(data, attach);
            if (components == 0) return at;

            var component = Math.Clamp(buffer[at].Component, 0, components - 1);

            anchorOffset = LayoutReaders.ReadUInt16At(
                data, attach + 2 + (component * classCount + markClass) * 2);

            if (anchorOffset == 0) return at;

            if (LayoutReaders.Anchor(data, attach + anchorOffset) is not { } onLigature) return at;

            Attach(buffer, at, on, onLigature, mark, target);

            return at + 1;
        }

        anchorOffset = LayoutReaders.ReadUInt16At(
            data, baseArray + 2 + (baseIndex * classCount + markClass) * 2);

        if (anchorOffset == 0) return at;

        if (LayoutReaders.Anchor(data, baseArray + anchorOffset) is not { } place) return at;

        Attach(buffer, at, on, place, mark, target);

        return at + 1;
    }

    private static void Attach(
        List<ShapeItem> buffer, int at, int on, (short X, short Y) place, (short X, short Y) mark,
        MarkTarget target)
    {
        var item = buffer[at];

        item.XOffset = place.X - mark.X;
        item.YOffset = place.Y - mark.Y;
        item.AttachedTo = on - at;
        item.AttachedToMark = target == MarkTarget.Mark;
    }

    /// <summary>
    /// Adds up what everything is attached to.
    /// </summary>
    /// <remarks>
    /// A mark's offset as the font gives it is measured from the glyph it hangs from, and the pen
    /// does not stand there: it has moved on by everything drawn since, or has yet to reach it
    /// where the line runs the other way. That distance is added here, once the whole run is
    /// placed, and a mark hanging from a mark takes its neighbour's answer with it — which is why
    /// this walks in the order it does rather than in the order the marks were found.
    /// </remarks>
    public static void Resolve(List<ShapeItem> buffer, bool rightToLeft)
    {
        for (var i = 0; i < buffer.Count; i++) Resolve(buffer, i, rightToLeft, 0);
    }

    private static void Resolve(List<ShapeItem> buffer, int at, bool rightToLeft, int depth)
    {
        var item = buffer[at];

        if (item.AttachedTo == 0 || depth > 8) return;

        var on = at + item.AttachedTo;
        if (on < 0 || on >= buffer.Count) return;

        // Whatever it hangs from must have stopped moving first.
        Resolve(buffer, on, rightToLeft, depth + 1);

        item.XOffset += buffer[on].XOffset;
        item.YOffset += buffer[on].YOffset;

        if (rightToLeft)
        {
            for (var k = on + 1; k <= at; k++) item.XOffset += buffer[k].Advance;
        }
        else
        {
            for (var k = on; k < at; k++) item.XOffset -= buffer[k].Advance;
        }

        item.AttachedTo = 0;
    }
}
