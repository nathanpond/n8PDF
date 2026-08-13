using n8PDF.Images;

namespace n8PDF.Tests.Support;

/// <summary>
/// Writes the fax encodings, so that reading them can be tested against a file whose every pixel
/// is known — and, more to the point, against a reader that is not this one.
/// </summary>
/// <remarks>
/// The code tables are the library's own rather than a second copy of them. That is deliberate: a
/// file written with them is handed to another program to read, and if a code in them is wrong
/// that program says so. A copy here would only ever agree with itself.
/// </remarks>
public static class CcittWriter
{
    /// <summary>The runs of a row: how many white, then how many black, and so on.</summary>
    private static List<int> Runs(byte[] pixels, int width, int y)
    {
        var runs = new List<int>();
        var white = true;
        var run = 0;

        for (var x = 0; x < width; x++)
        {
            var black = pixels[y * width + x] != 0;

            if (black == !white)
            {
                run++;
                continue;
            }

            runs.Add(run);
            run = 1;
            white = !white;
        }

        runs.Add(run);

        return runs;
    }

    /// <summary>A page written a line at a time, each on its own: the simplest of the three.</summary>
    public static byte[] Group3(byte[] pixels, int width, int height, bool byteAligned = true)
    {
        var bits = new BitWriter();

        for (var y = 0; y < height; y++)
        {
            if (byteAligned) bits.AlignToByte();

            var white = true;

            foreach (var run in Runs(pixels, width, y))
            {
                Write(bits, run, white);
                white = !white;
            }
        }

        return bits.ToArray();
    }

    /// <summary>
    /// A page written the way a group 3 file may be: every line introduced by an end-of-line code
    /// and a bit saying which of the two ways it was written, so that a file can mix them.
    /// </summary>
    public static byte[] Group3Mixed(byte[] pixels, int width, int height)
    {
        var bits = new BitWriter();
        var reference = new List<int> { width, width };

        for (var y = 0; y < height; y++)
        {
            var changes = Changes(pixels, width, y);

            bits.Write(0b000000000001, 12);

            // The first line of a page is written on its own, since it has nothing above it to be
            // written against; the rest are written against the line before.
            var alone = y == 0;
            bits.Write(alone ? 1 : 0, 1);

            if (alone)
            {
                var white = true;

                foreach (var run in Runs(pixels, width, y))
                {
                    Write(bits, run, white);
                    white = !white;
                }
            }
            else
            {
                WriteLine(bits, changes, reference, width);
            }

            reference = changes;
        }

        return bits.ToArray();
    }

    /// <summary>
    /// A page written against itself: every line but the first said in terms of the one above it.
    /// </summary>
    /// <param name="plain">
    /// Whether to write every line the long way, spelling out two runs at a time. That is legal
    /// group 4 and is the case a reader has to get right first, but it is not what a fax looks
    /// like: the point of the encoding is that a line mostly says how far each change has moved
    /// since the line above, which is what the other way writes.
    /// </param>
    public static byte[] Group4(byte[] pixels, int width, int height, bool plain = false)
    {
        var bits = new BitWriter();

        // The line above the first is all white, so it changes colour nowhere.
        var reference = new List<int> { width, width };

        for (var y = 0; y < height; y++)
        {
            var changes = Changes(pixels, width, y);

            if (plain) WritePlainLine(bits, changes, width);
            else WriteLine(bits, changes, reference, width);

            reference = changes;
        }

        // The end of a group 4 page, which is the end-of-line code twice over.
        bits.Write(0b000000000001, 12);
        bits.Write(0b000000000001, 12);

        return bits.ToArray();
    }

    /// <summary>Where a row changes colour, in order, beginning from white.</summary>
    private static List<int> Changes(byte[] pixels, int width, int y)
    {
        var changes = new List<int>();
        var white = true;

        for (var x = 0; x < width; x++)
        {
            var black = pixels[y * width + x] != 0;

            if (black == !white) continue;

            changes.Add(x);
            white = !white;
        }

        changes.Add(width);
        changes.Add(width);

        return changes;
    }

    private static void WritePlainLine(BitWriter bits, List<int> changes, int width)
    {
        var at = 0;
        var white = true;
        var index = 0;

        while (at < width)
        {
            var first = (index < changes.Count ? Math.Min(changes[index++], width) : width) - at;
            var middle = at + first;
            var second = (index < changes.Count ? Math.Min(changes[index++], width) : width) - middle;

            bits.Write(0b001, 3);
            Write(bits, first, white);
            Write(bits, second, !white);

            at = middle + second;
        }
    }

