using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Styling;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// What a table takes from its style rather than from itself.
/// </summary>
/// <remarks>
/// Two halves. The first asks the rules directly — which conditional format reaches which cell,
/// and what a table's own properties do to what its style said — because a rule is easier to read
/// as a rule than as a page of coordinates. The second asks Word, which is the only thing that
/// can say the rules are the right ones: table-style-conditional-probe gives every one of the
/// thirteen formats a different type size, so the size a cell is drawn at names the format that
/// reached it, and the whole precedence order comes off one document.
/// </remarks>
public class TableStyleTests
{
    private const string Styles = """
        <w:style w:type="table" w:default="1" w:styleId="TableNormal">
          <w:name w:val="Normal Table"/>
          <w:tblPr>
            <w:tblInd w:w="0" w:type="dxa"/>
            <w:tblCellMar>
              <w:left w:w="108" w:type="dxa"/><w:right w:w="108" w:type="dxa"/>
            </w:tblCellMar>
          </w:tblPr>
        </w:style>
        <w:style w:type="table" w:styleId="TableGrid">
          <w:name w:val="Table Grid"/>
          <w:basedOn w:val="TableNormal"/>
          <w:tblPr>
            <w:tblBorders>
              <w:top w:val="single" w:sz="4"/><w:left w:val="single" w:sz="4"/>
              <w:bottom w:val="single" w:sz="4"/><w:right w:val="single" w:sz="4"/>
              <w:insideH w:val="single" w:sz="4"/><w:insideV w:val="single" w:sz="6"/>
            </w:tblBorders>
          </w:tblPr>
          <w:tblStylePr w:type="firstRow">
            <w:rPr><w:b/></w:rPr>
            <w:tcPr><w:shd w:val="clear" w:fill="D9D9D9"/></w:tcPr>
          </w:tblStylePr>
          <w:tblStylePr w:type="band1Horz">
            <w:tcPr><w:shd w:val="clear" w:fill="EEEEEE"/></w:tcPr>
          </w:tblStylePr>
        </w:style>
        """;

    /// <summary>A two-by-two table wearing a style, with whatever it declares of its own.</summary>
    private static Table Build(string tableProperties = "", string firstCell = "")
    {
        var xml = XDocument.Parse($"""
            <w:tbl xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:tblPr><w:tblStyle w:val="TableGrid"/>{tableProperties}</w:tblPr>
              <w:tblGrid><w:gridCol w:w="2340"/><w:gridCol w:w="2340"/></w:tblGrid>
              <w:tr>
                <w:tc><w:tcPr>{firstCell}</w:tcPr><w:p><w:r><w:t>A</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>B</w:t></w:r></w:p></w:tc>
              </w:tr>
              <w:tr>
                <w:tc><w:p><w:r><w:t>C</w:t></w:r></w:p></w:tc>
                <w:tc><w:p><w:r><w:t>D</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            """);

        var table = DocumentParser.ParseTable(xml.Root!);
        TableStyles.Apply(table, Definitions());

        return table;
    }

    private static StyleDefinitions Definitions() => StylesParser.Parse(XDocument.Parse($"""
        <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">{Styles}</w:styles>
        """));

    /// <summary>
    /// The rules a gallery table is drawn with live in the style and nowhere else, so a table that
    /// declares no border of its own is still ruled on every edge and between every cell.
    /// </summary>
    [Fact]
    public void A_table_is_ruled_by_its_style()
    {
        var borders = Build().Properties.Borders;

        Assert.Equal(4, borders.Top!.SizeEighthPoints);
        Assert.Equal(4, borders.InsideHorizontal!.SizeEighthPoints);
        Assert.Equal(6, borders.InsideVertical!.SizeEighthPoints);
        Assert.True(borders.Left!.IsVisible);
        Assert.True(borders.Right!.IsVisible);
        Assert.True(borders.Bottom!.IsVisible);
    }

    /// <summary>And a table that names no style is ruled by nothing.</summary>
    [Fact]
    public void A_table_with_no_style_is_ruled_by_nothing()
    {
        var xml = XDocument.Parse("""
            <w:tbl xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:tblGrid><w:gridCol w:w="2340"/></w:tblGrid>
              <w:tr><w:tc><w:p/></w:tc></w:tr>
            </w:tbl>
            """);

        var table = DocumentParser.ParseTable(xml.Root!);
        TableStyles.Apply(table, Definitions());

        Assert.Null(table.Properties.Borders.Top);
        Assert.Null(table.Properties.Borders.InsideHorizontal);
    }

    /// <summary>A style is inherited through <c>basedOn</c> like any other.</summary>
    [Fact]
    public void What_the_style_is_based_on_comes_with_it()
    {
        // The cell margins are TableNormal's; the borders above are TableGrid's own.
        var properties = Build().Properties;

        Assert.Equal(108, properties.CellMarginLeftTwips);
        Assert.Equal(108, properties.CellMarginRightTwips);
        Assert.Equal(0, properties.IndentTwips);
    }

