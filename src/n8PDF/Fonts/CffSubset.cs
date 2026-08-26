namespace n8PDF.Fonts;

/// <summary>
/// Rebuilds a CFF font program to hold only the charstrings a document used.
/// </summary>
/// <remarks>
/// A PostScript-outline face keeps its glyphs in a <c>CFF </c> table rather than in <c>glyf</c>,
/// so the TrueType subsetter cannot reach them. The idea is the same — keep the glyphs that were
/// drawn, empty the rest, and leave the numbering alone so that nothing else in the PDF has to be
/// rewritten — but the format is a nest of offsets rather than a table of them, and every offset
/// that moves has to be rewritten.
///
/// The subroutines go the same way as the glyphs. A subroutine is a piece of charstring pulled
/// out because several glyphs share it, and finding which ones survive means running the
/// charstrings that survive: which subroutine a call reaches depends on a bias worked out from
/// how many there are, and stepping over a hint mask means having counted the stems before it. In
/// a CID-keyed font that is done against each font dictionary's own set, which is what FDSelect
/// is for. They are most of what a Chinese subset would otherwise weigh.
///
/// What is kept whole: the name and string indexes, the charset, the encoding, and the private
/// dictionaries themselves.
/// </remarks>
internal static class CffSubset
{
    /// <summary>A charstring that draws nothing: the endchar operator by itself.</summary>
    private static readonly byte[] Empty = [14];

    /// <summary>A subroutine that does nothing: the return operator by itself.</summary>
    private static readonly byte[] Return = [11];

    /// <summary>
    /// Builds a subset of a CFF table, or returns null when it is not one this understands — in
    /// which case the whole table is embedded, which is correct if larger.
    /// </summary>
    public static byte[]? Build(byte[] source, int offset, int length, IReadOnlyCollection<ushort> glyphs)
    {
        try
        {
            var cff = new byte[length];
            Array.Copy(source, offset, cff, 0, length);

            return Rebuild(cff, glyphs);
        }
        catch (Exception e) when (e is FontFormatException or IndexOutOfRangeException
                                     or ArgumentOutOfRangeException or OverflowException
                                     or InvalidOperationException)
        {
            return null;
        }
    }

    private static byte[]? Rebuild(byte[] cff, IReadOnlyCollection<ushort> glyphs)
    {
        if (cff.Length < 4) return null;

        var headerSize = cff[2];
        if (headerSize < 4 || headerSize > cff.Length) return null;

        var names = ReadIndex(cff, headerSize);
        var topDicts = ReadIndex(cff, names.Limit);
        var strings = ReadIndex(cff, topDicts.Limit);
        var globalSubrs = ReadIndex(cff, strings.Limit);

        if (topDicts.Count != 1) return null;

        var top = ParseDict(cff, topDicts.Start(0), topDicts.End(0));

        // A font with no charstrings, or one whose outlines are somewhere unexpected, is left
        // alone rather than guessed at.
        if (!top.TryGetValue(17, out var charStringsOperands) || charStringsOperands.Count < 1) return null;

        var charStrings = ReadIndex(cff, (int)charStringsOperands[^1]);
        if (charStrings.Count == 0) return null;

        var wanted = new HashSet<ushort> { 0 };
        foreach (var glyph in glyphs)
        {
            if (glyph < charStrings.Count) wanted.Add(glyph);
        }

        // The pieces the top dictionary points at, each copied whole and given a new home.
        var charset = Region(cff, top, 15, charStrings.Count);
        var encoding = Region(cff, top, 16, charStrings.Count);
        var fdSelect = Region(cff, top, 0xc25, charStrings.Count);

        var privateBlock = PrivateBlock(cff, top);
        var fdArray = FontDictionaries(cff, top);

        // Which subroutines the glyphs being kept actually reach. Everything else in those
        // indexes can be emptied along with the glyphs.
        MarkSubroutines(cff, top, charStrings, globalSubrs, wanted, privateBlock, fdArray, out var globalUsed);

        var outlines = BuildCharStrings(cff, charStrings, wanted);

        return Write(cff, headerSize, names, topDicts, strings, globalSubrs, globalUsed, top,
            outlines, charset, encoding, fdSelect, privateBlock, fdArray);
    }

