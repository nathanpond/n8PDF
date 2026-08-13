using n8PDF.Fonts.OpenType;

namespace n8PDF.Fonts.Aat;

/// <summary>
/// Apple's <c>morx</c> table: what a font does to a run of glyphs, said as state machines rather
/// than as lookups.
/// </summary>
/// <remarks>
/// Some faces describe their shaping only this way and carry no OpenType tables at all — on this
/// machine that is a hundred and sixty of them, including Devanagari MT, Gurmukhi MT, Gujarati MT,
/// Thonburi and the whole Helvetica family. A converter that reads only OpenType draws those
/// scripts as rows of unjoined letters, and draws Latin without the ligatures the face was drawn
/// with.
///
/// The table is a list of chains; each chain is a set of features and a list of subtables, and a
/// subtable runs if any of the flags it names are on. Which flags are on is decided by the chain's
/// defaults together with whatever features the text asks for — and a run of ordinary text asks
/// for nothing, so the defaults are what nearly everything gets.
///
/// Five kinds of subtable: one that rearranges glyphs, one that swaps a glyph according to what
/// was seen before it, one that writes several as one, one that swaps a glyph whatever its
/// company, and one that inserts glyphs. The first, second, third and fifth are state machines and
/// share the machinery in <see cref="StateMachine"/>.
/// </remarks>
internal sealed class Metamorphosis
{
    private readonly byte[] _data;
    private readonly int _glyphCount;
    private readonly List<Subtable> _subtables = [];

    private Metamorphosis(byte[] data, int glyphCount)
    {
        _data = data;
        _glyphCount = glyphCount;
    }

    private readonly record struct Subtable(int Kind, int Offset, int Length, uint Coverage);

    public bool IsEmpty => _subtables.Count == 0;

