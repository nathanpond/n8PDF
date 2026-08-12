using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests a table row taller than what is left of the page, which Word breaks across the two
/// unless the row says it may not be.
/// </summary>
public class TableSplitTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }

    /// <summary>A one-row table whose cell holds the given number of lines.</summary>
    private static string Table(int lines, bool cantSplit = false, string label = "Row")
    {
        var content = string.Concat(Enumerable.Range(1, lines).Select(i =>
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t>{label} line {i}.</w:t></w:r></w:p>"));

        var properties = cantSplit ? "<w:trPr><w:cantSplit/></w:trPr>" : string.Empty;

        return "<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/>" +
               "<w:tblBorders><w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/>" +
               "<w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/></w:tblBorders>" +
               "<w:tblLayout w:type=\"fixed\"/></w:tblPr>" +
               "<w:tblGrid><w:gridCol w:w=\"9360\"/></w:tblGrid>" +
               $"<w:tr>{properties}<w:tc>{content}</w:tc></w:tr></w:tbl>";
    }

    /// <summary>A document whose table starts with the given number of lines left on the page.</summary>
    private static LaidOutDocument Straddling(int linesLeft, int rowLines, bool cantSplit = false)
    {
        var builder = new DocxBuilder();

        for (var i = 1; i <= 46 - linesLeft; i++)
            builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        return LayoutOf(builder.AddRawParagraph(Table(rowLines, cantSplit)));
    }

    private static List<string> RowLinesOn(LaidOutPage page) =>
        page.Lines
            .Where(l => l.Texts.Any(t => t.Text.StartsWith("Row")))
            .OrderBy(l => l.BaselineY)
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)))
            .ToList();

    [Fact]
    public void A_row_too_tall_for_the_page_is_broken_across_it()
    {
        var layout = Straddling(linesLeft: 8, rowLines: 20);

        Assert.Equal(2, layout.Pages.Count);

        var first = RowLinesOn(layout.Pages[0]);
        var second = RowLinesOn(layout.Pages[1]);

        Assert.NotEmpty(first);
        Assert.Equal(20, first.Count + second.Count);

        // Every line is whole and they carry on in order across the break.
        Assert.Equal("Row line 1.", first[0]);
        Assert.Equal($"Row line {first.Count + 1}.", second[0]);
        Assert.Equal("Row line 20.", second[^1]);
    }

    /// <summary>
    /// Word closes off both halves with a full border box, as though each were a row of its own.
    /// </summary>
    [Fact]
    public void Both_halves_are_bordered()
    {
        var layout = Straddling(linesLeft: 8, rowLines: 20);

        foreach (var page in layout.Pages)
        {
            var horizontals = page.Rectangles.Where(r => r.Width > 100 && r.Height < 2).ToList();
            var verticals = page.Rectangles.Where(r => r.Height > 20 && r.Width < 2).ToList();

            Assert.Equal(2, horizontals.Count);
            Assert.Equal(2, verticals.Count);

            // The box closes around the text that is on this page.
            var rows = page.Lines.Where(l => l.Texts.Any(t => t.Text.StartsWith("Row"))).ToList();
            var top = rows.Min(l => l.BaselineY - l.Ascent);
            var bottom = rows.Max(l => l.BaselineY - l.Ascent + l.Height);

            Assert.True(horizontals.Min(r => r.Y) < top, "the row is not closed above");
            Assert.True(horizontals.Max(r => r.Y) >= bottom - 0.5, "the row is not closed below");
        }
    }

    [Fact]
    public void A_row_that_may_not_split_moves_whole()
    {
        var layout = Straddling(linesLeft: 8, rowLines: 20, cantSplit: true);

        Assert.Equal(2, layout.Pages.Count);
        Assert.Empty(RowLinesOn(layout.Pages[0]));
        Assert.Equal(20, RowLinesOn(layout.Pages[1]).Count);
    }

    /// <summary>
    /// A row is only broken where a line of it would fit. One that would leave an empty box at the
    /// foot of the page moves instead.
    /// </summary>
    [Fact]
    public void A_row_with_no_room_for_a_line_moves_whole()
    {
        var layout = Straddling(linesLeft: 0, rowLines: 6);

        Assert.Equal(2, layout.Pages.Count);
        Assert.Empty(RowLinesOn(layout.Pages[0]));
        Assert.Equal(6, RowLinesOn(layout.Pages[1]).Count);
    }

    /// <summary>
    /// A row taller than a whole page is broken more than once rather than pushed along for ever.
    /// </summary>
    [Fact]
    public void A_row_taller_than_a_page_is_broken_more_than_once()
    {
        var layout = Straddling(linesLeft: 10, rowLines: 100);

        Assert.Equal(3, layout.Pages.Count);
        Assert.All(layout.Pages, page => Assert.NotEmpty(RowLinesOn(page)));

        Assert.Equal(100, layout.Pages.Sum(page => RowLinesOn(page).Count));
    }

    /// <summary>
    /// Cells that ran out of content sooner stay on the page the row began on: only what is left
    /// carries over.
    /// </summary>
    [Fact]
    public void A_shorter_cell_stays_where_it_was()
    {
        var layout = LayoutOf(Fixtures.Build("table-split"));

        var pages = layout.Pages
            .Select(page => page.Lines.SelectMany(l => l.Texts).Select(t => t.Text).ToList())
            .ToList();

        Assert.Contains(pages[0], text => text.Contains("Splitting second cell"));
        Assert.DoesNotContain(pages[1], text => text.Contains("Splitting second cell"));

        // And the first cell carried on where it left off.
        Assert.Contains(pages[1], text => text.Contains("Splitting line 9."));
    }

    [Fact]
    public void The_fixture_splits_one_row_and_keeps_the_other_whole()
    {
        var layout = LayoutOf(Fixtures.Build("table-split"));

        Assert.Equal(3, layout.Pages.Count);

        static int Lines(LaidOutPage page, string prefix) =>
            page.Lines.Count(l => l.Texts.Any(t => t.Text.StartsWith(prefix)));

        // Eight lines of the splitting row on the first page and twelve on the second.
        Assert.Equal(8, Lines(layout.Pages[0], "Splitting line"));
        Assert.Equal(12, Lines(layout.Pages[1], "Splitting line"));

        // The row that may not split is whole on the last page.
        Assert.Equal(0, Lines(layout.Pages[1], "Whole line"));
        Assert.Equal(12, Lines(layout.Pages[2], "Whole line"));
    }
}