    /// <summary>
    /// Runs every charstring being kept, marking the subroutines it and they reach.
    /// </summary>
    /// <remarks>
    /// In a CID-keyed font the local subroutines a glyph may call are those of its own font
    /// dictionary, which is what FDSelect says — so the glyphs are scanned against the right set
    /// rather than against all of them at once.
    /// </remarks>
    private static void MarkSubroutines(
        byte[] cff, Dictionary<int, List<double>> top, CffIndex charStrings, CffIndex globalSubrs,
        HashSet<ushort> wanted, PrivateInfo? privateBlock,
        List<(byte[] Dict, PrivateInfo Private)>? fdArray, out HashSet<int> globalUsed)
    {
        var scanner = new SubroutineScanner(cff, globalSubrs);
        var fdSelect = fdArray is { Count: > 0 } ? ReadFdSelect(cff, top, charStrings.Count) : null;

        foreach (var glyph in wanted)
        {
            var owner = privateBlock;

            if (fdArray is { Count: > 0 })
            {
                var fd = fdSelect is not null && glyph < fdSelect.Length ? fdSelect[glyph] : 0;
                if (fd < 0 || fd >= fdArray.Count) continue;

                owner = fdArray[fd].Private;
            }

            scanner.Scan(charStrings.Start(glyph), charStrings.End(glyph), owner?.Subrs, owner?.Used ?? []);
        }

        globalUsed = scanner.GlobalUsed;
    }

    /// <summary>Copies the retained charstrings, replacing the rest with one that draws nothing.</summary>
    private static byte[] BuildCharStrings(byte[] cff, CffIndex charStrings, HashSet<ushort> wanted)
    {
        var entries = new List<byte[]>(charStrings.Count);

        for (var glyph = 0; glyph < charStrings.Count; glyph++)
        {
            if (!wanted.Contains((ushort)glyph))
            {
                entries.Add(Empty);
                continue;
            }

            var start = charStrings.Start(glyph);
            var end = charStrings.End(glyph);

            entries.Add(end > start ? cff[start..end] : Empty);
        }

        return WriteIndex(entries);
    }

    // ----- the pieces that are copied whole -----

    /// <summary>
    /// A block the top dictionary points at, taken from its offset to wherever it ends.
    /// </summary>
    /// <remarks>
    /// A charset, an encoding and an FDSelect all state their own length only through their
    /// format, so each is measured by reading it. The predefined charsets and encodings — the
    /// operands 0, 1 and 2 — are not offsets at all and are left as they are.
    /// </remarks>
    private static (int Offset, byte[] Data)? Region(byte[] cff, Dictionary<int, List<double>> top, int op, int glyphCount)
    {
        if (!top.TryGetValue(op, out var operands) || operands.Count < 1) return null;

        var offset = (int)operands[^1];
        if (offset <= 2) return null;
        if (offset >= cff.Length) return null;

        var length = op switch
        {
            15 => CharsetLength(cff, offset, glyphCount),
            16 => EncodingLength(cff, offset),
            _ => FdSelectLength(cff, offset, glyphCount)
        };

        if (length <= 0 || offset + length > cff.Length) return null;

        return (offset, cff[offset..(offset + length)]);
    }

    private static int CharsetLength(byte[] cff, int offset, int glyphCount)
    {
        var format = cff[offset];

        return format switch
        {
            // One identifier per glyph after the first, which is always .notdef.
            0 => 1 + (glyphCount - 1) * 2,

            // Ranges of consecutive identifiers, counted until they cover every glyph.
            1 or 2 => RangedCharsetLength(cff, offset, glyphCount, format == 1 ? 3 : 4),
            _ => 0
        };
    }

