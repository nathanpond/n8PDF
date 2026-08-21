using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where a script sits along the letter it is on, against Word's own.
/// </summary>
/// <remarks>
/// The face states two things about that: how far the letter leans, and a kern for each of the
/// four corners of each glyph, given as a staircase of values by height. math-kern-probe puts a
/// script on fifteen letters chosen for what the face says about them — the largest kern it
/// states, the smallest, a negative one, a staircase whose step a full stop does not reach — and
/// this compares where every one of them lands with where Word lands it.
///
/// Fourteen of the fifteen land within seven thousandths of a point of Word's. The fifteenth is an
/// A sitting over an x, where the face states 90 units for the A's bottom left corner and Word
/// behaves as though it were 82: a twenty-seventh of a point, and nothing here accounts for it.
/// </remarks>
public class MathKernTests(ITestOutputHelper output)
{
    private static readonly string[] Probes =
    [
        "x^2", "x^2 stated", "x^2 at sixteen", "x_2", "b^2", "i^2", "n^2", "A^2",
        "f_x", "f^x", "x^A", "x_A", "i^.", "._A", "x^2 in a sixteen point paragraph"
    ];

    [Fact]
    public void Every_script_sits_where_word_sits_it()
    {
        if (TestFonts.SkipForMissingFaces()) return;

        var ours = Pieces(PdfTextExtractor.Extract(Converter.Convert(
            Fixtures.Build("math-kern-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })));

        var word = Pieces(PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "math-kern-probe.pdf")));

        Assert.Equal(Probes.Length, word.Count);
        Assert.Equal(Probes.Length, ours.Count);

        var worst = 0.0;

        for (var i = 0; i < Probes.Length; i++)
        {
            // Two pieces to a probe: the letter, then the script sitting on it.
            Assert.Equal(2, word[i].Count);
            Assert.Equal(2, ours[i].Count);

            var gap = ours[i][1] - word[i][1];
            worst = Math.Max(worst, Math.Abs(gap));

            output.WriteLine($"{Probes[i],-34} {ours[i][1],9:0.####} against {word[i][1],9:0.####}" +
                             $"  {gap,7:0.####}");

            Assert.True(Math.Abs(gap) < 0.05,
                $"{Probes[i]}: the script begins at {ours[i][1]:0.####} " +
                $"where Word begins it at {word[i][1]:0.####}");
        }

        output.WriteLine($"fifteen scripts, worst {worst:0.####}pt");
    }

    /// <summary>
    /// The two probes that say when Word kerns at all: it does where the letters are the size the
    /// equation is set at, and does not where they are larger, or smaller.
    /// </summary>
    [Theory]
    [InlineData(2, "letters larger than the equation")]
    [InlineData(14, "letters smaller than it")]
    public void A_letter_that_is_not_the_equation_s_own_size_takes_no_kern(int probe, string what)
    {
        if (TestFonts.SkipForMissingFaces()) return;

        var runs = Pieces(PdfTextExtractor.Extract(Converter.Convert(
            Fixtures.Build("math-kern-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })))[probe];

        var fonts = TestFonts.CreatePinnedLibrary();
        var math = fonts.Resolve("Cambria Math").Font;

        var size = probe == 2 ? 16.0 : 12.0;
        var advance = math.GetAdvanceWidth(math.GetGlyphIndex(0x1D465)) * size / math.Metrics.UnitsPerEm;

        // Straight after the letter: none of the 62 units of lean the face states for an x, and
        // none of the 50 units of kern for its top right corner.
        Assert.Equal(runs[0] + advance, runs[1], 2);

        output.WriteLine($"{what}: the script sits at the letter's plain advance");
    }

    [Fact]
    public void The_face_states_a_staircase_for_each_corner()
    {
        if (TestFonts.SkipForMissingFaces()) return;

        var font = TestFonts.CreatePinnedLibrary().Resolve("Cambria Math").Font;

        var x = font.MathKerns[font.GetGlyphIndex(0x1D465)];

        // One value and no heights: an x takes the same kern however high the script sits.
        Assert.Empty(x.TopRight!.Heights);
        Assert.Equal(50, x.TopRight.Values[0]);

        // And a staircase under it, which turns at 690 design units.
        Assert.Equal([690], x.BottomRight!.Heights);
        Assert.Equal([-20, 0], x.BottomRight.Values);

        Assert.Equal(-20, x.BottomRight.At(0));
        Assert.Equal(-20, x.BottomRight.At(689));
        Assert.Equal(0, x.BottomRight.At(690));

        // The largest the face states here: an x tucked under an f comes back 400 units.
        var f = font.MathKerns[font.GetGlyphIndex(0x1D453)];

        Assert.Equal([420, 720], f.BottomRight!.Heights);
        Assert.Equal([-400, -320, 0], f.BottomRight.Values);

        // A digit is not in the table at all, and takes nothing.
        Assert.False(font.MathKerns.ContainsKey(font.GetGlyphIndex('2')));
    }

    /// <summary>
    /// The pieces of each probe's equation, left to right, page by page: where the letter begins
    /// and where the script begins.
    /// </summary>
    private static List<List<double>> Pieces(IReadOnlyList<ExtractedTextRun> runs)
    {
        // The stop that marks the line, which is at the margin — not the one that is the whole
        // of the equation two probes from the end.
        var anchors = runs.Where(run => run.Text.Trim() == "." && run.X < 72.4)
            .Select(run => run.PageIndex * 2000.0 + run.BaselineY)
            .Distinct().Order().ToList();

        return
        [
            .. anchors.Select(anchor => runs
                .Where(run => run.FontSize > 3 &&
                              Math.Abs(run.PageIndex * 2000.0 + run.BaselineY - anchor) < 9 &&
                              run.X > 72.4)
                .OrderBy(run => run.X)
                .Select(run => run.X)
                .ToList())
        ];
    }
}