    /// <summary>Anything the table says for itself wins over what the style said.</summary>
    [Fact]
    public void The_table_overrides_its_style()
    {
        var properties = Build("""
            <w:tblInd w:w="720" w:type="dxa"/>
            <w:tblBorders><w:top w:val="single" w:sz="24"/></w:tblBorders>
            <w:tblCellMar><w:left w:w="0" w:type="dxa"/></w:tblCellMar>
            """).Properties;

        Assert.Equal(720, properties.IndentTwips);
        Assert.Equal(24, properties.Borders.Top!.SizeEighthPoints);
        Assert.Equal(0, properties.CellMarginLeftTwips);

        // What it did not override is still the style's.
        Assert.Equal(108, properties.CellMarginRightTwips);
        Assert.Equal(4, properties.Borders.InsideHorizontal!.SizeEighthPoints);
    }

    /// <summary>
    /// A conditional format reaches a cell only where the table's <c>w:tblLook</c> asks for it.
    /// </summary>
    [Fact]
    public void The_look_says_which_conditional_formats_are_in_force()
    {
        // Nothing asked for: no first row, so no shading and no banding either.
        var plain = Build("<w:tblLook w:val=\"0000\" w:firstRow=\"0\" w:noHBand=\"1\"/>");
        Assert.Null(plain.Rows[0].Cells[0].ShadingPaint);
        Assert.Null(plain.Rows[1].Cells[0].ShadingPaint);

        // Asked for: the first row is shaded, and the row after it is the first band.
        var styled = Build("<w:tblLook w:val=\"0020\" w:firstRow=\"1\" w:noHBand=\"0\"/>");
        Assert.Equal("D9D9D9", Hex(styled.Rows[0].Cells[0]));
        Assert.Equal("EEEEEE", Hex(styled.Rows[1].Cells[0]));
    }

    /// <summary>The older spelling of the look says the same thing in one number.</summary>
    [Fact]
    public void The_look_can_be_spelled_as_a_number()
    {
        // 0x0020 is the first row and nothing else; the banding bits being clear leaves banding on.
        var table = Build("<w:tblLook w:val=\"0020\"/>");

        Assert.Equal("D9D9D9", Hex(table.Rows[0].Cells[0]));
        Assert.Equal("EEEEEE", Hex(table.Rows[1].Cells[0]));
    }

    /// <summary>
    /// A cell shading itself overrides the style, and a cell declaring no fill at all is not the
    /// same as one declaring "auto": the second turns off shading the style would have given it.
    /// </summary>
    /// <remarks>
    /// Turning it off leaves the cell white rather than leaving it unpainted, which is Word's own
    /// doing — see cell-shading-probe, where a cell asking for a clear pattern over an automatic
    /// fill comes out of Word as a white rectangle and a paragraph asking for exactly the same
    /// thing comes out as nothing at all.
    /// </remarks>
    [Fact]
    public void A_cell_overrides_the_shading_its_style_gives_it()
    {
        var look = "<w:tblLook w:val=\"0020\" w:firstRow=\"1\"/>";

        Assert.Equal("FF0000", Hex(Build(look, "<w:shd w:val=\"clear\" w:fill=\"FF0000\"/>")
            .Rows[0].Cells[0]));

        Assert.Equal("FFFFFF", Hex(Build(look, "<w:shd w:val=\"clear\" w:fill=\"auto\"/>")
            .Rows[0].Cells[0]));

        Assert.Equal("D9D9D9", Hex(Build(look).Rows[0].Cells[0]));
    }

    /// <summary>The colour a cell is painted, as RRGGBB, or null where it is painted none.</summary>
    private static string? Hex(n8PDF.Ooxml.TableCell cell) =>
        cell.ShadingPaint is not { } paint
            ? null
            : $"{(int)Math.Round(paint.Red * 255):X2}" +
              $"{(int)Math.Round(paint.Green * 255):X2}" +
              $"{(int)Math.Round(paint.Blue * 255):X2}";

    /// <summary>
    /// The style's text formatting reaches the paragraphs in its cells, and sits below the
    /// paragraph's own style in the cascade.
    /// </summary>
    [Fact]
    public void The_style_formats_the_text_in_its_cells()
    {
        var table = Build("<w:tblLook w:val=\"0020\" w:firstRow=\"1\"/>");
        var resolver = new StyleResolver(Definitions());

        var heading = (Paragraph)table.Rows[0].Cells[0].Content[0];
        var body = (Paragraph)table.Rows[1].Cells[0].Content[0];

        Assert.True(resolver.ResolveRun(heading.Properties, null).Bold);
        Assert.False(resolver.ResolveRun(body.Properties, null).Bold);
    }

