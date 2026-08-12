using System.Globalization;
using System.Text;

namespace n8PDF.Tests.Support.PdfReading;

/// <summary>One visual line, assembled from however many runs the producer chose to emit.</summary>
public sealed record TextLine(int PageIndex, double BaselineY, double StartX, double EndX, string Text)
{
    public double Width => EndX - StartX;
}

/// <summary>A matched pair of lines and the differences between them.</summary>
public sealed record LineDelta(int Index, TextLine? Ours, TextLine? Theirs)
{
    public bool IsMatched => Ours is not null && Theirs is not null;

    public double StartXDelta => IsMatched ? Ours!.StartX - Theirs!.StartX : double.NaN;

    public double BaselineDelta => IsMatched ? Ours!.BaselineY - Theirs!.BaselineY : double.NaN;

    public double WidthDelta => IsMatched ? Ours!.Width - Theirs!.Width : double.NaN;

    public bool TextMatches => IsMatched &&
        string.Equals(Normalize(Ours!.Text), Normalize(Theirs!.Text), StringComparison.Ordinal);

    /// <summary>
    /// Whitespace is normalised before comparison. Producers split runs differently and encode
    /// spacing in different ways, so exact whitespace is not a meaningful difference.
    /// </summary>
    public static string Normalize(string text) => string.Join(' ',
        text.Split((char[])[' ', '\t', '\n', '\r', ' '], StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>The full comparison of two documents.</summary>
public sealed record ComparisonReport(string Name, IReadOnlyList<LineDelta> Deltas)
{
    public IEnumerable<LineDelta> Matched => Deltas.Where(d => d.IsMatched);

    public int LineCount => Deltas.Count;

    public int UnmatchedCount => Deltas.Count(d => !d.IsMatched);

    public int TextMismatchCount => Matched.Count(d => !d.TextMatches);

    public double MaxAbsStartXDelta => Matched.Any() ? Matched.Max(d => Math.Abs(d.StartXDelta)) : 0;

    public double MaxAbsBaselineDelta => Matched.Any() ? Matched.Max(d => Math.Abs(d.BaselineDelta)) : 0;

    public double MaxAbsWidthDelta => Matched.Any() ? Matched.Max(d => Math.Abs(d.WidthDelta)) : 0;

    /// <summary>
    /// Mean signed baseline difference. A consistent non-zero value points at a systematic cause
    /// such as the line-height rule, which is far more useful to know than the maximum alone.
    /// </summary>
    public double MeanBaselineDelta => Matched.Any() ? Matched.Average(d => d.BaselineDelta) : 0;

    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"=== {Name} ===\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"lines: {LineCount}  unmatched: {UnmatchedCount}  text mismatches: {TextMismatchCount}\n");
        sb.Append(CultureInfo.InvariantCulture,
            $"max |dx|: {MaxAbsStartXDelta:0.###}pt   max |dy|: {MaxAbsBaselineDelta:0.###}pt   " +
            $"max |dw|: {MaxAbsWidthDelta:0.###}pt   mean dy: {MeanBaselineDelta:+0.###;-0.###;0}pt\n");

        sb.Append("  #  page        x (ours/word)          y (ours/word)         width (ours/word)   text\n");

        foreach (var delta in Deltas)
        {
            if (!delta.IsMatched)
            {
                var only = delta.Ours is not null ? "ours only" : "word only";
                var line = delta.Ours ?? delta.Theirs!;
                sb.Append(CultureInfo.InvariantCulture,
                    $"{delta.Index,3}  {line.PageIndex,4}  {only,-58}  \"{Truncate(line.Text)}\"\n");
                continue;
            }

            var flag = delta.TextMatches ? " " : "!";
            sb.Append(CultureInfo.InvariantCulture,
                $"{delta.Index,3}  {delta.Ours!.PageIndex,4}  " +
                $"{delta.Ours.StartX,7:0.##}/{delta.Theirs!.StartX,-7:0.##} {delta.StartXDelta,+7:0.##}  " +
                $"{delta.Ours.BaselineY,7:0.##}/{delta.Theirs.BaselineY,-7:0.##} {delta.BaselineDelta,+7:0.##}  " +
                $"{delta.Ours.Width,7:0.##}/{delta.Theirs.Width,-7:0.##} {delta.WidthDelta,+7:0.##} {flag} " +
                $"\"{Truncate(delta.Ours.Text)}\"\n");

            // When the text differs, both sides in full: it is the only way to tell a genuine
            // line-breaking difference from an artefact of reconstructing Word's spacing.
            if (!delta.TextMatches)
            {
                sb.Append(CultureInfo.InvariantCulture, $"       ours: \"{delta.Ours.Text}\"\n");
                sb.Append(CultureInfo.InvariantCulture, $"       word: \"{delta.Theirs.Text}\"\n");
            }
        }

        return sb.ToString();
    }

    private static string Truncate(string text) => text.Length <= 46 ? text : text[..46] + "…";
}

/// <summary>
/// Turns two PDFs into a line-by-line difference in points.
/// </summary>
/// <remarks>
/// Comparison happens at line level rather than run level because run boundaries are an
/// implementation detail: Word emits a justified line as dozens of runs while n8PDF emits one per
/// formatting change. Lines are the smallest unit both producers agree on.
/// </remarks>
public static class PdfLineComparison
{
    /// <summary>
    /// Groups runs into lines. Runs on the same baseline belong to the same line; the tolerance
    /// absorbs the sub-point baseline jitter produced by superscripts and mixed font sizes.
    /// </summary>
    public static List<TextLine> GroupIntoLines(IEnumerable<ExtractedTextRun> runs, double tolerance = 1.0)
    {
        var lines = new List<TextLine>();

        foreach (var pageGroup in runs.GroupBy(r => r.PageIndex).OrderBy(g => g.Key))
        {
            var remaining = pageGroup.OrderBy(r => r.BaselineY).ThenBy(r => r.X).ToList();
            var index = 0;

            while (index < remaining.Count)
            {
                var baseline = remaining[index].BaselineY;
                var cluster = new List<ExtractedTextRun>();

                while (index < remaining.Count && Math.Abs(remaining[index].BaselineY - baseline) <= tolerance)
                {
                    cluster.Add(remaining[index]);
                    index++;
                }

                var ordered = cluster.OrderBy(r => r.X).ToList();

                // Word draws a trailing space for the paragraph mark, styled with the document
                // default font rather than the run's. n8PDF drops trailing spaces because they
                // are invisible and would make a justified line measure past the margin. Ignoring
                // them on both sides keeps the comparison about the visible text.
                while (ordered.Count > 1 && string.IsNullOrWhiteSpace(ordered[^1].Text))
                    ordered.RemoveAt(ordered.Count - 1);

                // Word emits a line as many runs and does not always encode the spaces between
                // words as characters, relying on TJ adjustments instead. A visible horizontal
                // gap therefore has to be reconstituted as a space or the text will not match.
                var text = new StringBuilder();
                for (var i = 0; i < ordered.Count; i++)
                {
                    if (i > 0)
                    {
                        var gap = ordered[i].X - (ordered[i - 1].X + ordered[i - 1].Width);
                        var threshold = Math.Max(0.6, ordered[i].FontSize * 0.12);

                        if (gap > threshold && !text.ToString().EndsWith(' ') && !ordered[i].Text.StartsWith(' '))
                            text.Append(' ');
                    }

                    text.Append(ordered[i].Text);
                }

                // A run may itself end in whitespace, so the extent is trimmed by the width of any
                // trailing space rather than only by dropping whole runs.
                var last = ordered[^1];
                var trailing = last.Text.Length - last.Text.TrimEnd().Length;
                var trailingWidth = trailing > 0 && last.Text.Length > 0
                    ? last.Width * trailing / last.Text.Length
                    : 0;

                lines.Add(new TextLine(
                    pageGroup.Key,
                    Math.Round(ordered.Min(r => r.BaselineY), 4),
                    Math.Round(ordered.Min(r => r.X), 4),
                    Math.Round(ordered.Max(r => r.X + r.Width) - trailingWidth, 4),
                    text.ToString().TrimEnd()));
            }
        }

        return lines;
    }

    /// <summary>Compares two documents line by line.</summary>
    public static ComparisonReport Compare(string name, byte[] ours, byte[] theirs)
    {
        var ourLines = GroupIntoLines(PdfTextExtractor.Extract(ours));
        var theirLines = GroupIntoLines(PdfTextExtractor.Extract(theirs));

        return Compare(name, ourLines, theirLines);
    }

    public static ComparisonReport Compare(string name, List<TextLine> ourLines, List<TextLine> theirLines)
    {
        var deltas = new List<LineDelta>();

        // Align by text where possible so that one extra or missing line does not shift every
        // subsequent comparison and drown the real differences.
        var i = 0;
        var j = 0;
        var index = 0;

        while (i < ourLines.Count || j < theirLines.Count)
        {
            if (i >= ourLines.Count)
            {
                deltas.Add(new LineDelta(index++, null, theirLines[j++]));
                continue;
            }

            if (j >= theirLines.Count)
            {
                deltas.Add(new LineDelta(index++, ourLines[i++], null));
                continue;
            }

            var ourText = LineDelta.Normalize(ourLines[i].Text);
            var theirText = LineDelta.Normalize(theirLines[j].Text);

            if (ourText == theirText)
            {
                deltas.Add(new LineDelta(index++, ourLines[i++], theirLines[j++]));
                continue;
            }

            // Texts differ: look a little way ahead for a re-synchronisation point rather than
            // pairing up lines that have nothing to do with each other.
            var resyncOurs = FindMatch(ourLines, i, theirText, 4);
            var resyncTheirs = FindMatch(theirLines, j, ourText, 4);

            if (resyncOurs > i && (resyncTheirs <= j || resyncOurs - i <= resyncTheirs - j))
            {
                deltas.Add(new LineDelta(index++, ourLines[i++], null));
                continue;
            }

            if (resyncTheirs > j)
            {
                deltas.Add(new LineDelta(index++, null, theirLines[j++]));
                continue;
            }

            // No resynchronisation nearby; pair them and let the text mismatch be reported.
            deltas.Add(new LineDelta(index++, ourLines[i++], theirLines[j++]));
        }

        return new ComparisonReport(name, deltas);
    }

    private static int FindMatch(List<TextLine> lines, int from, string text, int lookahead)
    {
        for (var k = from; k < Math.Min(lines.Count, from + lookahead); k++)
        {
            if (LineDelta.Normalize(lines[k].Text) == text) return k;
        }

        return -1;
    }
}
