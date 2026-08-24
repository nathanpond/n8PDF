namespace n8PDF.Images;

/// <summary>
/// Reads the fax encodings: the ones a scanner writes, where a page is black on white and what is
/// sent is the lengths of the runs rather than the pixels.
/// </summary>
/// <remarks>
/// There are two ways of writing a line and three encodings made from them. One dimension writes
/// the line on its own, as alternating runs of white and black in the code the standard fixes.
/// Two dimensions writes it against the line above: where the two change colour in nearly the same
/// places — which is most of a page of text — saying how far the change has moved is far shorter
/// than saying where it is.
///
/// Group 3 uses one dimension, or either with a bit at the start of each line saying which. Group
/// 4 uses two throughout, with the line above the first taken to be all white. That is the whole
/// of the difference between them.
/// </remarks>
internal static class CcittDecoder
{
    /// <summary>
    /// Unpacks a strip.
    /// </summary>
    /// <param name="twoDimensional">
    /// Whether the lines may be written against the one above. Group 4 always is; a group 3 strip
    /// says so in its options, and then each line says which of the two it used.
    /// </param>
    /// <param name="pureTwoDimensional">Group 4, where no line is written on its own.</param>
    /// <param name="byteAligned">Whether every line begins on a byte, which some writers do.</param>
    /// <returns>One bit per pixel, a set bit meaning black.</returns>
    public static byte[] Decode(
        byte[] data, int offset, int length, int width, int height,
        bool twoDimensional, bool pureTwoDimensional, bool byteAligned)
    {
        var rowBytes = (width + 7) / 8;
        var rows = new byte[rowBytes * Math.Max(1, height)];

        var bits = new BitReader(data, offset, length);

        // Where the line above changes colour, which is what a line written against it is read
        // from. The line before the first is all white, so it changes nowhere.
        var reference = new List<int> { width, width };

        for (var y = 0; y < height; y++)
        {
            if (byteAligned) bits.AlignToByte();

            // Group 4 is written the one way throughout. A group 3 file that may use either says
            // which in the bit after each end-of-line code: set for a line written on its own.
            var oneDimensional = !pureTwoDimensional && !twoDimensional;

            while (bits.Peek(CcittTables.EndOfLineLength) == CcittTables.EndOfLine)
            {
                bits.Skip(CcittTables.EndOfLineLength);

                if (twoDimensional && !pureTwoDimensional) oneDimensional = bits.Read(1) == 1;
            }

            if (bits.AtEnd) break;

            var changes = oneDimensional
                ? ReadOneDimensional(ref bits, width)
                : ReadTwoDimensional(ref bits, width, reference);

            if (changes is null) break;

            Write(rows, y * rowBytes, width, changes);
            reference = changes;
        }

        return rows;
    }

    /// <summary>
    /// A line written on its own: alternating runs of white and black, beginning with white.
    /// </summary>
    /// <returns>Where the line changes colour, in order, ending past its own width.</returns>
    private static List<int>? ReadOneDimensional(ref BitReader bits, int width)
    {
        var changes = new List<int>();
        var at = 0;
        var white = true;

        // A line of width pixels changes colour at most width times; a run of zero neither
        // advances nor terminates, so without this bound a strip of zero-runs spins forever and
        // grows the changes list without end while decoding an image one pixel tall (#46).
        while (at < width && changes.Count <= width)
        {
            var run = ReadRun(ref bits, white);
            if (run < 0) return changes.Count > 0 ? Close(changes, width) : null;

            at = Math.Min(width, at + run);
            changes.Add(at);
            white = !white;
        }

        return Close(changes, width);
    }

    /// <summary>
    /// A line written against the one above it. Each mode says where the next change of colour is
    /// in terms of the changes above: at the same place, a little either side of it, or — where
    /// the two lines have nothing to do with each other — two runs written out in full.
    /// </summary>
    /// <remarks>
    /// The line is read from a position that begins one pixel before it rather than at its first
    /// pixel. That imaginary place is what makes a change at the very start of a line findable:
    /// every rule here is "the first change after where we are", and a change at nought is not
    /// after nought.
    /// </remarks>
    private static List<int>? ReadTwoDimensional(ref BitReader bits, int width, List<int> reference)
    {
        var changes = new List<int>();
        var a0 = -1;
        var white = true;

        // As in the one-dimensional case (#46): a line's transitions cannot outnumber its pixels
        // by more than the two a horizontal mode writes at once, so this bounds a strip that
        // would otherwise spin on zero-runs.
        while (a0 < width && changes.Count <= 2 * width + 1)
        {
            // The first change above that is past where this line has reached and of the colour
            // this line is looking for, and the one after it.
            var b1 = Above(reference, a0, white, width);
            var b2 = After(reference, b1, width);

            var mode = ReadMode(ref bits);

            switch (mode.Kind)
            {
                case ModeKind.Pass:
                    // The run above begins and ends before this line changes at all, so this line
                    // carries on in the colour it is in.
                    a0 = b2;
                    break;

                case ModeKind.Horizontal:
                {
                    // Two runs of this line's own, the first in the colour it is in. They are
                    // measured from where the line has reached, which is its start on the first.
                    var first = ReadRun(ref bits, white);
                    var second = ReadRun(ref bits, !white);

                    if (first < 0 || second < 0) return changes.Count > 0 ? Close(changes, width) : null;

                    var from = Math.Max(0, a0);
                    var middle = Math.Min(width, from + first);
                    var end = Math.Min(width, middle + second);

                    changes.Add(middle);
                    changes.Add(end);

                    // Two runs bring the line back to the colour it started them in.
                    a0 = end;
                    break;
                }

                case ModeKind.Vertical:
                {
                    var a1 = Math.Clamp(b1 + mode.Offset, 0, width);

                    changes.Add(a1);
                    a0 = a1;
                    white = !white;
                    break;
                }

                default:
                    return changes.Count > 0 ? Close(changes, width) : null;
            }
        }

        return Close(changes, width);
    }

