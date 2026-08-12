using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests cells merged down the page: a cell saying <c>w:vMerge w:val="restart"</c> owns every
/// cell beneath it that says it continues, and the run they make behaves as one tall cell.
/// </summary>
/// <remarks>
/// What the rows do is the part that is easy to get wrong. Word does not give the merged cell's
/// height to the row it begins in — the rows keep the heights their own cells ask for and the
/// merged text runs down through them, so three lines merged across three one-line rows leave
/// those rows a line tall each. Only what will not fit makes the run taller, and the last row of
/// the run takes all of it. Every number here is read from Word's export of
/// table-vertical-merge.
/// </remarks>
public class TableMergeTests
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

    /// <summary>A cell holding the given lines, with the given properties.</summary>
    private static string Cell(string properties, params string[] lines)
    {
        var content = lines.Length == 0
            ? $"<w:p><w:pPr>{ZeroSpacing}</w:pPr></w:p>"
            : string.Concat(lines.Select(line =>
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>{line}</w:t></w:r></w:p>"));

        return $"<w:tc>{(properties.Length == 0 ? string.Empty : $"<w:tcPr>{properties}</w:tcPr>")}{content}</w:tc>";
    }

    private static string Merged(string alignment, params string[] lines) =>
        Cell("<w:vMerge w:val=\"restart\"/>" +
             (alignment.Length == 0 ? string.Empty : $"<w:vAlign w:val=\"{alignment}\"/>"), lines);

    private static string Continues() => Cell("<w:vMerge w:val=\"continue\"/>");

    /// <summary>A two-column bordered table of the given rows, each written as its cells.</summary>
    private static string Table(params string[] rows) =>
        "<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/>" +
        "<w:tblBorders>" +
        "<w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/>" +
        "<w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/>" +
        "<w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/>" +
        "</w:tblBorders><w:tblLayout w:type=\"fixed\"/>" +
        "<w:tblCellMar>" +
        "<w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"0\" w:type=\"dxa\"/>" +
        "<w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/>" +
        "</w:tblCellMar></w:tblPr>" +
        "<w:tblGrid><w:gridCol w:w=\"4680\"/><w:gridCol w:w=\"4680\"/></w:tblGrid>" +
        string.Concat(rows.Select(cells => $"<w:tr>{cells}</w:tr>")) +
        "</w:tbl>";

    /// <summary>
    /// A merged cell of the given lines and alignment, beside a column of one line to a row.
    /// </summary>
    private static LaidOutPage Merge(string alignment, params string[] lines) =>
        LayoutOf(new DocxBuilder().AddRawParagraph(Table(
            Merged(alignment, lines) + Cell(string.Empty, "First"),
            Continues() + Cell(string.Empty, "Second"),
            Continues() + Cell(string.Empty, "Third")))).Pages[0];

    /// <summary>The baseline of the line beginning with the given word.</summary>
    private static double BaselineOf(LaidOutPage page, string word) =>
        page.Lines.Single(l => l.Texts.Any(t => t.Text.StartsWith(word))).BaselineY;

    /// <summary>
    /// The merged cell's text runs on past the foot of the row it begins in, and the rows beside
    /// it keep the heights their own single lines ask for.
    /// </summary>
    [Fact]
    public void Merged_text_runs_on_through_the_rows_below_it()
    {
        var page = Merge(string.Empty, "Merged one", "Merged two", "Merged three");

        var first = BaselineOf(page, "First");
        var second = BaselineOf(page, "Second");
        var third = BaselineOf(page, "Third");

        // A line of 12pt Times, three times over: the rows are not stretched by the merged cell.
        Assert.Equal(14.3, second - first, 1);
        Assert.Equal(14.3, third - second, 1);

        // The merged cell's own lines follow each other at its own line height, which starts from
        // the top of the run rather than from any row.
        Assert.Equal(first, BaselineOf(page, "Merged one"), 1);
        Assert.Equal(13.8, BaselineOf(page, "Merged two") - BaselineOf(page, "Merged one"), 1);
    }

    /// <summary>
    /// A merged run is one tall cell, so it has no line across it: the inside rule is drawn in
    /// every column but the merged one, and the run is closed off only where it ends.
    /// </summary>
    [Fact]
    public void No_rule_is_drawn_across_a_merged_run()
    {
        var page = Merge(string.Empty, "Merged");

        var rules = page.Rectangles.Where(r => r.Height < 2).ToList();

        // Every horizontal rule over the merged column: the table's own top and bottom edges, and
        // nothing between them.
        var merged = rules.Where(r => r.X < 100).OrderBy(r => r.Y).Select(r => r.Y).Distinct().ToList();
        var beside = rules.Where(r => r.X > 100).OrderBy(r => r.Y).Select(r => r.Y).Distinct().ToList();

        Assert.Equal(2, merged.Count);
        Assert.Equal(4, beside.Count);
        Assert.Equal(merged[0], beside[0], 2);
        Assert.Equal(merged[1], beside[^1], 2);
    }

    /// <summary>
    /// Vertical alignment is measured over the whole run rather than over the row the merged cell
    /// begins in, so a centred line lands beside the middle row of three.
    /// </summary>
    [Theory]
    [InlineData("top", "First")]
    [InlineData("center", "Second")]
    [InlineData("bottom", "Third")]
    public void Alignment_is_measured_over_the_whole_run(string alignment, string beside)
    {
        var page = Merge(alignment, "Aligned");

        Assert.Equal(BaselineOf(page, beside), BaselineOf(page, "Aligned"), 1);
    }

    /// <summary>
    /// Content the rows cannot hold makes the run taller — and it is the last row of the run that
    /// grows, which is where Word puts it.
    /// </summary>
    [Fact]
    public void Content_that_will_not_fit_makes_the_last_row_of_the_run_taller()
    {
        var page = LayoutOf(new DocxBuilder().AddRawParagraph(Table(
            Merged(string.Empty, "Tall one", "Tall two", "Tall three", "Tall four") +
            Cell(string.Empty, "Short one"),
            Continues() + Cell(string.Empty, "Short two")))).Pages[0];

        // The first row is a line tall, as its own cell asks: the second takes the overflow.
        Assert.Equal(14.3, BaselineOf(page, "Short two") - BaselineOf(page, "Short one"), 1);

        // And all four merged lines are on the page, the last of them below the second row's own.
        Assert.True(BaselineOf(page, "Tall four") > BaselineOf(page, "Short two"));

        var rules = page.Rectangles.Where(r => r.Height < 2).Select(r => r.Y).ToList();
        Assert.True(rules.Max() > BaselineOf(page, "Tall four"),
            "the table closes above the last of the merged lines");
    }

    /// <summary>
    /// A merge belongs to its column: two runs one after the other in the same column, and a third
    /// overlapping both of them in the next column along, are each their own.
    /// </summary>
    [Fact]
    public void Merges_in_different_columns_are_independent()
    {
        var layout = LayoutOf(Fixtures.Build("table-vertical-merge"));
        var page = layout.Pages[5];

        var rows = new[] { "Last one", "Last two", "Last three", "Last four" }
            .Select(text => BaselineOf(page, text))
            .ToList();

        // The first column's two runs cover rows 1-2 and 3-4, and the middle column's single run
        // covers rows 2-3: each starts at the top of its own run.
        Assert.Equal(rows[0], BaselineOf(page, "First pair"), 1);
        Assert.Equal(rows[2], BaselineOf(page, "Second pair"), 1);
        Assert.Equal(rows[1], BaselineOf(page, "Straddling"), 1);
        Assert.Equal(rows[3], BaselineOf(page, "Middle four"), 1);
    }

    /// <summary>
    /// A merged cell's shading covers the run rather than the row it began in, and goes into the
    /// page underneath the borders drawn over it.
    /// </summary>
    [Fact]
    public void Shading_covers_the_whole_run()
    {
        var page = LayoutOf(new DocxBuilder().AddRawParagraph(Table(
            Cell("<w:vMerge w:val=\"restart\"/><w:shd w:val=\"clear\" w:fill=\"D9D9D9\"/>", "Shaded") +
            Cell(string.Empty, "Plain one"),
            Continues() + Cell(string.Empty, "Plain two")))).Pages[0];

        var fill = Assert.Single(page.Rectangles, r => r.Height > 2 && r.Width > 2);

        // Two rows of a line each, and the fill covers both.
        Assert.Equal(28.6, fill.Height, 1);

        var borders = page.Rectangles.Where(r => r.Height < 2 || r.Width < 2).ToList();
        Assert.True(page.Rectangles.IndexOf(fill) < borders.Max(page.Rectangles.IndexOf),
            "the fill is drawn over the borders it should sit under");
    }


    /// <summary>
    /// A row holding a merged cell divides like any other, and divides the run with it: what is
    /// left of the merged cell's text carries on over the page rather than following the row.
    /// </summary>
    [Fact]
    public void A_row_holding_a_merged_cell_divides()
    {
        var layout = LayoutOf(Fixtures.Build("table-merge-split"));

        static List<string> On(LaidOutPage page, string prefix) =>
            [.. page.Lines.SelectMany(l => l.Texts).Select(t => t.Text).Where(t => t.StartsWith(prefix))];

        // The row is twelve lines tall and eight of them fit, so it divides there — and the merged
        // cell divides at the same place, not at its own twentieth line.
        Assert.Equal(8, On(layout.Pages[0], "Beside").Count);
        Assert.Equal(8, On(layout.Pages[0], "Merged").Count);

        Assert.Equal(4, On(layout.Pages[1], "Beside").Count);
        Assert.Equal(12, On(layout.Pages[1], "Merged").Count);

        // In order, with nothing repeated across the break.
        Assert.Equal("Merged 8", On(layout.Pages[0], "Merged")[^1]);
        Assert.Equal("Merged 9", On(layout.Pages[1], "Merged")[0]);
    }

    /// <summary>
    /// The run's last row still takes what the rows above could not hold, and it takes it on the
    /// page the run ends on: the three-line row after the break grows to hold the eight lines of
    /// merged text that are left.
    /// </summary>
    [Fact]
    public void The_run_still_ends_where_its_content_does()
    {
        var page = LayoutOf(Fixtures.Build("table-merge-split")).Pages[1];

        var last = page.Lines
            .Where(l => l.Texts.Any(t => t.Text.StartsWith("Merged")))
            .Max(l => l.BaselineY);

        var after = page.Lines
            .Where(l => l.Texts.Any(t => t.Text.StartsWith("After")))
            .Max(l => l.BaselineY);

        // The merged text runs on past the last of the rows beside it.
        Assert.True(last > after, "the merged text ends above the row it runs through");

        // And the table closes below all of it.
        var rules = page.Rectangles.Where(r => r.Height < 2).Select(r => r.Y).ToList();
        Assert.True(rules.Max() > last, "the table closes above the last of the merged lines");
    }

    /// <summary>
    /// Word rules a merged cell where a page ends even though it rules none between the rows of a
    /// run, so both halves are closed boxes: the run is shut at the foot of the page it leaves and
    /// opened again at the top of the one it lands on.
    /// </summary>
    [Fact]
    public void A_run_is_ruled_where_the_page_divides_it()
    {
        var layout = LayoutOf(Fixtures.Build("table-merge-split"));

        static List<double> RulesOverTheMergedColumn(LaidOutPage page) =>
            [.. page.Rectangles
                .Where(r => r.Height < 2 && r.Width > 100 && r.X < 100)
                .Select(r => Math.Round(r.Y, 2))
                .Distinct()
                .Order()];

        var first = RulesOverTheMergedColumn(layout.Pages[0]);
        var second = RulesOverTheMergedColumn(layout.Pages[1]);

        // Where the row divides: the table's own top, and the rule that closes the page.
        Assert.Equal(2, first.Count);

        // And on the next page the run opens again, with no rule between its rows until the table
        // ends — the top of the page and the foot of the table, and nothing between them.
        Assert.Equal(2, second.Count);

        var lines = layout.Pages[1].Lines.Where(l => l.Texts.Count > 0).ToList();
        Assert.True(second[0] < lines.Min(l => l.BaselineY - l.Ascent) + 0.5);
        Assert.True(second[1] > lines.Max(l => l.BaselineY));
    }
    /// <summary>
    /// A run whose rows do not all fit on one page is closed off where the page ends and opened
    /// again on the next, with the text carrying on there rather than being lost.
    /// </summary>
    [Fact]
    public void A_run_that_reaches_the_foot_of_the_page_carries_on_over_it()
    {
        var builder = new DocxBuilder();
        for (var i = 1; i <= 40; i++) builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        var rows = new List<string>();
        for (var i = 1; i <= 12; i++)
            rows.Add((i == 1
                         ? Merged(string.Empty, [.. Enumerable.Range(1, 12).Select(n => $"Merged {n}")])
                         : Continues()) +
                     Cell(string.Empty, $"Row {i}"));

        var layout = LayoutOf(builder.AddRawParagraph(Table([.. rows])));

        Assert.Equal(2, layout.Pages.Count);

        static List<string> MergedLines(LaidOutPage page) =>
            [.. page.Lines.SelectMany(l => l.Texts).Select(t => t.Text).Where(t => t.StartsWith("Merged"))];

        var first = MergedLines(layout.Pages[0]);
        var second = MergedLines(layout.Pages[1]);

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.Equal(12, first.Count + second.Count);

        // In order, and unbroken across the page: what the first page could not hold begins the
        // second.
        Assert.Equal("Merged 1", first[0]);
        Assert.Equal($"Merged {first.Count + 1}", second[0]);
    }
}