    /// <summary>Reads the table, or gives back null where the font has nothing to say.</summary>
    public static Metamorphosis? Read(byte[] data, int offset, int length, int glyphCount)
    {
        try
        {
            if (offset + 8 > data.Length) return null;

            var version = AatLookup.Read16(data, offset);
            if (version is < 2 or > 3) return null;

            var chains = (int)AatLookup.Read32(data, offset + 4);

            var result = new Metamorphosis(data, glyphCount);
            var at = offset + 8;

            for (var chain = 0; chain < chains && at + 16 <= data.Length; chain++)
            {
                var flags = AatLookup.Read32(data, at);
                var chainLength = (int)AatLookup.Read32(data, at + 4);
                var features = (int)AatLookup.Read32(data, at + 8);
                var subtables = (int)AatLookup.Read32(data, at + 12);

                // The features say which flags a document may turn on and off. Nothing here asks
                // for any of them, so what is left is what the chain turns on by itself.
                var body = at + 16 + features * 12;

                for (var i = 0; i < subtables && body + 12 <= data.Length; i++)
                {
                    var subtableLength = (int)AatLookup.Read32(data, body);
                    var coverage = AatLookup.Read32(data, body + 4);
                    var subFeatures = AatLookup.Read32(data, body + 8);

                    if (subtableLength < 12) break;

                    if ((subFeatures & flags) != 0)
                    {
                        result._subtables.Add(new Subtable((int)(coverage & 0xFF), body + 12,
                            subtableLength - 12, coverage));
                    }

                    body += subtableLength;
                }

                if (chainLength <= 0) break;
                at += chainLength;
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
    /// Applies every subtable that is on, in the order the font lists them.
    /// </summary>
    /// <param name="rightToLeft">
    /// Whether the run is drawn from the right. A subtable says which way it wants to be read, and
    /// a run that is drawn backwards is handed to it backwards unless it says otherwise.
    /// </param>
    public void Apply(List<ShapeItem> buffer, bool rightToLeft)
    {
        foreach (var subtable in _subtables)
        {
            // A subtable meant for text set down the page is not for text set across it.
            var vertical = (subtable.Coverage & 0x80000000) != 0;
            var anyDirection = (subtable.Coverage & 0x20000000) != 0;

            if (!anyDirection && vertical) continue;

            // Which way round a subtable wants to read the run. One flag says it reads backwards,
            // another says it reads in the order the text is stored rather than the order it is
            // drawn; and where a subtable says nothing about the order it is stored in, reading it
            // backwards is the same thing as the line running from the right.
            var descending = (subtable.Coverage & 0x40000000) != 0;
            var logical = (subtable.Coverage & 0x10000000) != 0;

            var backwards = logical ? descending : descending != rightToLeft;

            if (backwards) buffer.Reverse();

            switch (subtable.Kind)
            {
                case 0:
                    Rearrange(buffer, subtable.Offset);
                    break;

                case 1:
                    Contextual(buffer, subtable.Offset);
                    break;

                case 2:
                    Ligatures(buffer, subtable.Offset);
                    break;

                case 4:
                    Swap(buffer, subtable.Offset);
                    break;

                case 5:
                    Insert(buffer, subtable.Offset);
                    break;
            }

            if (backwards) buffer.Reverse();


            // What a machine marked as gone goes before the next one runs. A machine that wrote
            // two glyphs as one leaves the other where it was; leaving it there would put it
            // between the pair the next machine is looking for, and the run would come out of the
            // end with its shapes half made.
            buffer.RemoveAll(item => item.Glyph == StateMachine.DeletedGlyph);
        }
    }

    // ----- one glyph for another, whatever its company -----

    private void Swap(List<ShapeItem> buffer, int offset)
    {
        foreach (var item in buffer)
        {
            if (AatLookup.Value(_data, offset, item.Glyph, _glyphCount) is not { } glyph) continue;
            if (glyph == item.Glyph) continue;

            item.Glyph = (ushort)glyph;
            item.Substituted = true;
        }
    }

    // ----- moving glyphs about -----

    private const ushort MarkFirst = 0x8000;
    private const ushort MarkLast = 0x2000;
    private const ushort Verb = 0x000F;

    /// <summary>
    /// Moves up to two glyphs from each end of a marked range to the other end, possibly turning
    /// them round: how these fonts write a vowel that is stored after its consonant and drawn
    /// before it.
    /// </summary>
    private void Rearrange(List<ShapeItem> buffer, int offset)
    {
        if (StateMachine.Read(_data, offset, _glyphCount, 0) is not { } machine) return;

        var start = 0;
        var end = 0;

        machine.Run(buffer, (at, entry) =>
        {
            if ((entry.Flags & MarkFirst) != 0) start = at;
            if ((entry.Flags & MarkLast) != 0) end = Math.Min(at + 1, buffer.Count);

            var verb = entry.Flags & Verb;
            if (verb == 0 || start >= end) return;

            // Each verb says how many glyphs to move from the front to the back and from the back
            // to the front, and whether either pair is turned round on the way.
            var moves = new byte[]
            {
                0x00, 0x10, 0x01, 0x11, 0x20, 0x30, 0x02, 0x03,
                0x12, 0x13, 0x21, 0x31, 0x22, 0x32, 0x23, 0x33
            }[verb];

            var left = Math.Min(2, moves >> 4);
            var right = Math.Min(2, moves & 0x0F);

            var reverseLeft = (moves >> 4) == 3;
            var reverseRight = (moves & 0x0F) == 3;

            if (end - start < left + right) return;

            var head = buffer.GetRange(start, left);
            var tail = buffer.GetRange(end - right, right);

            var middle = buffer.GetRange(start + left, end - start - left - right);

            if (reverseLeft) head.Reverse();
            if (reverseRight) tail.Reverse();

            var rearranged = new List<ShapeItem>(end - start);
            rearranged.AddRange(tail);
            rearranged.AddRange(middle);
            rearranged.AddRange(head);

            for (var i = 0; i < rearranged.Count; i++) buffer[start + i] = rearranged[i];
        });
    }

    // ----- one glyph for another, given what came before it -----

    private const ushort SetMark = 0x8000;

    private void Contextual(List<ShapeItem> buffer, int offset)
    {
        if (StateMachine.Read(_data, offset, _glyphCount, 2) is not { } machine) return;
        if (offset + 20 > _data.Length) return;

        // Where the tables of replacements live: an array of offsets, each to a lookup.
        var tables = offset + (int)AatLookup.Read32(_data, offset + 16);

        var mark = 0;
        var marked = false;

        machine.Run(buffer, (at, entry) =>
        {
            // A run's end substitutes nothing unless something was marked, which is what the
            // system these fonts were drawn against does.
            if (at >= buffer.Count && !marked) return;

            if (entry.First != 0xFFFF && mark < buffer.Count)
                Replace(buffer[mark], Table(tables, entry.First));

            var current = Math.Min(at, buffer.Count - 1);

            if (entry.Second != 0xFFFF && current >= 0)
                Replace(buffer[current], Table(tables, entry.Second));

            if ((entry.Flags & SetMark) == 0) return;

            marked = true;
            mark = Math.Min(at, Math.Max(0, buffer.Count - 1));
        });

        return;

        int Table(int at, int index)
        {
            var entry = at + index * 4;

            return entry + 4 <= _data.Length ? at + (int)AatLookup.Read32(_data, entry) : 0;
        }

        void Replace(ShapeItem item, int lookup)
        {
            if (lookup <= 0) return;
            if (AatLookup.Value(_data, lookup, item.Glyph, _glyphCount) is not { } glyph) return;
            if (glyph == 0 || glyph == item.Glyph) return;

            item.Glyph = (ushort)glyph;
            item.Substituted = true;
        }
    }

    // ----- several glyphs written as one -----

    private const ushort SetComponent = 0x8000;
    private const ushort PerformAction = 0x2000;

    private const uint LastAction = 0x80000000;
    private const uint StoreAction = 0x40000000;
    private const uint ActionOffset = 0x3FFFFFFF;

    /// <summary>
    /// Writes several glyphs as one. The machine marks the components as it goes; when it reaches
    /// the end of a match it walks a list of actions, one for each marked glyph from the last
    /// backwards, adding up an index into the table of ligatures as it goes. What comes out is one
    /// glyph in place of the first component and nothing in place of the rest.
    /// </summary>
    private void Ligatures(List<ShapeItem> buffer, int offset)
    {
        if (StateMachine.Read(_data, offset, _glyphCount, 1) is not { } machine) return;
        if (offset + 28 > _data.Length) return;

        var actions = offset + (int)AatLookup.Read32(_data, offset + 16);
        var components = offset + (int)AatLookup.Read32(_data, offset + 20);
        var ligatures = offset + (int)AatLookup.Read32(_data, offset + 24);

        var marked = new List<int>();

        machine.Run(buffer, (at, entry) =>
        {
            if ((entry.Flags & SetComponent) != 0)
            {
                // Never mark the same glyph twice, which a machine that stays put would do.
                if (marked.Count > 0 && marked[^1] == at) marked.RemoveAt(marked.Count - 1);

                marked.Add(at);
            }

            if ((entry.Flags & PerformAction) == 0) return;
            if (marked.Count == 0 || at >= buffer.Count) return;

            var cursor = marked.Count;
            var action = actions + entry.First * 4;
            var ligature = 0u;

            while (true)
            {
                if (cursor == 0)
                {
                    marked.Clear();
                    break;
                }

                if (action + 4 > _data.Length) break;

                var step = AatLookup.Read32(_data, action);

                // The offset is a signed thirty-bit number added to the glyph, which lands in the
                // table of components.
                var raw = step & ActionOffset;
                if ((raw & 0x20000000) != 0) raw |= 0xC0000000;

                var position = marked[--cursor];

                // The glyph plus the offset is the place in the table of components: an index
                // rather than a distance in bytes, which is what the older form of this table
                // held and what makes the two easy to confuse.
                var component = buffer[position].Glyph + (int)raw;
                if (component < 0) break;

                var index = components + component * 2;
                if (index + 2 > _data.Length) break;

                ligature += AatLookup.Read16(_data, index);

                if ((step & (StoreAction | LastAction)) != 0)
                {
                    var found = ligatures + (int)ligature * 2;

                    if (found + 2 > _data.Length) break;

                    buffer[position].Glyph = AatLookup.Read16(_data, found);
                    buffer[position].Substituted = true;
                    buffer[position].Ligated = true;

                    // Everything the ligature was made of after this one is gone — and the shape
                    // that is left stands for all of their characters, so that a word drawn as one
                    // glyph can still be searched for and copied out as the word.
                    var merged = new List<int>(buffer[position].Merged ?? [buffer[position].Cluster]);

                    while (marked.Count - 1 > cursor)
                    {
                        var gone = buffer[marked[^1]];

                        merged.AddRange(gone.Merged ?? [gone.Cluster]);

                        gone.Glyph = StateMachine.DeletedGlyph;
                        marked.RemoveAt(marked.Count - 1);
                    }

                    merged.Sort();
                    buffer[position].Merged = [.. merged];

                    ligature = 0;
                }

                if ((step & LastAction) != 0) break;

                action += 4;
            }
        });
    }

    // ----- glyphs put in that were never in the text -----

    private const ushort CurrentInsertBefore = 0x0800;
    private const ushort MarkedInsertBefore = 0x0400;
    private const ushort CurrentInsertCount = 0x03E0;
    private const ushort MarkedInsertCount = 0x001F;

    /// <summary>
    /// Puts glyphs into the run that the text never held: how these fonts write the piece of a
    /// letter that has no character of its own.
    /// </summary>
    private void Insert(List<ShapeItem> buffer, int offset)
    {
        if (StateMachine.Read(_data, offset, _glyphCount, 2) is not { } machine) return;
        if (offset + 20 > _data.Length) return;

        var list = offset + (int)AatLookup.Read32(_data, offset + 16);

        var mark = 0;

        machine.Run(buffer, (at, entry) =>
        {
            var marked = entry.Flags & MarkedInsertCount;
            var current = (entry.Flags & CurrentInsertCount) >> 5;

            // The marked glyph first, so that inserting before the current one does not move it.
            if (marked > 0 && entry.First != 0xFFFF)
            {
                var to = mark + ((entry.Flags & MarkedInsertBefore) != 0 ? 0 : 1);
                Put(to, entry.First, marked);
            }

            if (current > 0 && entry.Second != 0xFFFF)
            {
                var to = Math.Min(at, buffer.Count) +
                         ((entry.Flags & CurrentInsertBefore) != 0 ? 0 : 1);

                Put(Math.Min(to, buffer.Count), entry.Second, current);
            }

            if ((entry.Flags & SetMark) != 0) mark = at;
        });

        return;

        void Put(int at, int from, int count)
        {
            if (at < 0 || at > buffer.Count) return;

            var neighbour = buffer[Math.Min(at, buffer.Count - 1)];

            for (var i = 0; i < count; i++)
            {
                var entry = list + (from + i) * 2;
                if (entry + 2 > _data.Length) return;

                var glyph = AatLookup.Read16(_data, entry);

                // What is put in stands for no character of its own: it belongs to the one beside
                // it, and is read back as nothing.
                buffer.Insert(at + i, new ShapeItem(glyph, neighbour.Cluster, neighbour.Mask)
                {
                    Standing = string.Empty,
                    Substituted = true
                });
            }
        }
    }
}
