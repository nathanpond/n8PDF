namespace n8PDF.Text;

/// <summary>
/// The Unicode bidirectional algorithm: which way each character of a line is drawn, and in what
/// order.
/// </summary>
/// <remarks>
/// A line of Hebrew or Arabic is not simply a line drawn backwards. Text is stored in the order it
/// is read — first character first, whichever direction that is — and drawn in the order it
/// appears, and the two differ the moment a line holds both directions at once, which nearly every
/// real line does: a number is always written left to right, and so is a Latin name or an address
/// inside an otherwise right-to-left sentence.
///
/// So the algorithm gives every character a level: even for left-to-right, odd for right-to-left,
/// higher for each nesting. Reordering is then mechanical — take the longest runs of the highest
/// level and turn them round, then the level below, and so on down. Everything hard is in arriving
/// at the levels, and nearly all of that is about the characters with no direction of their own. A
/// comma between two Hebrew words belongs to the Hebrew; the same comma between a Hebrew word and
/// an English one belongs to whichever the paragraph as a whole runs.
///
/// This follows the standard's rules and its numbering: P2 and P3 for the paragraph, X1 to X8 for
/// the explicit marks, X10 for the sequences the rest is applied to, W1 to W7 for the weak
/// characters, N0 to N2 for the neutral ones, I1 and I2 for the levels themselves, and L1 and L2
/// for the reordering. The names in the code are those, so that a reader with the standard beside
/// them can find their place.
/// </remarks>
internal static class Bidi
{
    /// <summary>How deep the explicit marks may nest, which the standard fixes.</summary>
    private const int MaxDepth = 125;

    /// <summary>
    /// The direction a paragraph runs, where the document says rather than the text.
    /// </summary>
    public enum Direction
    {
        /// <summary>Whichever the first strong character of the text says.</summary>
        FromText,

        LeftToRight,
        RightToLeft
    }

    /// <summary>
    /// The result of running the algorithm: what level each character came out at, and which way
    /// the paragraph as a whole runs.
    /// </summary>
    public sealed class Result(byte[] levels, byte paragraphLevel)
    {
        public byte[] Levels { get; } = levels;

        public byte ParagraphLevel { get; } = paragraphLevel;

        public bool IsRightToLeft => (ParagraphLevel & 1) != 0;
    }

    /// <summary>Works out the level of every character of a paragraph.</summary>
    public static Result Resolve(string text, Direction direction = Direction.FromText)
    {
        var classes = new BidiClass[text.Length];
        for (var i = 0; i < text.Length; i++) classes[i] = ClassOf(text, i);

        var paragraphLevel = direction switch
        {
            Direction.LeftToRight => (byte)0,
            Direction.RightToLeft => (byte)1,
            _ => FirstStrong(classes, 0, text.Length)                                    // P2, P3
        };

        var levels = new byte[text.Length];
        var resolved = new BidiClass[text.Length];

        // The isolate initiator -> matching PDI map, and which PDIs match an opener, computed
        // once by a single stack pass rather than rescanned per isolate: the per-isolate scans
        // were O(N^2) on a run of isolate characters (#215). The isolate structure of classes is
        // stable through Explicit (it reclassifies only the embedding controls, not the isolates).
        var matchingPdi = new int[classes.Length];
        Array.Fill(matchingPdi, classes.Length);
        var isolateStack = new Stack<int>();
        for (var i = 0; i < classes.Length; i++)
        {
            if (classes[i] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI) isolateStack.Push(i);
            else if (classes[i] == BidiClass.PDI && isolateStack.Count > 0) matchingPdi[isolateStack.Pop()] = i;
        }

        var matchesIsolate = new bool[classes.Length];
        for (var i = 0; i < classes.Length; i++)
        {
            if (classes[i] is (BidiClass.LRI or BidiClass.RLI or BidiClass.FSI) && matchingPdi[i] < classes.Length)
                matchesIsolate[matchingPdi[i]] = true;
        }

        Explicit(classes, resolved, levels, paragraphLevel, matchingPdi);                 // X1–X8
        Sequences(text, classes, resolved, levels, paragraphLevel, matchingPdi, matchesIsolate);  // X10, W, N, I
        ResetLevels(classes, levels, paragraphLevel);                                    // L1

        return new Result(levels, paragraphLevel);
    }

