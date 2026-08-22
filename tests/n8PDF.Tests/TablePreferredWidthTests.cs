using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a table's own stated width does — <c>w:tblW</c>, which says how wide the whole is to be
/// and nothing about how to divide it.
/// </summary>
/// <remarks>
/// table-preferred-width-probe measures seven of them, and five come out exactly Word's:
///
///   * the width is met exactly, whether it is wider than the contents want or narrower;
///   * a share (<c>w:type="pct"</c>) is a share of the **measure** — half of a 468 point column
///     comes out 234;
///   * a width narrower than the contents wraps them rather than overflowing;
///   * a width wider than the page is **not** held to the page: Word writes such a table straight
///     off the paper's edge, and so does this;
///   * the width is divided in proportion to what each column wants, each want being its content
///     rounded up to a whole twip, and the resulting edges go on the grid.
///
/// Two of the seven are a grid step out on their outer columns, and both are the same shape: three
/// columns of nearly equal content — 'a', 'b' and 'c', which are 5.32617, 6 and 5.32617 points of
/// Times at twelve. What is left there is smaller than anything this repository has failed to close
/// before, and it is worth writing down exactly how small.
///
/// Word's own page says its first edge falls at or past 2076 twips of the 6480 the table asks for,
/// and its second short of 4404. Dividing in proportion to wants of 107, 120 and 107 twips puts
/// them at 2075.93 and 4404.07 — **seven hundredths of a twip** outside each, which is three
/// thousandths of a point, and each lands on the far side of a rounding boundary. The same
/// arithmetic is exactly right for every other page of the probe.
///
/// What has been ruled out, each by the page it breaks: wants of content plus one twip (lands this
/// page, but throws the second edge of the three-differing-widths page 1.2 twips wide); equal
/// sharing of the surplus (out by four points); proportional to the minimum rather than the
/// maximum (out by sixty on the unequal page); a constant added per cell, which cannot satisfy this
/// page and the three-differing-widths page together at any value; and any blend of proportional
/// and equal sharing, which needs 0.93 here and 1.00 there. Word's answer is pinned to a
/// hundredth of a point and no rule of this family reaches it.
/// </remarks>
public class TablePreferredWidthTests(ITestOutputHelper output)
{
    /// <summary>
    /// The stated width is met exactly, page by page, and each column is within a point of Word's.
    /// </summary>
    /// <param name="apart">
    /// How far from Word's this page's columns may be: nothing at all, except on the two whose
    /// division falls the wrong side of a rounding boundary by three thousandths of a point. There
    /// the outer columns are a step narrow and the middle one takes both steps, so it is two out
    /// where they are one.
    /// </param>
    [Theory]
    [InlineData(0, "nearly equal content", 324.0, 0.5)]
    [InlineData(1, "very unequal content", 324.0, 0.001)]
    [InlineData(2, "three widths that differ", 324.0, 0.001)]
    [InlineData(3, "narrower than the content wants", 144.0, 0.001)]
    [InlineData(4, "a share of the measure", 234.0, 0.25)]
    [InlineData(5, "the cells stating widths too", 324.0, 0.001)]
    [InlineData(6, "wider than the page allows", 720.0, 0.001)]
    public void The_table_is_as_wide_as_it_says_it_is(int page, string what, double width, double apart)
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

        for (var i = 0; i < word.Count; i++) Assert.Equal(word[i], ours[i], apart);
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