    private static int RangedCharsetLength(byte[] cff, int offset, int glyphCount, int rangeSize)
    {
        var covered = 1;
        var position = offset + 1;

        while (covered < glyphCount)
        {
            if (position + rangeSize > cff.Length) return 0;

            var left = rangeSize == 3
                ? cff[position + 2]
                : (cff[position + 2] << 8) | cff[position + 3];

            covered += left + 1;
            position += rangeSize;
        }

        return position - offset;
    }

    private static int EncodingLength(byte[] cff, int offset)
    {
        var format = cff[offset];
        var length = (format & 0x7f) switch
        {
            0 => 2 + cff[offset + 1],
            1 => 2 + cff[offset + 1] * 2,
            _ => 0
        };

        // A supplement list may follow, one byte of count and three bytes each.
        if (length > 0 && (format & 0x80) != 0 && offset + length < cff.Length)
            length += 1 + cff[offset + length] * 3;

        return length;
    }

    private static int FdSelectLength(byte[] cff, int offset, int glyphCount)
    {
        return cff[offset] switch
        {
            0 => 1 + glyphCount,

            // A count, then that many three-byte ranges, then a sentinel.
            3 => 3 + (((cff[offset + 1] << 8) | cff[offset + 2]) * 3) + 2,
            _ => 0
        };
    }

    /// <summary>
    /// A private dictionary and the local subroutines it points at.
    /// </summary>
    /// <param name="Size">What the dictionary's own length is said to be, which does not change.</param>
    /// <param name="SubrsOffset">
    /// Where the subroutines sit, measured from the dictionary's start — which is how the
    /// dictionary refers to them, so the distance has to survive the move.
    /// </param>
    private sealed record PrivateInfo(int Size, byte[] Dict, int SubrsOffset, CffIndex? Subrs)
    {
        /// <summary>Which of the subroutines are reached from a charstring that was kept.</summary>
        public HashSet<int> Used { get; } = [];

        /// <summary>The dictionary and its subroutines, the unused ones emptied.</summary>
        public byte[] Build(byte[] cff)
        {
            if (Subrs is not { } subrs) return Dict;

            var output = new MemoryStream();
            output.Write(Dict);

            // The dictionary states where its subroutines are; anything between the two is
            // padding, and keeping it keeps that statement true.
            while (output.Length < SubrsOffset) output.WriteByte(0);

            output.Write(Prune(cff, subrs, Used));
            return output.ToArray();
        }
    }

    /// <summary>
    /// Rebuilds a subroutine index, replacing the ones nothing reaches with a bare return.
    /// </summary>
    /// <remarks>
    /// Emptying rather than removing, for the same reason the glyphs are emptied: a call names a
    /// subroutine by its position, offset by a bias that is itself worked out from how many there
    /// are. Take one out and every call after it points at the wrong one.
    /// </remarks>
    private static byte[] Prune(byte[] cff, CffIndex subrs, HashSet<int> used)
    {
        var entries = new List<byte[]>(subrs.Count);

        for (var i = 0; i < subrs.Count; i++)
        {
            entries.Add(used.Contains(i) ? cff[subrs.Start(i)..subrs.End(i)] : Return);
        }

        return WriteIndex(entries);
    }

    private static PrivateInfo? PrivateBlock(byte[] cff, Dictionary<int, List<double>> top)
    {
        if (!top.TryGetValue(18, out var operands) || operands.Count < 2) return null;

        var size = (int)operands[0];
        var offset = (int)operands[1];

        if (size <= 0 || offset <= 0 || offset + size > cff.Length) return null;

        var dict = ParseDict(cff, offset, offset + size);
        var block = cff[offset..(offset + size)];

        if (!dict.TryGetValue(19, out var subrs) || subrs.Count < 1)
            return new PrivateInfo(size, block, 0, null);

        var relative = (int)subrs[^1];
        if (relative <= 0 || offset + relative >= cff.Length)
            return new PrivateInfo(size, block, 0, null);

        return new PrivateInfo(size, block, relative, ReadIndex(cff, offset + relative));
    }

