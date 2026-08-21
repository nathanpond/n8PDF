namespace n8PDF.Text;

/// <summary>
/// Where a word may be broken at the end of a line.
/// </summary>
/// <remarks>
/// Liang's algorithm, and the patterns TeX has carried since 1990. A word is wrapped in dots,
/// every substring of it is looked up in the table, and each position keeps the largest number any
/// pattern gave it; an odd number means a break is allowed there. It is not a rule anyone could
/// state — the table is the rule — but it agrees with the places Word breaks: hyphenation-probe
/// has Word breaking conspicuous, examples, misunderstanding, understanding and organisation, and
/// every one of the five is a point this finds.
///
/// Two letters must stay behind and three must go on, which is what the pattern file states and
/// what Word does with these words. A handful of words no pattern gets right are spelled out
/// instead, hyphens and all.
///
/// English only. A word with a letter outside the alphabet in it is left whole, which is what
/// happens to every other language: hyphenating one by English rules would be worse than leaving
/// it alone, and Word has a dictionary for each language where this has one.
/// </remarks>
internal static class Hyphenator
{
    /// <summary>How many letters must stay behind, and how many must go on.</summary>
    /// <remarks>
    /// Two and two, which is Word's rule rather than the pattern file's: the file states two and
    /// three, as a typesetter would, but hyphenation-probe has Word breaking PARTICULARLY after
    /// PARTICULAR and leaving LY to the next line. Two before is what Word does as well, breaking
    /// organisation after "or".
    /// </remarks>
    private const int Before = 2;
    private const int After = 2;

    private static readonly Dictionary<string, byte[]> Patterns = ReadPatterns();
    private static readonly Dictionary<string, int[]> Exceptions = ReadExceptions();

    /// <summary>
    /// Where the word may be broken, as the number of letters before each break, in order.
    /// </summary>
    public static IReadOnlyList<int> Points(string word)
    {
        if (word.Length < Before + After) return [];

        var lower = Lowered(word);
        if (lower is null) return [];

        if (Exceptions.TryGetValue(lower, out var spelled)) return Allowed(spelled, word.Length);

        // The word between dots, so that a pattern can speak about its beginning and its end.
        var padded = string.Concat(".", lower, ".");

        // One more than the letters: the value before each of them, and one past the last.
        var values = new byte[padded.Length + 1];

        for (var i = 0; i < padded.Length; i++)
        for (var j = i + 1; j <= padded.Length; j++)
        {
            if (!Patterns.TryGetValue(padded[i..j], out var pattern)) continue;

            for (var k = 0; k < pattern.Length; k++)
                values[i + k] = Math.Max(values[i + k], pattern[k]);
        }

        var points = new List<int>();

        // values[i + 1] is the number between the i'th letter of the word and the one after it.
        for (var i = Before; i <= word.Length - After; i++)
            if (values[i + 1] % 2 == 1)
                points.Add(i);

        return points;
    }

    /// <summary>The word in lower case, or null where it is not a word this can break.</summary>
    private static string? Lowered(string word)
    {
        Span<char> letters = word.Length <= 64 ? stackalloc char[word.Length] : new char[word.Length];

        for (var i = 0; i < word.Length; i++)
        {
            var c = char.ToLowerInvariant(word[i]);
            if (c is < 'a' or > 'z') return null;

            letters[i] = c;
        }

        return new string(letters);
    }

    /// <summary>The points of a spelled-out word, kept to the two-and-three rule like any other.</summary>
    private static List<int> Allowed(int[] points, int length)
    {
        var kept = new List<int>();

        foreach (var point in points)
            if (point >= Before && point <= length - After)
                kept.Add(point);

        return kept;
    }

    private static Dictionary<string, byte[]> ReadPatterns()
    {
        var table = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (var pattern in HyphenationTables.Patterns.Split(
                     (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var letters = new char[pattern.Length];
            var values = new byte[pattern.Length + 1];
            var count = 0;

            foreach (var c in pattern)
            {
                if (char.IsAsciiDigit(c)) values[count] = (byte)(c - '0');
                else letters[count++] = c;
            }

            table[new string(letters, 0, count)] = values[..(count + 1)];
        }

        return table;
    }

    private static Dictionary<string, int[]> ReadExceptions()
    {
        var table = new Dictionary<string, int[]>(StringComparer.Ordinal);

        foreach (var word in HyphenationTables.Exceptions.Split(
                     (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var points = new List<int>();
            var letters = 0;

            foreach (var c in word)
            {
                if (c == '-') points.Add(letters);
                else letters++;
            }

            table[word.Replace("-", string.Empty)] = points.ToArray();
        }

        return table;
    }
}
