using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The mark a run asks for over each of its characters, from <c>w:em</c>.
/// </summary>
/// <remarks>
/// Word draws each mark as a character of its own, at the text's size, in an East Asian face: a
/// fullwidth stop for the dot and for the dot below, an ideographic comma for the comma, a ring
/// above for the circle. emphasis-mark-probe reads the rest off its page:
///
///   what gets one   every character but a space — punctuation included, so "a,b" takes three
///   where it sits   centred over the character by the mark's **ink**, not by its advance: Word's
///                   fullwidth stop carries its dot a sixth of an em from the glyph's own edge and
///                   the mark still lands in the middle of the letter
///   how high        the dot and the comma stand the type size and a step of the grid above the
///                   baseline — exact at twelve, twenty-four and forty-eight point, a step out at
///                   eight — the ring three tenths of the size above it, and the dot below three
///                   eighths of the size under it
///   the line        grows to hold whatever stands above the text's own ascent
///
/// Which face carries the mark depends on what is installed, so the glyph's own origin is not
/// compared: what is compared is where the marks fall relative to each other, which is the rule
/// about centring, and where their baselines sit, which is the rule about height.
/// </remarks>
public class EmphasisMarkTests(ITestOutputHelper output)
{
    /// <summary>Every character but a space takes a mark.</summary>
    [Fact]
    public void Every_character_but_a_space_takes_one()
    {
        if (TestFonts.SkipForMissingFonts("emphasis-mark-probe")) return;

        var marks = Marks(Ours(), 2);

        output.WriteLine($"{marks.Count} marks over the page's three lines");

        // "a b" takes two, "a,b" takes three, and "marked" takes six with none over "plain" —
        // eleven, which is what Word's own page holds.
        Assert.Equal(11, marks.Count);
    }

    /// <summary>
    /// The marks step along with the characters they stand over, which is the centring rule.
    /// </summary>
    /// <remarks>
    /// Word's own steps over "marked" are 7.33, 4.661, 4.998, 5.663 and 5.663 points, which are
    /// the advances of m, a, r, k and e in Times at twelve. Ours are the same to a hundredth.
    /// </remarks>
    [Fact]
    public void The_marks_step_with_the_characters()
    {
        if (TestFonts.SkipForMissingFonts("emphasis-mark-probe")) return;

        var steps = Steps(Marks(Ours(), 2));

        output.WriteLine(string.Join(" ", steps.Select(s => s.ToString("0.###"))));

        double[] overMarked = [7.33, 4.661, 4.998, 5.663, 5.663];

        // Eight steps in all: one over "a b", two over "a,b", and five over "marked".
        Assert.Equal(8, steps.Count);

        foreach (var (expected, got) in overMarked.Zip(steps.TakeLast(overMarked.Length)))
            Assert.Equal(expected, got, 0.01);
    }

    /// <summary>
    /// How far above or below the baseline each kind stands, at twelve point.
    /// </summary>
    /// <remarks>
    /// Word's own page: the dot and the comma 12.24 points above the baseline, the ring 3.6 above,
    /// the dot below 4.56 under. The first page of the probe holds the four in that order.
    /// </remarks>
    [Fact]
    public void Each_kind_stands_where_word_puts_it()
    {
        if (TestFonts.SkipForMissingFonts("emphasis-mark-probe")) return;

        var ours = Offsets(Ours());

        output.WriteLine(string.Join(" ", ours.Select(o => o.ToString("0.###"))));

        Assert.Equal([-12.24, -12.24, -3.6, 4.56], ours);
    }

    private static byte[] Ours() => Converter.Convert(Fixtures.Build("emphasis-mark-probe"),
        new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>The marks of a page, in the order they stand across it.</summary>
    private static List<(double X, double BaselineY)> Marks(byte[] pdf, int page) =>
        [.. PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && IsMark(run.Text))
            .OrderBy(run => run.BaselineY).ThenBy(run => run.X)
            .Select(run => (run.X, run.BaselineY))];

    /// <summary>
    /// Whether a run is marks rather than text. Which characters they come out as depends on the
    /// face that carried them and on what its own map says, so what is asked is that the run holds
    /// no letters, no digits and no spaces — which the probe's own text always does.
    /// </summary>
    private static bool IsMark(string text) =>
        text.Trim().Length > 0 &&
        text.All(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c) && c != ',');

    /// <summary>How far each mark stands from the one before it, within a line.</summary>
    private static List<double> Steps(List<(double X, double BaselineY)> marks)
    {
        var steps = new List<double>();

        for (var i = 1; i < marks.Count; i++)
        {
            if (Math.Abs(marks[i].BaselineY - marks[i - 1].BaselineY) < 0.01)
                steps.Add(Math.Round(marks[i].X - marks[i - 1].X, 3));
        }

        return steps;
    }

    /// <summary>
    /// How far the marks of each of the first page's four lines stand from the text they mark.
    /// </summary>
    private static List<double> Offsets(byte[] pdf)
    {
        var runs = PdfTextExtractor.Extract(pdf).Where(run => run.PageIndex == 0).ToList();

        // Each of the four lines holds one group of marks, and the groups come down the page in
        // the same order the lines do.
        var lines = runs.Where(run => run.Text.Contains("abc", StringComparison.Ordinal))
            .OrderBy(run => run.BaselineY)
            .ToList();

        var groups = runs.Where(run => IsMark(run.Text))
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(group => group.Key)
            .ToList();

        return [.. lines.Zip(groups, (line, group) => Math.Round(group.Key - line.BaselineY, 3))];
    }
}