    /// <summary>
    /// A line written the way a fax writes one: each change said in terms of the change above it
    /// where the two are close, and spelled out only where they are not.
    /// </summary>
    private static void WriteLine(BitWriter bits, List<int> changes, List<int> reference, int width)
    {
        var a0 = -1;
        var white = true;

        while (a0 < width)
        {
            var a1 = After(changes, a0, width);
            var a2 = After(changes, a1, width);

            var b1 = Opposite(reference, a0, white, width);
            var b2 = After(reference, b1, width);

            if (b2 < a1)
            {
                // The run above begins and ends before this line changes at all.
                bits.Write(0b0001, 4);
                a0 = b2;
                continue;
            }

            var offset = a1 - b1;

            if (Math.Abs(offset) <= 3)
            {
                bits.Write(offset switch
                {
                    0 => 0b1,
                    1 => 0b011,
                    -1 => 0b010,
                    2 => 0b000011,
                    -2 => 0b000010,
                    3 => 0b0000011,
                    _ => 0b0000010
                }, offset switch { 0 => 1, 1 or -1 => 3, 2 or -2 => 6, _ => 7 });

                a0 = a1;
                white = !white;
                continue;
            }

            // Nothing above to say it in terms of: two runs of this line's own.
            bits.Write(0b001, 3);
            Write(bits, a1 - Math.Max(0, a0), white);
            Write(bits, a2 - a1, !white);

            a0 = a2;
        }
    }

    private static int After(List<int> changes, int at, int width)
    {
        foreach (var change in changes)
        {
            if (change > at) return Math.Min(change, width);
        }

        return width;
    }

    /// <summary>
    /// The first change on the line above that is past this point and of the colour being looked
    /// for. The changes alternate from white, so which they are is which place they stand in.
    /// </summary>
    private static int Opposite(List<int> reference, int at, bool white, int width)
    {
        for (var i = 0; i < reference.Count; i++)
        {
            if (reference[i] <= at) continue;
            if (i % 2 == (white ? 0 : 1)) return Math.Min(reference[i], width);
        }

        return width;
    }

    /// <summary>
    /// One run: a makeup code for the multiple of sixty-four it passes, then a terminating code.
    /// </summary>
    private static void Write(BitWriter bits, int run, bool white)
    {
        while (run >= 64)
        {
            var makeup = Longest(run, white);

            bits.Write(makeup.Bits, makeup.Length);
            run -= makeup.Run;
        }

        var terminating = Find(run, white);
        bits.Write(terminating.Bits, terminating.Length);
    }

    /// <summary>The largest makeup code that does not overshoot the run.</summary>
    private static CcittTables.Code Longest(int run, bool white)
    {
        var best = default(CcittTables.Code);

        foreach (var code in white ? CcittTables.White : CcittTables.Black)
        {
            if (code.Run is >= 64 and <= 1728 && code.Run <= run && code.Run > best.Run) best = code;
        }

        foreach (var code in CcittTables.Extended)
        {
            if (code.Run <= run && code.Run > best.Run) best = code;
        }

        return best;
    }

    private static CcittTables.Code Find(int run, bool white)
    {
        foreach (var code in white ? CcittTables.White : CcittTables.Black)
        {
            if (code.Run == run) return code;
        }

        throw new ArgumentOutOfRangeException(nameof(run), run, "No code for that run.");
    }

    /// <summary>A TIFF holding one of the fax encodings.</summary>
    public static byte[] Tiff(
        byte[] pixels, int width, int height, int compression, bool byteAligned = true, bool plain = false)
    {
        var body = compression switch
        {
            4 => Group4(pixels, width, height, plain),
            3 when !byteAligned => Group3Mixed(pixels, width, height),
            _ => Group3(pixels, width, height, byteAligned)
        };

        var tags = new List<(int Id, int Type, int Value)>
        {
            (256, 3, width),
            (257, 3, height),
            (258, 3, 1),
            (259, 3, compression),
            (262, 3, 0),                                  // nought is white, as a fax has it
            (273, 4, 0),
            (277, 3, 1),
            (278, 3, height),
            (279, 4, body.Length),
            (284, 3, 1),
            // What the options say: for a group 3 file, whether its lines may be written against
            // one another and whether each begins on a byte.
            (compression == 4 ? 293 : 292, 4, compression == 4 ? 0 : byteAligned ? 4 : 1)
        };

        var file = new List<byte>();
        file.AddRange("II"u8.ToArray());
        file.AddRange(Number(42, 2));
        file.AddRange(Number(8 + body.Length, 4));
        file.AddRange(body);

        file.AddRange(Number(tags.Count, 2));

        foreach (var (id, type, value) in tags)
        {
            file.AddRange(Number(id, 2));
            file.AddRange(Number(type, 2));
            file.AddRange(Number(1, 4));
            file.AddRange(id == 273 ? Number(8, 4) : type == 3 ? [.. Number(value, 2), 0, 0] : Number(value, 4));
        }

        file.AddRange(Number(0, 4));

        return [.. file];
    }

    private static byte[] Number(int value, int size)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++) bytes[i] = (byte)(value >> (8 * i));

        return bytes;
    }

    /// <summary>Writes bits biggest first, which is the order a fax is written in.</summary>
    private sealed class BitWriter
    {
        private readonly List<byte> _bytes = [];
        private int _buffer;
        private int _count;

        public void Write(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                _buffer = (_buffer << 1) | ((value >> i) & 1);
                _count++;

                if (_count != 8) continue;

                _bytes.Add((byte)_buffer);
                _buffer = 0;
                _count = 0;
            }
        }

        public void AlignToByte()
        {
            while (_count != 0) Write(0, 1);
        }

        public byte[] ToArray()
        {
            var copy = new List<byte>(_bytes);

            if (_count > 0) copy.Add((byte)(_buffer << (8 - _count)));

            return [.. copy];
        }
    }
}
