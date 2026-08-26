using n8PDF.Fonts.OpenType;

namespace n8PDF.Fonts.Aat;

/// <summary>
/// Apple's <c>kerx</c> table: where the glyphs go, for the faces that say it here rather than in
/// <c>GPOS</c>.
/// </summary>
/// <remarks>
/// Two quite different things live in the one table. Most of it is kerning — a pair drawn closer
/// together, either by naming the pair, by naming classes of glyphs, or by a state machine that
/// keeps a stack of the glyphs it has passed and adjusts several of them at once. The rest is
/// attachment: a state machine that marks a letter, and then fastens what follows to it by naming
/// a point on each, which is how these faces put a vowel sign on a consonant.
///
/// Kerning is applied only where the document asks for it, as everywhere else here; attachment is
/// applied always, because a mark that is not fastened is not merely unkerned but in the wrong
/// place.
/// </remarks>
internal sealed class ExtendedKerning
{
    private readonly byte[] _data;
    private readonly int _glyphCount;
    private readonly Anchors? _anchors;
    private readonly List<Subtable> _subtables = [];

    private ExtendedKerning(byte[] data, int glyphCount, Anchors? anchors)
    {
        _data = data;
        _glyphCount = glyphCount;
        _anchors = anchors;
    }

    private readonly record struct Subtable(int Format, int Offset, uint Coverage);

    public bool IsEmpty => _subtables.Count == 0;

    /// <summary>Whether any of it fastens one glyph to another rather than merely spacing them.</summary>
    public bool Attaches { get; private set; }