    /// <summary>
    /// Puts a line's characters in the order they are drawn, given the levels its characters came
    /// out at. This is rule L2: the longest runs at the highest level are turned round, then those
    /// at the level below, down to the lowest odd level.
    /// </summary>
    /// <param name="text">
    /// The text the levels belong to, where the marks drawn on letters are to be kept with them.
    /// That is rule L3, and it matters to anything that draws rather than merely reorders: turning
    /// a right-to-left run round puts a mark before the letter it belongs on, and a mark is drawn
    /// on whatever it follows.
    /// </param>
    /// <returns>For each place on the line, which character of the text goes there.</returns>
    public static int[] Reorder(ReadOnlySpan<byte> levels, string? text = null)
    {
        var order = new int[levels.Length];
        for (var i = 0; i < order.Length; i++) order[i] = i;

        if (levels.Length == 0) return order;

        byte highest = 0;
        var lowestOdd = (byte)(MaxDepth + 1);

        foreach (var level in levels)
        {
            if (level > highest) highest = level;
            if ((level & 1) != 0 && level < lowestOdd) lowestOdd = level;
        }

        for (var level = highest; level >= lowestOdd && level > 0; level--)
        {
            for (var i = 0; i < levels.Length; i++)
            {
                if (levels[i] < level) continue;

                var start = i;
                while (i + 1 < levels.Length && levels[i + 1] >= level) i++;

                Array.Reverse(order, start, i - start + 1);
            }
        }

        if (text is not null) KeepMarksWithTheirLetters(order, text);

        return order;
    }

    /// <summary>
    /// L3: puts back the letter a run of marks belongs to, which turning the run round has left
    /// standing after them.
    /// </summary>
    private static void KeepMarksWithTheirLetters(int[] order, string text)
    {
        for (var i = 0; i < order.Length; i++)
        {
            // A letter and its marks come out of the reordering as a descending run, the marks
            // first and the letter they are drawn on last.
            if (order[i] >= text.Length || ClassOf(text, order[i]) != BidiClass.NSM) continue;

            var end = i;

            while (end + 1 < order.Length && order[end + 1] == order[end] - 1)
            {
                end++;

                if (order[end] >= text.Length || ClassOf(text, order[end]) != BidiClass.NSM) break;
            }

            if (end > i) Array.Reverse(order, i, end - i + 1);

            i = end;
        }
    }

    /// <summary>
    /// What a character is drawn as where the line around it runs right to left: a bracket faces
    /// the way the reader is going, so what is stored as an opening bracket is drawn as a closing
    /// one. This is rule L4.
    /// </summary>
    // The generated table is a flat (from, to) pair list; scanning it per character cost ~180
    // comparisons on every glyph of an RTL slice (#225). Index it once into a dictionary — the
    // table is generator output, so the structure lives here in the consumer, not in the file.
    private static readonly Dictionary<char, char> MirrorMap = BuildPairMap(BidiTables.Mirrored);
    private static readonly Dictionary<char, char> BracketMap = BuildPairMap(BidiTables.BracketPairs);

    private static Dictionary<char, char> BuildPairMap(int[] pairs)
    {
        var map = new Dictionary<char, char>(pairs.Length / 2);
        for (var i = 0; i < pairs.Length; i += 2) map[(char)pairs[i]] = (char)pairs[i + 1];
        return map;
    }

    public static char Mirror(char value) => MirrorMap.TryGetValue(value, out var m) ? m : value;

    /// <summary>The class of the character at a position, reading a surrogate pair as one.</summary>
    public static BidiClass ClassOf(string text, int at)
    {
        int codePoint = text[at];

        if (char.IsHighSurrogate(text[at]) && at + 1 < text.Length && char.IsLowSurrogate(text[at + 1]))
            codePoint = char.ConvertToUtf32(text[at], text[at + 1]);
        else if (char.IsLowSurrogate(text[at]) && at > 0 && char.IsHighSurrogate(text[at - 1]))
            codePoint = char.ConvertToUtf32(text[at - 1], text[at]);

        return ClassOf(codePoint);
    }

    public static BidiClass ClassOf(int codePoint)
    {
        var starts = BidiTables.RangeStarts;

        var low = 0;
        var high = starts.Length - 1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (starts[middle] <= codePoint)
            {
                if (middle == starts.Length - 1 || starts[middle + 1] > codePoint)
                    return BidiTables.RangeClasses[middle];

                low = middle + 1;
            }
            else high = middle - 1;
        }