    /// <summary>
    /// The font dictionaries of a CID-keyed font, each with its own private block.
    /// </summary>
    private static List<(byte[] Dict, PrivateInfo Private)>? FontDictionaries(
        byte[] cff, Dictionary<int, List<double>> top)
    {
        if (!top.TryGetValue(0xc24, out var operands) || operands.Count < 1) return null;

        var index = ReadIndex(cff, (int)operands[^1]);
        var result = new List<(byte[], PrivateInfo)>(index.Count);

        for (var i = 0; i < index.Count; i++)
        {
            var dict = ParseDict(cff, index.Start(i), index.End(i));
            if (PrivateBlock(cff, dict) is not { } block) return null;

            result.Add((cff[index.Start(i)..index.End(i)], block));
        }

        return result;
    }

    /// <summary>
    /// Which font dictionary each glyph belongs to, from FDSelect. A font without one puts every
    /// glyph in the first.
    /// </summary>
    private static int[] ReadFdSelect(byte[] cff, Dictionary<int, List<double>> top, int glyphCount)
    {
        var map = new int[glyphCount];

        if (!top.TryGetValue(0xc25, out var operands) || operands.Count < 1) return map;

        var offset = (int)operands[^1];
        if (offset <= 0 || offset >= cff.Length) return map;

        switch (cff[offset])
        {
            case 0:
                for (var glyph = 0; glyph < glyphCount && offset + 1 + glyph < cff.Length; glyph++)
                    map[glyph] = cff[offset + 1 + glyph];

                break;

            case 3:
            {
                var ranges = (cff[offset + 1] << 8) | cff[offset + 2];
                var position = offset + 3;

                for (var i = 0; i < ranges && position + 5 <= cff.Length; i++, position += 3)
                {
                    var first = (cff[position] << 8) | cff[position + 1];
                    var fd = cff[position + 2];
                    var next = (cff[position + 3] << 8) | cff[position + 4];

                    for (var glyph = first; glyph < next && glyph < glyphCount; glyph++)
                        map[glyph] = fd;
                }

                break;
            }
        }

        return map;
    }

    // ----- writing -----    // ----- writing -----

    /// <summary>
    /// Lays the font out again, with every offset in the top dictionary pointing at where its
    /// piece ended up.
    /// </summary>
    /// <remarks>
    /// Offsets are written in the five-byte form whatever their value, which fixes the size of
    /// the dictionary that holds them. Otherwise moving a piece could change how many bytes its
    /// offset takes, which would move every piece after it, which would change the offsets again.
    /// </remarks>
    private static byte[] Write(
        byte[] cff, int headerSize, CffIndex names, CffIndex topDicts, CffIndex strings, CffIndex globalSubrs,
        HashSet<int> globalUsed, Dictionary<int, List<double>> top, byte[] charStrings,
        (int Offset, byte[] Data)? charset, (int Offset, byte[] Data)? encoding, (int Offset, byte[] Data)? fdSelect,
        PrivateInfo? privateBlock, List<(byte[] Dict, PrivateInfo Private)>? fdArray)
    {
        var header = cff[..headerSize];
        var nameIndex = cff[names.Offset..names.Limit];
        var stringIndex = cff[strings.Offset..strings.Limit];
        var subrIndex = Prune(cff, globalSubrs, globalUsed);

        var privateData = privateBlock?.Build(cff);
        var fdPrivateData = (fdArray ?? []).Select(entry => entry.Private.Build(cff)).ToList();

        // The top dictionary is written twice: once to learn its length, and again once the
        // offsets that go in it are known. Both come out the same size.
        var placeholder = WriteTopDict(top, 0, 0, 0, 0, 0, privateBlock?.Size ?? 0, 0);
        var topIndex = WriteIndex([placeholder]);

        var position = header.Length + nameIndex.Length + topIndex.Length + stringIndex.Length + subrIndex.Length;

        var charsetOffset = Place(ref position, charset?.Data);
        var encodingOffset = Place(ref position, encoding?.Data);
        var fdSelectOffset = Place(ref position, fdSelect?.Data);

        var charStringsOffset = position;
        position += charStrings.Length;

        // Each font dictionary of a CID font points at its own private block, so the blocks are
        // placed first and the dictionaries rewritten to reach them.
        var fdDicts = new List<byte[]>();
        var fdPrivateOffsets = new List<int>();

        foreach (var data in fdPrivateData)
        {
            fdPrivateOffsets.Add(position);
            position += data.Length;
        }

        var privateOffset = 0;
        if (privateData is not null)
        {
            privateOffset = position;
            position += privateData.Length;
        }

        var fdArrayOffset = 0;
        if (fdArray is { Count: > 0 })
        {
            for (var i = 0; i < fdArray.Count; i++)
            {
                var dict = ParseDict(fdArray[i].Dict, 0, fdArray[i].Dict.Length);
                fdDicts.Add(WriteFontDict(dict, fdArray[i].Private.Size, fdPrivateOffsets[i]));
            }

            fdArrayOffset = position;
            position += WriteIndex(fdDicts).Length;
        }

        var topDict = WriteTopDict(
            top, charStringsOffset, charsetOffset, encodingOffset, fdArrayOffset, fdSelectOffset,
            privateBlock?.Size ?? 0, privateOffset);

        var output = new MemoryStream();
        output.Write(header);
        output.Write(nameIndex);
        output.Write(WriteIndex([topDict]));
        output.Write(stringIndex);
        output.Write(subrIndex);

        if (charset is { } c) output.Write(c.Data);
        if (encoding is { } e) output.Write(e.Data);
        if (fdSelect is { } f) output.Write(f.Data);

        output.Write(charStrings);

        foreach (var data in fdPrivateData) output.Write(data);
        if (privateData is not null) output.Write(privateData);
        if (fdDicts.Count > 0) output.Write(WriteIndex(fdDicts));

        return output.ToArray();
    }

