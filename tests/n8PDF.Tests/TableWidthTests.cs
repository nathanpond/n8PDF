using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a declared cell width does to a table left on autofit.
/// </summary>
/// <remarks>
/// It is a preference, not a measurement, and table-width-probe pins the five ways that plays out
/// against Word's own export. Each cell of the probe is shaded a colour of its own, so a column is
/// read off the page as a rectangle rather than inferred from where its text landed:
///
///   widths that fit          taken exactly — 72, 108 and 144 points come out as those
///   content that will not    the column grows to hold it and its neighbours keep what they asked
///     fit the width asked      for: 36/36/36 with an unbreakable word in the middle comes out
///                              36/142.56/36
///   more than the measure    scaled down together: three of 200 come out three of 156
///   a column asking nothing  sized by its own content beside ones that ask
///   two rows disagreeing     the wider of the two wins
///
/// Before this the declared width was ignored outright and every column was sized by its content,
/// which put a table of three declared columns 300 points from Word's.
/// </remarks>
public class TableWidthTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(0, "widths that fit", new[] { 72.0, 108.0, 144.0 })]
    [InlineData(1, "a column that cannot hold what it asks for", new[] { 36.0, 142.56, 36.0 })]
    [InlineData(2, "more than the measure", new[] { 156.0, 156.0, 156.0 })]
    [InlineData(3, "one column asking for nothing", new[] { 72.0, 90.72, 72.0 })]
    [InlineData(4, "two rows disagreeing", new[] { 144.0, 144.0 })]
    public void Each_column_is_the_width_word_gives_it(int page, string what, double[] expected)
    {
        if (TestFonts.SkipForMissingFonts("table-width-probe")) return;

        var word = Columns(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "table-width-probe.pdf")), page);
        var ours = Columns(Converter.Convert(Fixtures.Build("table-width-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"{what}\n  word {string.Join(" ", word.Select(w => w.ToString("0.##")))}" +
                         $"\n  ours {string.Join(" ", ours.Select(w => w.ToString("0.##")))}");

        Assert.Equal(expected.Length, word.Count);
        Assert.Equal(expected.Length, ours.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], word[i], 0.01);

            // A column sized by its content is held to a step of the grid rather than exactly.
            // Every column edge goes on that grid (see OnTheGrid), and a column that has to hold
            // something unbreakable keeps enough room for it — so where our measure of a word
            // runs a hair above Word's, as it does for the probe's long one by nine hundredths of
            // a point, the column takes the next step up and the column after it gives one back.
            // A column whose width is declared or divided has no such slack and is exact.
            Assert.Equal(expected[i], ours[i], 0.25);
        }
    }

    /// <summary>The widths of the shaded cells across the first row of the page, left to right.</summary>
    private static List<double> Columns(byte[] pdf, int page)
    {
        var fills = PdfPathExtractor.Extract(pdf)
            .Where(fill => fill.PageIndex == page && fill.Width > 1 && fill.Height > 1)
            .OrderBy(fill => fill.Top).ThenBy(fill => fill.Left)
            .ToList();

        if (fills.Count == 0) return [];

        // The first row only, and one entry to a cell: Word paints each cell twice over, once to
        // its edge and once inset inside its border.
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
