using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Which way the columns of a table run, from <c>w:bidiVisual</c>.
/// </summary>
/// <remarks>
/// <c>column-order-probe</c> is five tables of three columns of different widths, each cell saying
/// which it is and shaded so it can be told apart in the ink as well as in the text. What they
/// settle, all read off Word's own export:
///
///   * The first cell of a row stands at the right and the rest follow leftwards.
///   * The table is laid from the right margin rather than the left.
///   * Its indent is measured from the right: half an inch moves it half an inch leftwards.
///   * The border a cell calls its left is drawn on its right — the probe's three point left
///     border comes out at the right-hand end of the mirrored table.
///   * What the border does to the text inside is not turned about with it. Word insets the
///     content of a cell by the border it calls its left however that border is drawn, which is
///     why the rightmost cell's text stands 1.44 points inside its left edge and not half a point.
///   * Cells joined by w:gridSpan are joined at the right-hand end, the two columns they cover
///     being the two the row began with.
/// </remarks>
public class ColumnOrderTests(ITestOutputHelper output)
{
    /// <summary>Where each column stands, ours against Word's.</summary>
    [Theory]
    [InlineData(0, "the ordinary way round")]
    [InlineData(1, "the other way round")]
    [InlineData(2, "the other way round, indented")]
    [InlineData(3, "the other way round, with two cells joined")]
    [InlineData(4, "the ordinary way round, with the same join")]
    public void The_columns_stand_where_words_stand(int page, string what)
    {
        if (TestFonts.SkipForMissingFonts("column-order-probe")) return;

        output.WriteLine(what);

        var word = Columns(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "column-order-probe.pdf")), page);
        var ours = Columns(Ours(), page);

        output.WriteLine($"word {string.Join(" | ", word)}");
        output.WriteLine($"ours {string.Join(" | ", ours)}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.Equal(word[i].Fill, ours[i].Fill);

            // Within a point and a half: Word's shading stops at the inside of a cell's borders
            // where this fills the cell, so the two agree on which column is where rather than on
            // the last fraction of where its colour begins.
            Assert.Equal(word[i].Left, ours[i].Left, 2.0);
        }
    }

    /// <summary>
    /// The first cell of a mirrored row is the rightmost, and the last the leftmost: the text of
    /// the page reads A2, A1, A0 from the left where the row was written A0, A1, A2.
    /// </summary>
    [Fact]
    public void The_first_cell_of_a_mirrored_row_stands_at_the_right()
    {
        if (TestFonts.SkipForMissingFonts("column-order-probe")) return;

        // Gathered by cell rather than by line: the first column is narrow enough that the words
        // in it fall on two lines, so what it says has to be put back together.
        var cells = PdfTextExtractor.Extract(Ours())
            .Where(run => run.PageIndex == 1 && run.BaselineY < 120)
            .GroupBy(run => Math.Round(run.X / 20))
            .OrderBy(cell => cell.Key)
            .Select(cell => string.Concat(cell.OrderBy(run => run.BaselineY).Select(run => run.Text)).Trim())
            .ToList();

        output.WriteLine(string.Join(" | ", cells));

        Assert.Equal(["Mirrored A2", "Mirrored A1", "Mirrored A0"], cells);
    }

    /// <summary>
    /// The table stands against the right margin, and an indent moves it further left rather than
    /// further right.
    /// </summary>
    [Fact]
    public void A_mirrored_table_is_laid_from_the_right_margin()
    {
        if (TestFonts.SkipForMissingFonts("column-order-probe")) return;

        var pdf = Ours();
        var word = File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "column-order-probe.pdf"));

        var plain = Box(pdf, 0);
        var mirrored = Box(pdf, 1);
        var indented = Box(pdf, 2);

        output.WriteLine($"plain {plain.Left:0.##}..{plain.Right:0.##}, " +
                         $"mirrored {mirrored.Left:0.##}..{mirrored.Right:0.##}, " +
                         $"indented {indented.Left:0.##}..{indented.Right:0.##}");

        // The plain table begins at the left margin and the mirrored one ends at the right.
        Assert.Equal(72, plain.Left, 1.6);
        Assert.Equal(540, mirrored.Right, 1.6);

        // Half an inch of indent takes the mirrored table half an inch to the left, and Word's
        // goes to the same place.
        Assert.Equal(mirrored.Right - 36, indented.Right, 1.6);
        Assert.Equal(Box(word, 2).Right, indented.Right, 0.5);
    }

    /// <summary>
    /// The border a cell calls its left is drawn on its right. The probe's table is bordered with
    /// three points on the left and half a point everywhere else, so the thick one says which end
    /// of the table Word thinks the first column is at.
    /// </summary>
    [Theory]
    [InlineData(0, 72.0, "the ordinary way round: at the left")]
    [InlineData(1, 540.0, "the other way round: at the right")]
    public void The_left_border_is_drawn_at_the_leading_edge(int page, double at, string what)
    {
        if (TestFonts.SkipForMissingFonts("column-order-probe")) return;

        output.WriteLine(what);

        var thick = PdfPathExtractor.Extract(Ours())
            .Where(rect => rect.PageIndex == page && rect.ColorHex == "000000" && rect.Width > 2)
            .OrderByDescending(rect => rect.Height)
            .First();

        output.WriteLine($"thickest upright border at {thick.Left:0.##}, {thick.Width:0.##} wide");

        // Drawn about the edge, so its middle is where the edge is.
        Assert.Equal(at, thick.Left + thick.Width / 2, 0.3);
    }

    /// <summary>
    /// Cells joined by <c>w:gridSpan</c> are joined at the right-hand end of a mirrored row: the
    /// two columns the join covers are the two the row began with, which are the two rightmost.
    /// </summary>
    [Fact]
    public void A_join_covers_the_columns_the_row_began_with()
    {
        if (TestFonts.SkipForMissingFonts("column-order-probe")) return;

        var joined = PdfPathExtractor.Extract(Ours())
            .Where(rect => rect.PageIndex == 3 && rect.ColorHex == "FFE0E0" && rect.Width > 100)
            .OrderBy(rect => rect.Top)
            .Last();

        output.WriteLine($"the joined cell runs {joined.Left:0.##} to {joined.Right:0.##}");

        // Two columns of 2160 twips: 144 points, ending at the right margin.
        Assert.Equal(144, joined.Width, 1.6);
        Assert.Equal(540, joined.Right, 1.6);
    }

    private static byte[] Ours() =>
        Converter.Convert(Fixtures.Build("column-order-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>
    /// Where each shaded column of the first row stands, left to right. The first row only: the
    /// second has cells joined on some of the probe's pages, and a colour standing in two places
    /// says nothing about the order of the columns.
    /// </summary>
    private static List<(double Left, string Fill)> Columns(byte[] pdf, int page)
    {
        var fills = PdfPathExtractor.Extract(pdf)
            .Where(rect => rect.PageIndex == page && rect.ColorHex != "000000")
            .ToList();

        var top = fills.Min(rect => rect.Top);

        return fills
            .Where(rect => rect.Top < top + 1)
            .GroupBy(rect => rect.ColorHex)
            .Select(colour => (Left: colour.Min(rect => rect.Left), Fill: colour.Key))
            .OrderBy(column => column.Left)
            .ToList();
    }

    private static (double Left, double Top, double Right, double Bottom) Box(byte[] pdf, int page)
    {
        var rects = PdfPathExtractor.Extract(pdf)
            .Where(rect => rect.PageIndex == page && rect.ColorHex == "000000")
            .ToList();

        return (rects.Min(r => r.Left), rects.Min(r => r.Top),
            rects.Max(r => r.Right), rects.Max(r => r.Bottom));
    }
}
