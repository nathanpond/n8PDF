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
/// What is kept whole: the name, string and subroutine indexes, the charset, the encoding, and
/// the private dictionaries with their local subroutines.
///
/// Pruning the subroutines as well would need a charstring interpreter — which subroutine a call
/// reaches depends on a bias computed from how many there are, and stepping over a hint mask
/// means having counted the stems before it — and for a CID-keyed font it would have to be done
/// per font dictionary, which is what FDSelect is for. In a Latin face the subroutines are a
/// small part of the whole and this is not worth it. In a Chinese one they are most of what a
/// subset still weighs, which is where it would pay.
/// </remarks>
internal static class CffSubset
{
    /// <summary>A charstring that draws nothing: the endchar operator by itself.</summary>
    private static readonly byte[] Empty = [14];

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

        var outlines = BuildCharStrings(cff, charStrings, wanted);

        return Write(cff, headerSize, names, topDicts, strings, globalSubrs, top,
            outlines, charset, encoding, fdSelect, privateBlock, fdArray);
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
    /// A private dictionary together with the local subroutines that follow it.
    /// </summary>
    /// <remarks>
    /// The dictionary points at its subroutines by a distance from its own start, so the two move
    /// as one block and that distance stays true.
    /// </remarks>
    private static (int Size, byte[] Data)? PrivateBlock(byte[] cff, Dictionary<int, List<double>> top)
    {
        if (!top.TryGetValue(18, out var operands) || operands.Count < 2) return null;

        var size = (int)operands[0];
        var offset = (int)operands[1];

        if (size <= 0 || offset <= 0 || offset + size > cff.Length) return null;

        var length = size;

        // The subroutine index, if there is one, sits at a distance from the dictionary's start.
        var dict = ParseDict(cff, offset, offset + size);
        if (dict.TryGetValue(19, out var subrs) && subrs.Count >= 1)
        {
            var subrsOffset = offset + (int)subrs[^1];
            if (subrsOffset > offset && subrsOffset < cff.Length)
                length = ReadIndex(cff, subrsOffset).Limit - offset;
        }

        if (offset + length > cff.Length) return null;

        return (size, cff[offset..(offset + length)]);
    }

    /// <summary>
    /// The font dictionaries of a CID-keyed font, each with its own private block.
    /// </summary>
    private static List<(byte[] Dict, int PrivateSize, byte[] Private)>? FontDictionaries(
        byte[] cff, Dictionary<int, List<double>> top)
    {
        if (!top.TryGetValue(0xc24, out var operands) || operands.Count < 1) return null;

        var index = ReadIndex(cff, (int)operands[^1]);
        var result = new List<(byte[], int, byte[])>(index.Count);

        for (var i = 0; i < index.Count; i++)
        {
            var dict = ParseDict(cff, index.Start(i), index.End(i));
            var block = PrivateBlock(cff, dict);
            if (block is not { } privateBlock) return null;

            result.Add((cff[index.Start(i)..index.End(i)], privateBlock.Size, privateBlock.Data));
        }

        return result;
    }

    // ----- writing -----

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
        Dictionary<int, List<double>> top, byte[] charStrings,
        (int Offset, byte[] Data)? charset, (int Offset, byte[] Data)? encoding, (int Offset, byte[] Data)? fdSelect,
        (int Size, byte[] Data)? privateBlock, List<(byte[] Dict, int PrivateSize, byte[] Private)>? fdArray)
    {
        var header = cff[..headerSize];
        var nameIndex = cff[names.Offset..names.Limit];
        var stringIndex = cff[strings.Offset..strings.Limit];
        var subrIndex = cff[globalSubrs.Offset..globalSubrs.Limit];

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

        foreach (var (_, _, data) in fdArray ?? [])
        {
            fdPrivateOffsets.Add(position);
            position += data.Length;
        }

        var privateOffset = 0;
        if (privateBlock is { } block)
        {
            privateOffset = position;
            position += block.Data.Length;
        }

        var fdArrayOffset = 0;
        if (fdArray is { Count: > 0 })
        {
            for (var i = 0; i < fdArray.Count; i++)
            {
                var dict = ParseDict(fdArray[i].Dict, 0, fdArray[i].Dict.Length);
                fdDicts.Add(WriteFontDict(dict, fdArray[i].PrivateSize, fdPrivateOffsets[i]));
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

        foreach (var (_, _, data) in fdArray ?? []) output.Write(data);
        if (privateBlock is { } p) output.Write(p.Data);
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
        output.WriteByte((byte)(value >> 24));
        output.WriteByte((byte)(value >> 16));
        output.WriteByte((byte)(value >> 8));
        output.WriteByte((byte)value);
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
        output.WriteByte((byte)(entries.Count >> 8));
        output.WriteByte((byte)entries.Count);
        output.WriteByte((byte)offSize);

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
        for (var b = size - 1; b >= 0; b--) output.WriteByte((byte)(value >> (b * 8)));
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
                    operands.Add((short)((data[position + 1] << 8) | data[position + 2]));
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
