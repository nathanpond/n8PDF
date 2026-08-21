using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where a sum's limits go, and an integral's, against Word's own.
/// </summary>
/// <remarks>
/// The equations fixture holds one of each and they disagree: Word places an integral's limits by
/// the rules a script follows, measured from the operator's own ink, and places a sum's at shifts
/// of its own. Both say the same thing in the markup and both are set in a line, so the markup
/// cannot be what decides it.
///
/// math-nary-probe asks the question directly. Every operator appears twice over — once saying its
/// limits go above and below, once saying they go beside — so that what the markup says and what
/// the operator is can be told apart, and there are two of each kind: a sum and a product, whose
/// limits Word writes above and below; an integral and a contour integral, whose go beside.
/// </remarks>
public class MathNaryTests(ITestOutputHelper output)
{
    private static readonly string[] Probes =
    [
        "sum, limits above", "sum, limits beside", "integral, limits above", "integral, limits beside",
        "product, limits above", "product, limits beside", "contour, limits above", "contour, limits beside",
        "sum with a lower limit", "sum with an upper limit",
        "integral with a lower limit", "integral with an upper limit",
        "sum under x", "sum under 1", "sum over x", "sum over 1", "sum, x either side",
        "integral under x", "sum at twenty-four point"
    ];

    /// <summary>
    /// Every limit of the probe, where Word puts it.
    /// </summary>
    /// <remarks>
    /// Every one of them where Word puts it, but for two that round the other way: a lone 1 over
    /// a sum, and the lower limit of the sum at twenty-four point. Both are out by the three
    /// hundredth of an inch Word rounds a position to and no more.
    /// </remarks>
    [Fact]
    public void Every_limit_sits_where_word_sits_it()
    {
        var ours = Limits(PdfTextExtractor.Extract(Converter.Convert(
            Fixtures.Build("math-nary-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })));

        var word = Limits(PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "math-nary-probe.pdf")));

        Assert.Equal(Probes.Length, word.Count);
        Assert.Equal(Probes.Length, ours.Count);

        var worst = 0.0;

        for (var i = 0; i < Probes.Length; i++)
        {
            Assert.Equal(word[i].Count, ours[i].Count);

            for (var j = 0; j < word[i].Count; j++)
            {
                worst = Math.Max(worst, Math.Abs(ours[i][j] - word[i][j]));

                Assert.True(Math.Abs(ours[i][j] - word[i][j]) < 0.3,
                    $"{Probes[i]}: a limit sits {ours[i][j]:0.###} from the line " +
                    $"where Word puts it at {word[i][j]:0.###}");
            }

            output.WriteLine($"{Probes[i],-28} " +
                             $"ours {string.Join(", ", ours[i].Select(v => $"{v,7:0.###}"))}   " +
                             $"word {string.Join(", ", word[i].Select(v => $"{v,7:0.###}"))}");
        }

        output.WriteLine($"worst {worst:0.###}pt");
    }

    /// <summary>
    /// What the markup asks for makes no difference; what the operator is makes all of it.
    /// </summary>
    [Fact]
    public void The_operator_decides_and_not_the_markup()
    {
        var ours = Limits(PdfTextExtractor.Extract(Converter.Convert(
            Fixtures.Build("math-nary-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })));

        // The first eight are the four operators, each written both ways round.
        for (var i = 0; i < 8; i += 2)
        {
            Assert.Equal(ours[i], ours[i + 1]);
            output.WriteLine($"{Probes[i]} and {Probes[i + 1]}: the same");
        }

        // And the two kinds do not agree with each other: a sum's limits sit closer in than an
        // integral's, which are placed from the operator's own ink.
        // The list runs down the page, so the first of each is its upper limit.
        Assert.True(ours[0][0] > ours[2][0] + 2,
            $"a sum's upper limit stands at {ours[0][0]:0.##} and an integral's at " +
            $"{ours[2][0]:0.##}; the integral's should be the higher of the two");
    }

    /// <summary>Where each probe's limits sit, above the line and below it.</summary>
    private static List<List<double>> Limits(IReadOnlyList<ExtractedTextRun> runs)
    {
        var anchors = runs.Where(run => run.Text.Trim() == "." && run.X < 72.45)
            .Select(run => run.PageIndex * 2000.0 + run.BaselineY)
            .Distinct().Order().ToList();

        return
        [
            .. anchors.Select(anchor => runs
                // A limit is the small type on the line — the operator and what it is taken of
                // are the equation's own size — and belongs to whichever line it is nearest,
                // since a limit of one probe can stand higher than the line above it.
                .Where(run => run.FontSize > 3 && run.FontSize < 18 && run.X > 72.4 &&
                              anchors.MinBy(other =>
                                  Math.Abs(other - (run.PageIndex * 2000.0 + run.BaselineY))) == anchor)
                .Select(run => Math.Round(run.PageIndex * 2000.0 + run.BaselineY - anchor, 3))
                .Distinct()
                .Order()
                .ToList())
        ];
    }
}