    private static int Place(ref int position, byte[]? data)
    {
        if (data is null) return 0;

        var offset = position;
        position += data.Length;
        return offset;
    }

    /// <summary>Rewrites the top dictionary, replacing every offset with where its piece now is.</summary>
    private static byte[] WriteTopDict(
        Dictionary<int, List<double>> top, int charStrings, int charset, int encoding,
        int fdArray, int fdSelect, int privateSize, int privateOffset)
    {
        var output = new MemoryStream();

        foreach (var (op, operands) in top.OrderBy(entry => entry.Key))
        {
            switch (op)
            {
                case 17:
                    WriteOffset(output, charStrings);
                    break;

                case 15 when charset != 0:
                    WriteOffset(output, charset);
                    break;

                case 16 when encoding != 0:
                    WriteOffset(output, encoding);
                    break;

                case 0xc24 when fdArray != 0:
                    WriteOffset(output, fdArray);
                    break;

                case 0xc25 when fdSelect != 0:
                    WriteOffset(output, fdSelect);
                    break;

                case 18:
                    WriteOffset(output, privateSize);
                    WriteOffset(output, privateOffset);
                    break;

                default:
                    // A predefined charset or encoding, and everything that is not an offset at
                    // all, is written back as it was.
                    foreach (var operand in operands) WriteOperand(output, operand);
                    break;
            }

            WriteOperator(output, op);
        }

        return output.ToArray();
    }

    private static byte[] WriteFontDict(Dictionary<int, List<double>> dict, int privateSize, int privateOffset)
    {
        var output = new MemoryStream();

        foreach (var (op, operands) in dict.OrderBy(entry => entry.Key))
        {
            if (op == 18)
            {
                WriteOffset(output, privateSize);
                WriteOffset(output, privateOffset);
            }
            else
            {
                foreach (var operand in operands) WriteOperand(output, operand);
            }

            WriteOperator(output, op);
        }

        return output.ToArray();
    }

    private static void WriteOperator(Stream output, int op)
    {
        if (op > 0xff)
        {
            output.WriteByte(12);
            output.WriteByte((byte)(op & 0xff));
        }
        else
        {
            output.WriteByte((byte)op);
        }
    }

    /// <summary>Writes an integer in the widest form, so that its length does not depend on it.</summary>
    private static void WriteOffset(Stream output, int value)
    {
        output.WriteByte(29);
        // Big-endian byte split — the low eight bits by design, unchecked serialisation (#266).
        unchecked
        {
            output.WriteByte((byte)(value >> 24));
            output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }
    }

