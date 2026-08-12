using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests how far inside its edge a cell starts its text.
/// </summary>
/// <remarks>
/// Three separate things push the text in — the cell margin, the border, and the default that
/// applies when no margin is declared — and none of them adds to the others in the way that would
/// be guessed. table-inset-weights-probe holds the same one-cell table fifteen times over, varying
/// border weight from nothing to six points against a margin of zero, then margin against a fixed
/// border, then leaving the margin element out altogether; Word's export of it is where every
/// number below comes from. The border rectangles in that export settle the frame the numbers are
/// read in: Word draws a table's left border straddling the left margin at every weight, so the
/// table's edge is on the margin and the inset is measured from there.
/// </remarks>
public class TableInsetTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private const double Margin = 72;

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

    /// <summary>
    /// A one-cell table of the given border weight in eighths of a point and cell margin in twips,
    /// with a margin of null leaving the element out as a hand-written document usually does.
    /// </summary>
    private static LaidOutPage Table(int eighths, int? marginTwips)
    {
        var borders = eighths == 0
            ? string.Empty
            : "<w:tblBorders>" +
              $"<w:top w:val=\"single\" w:sz=\"{eighths}\"/><w:left w:val=\"single\" w:sz=\"{eighths}\"/>" +
              $"<w:bottom w:val=\"single\" w:sz=\"{eighths}\"/><w:right w:val=\"single\" w:sz=\"{eighths}\"/>" +
              "</w:tblBorders>";

        var margins = marginTwips is { } twips
            ? "<w:tblCellMar>" +
              $"<w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"{twips}\" w:type=\"dxa\"/>" +
              $"<w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"{twips}\" w:type=\"dxa\"/>" +
              "</w:tblCellMar>"
            : string.Empty;

        return LayoutOf(new DocxBuilder().AddRawParagraph(
            "<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/>" + borders +
            "<w:tblLayout w:type=\"fixed\"/>" + margins + "</w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"9360\"/></w:tblGrid>" +
            $"<w:tr><w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Cell</w:t></w:r></w:p></w:tc></w:tr></w:tbl>")).Pages[0];
    }

    /// <summary>Where the cell's text starts, across and down.</summary>
    private static (double X, double Top) Text(LaidOutPage page)
    {
        var line = page.Lines.Single(l => l.Texts.Any(t => t.Text == "Cell"));
        return (line.Texts[0].X, line.BaselineY - line.Ascent);
    }

    /// <summary>
    /// Half of a border falls inside the cell and half outside, and text starts at the inner edge:
    /// a two point border puts it a point in, not two.
    /// </summary>
    [Theory]
    [InlineData(4, 0.25)]
    [InlineData(16, 1.0)]
    [InlineData(48, 3.0)]
    public void Text_starts_at_the_inside_edge_of_the_border(int eighths, double expected)
    {
        Assert.Equal(Margin + expected, Text(Table(eighths, 0)).X, 2);
    }

    /// <summary>
    /// A margin and a border do not add: whichever reaches further in is the whole of the inset.
    /// A margin of three and a half points against a one point border leaves the text three and a
    /// half points in, because the half of the border inside the cell is already inside the margin.
    /// </summary>
    [Fact]
    public void A_margin_and_a_border_do_not_add()
    {
        // 72 twips is 3.6pt, well clear of the half point that a one point border reaches in.
        Assert.Equal(Margin + 3.6, Text(Table(8, 72)).X, 2);

        // And the other way about: a margin narrower than the border disappears into it.
        Assert.Equal(Margin + 3.0, Text(Table(48, 20)).X, 2);
    }

    /// <summary>
    /// Downwards it is not the same rule. Word clears the whole border above the text rather than
    /// the half of it that is inside the cell, which the probe shows at every weight: a six point
    /// border pushes the first line six points down, twice as far as it pushes it across.
    /// </summary>
    [Fact]
    public void The_top_clears_the_whole_border_rather_than_half_of_it()
    {
        var plain = Text(Table(0, 0)).Top;

        Assert.Equal(plain + 0.5, Text(Table(4, 0)).Top, 2);
        Assert.Equal(plain + 6.0, Text(Table(48, 0)).Top, 2);

        // Which is twice what the same border does across.
        Assert.Equal(Margin + 3.0, Text(Table(48, 0)).X, 2);
    }

    /// <summary>
    /// Declaring no cell margin is not the same as declaring one of zero. Word puts half a point
    /// of padding into a table that says nothing about it — the familiar 108 twips comes from the
    /// built-in TableNormal style, which a document written by hand does not have, but what is
    /// left over is not nothing.
    /// </summary>
    [Fact]
    public void An_absent_margin_is_not_a_margin_of_zero()
    {
        Assert.Equal(Margin, Text(Table(0, 0)).X, 2);
        Assert.Equal(Margin + 0.5, Text(Table(0, null)).X, 2);

        // It is a margin like any other, so a border wider than it still wins.
        Assert.Equal(Margin + 0.5, Text(Table(4, null)).X, 2);
        Assert.Equal(Margin + 1.0, Text(Table(16, null)).X, 2);
    }

    /// <summary>
    /// The fixture keeps its shape: fifteen tables, one to a page. Stacked, each table's height
    /// would carry into the next one's position and a difference of a fraction at the top of the
    /// page would read as twenty points at the foot of it — which is what the comparison against
    /// Word's export of this fixture measured before the tables were separated.
    /// </summary>
    [Fact]
    public void The_fixture_holds_one_table_to_a_page()
    {
        var layout = LayoutOf(Fixtures.Build("table-inset-weights-probe"));

        Assert.Equal(15, layout.Pages.Count);
        Assert.All(layout.Pages, page => Assert.Single(page.Lines, l => l.Texts.Count > 0));
    }
}
