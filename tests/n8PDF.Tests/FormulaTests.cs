using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests the two fields that work something out rather than look it up: IF, which chooses between
/// two pieces of text, and the formula field, which is arithmetic — over numbers written into it,
/// or over the cells of the table it stands in.
/// </summary>
/// <remarks>
/// Everything asserted here is read from Word's export of the formulas fixture, which holds each
/// of these on a line of its own. Two of its answers are worth stating outright, because neither
/// is what would be guessed:
///
/// A picture's <c>#</c> reserves a space where it has no digit to show, so five against
/// <c>$#,##0.00</c> comes out as "$   5.00" rather than "$5.00". And a direction reads only as far
/// as the numbers go — a column of 10, "n/a" and 3 sums to 3 from below it — while a range named
/// outright reads all of it and passes over what is not a number, so the same column averaged as
/// A1:A3 is 6.5 rather than 4.33.
/// </remarks>
public class FormulaTests
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

    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Field(string instruction, string cached = "") =>
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:instrText xml:space=\"preserve\">{Escape(instruction)}</w:instrText></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        (cached.Length == 0
            ? $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r>"
            : $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{cached}</w:t></w:r>") +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r>";

    /// <summary>What one field shows, in a document holding nothing else.</summary>
    private static string Shows(string instruction, string cached = "")
    {
        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>{Field(instruction, cached)}</w:p>"));

        return string.Concat(layout.Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text)).Trim();
    }

    [Theory]
    [InlineData(" =2+3*4 ", "14")]
    [InlineData(" =(2+3)*4 ", "20")]
    [InlineData(" =10/4 ", "2.5")]
    [InlineData(" =2^10 ", "1024")]
    [InlineData(" =7-9 ", "-2")]
    [InlineData(" =50%*8 ", "4")]
    [InlineData(" =-3+10 ", "7")]
    public void Arithmetic_is_worked_out_as_arithmetic(string instruction, string expected)
    {
        Assert.Equal(expected, Shows(instruction));
    }

    /// <summary>
    /// A formula with no picture reads to two decimal places, with the zeros at the end dropped:
    /// ten thirds is 3.33 and an eighth is 0.13, but ten quarters stays 2.5.
    /// </summary>
    [Theory]
    [InlineData(" =10/3 ", "3.33")]
    [InlineData(" =1/8 ", "0.13")]
    [InlineData(" =10/4 ", "2.5")]
    [InlineData(" =8/2 ", "4")]
    public void A_formula_with_no_picture_reads_to_two_places(string instruction, string expected)
    {
        Assert.Equal(expected, Shows(instruction));
    }

    [Theory]
    [InlineData(" =SUM(1,2,3) ", "6")]
    [InlineData(" =PRODUCT(2,3,4) ", "24")]
    [InlineData(" =AVERAGE(2,4,9) ", "5")]
    [InlineData(" =COUNT(2,9,4) ", "3")]
    [InlineData(" =MAX(2,9,4) ", "9")]
    [InlineData(" =MIN(2,9,4) ", "2")]
    [InlineData(" =ROUND(3.14159,2) ", "3.14")]
    [InlineData(" =ABS(-7) ", "7")]
    [InlineData(" =INT(7.9) ", "7")]
    [InlineData(" =MOD(7,3) ", "1")]
    [InlineData(" =SIGN(-3) ", "-1")]
    [InlineData(" =IF(2>1,10,20) ", "10")]
    [InlineData(" =IF(2<1,10,20) ", "20")]
    [InlineData(" =AND(1,1) ", "1")]
    [InlineData(" =AND(1,0) ", "0")]
    [InlineData(" =OR(0,1) ", "1")]
    [InlineData(" =NOT(0) ", "1")]
    [InlineData(" =TRUE() ", "1")]
    public void The_functions_are_the_ones_Word_knows(string instruction, string expected)
    {
        Assert.Equal(expected, Shows(instruction));
    }

    /// <summary>
    /// A picture says how the answer is to be spelled. Its <c>0</c> shows a nought where it has no
    /// digit and its <c>#</c> shows a space, which is what puts three of them into "$   5.00".
    /// </summary>
    [Theory]
    [InlineData(" =10/4 \\# \"0.00\" ", "2.50")]
    [InlineData(" =1234567 \\# \"#,##0\" ", "1,234,567")]
    [InlineData(" =5 \\# \"$#,##0.00\" ", "$   5.00")]
    [InlineData(" =0-5 \\# \"0.00;(0.00)\" ", "(5.00)")]
    [InlineData(" =7 \\# \"##\" ", "7")]
    [InlineData(" =7 \\# \"000\" ", "007")]
    public void A_picture_says_how_the_answer_is_spelled(string instruction, string expected)
    {
        // The leading space of an unfilled place is real, and is only trimmed here because a line
        // of text cannot begin with one.
        Assert.Equal(expected, Shows(instruction));
    }

    /// <summary>The picture itself, where a line's own trimming would hide the spaces.</summary>
    [Fact]
    public void An_empty_place_stands_open_or_closed_as_its_own_kind_asks()
    {
        Assert.Equal("$   5.00", NumericPicture.Format(5, "$#,##0.00"));
        Assert.Equal(" 7", NumericPicture.Format(7, "##"));
        Assert.Equal("07", NumericPicture.Format(7, "00"));

        // A number with more digits than the picture has places keeps all of them.
        Assert.Equal("1,234,567", NumericPicture.Format(1234567, "#,##0"));
        Assert.Equal("1234567", NumericPicture.Format(1234567, "0"));
    }

    [Theory]
    [InlineData(" IF 1 = 1 \"yes\" \"no\" ", "yes")]
    [InlineData(" IF 2 > 3 \"yes\" \"no\" ", "no")]
    [InlineData(" IF 5 >= 5 \"yes\" \"no\" ", "yes")]
    [InlineData(" IF 5 <> 4 \"yes\" \"no\" ", "yes")]
    [InlineData(" IF \"abc\" = \"abc\" \"yes\" \"no\" ", "yes")]
    [InlineData(" IF \"abc\" <> \"abd\" \"yes\" \"no\" ", "yes")]
    [InlineData(" IF \"ABC\" = \"abc\" \"yes\" \"no\" ", "yes")]
    public void An_if_field_chooses_between_two_pieces_of_text(string instruction, string expected)
    {
        Assert.Equal(expected, Shows(instruction));
    }

    /// <summary>
    /// The text an equality is compared against may hold wildcards, which is how a field asks
    /// whether something begins with a word.
    /// </summary>
    [Theory]
    [InlineData(" IF \"abcdef\" = \"abc*\" \"yes\" \"no\" ", "yes")]
    [InlineData(" IF \"abcdef\" = \"xyz*\" \"yes\" \"no\" ", "no")]
    [InlineData(" IF \"cat\" = \"c?t\" \"yes\" \"no\" ", "yes")]
    public void An_equality_may_be_asked_with_wildcards(string instruction, string expected)
    {
        Assert.Equal(expected, Shows(instruction));
    }

    /// <summary>
    /// A field naming no answer for the case it landed in shows nothing, which is an answer rather
    /// than something that could not be worked out — the cached result does not stand.
    /// </summary>
    [Fact]
    public void An_if_field_with_no_answer_for_its_case_shows_nothing()
    {
        Assert.Equal("", Shows(" IF 1 = 2 \"yes\" ", "cached"));
    }

    /// <summary>
    /// A formula that cannot be worked out keeps what the document last showed for it, the way
    /// every other field does.
    /// </summary>
    [Theory]
    [InlineData(" =1/0 ")]
    [InlineData(" =2+ ")]
    [InlineData(" =NOSUCHFUNCTION(1) ")]
    [InlineData(" IF 1 \"yes\" \"no\" ")]
    public void What_cannot_be_worked_out_keeps_its_cached_result(string instruction)
    {
        Assert.Equal("cached", Shows(instruction, "cached"));
    }

    // ----- formulas over a table -----

    /// <summary>A three-column table of the given rows, each cell written as its content.</summary>
    private static string Table(params string[][] rows)
    {
        var body = string.Concat(rows.Select(cells =>
            "<w:tr>" + string.Concat(cells.Select(cell =>
                $"<w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                (cell.StartsWith(" =") || cell.StartsWith(" IF")
                    ? Field(cell)
                    : $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{cell}</w:t></w:r>") +
                "</w:p></w:tc>")) + "</w:tr>"));

        return "<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/>" +
               "<w:tblLayout w:type=\"fixed\"/></w:tblPr>" +
               "<w:tblGrid><w:gridCol w:w=\"3120\"/><w:gridCol w:w=\"3120\"/><w:gridCol w:w=\"3120\"/></w:tblGrid>" +
               body + "</w:tbl>";
    }

    private static List<string> CellsOf(byte[] docx)
    {
        var layout = LayoutOf(docx);

        return [.. layout.Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text.Trim())];
    }

    private static List<string> CellsOf(DocxBuilder builder)
    {
        var layout = LayoutOf(builder);

        return [.. layout.Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text.Trim())];
    }

    /// <summary>A direction stands for the cells running that way from the one it is in.</summary>
    [Fact]
    public void A_direction_reads_the_cells_around_the_formula()
    {
        var cells = CellsOf(new DocxBuilder().AddRawParagraph(Table(
            ["10", "20", " =SUM(LEFT) "],
            ["3", "4.5", " =SUM(ABOVE) "],
            [" =SUM(ABOVE) ", " =AVERAGE(ABOVE) ", " =COUNT(ABOVE) "])));

        Assert.Contains("30", cells);      // 10 + 20, to the left
        Assert.Contains("13", cells);      // 10 + 3, above
        Assert.Contains("12.25", cells);   // (20 + 4.5) / 2
    }

    /// <summary>
    /// A direction reads only as far as the numbers go: a cell of text stops it, and what lies
    /// beyond is not counted.
    /// </summary>
    [Fact]
    public void A_direction_stops_at_the_first_cell_that_is_not_a_number()
    {
        var cells = CellsOf(new DocxBuilder().AddRawParagraph(Table(
            ["10", "20", "x"],
            ["n/a", "4.5", "y"],
            ["3", "6", "z"],
            [" =SUM(ABOVE) ", " =SUM(ABOVE) ", "end"])));

        // The first column stops at "n/a" and comes to 3; the second reads all of it.
        Assert.Contains("3", cells);
        Assert.Contains("30.5", cells);
    }

    /// <summary>
    /// A range named outright reads the whole of it and passes over what is not a number, rather
    /// than stopping or counting it as nothing.
    /// </summary>
    [Fact]
    public void A_range_passes_over_the_cells_that_hold_no_number()
    {
        var cells = CellsOf(new DocxBuilder().AddRawParagraph(Table(
            ["10", "20", "x"],
            ["n/a", "4.5", "y"],
            ["3", "6", " =AVERAGE(A1:A3) "])));

        // Thirteen over two rather than over three.
        Assert.Contains("6.5", cells);
    }

    [Fact]
    public void A_cell_can_be_named_outright()
    {
        var cells = CellsOf(new DocxBuilder().AddRawParagraph(Table(
            ["10", "20", " =A1*B1 "],
            ["3", "4.5", " =SUM(A1:B2) "])));

        Assert.Contains("200", cells);
        Assert.Contains("37.5", cells);
    }

    /// <summary>
    /// A cell holding a formula is worked out rather than read as text, so a total can be totalled.
    /// </summary>
    [Fact]
    public void A_total_can_be_read_by_another_total()
    {
        var cells = CellsOf(new DocxBuilder().AddRawParagraph(Table(
            ["10", "20", " =SUM(LEFT) "],
            ["3", "4", " =SUM(LEFT) "],
            ["", "", " =SUM(ABOVE) "])));

        Assert.Contains("30", cells);
        Assert.Contains("7", cells);
        Assert.Contains("37", cells);
    }

    /// <summary>
    /// A cell reading "$1,200.00" is twelve hundred: what is written around a number does not stop
    /// it being one, while a cell of words is no number at all.
    /// </summary>
    [Fact]
    public void What_is_written_around_a_number_does_not_stop_it_being_one()
    {
        var cells = CellsOf(new DocxBuilder().AddRawParagraph(Table(
            ["$1,200.00", "300", " =SUM(LEFT) "])));

        Assert.Contains("1500", cells);
    }

    [Fact]
    public void The_fixture_works_out_every_one_of_its_formulas()
    {
        var lines = LayoutOf(Fixtures.Build("formulas")).Pages
            .SelectMany(page => page.Lines)
            .Select(line => string.Concat(line.Texts.OrderBy(t => t.X).Select(t => t.Text)).Trim())
            .ToList();

        Assert.Contains("sum-of-terms: 14", lines);
        Assert.Contains("if-wildcard: yes", lines);
        Assert.Contains("picture-money: $   5.00", lines);

        // The table's last row, which is three formulas reading three different ways: a column
        // stopped by a cell of text, a count of the one beside it, and a range that reads past it.
        var cells = LayoutOf(Fixtures.Build("formulas")).Pages
            .SelectMany(page => page.Lines)
            .SelectMany(line => line.Texts)
            .Select(text => text.Text.Trim())
            .ToList();

        Assert.Contains("3", cells);
        Assert.Contains("6.5", cells);
        Assert.Contains("43.5", cells);
    }
}