    private static void WriteOperand(Stream output, double value)
    {
        if (value != Math.Floor(value) || value is < int.MinValue or > int.MaxValue)
        {
            // A real number, written in the packed decimal form the format uses for them.
            WriteReal(output, value);
            return;
        }

        WriteOffset(output, (int)value);
    }

    /// <summary>Writes a real number as nibbles: digits, and the codes for sign, point and end.</summary>
    private static void WriteReal(Stream output, double value)
    {
        var text = value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var nibbles = new List<int>();

        foreach (var character in text)
        {
            switch (character)
            {
                case >= '0' and <= '9':
                    nibbles.Add(character - '0');
                    break;
                case '.':
                    nibbles.Add(0xa);
                    break;
                case '-':
                    nibbles.Add(0xe);
                    break;
                case 'E' or 'e':
                    nibbles.Add(0xb);
                    break;
                case '+':
                    break;
            }
        }

        nibbles.Add(0xf);
        if (nibbles.Count % 2 != 0) nibbles.Add(0xf);

        output.WriteByte(30);
        for (var i = 0; i < nibbles.Count; i += 2)
            output.WriteByte((byte)((nibbles[i] << 4) | nibbles[i + 1]));
    }

    // ----- finding what a charstring reaches -----

    /// <summary>
    /// Walks the charstrings that were kept and marks every subroutine they call.
    /// </summary>
    /// <remarks>
    /// A subroutine is a piece of a charstring pulled out because several glyphs share it, and it
    /// may call others in turn, so this follows them down. What it is really doing is running the
    /// charstring far enough to know which numbers were on the stack when a call was made — the
    /// number is the subroutine, offset by a bias — which is why it has to understand the
    /// operators well enough to keep the stack straight.
    /// </remarks>
    private sealed class SubroutineScanner(byte[] cff, CffIndex global)
    {
        private readonly List<int> _stack = [];
        private CffIndex? _local;
        private HashSet<int> _localUsed = [];
        private int _stems;

        public HashSet<int> GlobalUsed { get; } = [];

        /// <summary>Runs one glyph's charstring, against the local subroutines it may call.</summary>
        public void Scan(int start, int end, CffIndex? local, HashSet<int> localUsed)
        {
            _local = local;
            _localUsed = localUsed;
            _stack.Clear();
            _stems = 0;

            Run(start, end, 0);
        }

        private void Run(int start, int end, int depth)
        {
            // The format allows ten levels of call; beyond that the font is malformed or is
            // trying to make this loop.
            if (depth > 10) return;

            var position = start;

            while (position < end && position < cff.Length)
            {
                var b0 = cff[position];

                if (b0 >= 32 || b0 == 28)
                {
                    position = ReadOperand(position);
                    continue;
                }

                position++;

                switch (b0)
                {
                    // The stem operators take pairs of numbers, and how many stems have been
                    // declared is what says how long a hint mask is.
                    case 1 or 3 or 18 or 23:
                        _stems += _stack.Count / 2;
                        _stack.Clear();
                        break;

                    // A mask may be preceded by the numbers of an implied vstem.
                    case 19 or 20:
                        _stems += _stack.Count / 2;
                        _stack.Clear();
                        position += (_stems + 7) / 8;
                        break;

                    case 10:
                        Call(_local, _localUsed, ref position, depth);
                        break;

                    case 29:
                        Call(global, GlobalUsed, ref position, depth);
                        break;

                    // A return hands back to the caller with the stack as it stands; an endchar
                    // finishes the glyph outright.
                    case 11:
                        return;

                    case 14:
                        return;

                    case 12:
                        position++;
                        _stack.Clear();
                        break;

                    default:
                        _stack.Clear();
                        break;
                }
            }
        }

        private void Call(CffIndex? subrs, HashSet<int> used, ref int position, int depth)
        {
            if (subrs is not { } index || index.Count == 0 || _stack.Count == 0) return;

            var number = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);

