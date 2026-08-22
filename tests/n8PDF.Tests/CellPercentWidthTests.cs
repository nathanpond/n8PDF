using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A cell width asked for as a share of the table — <c>w:tcW w:type="pct"</c>, in fiftieths of a
/// percent — and what the share turns out to be a share of.
/// </summary>
/// <remarks>
/// cell-percent-width-probe asks it seven ways and this follows Word on all seven:
///
///   * of a table stating its width in points, the share is of that width;
///   * of a table stating its width as a share of the measure, it is of the measure through it —
///     a half of a table that is the whole 468 point column comes out 234;
///   * of a table stating nothing there is nothing to take a share of but the contents, and Word
///     makes the table **as narrow as the shares allow**: a quarter, a half and a quarter round a
///     letter each come out 5.28, 10.8 and 5.28, a table of 21.36 points, which is the narrowest
///     at which a quarter still holds its letter;
///   * put a column of text in the middle cell of that same table and it fills the measure
///     instead, its half being 234 — the requirement is capped at the room there is;
///   * shares falling short of the whole are stretched to fill it: two quarters come out halves;
///   * shares adding to more than the whole are taken in order until it is spent, so the second of
///     two three-quarters gets the remaining quarter;
///   * and a share beside a stated 72 points and a column asking for nothing comes out 162, 72 and
///     90 — the share taken first, the statement kept, and everything left over going to the one
///     that asked for nothing.
///
/// What is left between us and Word is a twelfth of a point, everywhere and for one reason: Word
/// puts each column on the 0.24pt grid and gives the last of them the remainder, so its quarter of
/// 324 is 81.12 and 80.88 where ours is 81 and 81. Column widths are exact everywhere else in this
/// engine, and snapping them here alone would be a rule with one probe behind it.
/// </remarks>
public class CellPercentWidthTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, "shares of a stated width", new[] { 81.12, 162.0, 80.88 })]
    [InlineData(1, "shares of the whole measure", new[] { 117.12, 234.0, 116.88 })]
    [InlineData(2, "shares of a table left to its contents", new[] { 5.28, 10.8, 5.28 })]
    [InlineData(3, "the same, one cell holding more", new[] { 117.12, 234.0, 116.88 })]
    [InlineData(4, "shares short of the whole", new[] { 162.0, 162.0 })]
    [InlineData(5, "shares beyond the whole", new[] { 243.12, 80.88 })]
    [InlineData(6, "a share beside a measurement", new[] { 162.0, 72.0, 90.0 })]
    public void Each_column_is_the_width_word_gives_it(int page, string what, double[] expected)
    {
        if (TestFonts.SkipForMissingFonts("cell-percent-width-probe")) return;

        var word = Columns(File.ReadAllBytes(
            Path.Combine(TestPaths.ReferencePdfs, "cell-percent-width-probe.pdf")), page);

        var ours = Columns(Converter.Convert(Fixtures.Build("cell-percent-width-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"{what}\n  word {string.Join(" ", word.Select(w => w.ToString("0.##")))}" +
                         $"\n  ours {string.Join(" ", ours.Select(w => w.ToString("0.##")))}");

        Assert.Equal(expected, word.Select(w => Math.Round(w, 2)));
        Assert.Equal(expected.Length, ours.Count);

        // A step of the grid. Every column edge is put on it, so a table whose width is stated
        // comes out exactly Word's; one whose width the shares had to work out from the contents
        // carries whatever our measure of those contents differs from Word's by, which on the
        // third page is enough to move a column a step.
        for (var i = 0; i < expected.Length; i++) Assert.Equal(expected[i], ours[i], 0.25);

        Assert.Equal(word.Sum(), ours.Sum(), 0.25);
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
