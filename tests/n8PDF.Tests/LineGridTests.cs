using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where Word puts a baseline, to the millipoint, against where this puts it.
/// </summary>
/// <remarks>
/// Word writes every baseline on a grid of one three-hundredth of an inch — 0.24 points, the same
/// grid it rounds a font size to. Two fixtures say what it rounds and what it does not:
///
/// line-ascent-probe gives each of its seventy-four pages a first paragraph of one letter, so that its
/// baseline is the top margin plus the ascent of that line and nothing else, and reads the ascent
/// straight off the page. line-grid-probe fills nine pages with forty single-spaced lines each,
/// which says both what a face's line height is — to a hundredth of a point, since thirty-nine
/// gaps divide the grid step down that far — and how the rounding behaves as the lines stack.
///
/// What comes out of the two of them is in <see cref="n8PDF.Layout.Grid"/>: the height of a line
/// is exact and the descent inside it is rounded, the ascent is what the two of them leave, and
/// the baseline is rounded where it lands. Neither rounding accumulates.
/// </remarks>
public class LineGridTests(ITestOutputHelper output)
{
    /// <summary>Word's grid, in points.</summary>
    private const double Step = 0.24;

    private static readonly string[] Blocks =
    [
        "Times 2", "Times 5", "Times 11", "Times 12", "Cambria 6",
        "Cambria 11", "Arial 12", "Arial 6", "Calibri 11"
    ];

    /// <summary>
    /// Six of the nine forty-line pages come out exactly as Word's, line for line, and the three
    /// that do not are each a single step of the grid out on some of their lines. All three are
    /// faces whose descent lands within a twentieth of a step of a half step, where a hair of
    /// difference in Word's own arithmetic decides the rounding.
    /// </summary>
    [Fact]
    public void Forty_lines_to_a_page_land_where_Word_lands_them()
    {
        var (exact, total, worst, perPage) = Compare("line-grid-probe");

        for (var page = 0; page < perPage.Count; page++)
            output.WriteLine($"{Blocks[page],-12} {perPage[page]}/40");

        output.WriteLine($"{exact}/{total} baselines exact, worst {worst:0.###}pt");

        Assert.True(worst <= Step + 0.001, $"a baseline is {worst:0.###}pt from Word's");
        Assert.True(exact >= 290, $"only {exact} of {total} baselines are Word's own");
        Assert.Equal(6, perPage.Count(page => page == 40));
    }

    /// <summary>
    /// The ascent of a line, read off the first paragraph of a page: seventy-one of the
    /// seventy-four measurements are Word's exactly, and the three that are not are one step of the grid
    /// out. Rounding the ascent itself instead accounts for sixty-two of them.
    /// </summary>
    [Fact]
    public void The_first_line_of_a_page_takes_the_ascent_Word_gives_it()
    {
        var (exact, total, worst, _) = Compare("line-ascent-probe");

        output.WriteLine($"{exact}/{total} ascents exact, worst {worst:0.###}pt");

        Assert.True(worst <= Step + 0.001, $"an ascent is {worst:0.###}pt from Word's");
        Assert.True(exact >= 71, $"only {exact} of {total} ascents are Word's own");
    }

    /// <summary>
    /// Text that is not written along the line: a mark placed against the letter it belongs to by
    /// the font's own attachment rules, and anything written under a transform of its own — a
    /// watermark turned across the page, a label turned up the side of a chart, the text inside a
    /// drawing. Word's own pages put these off the grid too, save for the watermarks, which are
    /// compared against Word's elsewhere and agree to the millipoint.
    /// </summary>
    private static readonly HashSet<string> NotWrittenAlongTheLine =
    [
        "arabic", "indic", "marks", "chart-title-legend-label", "images-metafile",
        "images-metafile-plus", "watermark", "watermark-fit-probe"
    ];

    /// <summary>
    /// Nothing this engine writes along a line stands off the grid. A position that is not a whole
    /// number of steps is one Word could not have written, whatever else is right about it.
    /// </summary>
    public static TheoryData<string> FixtureNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Fixtures.All.Keys) data.Add(name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Every_baseline_written_stands_on_the_grid(string fixture)
    {
        if (NotWrittenAlongTheLine.Contains(fixture)) return;

        var ours = PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build(fixture),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        var off = ours.Select(run => run.BaselineY).Distinct()
            .Where(y => Math.Abs(y / Step - Math.Round(y / Step)) > 0.001)
            .ToList();

        Assert.True(off.Count == 0,
            $"'{fixture}': {off.Count} baselines are off the grid, the first at {off.FirstOrDefault():0.####}");
    }

    private static (int Exact, int Total, double Worst, List<int> PerPage) Compare(string fixture)
    {
        var word = PdfTextExtractor.ExtractFile(Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf"));
        var ours = PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build(fixture),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        var exact = 0;
        var total = 0;
        var worst = 0.0;
        var perPage = new List<int>();

        for (var page = 0; page <= word.Select(run => run.PageIndex).Max(); page++)
        {
            var them = Baselines(word, page);
            var us = Baselines(ours, page);
            var good = 0;

            Assert.Equal(them.Count, us.Count);

            for (var i = 0; i < them.Count; i++)
            {
                total++;
                worst = Math.Max(worst, Math.Abs(them[i] - us[i]));
                if (Math.Abs(them[i] - us[i]) < 0.001) { good++; exact++; }
            }

            perPage.Add(good);
        }

        return (exact, total, worst, perPage);
    }

    private static List<double> Baselines(IReadOnlyList<ExtractedTextRun> runs, int page) =>
        [.. runs.Where(run => run.PageIndex == page).Select(run => run.BaselineY).Distinct().Order()];
}