            var target = number + Bias(index.Count);
            if (target < 0 || target >= index.Count) return;

            // Already followed, and following it again would only mark what is marked; the stack
            // it would leave behind is the price of not doing so.
            if (!used.Add(target)) return;

            Run(index.Start(target), index.End(target), depth + 1);
        }

        /// <summary>Reads a number onto the stack and returns where it ended.</summary>
        private int ReadOperand(int position)
        {
            var b0 = cff[position];

            switch (b0)
            {
                case 28:
                    // A signed 16-bit operand: a negative one has its top bit set, so the cast is
                    // a reinterpretation and stays unchecked (#266).
                    Push(unchecked((short)((cff[position + 1] << 8) | cff[position + 2])));
                    return position + 3;

                case 255:
                    // A number with a fractional part, of which only the whole part can be a
                    // subroutine number.
                    Push((cff[position + 1] << 8) | cff[position + 2]);
                    return position + 5;

                case <= 246:
                    Push(b0 - 139);
                    return position + 1;

                case <= 250:
                    Push((b0 - 247) * 256 + cff[position + 1] + 108);
                    return position + 2;

                default:
                    Push(-(b0 - 251) * 256 - cff[position + 1] - 108);
                    return position + 2;
            }
        }

        private void Push(int value)
        {
            // The stack holds at most forty-eight numbers, and a malformed charstring that keeps
            // pushing should not keep growing this.
            if (_stack.Count < 48) _stack.Add(value);
        }

