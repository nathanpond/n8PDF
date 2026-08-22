using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where a column's edge falls, as against where the arithmetic puts it.
/// </summary>
/// <remarks>
/// On the grid, like everything else Word writes: each column edge goes to the nearest
/// three-hundredth of an inch and a column's width is whatever the gap between two snapped edges
/// comes to. It is the edges and not the widths, which is why three columns of one declared width
/// need not be equal — column-grid-probe's three fifty-point columns, fifty points being 208 steps
/// and a third, come out of Word 49.92, 50.16 and 49.92, exactly where 122, 172 and 222 land when
/// each is rounded.
///
/// Five of the probe's six pages come out of this engine identical to Word's: declared widths,
/// awkward declared widths, a stated grid under a fixed layout, widths scaled down to fit the
/// measure, and halves falling the other side of a step. The sixth sizes its columns by their
/// contents and is a step out on one edge of three — not because the text is measured differently
/// (TextMeasureTests shows it is not), but because of what Word makes of a cell's content width
/// before the edges accumulate.
///
/// One rule goes with the snapping: a column sized to hold something that cannot be broken keeps
/// enough room for it, taking the step it needs out of the column after it. Without that, a column
/// measured to fit a long word exactly loses a hundredth to the rounding and breaks the word Word
/// leaves whole — which is a visible difference where a quarter point of column is not. Word keeps
/// such a word in a column a fiftieth of a point too narrow for it instead, which is a tolerance in
/// its line breaking that nothing here has measured yet.
/// </remarks>
public class ColumnGridTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, "fifty points a column", new[] { 49.92, 50.16, 49.92 }, true)]
    [InlineData(1, "three awkward widths", new[] { 25.92, 41.04, 65.04 }, true)]
    [InlineData(2, "sized by their contents", new[] { 3.36, 10.8, 8.64 }, false)]
    [InlineData(3, "a stated grid", new[] { 49.92, 50.16, 49.92 }, true)]
    [InlineData(4, "too wide for the measure", new[] { 156.0, 156.0, 156.0 }, true)]
    [InlineData(5, "halves the other way", new[] { 50.16, 49.92 }, true)]
    public void Each_column_edge_is_on_the_grid(int page, string what, double[] expected, bool exact)
    {
        if (TestFonts.SkipForMissingFonts("column-grid-probe")) return;

        var word = Columns(File.ReadAllBytes(
            Path.Combine(TestPaths.ReferencePdfs, "column-grid-probe.pdf")), page);

        var ours = Columns(Converter.Convert(Fixtures.Build("column-grid-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"{what}\n  word {string.Join(" ", word.Select(w => w.ToString("0.##")))}" +
                         $"\n  ours {string.Join(" ", ours.Select(w => w.ToString("0.##")))}");

        Assert.Equal(expected, word.Select(w => Math.Round(w, 2)));
        Assert.Equal(expected.Length, ours.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            // Every column that was declared, stated or divided is Word's exactly. A column sized
            // by its content carries our measure of that content, and is held to a step.
            Assert.Equal(expected[i], ours[i], exact ? 0.001 : 0.25);
        }

        // And every edge of ours is on the grid, whether or not it is where Word's is.
        var edge = 0.0;

        foreach (var width in ours)
        {
            edge += width;
            Assert.Equal(0, Math.Abs(edge / 0.24 - Math.Round(edge / 0.24)), 0.001);
        }
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

        foreach (var fill in fills.Where(fill => Math.Abs(fill.Top - top) < 1))
        {
            if (kept.Any(seen => Math.Abs(seen.Left - fill.Left) < 1)) continue;
            kept.Add(fill);
        }

        return [.. kept.Select(fill => fill.Width)];
    }
}