    public static ExtendedKerning? Read(byte[] data, int offset, int glyphCount, Anchors? anchors)
    {
        try
        {
            if (offset + 8 > data.Length) return null;

            var version = AatLookup.Read16(data, offset);
            if (version is < 2 or > 3) return null;

            var count = (int)AatLookup.Read32(data, offset + 4);

            var result = new ExtendedKerning(data, glyphCount, anchors);
            var at = offset + 8;

            for (var i = 0; i < count && at + 12 <= data.Length; i++)
            {
                var length = (int)AatLookup.Read32(data, at);
                var coverage = AatLookup.Read32(data, at + 4);

                if (length < 12) break;

                var format = (int)(coverage & 0xFF);

                result._subtables.Add(new Subtable(format, at, coverage));
                if (format == 4) result.Attaches = true;

                at += length;
            }

            return result.IsEmpty ? null : result;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException
                                     or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies the table.
    /// </summary>
    /// <param name="kerning">Whether the document asked for the pairs to be kerned.</param>
    /// <param name="classes">
    /// What the font says its glyphs are, where it says anything. Which glyphs a kerning table
    /// looks past is its answer and not Unicode's: a face that says nothing about its glyphs is
    /// taken to mean that none of them is a mark, and its pairs are kerned as they come.
    /// </param>
    public void Apply(List<ShapeItem> buffer, bool kerning, bool rightToLeft, GlyphClasses? classes)
    {
        foreach (var subtable in _subtables)
        {
            // What is meant for text set down the page is not for text set across it.
            if ((subtable.Coverage & 0x80000000) != 0) continue;

            var crossStream = (subtable.Coverage & 0x40000000) != 0;
            var attaches = subtable.Format == 4;

            if (!attaches && !kerning) continue;

            // Only the state machines are read in a direction; the tables of pairs and classes are
            // read as they are.
            var backwards = (subtable.Coverage & 0x10000000) != 0;

            // A table of pairs or of classes read backwards is not read at all: it says nothing
            // about which way round its pairs are, so there is nothing to turn round.
            if (backwards && subtable.Format is 0 or 2) continue;

            var reverse = backwards && subtable.Format is 1 or 4;
            if (reverse) buffer.Reverse();

            switch (subtable.Format)
            {
                case 0:
                    Pairs(buffer, subtable.Offset, crossStream, classes);
                    break;

                case 1:
                    Contextual(buffer, subtable.Offset, crossStream);
                    break;

                case 2:
                    Classes(buffer, subtable.Offset, crossStream, classes);
                    break;

                case 4:
                    Attach(buffer, subtable.Offset);
                    break;
            }

            if (reverse) buffer.Reverse();
        }
    }

    /// <summary>
    /// Moves a pair together, or apart, or across the line where the table says so.
    /// </summary>
    /// <remarks>
    /// A kern is shared between the two glyphs rather than taken out of the first one's advance:
    /// half goes to the glyph on the left and half to the one on the right, which is also moved by
    /// its half so that the pen and the ink agree. That is what these faces are drawn against —
    /// OpenType's own kerning is expressed the other way, as a shortening of the left glyph alone,
    /// and the two put the second glyph in different places while giving the pair the same width.
    /// </remarks>
    private static void Adjust(List<ShapeItem> buffer, int at, int next, int value, bool crossStream)
    {
        // Cross-stream kerning moves a glyph off the line rather than along it: what a face uses
        // to run a joined script up and down as it goes.
        if (crossStream)
        {
            buffer[next].YOffset += value;
            return;
        }

        var left = value >> 1;
        var right = value - left;

        buffer[at].Advance += left;
        buffer[next].Advance += right;
        buffer[next].XOffset += right;
    }

    // ----- kerning by naming the pairs -----

    /// <summary>
    /// The pairs a kerning table is asked about: each glyph and the next one that is not a mark.
    /// A vowel written over a letter does not come between that letter and the one after it, any
    /// more than it comes between them in the reading.
    /// </summary>
    private static IEnumerable<(int Left, int Right)> Adjacent(
        List<ShapeItem> buffer, GlyphClasses? classes)
    {
        // What the font says, where it says anything. Where it does not, a mark that takes up no
        // room of its own is passed over and one that does is not: a vowel written above a letter
        // stands between nothing, while one written beside it is as much a part of the line as the
        // letters are.
        bool Mark(int at) =>
            classes is { Classifies: true }
                ? classes.IsMark(buffer[at].Glyph)
                : System.Globalization.CharUnicodeInfo.GetUnicodeCategory(buffer[at].CodePoint)
                    is System.Globalization.UnicodeCategory.NonSpacingMark
                    or System.Globalization.UnicodeCategory.EnclosingMark;

        for (var i = 0; i < buffer.Count; i++)
        {
            if (Mark(i)) continue;

            var j = i + 1;
            while (j < buffer.Count && Mark(j)) j++;

            if (j >= buffer.Count) break;

            yield return (i, j);
        }
    }

    private void Pairs(List<ShapeItem> buffer, int offset, bool crossStream, GlyphClasses? classes)
    {
        var body = offset + 12;
        if (body + 8 > _data.Length) return;

        // How many pairs there are, and then three numbers for a search this does not need to be
        // told how to do. Each pair is two glyphs and a value: six bytes.
        var units = (int)AatLookup.Read32(_data, body);
        if (units <= 0) return;

        const int unitSize = 6;
        var at = body + 16;

        foreach (var (i, j) in Adjacent(buffer, classes))
        {
            var key = ((uint)buffer[i].Glyph << 16) | buffer[j].Glyph;

            var low = 0;
            var high = units - 1;

            while (low <= high)
            {
                var middle = (low + high) / 2;
                var entry = at + middle * unitSize;

                if (entry + 6 > _data.Length) break;

                var found = AatLookup.Read32(_data, entry);

                if (key < found) high = middle - 1;
                else if (key > found) low = middle + 1;
                else
                {
                    Adjust(buffer, i, j, AatLookup.ReadInt16(_data, entry + 4), crossStream);
                    break;
                }
            }
        }
    }

    // ----- kerning by naming classes of glyphs -----

    private void Classes(
        List<ShapeItem> buffer, int offset, bool crossStream, GlyphClasses? classes)
    {
        var body = offset + 12;
        if (body + 16 > _data.Length) return;

        var left = offset + (int)AatLookup.Read32(_data, body + 4);
        var right = offset + (int)AatLookup.Read32(_data, body + 8);
        var values = offset + (int)AatLookup.Read32(_data, body + 12);

        foreach (var (i, j) in Adjacent(buffer, classes))
        {
            // The classes are not numbers to be multiplied out: the one on the left is already the
            // distance to its row and the one on the right the distance along it.
            var row = AatLookup.Value(_data, left, buffer[i].Glyph, _glyphCount);
            var column = AatLookup.Value(_data, right, buffer[j].Glyph, _glyphCount);

            if (row is null || column is null) continue;

            // The two together are a place in the array counted in values rather than in bytes,
            // which is the one difference between this table and the older one it grew out of.
            var at = values + (row.Value + column.Value) * 2;
            if (at + 2 > _data.Length) continue;

            Adjust(buffer, i, j, AatLookup.ReadInt16(_data, at), crossStream);
        }
    }

    // ----- kerning decided by what came before -----

    private const ushort Push = 0x8000;
    private const ushort Reset = 0x2000;

    /// <summary>
    /// A machine that keeps a stack of the glyphs it has passed and, when it reaches the end of
    /// something it recognises, moves several of them at once.
    /// </summary>
    private void Contextual(List<ShapeItem> buffer, int offset, bool crossStream)
    {
        var body = offset + 12;

        if (StateMachine.Read(_data, body, _glyphCount, 1) is not { } machine) return;
        if (body + 20 > _data.Length) return;

        var values = offset + (int)AatLookup.Read32(_data, body + 16);

        var stack = new List<int>();

        machine.Run(buffer, (at, entry) =>
        {
            if ((entry.Flags & Reset) != 0) stack.Clear();

            if ((entry.Flags & Push) != 0)
            {
                if (stack.Count >= 8) stack.Clear();
                else stack.Add(at);
            }

            if (entry.First == 0xFFFF) return;

            // Here the index is counted in bytes, which is the other way round from the table of
            // classes and is the sort of thing only a comparison finds.
            var action = values + entry.First;

            // The values are taken one at a time off the top of the stack, and the last of them
            // says so in its lowest bit.
            while (stack.Count > 0)
            {
                if (action + 2 > _data.Length) return;

                var value = AatLookup.ReadInt16(_data, action);
                var last = (value & 1) != 0;

                var target = stack[^1];
                stack.RemoveAt(stack.Count - 1);

                // A machine's own kerning is not shared between the pair: the whole of it moves
                // the glyph that was popped, pen and ink together.
                if (target < buffer.Count && !crossStream)
                {
                    buffer[target].Advance += value & ~1;
                    buffer[target].XOffset += value & ~1;
                }

                action += 2;
                if (last) break;
            }
        });
    }

    // ----- fastening one glyph to another -----

    private const ushort Mark = 0x8000;

    /// <summary>
    /// Fastens a glyph to the one marked before it, by naming a point on each. The two points are
    /// brought together, which moves the second glyph and not the pen.
    /// </summary>
    private void Attach(List<ShapeItem> buffer, int offset)
    {
        var body = offset + 12;

        if (StateMachine.Read(_data, body, _glyphCount, 1) is not { } machine) return;
        if (body + 20 > _data.Length) return;

        var flags = AatLookup.Read32(_data, body + 16);

        var kind = (int)(flags >> 30);

        // This one offset is measured from the state table rather than from the subtable, which
        // is the sort of thing that is only ever found out by trying it.
        var data = body + (int)(flags & 0x00FFFFFF);

        var mark = 0;
        var marked = false;

        machine.Run(buffer, (at, entry) =>
        {
            if (marked && entry.First != 0xFFFF && at < buffer.Count && mark < buffer.Count)
            {
                var action = data + entry.First * 2;

                if (Offset(kind, action, buffer[mark].Glyph, buffer[at].Glyph) is { } moved)
                {
                    buffer[at].XOffset = moved.X;
                    buffer[at].YOffset = moved.Y;

                    // Where it hangs from, so that the movement can be measured from the letter
                    // rather than from wherever the pen happened to be.
                    buffer[at].AttachedTo = mark - at;
                    buffer[at].AttachedToMark = false;
                }
            }

            if ((entry.Flags & Mark) == 0) return;

            marked = true;
            mark = Math.Min(at, buffer.Count - 1);
        });
    }

    /// <summary>
    /// How far the second glyph moves, by whichever of the three ways of saying it the table uses.
    /// </summary>
    private (int X, int Y)? Offset(int kind, int action, ushort marked, ushort current)
    {
        switch (kind)
        {
            // Points named in the anchor table, which is what every face here uses.
            case 1:
            {
                if (_anchors is null || action + 4 > _data.Length) return null;

                var onMarked = _anchors.Point(marked, AatLookup.Read16(_data, action));
                var onCurrent = _anchors.Point(current, AatLookup.Read16(_data, action + 2));

                if (onMarked is null || onCurrent is null) return null;

                return (onMarked.Value.X - onCurrent.Value.X, onMarked.Value.Y - onCurrent.Value.Y);
            }

            // Coordinates given outright.
            case 2:
            {
                if (action + 8 > _data.Length) return null;

                var markedX = AatLookup.ReadInt16(_data, action);
                var markedY = AatLookup.ReadInt16(_data, action + 2);
                var currentX = AatLookup.ReadInt16(_data, action + 4);
                var currentY = AatLookup.ReadInt16(_data, action + 6);

                return (markedX - currentX, markedY - currentY);
            }

            // Points on the outlines themselves, which would mean reading the glyphs to find out
            // where they are. No face on this machine asks for it.
            default:
                return null;
        }
    }
}