        /// <summary>
        /// What a subroutine number is offset by, which the format works out from how many there
        /// are so that the commonest ones take the fewest bytes to name.
        /// </summary>
        private static int Bias(int count) => count switch
        {
            < 1240 => 107,
            < 33900 => 1131,
            _ => 32768
        };
    }

    // ----- reading -----

    /// <summary>An INDEX: a count, a table of offsets into its data, and the data.</summary>
    private readonly record struct CffIndex(byte[] Data, int Offset, int Count, int OffSize, int Offsets, int DataStart)
    {
        public int Start(int i) => DataStart + ReadOffset(i) - 1;

        public int End(int i) => DataStart + ReadOffset(i + 1) - 1;

        /// <summary>Where the index finishes, which is where whatever follows it begins.</summary>
        public int Limit => Count == 0 ? Offset + 2 : DataStart + ReadOffset(Count) - 1;

        private int ReadOffset(int i)
        {
            var at = Offsets + i * OffSize;
            var value = 0;

            for (var b = 0; b < OffSize; b++) value = (value << 8) | Data[at + b];

            return value;
        }
    }

    private static CffIndex ReadIndex(byte[] cff, int offset)
    {
        if (offset + 2 > cff.Length) throw new FontFormatException("An index runs past the table.");

        var count = (cff[offset] << 8) | cff[offset + 1];
        if (count == 0) return new CffIndex(cff, offset, 0, 0, 0, 0);

        var offSize = cff[offset + 2];
        if (offSize is < 1 or > 4) throw new FontFormatException($"An index has an offset size of {offSize}.");

        var offsets = offset + 3;
        var dataStart = offsets + (count + 1) * offSize;

        // The offset array itself must lie within the table, and the offsets must be validated,
        // not just read: an INDEX whose offsets are wide or non-monotonic makes each kept glyph's
        // charstring slice span most of the table, so a font used by many glyphs amplifies the
        // subset to gigabytes. Validated here — first offset one, non-decreasing, within the
        // table — so a bad INDEX is refused and the font embeds whole rather than aborting (#194).
        if (dataStart > cff.Length) throw new FontFormatException("An index's data runs past the table.");

        var previous = 0;
        for (var i = 0; i <= count; i++)
        {
            var at = offsets + i * offSize;
            if (offSize < 0 || at > cff.Length - offSize) throw new FontFormatException("An index offset runs past the table.");

            var value = 0;
            for (var b = 0; b < offSize; b++) value = (value << 8) | cff[at + b];

            if (value < 1 || value < previous || dataStart + value - 1 > cff.Length)
                throw new FontFormatException("An index has offsets that do not run forward within the table.");

            previous = value;
        }

        return new CffIndex(cff, offset, count, offSize, offsets, dataStart);
    }

    private static byte[] WriteIndex(IReadOnlyList<byte[]> entries)
    {
        if (entries.Count == 0) return [0, 0];

        var total = entries.Sum(entry => entry.Length);
        var offSize = (total + 1) switch
        {
            <= 0xff => 1,
            <= 0xffff => 2,
            <= 0xffffff => 3,
            _ => 4
        };

        var output = new MemoryStream();
        // Serialising the count and offsets keeps their low bytes by design — unchecked, unlike the
        // size arithmetic the assembly guards (#266).
        unchecked
        {
            output.WriteByte((byte)(entries.Count >> 8));
            output.WriteByte((byte)entries.Count);
            output.WriteByte((byte)offSize);
        }

        var position = 1;
        WriteSized(output, position, offSize);

        foreach (var entry in entries)
        {
            position += entry.Length;
            WriteSized(output, position, offSize);
        }

        foreach (var entry in entries) output.Write(entry);

        return output.ToArray();
    }

    private static void WriteSized(Stream output, int value, int size)
    {
        // Each byte is a slice of the value by design — unchecked serialisation (#266).
        for (var b = size - 1; b >= 0; b--) output.WriteByte(unchecked((byte)(value >> (b * 8))));
    }

    /// <summary>
    /// Reads a dictionary: operands, then the operator they belong to. Escaped operators are keyed
    /// as 0xc00 plus their second byte, which keeps them apart from the single-byte ones.
    /// </summary>
    private static Dictionary<int, List<double>> ParseDict(byte[] data, int start, int end)
    {
        var result = new Dictionary<int, List<double>>();
        var operands = new List<double>();
        var position = start;

        while (position < end)
        {
            var b0 = data[position];

            if (b0 <= 21)
            {
                int op = b0;
                position++;

                if (b0 == 12)
                {
                    op = 0xc00 | data[position];
                    position++;
                }

                result[op] = new List<double>(operands);
                operands.Clear();
                continue;
            }

            switch (b0)
            {
                case 28:
                    // A signed 16-bit operand — a negative one has its top bit set — read as a
                    // reinterpretation, so unchecked (#266).
                    operands.Add(unchecked((short)((data[position + 1] << 8) | data[position + 2])));
                    position += 3;
                    break;

                case 29:
                    operands.Add((data[position + 1] << 24) | (data[position + 2] << 16) |
                                 (data[position + 3] << 8) | data[position + 4]);
                    position += 5;
                    break;

                case 30:
                    operands.Add(ReadReal(data, ref position));
                    break;

                case >= 32 and <= 246:
                    operands.Add(b0 - 139);
                    position++;
                    break;

                case >= 247 and <= 250:
                    operands.Add((b0 - 247) * 256 + data[position + 1] + 108);
                    position += 2;
                    break;

                case >= 251 and <= 254:
                    operands.Add(-(b0 - 251) * 256 - data[position + 1] - 108);
                    position += 2;
                    break;

                default:
                    throw new FontFormatException($"A dictionary holds the reserved byte {b0}.");
            }
        }

        return result;
    }

    private static double ReadReal(byte[] data, ref int position)
    {
        position++;
        var text = new System.Text.StringBuilder();

        while (position < data.Length)
        {
            var b = data[position++];

            foreach (var nibble in (int[])[b >> 4, b & 0xf])
            {
                switch (nibble)
                {
                    case <= 9:
                        text.Append((char)('0' + nibble));
                        break;
                    case 0xa:
                        text.Append('.');
                        break;
                    case 0xb:
                        text.Append('E');
                        break;
                    case 0xc:
                        text.Append("E-");
                        break;
                    case 0xe:
                        text.Append('-');
                        break;
                    case 0xf:
                        return double.TryParse(
                            text.ToString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var value)
                            ? value
                            : 0;
                }
            }
        }

        return 0;
    }
}