        return BidiClass.L;
    }

    /// <summary>
    /// The level the first strong character of a range asks for, which is what a paragraph takes
    /// where nothing else says. Rules P2 and P3, and the same rules again for the text inside an
    /// isolate that asks to decide for itself.
    /// </summary>
    private static byte FirstStrong(BidiClass[] classes, int from, int to)
    {
        var isolates = 0;

        for (var i = from; i < to; i++)
        {
            switch (classes[i])
            {
                case BidiClass.LRI or BidiClass.RLI or BidiClass.FSI:
                    isolates++;
                    break;

                case BidiClass.PDI:
                    if (isolates > 0) isolates--;
                    break;

                case BidiClass.L when isolates == 0:
                    return 0;

                case BidiClass.R or BidiClass.AL when isolates == 0:
                    return 1;
            }
        }

        return 0;
    }

    /// <summary>
    /// The explicit marks: the embeddings, overrides and isolates a document may put around text
    /// to say what the algorithm cannot work out. Rules X1 to X8.
    /// </summary>
    private static void Explicit(
        BidiClass[] classes, BidiClass[] resolved, byte[] levels, byte paragraphLevel, int[] matchingPdi)
    {
        var stack = new Stack<(byte Level, BidiClass Override, bool Isolate)>();
        stack.Push((paragraphLevel, BidiClass.ON, false));

        var overflowIsolates = 0;
        var overflowEmbeddings = 0;
        var validIsolates = 0;

        for (var i = 0; i < classes.Length; i++)
        {
            var type = classes[i];
            resolved[i] = type;

            switch (type)
            {
                case BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO
                    or BidiClass.RLI or BidiClass.LRI or BidiClass.FSI:
                {
                    var isolate = type is BidiClass.RLI or BidiClass.LRI or BidiClass.FSI;

                    // An isolate is drawn, and takes the level of what it stands in.
                    if (isolate) levels[i] = stack.Peek().Level;

                    var rightToLeft = type switch
                    {
                        BidiClass.RLE or BidiClass.RLO or BidiClass.RLI => true,
                        BidiClass.FSI => FirstStrong(classes, i + 1, matchingPdi[i]) == 1,   // (#215)
                        _ => false
                    };

                    if (isolate && stack.Peek().Override != BidiClass.ON)
                        resolved[i] = stack.Peek().Override;

                    var next = rightToLeft
                        ? (byte)((stack.Peek().Level + 1) | 1)
                        : (byte)((stack.Peek().Level + 2) & ~1);

                    if (next <= MaxDepth && overflowIsolates == 0 && overflowEmbeddings == 0)
                    {
                        if (isolate) validIsolates++;

                        stack.Push((next, type switch
                        {
                            BidiClass.RLO => BidiClass.R,
                            BidiClass.LRO => BidiClass.L,
                            _ => BidiClass.ON
                        }, isolate));

                        if (!isolate) levels[i] = next;
                    }
                    else if (isolate) overflowIsolates++;
                    else if (overflowIsolates == 0) overflowEmbeddings++;

                    if (!isolate) resolved[i] = BidiClass.BN;

                    break;
                }

                case BidiClass.PDI:
                {
                    if (overflowIsolates > 0) overflowIsolates--;
                    else if (validIsolates > 0)
                    {
                        overflowEmbeddings = 0;

                        while (!stack.Peek().Isolate) stack.Pop();

                        stack.Pop();
                        validIsolates--;
                    }

                    levels[i] = stack.Peek().Level;

                    if (stack.Peek().Override != BidiClass.ON) resolved[i] = stack.Peek().Override;

                    break;
                }

                case BidiClass.PDF:
                {
                    levels[i] = stack.Peek().Level;
                    resolved[i] = BidiClass.BN;

                    if (overflowIsolates > 0) { }
                    else if (overflowEmbeddings > 0) overflowEmbeddings--;
                    else if (!stack.Peek().Isolate && stack.Count > 1) stack.Pop();

                    break;
                }

                case BidiClass.B:
                {
                    // A paragraph mark ends everything and takes the paragraph's own level.
                    stack.Clear();
                    stack.Push((paragraphLevel, BidiClass.ON, false));

                    overflowIsolates = overflowEmbeddings = validIsolates = 0;
                    levels[i] = paragraphLevel;

                    break;
                }

                default:
                {
                    levels[i] = stack.Peek().Level;

                    if (stack.Peek().Override != BidiClass.ON) resolved[i] = stack.Peek().Override;

                    break;
                }
            }
        }
    }

    /// <summary>Where the isolate opened at a position is closed, or the end of the text.</summary>
    private static int MatchingPdi(BidiClass[] classes, int at)
    {
        var depth = 1;

        for (var i = at + 1; i < classes.Length; i++)
        {
            if (classes[i] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI) depth++;
            else if (classes[i] == BidiClass.PDI && --depth == 0) return i;
        }

        return classes.Length;
    }

    /// <summary>
    /// Divides the text into the sequences the rest of the algorithm runs over — a level run, or
    /// several joined across an isolate — and works each one out. Rule X10.
    /// </summary>
    private static void Sequences(
        string text, BidiClass[] classes, BidiClass[] resolved, byte[] levels, byte paragraphLevel,
        int[] matchingPdi, bool[] matchesIsolate)
    {
        var length = classes.Length;

        // The characters that count. Everything the explicit rules removed is passed over, and
        // put back at the level of what precedes it once the levels are settled.
        var kept = new List<int>(length);
        for (var i = 0; i < length; i++)
        {
            if (resolved[i] != BidiClass.BN) kept.Add(i);
        }

        var used = new bool[length];

        foreach (var start in kept)
        {
            if (used[start]) continue;
            if (classes[start] == BidiClass.PDI && matchesIsolate[start]) continue;   // precomputed (#215)

            // A sequence runs from here to the end of its level run, and on across an isolate
            // that is closed later.
            var sequence = new List<int>();
            var at = start;

            while (true)
            {
                var level = levels[at];
                var last = at;

                // Whether a kept index of a different level has appeared since the last one added:
                // tracked as the loop runs rather than rescanned with kept.Any at every element,
                // which was O(N^2) on text interleaved with dropped control characters (#214).
                var sawDifferentLevel = false;

                foreach (var i in kept)
                {
                    if (i < at) continue;

                    if (levels[i] != level)
                    {
                        if (i > last) sawDifferentLevel = true;
                        continue;
                    }

                    if (i > at && used[i]) break;
                    if (i > last + 1 && sawDifferentLevel) break;

                    sequence.Add(i);
                    used[i] = true;
                    last = i;
                    sawDifferentLevel = false;
                }

                // An isolate that is closed carries the sequence on after the character closing
                // it, so that text either side of an isolate is worked out as one.
                if (classes[last] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI)
                {
                    var closing = matchingPdi[last];   // precomputed (#215)

                    if (closing < length && !used[closing])
                    {
                        at = closing;
                        continue;
                    }
                }

                break;
            }

            if (sequence.Count == 0) continue;

            Resolve(text, classes, resolved, levels, sequence, paragraphLevel, kept);
        }

        // Anything removed takes the level of the character before it, or the paragraph's own
        // where it stands first. Nothing is drawn for these, but they are still counted and still
        // have to be given a level for the counting to come out right.
        for (var i = 0; i < length; i++)
        {
            if (resolved[i] != BidiClass.BN) continue;

            levels[i] = i == 0 ? paragraphLevel : levels[i - 1];
        }
    }

    /// <summary>The weak, neutral and implicit rules over one sequence: W1–W7, N0–N2, I1–I2.</summary>
    private static void Resolve(
        string text, BidiClass[] classes, BidiClass[] resolved, byte[] levels, List<int> sequence,
        byte paragraphLevel, List<int> kept)
    {
        var level = levels[sequence[0]];

        // What the sequence is taken to sit between: the level of what comes before it and after
        // it, the higher of the two deciding which direction the neutrals at its edges take.
        var sos = DirectionOf(Math.Max(level, LevelBefore(kept, levels, sequence[0], paragraphLevel)));
        var last = sequence[^1];

        var eos = classes[last] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI &&
                  MatchingPdi(classes, last) >= classes.Length
            ? DirectionOf(Math.Max(level, paragraphLevel))
            : DirectionOf(Math.Max(level, LevelAfter(kept, levels, last, paragraphLevel)));

        var types = new BidiClass[sequence.Count];
        for (var i = 0; i < sequence.Count; i++) types[i] = resolved[sequence[i]];

        // W1: a mark takes the type of what it is drawn on.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] != BidiClass.NSM) continue;

            types[i] = i == 0
                ? sos
                : types[i - 1] is BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI
                    ? BidiClass.ON
                    : types[i - 1];
        }

        // W2: a European digit after an Arabic letter is an Arabic number. What "after" means
        // where nothing precedes it is what the sequence is taken to sit after, which is why sos
        // is one of the answers the search can end on.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] == BidiClass.EN && FirstStrongBefore(types, i, sos) == BidiClass.AL)
                types[i] = BidiClass.AN;
        }

        // W3: an Arabic letter is right-to-left from here on.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] == BidiClass.AL) types[i] = BidiClass.R;
        }

        // W4: a single separator between two numbers of a kind joins them.
        for (var i = 1; i + 1 < types.Length; i++)
        {
            if (types[i] == BidiClass.ES && types[i - 1] == BidiClass.EN && types[i + 1] == BidiClass.EN)
                types[i] = BidiClass.EN;

            if (types[i] == BidiClass.CS && types[i - 1] == types[i + 1] &&
                types[i - 1] is BidiClass.EN or BidiClass.AN)
            {
                types[i] = types[i - 1];
            }
        }

        // W5: a run of terminators beside a European number joins it.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] != BidiClass.ET) continue;

            var start = i;
            while (i < types.Length && types[i] == BidiClass.ET) i++;

            var before = start > 0 && types[start - 1] == BidiClass.EN;
            var after = i < types.Length && types[i] == BidiClass.EN;

            if (before || after)
            {
                for (var j = start; j < i; j++) types[j] = BidiClass.EN;
            }

            i--;
        }

        // W6: what is left of the separators and terminators is neutral.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] is BidiClass.ET or BidiClass.ES or BidiClass.CS) types[i] = BidiClass.ON;
        }

        // W7: a European digit with a left-to-right letter before it is left-to-right — and,
        // again, what stands before the sequence counts as such a letter.
        for (var i = 0; i < types.Length; i++)
        {
            if (types[i] == BidiClass.EN && FirstStrongBefore(types, i, sos) == BidiClass.L)
                types[i] = BidiClass.L;
        }

        Brackets(text, sequence, types, level, sos);                                     // N0

        // N1: neutrals between two of the same direction take it; a number counts as right.
        // N2: the rest take the direction of the sequence itself.
        for (var i = 0; i < types.Length; i++)
        {
            if (!IsNeutral(types[i])) continue;

            var start = i;
            while (i < types.Length && IsNeutral(types[i])) i++;

            var before = start == 0 ? sos : DirectionOfType(types[start - 1]);
            var after = i == types.Length ? eos : DirectionOfType(types[i]);

            var taken = before == after ? before : DirectionOf(level);

            for (var j = start; j < i; j++) types[j] = taken;

            i--;
        }

        // I1, I2: the levels themselves.
        for (var i = 0; i < types.Length; i++)
        {
            var at = levels[sequence[i]];

            levels[sequence[i]] = (at & 1) == 0
                ? types[i] switch
                {
                    BidiClass.R => (byte)(at + 1),
                    BidiClass.AN or BidiClass.EN => (byte)(at + 2),
                    _ => at
                }
                : types[i] switch
                {
                    BidiClass.L or BidiClass.AN or BidiClass.EN => (byte)(at + 1),
                    _ => at
                };
        }
    }

    /// <summary>
    /// N0: a pair of brackets takes one direction, so that a bracketed word inside a sentence
    /// running the other way keeps its brackets the right way round.
    /// </summary>
    private static void Brackets(
        string text, List<int> sequence, BidiClass[] types, byte level, BidiClass sos)
    {
        var stack = new Stack<(int Position, char Closing)>();
        var pairs = new List<(int Open, int Close)>();

        for (var i = 0; i < sequence.Count && stack.Count < 63; i++)
        {
            if (types[i] != BidiClass.ON) continue;

            var at = sequence[i];
            if (at >= text.Length) continue;

            var character = Canonical(text[at]);

            if (ClosingOf(character) is { } closing)
            {
                stack.Push((i, closing));
                continue;
            }

            for (var depth = 0; depth < stack.Count; depth++)
            {
                var candidate = stack.ElementAt(depth);
                if (candidate.Closing != character) continue;

                pairs.Add((candidate.Position, i));

                for (var pop = 0; pop <= depth; pop++) stack.Pop();
                break;
            }
        }

        pairs.Sort((a, b) => a.Open.CompareTo(b.Open));

        var direction = DirectionOf(level);

        foreach (var (open, close) in pairs)
        {
            // Whether the text inside runs the way the sequence does.
            var inside = false;
            var opposite = false;

            for (var i = open + 1; i < close; i++)
            {
                var found = DirectionOfType(types[i]);
                if (found == BidiClass.ON) continue;

                if (found == direction) { inside = true; break; }

                opposite = true;
            }

            if (inside)
            {
                Set(open, close, direction);
                continue;
            }

            if (!opposite) continue;

            // Nothing inside runs our way but something runs the other: the brackets follow the
            // context before them where that agrees, and the sequence otherwise.
            var previous = sos;

            for (var i = open - 1; i >= 0; i--)
            {
                var found = DirectionOfType(types[i]);
                if (found == BidiClass.ON) continue;

                previous = found;
                break;
            }

            Set(open, close, previous != direction ? previous : direction);
        }

        void Set(int open, int close, BidiClass taken)
        {
            types[open] = types[close] = taken;

            // A mark drawn on a bracket goes with it.
            for (var i = open + 1; i < types.Length && i < close; i++)
            {
                if (Bidi.ClassOf(text, sequence[i]) != BidiClass.NSM) break;

                types[i] = taken;
            }

            for (var i = close + 1; i < types.Length; i++)
            {
                if (Bidi.ClassOf(text, sequence[i]) != BidiClass.NSM) break;

                types[i] = taken;
            }
        }
    }

    /// <summary>The closing bracket that matches an opening one, or null where it is not one.</summary>
    private static char? ClosingOf(char value) =>
        BracketMap.TryGetValue(value, out var c) ? c : null;

    /// <summary>
    /// The two brackets Unicode says are the same as two others, which have to be matched with
    /// them: the standard names them outright rather than by a rule.
    /// </summary>
    private static char Canonical(char value) => value switch
    {
        '〈' => '〈',
        '〉' => '〉',
        _ => value
    };

    private static bool IsNeutral(BidiClass type) =>
        type is BidiClass.B or BidiClass.S or BidiClass.WS or BidiClass.ON
            or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI;

    private static BidiClass DirectionOf(int level) => (level & 1) == 0 ? BidiClass.L : BidiClass.R;

    private static BidiClass DirectionOfType(BidiClass type) => type switch
    {
        BidiClass.L => BidiClass.L,
        BidiClass.R or BidiClass.EN or BidiClass.AN => BidiClass.R,
        _ => BidiClass.ON
    };

    /// <summary>
    /// The nearest strong type before a position, or what the sequence sits after where there is
    /// none: the standard's backward searches end on sos as though it were a character.
    /// </summary>
    private static BidiClass FirstStrongBefore(BidiClass[] types, int at, BidiClass sos)
    {
        for (var i = at - 1; i >= 0; i--)
        {
            if (types[i] is BidiClass.L or BidiClass.R or BidiClass.AL) return types[i];
        }

        return sos;
    }

    private static byte LevelBefore(List<int> kept, byte[] levels, int at, byte paragraphLevel)
    {
        for (var i = kept.Count - 1; i >= 0; i--)
        {
            if (kept[i] < at) return levels[kept[i]];
        }

        return paragraphLevel;
    }

    private static byte LevelAfter(List<int> kept, byte[] levels, int at, byte paragraphLevel)
    {
        foreach (var i in kept)
        {
            if (i > at) return levels[i];
        }

        return paragraphLevel;
    }

    /// <summary>
    /// L1: a tab, a paragraph mark, and any whitespace before one of them or at the end of the
    /// line goes back to the paragraph's own direction — so a line of Hebrew ending in a full stop
    /// and a space does not carry that space round to the wrong end.
    /// </summary>
    private static void ResetLevels(BidiClass[] classes, byte[] levels, byte paragraphLevel)
    {
        var resetting = true;

        for (var i = classes.Length - 1; i >= 0; i--)
        {
            switch (classes[i])
            {
                case BidiClass.B or BidiClass.S:
                    levels[i] = paragraphLevel;
                    resetting = true;
                    break;

                case BidiClass.WS or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI:
                    if (resetting) levels[i] = paragraphLevel;
                    break;

                // The marks themselves are not drawn, and count as part of the run of whitespace
                // this rule resets — without ending it, since what stands before them may be
                // whitespace too.
                case BidiClass.RLE or BidiClass.LRE or BidiClass.RLO or BidiClass.LRO
                    or BidiClass.PDF or BidiClass.BN:
                    if (resetting) levels[i] = paragraphLevel;
                    break;

                default:
                    resetting = false;
                    break;
            }
        }
    }
}