    private static List<int> Close(List<int> changes, int width)
    {
        // A line's changes end past its own width, so that reading the line below it never runs
        // off the end of them.
        changes.Add(width);
        changes.Add(width);

        return changes;
    }

    /// <summary>
    /// Where the line above next changes to the colour being looked for, past the point this line
    /// has reached.
    /// </summary>
    /// <remarks>
    /// The changes of a line alternate and the first of them is a change away from white, so which
    /// colour a change is going to is which place it stands in: the even ones turn black and the
    /// odd ones turn white.
    /// </remarks>
    private static int Above(List<int> reference, int a0, bool white, int width)
    {
        for (var i = 0; i < reference.Count; i++)
        {
            if (reference[i] <= a0) continue;
            if (i % 2 == (white ? 0 : 1)) return Math.Min(reference[i], width);
        }

        return width;
    }

    private static int After(List<int> reference, int at, int width)
    {
        foreach (var change in reference)
        {
            if (change > at) return Math.Min(change, width);
        }

        return width;
    }

    private enum ModeKind
    {
        Unknown,
        Pass,
        Horizontal,
        Vertical
    }

    private readonly record struct Mode(ModeKind Kind, int Offset);

    /// <summary>
    /// Reads which of the modes the next change is written in. They are a prefix code of their
    /// own: one bit for the commonest, up to seven for the rarest.
    /// </summary>
    private static Mode ReadMode(ref BitReader bits)
    {
        if (bits.Read(1) == 1) return new Mode(ModeKind.Vertical, 0);          // 1

        if (bits.Read(1) == 1)                                                  // 01x
            return new Mode(ModeKind.Vertical, bits.Read(1) == 1 ? 1 : -1);

        if (bits.Read(1) == 1) return new Mode(ModeKind.Horizontal, 0);         // 001

        if (bits.Read(1) == 1) return new Mode(ModeKind.Pass, 0);               // 0001

        if (bits.Read(1) == 1)                                                  // 00001x
            return new Mode(ModeKind.Vertical, bits.Read(1) == 1 ? 2 : -2);

        if (bits.Read(1) == 1)                                                  // 000001x
            return new Mode(ModeKind.Vertical, bits.Read(1) == 1 ? 3 : -3);

        return new Mode(ModeKind.Unknown, 0);
    }

    /// <summary>
    /// Reads one run: a makeup code for the multiple of sixty-four, where the run is long enough
    /// to need one, and then a terminating code for what is left.
    /// </summary>
    private static int ReadRun(ref BitReader bits, bool white)
    {
        var total = 0;

        while (true)
        {
            var run = ReadCode(ref bits, white);
            if (run < 0) return -1;

            total += run;

            // A make-up-code chain has no terminator in a crafted strip, and each adds at least
            // sixty-four, so an uncapped total overflows int; a run past any real image width is
            // malformed, so it stops there (#47).
            if (total > 100_000_000) return total;

            // A makeup code is followed by a terminating one, which is any run under sixty-four.
            if (run < 64 || run % 64 != 0) return total;
        }
    }

    private static int ReadCode(ref BitReader bits, bool white)
    {
        var table = white ? CcittTables.White : CcittTables.Black;

        // The codes are a prefix code, so the first length that matches is the one meant.
        for (var length = 2; length <= 14; length++)
        {
            var peeked = bits.Peek(length);
            if (peeked < 0) return -1;

            foreach (var code in table)
            {
                if (code.Length != length || code.Bits != peeked) continue;

                bits.Skip(length);
                return code.Run;
            }

            foreach (var code in CcittTables.Extended)
            {
                if (code.Length != length || code.Bits != peeked) continue;

                bits.Skip(length);
                return code.Run;
            }
        }

        return -1;
    }

    /// <summary>Fills a row in from where it changes colour, beginning white.</summary>
    private static void Write(byte[] rows, int start, int width, List<int> changes)
    {
        var at = 0;
        var white = true;

        foreach (var change in changes)
        {
            var end = Math.Min(width, change);

            if (!white)
            {
                for (var x = at; x < end; x++) rows[start + x / 8] |= (byte)(0x80 >> (x % 8));
            }

            at = end;
            white = !white;

            if (at >= width) break;
        }
    }

    /// <summary>Reads a run of bits, biggest first, which is the order a fax is written in.</summary>
    private struct BitReader(byte[] data, int offset, int length)
    {
        private int _bit;

        private readonly int _bits = length * 8;

        public readonly bool AtEnd => _bit >= _bits;

        public int Read(int count)
        {
            var value = Peek(count);
            if (value >= 0) _bit += count;

            return value;
        }

        public readonly int Peek(int count)
        {
            if (_bit + count > _bits) return -1;

            var value = 0;

            for (var i = 0; i < count; i++)
            {
                var at = _bit + i;
                var bit = (data[offset + at / 8] >> (7 - at % 8)) & 1;

                value = (value << 1) | bit;
            }

            return value;
        }

        public void Skip(int count) => _bit += count;

        public void AlignToByte() => _bit = (_bit + 7) / 8 * 8;
    }
}
