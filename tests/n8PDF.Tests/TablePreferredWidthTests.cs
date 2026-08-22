using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a table's own stated width does — <c>w:tblW</c>, which says how wide the whole is to be
/// and nothing about how to divide it.
/// </summary>
/// <remarks>
/// table-preferred-width-probe measures seven of them. Four things it settles outright, and this
/// matches Word on all four:
///
///   * the width is met exactly, whether it is wider than the contents want or narrower;
///   * a share (<c>w:type="pct"</c>) is a share of the **measure** — half of a 468 point column
///     comes out 234;
///   * a width narrower than the contents want wraps them rather than overflowing;
///   * a width wider than the page is **not** held to the page: Word writes the table straight off
///     the paper's edge, and so does this.
///
/// The fifth thing — how the width is divided between the columns — is fitted rather than derived,
/// and this says so. The columns grow in proportion to what each wanted, which follows Word to
/// within 0.7pt on the probe: closest where the columns differ most (0.14pt with one long column
/// against two letters) and furthest where they are nearly equal (0.58pt across three letters).
/// Word's own idea of what a cell wants is not the sum of the advances its PDF writes — no
/// constant, share or rounding of the measured content reproduces the last fraction of a point —
/// so what is asserted below is the total exactly and each column to within a point.
/// </remarks>
public class TablePreferredWidthTests(ITestOutputHelper output)
{
    /// <summary>
    /// The stated width is met exactly, page by page, and each column is within a point of Word's.
    /// </summary>
    [Theory]
    [InlineData(0, "nearly equal content", 324.0)]
    [InlineData(1, "very unequal content", 324.0)]
    [InlineData(2, "three widths that differ", 324.0)]
    [InlineData(3, "narrower than the content wants", 144.0)]
    [InlineData(4, "a share of the measure", 234.0)]
    [InlineData(5, "the cells stating widths too", 324.0)]
    [InlineData(6, "wider than the page allows", 720.0)]
    public void The_table_is_as_wide_as_it_says_it_is(int page, string what, double width)
    {
        if (TestFonts.SkipForMissingFonts("table-preferred-width-probe")) return;

        var word = Columns(File.ReadAllBytes(
            Path.Combine(TestPaths.ReferencePdfs, "table-preferred-width-probe.pdf")), page);

        var ours = Columns(Converter.Convert(Fixtures.Build("table-preferred-width-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"{what}\n  word {string.Join(" ", word.Select(w => w.ToString("0.##")))}" +
                         $"\n  ours {string.Join(" ", ours.Select(w => w.ToString("0.##")))}");

        Assert.Equal(word.Count, ours.Count);
        Assert.Equal(width, word.Sum(), 0.01);
        Assert.Equal(width, ours.Sum(), 0.01);

        for (var i = 0; i < word.Count; i++) Assert.Equal(word[i], ours[i], 1.0);
    }

    /// <summary>
    /// A share is a share of the measure, and a width beyond the page is not brought back to it.
    /// </summary>
    /// <remarks>
    /// Both are worth stating on their own because both look like mistakes: half of the probe's
    /// 468 point measure is 234 and not half the paper, and a table asking for 720 points on a
    /// 612 point page runs off it in Word's own export rather than being squeezed to fit.
    /// </remarks>
    [Fact]
    public void A_width_beyond_the_page_runs_off_it()
    {
        if (TestFonts.SkipForMissingFonts("table-preferred-width-probe")) return;

        var ours = Converter.Convert(Fixtures.Build("table-preferred-width-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var last = Columns(ours, 6);

        Assert.Equal(720, last.Sum(), 0.01);

        // The measure is 468 points from a left margin of 72, so the table's right edge stands
        // 324 points past the right margin and 180 past the paper's own edge.
        Assert.True(72 + last.Sum() > 612, "the table should overhang the paper, as Word's does");
    }

    /// <summary>The widths of the cells across the first row of a page, left to right.</summary>
    private static List<double> Columns(byte[] pdf, int page)
    {
        var fills = PdfPathExtractor.Extract(pdf)
            .Where(fill => fill.PageIndex == page && fill.Width > 1 && fill.Height > 1)
            .OrderBy(fill => fill.Top).ThenBy(fill => fill.Left)
            .ToList();

        if (fills.Count == 0) return [];

        var top = fills[0].Top;
        var kept = new List<ExtractedRectangle>();

        // One entry to a cell: Word paints each twice, once to its edge and once inside it.
        foreach (var fill in fills.Where(fill => Math.Abs(fill.Top - top) < 1))
        {
            if (kept.Any(seen => Math.Abs(seen.Left - fill.Left) < 1)) continue;
            kept.Add(fill);
        }

        return [.. kept.Select(fill => fill.Width)];
    }
}