    /// <summary>
    /// And the whole of it against Word. Every cell of the probe's tables is drawn at the size of
    /// whichever conditional format reached it, so comparing the sizes cell for cell says the two
    /// resolved the same format in every one of them.
    /// </summary>
    /// <remarks>
    /// What is counted is how many characters each page holds at each size, rather than the size
    /// of each run one by one: Word breaks a cell's four characters into three runs and this
    /// reader into one, so run against run compares nothing. Which cells came out at which size
    /// is what the counts say, and that they are in the right places is TextPositionComparison's
    /// to say — it matches these two documents line for line.
    ///
    /// Sizes are read to the nearest quarter point because Word rounds the size it writes to
    /// 1/300 inch, the same quantum it rounds every position to: 15pt is written as 15.12.
    /// </remarks>
    [Fact]
    public void Every_cell_is_set_in_the_size_word_sets_it_in()
    {
        var (ours, theirs) = BothWays("table-style-conditional-probe");

        var mine = CharactersBySize(ours);
        var word = CharactersBySize(theirs);

        foreach (var (where, count) in word)
        {
            Assert.True(mine.TryGetValue(where, out var ourCount) && ourCount == count,
                $"page {where.Page + 1} holds {count} characters at {where.Size:0.##}pt in Word's " +
                $"and {mine.GetValueOrDefault(where)} in ours, so a different conditional format " +
                "reached one of its cells.");
        }

        Assert.Equal(word.Count, mine.Count);
    }

    /// <summary>
    /// How many characters each page draws at each size, which is what says which conditional
    /// format reached which cell.
    /// </summary>
    private static Dictionary<(int Page, double Size), int> CharactersBySize(byte[] pdf)
    {
        var counted = new Dictionary<(int, double), int>();

        foreach (var run in PdfTextExtractor.Extract(pdf))
        {
            var characters = run.Text.Count(character => !char.IsWhiteSpace(character));
            if (characters == 0) continue;

            var where = (run.PageIndex, Math.Round(run.FontSize * 4, MidpointRounding.AwayFromZero) / 4);
            counted[where] = counted.GetValueOrDefault(where) + characters;
        }

        return counted;
    }

    /// <summary>Every cell the style shades, shaded in the same colour and over the same box.</summary>
    /// <remarks>
    /// Word paints a shaded cell twice, once over the cell and once over the box its text sits in,
    /// so its rectangles are not counted — what is asserted is that each of ours has one of its
    /// underneath.
    /// </remarks>
    [Fact]
    public void Every_shaded_cell_is_shaded_the_way_word_shades_it()
    {
        var (ours, theirs) = BothWays("table-style-conditional-probe");

        var word = PdfPathExtractor.Extract(theirs);
        var shaded = PdfPathExtractor.Extract(ours)
            .Where(rectangle => rectangle.ColorHex != "000000")
            .ToList();

        // Seven pages of tables, all but one of them shaded throughout.
        Assert.Equal(92, shaded.Count);

        foreach (var rectangle in shaded)
        {
            Assert.True(word.Any(other =>
                    other.PageIndex == rectangle.PageIndex &&
                    other.ColorHex == rectangle.ColorHex &&
                    Math.Abs(other.Left - rectangle.Left) < 0.3 &&
                    Math.Abs(other.Top - rectangle.Top) < 0.3 &&
                    Math.Abs(other.Width - rectangle.Width) < 0.3 &&
                    Math.Abs(other.Height - rectangle.Height) < 0.3),
                $"Word shades nothing like {rectangle}.");
        }
    }

    /// <summary>
    /// And the table its style rules: a line wherever Word draws one, across and down.
    /// </summary>
    /// <remarks>
    /// The rules themselves are compared rather than the rectangles that make them up, because the
    /// two are drawn differently: Word divides a rule at every cell boundary and fills the corners
    /// in with squares of their own, where one rule here is one rectangle. What both agree on is
    /// where the ink is, which is what is asserted — every line across at the same height, every
    /// line down at the same place.
    /// </remarks>
    [Fact]
    public void A_table_is_ruled_where_word_rules_it()
    {
        var (ours, theirs) = BothWays("table-style");

        Compare(Rules(ours, across: true), Rules(theirs, across: true), "across");
        Compare(Rules(ours, across: false), Rules(theirs, across: false), "down");

        static void Compare(List<double> mine, List<double> word, string which)
        {
            Assert.True(word.Count > 0, $"Word ruled nothing {which}, so there is nothing to check.");
            Assert.Equal(word.Count, mine.Count);

            for (var i = 0; i < mine.Count; i++)
            {
                // Half a point, which is a little over Word's own quantum of 1/300 inch: the
                // rules run {which} at [{string.Join(", ", word)}] in Word's.
                Assert.True(Math.Abs(mine[i] - word[i]) < 0.5,
                    $"a rule {which} is at {mine[i]:0.##} where Word's is at {word[i]:0.##}.");
            }
        }
    }

    /// <summary>
    /// Where the rules are: the height of each line across the table, or the position of each line
    /// down it. The squares Word fills the corners in with are left out, being neither.
    /// </summary>
    private static List<double> Rules(byte[] pdf, bool across) =>
        [.. PdfPathExtractor.Extract(pdf)
            .Where(r => across ? r.Height < 2 && r.Width > 2 : r.Width < 2 && r.Height > 2)
            .Select(r => across ? r.Top : r.Left)
            .Select(position => Math.Round(position, 1))
            .Distinct()
            .Order()];

    private static (byte[] Ours, byte[] Theirs) BothWays(string fixtureName)
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        return (Converter.Convert(Fixtures.Build(fixtureName),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));
    }
}
