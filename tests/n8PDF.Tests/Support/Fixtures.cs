using System.Globalization;
namespace n8PDF.Tests.Support;

/// <summary>
/// The catalogue of hand-authored fixtures, each isolating one feature.
/// </summary>
/// <remarks>
/// Defined in code so the markup is diffable and reviewable, and materialised to
/// <c>Fixtures/Minimal</c> as real <c>.docx</c> files so the same document can be opened in Word
/// and compared against our output by eye or, once reference PDFs exist, by measurement.
/// </remarks>
public static class Fixtures
{
    private const string TimesNewRoman = "Times New Roman";

    /// <summary>Run properties in schema order. Never concatenate these strings by hand.</summary>
    private static string Times(
        int halfPoints = 24,
        bool bold = false,
        bool italic = false,
        bool strike = false,
        string? color = null,
        string? highlight = null,
        string? underline = null,
        string? emphasis = null,
        string? borderStyle = null,
        int borderEighths = 8,
        int borderSpace = 0,
        string? shadingFill = null,
        string? shadingPattern = null,
        string? shadingColor = null,
        int? kerningHalfPoints = null,
        int? positionHalfPoints = null) =>
        DocxBuilder.RunProperties(
            font: TimesNewRoman, halfPoints: halfPoints, bold: bold, italic: italic,
            strike: strike, color: color, highlight: highlight, underline: underline,
            emphasis: emphasis,
            borderStyle: borderStyle, borderEighths: borderEighths, borderSpace: borderSpace,
            shadingFill: shadingFill, shadingPattern: shadingPattern, shadingColor: shadingColor,
            kerningHalfPoints: kerningHalfPoints, positionHalfPoints: positionHalfPoints);

    private static readonly string Times12 = Times();

    /// <summary>
    /// A style of the given id and name. The properties are written in the order the schema puts
    /// them in: tabs before spacing, indents after it, and the outline level last of all.
    /// </summary>
    private static string Style(
        string id, string name, string tabs = "", string indent = "", string outline = "",
        bool bold = false) =>
        $"<w:style w:type=\"paragraph\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/>" +
        $"<w:pPr>{tabs}{ZeroSpacing}{indent}{outline}</w:pPr>" +
        $"<w:rPr>{Times(24, bold: bold)}</w:rPr></w:style>";

    /// <summary>
    /// What a table-of-contents style carries: a right tab stop at the far margin with a dotted
    /// leader running out to it, which is what the page number sits against.
    /// </summary>
    private const string TocTab =
        "<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9360\"/></w:tabs>";

    /// <summary>
    /// A table whose last row and last column work themselves out: the formulas read the cells
    /// above and to the left of them, and one reads two cells named outright.
    /// </summary>
    private static string FormulaTable()
    {
        static string Cell(string content) =>
            $"<w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr>{content}</w:p></w:tc>";

        static string Text(string text) =>
            Cell($"<w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r>");

        static string Formula(string instruction) => Cell(StyleRefRuns(instruction));

        return "<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/>" +
               "<w:tblBorders>" +
               "<w:top w:val=\"single\" w:sz=\"4\"/><w:left w:val=\"single\" w:sz=\"4\"/>" +
               "<w:bottom w:val=\"single\" w:sz=\"4\"/><w:right w:val=\"single\" w:sz=\"4\"/>" +
               "<w:insideH w:val=\"single\" w:sz=\"4\"/><w:insideV w:val=\"single\" w:sz=\"4\"/>" +
               "</w:tblBorders><w:tblLayout w:type=\"fixed\"/></w:tblPr>" +
               "<w:tblGrid><w:gridCol w:w=\"3120\"/><w:gridCol w:w=\"3120\"/><w:gridCol w:w=\"3120\"/></w:tblGrid>" +
               $"<w:tr>{Text("10")}{Text("20")}{Formula(" =SUM(LEFT) ")}</w:tr>" +

               // A cell holding text rather than a number, which says what the ones around it
               // make of it: whether it counts as nothing or stops the reading altogether.
               $"<w:tr>{Text("n/a")}{Text("4.5")}{Formula(" =A1*B2 ")}</w:tr>" +
               $"<w:tr>{Text("3")}{Text("6")}{Formula(" =SUM(A1:B3) ")}</w:tr>" +
               $"<w:tr>{Formula(" =SUM(ABOVE) ")}{Formula(" =COUNT(ABOVE) ")}" +
               $"{Formula(" =AVERAGE(A1:A3) ")}</w:tr></w:tbl>";
    }

    /// <summary>Text as it goes into an XML element.</summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>
    /// The runs of one field: the instruction, and an empty result for Word to fill in.
    /// </summary>
    /// <remarks>
    /// The instruction is escaped, because an instruction is text like any other: the less-than of
    /// a comparison is not markup, and writing it as one leaves a document Word cannot open.
    /// </remarks>
    private static string StyleRefRuns(string instruction) =>
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:instrText xml:space=\"preserve\">{Escape(instruction)}</w:instrText></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r>";

    /// <summary>Numbered lines of text, for a cell that has to be taller than its neighbour.</summary>
    private static IEnumerable<string> Lines(string label, int count) =>
        Enumerable.Range(1, count).Select(i => $"{label} {i}");

    /// <summary>
    /// A cell of a vertical-merge table: the paragraphs given, or the merge marker instead.
    /// </summary>
    /// <param name="merge">
    /// "restart" to begin a merge, "continue" to carry one on, or null for an ordinary cell. A
    /// continuing cell is written empty, as Word writes it: whatever it held is not shown.
    /// </param>
    private static string MergeCell(
        string? merge, string? shading = null, string? alignment = null, params string[] lines)
    {
        var properties = string.Concat(
            merge is null ? string.Empty : $"<w:vMerge w:val=\"{merge}\"/>",
            shading is null ? string.Empty : $"<w:shd w:val=\"clear\" w:fill=\"{shading}\"/>",
            alignment is null ? string.Empty : $"<w:vAlign w:val=\"{alignment}\"/>");

        var content = lines.Length == 0
            ? $"<w:p><w:pPr>{ZeroSpacing}</w:pPr></w:p>"
            : string.Concat(lines.Select(line =>
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                $"<w:t>{line}</w:t></w:r></w:p>"));

        return $"<w:tc>{(properties.Length == 0 ? string.Empty : $"<w:tcPr>{properties}</w:tcPr>")}" +
               $"{content}</w:tc>";
    }

    /// <summary>
    /// A bordered fixed-layout table of the given rows, each already written as cells, with no
    /// cell margins so that a measured position is the geometry and nothing else.
    /// </summary>
    private static string MergeTable(int columns, bool pageBreak, params string[] rows)
    {
        var opening = pageBreak
            ? $"<w:p><w:pPr><w:pageBreakBefore/>{ZeroSpacing}</w:pPr></w:p>"
            : string.Empty;

        var grid = string.Concat(Enumerable.Repeat($"<w:gridCol w:w=\"{9360 / columns}\"/>", columns));

        return opening +
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
               $"<w:tblGrid>{grid}</w:tblGrid>" +
               string.Concat(rows.Select(cells => $"<w:tr>{cells}</w:tr>")) +
               "</w:tbl>";
    }

    /// <summary>
    /// A one-cell table of the given border weight, in eighths of a point, and left cell margin
    /// in twips. Everything else is pinned so that only those two can move the text. A margin of
    /// null leaves the element out altogether, which is the case Word fills in for itself.
    /// </summary>
    private static string InsetProbeTable(string label, int eighths, int? marginTwips, bool pageBreak = false)
    {
        var opening = pageBreak
            ? $"<w:p><w:pPr><w:pageBreakBefore/>{ZeroSpacing}</w:pPr></w:p>"
            : string.Empty;

        var borders = eighths == 0
            ? string.Empty
            : "<w:tblBorders>" +
              $"<w:top w:val=\"single\" w:sz=\"{eighths}\" w:color=\"auto\"/>" +
              $"<w:left w:val=\"single\" w:sz=\"{eighths}\" w:color=\"auto\"/>" +
              $"<w:bottom w:val=\"single\" w:sz=\"{eighths}\" w:color=\"auto\"/>" +
              $"<w:right w:val=\"single\" w:sz=\"{eighths}\" w:color=\"auto\"/>" +
              "</w:tblBorders>";

        return opening +
               "<w:tbl><w:tblPr><w:tblW w:w=\"9360\" w:type=\"dxa\"/>" +
               borders +
               "<w:tblLayout w:type=\"fixed\"/>" +
               (marginTwips is { } margin
                   ? "<w:tblCellMar>" +
                     $"<w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"{margin}\" w:type=\"dxa\"/>" +
                     $"<w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"{margin}\" w:type=\"dxa\"/>" +
                     "</w:tblCellMar>"
                   : string.Empty) +
               "</w:tblPr>" +
               "<w:tblGrid><w:gridCol w:w=\"9360\"/></w:tblGrid>" +
               $"<w:tr><w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
               $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{label}</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";
    }

    /// <summary>
    /// A bordered table of one row and two cells, the first holding as many lines as asked for.
    /// </summary>
    /// <summary>
    /// A table of numbered rows, the first few of which may be marked as heading rows. The rows
    /// say which they are in their own text, so a page of the export says outright which of them
    /// Word put there.
    /// </summary>
    /// <summary>
    /// A narrow table of two columns, with the positioning properties it is given. Narrow so that
    /// there is room for text beside it, and bordered so its edges can be found in the ink.
    /// </summary>
    /// <summary>
    /// A table of two columns whose second cell is turned, for measuring what Word does with
    /// <c>w:textDirection</c>. The first column is left alone, so the row's own height can be read
    /// off it.
    /// </summary>
    /// <summary>
    /// A table of three columns of different widths, each cell saying which it is, for measuring
    /// what <c>w:bidiVisual</c> does with the order of them.
    /// </summary>
    /// <param name="mirrored">Whether the table asks for its columns the other way round.</param>
    /// <param name="span">Whether the middle row's first two cells are joined into one.</param>
    /// <summary>
    /// The body the hyphenation probes share: one paragraph of long words in a narrow measure,
    /// where what changes between the fixtures is only what the document's settings say.
    /// </summary>
    private static DocxBuilder HyphenationBody(DocxBuilder builder, bool capitals = false)
    {
        const string Body =
            "Hyphenation is the business of breaking a word between two lines when the " +
            "remainder would otherwise be unreasonably conspicuous, and typographers " +
            "have argued about it interminably. Consider representative examples: " +
            "communication, extraordinary, misunderstanding, particularly, " +
            "responsibility, understanding, international, development, organisation.";

        const string Capitals =
            "COMMUNICATION EXTRAORDINARY MISUNDERSTANDING PARTICULARLY RESPONSIBILITY " +
            "UNDERSTANDING INTERNATIONAL DEVELOPMENT ORGANISATION CONSIDERATION.";

        return builder.AddParagraph(
            capitals ? Capitals : Body,
            ZeroSpacing + "<w:ind w:left=\"0\" w:right=\"5040\"/>",
            Times12);
    }

    /// <summary>
    /// A table of two rows, for measuring what Word does with two of them written one after the
    /// other with nothing in between.
    /// </summary>
    private static string AdjacentTable(
        string label, int first, int second, string fill, int? indentTwips = null,
        bool banded = false, int? declaredWidth = null)
    {
        string Cell(string text, int width) =>
            $"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/>" +
            $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/></w:tcPr>" +
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(text)}</w:t></w:r></w:p></w:tc>";

        var rows = string.Concat(Enumerable.Range(1, 2).Select(i =>
            $"<w:tr>{Cell($"{label} {i}a", first)}{Cell($"{label} {i}b", second)}</w:tr>"));

        return $"""
                <w:tbl>
                  <w:tblPr>
                    <w:tblW w:w="{declaredWidth ?? first + second}" w:type="dxa"/>
                    {(indentTwips is { } indent ? $"<w:tblInd w:w=\"{indent}\" w:type=\"dxa\"/>" : string.Empty)}
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="24" w:color="auto"/>
                      <w:left w:val="single" w:sz="4" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="24" w:color="auto"/>
                      <w:right w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideH w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideV w:val="single" w:sz="4" w:color="auto"/>
                    </w:tblBorders>
                    <w:tblLayout w:type="fixed"/>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="{first}"/><w:gridCol w:w="{second}"/></w:tblGrid>
                  {rows}
                </w:tbl>
                """;
    }

    private static string ColumnOrderTable(
        string label, bool mirrored, int indentTwips = 0, bool span = false, bool pageBreak = false)
    {
        var opening = pageBreak
            ? $"<w:p><w:pPr><w:pageBreakBefore/>{ZeroSpacing}</w:pPr></w:p>"
            : string.Empty;

        // Shading tells the columns apart in the ink as well as in the text.
        static string Cell(string text, int width, string fill, int grid = 1) =>
            $"<w:tc><w:tcPr><w:tcW w:w=\"{width}\" w:type=\"dxa\"/>" +
            (grid > 1 ? $"<w:gridSpan w:val=\"{grid}\"/>" : string.Empty) +
            $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/></w:tcPr>" +
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(text)}</w:t></w:r></w:p></w:tc>";

        var second = span
            ? $"<w:tr>{Cell($"{label} joined", 2880, "FFE0E0", 2)}{Cell($"{label} C2", 2160, "E0E0FF")}</w:tr>"
            : $"<w:tr>{Cell($"{label} B0", 720, "FFE0E0")}{Cell($"{label} B1", 2160, "E0FFE0")}" +
              $"{Cell($"{label} B2", 2160, "E0E0FF")}</w:tr>";

        return $"""
                {opening}
                <w:tbl>
                  <w:tblPr>
                    {(mirrored ? "<w:bidiVisual/>" : string.Empty)}
                    <w:tblW w:w="5040" w:type="dxa"/>
                    {(indentTwips == 0 ? string.Empty : $"<w:tblInd w:w=\"{indentTwips}\" w:type=\"dxa\"/>")}
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="4" w:color="auto"/>
                      <w:left w:val="single" w:sz="24" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="4" w:color="auto"/>
                      <w:right w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideH w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideV w:val="single" w:sz="4" w:color="auto"/>
                    </w:tblBorders>
                    <w:tblLayout w:type="fixed"/>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="720"/><w:gridCol w:w="2160"/><w:gridCol w:w="2160"/></w:tblGrid>
                  <w:tr>
                    {Cell($"{label} A0", 720, "FFE0E0")}
                    {Cell($"{label} A1", 2160, "E0FFE0")}
                    {Cell($"{label} A2", 2160, "E0E0FF")}
                  </w:tr>
                  {second}
                </w:tbl>
                """;
    }

    private static string TurnedTable(
        string label, string direction, string text, int? heightTwips = null, string? align = null,
        string? valign = null, bool pageBreak = false)
    {
        var opening = pageBreak
            ? $"<w:p><w:pPr><w:pageBreakBefore/>{ZeroSpacing}</w:pPr></w:p>"
            : string.Empty;

        var height = heightTwips is { } twips
            ? $"<w:trPr><w:trHeight w:val=\"{twips}\" w:hRule=\"exact\"/></w:trPr>"
            : string.Empty;

        // CT_TcPr puts textDirection after vAlign's neighbours and before vAlign itself.
        var cellProperties =
            $"<w:tcW w:w=\"{(direction == "lrTb" ? 288 : 1440)}\" w:type=\"dxa\"/>" +
            $"<w:textDirection w:val=\"{direction}\"/>" +
            (valign is null ? string.Empty : $"<w:vAlign w:val=\"{valign}\"/>");

        var paragraph = align is null ? ZeroSpacing : ZeroSpacing + $"<w:jc w:val=\"{align}\"/>";

        return $"""
                {opening}
                <w:tbl>
                  <w:tblPr>
                    <w:tblW w:w="{2880 + (direction == "lrTb" ? 288 : 1440)}" w:type="dxa"/>
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="4" w:color="auto"/>
                      <w:left w:val="single" w:sz="4" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="4" w:color="auto"/>
                      <w:right w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideH w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideV w:val="single" w:sz="4" w:color="auto"/>
                    </w:tblBorders>
                    <w:tblLayout w:type="fixed"/>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="2880"/><w:gridCol w:w="{(direction == "lrTb" ? 288 : 1440)}"/></w:tblGrid>
                  <w:tr>
                    {height}
                    <w:tc><w:tcPr><w:tcW w:w="2880" w:type="dxa"/></w:tcPr>
                      <w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>
                      <w:t xml:space="preserve">{DocxBuilder.Escape(label)}</w:t></w:r></w:p></w:tc>
                    <w:tc><w:tcPr>{cellProperties}</w:tcPr>
                      <w:p><w:pPr>{paragraph}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>
                      <w:t xml:space="preserve">{DocxBuilder.Escape(text)}</w:t></w:r></w:p></w:tc>
                  </w:tr>
                </w:tbl>
                """;
    }

    private static string PositionedTable(string label, int rows, string positioning, int borderSize = 4)
    {
        static string Cell(string text) =>
            $"<w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(text)}</w:t></w:r></w:p></w:tc>";

        var body = string.Concat(Enumerable.Range(1, rows).Select(i =>
            $"<w:tr>{Cell($"{label} {i}")}{Cell("cell")}</w:tr>"));

        return $"""
                <w:tbl>
                  <w:tblPr>
                    {positioning}
                    <w:tblW w:w="2880" w:type="dxa"/>
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="{borderSize}" w:color="auto"/>
                      <w:left w:val="single" w:sz="{borderSize}" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="{borderSize}" w:color="auto"/>
                      <w:right w:val="single" w:sz="{borderSize}" w:color="auto"/>
                      <w:insideH w:val="single" w:sz="{borderSize}" w:color="auto"/>
                      <w:insideV w:val="single" w:sz="{borderSize}" w:color="auto"/>
                    </w:tblBorders>
                    <w:tblLayout w:type="fixed"/>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="1800"/><w:gridCol w:w="1080"/></w:tblGrid>
                  {body}
                </w:tbl>
                """;
    }

    private static string HeadingTable(string label, int rows, params int[] headings)
    {
        static string Cell(string text) =>
            $"<w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(text)}</w:t></w:r></w:p></w:tc>";

        var body = string.Concat(Enumerable.Range(1, rows).Select(i =>
            $"<w:tr>{(headings.Contains(i) ? "<w:trPr><w:tblHeader/></w:trPr>" : string.Empty)}" +
            Cell($"{label} row {i}") + Cell(headings.Contains(i) ? "heading" : "body") + "</w:tr>"));

        return $"""
                <w:tbl>
                  <w:tblPr>
                    <w:tblW w:w="9360" w:type="dxa"/>
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="4" w:color="auto"/>
                      <w:left w:val="single" w:sz="4" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="4" w:color="auto"/>
                      <w:right w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideH w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideV w:val="single" w:sz="4" w:color="auto"/>
                    </w:tblBorders>
                    <w:tblLayout w:type="fixed"/>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="6480"/><w:gridCol w:w="2880"/></w:tblGrid>
                  {body}
                </w:tbl>
                """;
    }

    private static string SplittableTable(string label, int lines, bool cantSplit)
    {
        var content = string.Concat(Enumerable.Range(1, lines).Select(i =>
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t>{label} line {i}.</w:t></w:r></w:p>"));

        var properties = cantSplit ? "<w:trPr><w:cantSplit/></w:trPr>" : string.Empty;

        return $"""
                <w:tbl>
                  <w:tblPr>
                    <w:tblW w:w="9360" w:type="dxa"/>
                    <!-- Half a point, which is what the other bordered fixtures use: a heavier
                         border makes this one measure how far Word insets cell content, which is a
                         question of its own and not what a split row is here to show. -->
                    <w:tblBorders>
                      <w:top w:val="single" w:sz="4" w:color="auto"/>
                      <w:left w:val="single" w:sz="4" w:color="auto"/>
                      <w:bottom w:val="single" w:sz="4" w:color="auto"/>
                      <w:right w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideH w:val="single" w:sz="4" w:color="auto"/>
                      <w:insideV w:val="single" w:sz="4" w:color="auto"/>
                    </w:tblBorders>
                    <w:tblLayout w:type="fixed"/>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="6480"/><w:gridCol w:w="2880"/></w:tblGrid>
                  <w:tr>{properties}
                    <w:tc>{content}</w:tc>
                    <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>
                      <w:t>{label} second cell.</w:t></w:r></w:p></w:tc>
                  </w:tr>
                </w:tbl>
                """;
    }

    /// <summary>
    /// A paragraph of exactly the given number of lines, separated by explicit breaks rather than
    /// left to wrap — which is what makes where each line falls something a fixture can rely on.
    /// </summary>
    private static string BrokenParagraph(string label, int lines, string paragraphProperties)
    {
        var markup = $"<w:p><w:pPr>{paragraphProperties}</w:pPr>";

        for (var i = 1; i <= lines; i++)
        {
            if (i > 1) markup += $"<w:r><w:rPr>{Times12}</w:rPr><w:br/></w:r>";
            markup += $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{label} {i}.</w:t></w:r>";
        }

        return markup + "</w:p>";
    }

    private static readonly string Times24 = Times(48);

    /// <summary>Ten point, which is the size Word's footnote text style uses.</summary>
    private static readonly string Times10 = Times(20);

    private static readonly string Times36 = Times(72);

    /// <summary>
    /// Paragraph properties with every spacing term pinned to zero, so a measured baseline gap is
    /// purely line-box geometry.
    /// </summary>
    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>
    /// The same, preceded by a page break. pageBreakBefore comes before spacing in CT_PPr, so the
    /// two cannot simply be concatenated in the other order.
    /// </summary>
    private const string ZeroSpacingNewPage = "<w:pageBreakBefore/>" + ZeroSpacing;

    /// <summary>
    /// Styles shared by the two measurement probes.
    /// </summary>
    /// <remarks>
    /// Both probes must declare identical document defaults or they cannot be read against each
    /// other. A paragraph mark's font contributes to line height in Word, so probes with
    /// different defaults produce line boxes that differ for a reason unrelated to what is being
    /// measured — which invalidated an earlier attempt at this comparison.
    ///
    /// The three big styles are deliberately identical in every respect except their name: two
    /// names Word reserves for built-in styles, and one it cannot possibly recognise.
    /// </remarks>
    private const string ProbeStyles = """
                                       <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                                       <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                                         <w:docDefaults>
                                           <w:rPrDefault>
                                             <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr>
                                           </w:rPrDefault>
                                         </w:docDefaults>
                                         <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                                         <w:style w:type="paragraph" w:styleId="Heading1">
                                           <w:name w:val="heading 1"/>
                                           <w:basedOn w:val="Normal"/>
                                           <w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr>
                                           <w:rPr><w:b/><w:sz w:val="40"/></w:rPr>
                                         </w:style>
                                         <w:style w:type="paragraph" w:styleId="Heading2">
                                           <w:name w:val="heading 2"/>
                                           <w:basedOn w:val="Normal"/>
                                           <w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr>
                                           <w:rPr><w:b/><w:sz w:val="40"/></w:rPr>
                                         </w:style>
                                         <w:style w:type="paragraph" w:styleId="CustomBig">
                                           <w:name w:val="Custom Big"/>
                                           <w:basedOn w:val="Normal"/>
                                           <w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr>
                                           <w:rPr><w:b/><w:sz w:val="40"/></w:rPr>
                                         </w:style>
                                       </w:styles>
                                       """;

    /// <summary>
    /// Styles for the built-in-Normal probe pair: everything fixed except whether the Normal
    /// style states its own spacing.
    /// </summary>
    private static string BuiltInNormalProbeStyles(string? normalSpacing) => $"""
                                                                              <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                                                                              <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                                                                                <w:docDefaults>
                                                                                  <w:rPrDefault>
                                                                                    <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr>
                                                                                  </w:rPrDefault>
                                                                                </w:docDefaults>
                                                                                <w:style w:type="paragraph" w:default="1" w:styleId="Normal">
                                                                                  <w:name w:val="Normal"/>
                                                                                  {(normalSpacing is null ? string.Empty : $"<w:pPr>{normalSpacing}</w:pPr>")}
                                                                                </w:style>
                                                                              </w:styles>
                                                                              """;

    /// <summary>
    /// A borderless, margin-free table left in Word's default autofit mode, with a left-aligned
    /// content row and a right-aligned marker row so that both edges of every column can be read
    /// straight off the rendered page.
    /// </summary>
    /// <summary>
    /// A document of several pages with two notes on each, and — where asked — a section break
    /// part way down the second page, so that restarting per page and restarting per section give
    /// different answers everywhere after it.
    /// </summary>
    private static DocxBuilder WithNotesThroughout(DocxBuilder builder, bool sections, string? restart = null)
    {
        var note = 0;

        for (var i = 1; i <= 60; i++)
        {
            if (i % 10 == 3 || i % 10 == 7)
            {
                var id = builder.AddFootnote(DocxBuilder.FootnoteBody($"Note {++note} of the document.", Times10));

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph {i}, with a note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(id) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                continue;
            }

            // One section break, part way down a page rather than at the foot of one, so that
            // restarting per section and restarting per page part company after it.
            if (sections && i == 45)
            {
                builder.AddParagraphWithSectionBreak(
                    $"Body paragraph {i} of sixty, closing a section.",
                    DocxBuilder.Section(type: "nextPage", footnoteRestart: restart), ZeroSpacing, Times12);

                continue;
            }

            builder.AddParagraph($"Body paragraph {i} of sixty.", ZeroSpacing, Times12);
        }

        return builder;
    }

    private static string AutofitTable(int[]? grid, string[] cells)
    {
        var gridXml = grid is null
            ? string.Empty
            : "<w:tblGrid>" + string.Concat(grid.Select(w => $"<w:gridCol w:w=\"{w}\"/>")) + "</w:tblGrid>";

        var left = string.Concat(cells.Select(text =>
            $"<w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r></w:p></w:tc>"));

        var right = string.Concat(cells.Select((_, i) =>
            $"<w:tc><w:p><w:pPr>{ZeroSpacing}<w:jc w:val=\"right\"/></w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>e{i}</w:t></w:r></w:p></w:tc>"));

        return $"""
                <w:tbl>
                  <w:tblPr>
                    <w:tblCellMar>
                      <w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/>
                      <w:top w:w="0" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/>
                    </w:tblCellMar>
                  </w:tblPr>
                  {gridXml}
                  <w:tr>{left}</w:tr>
                  <w:tr>{right}</w:tr>
                </w:tbl>
                """;
    }

    /// <summary>
    /// A single-cell fixed-layout table on its own page, varying only the three things that could
    /// inset its content: the table indent, the cell margin, and the border.
    /// </summary>
    private static string InsetTable(string label, int? indentTwips, int marginTwips, bool borders)
    {
        // CT_TblPrBase is a sequence: tblW, tblInd, tblBorders, tblLayout, tblCellMar.
        var indent = indentTwips is null ? string.Empty : $"<w:tblInd w:w=\"{indentTwips}\" w:type=\"dxa\"/>";
        var border = borders
            ? "<w:tblBorders>" +
              "<w:top w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/>" +
              "<w:left w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/>" +
              "<w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/>" +
              "<w:right w:val=\"single\" w:sz=\"4\" w:color=\"auto\"/>" +
              "</w:tblBorders>"
            : string.Empty;

        return $"""
                <w:tbl>
                  <w:tblPr>
                    <w:tblW w:w="4680" w:type="dxa"/>
                    {indent}{border}<w:tblLayout w:type="fixed"/>
                    <w:tblCellMar>
                      <w:left w:w="{marginTwips}" w:type="dxa"/><w:right w:w="{marginTwips}" w:type="dxa"/>
                      <w:top w:w="0" w:type="dxa"/><w:bottom w:w="0" w:type="dxa"/>
                    </w:tblCellMar>
                  </w:tblPr>
                  <w:tblGrid><w:gridCol w:w="4680"/></w:tblGrid>
                  <w:tr><w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr>
                    <w:r><w:rPr>{Times12}</w:rPr><w:t>{label}</w:t></w:r>
                  </w:p></w:tc></w:tr>
                </w:tbl>
                """;
    }

    /// <summary>
    /// The two table styles every Word document that has ever held a table carries: the built-in
    /// <c>TableNormal</c> every other one is based on, and <c>TableGrid</c>, which is what the
    /// ruled table on the gallery's front row inserts. Copied from what Word writes.
    /// </summary>
    private const string GridTableStyles = """
                                           <w:style w:type="table" w:default="1" w:styleId="TableNormal">
                                             <w:name w:val="Normal Table"/>
                                             <w:tblPr>
                                               <w:tblInd w:w="0" w:type="dxa"/>
                                               <w:tblCellMar>
                                                 <w:top w:w="0" w:type="dxa"/>
                                                 <w:left w:w="108" w:type="dxa"/>
                                                 <w:bottom w:w="0" w:type="dxa"/>
                                                 <w:right w:w="108" w:type="dxa"/>
                                               </w:tblCellMar>
                                             </w:tblPr>
                                           </w:style>
                                           <w:style w:type="table" w:styleId="TableGrid">
                                             <w:name w:val="Table Grid"/>
                                             <w:basedOn w:val="TableNormal"/>
                                             <w:pPr>
                                               <w:spacing w:after="0" w:line="240" w:lineRule="auto"/>
                                             </w:pPr>
                                             <w:tblPr>
                                               <w:tblBorders>
                                                 <w:top w:val="single" w:sz="4" w:space="0" w:color="auto"/>
                                                 <w:left w:val="single" w:sz="4" w:space="0" w:color="auto"/>
                                                 <w:bottom w:val="single" w:sz="4" w:space="0" w:color="auto"/>
                                                 <w:right w:val="single" w:sz="4" w:space="0" w:color="auto"/>
                                                 <w:insideH w:val="single" w:sz="4" w:space="0" w:color="auto"/>
                                                 <w:insideV w:val="single" w:sz="4" w:space="0" w:color="auto"/>
                                               </w:tblBorders>
                                             </w:tblPr>
                                           </w:style>
                                           """;

    /// <summary>
    /// One conditional format of the probe style: a type, the size it sets, and the fill it puts
    /// behind the cells it reaches.
    /// </summary>
    private static string ProbeConditional(string type, int halfPoints, string fill) => $"""
         <w:tblStylePr w:type="{type}">
           <w:rPr>{Times(halfPoints)}</w:rPr>
           <w:tcPr><w:shd w:val="clear" w:color="auto" w:fill="{fill}"/></w:tcPr>
         </w:tblStylePr>
         """;

    /// <summary>
    /// A table style whose every conditional format sets a different type size and a different
    /// fill, so that Word's export says outright which one reached each cell.
    /// </summary>
    /// <remarks>
    /// Sizes rather than colours carry the answer, because a PDF names the size of every run it
    /// draws and reading one back needs no interpretation at all. The fills are there so the same
    /// document answers the same question about shading.
    ///
    /// The whole-table formatting is the style's own pPr and rPr rather than a wholeTable
    /// override, which is where Word puts it. Its pPr closing up the spacing is load-bearing: a
    /// cell paragraph in this fixture declares nothing of its own, so if a table style's paragraph
    /// formatting does not reach the cells, every row lands somewhere else.
    /// </remarks>
    private const string ProbeTableStyle = """
                                           <w:style w:type="table" w:styleId="ProbeTable">
                                             <w:name w:val="Probe Table"/>
                                             <w:basedOn w:val="TableNormal"/>
                                             <w:pPr>
                                               <w:spacing w:after="0" w:line="240" w:lineRule="auto"/>
                                             </w:pPr>
                                             <w:rPr>{whole}</w:rPr>
                                             <w:tblPr/>
                                           {conditionals}
                                           </w:style>
                                           """;

    private static string ProbeStyleXml() => ProbeTableStyle
        .Replace("{whole}", Times(20))
        .Replace("{conditionals}", string.Concat(
            // Bands first, then the edges, then the corners. The order they are written in has no
            // meaning — which one wins is the question this fixture exists to answer.
            ProbeConditional("band1Vert", 26, "E6E6FF"),
            ProbeConditional("band2Vert", 28, "CCCCFF"),
            ProbeConditional("band1Horz", 22, "E6FFE6"),
            ProbeConditional("band2Horz", 24, "CCFFCC"),
            ProbeConditional("firstCol", 34, "FFE6E6"),
            ProbeConditional("lastCol", 36, "FFCCCC"),
            ProbeConditional("firstRow", 30, "FFFFCC"),
            ProbeConditional("lastRow", 32, "FFE6CC"),
            ProbeConditional("nwCell", 38, "D9D9D9"),
            ProbeConditional("neCell", 40, "BFBFBF"),
            ProbeConditional("swCell", 42, "A6A6A6"),
            ProbeConditional("seCell", 44, "8C8C8C")));

    /// <summary>
    /// A table under the probe style, whose cells name themselves so that the size each one comes
    /// out at can be read against the position it holds.
    /// </summary>
    /// <param name="look">
    /// The <c>w:tblLook</c>, which is what says whether the first row, the last row, the two edge
    /// columns and the banding are in force at all.
    /// </param>
    /// <param name="rows">How many rows the table has.</param>
    /// <param name="columns">How many columns it is divided into, evenly across the measure.</param>
    /// <param name="bandSize">How many rows and columns make up one band.</param>
    /// <param name="styleId">The table style the table wears.</param>
    private static string ProbeTable(
        string look, int rows, int columns, int bandSize = 1, string styleId = "ProbeTable")
    {
        var width = 9360 / columns;

        var grid = "<w:tblGrid>" +
                   string.Concat(Enumerable.Repeat($"<w:gridCol w:w=\"{width}\"/>", columns)) +
                   "</w:tblGrid>";

        var body = string.Concat(Enumerable.Range(1, rows).Select(row =>
            "<w:tr>" + string.Concat(Enumerable.Range(1, columns).Select(column =>
                $"<w:tc><w:p><w:r><w:t>R{row}C{column}</w:t></w:r></w:p></w:tc>")) + "</w:tr>"));

        return $"""
                <w:tbl>
                  <w:tblPr>
                    <w:tblStyle w:val="{styleId}"/>
                    <w:tblStyleRowBandSize w:val="{bandSize}"/>
                    <w:tblStyleColBandSize w:val="{bandSize}"/>
                    <w:tblW w:w="9360" w:type="dxa"/>
                    <w:tblLayout w:type="fixed"/>
                    {look}
                  </w:tblPr>
                  {grid}
                  {body}
                </w:tbl>
                """;
    }

    /// <summary>Every conditional format in force.</summary>
    private const string LookEverything =
        "<w:tblLook w:val=\"01E0\" w:firstRow=\"1\" w:lastRow=\"1\" w:firstColumn=\"1\" " +
        "w:lastColumn=\"1\" w:noHBand=\"0\" w:noVBand=\"0\"/>";

    /// <summary>None of them, which is what leaves the whole-table formatting alone on the page.</summary>
    private const string LookNothing =
        "<w:tblLook w:val=\"0000\" w:firstRow=\"0\" w:lastRow=\"0\" w:firstColumn=\"0\" " +
        "w:lastColumn=\"0\" w:noHBand=\"1\" w:noVBand=\"1\"/>";

    /// <summary>
    /// Everything but the banding down the columns, which is the only way to see the banding
    /// across the rows: where both are in force the columns win, and the rows leave no mark.
    /// </summary>
    private const string LookNoVerticalBands =
        "<w:tblLook w:val=\"05E0\" w:firstRow=\"1\" w:lastRow=\"1\" w:firstColumn=\"1\" " +
        "w:lastColumn=\"1\" w:noHBand=\"0\" w:noVBand=\"1\"/>";

    /// <summary>
    /// A style with no corner formats and no banding down the columns, which is what most of
    /// Word's own gallery looks like. Its first row and its first column therefore meet in a cell
    /// nothing else covers, and which of the two wins there is a question only Word can answer.
    /// </summary>
    private static string EdgeStyleXml() => $"""
                                             <w:style w:type="table" w:styleId="EdgeTable">
                                               <w:name w:val="Edge Table"/>
                                               <w:basedOn w:val="TableNormal"/>
                                               <w:pPr>
                                                 <w:spacing w:after="0" w:line="240" w:lineRule="auto"/>
                                               </w:pPr>
                                               <w:rPr>{Times(20)}</w:rPr>
                                               <w:tblPr/>
                                             {ProbeConditional("band1Horz", 22, "E6FFE6")}
                                             {ProbeConditional("band2Horz", 24, "CCFFCC")}
                                             {ProbeConditional("firstCol", 34, "FFE6E6")}
                                             {ProbeConditional("firstRow", 30, "FFFFCC")}
                                             </w:style>
                                             """;

    /// <summary>
    /// One row of three cells asking where a table style sits in the cascade: against nothing,
    /// against a paragraph style, and against formatting on the run itself.
    /// </summary>
    private static string CascadeTable() => $"""
                                             <w:tbl>
                                               <w:tblPr>
                                                 <w:tblStyle w:val="ProbeTable"/>
                                                 <w:tblW w:w="9360" w:type="dxa"/>
                                                 <w:tblLayout w:type="fixed"/>
                                                 {LookNothing}
                                               </w:tblPr>
                                               <w:tblGrid><w:gridCol w:w="3120"/><w:gridCol w:w="3120"/><w:gridCol w:w="3120"/></w:tblGrid>
                                               <w:tr>
                                                 <w:tc><w:p><w:r><w:t>Alone</w:t></w:r></w:p></w:tc>
                                                 <w:tc><w:p><w:pPr><w:pStyle w:val="CellStyle"/></w:pPr><w:r><w:t>Styled</w:t></w:r></w:p></w:tc>
                                                 <w:tc><w:p><w:r><w:rPr>{Times(46)}</w:rPr><w:t>Direct</w:t></w:r></w:p></w:tc>
                                               </w:tr>
                                             </w:tbl>
                                             """;

    /// <summary>A paragraph style for a cell to wear, one size larger than the table style's.</summary>
    private const string CellParagraphStyle = """
                                              <w:style w:type="paragraph" w:styleId="CellStyle">
                                                <w:name w:val="Cell Style"/>
                                                <w:pPr><w:spacing w:after="0" w:line="240" w:lineRule="auto"/></w:pPr>
                                                <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="42"/></w:rPr>
                                              </w:style>
                                              """;

    /// <summary>A paragraph for the inside of a shape, spaced like the ones outside it.</summary>
    private static string ShapeText(string text) =>
        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>";

    /// <summary>
    /// One page of the inset probe: a text box on its own, holding one short line whose position
    /// is the whole measurement.
    /// </summary>
    /// <remarks>
    /// The box is wider and taller than the line needs, so that where the line sits inside it says
    /// what the insets are, whether the outline is added to them, and what the anchor did.
    /// </remarks>
    private static string InsetShapePage(
        string label, (double Left, double Top, double Right, double Bottom)? insets,
        double lineWidth, string anchor, bool first = false) =>
        $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacingNewPage)}</w:pPr>" +
        DocxBuilder.InlineShape(216, 72, content: ShapeText(label),
            fillHex: "FFFFFF", lineHex: "000000", lineWidthPoints: lineWidth,
            insets: insets, anchor: anchor, id: 300 + label[0]) +
        "</w:p>";

    /// <summary>
    /// One page of the stroke probe: a rectangle in the line, alone, whose stroke is the only
    /// thing that varies.
    /// </summary>
    /// <summary>A number as markup wants it: no trailing zeros, and the point as a point.</summary>
    private static string Number(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string StrokeProbePage(
        string? strokeWeight, bool first = false, string element = "rect") =>
        $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacingNewPage)}</w:pPr>" +
        DocxBuilder.VmlShape("width:108pt;height:54pt", element: element,
            fillColor: "#c0d8f0", strokeColor: strokeWeight is null ? null : "#000000",
            strokeWeight: strokeWeight, id: 1050) +
        "</w:p>" +
        // A line naming the page, so that the reading of the reference has something to read and
        // so that a page of this fixture can be told from the next by eye.
        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:t xml:space=\"preserve\">{element} {strokeWeight ?? "unstroked"}</w:t></w:r></w:p>";

    /// <summary>
    /// A picture of flat bands, from black through to white and then one of colour, so that what
    /// washing a picture out does to each shade can be read straight off the page.
    /// </summary>
    private static byte[] BandedImage()
    {
        (byte Red, byte Green, byte Blue)[] bands =
            [(0, 0, 0), (64, 64, 64), (128, 128, 128), (192, 192, 192), (255, 255, 255), (220, 40, 40)];

        const int band = 16;
        const int height = 32;

        var pixels = new byte[bands.Length * band * height * 3];

        for (var y = 0; y < height; y++)
        for (var x = 0; x < bands.Length * band; x++)
        {
            var (red, green, blue) = bands[x / band];
            var at = (y * bands.Length * band + x) * 3;

            pixels[at] = red;
            pixels[at + 1] = green;
            pixels[at + 2] = blue;
        }

        return PngWriter.Write(bands.Length * band, height, pixels, hasAlpha: false);
    }

    /// <summary>
    /// Two series in one chart, overlapping each other, which is the other half of how wide a bar
    /// is: the gap says how much room the bars of a category share, and the overlap how far they
    /// stand over one another inside it.
    /// </summary>
    private static string TwoSeriesChart() => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout><c:manualLayout>
              <c:layoutTarget val="inner"/>
              <c:xMode val="edge"/><c:yMode val="edge"/>
              <c:x val="0.25"/><c:y val="0.1"/><c:w val="0.65"/><c:h val="0.65"/>
            </c:manualLayout></c:layout>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, "Units", ["One", "Two"], [40, 80], "4472C4")}
              {DocxBuilder.ChartSeries(1, "Others", ["One", "Two"], [60, 30], "ED7D31")}
              <c:gapWidth val="100"/>
              <c:overlap val="-27"/>
              <c:axId val="111111111"/><c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/><c:axPos val="b"/>
              <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
              <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/><c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
              <c:crossBetween val="between"/><c:majorUnit val="50"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
        </c:chart>
        """;

    /// <summary>
    /// A column chart that says nothing about where its plotting goes, so that Word has to work it
    /// out — which is what the automatic layout probe measures.
    /// </summary>
    /// <param name="maximum">
    /// What the value axis runs to, which is what makes its labels wide or narrow without changing
    /// anything else.
    /// </param>
    private static string AutoLayoutChart(
        double maximum, int labelSize = 1000, string? category = null,
        string tickLabels = "nextTo")
    {
        var text = $"""
            <c:txPr><a:bodyPr/><a:lstStyle/>
              <a:p><a:pPr><a:defRPr sz="{labelSize}"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p>
            </c:txPr>
            """;

        var categories = category is null
            ? new[] { "One", "Two" }
            : [category, category];

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout/>
                <c:barChart>
                  <c:barDir val="col"/>
                  <c:grouping val="clustered"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", categories,
                      [maximum * 0.4, maximum * 0.8], "4472C4")}
                  <c:gapWidth val="150"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:barChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="{tickLabels}"/>
                  {text}
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                  <c:noMultiLvlLbl val="0"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/>
                    <c:max val="{maximum.ToString(CultureInfo.InvariantCulture)}"/>
                    <c:min val="0"/>
                  </c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="{tickLabels}"/>
                  {text}
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>
                  <c:majorUnit val="{(maximum / 2).ToString(CultureInfo.InvariantCulture)}"/>
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A column chart that says nothing about what its value axis runs between, so that Word has
    /// to choose — which is what the scaling probe measures. Its plotting is placed by hand, so
    /// that the labels can be read off without the placing moving with them.
    /// </summary>
    /// <summary>
    /// The same chart lying down: the value axis runs along the foot, and how long it is can be
    /// varied, since that is the thing an upright chart cannot ask about.
    /// </summary>
    private static string AutoScaleBarChart(
        IReadOnlyList<double> values, double x, double width, string direction = "bar",
        double labelSize = 10, double y = 0.1, double height = 0.7)
    {
        var categories = Enumerable.Range(1, values.Count).Select(i => $"C{i}").ToList();

        var (categoryPosition, valuePosition) = direction == "bar" ? ("l", "b") : ("b", "l");

        var text = $"""
            <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr>
              <a:defRPr sz="{(int)Math.Round(labelSize * 100)}"/>
            </a:pPr><a:endParaRPr lang="en-US"/></a:p></c:txPr>
            """;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout><c:manualLayout>
                  <c:layoutTarget val="inner"/>
                  <c:xMode val="edge"/><c:yMode val="edge"/>
                  <c:x val="{x.ToString(CultureInfo.InvariantCulture)}"/>
                  <c:y val="{y.ToString(CultureInfo.InvariantCulture)}"/>
                  <c:w val="{width.ToString(CultureInfo.InvariantCulture)}"/>
                  <c:h val="{height.ToString(CultureInfo.InvariantCulture)}"/>
                </c:manualLayout></c:layout>
                <c:barChart>
                  <c:barDir val="{direction}"/>
                  <c:grouping val="clustered"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", categories, values, "4472C4")}
                  <c:gapWidth val="150"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:barChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="{categoryPosition}"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>{text}
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="{valuePosition}"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>{text}
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// An area chart: the same shape as a line chart, filled down to the axis.
    /// </summary>
    private static string AreaChart(
        string grouping, int series, bool manualLayout = true, double? maximum = 60,
        string numberFormat = "General", double[]? values = null, string? firstCategory = null)
    {
        var categories = new[] { firstCategory ?? "One", "Two", "Three", "Four" };

        values ??= [30, 45, 20, 55];

        var layout = manualLayout
            ? """
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.25"/><c:y val="0.1"/><c:w val="0.65"/><c:h val="0.7"/>
              </c:manualLayout>
              """
            : string.Empty;

        var scale = maximum is { } top
            ? $"""<c:max val="{top.ToString(CultureInfo.InvariantCulture)}"/><c:min val="0"/>"""
            : string.Empty;

        var unit = maximum is { } value
            ? $"""<c:majorUnit val="{(value / 3).ToString(CultureInfo.InvariantCulture)}"/>"""
            : string.Empty;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{layout}</c:layout>
                <c:areaChart>
                  <c:grouping val="{grouping}"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", categories, values, "4472C4")}
                  {(series > 1
                      ? DocxBuilder.ChartSeries(1, "Others", categories, [10, 25, 50, 15], "ED7D31")
                      : string.Empty)}
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:areaChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/>{scale}</c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="{numberFormat}" sourceLinked="0"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="midCat"/>{unit}
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A scatter chart: pairs of numbers rather than a value against a category, so both of its
    /// axes are value axes and both have to be scaled.
    /// </summary>
    private static string ScatterChart(
        string style, string? marker = "circle", bool line = true, bool smooth = false,
        bool stated = true, bool manualLayout = true, int series = 1, double markerSize = 7)
    {
        var layout = manualLayout
            ? """
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.25"/><c:y val="0.1"/><c:w val="0.65"/><c:h val="0.7"/>
              </c:manualLayout>
              """
            : string.Empty;

        static string Scaling(bool stated, double maximum, double unit) => stated
            ? $"""
               <c:scaling><c:orientation val="minMax"/>
                 <c:max val="{maximum.ToString(CultureInfo.InvariantCulture)}"/><c:min val="0"/>
               </c:scaling>
               """
            : "<c:scaling><c:orientation val=\"minMax\"/></c:scaling>";

        static string Unit(bool stated, double unit) => stated
            ? $"""<c:majorUnit val="{unit.ToString(CultureInfo.InvariantCulture)}"/>"""
            : string.Empty;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{layout}</c:layout>
                <c:scatterChart>
                  <c:scatterStyle val="{style}"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartScatterSeries(0, "Units", [1, 2, 4, 7], [30, 45, 20, 55],
                      "4472C4", marker, markerSize, line: line, smooth: smooth)}
                  {(series > 1
                      ? DocxBuilder.ChartScatterSeries(1, "Others", [1, 3, 5, 7], [10, 25, 50, 15],
                          "ED7D31", marker is null ? null : "square", markerSize,
                          line: line, smooth: smooth)
                      : string.Empty)}
                  {(series > 2
                      ? DocxBuilder.ChartScatterSeries(2, "Third", [1, 3, 5, 7], [5, 35, 15, 40],
                          "A5A5A5", marker is null ? null : "triangle", markerSize,
                          line: line, smooth: smooth)
                      : string.Empty)}
                  {(series > 3
                      ? DocxBuilder.ChartScatterSeries(3, "Fourth", [1, 3, 5, 7], [50, 5, 40, 25],
                          "FFC000", marker is null ? null : "x", markerSize,
                          line: line, smooth: smooth)
                      : string.Empty)}
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:scatterChart>
                <c:valAx>
                  <c:axId val="111111111"/>
                  {Scaling(stated, 8, 2)}
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="midCat"/>{Unit(stated, 2)}
                </c:valAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  {Scaling(stated, 60, 20)}
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="midCat"/>{Unit(stated, 20)}
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A chart carrying the three things that go round the plotting rather than in it: a title
    /// over the top, a legend to one side, and a number written at each point.
    /// </summary>
    private static string DressedChart(
        string kind = "col", int series = 2, string? title = null, int titleSize = 0,
        bool axisTitles = false, string? legend = null, bool labels = false,
        string labelPosition = "", string labelFormat = "General", bool percent = false,
        string categoryTitle = "Quarter", int textSize = 0)
    {
        var categories = new[] { "One", "Two", "Three", "Four" };

        // The face is named along with the size, since a title that states one without the other
        // loses the theme's own and is drawn in something else entirely.
        static string Rich(string text, int size) => $"""
            <c:tx><c:rich>
              <a:bodyPr/><a:lstStyle/>
              <a:p><a:pPr>
                <a:defRPr sz="{(size > 0 ? size : 1800)}" b="0">
                  <a:latin typeface="+mj-lt"/>
                </a:defRPr>
              </a:pPr>
                <a:r><a:t>{DocxBuilder.Escape(text)}</a:t></a:r>
              </a:p>
            </c:rich></c:tx>
            """;

        var text = textSize > 0
            ? $"""
               <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr>
                 <a:defRPr sz="{textSize}"/>
               </a:pPr><a:endParaRPr lang="en-US"/></a:p></c:txPr>
               """
            : string.Empty;

        static string Title(string? text, int size) => text is null
            ? string.Empty
            : $"<c:title>{Rich(text, size)}<c:overlay val=\"0\"/></c:title>";

        var dLbls = labels
            ? $"""
               <c:dLbls>
                 <c:numFmt formatCode="{labelFormat}" sourceLinked="0"/>
                 {text}
                 {(labelPosition.Length > 0 ? $"<c:dLblPos val=\"{labelPosition}\"/>" : string.Empty)}
                 <c:showLegendKey val="0"/>
                 <c:showVal val="{(percent ? 0 : 1)}"/>
                 <c:showCatName val="0"/>
                 <c:showSerName val="0"/>
                 <c:showPercent val="{(percent ? 1 : 0)}"/>
                 <c:showBubbleSize val="0"/>
               </c:dLbls>
               """
            : string.Empty;

        var body = kind switch
        {
            "line" => $"""
                <c:lineChart>
                  <c:grouping val="standard"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartLineSeries(0, "Units", categories, [30, 45, 20, 55], "4472C4")}
                  {(series > 1
                      ? DocxBuilder.ChartLineSeries(1, "Others", categories, [10, 25, 50, 15],
                          "ED7D31")
                      : string.Empty)}
                  {dLbls}
                  <c:marker val="0"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:lineChart>
                """,

            "pie" => $"""
                <c:pieChart>
                  <c:varyColors val="1"/>
                  {DocxBuilder.ChartPieSeries("Units", categories, [30, 45, 20, 55],
                      ["4472C4", "ED7D31", "A5A5A5", "FFC000"])}
                  {dLbls}
                  <c:firstSliceAng val="0"/>
                </c:pieChart>
                """,

            _ => $"""
                <c:barChart>
                  <c:barDir val="col"/>
                  <c:grouping val="clustered"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", categories, [30, 45, 20, 55], "4472C4")}
                  {(series > 1
                      ? DocxBuilder.ChartSeries(1, "Others", categories, [10, 25, 50, 15], "ED7D31")
                      : string.Empty)}
                  {(series > 2
                      ? DocxBuilder.ChartSeries(2, "Third", categories, [5, 35, 15, 40], "A5A5A5")
                      : string.Empty)}
                  {(series > 3
                      ? DocxBuilder.ChartSeries(3, "Fourth and last", categories, [50, 5, 40, 25],
                          "FFC000")
                      : string.Empty)}
                  {dLbls}
                  <c:gapWidth val="150"/>
                  <c:overlap val="-27"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:barChart>
                """
        };

        var axes = kind == "pie"
            ? string.Empty
            : $"""
               <c:catAx>
                 <c:axId val="111111111"/>
                 <c:scaling><c:orientation val="minMax"/></c:scaling>
                 <c:delete val="0"/><c:axPos val="b"/>
                 {(axisTitles ? Title(categoryTitle, 1000) : string.Empty)}
                 <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                 <c:tickLblPos val="nextTo"/>
                 <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                 <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
               </c:catAx>
               <c:valAx>
                 <c:axId val="222222222"/>
                 <c:scaling><c:orientation val="minMax"/></c:scaling>
                 <c:delete val="0"/><c:axPos val="l"/>
                 <c:majorGridlines/>
                 {(axisTitles ? Title("Units sold", 1000) : string.Empty)}
                 <c:numFmt formatCode="General" sourceLinked="1"/>
                 <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                 <c:tickLblPos val="nextTo"/>
                 <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                 <c:crossBetween val="between"/>
               </c:valAx>
               """;

        return $"""
            <c:chart>
              {Title(title, titleSize)}
              <c:autoTitleDeleted val="{(title is null ? 1 : 0)}"/>
              <c:plotArea>
                <c:layout/>
                {body}
                {axes}
              </c:plotArea>
              {(legend is null
                  ? string.Empty
                  : $"<c:legend><c:legendPos val=\"{legend}\"/><c:overlay val=\"0\"/>{text}</c:legend>")}
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    private static string AutoScaleChart(IReadOnlyList<double> values)
    {
        var categories = Enumerable.Range(1, values.Count).Select(i => $"C{i}").ToList();

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout><c:manualLayout>
                  <c:layoutTarget val="inner"/>
                  <c:xMode val="edge"/><c:yMode val="edge"/>
                  <c:x val="0.3"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.7"/>
                </c:manualLayout></c:layout>
                <c:barChart>
                  <c:barDir val="col"/>
                  <c:grouping val="clustered"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", categories, values, "4472C4")}
                  <c:gapWidth val="150"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:barChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>The data each page of the scaling probe holds, and nothing else varies.</summary>
    /// <summary>
    /// A lying-down chart's numbers, and how long the axis they run along is: the same data over
    /// three lengths, and the same length over several sets of data.
    /// </summary>
    private static readonly
        (double[] Values, double X, double Width, string Direction, double LabelSize,
        double Y, double Height)[]
        BarScaleProbeData =
    [
        ([-45, 30], 0.3, 0.6, "bar", 10, 0.1, 0.7),
        ([-45, 30], 0.3, 0.4375, "bar", 10, 0.1, 0.7),
        ([-45, 30], 0.1, 0.85, "bar", 10, 0.1, 0.7),
        ([-20, 60], 0.3, 0.4375, "bar", 10, 0.1, 0.7),
        ([-20, 60], 0.3, 0.6, "bar", 10, 0.1, 0.7),
        ([47], 0.3, 0.6, "bar", 10, 0.1, 0.7),
        ([47], 0.3, 0.4375, "bar", 10, 0.1, 0.7),
        ([9.5], 0.3, 0.6, "bar", 10, 0.1, 0.7),
        ([1000], 0.3, 0.6, "bar", 10, 0.1, 0.7),
        ([0.4], 0.3, 0.6, "bar", 10, 0.1, 0.7),

        // Whether the room an axis needs for a number is the number's own width or just its
        // height: these are the pages where the two answers part. The one set in twenty point is
        // given a shorter plot than the rest, so that its labels have room to fall inside the
        // frame — Word nudges a hand-placed plot that has not, and this page is not about that.
        ([1000000], 0.3, 0.6, "bar", 10, 0.1, 0.7),
        ([47], 0.3, 0.6, "bar", 20, 0.05, 0.5),
        ([9.5], 0.3, 0.6, "col", 20, 0.1, 0.7),
        ([47], 0.3, 0.6, "col", 20, 0.1, 0.7)
    ];

    private static readonly double[][] ScaleProbeData =
    [
        [1], [3, 7], [9.5], [10], [12], [47], [100], [105], [1000], [0.4], [-20, 60], [55, 30]
    ];

    /// <summary>A line chart, with its plotting placed by hand and its axis told what to do.</summary>
    private static string LineChart(
        int series, string marker = "none", int markerSize = 0, string? legend = null,
        int legendSize = 0) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout><c:manualLayout>
              <c:layoutTarget val="inner"/>
              <c:xMode val="edge"/><c:yMode val="edge"/>
              <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.7"/><c:h val="0.7"/>
            </c:manualLayout></c:layout>
            <c:lineChart>
              <c:grouping val="standard"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartLineSeries(0, "Units", ["One", "Two", "Three", "Four"],
                  [30, 45, 20, 55], "4472C4", marker: marker, markerSize: markerSize)}
              {(series > 1
                  ? DocxBuilder.ChartLineSeries(1, "Others", ["One", "Two", "Three", "Four"],
                      [10, 25, 50, 15], "ED7D31", marker: marker, markerSize: markerSize)
                  : string.Empty)}
              <c:marker val="0"/>
              <c:axId val="111111111"/><c:axId val="222222222"/>
            </c:lineChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/><c:axPos val="b"/>
              <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
              <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/><c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
              <c:crossBetween val="between"/><c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          {(legend is null
              ? string.Empty
              : $"""
                 <c:legend><c:legendPos val="{legend}"/><c:overlay val="0"/>
                   {(legendSize > 0
                       ? $"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"{legendSize * 100}\"/></a:pPr><a:endParaRPr lang=\"en-GB\"/></a:p></c:txPr>"
                       : string.Empty)}
                 </c:legend>
                 """)}
          <c:plotVisOnly val="1"/>
        </c:chart>
        """;

    /// <summary>A pie chart, whose plotting is placed by hand on one page and left to Word on another.</summary>
    private static string PieChart(bool manualLayout) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>{(manualLayout
                ? """
                  <c:manualLayout>
                    <c:layoutTarget val="inner"/>
                    <c:xMode val="edge"/><c:yMode val="edge"/>
                    <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.8"/>
                  </c:manualLayout>
                  """
                : string.Empty)}</c:layout>
            <c:pieChart>
              <c:varyColors val="1"/>
              {DocxBuilder.ChartPieSeries("Units", ["One", "Two", "Three", "Four"],
                  [30, 45, 20, 55], ["4472C4", "ED7D31", "A5A5A5", "FFC000"])}
              <c:firstSliceAng val="0"/>
            </c:pieChart>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
        </c:chart>
        """;

    /// <summary>
    /// A doughnut chart: a pie with a hole through the middle, and one ring for every series it
    /// holds rather than one pie.
    /// </summary>
    private static string DoughnutChart(
        int hole = 50, int series = 1, int firstSliceAngle = 0, bool manualLayout = true,
        string? legend = null, bool labels = false, int labelSize = 0, bool pie = false)
    {
        string[] categories = ["One", "Two", "Three", "Four"];

        var ring = labels
            ? $"""
              <c:dLbls>
                {(labelSize > 0
                    ? $"<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"{labelSize * 100}\"/></a:pPr><a:endParaRPr lang=\"en-GB\"/></a:p></c:txPr>"
                    : string.Empty)}
                <c:showLegendKey val="0"/><c:showVal val="0"/><c:showCatName val="0"/>
                <c:showSerName val="0"/><c:showPercent val="1"/><c:showBubbleSize val="0"/>
              </c:dLbls>
              """
            : string.Empty;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{(manualLayout
                    ? """
                      <c:manualLayout>
                        <c:layoutTarget val="inner"/>
                        <c:xMode val="edge"/><c:yMode val="edge"/>
                        <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.8"/>
                      </c:manualLayout>
                      """
                    : string.Empty)}</c:layout>
                <c:{(pie ? "pieChart" : "doughnutChart")}>
                  <c:varyColors val="1"/>
                  {DocxBuilder.ChartPieSeries("Units", categories, [30, 45, 20, 55],
                      ["4472C4", "ED7D31", "A5A5A5", "FFC000"])}
                  {(series > 1
                      ? DocxBuilder.ChartPieSeries("Others", categories, [10, 25, 50, 15],
                          ["5B9BD5", "70AD47", "264478", "9E480E"], index: 1)
                      : string.Empty)}
                  {ring}
                  <c:firstSliceAng val="{firstSliceAngle}"/>
                  {(pie ? string.Empty : $"<c:holeSize val=\"{hole}\"/>")}
                </c:{(pie ? "pieChart" : "doughnutChart")}>
              </c:plotArea>
              {(legend is null
                  ? string.Empty
                  : $"<c:legend><c:legendPos val=\"{legend}\"/><c:overlay val=\"0\"/></c:legend>")}
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A bubble chart: a scatter whose points carry a third number, drawn as how large the bubble
    /// at each pair is.
    /// </summary>
    private static string BubbleChart(
        int scale = 100, string sizeRepresents = "area", int series = 1,
        bool manualLayout = true, bool stated = true, double[]? sizes = null,
        double[]? x = null, double[]? y = null, bool smallPlot = false)
    {
        sizes ??= [10, 20, 30, 40];
        x ??= [1, 2, 4, 7];
        y ??= [30, 45, 20, 55];

        var layout = manualLayout
            ? $"""
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.25"/><c:y val="0.1"/>
                <c:w val="{(smallPlot ? "0.3" : "0.65")}"/><c:h val="{(smallPlot ? "0.3" : "0.7")}"/>
              </c:manualLayout>
              """
            : string.Empty;

        static string Scaling(bool stated, double maximum) => stated
            ? $"""
               <c:scaling><c:orientation val="minMax"/>
                 <c:max val="{maximum.ToString(CultureInfo.InvariantCulture)}"/><c:min val="0"/>
               </c:scaling>
               """
            : "<c:scaling><c:orientation val=\"minMax\"/></c:scaling>";

        static string Unit(bool stated, double unit) => stated
            ? $"""<c:majorUnit val="{unit.ToString(CultureInfo.InvariantCulture)}"/>"""
            : string.Empty;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{layout}</c:layout>
                <c:bubbleChart>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartBubbleSeries(0, "Units", x, y, sizes, "4472C4")}
                  {(series > 1
                      ? DocxBuilder.ChartBubbleSeries(1, "Others", [1, 3, 5, 7], [10, 25, 50, 15],
                          [40, 30, 20, 10], "ED7D31")
                      : string.Empty)}
                  <c:bubbleScale val="{scale}"/>
                  <c:showNegBubbles val="0"/>
                  <c:sizeRepresents val="{sizeRepresents}"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:bubbleChart>
                <c:valAx>
                  <c:axId val="111111111"/>
                  {Scaling(stated, 8)}
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="midCat"/>{Unit(stated, 2)}
                </c:valAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  {Scaling(stated, 60)}
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="midCat"/>{Unit(stated, 20)}
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A radar chart: the categories set round a circle rather than along a line, and the values
    /// measured out from its middle.
    /// </summary>
    private static string RadarChart(
        string style = "standard", int series = 1, bool manualLayout = true, bool stated = true,
        string[]? categories = null, double[]? values = null, string? legend = null,
        int labelSize = 0, int markerSize = 0)
    {
        categories ??= ["One", "Two", "Three", "Four", "Five"];
        values ??= [30, 45, 20, 55, 35];

        // What the labels round the web are set in, where the page is asking about the size.
        var text = labelSize > 0
            ? $"""<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="{labelSize * 100}"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>"""
            : string.Empty;

        var layout = manualLayout
            ? """
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.8"/>
              </c:manualLayout>
              """
            : string.Empty;

        // A filled radar is a shape rather than a line, so its series carries a fill; the other
        // two carry a line and whatever marker the style asks for.
        string Series(int index, string name, IReadOnlyList<double> numbers, string colour) =>
            style == "filled"
                ? DocxBuilder.ChartSeries(index, name, categories, numbers, colour)
                : DocxBuilder.ChartLineSeries(index, name, categories, numbers, colour,
                    marker: style == "marker" ? "circle" : "none", markerSize: markerSize);

        var scale = stated
            ? """<c:max val="60"/><c:min val="0"/>"""
            : string.Empty;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{layout}</c:layout>
                <c:radarChart>
                  <c:radarStyle val="{style}"/>
                  <c:varyColors val="0"/>
                  {Series(0, "Units", values, "4472C4")}
                  {(series > 1 ? Series(1, "Others", [10, 25, 50, 15, 40], "ED7D31") : string.Empty)}
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:radarChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>{text}
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/>{scale}</c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>{text}
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>{(stated ? "<c:majorUnit val=\"20\"/>" : string.Empty)}
                </c:valAx>
              </c:plotArea>
              {(legend is null
                  ? string.Empty
                  : $"<c:legend><c:legendPos val=\"{legend}\"/><c:overlay val=\"0\"/></c:legend>")}
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A stock chart: three or four series read together as one, drawn as the lines between them
    /// rather than as lines along them.
    /// </summary>
    /// <param name="series">
    /// Three for high, low and close; four for open, high, low and close, which is the one that
    /// draws a bar between the opening and the closing as well.
    /// </param>
    /// <summary>
    /// A stock chart whose up and down bars are painted in colours the fallback would not choose.
    /// </summary>
    /// <remarks>
    /// The point of the colours. <see cref="StockChart"/> states white for a rising bar and black
    /// for a falling one, which are exactly what the composer fills in when the document says
    /// nothing — so a reader that fails to read the colours draws the same picture, and no fixture
    /// using it can tell the two apart. That is how the wrong-namespace lookup behind #80 survived
    /// the suite.
    ///
    /// Green and red are chosen because nothing else on the page is either, which turns "were the
    /// stated colours read" into a count of pixels that belong to nothing else.
    /// </remarks>
    private static string UpDownBarProbeChart(string up, string down) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.7"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:stockChart>
              {DocxBuilder.ChartStockSeries(0, "Open", ["One", "Two", "Three", "Four"], [28, 40, 22, 50])}
              {DocxBuilder.ChartStockSeries(1, "High", ["One", "Two", "Three", "Four"], [40, 52, 33, 58])}
              {DocxBuilder.ChartStockSeries(2, "Low", ["One", "Two", "Three", "Four"], [20, 30, 15, 35])}
              {DocxBuilder.ChartStockSeries(3, "Close", ["One", "Two", "Three", "Four"], [35, 33, 28, 45], marker: "none")}
              <c:hiLowLines/>
              <c:upDownBars>
                <c:gapWidth val="150"/>
                <c:upBars><c:spPr><a:solidFill><a:srgbClr val="{up}"/></a:solidFill>
                  <a:ln w="9525"><a:solidFill><a:srgbClr val="{up}"/></a:solidFill></a:ln>
                </c:spPr></c:upBars>
                <c:downBars><c:spPr><a:solidFill><a:srgbClr val="{down}"/></a:solidFill>
                  <a:ln w="9525"><a:solidFill><a:srgbClr val="{down}"/></a:solidFill></a:ln>
                </c:spPr></c:downBars>
              </c:upDownBars>
              <c:axId val="111111111"/><c:axId val="222222222"/>
            </c:stockChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    private static string StockChart(
        int series = 3, bool manualLayout = true, int gapWidth = 150, bool stated = true,
        string closeMarker = "", string? legend = null)
    {
        string[] categories = ["One", "Two", "Three", "Four"];

        var layout = manualLayout
            ? """
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.7"/><c:h val="0.7"/>
              </c:manualLayout>
              """
            : string.Empty;

        // The order is the one a stock chart is read in, and it is the order that says which
        // series is which: open, high, low, close, with the opening left out where there is none.
        var opening = series > 3
            ? DocxBuilder.ChartStockSeries(0, "Open", categories, [28, 40, 22, 50])
            : string.Empty;

        var shift = series > 3 ? 1 : 0;

        var scale = stated
            ? """<c:max val="60"/><c:min val="0"/>"""
            : string.Empty;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{layout}</c:layout>
                <c:stockChart>
                  {opening}
                  {DocxBuilder.ChartStockSeries(shift, "High", categories, [40, 52, 33, 58])}
                  {DocxBuilder.ChartStockSeries(shift + 1, "Low", categories, [20, 30, 15, 35])}
                  {DocxBuilder.ChartStockSeries(shift + 2, "Close", categories, [35, 33, 28, 45],
                      marker: closeMarker.Length > 0 ? closeMarker : series > 3 ? "none" : "dot")}
                  <c:hiLowLines/>
                  {(series > 3
                      ? $"""
                         <c:upDownBars>
                           <c:gapWidth val="{gapWidth}"/>
                           <c:upBars><c:spPr><a:solidFill><a:srgbClr val="FFFFFF"/></a:solidFill>
                             <a:ln w="9525"><a:solidFill><a:srgbClr val="000000"/></a:solidFill></a:ln>
                           </c:spPr></c:upBars>
                           <c:downBars><c:spPr><a:solidFill><a:srgbClr val="000000"/></a:solidFill>
                             <a:ln w="9525"><a:solidFill><a:srgbClr val="000000"/></a:solidFill></a:ln>
                           </c:spPr></c:downBars>
                         </c:upDownBars>
                         """
                      : string.Empty)}
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:stockChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:majorTickMark val="out"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/>{scale}</c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>{(stated ? "<c:majorUnit val=\"20\"/>" : string.Empty)}
                </c:valAx>
              </c:plotArea>
              {(legend is null
                  ? string.Empty
                  : $"<c:legend><c:legendPos val=\"{legend}\"/><c:overlay val=\"0\"/></c:legend>")}
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>
    /// A bar chart of one kind or another: standing up or lying along, clustered or stacked, with
    /// its plotting placed by hand or left to Word.
    /// </summary>
    private static string BarChart(
        string direction, string grouping, int series, bool manualLayout = true,
        double? maximum = 60, string numberFormat = "General", string tickMark = "none",
        double[]? values = null)
    {
        var categories = new[] { "One", "Two", "Three" };

        values ??= [30, 45, 20];

        var layout = manualLayout
            ? """
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.25"/><c:y val="0.1"/><c:w val="0.65"/><c:h val="0.7"/>
              </c:manualLayout>
              """
            : string.Empty;

        var scale = maximum is { } top
            ? $"""<c:max val="{top.ToString(CultureInfo.InvariantCulture)}"/><c:min val="0"/>"""
            : string.Empty;

        var unit = maximum is { } value
            ? $"""<c:majorUnit val="{(value / 3).ToString(CultureInfo.InvariantCulture)}"/>"""
            : string.Empty;

        // Which way round the axes go is the whole of what makes a bar lie down.
        var (categoryPosition, valuePosition) = direction == "bar" ? ("l", "b") : ("b", "l");

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout>{layout}</c:layout>
                <c:barChart>
                  <c:barDir val="{direction}"/>
                  <c:grouping val="{grouping}"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", categories, values, "4472C4")}
                  {(series > 1
                      ? DocxBuilder.ChartSeries(1, "Others", categories, [10, 15, 25], "ED7D31")
                      : string.Empty)}
                  <c:gapWidth val="150"/>
                  <c:overlap val="{(grouping == "clustered" ? -27 : 100)}"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:barChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="{categoryPosition}"/>
                  <c:majorTickMark val="{tickMark}"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/><c:lblOffset val="100"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/>{scale}</c:scaling>
                  <c:delete val="0"/><c:axPos val="{valuePosition}"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="{numberFormat}" sourceLinked="0"/>
                  <c:majorTickMark val="none"/><c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>{unit}
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>A chart part, wrapped in the element every one of them begins with.</summary>
    /// <summary>
    /// A three-dimensional column chart whose plot area is placed by hand and whose single bar
    /// fills the plot box across and in depth.
    /// </summary>
    /// <remarks>
    /// The bar is the box: no gap between categories and none in depth, with one of each, so its
    /// eight corners sit at the corners of the plotting itself. It reaches 60 of 100 up the value
    /// axis, which keeps it clear of the ceiling — a bar at the maximum is cut by the plot area and
    /// what comes back is the rectangle's corners rather than the box's.
    ///
    /// Every axis is deleted and labelled nothing, so the only ink on the page is the box. That is
    /// what lets <c>BoxSilhouette</c> find it by colour alone.
    /// </remarks>
    private static string ChartPart3D(
        double x, double y, double w, double h, double rotX, double rotY) => $"""
          <c:chart>
            <c:view3D><c:rotX val="{rotX}"/><c:rotY val="{rotY}"/><c:rAngAx val="0"/>
              <c:perspective val="30"/><c:depthPercent val="100"/></c:view3D>
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="{x:0.######}"/><c:y val="{y:0.######}"/>
                <c:w val="{w:0.######}"/><c:h val="{h:0.######}"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>60</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="0"/><c:gapDepth val="0"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;

    private static string ChartPart3DView(
        string view3D, double x, double y, double w, double h) => $"""
          <c:chart>
            {view3D}
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="{x:0.######}"/><c:y val="{y:0.######}"/>
                <c:w val="{w:0.######}"/><c:h val="{h:0.######}"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>60</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="0"/><c:gapDepth val="0"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;

    private static string ChartPart3DValue(
        double value, double minimum, double maximum) => $"""
          <c:chart>
            <c:view3D><c:rotX val="15"/><c:rotY val="20"/><c:rAngAx val="0"/>
              <c:perspective val="30"/><c:depthPercent val="100"/></c:view3D>
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/>
                <c:w val="0.6"/><c:h val="0.55"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>{value}</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="0"/><c:gapDepth val="0"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="{maximum}"/><c:min val="{minimum}"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;

    /// <summary>
    /// A three-dimensional column chart of one bar in a stated colour, with the walls and floor
    /// stated or not.
    /// </summary>
    /// <remarks>
    /// <c>c:floor</c>, <c>c:sideWall</c> and <c>c:backWall</c> are children of <c>c:chart</c> and
    /// go after <c>c:view3D</c> and before <c>c:plotArea</c>. Put inside <c>c:plotArea</c>, where a
    /// reader might reasonably expect the things that describe the plot to live, Word **silently
    /// ignores them** — which is what <c>misplaced</c> exists to prove, since a rule saying they are
    /// honoured means nothing without a page showing what being ignored looks like.
    /// </remarks>
    private static string ChartPart3DShade(string colour, double value, string walls)
    {
        var stated = walls switch
        {
            "all" => """
                  <c:floor><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr></c:floor>
                  <c:sideWall><c:spPr><a:solidFill><a:srgbClr val="00C000"/></a:solidFill></c:spPr></c:sideWall>
                  <c:backWall><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr></c:backWall>
                """,
            "floor" => """
                  <c:floor><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr></c:floor>
                """,
            "misplaced" => """
                  <c:floor><c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr></c:floor>
                  <c:sideWall><c:spPr><a:solidFill><a:srgbClr val="00C000"/></a:solidFill></c:spPr></c:sideWall>
                  <c:backWall><c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr></c:backWall>
                """,
            _ => ""
        };

        return $$"""
          <c:chart>
            <c:view3D><c:rotX val="15"/><c:rotY val="20"/><c:rAngAx val="0"/>
              <c:perspective val="30"/><c:depthPercent val="100"/></c:view3D>
        {{(walls == "misplaced" ? "" : stated)}}
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.55"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="{{colour}}"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>{{value}}</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="150"/><c:gapDepth val="150"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
        {{(walls == "misplaced" ? stated : "")}}
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;
    }

    /// <summary>
    /// A three-dimensional column chart whose single bar reaches the axis maximum, so that the bar
    /// is the whole plot box.
    /// </summary>
    /// <remarks>
    /// Everywhere else in this suite a bar is held below the maximum, because one that reaches it is
    /// cut by the plot area and what comes back is the rectangle's corner rather than the box's.
    /// Here it is deliberate and it is safe: the scene does not fill the rectangle, so a bar at the
    /// maximum stops short of the edge and the gap it leaves is the thing being measured.
    /// </remarks>
    private static string ChartPart3DInset(int hPercent, double x, double y, double w, double h) => $$"""
          <c:chart>
            <c:view3D><c:rotX val="15"/><c:rotY val="20"/><c:rAngAx val="0"/>
              <c:perspective val="30"/><c:depthPercent val="100"/>{{(hPercent > 0 ? $"<c:hPercent val=\"{hPercent}\"/>" : "")}}</c:view3D>
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="{{x}}"/><c:y val="{{y}}"/><c:w val="{{w}}"/><c:h val="{{h}}"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>100</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="0"/><c:gapDepth val="0"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;

    /// <summary>
    /// A three-dimensional chart showing gridlines on the value axis and on the series axis, in
    /// different colours.
    /// </summary>
    /// <remarks>
    /// Five series with the gaps at their widest and values of one, so the bars are slivers and the
    /// floor they stand on is left in view. They are near-white for the same reason.
    /// </remarks>
    /// <summary>
    /// The same scene as <see cref="ChartPart3DGrid"/>, with the number of series as a parameter.
    /// </summary>
    /// <remarks>
    /// Written separately rather than by generalising the other, because the other is what a dozen
    /// committed measurements were taken against and its XML should not move underneath them.
    ///
    /// The series are the point of it: a series-axis gridline is drawn per series, so the count is
    /// how many lines the floor carries, and that is one of the two things #129 is measuring. Every
    /// series is the same near-white so the bars stay out of the way of the gridlines, and every
    /// value is one so the bars are all of a height and none of them hides a line another needs.
    /// </remarks>
    /// <summary>
    /// One three-dimensional bar with the two gaps stated, for measuring what a bar's footprint is.
    /// </summary>
    /// <remarks>
    /// One category and one series throughout, so the slot is the whole box and the bar's footprint
    /// is read directly against it. <c>standard</c> grouping rather than <c>clustered</c>: the two
    /// draw different pictures from the same document, and an earlier run of #114 measured a third
    /// of the width under <c>clustered</c> believing it was measuring depth.
    ///
    /// The bar is red and nothing else on the page is, which is what <see cref="BoxSilhouette"/>
    /// needs. The value is held at 60 on every page so the bar's height never moves — only its
    /// footprint is under test.
    /// </remarks>
    /// <summary>
    /// A three-dimensional bar chart with the counts as parameters, every bar red and abutting.
    /// </summary>
    /// <remarks>
    /// For #116, whose question is what the number of categories and series does to the box itself.
    /// Both gaps are nought so the bars fill their footprints and touch, and every value is the same
    /// so their union is a box rather than a staircase. Every bar is red, so what
    /// <see cref="BoxSilhouette"/> finds is the box and not a bar in it.
    ///
    /// <c>standard</c> grouping throughout: it is what puts several series **in depth**. Under
    /// <c>clustered</c> they stand side by side across instead, which an earlier run of #114 spent a
    /// session measuring in the belief it was measuring depth.
    /// </remarks>
    /// <summary>
    /// A three-dimensional bar chart with the gaps stated and exactly one bar marked out in red.
    /// </summary>
    /// <remarks>
    /// For the multi-bar half of #114. The bars cannot all be one colour here — with a gap stated
    /// they no longer abut, so the red region would be several boxes and its hull is not a hexagon
    /// that <see cref="BoxSilhouette"/> can read. One bar is red and the rest are grey, so the
    /// silhouette is that bar alone.
    ///
    /// Right-angled axes throughout, which is what makes the reading exact: with no perspective the
    /// projection is affine, so a ratio of lengths on the page is a ratio of lengths in the scene and
    /// no projection has to be solved for. #116 showed the perspective arm scatters several per cent
    /// and invents a dependence on the plot rectangle.
    ///
    /// A page with the gap at nought and every bar red gives the box under the same fit, which is
    /// what each gapped page is read against.
    /// </remarks>
    private static string ChartPart3DSlot(
        int categories, int series, int gapWidth, int gapDepth, int red, int rotX = 15)
    {
        string Cell(int i) => $"{(char)('B' + i)}";

        var points = string.Concat(Enumerable.Range(0, categories)
            .Select(i => $"""<c:pt idx="{i}"><c:v>C{i}</c:v></c:pt>"""));

        var values = string.Concat(Enumerable.Range(0, categories)
            .Select(i => $"""<c:pt idx="{i}"><c:v>60</c:v></c:pt>"""));

        var built = new System.Text.StringBuilder();

        for (var j = 0; j < series; j++)
        {
            // With several series it is the series that is picked out; with several categories, the
            // point. `red` is negative on the pages where every bar is red and the union is the box.
            var whole = red < 0 || (series > 1 ? j == red : false) ? "FF0000" : "BFBFBF";

            var marked = red >= 0 && series == 1
                ? $"""
                   <c:dPt><c:idx val="{red}"/><c:bubble3D val="0"/>
                     <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                       <a:ln><a:noFill/></a:ln></c:spPr></c:dPt>
                   """
                : "";

            built.Append($"""
                <c:ser><c:idx val="{j}"/><c:order val="{j}"/>
                  <c:tx><c:strRef><c:f>Sheet1!${Cell(j)}$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S{j}</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="{whole}"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  {marked}
                  <c:cat><c:strRef><c:f>Sheet1!$A$2:$A${categories + 1}</c:f><c:strCache>
                    <c:ptCount val="{categories}"/>{points}</c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!${Cell(j)}$2:${Cell(j)}${categories + 1}</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="{categories}"/>{values}
                    </c:numCache></c:numRef></c:val>
                </c:ser>
                """);
        }

        return $$"""
              <c:chart>
                <c:view3D><c:rotX val="{{rotX}}"/><c:rotY val="20"/><c:rAngAx val="1"/>
                  <c:perspective val="30"/><c:depthPercent val="100"/></c:view3D>
                <c:plotArea>
                  <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                    <c:xMode val="edge"/><c:yMode val="edge"/>
                    <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.55"/>
                  </c:manualLayout></c:layout>
                  <c:bar3DChart>
                    <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                    {{built}}
                    <c:gapWidth val="{{gapWidth}}"/><c:gapDepth val="{{gapDepth}}"/><c:shape val="box"/>
                    <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
                  </c:bar3DChart>
                  <c:catAx><c:axId val="111111111"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                    <c:crossAx val="222222222"/></c:catAx>
                  <c:valAx><c:axId val="222222222"/>
                    <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                    <c:crossAx val="111111111"/></c:valAx>
                  <c:serAx><c:axId val="333333333"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                    <c:crossAx val="222222222"/></c:serAx>
                </c:plotArea>
                <c:plotVisOnly val="1"/>
              </c:chart>
            """;
    }

    private static string ChartPart3DCounts(
        int categories, int series, double x, double y, double w, double h, int rightAngled = 0,
        int value = 60, int depthPercent = 100, int rotX = 15, int rotY = 20, int perspective = 30,
        int hPercent = 0)
    {
        string Cell(int i) => $"{(char)('B' + i)}";

        var points = string.Concat(Enumerable.Range(0, categories)
            .Select(i => $"""<c:pt idx="{i}"><c:v>C{i}</c:v></c:pt>"""));

        var values = string.Concat(Enumerable.Range(0, categories)
            .Select(i => $"""<c:pt idx="{i}"><c:v>{value}</c:v></c:pt>"""));

        var built = new System.Text.StringBuilder();

        for (var j = 0; j < series; j++)
            built.Append($"""
                <c:ser><c:idx val="{j}"/><c:order val="{j}"/>
                  <c:tx><c:strRef><c:f>Sheet1!${Cell(j)}$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S{j}</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2:$A${categories + 1}</c:f><c:strCache>
                    <c:ptCount val="{categories}"/>{points}</c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!${Cell(j)}$2:${Cell(j)}${categories + 1}</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="{categories}"/>{values}
                    </c:numCache></c:numRef></c:val>
                </c:ser>
                """);

        return $$"""
              <c:chart>
                <c:view3D><c:rotX val="{{rotX}}"/><c:rotY val="{{rotY}}"/><c:rAngAx val="{{rightAngled}}"/>
                  <c:perspective val="{{perspective}}"/><c:depthPercent val="{{depthPercent}}"/>{{(
                    hPercent > 0 ? $"<c:hPercent val=\"{hPercent}\"/>" : "")}}</c:view3D>
                <c:plotArea>
                  <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                    <c:xMode val="edge"/><c:yMode val="edge"/>
                    <c:x val="{{x:0.######}}"/><c:y val="{{y:0.######}}"/>
                    <c:w val="{{w:0.######}}"/><c:h val="{{h:0.######}}"/>
                  </c:manualLayout></c:layout>
                  <c:bar3DChart>
                    <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                    {{built}}
                    <c:gapWidth val="0"/><c:gapDepth val="0"/><c:shape val="box"/>
                    <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
                  </c:bar3DChart>
                  <c:catAx><c:axId val="111111111"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                    <c:crossAx val="222222222"/></c:catAx>
                  <c:valAx><c:axId val="222222222"/>
                    <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                    <c:crossAx val="111111111"/></c:valAx>
                  <c:serAx><c:axId val="333333333"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                    <c:crossAx val="222222222"/></c:serAx>
                </c:plotArea>
                <c:plotVisOnly val="1"/>
              </c:chart>
            """;
    }

    private static string ChartPart3DFootprint(int gapWidth, int gapDepth) => $$"""
          <c:chart>
            <c:view3D><c:rotX val="15"/><c:rotY val="20"/><c:rAngAx val="0"/>
              <c:perspective val="30"/><c:depthPercent val="100"/></c:view3D>
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/><c:w val="0.6"/><c:h val="0.55"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>60</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="{{gapWidth}}"/><c:gapDepth val="{{gapDepth}}"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;

    private static string ChartPart3DGridSeries(double rotX, int depthPercent, int series)
    {
        var built = new System.Text.StringBuilder();

        for (var i = 0; i < series; i++)
        {
            var column = (char)('B' + i);

            built.Append($"""
                <c:ser><c:idx val="{i}"/><c:order val="{i}"/>
                  <c:tx><c:strRef><c:f>Sheet1!${column}$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S{i}</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="F4F4F4"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                """);

            if (i == 0)
                built.Append("""
                      <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                        <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                    """);

            built.Append($"""
                  <c:val><c:numRef><c:f>Sheet1!${column}$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                """);
        }

        return $$"""
              <c:chart>
                <c:view3D><c:rotX val="{{rotX}}"/><c:rotY val="20"/><c:rAngAx val="0"/>
                  <c:perspective val="30"/><c:depthPercent val="{{depthPercent}}"/></c:view3D>
                <c:plotArea>
                  <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                    <c:xMode val="edge"/><c:yMode val="edge"/>
                    <c:x val="0.2"/><c:y val="0.05"/><c:w val="0.6"/><c:h val="0.80"/>
                  </c:manualLayout></c:layout>
                  <c:bar3DChart>
                    <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                    {{built}}
                    <c:gapWidth val="500"/><c:gapDepth val="500"/><c:shape val="box"/>
                    <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
                  </c:bar3DChart>
                  <c:catAx><c:axId val="111111111"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                    <c:crossAx val="222222222"/></c:catAx>
                  <c:valAx><c:axId val="222222222"/>
                    <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                    <c:majorGridlines><c:spPr><a:ln w="25400"><a:solidFill>
                      <a:srgbClr val="FF0000"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>
                    <c:majorUnit val="20"/>
                    <c:crossAx val="111111111"/></c:valAx>
                  <c:serAx><c:axId val="333333333"/>
                    <c:scaling><c:orientation val="minMax"/></c:scaling>
                    <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                    <c:majorGridlines><c:spPr><a:ln w="25400"><a:solidFill>
                      <a:srgbClr val="0000FF"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>
                    <c:crossAx val="222222222"/></c:serAx>
                </c:plotArea>
                <c:plotVisOnly val="1"/>
              </c:chart>
            """;
    }

    private static string ChartPart3DGrid(double rotX, int depthPercent = 100) => $$"""
          <c:chart>
            <c:view3D><c:rotX val="{{rotX}}"/><c:rotY val="20"/><c:rAngAx val="0"/>
              <c:perspective val="30"/><c:depthPercent val="{{depthPercent}}"/></c:view3D>
            <c:plotArea>
              <c:layout><c:manualLayout><c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.05"/><c:w val="0.6"/><c:h val="0.80"/>
              </c:manualLayout></c:layout>
              <c:bar3DChart>
                <c:barDir val="col"/><c:grouping val="standard"/><c:varyColors val="0"/>
                <c:ser><c:idx val="0"/><c:order val="0"/>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S0</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="F4F4F4"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>K</c:v></c:pt></c:strCache></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:ser><c:idx val="1"/><c:order val="1"/>
                  <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S1</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="F4F4F4"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:val><c:numRef><c:f>Sheet1!$C$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:ser><c:idx val="2"/><c:order val="2"/>
                  <c:tx><c:strRef><c:f>Sheet1!$D$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S2</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="F4F4F4"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:val><c:numRef><c:f>Sheet1!$D$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:ser><c:idx val="3"/><c:order val="3"/>
                  <c:tx><c:strRef><c:f>Sheet1!$E$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S3</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="F4F4F4"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:val><c:numRef><c:f>Sheet1!$E$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:ser><c:idx val="4"/><c:order val="4"/>
                  <c:tx><c:strRef><c:f>Sheet1!$F$1</c:f><c:strCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>S4</c:v></c:pt></c:strCache></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:srgbClr val="F4F4F4"/></a:solidFill>
                    <a:ln><a:noFill/></a:ln></c:spPr>
                  <c:val><c:numRef><c:f>Sheet1!$F$2</c:f><c:numCache>
                    <c:formatCode>General</c:formatCode><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
                <c:gapWidth val="500"/><c:gapDepth val="500"/><c:shape val="box"/>
                <c:axId val="111111111"/><c:axId val="222222222"/><c:axId val="333333333"/>
              </c:bar3DChart>
              <c:catAx><c:axId val="111111111"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:crossAx val="222222222"/></c:catAx>
              <c:valAx><c:axId val="222222222"/>
                <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                <c:delete val="1"/><c:axPos val="l"/><c:tickLblPos val="none"/>
                <c:majorGridlines><c:spPr><a:ln w="25400"><a:solidFill>
                  <a:srgbClr val="FF0000"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>
                <c:majorUnit val="20"/>
                <c:crossAx val="111111111"/></c:valAx>
              <c:serAx><c:axId val="333333333"/>
                <c:scaling><c:orientation val="minMax"/></c:scaling>
                <c:delete val="1"/><c:axPos val="b"/><c:tickLblPos val="none"/>
                <c:majorGridlines><c:spPr><a:ln w="25400"><a:solidFill>
                  <a:srgbClr val="0000FF"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>
                <c:crossAx val="222222222"/></c:serAx>
            </c:plotArea>
            <c:plotVisOnly val="1"/>
          </c:chart>
        """;

    private static string ChartPart(string chartXml) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                      xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          {chartXml}
        </c:chartSpace>
        """;

    /// <summary>
    /// A column chart whose plot area is placed by hand and whose value axis is told what to do.
    /// </summary>
    /// <remarks>
    /// The plot area is given as fractions of the frame, which is what a chart means by an inner
    /// layout: where the bars go, without the axis labels or anything else moving it. That pins
    /// everything the plotting can be measured against — a bar of value v in a plot of known
    /// height reaches a known place — and leaves Word's automatic placing for its own probe.
    ///
    /// Nothing is left to a default that could be guessed at: the axes state their bounds and
    /// their spacing, the bars state their colours, and there is neither a title nor a legend.
    /// </remarks>
    /// <summary>
    /// A chart of four points carrying one trendline, for measuring what Word draws for it.
    /// </summary>
    /// <remarks>
    /// Everything about the plot is stated rather than left to Word — where it goes, what the
    /// value axis runs between — so that the only thing varying between the probe's pages is the
    /// trendline itself. The values 30/45/20/55 are the ones the other chart fixtures use, and
    /// they are deliberately not collinear: points on a line would make every kind of fit agree
    /// and measure nothing.
    ///
    /// The trendline states its own line format, which is what Word writes when one is added
    /// through its interface, so the colour and weight are read from the document rather than
    /// guessed at.
    /// </remarks>
    private static string TrendlineProbeChart(
        string kind, int order = 2, int period = 2, double forward = 0, double backward = 0,
        double? intercept = null) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/>
                <c:w val="0.7"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:lineChart>
              <c:grouping val="standard"/>
              <c:varyColors val="0"/>
              <c:ser>
                <c:idx val="0"/>
                <c:order val="0"/>
                <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f>
                  <c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>Units</c:v></c:pt></c:strCache>
                </c:strRef></c:tx>
                <c:spPr><a:ln w="28575"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln></c:spPr>
                <c:marker><c:symbol val="none"/></c:marker>
                <c:trendline>
                  <c:spPr><a:ln w="19050"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:ln></c:spPr>
                  <c:trendlineType val="{kind}"/>
                  {(kind == "poly" ? $"<c:order val=\"{order}\"/>" : string.Empty)}
                  {(kind == "movingAvg" ? $"<c:period val=\"{period}\"/>" : string.Empty)}
                  {(forward != 0 ? $"<c:forward val=\"{forward.ToString(CultureInfo.InvariantCulture)}\"/>" : string.Empty)}
                  {(backward != 0 ? $"<c:backward val=\"{backward.ToString(CultureInfo.InvariantCulture)}\"/>" : string.Empty)}
                  {(intercept is { } cross ? $"<c:intercept val=\"{cross.ToString(CultureInfo.InvariantCulture)}\"/>" : string.Empty)}
                  <c:dispRSqr val="0"/>
                  <c:dispEq val="0"/>
                </c:trendline>
                <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f>
                  <c:strCache><c:ptCount val="4"/><c:pt idx="0"><c:v>North</c:v></c:pt><c:pt idx="1"><c:v>South</c:v></c:pt><c:pt idx="2"><c:v>East</c:v></c:pt><c:pt idx="3"><c:v>West</c:v></c:pt></c:strCache>
                </c:strRef></c:cat>
                <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f>
                  <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="4"/>
                    <c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>45</c:v></c:pt><c:pt idx="2"><c:v>20</c:v></c:pt><c:pt idx="3"><c:v>55</c:v></c:pt>
                  </c:numCache>
                </c:numRef></c:val>
                <c:smooth val="0"/>
              </c:ser>
              <c:marker val="1"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:lineChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling>
                <c:orientation val="minMax"/>
                <c:max val="80"/>
                <c:min val="0"/>
              </c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    /// <summary>
    /// A chart of four points carrying error bars, for measuring what Word draws for them.
    /// </summary>
    /// <remarks>
    /// Built like the trendline probe beside it: everything about the plot stated, the bars
    /// painted a red nothing else on the page uses, and one thing varying per page. The values
    /// 30/45/20/55 have a population deviation of 13.462 and a sample one of 15.546 — 15% apart,
    /// which is what lets one page tell them from each other.
    /// </remarks>
    private static string ErrorBarProbeChart(
        string amount, double value = 10, string type = "both", bool caps = true,
        double plotWidth = 0.7, double labelSize = 10, string? plus = null, string? minus = null) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/>
                <c:w val="{plotWidth.ToString(CultureInfo.InvariantCulture)}"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:lineChart>
              <c:grouping val="standard"/>
              <c:varyColors val="0"/>
              <c:ser>
                <c:idx val="0"/>
                <c:order val="0"/>
                <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f>
                  <c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>Units</c:v></c:pt></c:strCache>
                </c:strRef></c:tx>
                <c:spPr><a:ln w="28575"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln></c:spPr>
                <c:marker><c:symbol val="none"/></c:marker>
                <c:errBars>
                  <c:spPr><a:ln w="12700"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:ln></c:spPr>
                  <c:errDir val="y"/>
                  <c:errBarType val="{type}"/>
                  <c:errValType val="{amount}"/>
                  <c:noEndCap val="{(caps ? "0" : "1")}"/>
                  {(plus is null ? string.Empty : $"<c:plus><c:numRef><c:f>Sheet1!$C$2:$C$5</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"4\"/>{plus}</c:numCache></c:numRef></c:plus>")}
                  {(minus is null ? string.Empty : $"<c:minus><c:numRef><c:f>Sheet1!$D$2:$D$5</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"4\"/>{minus}</c:numCache></c:numRef></c:minus>")}
                  <c:val val="{value.ToString(CultureInfo.InvariantCulture)}"/>
                </c:errBars>
                <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f>
                  <c:strCache><c:ptCount val="4"/><c:pt idx="0"><c:v>North</c:v></c:pt><c:pt idx="1"><c:v>South</c:v></c:pt><c:pt idx="2"><c:v>East</c:v></c:pt><c:pt idx="3"><c:v>West</c:v></c:pt></c:strCache>
                </c:strRef></c:cat>
                <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f>
                  <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="4"/>
                    <c:pt idx="0"><c:v>30</c:v></c:pt><c:pt idx="1"><c:v>45</c:v></c:pt><c:pt idx="2"><c:v>20</c:v></c:pt><c:pt idx="3"><c:v>55</c:v></c:pt>
                  </c:numCache>
                </c:numRef></c:val>
                <c:smooth val="0"/>
              </c:ser>
              <c:marker val="1"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:lineChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="{(int)(labelSize * 100)}"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling>
                <c:orientation val="minMax"/>
                <c:max val="80"/>
                <c:min val="0"/>
              </c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    /// <summary>
    /// A line chart carrying drop lines, high-low lines or both, for measuring what Word draws.
    /// </summary>
    /// <remarks>
    /// Painted the same red the trendline and error-bar probes use, and for the same reason: it
    /// is what makes a thin line measurable against Word's, since nothing else on the page is red.
    ///
    /// A second series is written where <paramref name="two"/> asks for one, because a high-low
    /// line needs two values in a category to span anything, and because it is what says which
    /// point a drop line hangs from.
    /// </remarks>
    private static string DropLineProbeChart(
        bool drop, bool highLow, bool two = false, double minimum = 0, double maximum = 80) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/>
                <c:w val="0.7"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:lineChart>
              <c:grouping val="standard"/>
              <c:varyColors val="0"/>
              {(drop ? """<c:dropLines><c:spPr><a:ln w="12700"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:ln></c:spPr></c:dropLines>""" : string.Empty)}
              {(highLow ? """<c:hiLowLines><c:spPr><a:ln w="12700"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:ln></c:spPr></c:hiLowLines>""" : string.Empty)}
              {DropLineSeries(0, "Units", [30, 45, 20, 55], "4472C4")}
              {(two ? DropLineSeries(1, "Others", [10, 25, 60, 15], "ED7D31") : string.Empty)}
              <c:marker val="1"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:lineChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling>
                <c:orientation val="minMax"/>
                <c:max val="{maximum.ToString(CultureInfo.InvariantCulture)}"/>
                <c:min val="{minimum.ToString(CultureInfo.InvariantCulture)}"/>
              </c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    /// <summary>One series of the drop-line probe, drawn thin so the red stands clear of it.</summary>
    private static string DropLineSeries(
        int index, string name, IReadOnlyList<double> values, string hex) => $"""
        <c:ser>
          <c:idx val="{index}"/>
          <c:order val="{index}"/>
          <c:tx><c:strRef><c:f>Sheet1!$B${index + 1}</c:f>
            <c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>{name}</c:v></c:pt></c:strCache>
          </c:strRef></c:tx>
          <c:spPr><a:ln w="19050"><a:solidFill><a:srgbClr val="{hex}"/></a:solidFill></a:ln></c:spPr>
          <c:marker><c:symbol val="none"/></c:marker>
          <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f>
            <c:strCache><c:ptCount val="4"/><c:pt idx="0"><c:v>North</c:v></c:pt><c:pt idx="1"><c:v>South</c:v></c:pt><c:pt idx="2"><c:v>East</c:v></c:pt><c:pt idx="3"><c:v>West</c:v></c:pt></c:strCache>
          </c:strRef></c:cat>
          <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f>
            <c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="4"/>
              {string.Concat(values.Select((v, i) => $"<c:pt idx=\"{i}\"><c:v>{v.ToString(CultureInfo.InvariantCulture)}</c:v></c:pt>"))}
            </c:numCache>
          </c:numRef></c:val>
          <c:smooth val="0"/>
        </c:ser>
        """;

    /// <summary>
    /// A chart whose title and legend may be put by hand, for measuring where Word puts them.
    /// </summary>
    /// <remarks>
    /// The plot area is left to Word rather than stated, unlike the other chart probes: what is
    /// being measured is partly whether a hand-placed title or legend still takes room off the
    /// plot, and stating the plot would answer that question by fiat.
    ///
    /// The title states <c>b="1"</c> outright. That was originally to keep #82 out of a fixture
    /// whose business is where a title is *put* — Word sets a chart title bold where the part says
    /// nothing and this did not, which made our title 1.91pt narrower and, since it is centred,
    /// started it 0.96pt to the right. #82 is fixed and the weight would now be right either way,
    /// but stating it is still the better fixture: it measures placement against a weight that
    /// cannot drift, and <c>chart-title-weight-probe</c> is where the default itself is measured.
    /// </remarks>
    /// <summary>
    /// A chart whose legend is placed by hand and given a size, for measuring what the size does.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PlacementProbeChart"/> because the question is different. That one
    /// settled where a stated corner puts a legend, and its pages state no size at all; this varies
    /// the size and the number of entries, which is what a rule about a *box* has to be measured
    /// against. There is no title, so nothing but the legend and the plot can move.
    /// </remarks>
    /// <summary>
    /// A chart with one long-named series and a sized legend, for measuring where Word cuts it.
    /// </summary>
    /// <remarks>
    /// One entry a page on purpose. The evidence that started this came from legends of two and
    /// three, where the packing of a row and the dropping of an entry that will not fit are
    /// confounded with the cutting; a single entry leaves only the cutting.
    ///
    /// The name is the alphabet because the drawn text then says outright where the cut fell. A
    /// repeated letter would give a width and leave the count to be worked out; this gives the
    /// count directly and the width as a check on it.
    ///
    /// The plot is stated rather than left to Word, unlike the first draft of this probe. How much
    /// room a legend given a size takes off the plot is a question of its own — and one this does
    /// not answer, since the answer turned out not to depend on the size at all. Stating the plot
    /// keeps that out of a fixture whose business is where the words break. See #91.
    /// </remarks>
    /// <summary>
    /// A chart whose plot is left to Word, for measuring what room a legend takes off it.
    /// </summary>
    /// <remarks>
    /// The plot is deliberately **not** stated, which is the opposite of every other chart probe
    /// here and the reason this needs a fixture of its own: the plot is the thing being measured.
    /// Its width is read off the category labels, which span it.
    ///
    /// The legend can be an ordinary one up the side, one placed by a corner, or one placed and
    /// given a size — and the name can be long or short. That is what separates "a stated size
    /// changes what the plot gives up" from "the room was never right for an entry this wide".
    /// </remarks>
    private static string LegendRoomProbeChart(
        string name, string placement, string? second = null) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout/>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, name, ["One", "Two", "Three", "Four"], [30, 45, 20, 55], "4472C4")}
              {(second is null
                  ? string.Empty
                  : DocxBuilder.ChartSeries(1, second, ["One", "Two", "Three", "Four"], [25, 40, 15, 50], "ED7D31"))}
              <c:gapWidth val="150"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:legend>
            <c:legendPos val="r"/>
            {placement switch
            {
                "corner" => """<c:layout><c:manualLayout><c:xMode val="edge"/><c:yMode val="edge"/><c:x val="0.6"/><c:y val="0.3"/></c:manualLayout></c:layout>""",
                "sized" => """<c:layout><c:manualLayout><c:xMode val="edge"/><c:yMode val="edge"/><c:x val="0.6"/><c:y val="0.3"/><c:w val="0.3"/><c:h val="0.25"/></c:manualLayout></c:layout>""",
                _ => "<c:layout/>"
            }}
            <c:overlay val="0"/>
            <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>
          </c:legend>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    /// <summary>
    /// A chart of three series whose sized legend box is swept in one dimension at a time.
    /// </summary>
    /// <remarks>
    /// Built for #87 and #90 together, because neither can be settled alone: the rule for which
    /// entries are drawn is expressed in the shares the other is about. Earlier probes moved the
    /// width and the height at once and so could say neither.
    ///
    /// Three names of different lengths, for the same reason as the probes before it — both open
    /// questions turn on *which* entry's extent decides a position.
    /// </remarks>
    private static string LegendBoxProbeChart((double W, double H) size) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.12"/><c:y val="0.1"/><c:w val="0.45"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, "Aa", ["One", "Two", "Three", "Four"], [30, 45, 20, 55], "4472C4")}
              {DocxBuilder.ChartSeries(1, "Middling", ["One", "Two", "Three", "Four"], [25, 40, 15, 50], "ED7D31")}
              {DocxBuilder.ChartSeries(2, "Longer name here", ["One", "Two", "Three", "Four"], [20, 35, 10, 45], "A5A5A5")}
              <c:gapWidth val="150"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:legend>
            <c:legendPos val="r"/>
            <c:layout><c:manualLayout>
              <c:xMode val="edge"/><c:yMode val="edge"/>
              <c:x val="0.05"/><c:y val="0.05"/>
              <c:w val="{size.W.ToString(CultureInfo.InvariantCulture)}"/>
              <c:h val="{size.H.ToString(CultureInfo.InvariantCulture)}"/>
            </c:manualLayout></c:layout>
            <c:overlay val="1"/>
            <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>
          </c:legend>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    private static string LegendCutProbeChart(double boxWidth, string name = "abcdefghijklmnopqrstuvwxyz") => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.12"/><c:y val="0.1"/><c:w val="0.5"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, name,
                  ["One", "Two", "Three", "Four"], [30, 45, 20, 55], "4472C4")}
              <c:gapWidth val="150"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:legend>
            <c:legendPos val="r"/>
            <c:layout><c:manualLayout>
              <c:xMode val="edge"/><c:yMode val="edge"/>
              <c:x val="0.1"/><c:y val="0.05"/>
              <c:w val="{boxWidth.ToString(CultureInfo.InvariantCulture)}"/><c:h val="0.25"/>
            </c:manualLayout></c:layout>
            <c:overlay val="0"/>
            <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>
          </c:legend>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    private static string LegendSizeProbeChart(
        (double X, double Y) corner, (double W, double H)? size, bool two) => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout/>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, "Units", ["North", "South", "East", "West"],
                  [30, 45, 20, 55], "4472C4")}
              {(two
                  ? DocxBuilder.ChartSeries(1, "Others", ["North", "South", "East", "West"],
                      [10, 25, 40, 15], "ED7D31")
                  : string.Empty)}
              <c:gapWidth val="150"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:legend>
            <c:legendPos val="r"/>
            <c:layout><c:manualLayout>
              <c:xMode val="edge"/><c:yMode val="edge"/>
              <c:x val="{corner.X.ToString(CultureInfo.InvariantCulture)}"/>
              <c:y val="{corner.Y.ToString(CultureInfo.InvariantCulture)}"/>
              {(size is { } z
                  ? $"<c:w val=\"{z.W.ToString(CultureInfo.InvariantCulture)}\"/><c:h val=\"{z.H.ToString(CultureInfo.InvariantCulture)}\"/>"
                  : string.Empty)}
            </c:manualLayout></c:layout>
            <c:overlay val="0"/>
            <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>
          </c:legend>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    private static string PlacementProbeChart(
        (double X, double Y)? title = null, (double X, double Y)? legend = null,
        bool overlayTitle = false, bool overlayLegend = false,
        string legendPosition = "r", (double W, double H)? legendSize = null) => $"""
        <c:chart>
          <c:title>
            <c:tx><c:rich>
              <a:bodyPr/><a:lstStyle/>
              <a:p><a:pPr><a:defRPr sz="1400" b="1"/></a:pPr><a:r><a:rPr lang="en-GB" sz="1400" b="1"/><a:t>Regional totals</a:t></a:r></a:p>
            </c:rich></c:tx>
            {(title is { } t ? $"<c:layout><c:manualLayout><c:xMode val=\"edge\"/><c:yMode val=\"edge\"/><c:x val=\"{t.X.ToString(CultureInfo.InvariantCulture)}\"/><c:y val=\"{t.Y.ToString(CultureInfo.InvariantCulture)}\"/></c:manualLayout></c:layout>" : "<c:layout/>")}
            <c:overlay val="{(overlayTitle ? "1" : "0")}"/>
          </c:title>
          <c:autoTitleDeleted val="0"/>
          <c:plotArea>
            <c:layout/>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, "Units", ["North", "South", "East", "West"],
                  [30, 45, 20, 55], "4472C4")}
              <c:gapWidth val="150"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:legend>
            <c:legendPos val="{legendPosition}"/>
            {(legend is { } l ? $"<c:layout><c:manualLayout><c:xMode val=\"edge\"/><c:yMode val=\"edge\"/><c:x val=\"{l.X.ToString(CultureInfo.InvariantCulture)}\"/><c:y val=\"{l.Y.ToString(CultureInfo.InvariantCulture)}\"/>{(legendSize is { } z ? $"<c:w val=\"{z.W.ToString(CultureInfo.InvariantCulture)}\"/><c:h val=\"{z.H.ToString(CultureInfo.InvariantCulture)}\"/>" : string.Empty)}</c:manualLayout></c:layout>" : "<c:layout/>")}
            <c:overlay val="{(overlayLegend ? "1" : "0")}"/>
            <c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz="1000"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p></c:txPr>
          </c:legend>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    /// <summary>
    /// A chart whose titles state a weight, or state none, for measuring what Word defaults to.
    /// </summary>
    /// <remarks>
    /// Nothing here is placed by hand: the titles sit where the chart puts them, so the whole of
    /// what varies between the pages is the weight and what it does to a title's width. A bold
    /// title is wider, and because the chart's own title is centred, wider means it also starts
    /// further left — which is what makes a weight measurable from a text position at all.
    /// </remarks>
    private static string TitleWeightProbeChart(string? weight, string? axisWeight) => $"""
        <c:chart>
          <c:title>
            <c:tx><c:rich>
              <a:bodyPr/><a:lstStyle/>
              <a:p><a:pPr><a:defRPr sz="1400"{(weight is null ? string.Empty : $" b=\"{weight}\"")}/></a:pPr>
                <a:r><a:rPr lang="en-GB" sz="1400"{(weight is null ? string.Empty : $" b=\"{weight}\"")}/><a:t>Regional totals</a:t></a:r>
              </a:p>
            </c:rich></c:tx>
            <c:layout/>
            <c:overlay val="0"/>
          </c:title>
          <c:autoTitleDeleted val="0"/>
          <c:plotArea>
            <c:layout/>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, "Units", ["North", "South", "East", "West"],
                  [30, 45, 20, 55], "4472C4")}
              <c:gapWidth val="150"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:title>
                <c:tx><c:rich>
                  <a:bodyPr/><a:lstStyle/>
                  <a:p><a:pPr><a:defRPr sz="1000"{(axisWeight is null ? string.Empty : $" b=\"{axisWeight}\"")}/></a:pPr>
                    <a:r><a:rPr lang="en-GB" sz="1000"{(axisWeight is null ? string.Empty : $" b=\"{axisWeight}\"")}/><a:t>Region</a:t></a:r>
                  </a:p>
                </c:rich></c:tx>
                <c:layout/>
                <c:overlay val="0"/>
              </c:title>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling><c:orientation val="minMax"/><c:max val="60"/><c:min val="0"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    private static string ColumnChart() => $"""
        <c:chart>
          <c:autoTitleDeleted val="1"/>
          <c:plotArea>
            <c:layout>
              <c:manualLayout>
                <c:layoutTarget val="inner"/>
                <c:xMode val="edge"/><c:yMode val="edge"/>
                <c:x val="0.2"/><c:y val="0.1"/>
                <c:w val="0.7"/><c:h val="0.7"/>
              </c:manualLayout>
            </c:layout>
            <c:barChart>
              <c:barDir val="col"/>
              <c:grouping val="clustered"/>
              <c:varyColors val="0"/>
              {DocxBuilder.ChartSeries(0, "Units", ["North", "South", "East", "West"],
                  [30, 45, 20, 55], "4472C4")}
              <c:gapWidth val="150"/>
              <c:overlap val="-27"/>
              <c:axId val="111111111"/>
              <c:axId val="222222222"/>
            </c:barChart>
            <c:catAx>
              <c:axId val="111111111"/>
              <c:scaling><c:orientation val="minMax"/></c:scaling>
              <c:delete val="0"/>
              <c:axPos val="b"/>
              <c:crossAx val="222222222"/>
              <c:crosses val="autoZero"/>
              <c:auto val="1"/>
              <c:lblAlgn val="ctr"/>
              <c:lblOffset val="100"/>
              <c:noMultiLvlLbl val="0"/>
            </c:catAx>
            <c:valAx>
              <c:axId val="222222222"/>
              <c:scaling>
                <c:orientation val="minMax"/>
                <c:max val="60"/>
                <c:min val="0"/>
              </c:scaling>
              <c:delete val="0"/>
              <c:axPos val="l"/>
              <c:majorGridlines/>
              <c:numFmt formatCode="General" sourceLinked="1"/>
              <c:majorTickMark val="none"/>
              <c:minorTickMark val="none"/>
              <c:tickLblPos val="nextTo"/>
              <c:crossAx val="111111111"/>
              <c:crosses val="autoZero"/>
              <c:crossBetween val="between"/>
              <c:majorUnit val="20"/>
            </c:valAx>
          </c:plotArea>
          <c:plotVisOnly val="1"/>
          <c:dispBlanksAs val="gap"/>
        </c:chart>
        """;

    /// <summary>
    /// A column chart with the two things a first drawing of one cannot settle: how far a label
    /// sits from the axis it belongs to, and how it is set against the mark it names.
    /// </summary>
    /// <param name="tickMark">Whether the axes carry marks, which the labels may have to clear.</param>
    /// <param name="labelSize">
    /// The type the labels are set in, in hundredths of a point, so that a gap fixed in points can
    /// be told from one that is a share of the type.
    /// </param>
    /// <param name="labelOffset">
    /// How far the categories sit below their axis, as a percentage of something the format does
    /// not say.
    /// </param>
    private static string AxisProbeChart(string tickMark, int labelSize, int labelOffset = 100)
    {
        var text = $"""
            <c:txPr><a:bodyPr/><a:lstStyle/>
              <a:p><a:pPr><a:defRPr sz="{labelSize}"/></a:pPr><a:endParaRPr lang="en-GB"/></a:p>
            </c:txPr>
            """;

        return $"""
            <c:chart>
              <c:autoTitleDeleted val="1"/>
              <c:plotArea>
                <c:layout><c:manualLayout>
                  <c:layoutTarget val="inner"/>
                  <c:xMode val="edge"/><c:yMode val="edge"/>
                  <c:x val="0.25"/><c:y val="0.1"/><c:w val="0.65"/><c:h val="0.65"/>
                </c:manualLayout></c:layout>
                <c:barChart>
                  <c:barDir val="col"/>
                  <c:grouping val="clustered"/>
                  <c:varyColors val="0"/>
                  {DocxBuilder.ChartSeries(0, "Units", ["One", "Two"], [40, 80], "4472C4")}
                  <c:gapWidth val="150"/>
                  <c:axId val="111111111"/><c:axId val="222222222"/>
                </c:barChart>
                <c:catAx>
                  <c:axId val="111111111"/>
                  <c:scaling><c:orientation val="minMax"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="b"/>
                  <c:majorTickMark val="{tickMark}"/>
                  <c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  {text}
                  <c:crossAx val="222222222"/><c:crosses val="autoZero"/>
                  <c:auto val="1"/><c:lblAlgn val="ctr"/>
                  <c:lblOffset val="{labelOffset}"/>
                  <c:noMultiLvlLbl val="0"/>
                </c:catAx>
                <c:valAx>
                  <c:axId val="222222222"/>
                  <c:scaling><c:orientation val="minMax"/><c:max val="100"/><c:min val="0"/></c:scaling>
                  <c:delete val="0"/><c:axPos val="l"/>
                  <c:majorGridlines/>
                  <c:numFmt formatCode="General" sourceLinked="1"/>
                  <c:majorTickMark val="{tickMark}"/>
                  <c:minorTickMark val="none"/>
                  <c:tickLblPos val="nextTo"/>
                  {text}
                  <c:crossAx val="111111111"/><c:crosses val="autoZero"/>
                  <c:crossBetween val="between"/>
                  <c:majorUnit val="50"/>
                </c:valAx>
              </c:plotArea>
              <c:plotVisOnly val="1"/>
            </c:chart>
            """;
    }

    /// <summary>Every fixture, keyed by the name its golden file and reference PDF share.</summary>
    public static IReadOnlyDictionary<string, Func<DocxBuilder>> All { get; } =
        new Dictionary<string, Func<DocxBuilder>>(StringComparer.Ordinal)
        {
            // The simplest possible document: one short line at the default margins. If this
            // ever moves, everything else has moved too.
            ["single-line"] = () => new DocxBuilder()
                .AddParagraph("The quick brown fox jumps over the lazy dog.", runProperties: Times12),

            // Non-default margins, isolating page geometry from everything else.
            ["margins-half-inch"] = () => new DocxBuilder()
                .WithPage(left: 720, right: 720, top: 720, bottom: 720)
                .AddParagraph("Half-inch margins on every side.", runProperties: Times12),

            ["page-a4"] = () => new DocxBuilder()
                .WithPage(widthTwips: 11906, heightTwips: 16838)
                .AddParagraph("A4 page geometry.", runProperties: Times12),

            // Word wrap driven purely by measured glyph widths.
            ["wrapping"] = () => new DocxBuilder()
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 12)),
                    runProperties: Times12),

            ["alignment"] = () => new DocxBuilder()
                .AddParagraph("Left aligned paragraph.", "<w:jc w:val=\"left\"/>", Times12)
                .AddParagraph("Centered paragraph.", "<w:jc w:val=\"center\"/>", Times12)
                .AddParagraph("Right aligned paragraph.", "<w:jc w:val=\"right\"/>", Times12)
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("Justified text stretches to both margins.", 6)),
                    "<w:jc w:val=\"both\"/>", Times12),

            ["character-formatting"] = () => new DocxBuilder()
                .AddParagraphWithRuns([
                    ("Regular, ", Times12),
                    ("bold, ", Times(bold: true)),
                    ("italic, ", Times(italic: true)),
                    ("bold italic, ", Times(bold: true, italic: true)),
                    ("underlined, ", Times(underline: "single")),
                    ("struck through, ", Times(strike: true)),
                    ("red.", Times(color: "CC0000"))
                ]),

            // What a highlight covers. Word paints a rectangle behind the run and writes nothing
            // else about it, so every question here is a question about ink: which colour each of
            // the sixteen names is, how far the box reaches above and below the baseline and what
            // decides that, and where it starts and stops along the line.
            //
            // Four pages. The names, one to a line, so each fill can be read off on its own. Then
            // the box at four sizes in Times and two in Arial, since a box that follows the run's
            // own face and one that follows the line are the same thing until a line holds two
            // sizes. Then the joins: a space between two highlighted words, highlighted or not,
            // and a highlighted run that wraps, where the trailing space at the break is the
            // question. Then a line whose highlighted run is the shorter of two, which is what
            // separates the run's box from the line's.
            ["highlight-probe"] = () =>
            {
                var builder = new DocxBuilder();

                string[] names =
                [
                    "yellow", "green", "cyan", "magenta", "blue", "red", "darkBlue", "darkCyan",
                    "darkGreen", "darkMagenta", "darkRed", "darkYellow", "darkGray", "lightGray",
                    "black", "white"
                ];

                foreach (var name in names)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times(24, highlight: name)}</w:rPr>" +
                        $"<w:t>{name}</w:t></w:r></w:p>");
                }

                // The box against the type: one word highlighted between two that are not, so the
                // baseline is read off the same line the box is measured on.
                var first = true;
                void Sized(string face, int halfPoints)
                {
                    var plain = DocxBuilder.RunProperties(font: face, halfPoints: halfPoints);
                    var lit = DocxBuilder.RunProperties(
                        font: face, halfPoints: halfPoints, highlight: "yellow");

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(first ? ZeroSpacingNewPage : ZeroSpacing)}</w:pPr>" +
                        $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\">ab </w:t></w:r>" +
                        $"<w:r><w:rPr>{lit}</w:rPr><w:t>lit</w:t></w:r>" +
                        $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\"> cd</w:t></w:r></w:p>");

                    first = false;
                }

                Sized(TimesNewRoman, 16);
                Sized(TimesNewRoman, 24);
                Sized(TimesNewRoman, 48);
                Sized(TimesNewRoman, 96);
                Sized("Arial", 24);
                Sized("Arial", 48);

                // The joins. The space between two highlighted words decides whether a highlight
                // is one box or two, and a run that wraps decides what happens to the space the
                // break falls on.
                var plain12 = Times12;
                var lit12 = Times(24, highlight: "yellow");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">one </w:t></w:r>" +
                    $"<w:r><w:rPr>{lit12}</w:rPr><w:t>two</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> </w:t></w:r>" +
                    $"<w:r><w:rPr>{lit12}</w:rPr><w:t>four</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> five</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">one </w:t></w:r>" +
                    $"<w:r><w:rPr>{lit12}</w:rPr><w:t xml:space=\"preserve\">two three four</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> five</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{lit12}</w:rPr><w:t xml:space=\"preserve\">" +
                    "A highlighted run long enough that it has to be broken, so that what the " +
                    "break does to the space it falls on can be read off the page rather than " +
                    "guessed at.</w:t></w:r></w:p>");

                // A paragraph whose own mark is highlighted, with a short last line: whether the
                // box reaches past the last character is the mark's doing.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}<w:rPr><w:highlight w:val=\"yellow\"/></w:rPr></w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t>Marked paragraph.</w:t></w:r></w:p>");

                // Two sizes on one line, the highlighted one the smaller: a box that follows the
                // line would be as tall as the thirty-six point run beside it.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                    $"<w:r><w:rPr>{lit12}</w:rPr><w:t xml:space=\"preserve\">small </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(72)}</w:rPr><w:t>TALL</w:t></w:r></w:p>");

                // And the other way about, the highlighted run the taller of the two.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">small </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(72, highlight: "yellow")}</w:rPr><w:t>TALL</w:t></w:r></w:p>");

                return builder;
            },

            // What a shaded paragraph covers. Four pages, one question each: how far the fill
            // reaches above the first line and below the last, whether the indents move its edges,
            // whether two shaded paragraphs in a row are one box or two, and what a pattern —
            // pct25 and the rest — makes of the two colours it is given.
            ["paragraph-shading-probe"] = () =>
            {
                var builder = new DocxBuilder();

                string Shade(string fill, string pattern = "clear", string color = "auto") =>
                    $"<w:shd w:val=\"{pattern}\" w:color=\"{color}\" w:fill=\"{fill}\"/>";

                void Rail(string text) => builder.AddParagraph(text, ZeroSpacing, Times12);

                // Page one: the vertical extent, against rails above and below. The spacing is
                // where the question is — a fill that covers the room a paragraph asks for before
                // and after it looks nothing like one that covers its lines.
                Rail("Rail above the first.");
                builder.AddParagraph("One shaded line, no spacing.",
                    Shade("FFF2CC") + ZeroSpacing, Times12);
                Rail("Rail between.");
                builder.AddParagraph("One shaded line with twelve points before and after it.",
                    Shade("FFF2CC") + "<w:spacing w:before=\"240\" w:after=\"240\"/>", Times12);
                Rail("Rail between again.");
                builder.AddParagraph(
                    "A shaded paragraph long enough to take three lines of the measure, so that "
                    + "the fill has an inside as well as a top and a bottom, and a last line that "
                    + "stops short of the right margin.",
                    Shade("FFF2CC") + ZeroSpacing, Times12);
                Rail("Rail below.");

                // Page two: the horizontal extent. An indent moves the text; whether it moves the
                // fill with it is the whole question, and the centred paragraph asks whether the
                // fill follows the text or the measure.
                builder.AddParagraph("Indents.", ZeroSpacingNewPage, Times12);
                builder.AddParagraph("Half an inch in from the left.",
                    Shade("DEEBF7") + ZeroSpacing + "<w:ind w:left=\"720\"/>", Times12);
                builder.AddParagraph("Half an inch in from the right.",
                    Shade("DEEBF7") + ZeroSpacing + "<w:ind w:right=\"720\"/>", Times12);
                builder.AddParagraph("First line indented, the rest not.",
                    Shade("DEEBF7") + ZeroSpacing + "<w:ind w:firstLine=\"720\"/>", Times12);
                builder.AddParagraph("Centred, and shorter than the measure.",
                    Shade("DEEBF7") + ZeroSpacing + "<w:jc w:val=\"center\"/>", Times12);

                // Page three: two in a row, the same fill and then a different one.
                builder.AddParagraph("Two in a row.", ZeroSpacingNewPage, Times12);
                builder.AddParagraph("The first of two, shaded.", Shade("E2EFDA") + ZeroSpacing, Times12);
                builder.AddParagraph("The second of two, the same.", Shade("E2EFDA") + ZeroSpacing, Times12);
                builder.AddParagraph("The third, a different fill.", Shade("FCE4D6") + ZeroSpacing, Times12);
                Rail("Rail below the three.");

                // Page four: the patterns. A pattern is two colours and a share, and what Word
                // does with the pair is not written down anywhere but the page.
                builder.AddParagraph("Patterns.", ZeroSpacingNewPage, Times12);
                foreach (var pattern in new[] { "pct10", "pct25", "pct50", "pct75", "solid" })
                {
                    builder.AddParagraph($"Pattern {pattern}, red on yellow.",
                        Shade("FFFF00", pattern, "FF0000") + ZeroSpacing, Times12);
                }

                return builder;
            },

            // What a run's own background covers, which is a different question from what a
            // highlight covers even though both are a rectangle behind a run. The pages mirror
            // highlight-probe's so the two can be read side by side: the box against four sizes
            // and two faces, the joins, a line of two sizes, the patterns, and then the two cases
            // only a run's background has — a run inside a shaded paragraph, and a run carrying a
            // background and a highlight at once.
            ["run-shading-probe"] = () =>
            {
                var builder = new DocxBuilder();

                string Shaded(string face, int halfPoints, string fill = "FFF2CC") =>
                    DocxBuilder.RunProperties(
                        font: face, halfPoints: halfPoints, shadingFill: fill);

                var first = true;
                void Sized(string face, int halfPoints)
                {
                    var plain = DocxBuilder.RunProperties(font: face, halfPoints: halfPoints);

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacing)}</w:pPr>" +
                        $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\">ab </w:t></w:r>" +
                        $"<w:r><w:rPr>{Shaded(face, halfPoints)}</w:rPr><w:t>lit</w:t></w:r>" +
                        $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\"> cd</w:t></w:r></w:p>");

                    first = false;
                }

                Sized(TimesNewRoman, 16);
                Sized(TimesNewRoman, 24);
                Sized(TimesNewRoman, 48);
                Sized(TimesNewRoman, 96);
                Sized("Arial", 24);
                Sized("Arial", 48);

                // The joins, exactly as highlight-probe asks them.
                var plain12 = Times12;
                var shaded12 = Times(24, shadingFill: "FFF2CC");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">one </w:t></w:r>" +
                    $"<w:r><w:rPr>{shaded12}</w:rPr><w:t>two</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> </w:t></w:r>" +
                    $"<w:r><w:rPr>{shaded12}</w:rPr><w:t>four</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> five</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">one </w:t></w:r>" +
                    $"<w:r><w:rPr>{shaded12}</w:rPr><w:t xml:space=\"preserve\">two three four</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> five</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{shaded12}</w:rPr><w:t xml:space=\"preserve\">" +
                    "A run with a background of its own, long enough that it has to be broken, so " +
                    "that what the break does to the space it falls on can be read off the page " +
                    "rather than guessed at.</w:t></w:r></w:p>");

                // A paragraph whose own mark carries the background, and a short last line.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}<w:rPr>" +
                    "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFF2CC\"/></w:rPr></w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t>Marked paragraph.</w:t></w:r></w:p>");

                // Two sizes on one line, each way about.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                    $"<w:r><w:rPr>{shaded12}</w:rPr><w:t xml:space=\"preserve\">small </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(72)}</w:rPr><w:t>TALL</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">small </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(72, shadingFill: "FFF2CC")}</w:rPr><w:t>TALL</w:t></w:r></w:p>");

                // The patterns, red on yellow, so that the blend can be compared against the one
                // a paragraph's background makes of the same pair.
                var pattern = true;
                foreach (var name in new[] { "pct10", "pct25", "pct50", "pct75", "solid" })
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(pattern ? ZeroSpacingNewPage : ZeroSpacing)}</w:pPr>" +
                        $"<w:r><w:rPr>{Times(24, shadingFill: "FFFF00", shadingPattern: name, shadingColor: "FF0000")}</w:rPr>" +
                        $"<w:t>Pattern {name}, red on yellow.</w:t></w:r></w:p>");

                    pattern = false;
                }

                // A run's background inside a paragraph's, which says which is drawn over which
                // and whether the run's reaches as far as the paragraph's does.
                builder.AddRawParagraph(
                    "<w:p><w:pPr><w:pageBreakBefore/>" +
                    "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"DEEBF7\"/>" +
                    $"{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">A paragraph shaded blue with </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(24, shadingFill: "FCE4D6")}</w:rPr><w:t>a run shaded orange</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\"> inside it.</w:t></w:r></w:p>");

                // And a run carrying both a background and a highlight, which is the only case
                // where the two rules meet.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t xml:space=\"preserve\">Both at once: </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(24, highlight: "yellow", shadingFill: "FCE4D6")}</w:rPr>" +
                    "<w:t>shaded and highlighted</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                return builder;
            },

            // What a cell makes of a pattern, which until now it made nothing of: a cell took its
            // fill and ignored the rest, where a paragraph and a run both blend the two colours
            // they are given. Two tables. The first asks every pattern of a cell directly — the
            // shares, a texture, and the three ways of saying "none" — and the second asks where a
            // pattern may be written: on the table as a whole, on a cell of it, and on a cell that
            // turns the table's off again.
            ["cell-shading-probe"] = () =>
            {
                string Cell(string shading, string text) =>
                    $"<w:tc><w:tcPr><w:tcW w:w=\"1560\" w:type=\"dxa\"/>{shading}</w:tcPr>" +
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times(16)}</w:rPr>" +
                    $"<w:t>{DocxBuilder.Escape(text)}</w:t></w:r></w:p></w:tc>";

                string Shd(string? pattern, string fill = "FFFF00", string color = "FF0000") =>
                    pattern is null
                        ? string.Empty
                        : $"<w:shd w:val=\"{pattern}\" w:color=\"{color}\" w:fill=\"{fill}\"/>";

                string Row(params string[] cells) => $"<w:tr>{string.Join(string.Empty, cells)}</w:tr>";

                // A fixed layout with a grid stated outright, so that what the columns are is not
                // a question this probe is asking: it is asking what colour each cell comes out.
                string Table(string properties, int columns, params string[] rows) =>
                    $"<w:tbl><w:tblPr><w:tblW w:w=\"{columns * 1560}\" w:type=\"dxa\"/>{properties}" +
                    "<w:tblLayout w:type=\"fixed\"/></w:tblPr><w:tblGrid>" +
                    string.Concat(Enumerable.Repeat("<w:gridCol w:w=\"1560\"/>", columns)) +
                    "</w:tblGrid>" + string.Join(string.Empty, rows) + "</w:tbl>";

                var plain = Times12;

                var builder = new DocxBuilder()
                    .AddParagraph("Patterns in a cell.", ZeroSpacing, Times12);

                // The shares, red on yellow, so the blend can be read against the one a paragraph
                // makes of the same pair. pct12 is there for the names that mean a half — an
                // eighth rather than a tenth — and pct5 for the smallest share there is.
                builder.AddRawParagraph(Table(string.Empty, 6,
                    Row(
                        Cell(Shd("pct5"), "5"),
                        Cell(Shd("pct10"), "10"),
                        Cell(Shd("pct12"), "12"),
                        Cell(Shd("pct25"), "25"),
                        Cell(Shd("pct50"), "50"),
                        Cell(Shd("pct75"), "75")),
                    Row(
                        Cell(Shd("solid"), "solid"),
                        Cell(Shd("clear"), "clear"),
                        Cell(Shd("nil"), "nil"),
                        Cell(Shd("clear", fill: "auto"), "auto"),
                        Cell(Shd("pct50", fill: "auto"), "auto 50"),
                        Cell(Shd("horzStripe"), "stripe"))));

                // The same question of a paragraph and of a run, since a cell is not the only
                // thing that can be given a pattern over a fill of nothing in particular.
                builder.AddParagraph("A paragraph, clear over an automatic fill.",
                    "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"auto\"/>" + ZeroSpacing, Times12);

                builder.AddParagraph("A paragraph, half red over an automatic fill.",
                    "<w:shd w:val=\"pct50\" w:color=\"FF0000\" w:fill=\"auto\"/>" + ZeroSpacing, Times12);

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\">A run, </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(24, shadingFill: "auto", shadingPattern: "pct50", shadingColor: "FF0000")}</w:rPr>" +
                    "<w:t>half red over an automatic fill</w:t></w:r>" +
                    $"<w:r><w:rPr>{plain}</w:rPr><w:t>.</w:t></w:r></w:p>");

                builder.AddParagraph("Where a pattern may be written.",
                    "<w:spacing w:before=\"240\" w:after=\"120\"/>", Times12);

                // A pattern on the whole table, a cell with one of its own, and a cell that turns
                // the table's off — which is what an automatic fill means where something above
                // has given one.
                builder.AddRawParagraph(Table(Shd("pct25", "FFFF00", "0070C0"), 4,
                    Row(
                        Cell(string.Empty, "table"),
                        Cell(Shd("pct50", "FFFF00", "FF0000"), "own"),
                        Cell(Shd("clear", fill: "auto"), "auto"),
                        Cell(Shd("solid", "FFFF00", "00B050"), "solid"))));

                return builder;
            },

            // What a declared cell width does to a table left on autofit. Word treats it as the
            // width the column would *like*, which is a different thing from the width it gets:
            // content that will not fit in it, a row that asks for something else, or a table
            // whose columns add up to more than the measure all move it. Nothing here was read
            // off the format — every page is one table and one question.
            //
            //   1  widths that fit, and content narrower than them
            //   2  a column whose content will not fit the width it asks for
            //   3  widths adding up to more than the measure
            //   4  some columns asking and others saying nothing
            //   5  two rows asking for different widths in the same column
            //
            // Two neighbouring questions are deliberately not here, having been measured and left
            // alone: a width asked for as a share of the table (`w:type="pct"`) inside a table of
            // no stated width, which Word answers with neither the share nor the content but
            // something between the two; and a table stating its own width whose cells state
            // none, where Word divides the surplus by a rule no reading of one page pins down.
            // Both are in the backlog with the numbers.
            //
            // Each cell is shaded a colour of its own, so the columns can be read straight off
            // the page as rectangles rather than inferred from where the text landed.
            ["table-width-probe"] = () =>
            {
                string[] fills = ["DEEBF7", "FCE4D6", "E2EFDA", "FFF2CC"];

                string Cell(string? width, string text, int index, string type = "dxa")
                {
                    var declared = width is null
                        ? string.Empty
                        : $"<w:tcW w:w=\"{width}\" w:type=\"{type}\"/>";

                    return $"<w:tc><w:tcPr>{declared}" +
                           $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fills[index % 4]}\"/>" +
                           $"</w:tcPr><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                           $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{DocxBuilder.Escape(text)}</w:t></w:r>" +
                           "</w:p></w:tc>";
                }

                string Table(string tableWidth, params string[] rows) =>
                    "<w:tbl><w:tblPr>" + tableWidth +
                    "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/>" +
                    "<w:top w:w=\"0\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>" +
                    "</w:tblPr>" + string.Join(string.Empty, rows) + "</w:tbl>";

                string Row(params string[] cells) => $"<w:tr>{string.Join(string.Empty, cells)}</w:tr>";

                const string Auto = "<w:tblW w:w=\"0\" w:type=\"auto\"/>";

                var builder = new DocxBuilder();
                var first = true;

                void Page(string label, string table)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    builder.AddRawParagraph(table);
                    first = false;
                }

                // Seventy-two, a hundred and eight and a hundred and forty-four points, well
                // inside the measure, holding a letter each.
                Page("Widths that fit.", Table(Auto,
                    Row(Cell("1440", "a", 0), Cell("2160", "b", 1), Cell("2880", "c", 2))));

                // Thirty-six points a column, and the middle one holding a word four times that
                // wide with nowhere to break it.
                Page("A column that cannot hold what it asks for.", Table(Auto,
                    Row(Cell("720", "a", 0), Cell("720", "Antidisestablishmentarianism", 1),
                        Cell("720", "c", 2))));

                // Two hundred points a column against a measure of four hundred and sixty-eight.
                Page("Widths adding up to more than the measure.", Table(Auto,
                    Row(Cell("4000", "a", 0), Cell("4000", "b", 1), Cell("4000", "c", 2))));

                // The middle column says nothing and holds more than the others.
                Page("Some columns asking, one saying nothing.", Table(Auto,
                    Row(Cell("1440", "a", 0), Cell(null, "a word or two here", 1), Cell("1440", "c", 2))));

                // The rows disagree: the first asks for seventy-two and a hundred and forty-four,
                // the second for the other way about.
                Page("Two rows asking for different widths.", Table(Auto,
                    Row(Cell("1440", "a", 0), Cell("2880", "b", 1)),
                    Row(Cell("2880", "c", 2), Cell("1440", "d", 3))));

                return builder;
            },

            // What a table's own stated width does, which is a different question from what a
            // cell's does: the table says how wide the whole is to be and says nothing about how
            // to divide it. Seven pages, each one table with every cell shaded a colour of its
            // own so the columns are read off the page as rectangles.
            //
            //   1  a width to divide between three columns of nearly equal content
            //   2  the same width between columns of very unequal content
            //   3  and again, with three widths that differ but none of them long
            //   4  a width narrower than the content wants
            //   5  a width stated as a share of the measure
            //   6  a width with the cells stating widths of their own as well
            //   7  a width wider than the page allows
            ["table-preferred-width-probe"] = () =>
            {
                string[] fills = ["DEEBF7", "FCE4D6", "E2EFDA", "FFF2CC"];

                string Cell(string text, int index, string? width = null)
                {
                    var declared = width is null
                        ? string.Empty
                        : $"<w:tcW w:w=\"{width}\" w:type=\"dxa\"/>";

                    return $"<w:tc><w:tcPr>{declared}" +
                           $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fills[index % 4]}\"/>" +
                           $"</w:tcPr><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                           $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{DocxBuilder.Escape(text)}</w:t></w:r>" +
                           "</w:p></w:tc>";
                }

                string Table(string width, params string[] cells) =>
                    "<w:tbl><w:tblPr>" + width +
                    "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/>" +
                    "<w:top w:w=\"0\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>" +
                    "</w:tblPr><w:tr>" + string.Join(string.Empty, cells) + "</w:tr></w:tbl>";

                // Three hundred and twenty-four points, which is well inside the measure.
                const string Stated = "<w:tblW w:w=\"6480\" w:type=\"dxa\"/>";

                var builder = new DocxBuilder();
                var first = true;

                void Page(string label, string table)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    builder.AddRawParagraph(table);
                    first = false;
                }

                Page("Nearly equal content.",
                    Table(Stated, Cell("a", 0), Cell("b", 1), Cell("c", 2)));

                Page("Very unequal content.",
                    Table(Stated, Cell("a", 0),
                        Cell("a column holding a good deal more than the others do", 1),
                        Cell("c", 2)));

                Page("Three widths that differ.",
                    Table(Stated, Cell("iiii", 0), Cell("MMMM", 1), Cell("wwwwwwww", 2)));

                Page("Narrower than the content wants.",
                    Table("<w:tblW w:w=\"2880\" w:type=\"dxa\"/>",
                        Cell("a column holding rather more than its share", 0),
                        Cell("and another beside it", 1)));

                Page("A share of the measure.",
                    Table("<w:tblW w:w=\"2500\" w:type=\"pct\"/>",
                        Cell("a", 0), Cell("b", 1), Cell("c", 2)));

                Page("The cells stating widths too.",
                    Table(Stated, Cell("a", 0, "1440"), Cell("b", 1, "1440"), Cell("c", 2, "1440")));

                Page("Wider than the page allows.",
                    Table("<w:tblW w:w=\"14400\" w:type=\"dxa\"/>",
                        Cell("a", 0), Cell("b", 1), Cell("c", 2)));

                return builder;
            },

            // A cell width asked for as a share rather than a measurement: w:tcW w:type="pct", in
            // fiftieths of a percent. What the share is a share *of* is the whole question, and it
            // depends on what the table says about its own width — a table that states one has a
            // number to take shares of, and a table left to its contents has not.
            //
            //   1  shares of a table stating its width in points
            //   2  shares of a table stating its width as a share of the measure
            //   3  shares of a table left to its contents, holding a letter each
            //   4  the same, with one cell holding a good deal more
            //   5  shares adding up to less than the whole
            //   6  shares adding up to more than the whole
            //   7  a share beside a measurement and beside a cell that asks for nothing
            ["cell-percent-width-probe"] = () =>
            {
                string[] fills = ["DEEBF7", "FCE4D6", "E2EFDA", "FFF2CC"];

                string Cell(string text, int index, string? width, string type = "pct") =>
                    "<w:tc><w:tcPr>" +
                    (width is null ? string.Empty : $"<w:tcW w:w=\"{width}\" w:type=\"{type}\"/>") +
                    $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fills[index % 4]}\"/>" +
                    $"</w:tcPr><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{DocxBuilder.Escape(text)}</w:t></w:r>" +
                    "</w:p></w:tc>";

                string Table(string width, params string[] cells) =>
                    "<w:tbl><w:tblPr>" + width +
                    "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/>" +
                    "<w:top w:w=\"0\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>" +
                    "</w:tblPr><w:tr>" + string.Join(string.Empty, cells) + "</w:tr></w:tbl>";

                const string Stated = "<w:tblW w:w=\"6480\" w:type=\"dxa\"/>";
                const string Whole = "<w:tblW w:w=\"5000\" w:type=\"pct\"/>";
                const string Auto = "<w:tblW w:w=\"0\" w:type=\"auto\"/>";

                var builder = new DocxBuilder();
                var first = true;

                void Page(string label, string table)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    builder.AddRawParagraph(table);
                    first = false;
                }

                // A quarter, a half and a quarter of three hundred and twenty-four points.
                Page("Shares of a stated width.", Table(Stated,
                    Cell("a", 0, "1250"), Cell("b", 1, "2500"), Cell("c", 2, "1250")));

                // The same shares of a table that is itself the whole measure.
                Page("Shares of the whole measure.", Table(Whole,
                    Cell("a", 0, "1250"), Cell("b", 1, "2500"), Cell("c", 2, "1250")));

                // And of a table that states nothing, so there is nothing to take a share of but
                // what the cells hold.
                Page("Shares of a table left to its contents.", Table(Auto,
                    Cell("a", 0, "1250"), Cell("b", 1, "2500"), Cell("c", 2, "1250")));

                Page("The same, one cell holding more.", Table(Auto,
                    Cell("a", 0, "1250"),
                    Cell("a column holding a good deal more than the others", 1, "2500"),
                    Cell("c", 2, "1250")));

                // Half the table between two cells, and then half again as much as there is.
                Page("Shares adding up to less than the whole.", Table(Stated,
                    Cell("a", 0, "1250"), Cell("b", 1, "1250")));

                Page("Shares adding up to more than the whole.", Table(Stated,
                    Cell("a", 0, "3750"), Cell("b", 1, "3750")));

                // A share, a measurement and a cell that asks for nothing, side by side.
                Page("A share beside a measurement.", Table(Stated,
                    Cell("a", 0, "2500"), Cell("b", 1, "1440", "dxa"), Cell("c", 2, null)));

                return builder;
            },

            // Where a column's edge falls, as against where the arithmetic puts it. Word writes
            // on a grid of a three-hundredth of an inch and a column width is rarely a whole
            // number of those, so something has to give: either every column is put on the grid
            // and the table ends up a fraction short of what it asked for, or the edges are put
            // there and the widths take what is left between them.
            //
            // Six pages, each a table whose cells are shaded so that every edge is ink, and every
            // page chosen so the exact answer falls between two steps of the grid:
            //
            //   1  three declared widths of fifty points, which is 208⅓ steps
            //   2  three declared widths that are all awkward, and all different
            //   3  no declarations at all: three columns sized by a letter each
            //   4  a stated grid and a fixed layout, so nothing is computed at all
            //   5  declared widths too wide for the measure, so all three are scaled
            //   6  two declared widths whose halves fall the other side of a step
            ["column-grid-probe"] = () =>
            {
                string[] fills = ["DEEBF7", "FCE4D6", "E2EFDA", "FFF2CC"];

                string Cell(string text, int index, int? twips) =>
                    "<w:tc><w:tcPr>" +
                    (twips is null ? string.Empty : $"<w:tcW w:w=\"{twips}\" w:type=\"dxa\"/>") +
                    $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fills[index % 4]}\"/>" +
                    $"</w:tcPr><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{DocxBuilder.Escape(text)}</w:t></w:r>" +
                    "</w:p></w:tc>";

                string Table(string properties, string grid, params string[] cells) =>
                    "<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/>" + properties +
                    "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/>" +
                    "<w:top w:w=\"0\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>" +
                    "</w:tblPr>" + grid + "<w:tr>" + string.Join(string.Empty, cells) + "</w:tr></w:tbl>";

                var builder = new DocxBuilder();
                var first = true;

                void Page(string label, string table)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    builder.AddRawParagraph(table);
                    first = false;
                }

                // Fifty points is 208 steps and a third of one.
                Page("Fifty points a column.", Table(string.Empty, string.Empty,
                    Cell("a", 0, 1000), Cell("b", 1, 1000), Cell("c", 2, 1000)));

                // 25.85, 41.15 and 65.05 points: none of them a whole step, and each falling a
                // different distance from one.
                Page("Three awkward widths.", Table(string.Empty, string.Empty,
                    Cell("a", 0, 517), Cell("b", 1, 823), Cell("c", 2, 1301)));

                // Nothing declared: the columns are as wide as a letter each, and a letter is
                // never a whole number of steps wide.
                Page("Sized by their contents.", Table(string.Empty, string.Empty,
                    Cell("i", 0, null), Cell("M", 1, null), Cell("w", 2, null)));

                // A grid stated outright and a layout that takes it as it stands.
                Page("A stated grid.", Table("<w:tblLayout w:type=\"fixed\"/>",
                    "<w:tblGrid><w:gridCol w:w=\"1000\"/><w:gridCol w:w=\"1000\"/>" +
                    "<w:gridCol w:w=\"1000\"/></w:tblGrid>",
                    Cell("a", 0, 1000), Cell("b", 1, 1000), Cell("c", 2, 1000)));

                // Three of two hundred and a fraction, against a measure of 468: every column is
                // scaled, and the scale is nothing like a whole step.
                Page("Too wide for the measure.", Table(string.Empty, string.Empty,
                    Cell("a", 0, 4001), Cell("b", 1, 4001), Cell("c", 2, 4001)));

                // 50.05 points apiece, which is 208.54 steps: the half falls the other side.
                Page("Halves the other way.", Table(string.Empty, string.Empty,
                    Cell("a", 0, 1001), Cell("b", 1, 1001)));

                return builder;
            },

            // How wide Word thinks a piece of text is, measured directly rather than inferred from
            // where a column edge landed. Every line here is set against the right margin, so the
            // place it begins is the margin less the width Word measured — and the same string is
            // repeated one, five, ten and forty times over, so that the rounding of a single
            // measurement is divided by forty and what is left is the rule.
            //
            // The letters are chosen for what they are in the font's own units at twelve point:
            // 'a' is 444 thousandths of an em, which is 5.328 points and 106.56 twips, so a
            // measurement quantised to the twip would show; 'b' is 500, which is 6 points and 120
            // twips exactly, so it would not. 'i' and 'M' are the narrowest and widest of the
            // ordinary letters. If Word rounds each glyph to something coarser than the point it
            // draws at, forty 'a's will say so and forty 'b's will stay silent.
            ["text-measure-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Line(string face, int halfPoints, string text, int times)
                {
                    var properties = DocxBuilder.RunProperties(font: face, halfPoints: halfPoints);

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacing)}" +
                        "<w:jc w:val=\"right\"/></w:pPr>" +
                        $"<w:r><w:rPr>{properties}</w:rPr>" +
                        $"<w:t>{string.Concat(Enumerable.Repeat(text, times))}</w:t></w:r></w:p>");

                    first = false;
                }

                // The longest run on each page is chosen so that even a line of the widest letter
                // stays inside the measure: a line that wrapped would be measuring the wrap
                // instead of the text.
                void Page(string face, int halfPoints, int longest, string label)
                {
                    builder.AddParagraph($"{label}.", first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    first = false;

                    foreach (var text in new[] { "a", "b", "i", "M", "iM" })
                    {
                        foreach (var times in new[] { 1, 5, 10, longest })
                        {
                            // Six tenths of an em a letter is a generous guess at the width, and
                            // what it is guarding against is a line long enough to be broken:
                            // these strings hold no spaces, so a line that did not fit would be
                            // measuring what Word does with a word too long for the measure.
                            if (text.Length * times * halfPoints * 0.3 > 440) continue;

                            Line(face, halfPoints, text, times);
                        }
                    }
                }

                Page(TimesNewRoman, 24, 40, "Times at twelve point");
                Page(TimesNewRoman, 22, 40, "Times at eleven point");
                Page(TimesNewRoman, 27, 30, "Times at thirteen and a half");
                Page("Arial", 24, 40, "Arial at twelve point");

                return builder;
            },

            // How far a word may overrun the measure before Word breaks it. That there is any
            // slack at all is something table-width-probe showed by accident: Word left a word of
            // 142.65 points in a column of 142.56 rather than breaking it, which is nine
            // hundredths of a point of overhang it was content with.
            //
            // The instrument is a right indent, which moves the measure a twip at a time — a
            // twentieth of a point, five times finer than the grid Word writes on. Every paragraph
            // holds the same ten capital Ms, which is 106.6992 points of Times at twelve and has
            // nowhere to break, so a paragraph that comes out as two lines is a paragraph whose
            // word Word gave up on. The sweep runs from a measure a third of a point wider than
            // the word to one three quarters of a point narrower.
            //
            // The second page asks the same of a table cell, where the column is put on the grid
            // first and the answer can only be read to a quarter point — but it is the case the
            // question came from.
            ["break-tolerance-probe"] = () =>
            {
                var builder = new DocxBuilder();

                const string Word = "MMMMMMMMMM";

                void Line(int rightTwips, bool newPage = false)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(newPage ? ZeroSpacingNewPage : ZeroSpacing)}" +
                        $"<w:ind w:right=\"{rightTwips}\"/></w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{Word}</w:t></w:r></w:p>");
                }

                // 7226 twips of indent leaves 106.7 points, which is a thousandth of a point more
                // than the word; every twip after that takes another twentieth off.
                var first = true;
                foreach (var indent in new[]
                         {
                             7220, 7224, 7226, 7227, 7228, 7229, 7230, 7231, 7232, 7233, 7234,
                             7236, 7238, 7240, 7245
                         })
                {
                    Line(indent, first);
                    first = false;
                }

                // The same in a cell, at the four grid steps around the word's own width. A fixed
                // layout so that the column is the width it is told and nothing else.
                string Cell(int twips) =>
                    "<w:tbl><w:tblPr><w:tblW w:w=\"" + twips + "\" w:type=\"dxa\"/>" +
                    "<w:tblLayout w:type=\"fixed\"/>" +
                    "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"0\" w:type=\"dxa\"/>" +
                    "<w:top w:w=\"0\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar>" +
                    $"</w:tblPr><w:tblGrid><w:gridCol w:w=\"{twips}\"/></w:tblGrid>" +
                    $"<w:tr><w:tc><w:tcPr><w:tcW w:w=\"{twips}\" w:type=\"dxa\"/>" +
                    "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"DEEBF7\"/></w:tcPr>" +
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                    $"<w:t>{Word}</w:t></w:r></w:p></w:tc></w:tr></w:tbl>";

                builder.AddParagraph("In a cell.", ZeroSpacingNewPage, Times12);

                // 2136 twips is 106.8 points, a step above the word; each of the rest is a step
                // further down, so the last is nearly three quarters of a point short of it.
                foreach (var twips in new[] { 2136, 2131, 2126, 2121, 2116, 2111 })
                {
                    builder.AddRawParagraph(Cell(twips));
                    builder.AddParagraph("-", ZeroSpacing, Times12);
                }

                return builder;
            },

            // The division of a stated width, asked in the one shape that can answer it. Two
            // columns leave one edge between them, so where that edge falls is the whole of what
            // Word decided: the share the first column got. Sweeping the width of the table moves
            // that edge across the grid, and each width says the share lies in a window a step
            // wide divided by the width — so thirty-six widths together say it far more narrowly
            // than any one of them could.
            //
            // The letters are chosen to tell the candidate rules apart. An 'i' is 66.68 twips of
            // Times at twelve and a 'b' is 120 exactly, so the share is 0.357193 of the pair as
            // they are, 0.358289 if each is rounded up to a whole twip, and 0.359788 if a twip is
            // then added to each. Those are a thousandth apart, which a wide table resolves.
            ["two-column-sweep-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                string Table(int twips, bool reversed = false)
                {
                    string Cell(string text, string? fill) =>
                        "<w:tc><w:tcPr>" +
                        (fill is null ? string.Empty
                            : $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"/>") +
                        $"</w:tcPr><w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r></w:p></w:tc>";

                    var cells = reversed
                        ? Cell("b", "DEEBF7") + Cell("i", null)
                        : Cell("i", "DEEBF7") + Cell("b", null);

                    return $"<w:tbl><w:tblPr><w:tblW w:w=\"{twips}\" w:type=\"dxa\"/>" +
                           "<w:tblCellMar><w:left w:w=\"0\" w:type=\"dxa\"/>" +
                           "<w:right w:w=\"0\" w:type=\"dxa\"/><w:top w:w=\"0\" w:type=\"dxa\"/>" +
                           "<w:bottom w:w=\"0\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>" +
                           $"<w:tr>{cells}</w:tr></w:tbl>";
                }

                void Add(int twips, bool reversed = false)
                {
                    builder.AddParagraph(first ? "Widths." : "-",
                        first ? ZeroSpacing : ZeroSpacing, Times12);
                    builder.AddRawParagraph(Table(twips, reversed));
                    first = false;
                }

                // Thirty-six widths from a hundred points to two hundred and eighty-seven and a
                // half, two and a half points apart, which walks the edge round the grid many
                // times over. Twelve to a page, since each table and its rail take 28 points.
                var count = 0;
                foreach (var twips in Enumerable.Range(0, 36).Select(i => 2000 + i * 50))
                {
                    if (count > 0 && count % 12 == 0)
                        builder.AddParagraph("More.", ZeroSpacingNewPage, Times12);

                    Add(twips);
                    count++;
                }

                // And four with the columns the other way about, which should put the same edge
                // the other side of the table.
                builder.AddParagraph("Reversed.", ZeroSpacingNewPage, Times12);
                foreach (var twips in new[] { 2000, 2400, 2800, 3200 }) Add(twips, reversed: true);

                return builder;
            },

            // The box round a paragraph, which is drawn and nothing else: every question about it
            // is a question about where the ink is. Seven pages, each asking one:
            //
            //   1  where the four sides stand against the text, on one line and on three
            //   2  what the space of each side does, at nothing, four points, twelve and
            //      thirty-one, which is as much as the format allows
            //   3  what the weight does, from a quarter point to six
            //   4  whether the indents move it, and whether centring the text does
            //   5  what two and three paragraphs of the same border in a row make — one box or
            //      several — and what a declared border between them changes
            //   6  how it stands against the paragraph's own background, which reaches a fiftieth
            //      of an inch past the text
            //   7  one side on its own: the rule under a heading, and the bar down the margin
            ["paragraph-border-probe"] = () =>
            {
                string Edge(string name, int size = 8, int space = 0, string colour = "000000") =>
                    $"<w:{name} w:val=\"single\" w:sz=\"{size}\" w:space=\"{space}\" w:color=\"{colour}\"/>";

                string Box(int size = 8, int space = 0, bool between = false, bool bar = false,
                    string? sides = null) =>
                    "<w:pBdr>" +
                    string.Concat((sides ?? "top left bottom right").Split(' ',
                            StringSplitOptions.RemoveEmptyEntries)
                        .Select(side => Edge(side, size, space))) +
                    (between ? Edge("between", size, space) : string.Empty) +
                    (bar ? Edge("bar", size, space) : string.Empty) +
                    "</w:pBdr>";

                var builder = new DocxBuilder();
                var first = true;

                void Page(string label)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    first = false;
                }

                void Rail(string text = "-") => builder.AddParagraph(text, ZeroSpacing, Times12);

                void Bordered(string text, string border, string? extra = null) =>
                    builder.AddParagraph(text, border + ZeroSpacing + extra, Times12);

                Page("The box.");
                Rail("Rail above.");
                Bordered("One line inside a box.", Box());
                Rail();
                Bordered(
                    "Three lines inside a box, which needs enough words in it to take three lines "
                    + "of the measure and so to show where the sides of the box run down the page "
                    + "beside them.", Box());
                Rail("Rail below.");

                Page("The space.");
                foreach (var space in new[] { 0, 4, 12, 31 })
                {
                    Bordered($"Space of {space} points.", Box(space: space));
                    Rail();
                }

                Page("The weight.");
                foreach (var size in new[] { 2, 8, 24, 48 })
                {
                    Bordered($"Weight of {size} eighths.", Box(size));
                    Rail();
                }

                Page("Indents and alignment.");
                Bordered("Indented half an inch from the left.", Box(), "<w:ind w:left=\"720\"/>");
                Rail();
                Bordered("Indented half an inch from the right.", Box(), "<w:ind w:right=\"720\"/>");
                Rail();
                Bordered("Centred, and shorter than the measure.", Box(), "<w:jc w:val=\"center\"/>");
                Rail();

                Page("One after another.");
                Bordered("First of three, all bordered alike.", Box());
                Bordered("Second of three.", Box());
                Bordered("Third of three.", Box());
                Rail();
                Bordered("First of two, with a border between them.", Box(between: true));
                Bordered("Second of two.", Box(between: true));
                Rail();

                Page("Against the background.");
                Bordered("Bordered and shaded together.",
                    Box() + "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"DEEBF7\"/>");
                Rail();
                Bordered("Shaded, bordered, and twelve points of space.",
                    Box(space: 12) + "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FCE4D6\"/>");
                Rail();

                Page("One side alone.");
                Bordered("A rule under this line.", Box(sides: "bottom"));
                Rail();
                Bordered("A rule over this one.", Box(sides: "top"));
                Rail();
                Bordered("A bar beside this one.", Box(sides: "", bar: true));
                Rail();

                return builder;
            },

            // The box round a run, from w:bdr, which is a different thing from the box round a
            // paragraph and has to be measured as one. Six pages:
            //
            //   1  where it stands against the run, at four sizes
            //   2  what its weight does, from a quarter point to six
            //   3  what its space does, at nothing, four points and twelve
            //   4  the joins: two bordered runs side by side, two with a plain space between
            //      them, and one long enough to be broken across two lines
            //   5  a bordered run beside a much larger one, which says whether the box follows
            //      the run or the line, as a highlight follows the line
            //   6  a bordered run that is also highlighted, one that is also shaded, and one
            //      inside a paragraph that has a box of its own
            ["run-border-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Line(string before, string inside, string after, string properties)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacing)}</w:pPr>" +
                        (before.Length == 0 ? string.Empty :
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{before}</w:t></w:r>") +
                        $"<w:r><w:rPr>{properties}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(inside)}</w:t></w:r>" +
                        (after.Length == 0 ? string.Empty :
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{after}</w:t></w:r>") +
                        "</w:p>");

                    first = false;
                }

                void Page(string label)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    first = false;
                }

                Page("The box round a run.");
                foreach (var halfPoints in new[] { 16, 24, 48, 96 })
                {
                    var plain = DocxBuilder.RunProperties(font: TimesNewRoman, halfPoints: halfPoints);
                    var boxed = DocxBuilder.RunProperties(
                        font: TimesNewRoman, halfPoints: halfPoints, borderStyle: "single");

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\">ab </w:t></w:r>" +
                        $"<w:r><w:rPr>{boxed}</w:rPr><w:t>lit</w:t></w:r>" +
                        $"<w:r><w:rPr>{plain}</w:rPr><w:t xml:space=\"preserve\"> cd</w:t></w:r></w:p>");
                }

                Page("The weight.");
                foreach (var size in new[] { 2, 8, 24, 48 })
                    Line("ab ", "lit", " cd", Times(24, borderStyle: "single", borderEighths: size));

                Page("The space.");
                foreach (var space in new[] { 0, 4, 12 })
                    Line("ab ", "lit", " cd", Times(24, borderStyle: "single", borderSpace: space));

                Page("The joins.");
                var boxedRun = Times(24, borderStyle: "single");

                // Two bordered runs side by side, and then two with a plain space between them.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">one </w:t></w:r>" +
                    $"<w:r><w:rPr>{boxedRun}</w:rPr><w:t>two</w:t></w:r>" +
                    $"<w:r><w:rPr>{boxedRun}</w:rPr><w:t>three</w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> four</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">one </w:t></w:r>" +
                    $"<w:r><w:rPr>{boxedRun}</w:rPr><w:t>two</w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> </w:t></w:r>" +
                    $"<w:r><w:rPr>{boxedRun}</w:rPr><w:t>four</w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> five</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{boxedRun}</w:rPr><w:t xml:space=\"preserve\">" +
                    "A bordered run long enough that it has to be broken, so that what the break " +
                    "does to the box round it can be read off the page rather than guessed at." +
                    "</w:t></w:r></w:p>");

                Page("Two sizes on one line.");
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{boxedRun}</w:rPr><w:t xml:space=\"preserve\">small </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(72)}</w:rPr><w:t>TALL</w:t></w:r></w:p>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">small </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times(72, borderStyle: "single")}</w:rPr><w:t>TALL</w:t></w:r></w:p>");

                Page("Against the rest.");
                Line("ab ", "lit", " cd", Times(24, highlight: "yellow", borderStyle: "single"));
                Line("ab ", "lit", " cd", Times(24, borderStyle: "single", shadingFill: "DEEBF7"));

                builder.AddParagraph("A bordered run inside a bordered paragraph.",
                    "<w:pBdr><w:top w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"000000\"/>" +
                    "<w:left w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"000000\"/>" +
                    "<w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"000000\"/>" +
                    "<w:right w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"000000\"/></w:pBdr>" +
                    ZeroSpacing, Times12);

                return builder;
            },

            // The marks a run can ask for over its characters, from w:em. Four pages:
            //
            //   1  the four kinds, one to a line, over the same three letters
            //   2  the dot at four sizes, which says what the mark is measured in
            //   3  what gets one: letters, the space between two words, and a stop
            //   4  whether the line grows to hold them, against rails above and below
            ["emphasis-mark-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Page(string label)
                {
                    builder.AddParagraph(label, first ? ZeroSpacing : ZeroSpacingNewPage, Times12);
                    first = false;
                }

                void Marked(string text, string mark, int halfPoints = 24)
                {
                    builder.AddParagraph(text, ZeroSpacing,
                        Times(halfPoints, emphasis: mark));
                }

                Page("The four kinds.");
                foreach (var mark in new[] { "dot", "comma", "circle", "underDot" })
                {
                    builder.AddParagraph("Rail.", ZeroSpacing, Times12);
                    Marked("abc", mark);
                }

                builder.AddParagraph("Rail.", ZeroSpacing, Times12);

                Page("The dot at four sizes.");
                foreach (var halfPoints in new[] { 16, 24, 48, 96 })
                {
                    builder.AddParagraph("Rail.", ZeroSpacing, Times12);
                    Marked("abc", "dot", halfPoints);
                }

                builder.AddParagraph("Rail.", ZeroSpacing, Times12);

                Page("What gets one.");
                Marked("a b", "dot");
                Marked("a,b", "dot");

                // Half the line marked and half not, so that where the marks stop can be seen.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(24, emphasis: "dot")}</w:rPr><w:t>marked</w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>plain</w:t></w:r></w:p>");

                Page("Does the line grow?");
                builder.AddParagraph("Rail above.", ZeroSpacing, Times12);
                Marked("Marked line.", "dot");
                builder.AddParagraph("Rail between.", ZeroSpacing, Times12);
                builder.AddParagraph("Plain line.", ZeroSpacing, Times12);
                builder.AddParagraph("Rail below.", ZeroSpacing, Times12);

                return builder;
            },

            ["font-sizes"] = () => new DocxBuilder()
                .AddParagraph("Twenty-four point heading.", runProperties: Times24)
                .AddParagraph("Twelve point body text.", runProperties: Times12)
                .AddParagraph("Eight point small print.", runProperties: Times(16)),

            ["indents"] = () => new DocxBuilder()
                .AddParagraph("No indent.", runProperties: Times12)
                .AddParagraph("Left indented half an inch.", "<w:ind w:left=\"720\"/>", Times12)
                .AddParagraph("First line indented.", "<w:ind w:firstLine=\"720\"/>", Times12)
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("Hanging indent wraps under itself.", 5)),
                    "<w:ind w:left=\"720\" w:hanging=\"360\"/>", Times12),

            ["paragraph-spacing"] = () => new DocxBuilder()
                .AddParagraph("Twelve points after.", "<w:spacing w:after=\"240\"/>", Times12)
                .AddParagraph("Twelve points before.", "<w:spacing w:before=\"240\" w:after=\"0\"/>", Times12)
                .AddParagraph("No spacing.", "<w:spacing w:before=\"0\" w:after=\"0\"/>", Times12),

            // Asymmetric space-before/space-after, chosen so that the four candidate models for
            // how Word combines them all predict different gaps and exactly one can survive:
            //   gap 1 (after 12, before 24) -> sum 36, max 24, after-only 12, before-only 24
            //   gap 2 (after 24, before 12) -> sum 36, max 24, after-only 24, before-only 12
            ["paragraph-spacing-asymmetric"] = () => new DocxBuilder()
                .AddParagraph("First paragraph, twelve points after.",
                    "<w:spacing w:before=\"0\" w:after=\"240\"/>", Times12)
                .AddParagraph("Second paragraph, twenty-four before and after.",
                    "<w:spacing w:before=\"480\" w:after=\"480\"/>", Times12)
                .AddParagraph("Third paragraph, twelve points before.",
                    "<w:spacing w:before=\"240\" w:after=\"0\"/>", Times12)
                .AddParagraph("Fourth paragraph, no spacing at all.",
                    "<w:spacing w:before=\"0\" w:after=\"0\"/>", Times12),

            // Each paragraph starts at the top of its own page, so its first baseline is measured
            // from a known origin and reveals the line-spacing rule directly, without the height
            // of anything above it confusing the result. The three multiples discriminate between
            // the candidate models for where a multiple's extra leading goes.
            ["line-spacing-multiples"] = () => new DocxBuilder()
                .AddParagraph("Single spaced first line.",
                    "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>", Times12)
                .AddParagraph("Double spaced first line.",
                    "<w:pageBreakBefore/><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"480\" w:lineRule=\"auto\"/>",
                    Times12)
                .AddParagraph("One and a half spaced first line.",
                    "<w:pageBreakBefore/><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"360\" w:lineRule=\"auto\"/>",
                    Times12),

            ["line-spacing"] = () => new DocxBuilder()
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("Single spaced text.", 8)),
                    "<w:spacing w:line=\"240\" w:lineRule=\"auto\" w:after=\"0\"/>", Times12)
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("Double spaced text.", 8)),
                    "<w:spacing w:line=\"480\" w:lineRule=\"auto\" w:after=\"0\"/>", Times12),

            ["tabs"] = () => new DocxBuilder()
                .AddRawParagraph(
                    $"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>A</w:t><w:tab/><w:t>B</w:t><w:tab/><w:t>C</w:t></w:r></w:p>")
                .AddRawParagraph($"""
                                  <w:p>
                                    <w:pPr><w:tabs><w:tab w:val="left" w:pos="2880"/><w:tab w:val="left" w:pos="5760"/></w:tabs></w:pPr>
                                    <w:r><w:rPr>{Times12}</w:rPr><w:t>A</w:t><w:tab/><w:t>B</w:t><w:tab/><w:t>C</w:t></w:r>
                                  </w:p>
                                  """),

            // Centre, right and decimal stops, which unlike a left stop cannot be resolved until
            // the text after them has been measured. The last two rows are the awkward cases: a
            // run with no separator for the decimal stop to line up, and one too wide to fit
            // before the stop it was aimed at.
            ["tabs-aligned"] = () =>
            {
                const string stops =
                    "<w:tabs>" +
                    "<w:tab w:val=\"center\" w:pos=\"2880\"/>" +
                    "<w:tab w:val=\"decimal\" w:pos=\"5040\"/>" +
                    "<w:tab w:val=\"right\" w:pos=\"9360\"/>" +
                    "</w:tabs>";

                var builder = new DocxBuilder();

                foreach (var (left, centre, figure, right) in new[]
                         {
                             ("Opening", "Centred", "1.5", "Right"),
                             ("Second", "Also centred", "22.75", "Aligned"),
                             ("Third", "A much wider centred run", "333.125", "End"),
                             ("Fourth", "Middle", "Total", "Last"),
                             ("A left run wide enough to overrun the centre stop it was aimed at",
                                 "Centred", "0.5", "Over")
                         })
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{stops}{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{left}</w:t><w:tab/><w:t>{centre}</w:t>" +
                        $"<w:tab/><w:t>{figure}</w:t><w:tab/><w:t>{right}</w:t></w:r></w:p>");
                }

                return builder;
            },

            // Bar stops, which ask for a vertical rule rather than a place for text to land.
            // Whether the rule appears on every line of a paragraph, on one with no tab character
            // in it at all, and on an empty one, and how tall it is against the type, are all read
            // back from Word's export.
            ["tab-bars"] = () =>
            {
                const string oneBar = "<w:tabs><w:tab w:val=\"bar\" w:pos=\"2880\"/></w:tabs>";
                const string twoBars =
                    "<w:tabs><w:tab w:val=\"bar\" w:pos=\"1440\"/><w:tab w:val=\"bar\" w:pos=\"5760\"/></w:tabs>";

                return new DocxBuilder()
                    // No tab character anywhere in it, and long enough to run to three lines.
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{oneBar}{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t>A paragraph with a bar stop declared on it and no tab character of " +
                        "its own, written long enough to run to three lines so that what happens " +
                        "on each of them can be seen.</w:t></w:r></w:p>")
                    // Twice the type size, to see what the rule is measured against.
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{oneBar}{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times24}</w:rPr>" +
                        "<w:t>Larger type</w:t></w:r></w:p>")
                    // Two of them, with a tab that has to pass through both.
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{twoBars}{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t>Two bars</w:t><w:tab/><w:t>after the tab</w:t></w:r></w:p>")
                    // An empty paragraph, which has a line box but nothing on it.
                    .AddRawParagraph($"<w:p><w:pPr>{oneBar}{ZeroSpacing}</w:pPr></w:p>")
                    .AddParagraph("A plain paragraph with no bar of its own.", ZeroSpacing, Times12);
            },

            // Kerning, which a document has to ask for and which Word only applies from the type
            // size it names upwards. The same text is set four ways so that the pairs can be
            // measured against each other as well as against Word.
            ["kerning"] = () =>
            {
                const string pairs = "AV AW To Ta Wa Yo LT P. F, Yes, Watch AVATAR";

                var builder = new DocxBuilder()
                    // Asked for, from eight point up: kerned.
                    .AddParagraph(pairs, ZeroSpacing, Times(kerningHalfPoints: 16))
                    // Not asked for at all: not kerned, however many pairs it holds.
                    .AddParagraph(pairs, ZeroSpacing, Times12)
                    // Asked for from twenty-four point up, at twelve: too small to kern.
                    .AddParagraph(pairs, ZeroSpacing, Times(kerningHalfPoints: 48))
                    // The same threshold at twenty-four point: kerned.
                    .AddParagraph(pairs, ZeroSpacing, Times(48, kerningHalfPoints: 48));

                // Calibri carries no legacy kern table, so anything that moves here came out of
                // GPOS and nowhere else.
                var calibri = DocxBuilder.RunProperties(
                    font: "Calibri", halfPoints: 24, kerningHalfPoints: 16);

                return builder
                    .AddParagraph(pairs, ZeroSpacing, calibri)
                    .AddParagraph(pairs, ZeroSpacing, DocxBuilder.RunProperties(font: "Calibri", halfPoints: 24));
            },

            // Every kind of leader Word offers, on a right stop at the margin — which is what a
            // table of contents is made of — plus one on a centre stop and one on a left stop.
            ["tab-leaders"] = () =>
            {
                var builder = new DocxBuilder();

                foreach (var (leader, label) in new[]
                         {
                             ("dot", "Dotted"), ("hyphen", "Hyphenated"), ("underscore", "Underscored"),
                             ("middleDot", "Middle dots"), ("heavy", "Heavy")
                         })
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr><w:tabs><w:tab w:val=\"right\" w:leader=\"{leader}\" w:pos=\"9360\"/></w:tabs>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{label}</w:t><w:tab/><w:t>12</w:t></w:r></w:p>");
                }

                builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:tabs><w:tab w:val=\"center\" w:leader=\"dot\" w:pos=\"4680\"/></w:tabs>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Centred</w:t><w:tab/><w:t>Middle</w:t></w:r></w:p>");

                return builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:leader=\"dot\" w:pos=\"4680\"/></w:tabs>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Left</w:t><w:tab/><w:t>After</w:t></w:r></w:p>");
            },

            ["breaks"] = () => new DocxBuilder()
                .AddRawParagraph(
                    $"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>Line one</w:t><w:br/><w:t>Line two</w:t></w:r></w:p>")
                .AddRawParagraph(
                    $"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>Before the page break</w:t><w:br w:type=\"page\"/><w:t>After the page break</w:t></w:r></w:p>"),

            // Enough content to force pagination, checking that flow across pages is stable.
            ["multi-page"] = () =>
            {
                var builder = new DocxBuilder();
                for (var i = 1; i <= 80; i++)
                    builder.AddParagraph($"Paragraph number {i} of eighty.", runProperties: Times12);
                return builder;
            },

            // Three ways of producing an identical 20pt bold paragraph with 12pt before and 6pt
            // after, each on its own page and each followed by ordinary body text. If Word gives
            // the same gap for all three, the divergence is in how we model a size change across
            // a paragraph boundary; if the first differs, Word is recognising "heading 1" as one
            // of its built-in styles and merging its own definition into ours.
            ["heading-spacing-probe"] = () => new DocxBuilder()
                .WithStyles(ProbeStyles)
                .AddParagraph("Heading one built in name", "<w:pStyle w:val=\"Heading1\"/>")
                .AddParagraph("Body after heading one.")
                .AddParagraph("Heading two built in name",
                    "<w:pStyle w:val=\"Heading2\"/><w:pageBreakBefore/>")
                .AddParagraph("Body after heading two.")
                .AddParagraph("Custom style same properties",
                    "<w:pStyle w:val=\"CustomBig\"/><w:pageBreakBefore/>")
                .AddParagraph("Body after the custom style.")
                .AddParagraph("Direct formatting no style",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"120\"/>",
                    Times(40, bold: true))
                .AddParagraph("Body after the direct formatting."),

            // Solves the line box directly. With all spacing at zero, the gap between two
            // baselines is exactly the first line's descent plus the second line's ascent, and
            // the first baseline on each page is exactly that page's first ascent. Three pages
            // of size pairs — 20 then 12, 12 then 12, 20 then 20 — give enough equations to
            // recover every ascent and descent Word is using, with no spacing term in the way.
            ["line-box-probe"] = () => new DocxBuilder()
                .WithStyles(ProbeStyles)
                .AddParagraph("Twenty point first line.", ZeroSpacing, Times(40))
                .AddParagraph("Twelve point second line.", ZeroSpacing, Times(24))
                .AddParagraph("Twelve point first line.", ZeroSpacingNewPage, Times(24))
                .AddParagraph("Twelve point second line.", ZeroSpacing, Times(24))
                .AddParagraph("Twenty point first line.", ZeroSpacingNewPage, Times(40))
                .AddParagraph("Twenty point second line.", ZeroSpacing, Times(40)),

            // Separates the two surviving explanations for the space-before divergence at the top
            // of a page. Every paragraph is 20pt bold on its own page; only the space-before value
            // and the paragraph mark's size vary.
            //   If Word applies space-before partially, pages 1 and 2 differ from each other.
            //   If Word suppresses it and the line's ascent comes from the paragraph mark instead,
            //   pages 1 and 2 agree and page 3 — whose mark is explicitly 20pt — sits lower.
            ["page-break-spacing-probe"] = () => new DocxBuilder()
                .WithStyles(ProbeStyles)
                .AddParagraph("Control no space before",
                    "<w:spacing w:before=\"0\" w:after=\"0\"/>", Times(40, bold: true))
                .AddParagraph("Twelve points before",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"0\"/>", Times(40, bold: true))
                .AddParagraph("Twenty four points before",
                    "<w:pageBreakBefore/><w:spacing w:before=\"480\" w:after=\"0\"/>", Times(40, bold: true))
                .AddParagraph("Twelve before and a twenty point mark",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"0\"/>"
                    + "<w:rPr><w:b/><w:sz w:val=\"40\"/></w:rPr>", Times(40, bold: true)),

            // Isolates the last unexplained divergence. Four identical 20pt bold paragraphs, each
            // declaring 12pt before, each reached by a page break so that the document's first
            // page cannot be a variable — a filler paragraph occupies page 0 for that reason.
            // Only two things vary, independently: whether space-after is non-zero, and whether
            // a paragraph follows on the same page.
            //   space-after is the cause      -> B and D shift, A and C do not
            //   the following paragraph is    -> C and D shift, A and B do not
            //   only the combination is       -> D alone shifts
            ["space-after-interaction-probe"] = () => new DocxBuilder()
                .WithStyles(ProbeStyles)
                .AddParagraph("Filler on the first page.", ZeroSpacing, Times12)
                .AddParagraph("A after zero alone",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"0\"/>", Times(40, bold: true))
                .AddParagraph("B after twelve alone",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"240\"/>", Times(40, bold: true))
                .AddParagraph("C after zero then a paragraph",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"0\"/>", Times(40, bold: true))
                .AddParagraph("Body following C.", ZeroSpacing, Times12)
                .AddParagraph("D after twelve then a paragraph",
                    "<w:pageBreakBefore/><w:spacing w:before=\"240\" w:after=\"240\"/>", Times(40, bold: true))
                .AddParagraph("Body following D.", ZeroSpacing, Times12),

            // Tests directly whether Word substitutes its own built-in definition for properties a
            // document's style leaves unstated. The two fixtures are identical apart from one
            // thing: whether Normal declares its spacing. Our engine treats "unstated" and
            // "explicitly zero" the same, so if Word does too the pair will measure identically.
            // If instead the empty one spreads out, Word is supplying its template's values and
            // the difference is exactly what it supplies.
            ["builtin-normal-empty"] = () => new DocxBuilder()
                .WithStyles(BuiltInNormalProbeStyles(normalSpacing: null))
                .AddParagraph("First paragraph with nothing stated.")
                .AddParagraph("Second paragraph with nothing stated.")
                .AddParagraph("Third paragraph with nothing stated."),

            ["builtin-normal-explicit"] = () => new DocxBuilder()
                .WithStyles(BuiltInNormalProbeStyles(
                    normalSpacing: "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>"))
                .AddParagraph("First paragraph with nothing stated.")
                .AddParagraph("Second paragraph with nothing stated.")
                .AddParagraph("Third paragraph with nothing stated."),

            // Separates the two values Word supplies. The attributes of w:spacing are merged
            // independently, so stating one leaves the other open for Word to fill in.
            //   line stated, spacing left open -> the gap reveals Word's space-after
            //   spacing stated, line left open -> the gap reveals Word's line multiple
            ["builtin-normal-line-only"] = () => new DocxBuilder()
                .WithStyles(BuiltInNormalProbeStyles(
                    normalSpacing: "<w:spacing w:line=\"240\" w:lineRule=\"auto\"/>"))
                .AddParagraph("First paragraph with nothing stated.")
                .AddParagraph("Second paragraph with nothing stated.")
                .AddParagraph("Third paragraph with nothing stated."),

            ["builtin-normal-spacing-only"] = () => new DocxBuilder()
                .WithStyles(BuiltInNormalProbeStyles(
                    normalSpacing: "<w:spacing w:before=\"0\" w:after=\"0\"/>"))
                .AddParagraph("First paragraph with nothing stated.")
                .AddParagraph("Second paragraph with nothing stated.")
                .AddParagraph("Third paragraph with nothing stated."),

            // A three-column table with explicit grid widths, borders on every edge, a spanned
            // cell and a wrapping cell, surrounded by ordinary paragraphs so that flow into and
            // out of the table is measurable too.
            ["tables"] = () => new DocxBuilder()
                .AddParagraph("Paragraph before the table.", ZeroSpacing, Times12)
                .AddRawParagraph($"""
                                  <w:tbl>
                                    <w:tblPr>
                                      <w:tblW w:w="9360" w:type="dxa"/>
                                      <w:tblBorders>
                                        <w:top w:val="single" w:sz="4" w:color="000000"/>
                                        <w:left w:val="single" w:sz="4" w:color="000000"/>
                                        <w:bottom w:val="single" w:sz="4" w:color="000000"/>
                                        <w:right w:val="single" w:sz="4" w:color="000000"/>
                                        <w:insideH w:val="single" w:sz="4" w:color="000000"/>
                                        <w:insideV w:val="single" w:sz="4" w:color="000000"/>
                                      </w:tblBorders>
                                      <w:tblLayout w:type="fixed"/>
                                    </w:tblPr>
                                    <w:tblGrid>
                                      <w:gridCol w:w="3120"/><w:gridCol w:w="3120"/><w:gridCol w:w="3120"/>
                                    </w:tblGrid>
                                    <w:tr>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>Region</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>Units</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>Revenue</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                    <w:tr>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>North</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>1240</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>48200</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                    <w:tr>
                                      <w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>South</w:t></w:r></w:p></w:tc>
                                      <w:tc>
                                        <w:tcPr><w:gridSpan w:val="2"/></w:tcPr>
                                        <w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t xml:space="preserve">A spanned cell whose text is long enough that it has to wrap onto more than one line.</w:t></w:r></w:p>
                                      </w:tc>
                                    </w:tr>
                                  </w:tbl>
                                  """)
                .AddParagraph("Paragraph after the table.", ZeroSpacing, Times12),

            // Two single-cell tables differing only in their left cell margin, each on its own
            // page. Word places table text 4.92pt left of where we do, which is close enough to
            // the 5.4pt default cell margin to suggest Word shifts a table left by that margin so
            // its cell text lines up with body text.
            //   If it does, the text sits at the left margin in both cases.
            //   If it does not, the second table's text sits 10.8pt further right than the first.
            ["table-indent-probe"] = () => new DocxBuilder()
                .AddRawParagraph($"""
                                  <w:tbl>
                                    <w:tblPr>
                                      <w:tblW w:w="4680" w:type="dxa"/>
                                      <w:tblLayout w:type="fixed"/>
                                      <w:tblCellMar>
                                        <w:left w:w="0" w:type="dxa"/><w:right w:w="0" w:type="dxa"/>
                                      </w:tblCellMar>
                                    </w:tblPr>
                                    <w:tblGrid><w:gridCol w:w="4680"/></w:tblGrid>
                                    <w:tr><w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Zero left cell margin</w:t></w:r></w:p></w:tc></w:tr>
                                  </w:tbl>
                                  """)
                .AddParagraph("Body text for reference.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph($"""
                                  <w:tbl>
                                    <w:tblPr>
                                      <w:tblW w:w="4680" w:type="dxa"/>
                                      <w:tblLayout w:type="fixed"/>
                                      <w:tblCellMar>
                                        <w:left w:w="216" w:type="dxa"/><w:right w:w="216" w:type="dxa"/>
                                      </w:tblCellMar>
                                    </w:tblPr>
                                    <w:tblGrid><w:gridCol w:w="4680"/></w:tblGrid>
                                    <w:tr><w:tc><w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Wide left cell margin</w:t></w:r></w:p></w:tc></w:tr>
                                  </w:tbl>
                                  """),

            // Measures Word's autofit column sizing. No borders and no cell margins, so cell text
            // begins exactly at its column's left edge; the second row of each table is
            // right-aligned so its text ends exactly at the right edge. Between them the two rows
            // give every column boundary directly.
            //   page 1  three very different content widths, no grid   -> is sizing content-based?
            //   page 2  the same content with an equal-width grid      -> is the grid honoured?
            //   page 3  content too wide to fit                        -> how is the excess shared?
            ["table-autofit-probe"] = () => new DocxBuilder()
                .AddRawParagraph(AutofitTable(grid: null,
                    ["A", "BBBBBBBBBB", "CCCCCCCCCCCCCCCCCCCC"]))
                .AddParagraph("Reference paragraph.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(AutofitTable(grid: [3120, 3120, 3120],
                    ["A", "BBBBBBBBBB", "CCCCCCCCCCCCCCCCCCCC"]))
                .AddParagraph("Reference paragraph.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(AutofitTable(grid: null,
                [
                    "Short",
                    "This cell holds a great deal more text than the others do, far more than can fit on one line at this size.",
                    "Mid length here"
                ])),

            // Isolates how Word insets table cell content. Each page holds one single-cell table
            // varying only the table indent, the cell margin and the border; the cell's text sits
            // at the content edge, so its x reveals the total inset directly. 12pt values are used
            // so the effects are far larger than Word's quantisation.
            //
            //   A  nothing            expect 72                additive and content-edge agree
            //   B  border only        additive 72.5
            //   C  indent only        additive 84
            //   D  indent + margin    additive 96,  content-edge 84   <- the discriminator
            //   E  all three          additive 96.5, content-edge 84
            ["table-inset-probe"] = () => new DocxBuilder()
                .AddRawParagraph(InsetTable("A", null, 0, borders: false))
                .AddParagraph("-", ZeroSpacing, Times12)
                .AddRawParagraph(InsetTable("B", null, 0, borders: true))
                .AddParagraph("-", ZeroSpacing, Times12)
                .AddRawParagraph(InsetTable("C", 240, 0, borders: false))
                .AddParagraph("-", ZeroSpacing, Times12)
                .AddRawParagraph(InsetTable("D", 240, 240, borders: false))
                .AddParagraph("-", ZeroSpacing, Times12)
                .AddRawParagraph(InsetTable("E", 240, 240, borders: true))
                .AddParagraph("Trailing.", ZeroSpacing, Times12),

            // A table that says almost nothing about itself and takes everything from its style:
            // the ruling on every edge, the paragraph spacing inside its cells, and the cell
            // margins TableNormal has carried since Word 97. Nothing here declares a border, so
            // an unstyled table comes out with none of them — which is what every table in every
            // real Word document did before this fixture existed.
            ["table-style"] = () => new DocxBuilder()
                .WithExtraStyles(GridTableStyles)
                .AddParagraph("Paragraph before the table.", ZeroSpacing, Times12)
                .AddRawParagraph($"""
                                  <w:tbl>
                                    <w:tblPr>
                                      <w:tblStyle w:val="TableGrid"/>
                                      <w:tblW w:w="9360" w:type="dxa"/>
                                      <w:tblLayout w:type="fixed"/>
                                      <w:tblLook w:val="04A0" w:firstRow="1" w:lastRow="0" w:firstColumn="1"
                                                 w:lastColumn="0" w:noHBand="0" w:noVBand="1"/>
                                    </w:tblPr>
                                    <w:tblGrid>
                                      <w:gridCol w:w="3120"/><w:gridCol w:w="3120"/><w:gridCol w:w="3120"/>
                                    </w:tblGrid>
                                    <w:tr>
                                      <w:tc><w:p><w:r><w:rPr>{Times(bold: true)}</w:rPr><w:t>Region</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times(bold: true)}</w:rPr><w:t>Units</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times(bold: true)}</w:rPr><w:t>Revenue</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                    <w:tr>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>North</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>1240</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>48200</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                    <w:tr>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>South</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>980</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>37110</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                  </w:tbl>
                                  """)
                .AddParagraph("Paragraph after the table.", ZeroSpacing, Times12),

            // Which conditional format wins where, measured rather than read. Every one of the
            // twelve sets a different type size, so the size Word draws each cell at names the
            // format that reached it, and the whole precedence order comes off one page.
            //
            //   page 1  every format in force        -> the order, and how the bands are counted
            //   page 2  none of them in force        -> what tblLook actually gates
            //   page 3  bands two rows and two wide  -> what a band size counts, and from where
            //   page 4  no banding down the columns  -> the row banding, which page 1 hides
            //   page 5  a style with no corners      -> a first row against a first column
            //   page 6  one row, and one column      -> whether the only one is first or last
            //   page 7  against the rest of the cascade
            ["table-style-conditional-probe"] = () => new DocxBuilder()
                .WithExtraStyles(GridTableStyles + ProbeStyleXml() + EdgeStyleXml() + CellParagraphStyle)
                .AddRawParagraph(ProbeTable(LookEverything, rows: 5, columns: 4))
                .AddParagraph("Nothing in force below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(ProbeTable(LookNothing, rows: 5, columns: 4))
                .AddParagraph("Bands of two below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(ProbeTable(LookEverything, rows: 6, columns: 4, bandSize: 2))
                .AddParagraph("No banding down the columns below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(ProbeTable(LookNoVerticalBands, rows: 5, columns: 4))
                .AddParagraph("No corner formats below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(ProbeTable(LookEverything, rows: 5, columns: 4, styleId: "EdgeTable"))
                .AddParagraph("One row, then one column, below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(ProbeTable(LookEverything, rows: 1, columns: 4))
                .AddParagraph("-", ZeroSpacing, Times12)
                .AddRawParagraph(ProbeTable(LookEverything, rows: 4, columns: 1))
                .AddParagraph("The cascade below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph(CascadeTable()),

            // Shapes: a text box in the line, a text box the text flows around, and four shapes
            // with nothing in them at all. What a shape holds is a document of its own — its own
            // paragraphs, laid out into a box that is not the page's — and what it is drawn with
            // is its geometry, its fill and its outline.
            ["shapes"] = () => new DocxBuilder()
                .AddParagraph("Paragraph before the box.", ZeroSpacing, Times12)
                .AddRawParagraph("<w:p><w:pPr>" + ZeroSpacing + "</w:pPr>" +
                                 DocxBuilder.InlineShape(216, 72,
                                     content: ShapeText("A box in the line of text.") +
                                              ShapeText("And a second paragraph in it."),
                                     fillHex: "FFFFFF", lineHex: "000000") + "</w:p>")
                .AddParagraph("Paragraph after the box.", ZeroSpacing, Times12)
                .AddRawParagraph("<w:p><w:pPr>" + ZeroSpacingNewPage + "</w:pPr>" +
                                 DocxBuilder.AnchoredShape(144, 90,
                                     content: ShapeText("A box the text goes round."),
                                     alignX: "left", offsetYPoints: 0, wrap: "square",
                                     fillHex: "FFFFFF", lineHex: "000000") +
                                 $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">" +
                                 Escape(string.Join(' ', Enumerable.Repeat(
                                     "Text runs alongside the box and closes up under it again.", 12))) +
                                 "</w:t></w:r></w:p>")
                .AddParagraph("Shapes with nothing in them below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph("<w:p><w:pPr>" + ZeroSpacing + "</w:pPr>" +
                                 DocxBuilder.InlineShape(108, 54, geometry: "rect",
                                     fillHex: "C0D8F0", lineHex: "1F4E79", lineWidthPoints: 2, id: 101) +
                                 DocxBuilder.InlineShape(108, 54, geometry: "roundRect",
                                     fillHex: "F0D8C0", lineHex: "7F4E19", lineWidthPoints: 1, id: 102) +
                                 "</w:p>")
                .AddRawParagraph("<w:p><w:pPr>" + ZeroSpacing + "</w:pPr>" +
                                 DocxBuilder.InlineShape(108, 54, geometry: "ellipse",
                                     fillHex: "D8F0C0", lineHex: "4E7F19", lineWidthPoints: 1, id: 103) +
                                 DocxBuilder.InlineShape(108, 54, geometry: "rect",
                                     fillHex: null, lineHex: "000000", lineWidthPoints: 3, id: 104) +
                                 "</w:p>")
                .AddParagraph("And one in the theme's own colours.", ZeroSpacing, Times12)
                .AddRawParagraph("<w:p><w:pPr>" + ZeroSpacing + "</w:pPr>" +
                                 DocxBuilder.InlineShape(108, 54, geometry: "rect",
                                     fillHex: "accent1", lineHex: "accent2", lineWidthPoints: 2,
                                     id: 105) +
                                 "</w:p>"),

            // How far inside its own edges a shape sets its text, which is three questions at
            // once: what the default inset is, whether the outline is added to it the way a table
            // cell's border is, and where the text sits in a box taller than it needs.
            //
            //   page 1  Word's own insets, a fine outline
            //   page 2  no insets at all, the same outline   -> is the outline part of the inset?
            //   page 3  no insets, a six point outline       -> the discriminator
            //   page 4  Word's insets, anchored centre
            //   page 5  Word's insets, anchored bottom
            ["shape-inset-probe"] = () => new DocxBuilder()
                .AddRawParagraph(InsetShapePage("A", null, 0.75, "t", first: true))
                .AddRawParagraph(InsetShapePage("B", (0, 0, 0, 0), 0.75, "t"))
                .AddRawParagraph(InsetShapePage("C", (0, 0, 0, 0), 6, "t"))
                .AddRawParagraph(InsetShapePage("D", null, 0.75, "ctr"))
                .AddRawParagraph(InsetShapePage("E", null, 0.75, "b")),

            // The same three things again in the older spelling of a shape: a box in the line, a
            // box the text goes round, and shapes with nothing in them. VML says all of it in a
            // style attribute borrowed from CSS rather than in elements of its own, and names its
            // geometry by the element rather than by an attribute.
            ["vml-shapes"] = () => new DocxBuilder()
                .AddParagraph("Paragraph before the box.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:216pt;height:72pt",
                                     ShapeText("A box in the line of text.") +
                                     ShapeText("And a second paragraph in it.")) +
                                 "</w:p>")
                .AddParagraph("Paragraph after the box.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.VmlShape(
                                     "position:absolute;margin-left:0;margin-top:0;width:144pt;" +
                                     "height:90pt;z-index:251658240;" +
                                     "mso-position-horizontal-relative:column;" +
                                     "mso-position-vertical-relative:paragraph",
                                     ShapeText("A box the text goes round."),
                                     wrap: "<w10:wrap type=\"square\"/>", id: 1027) +
                                 $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">" +
                                 Escape(string.Join(' ', Enumerable.Repeat(
                                     "Text runs alongside the box and closes up under it again.", 12))) +
                                 "</w:t></w:r></w:p>")
                .AddParagraph("Shapes with nothing in them below.", ZeroSpacingNewPage, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:108pt;height:54pt", element: "rect",
                                     fillColor: "#c0d8f0", strokeColor: "#1f4e79",
                                     strokeWeight: "2pt", id: 1028) +
                                 DocxBuilder.VmlShape("width:108pt;height:54pt", element: "roundrect",
                                     fillColor: "#f0d8c0", strokeColor: "#7f4e19", id: 1029) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:108pt;height:54pt", element: "oval",
                                     fillColor: "#d8f0c0", strokeColor: "#4e7f19", id: 1030) +
                                 DocxBuilder.VmlShape("width:108pt;height:54pt", element: "rect",
                                     fillColor: null, strokeColor: "#000000", strokeWeight: "3pt",
                                     id: 1031) +
                                 "</w:p>"),

            // A watermark: a word set across the page behind everything else, which is a shape in
            // the header holding its text on a path rather than in paragraphs. Two pages of body
            // text, so that it is drawn on both.
            ["watermark"] = () => new DocxBuilder()
                .WithHeaderFooter(header: true,
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>{DocxBuilder.Watermark("DRAFT", 527.85, 131.95)}</w:p>")
                .AddParagraph("The first page under the watermark.", ZeroSpacing, Times12)
                .AddParagraph("The second page under it.", ZeroSpacingNewPage, Times12),

            // How large Word sets a word on a path, which is the question a watermark turns on:
            // the size it declares is a single point, standing for "as large as the shape holds".
            //
            //   page 1  DRAFT in a wide box
            //   page 2  the same word in a box half as tall  -> does the height decide?
            //   page 3  a longer word in the first box       -> does the width?
            //   page 4  a short word in the first box
            ["watermark-fit-probe"] = () => new DocxBuilder()
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.Watermark("DRAFT", 400, 100, rotation: null, id: 2060) +
                                 "</w:p>")
                .AddParagraph("DRAFT in four hundred by one hundred.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.Watermark("DRAFT", 400, 50, rotation: null, id: 2061) +
                                 "</w:p>")
                .AddParagraph("The same word in half the height.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.Watermark("CONFIDENTIAL", 400, 100, rotation: null, id: 2062) +
                                 "</w:p>")
                .AddParagraph("A longer word in the first box.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.Watermark("II", 400, 100, rotation: null, id: 2063) +
                                 "</w:p>")
                .AddParagraph("A short one in the first box.", ZeroSpacing, Times12)

                // A word that reaches below the line, a different face, and a narrower box: what
                // the first four pages cannot tell apart is whether the fitting is of the letters
                // themselves or of the box the face would set them in.
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.Watermark("Apply", 400, 100, rotation: null, id: 2064) +
                                 "</w:p>")
                .AddParagraph("A word that reaches below the line.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.Watermark("DRAFT", 400, 100, fontFamily: "Times New Roman",
                                     rotation: null, id: 2065) +
                                 "</w:p>")
                .AddParagraph("The first word in another face.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.Watermark("DRAFT", 200, 100, rotation: null, id: 2066) +
                                 "</w:p>")
                .AddParagraph("And in a narrower box.", ZeroSpacing, Times12),

            // A watermark of a picture rather than a word, which is the other kind Word makes: an
            // image in the header, washed out so the page can be read through it. The picture is
            // flat bands of known colours, so what the washing out did to each can be read off.
            ["watermark-picture"] = () =>
            {
                var builder = new DocxBuilder();
                var image = builder.AddHeaderImage(BandedImage(), "rIdWatermark");

                return builder
                    .WithHeaderFooter(header: true,
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        DocxBuilder.PictureWatermark(image.Id, 288, 144) + "</w:p>",
                        headerImages: [image])
                    .AddParagraph("The first page under the picture.", ZeroSpacing, Times12)
                    .AddParagraph("The second page under it.", ZeroSpacingNewPage, Times12);
            },

            // What washing a picture out actually does, measured rather than assumed: the same
            // bands six times over, varying the two numbers that describe it. The shapes sit in
            // the body rather than in a header, since what is being measured is the transform and
            // not where a watermark goes.
            //
            //   page 1  nothing said            -> what a picture with no washing looks like
            //   page 2  Word's own watermark    -> the pair it writes for every one it makes
            //   page 3  half the gain, no black level
            //   page 4  full gain, half the black level
            //   page 5  Word's gain, no black level
            //   page 6  Word's black level, full gain
            ["watermark-washout-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var image = builder.AddImagePart(BandedImage());

                (string Gain, string Black)[] settings =
                [
                    ("65536f", "0"), ("19661f", "22938f"), ("32768f", "0"),
                    ("65536f", "32768f"), ("19661f", "0"), ("65536f", "22938f")
                ];

                for (var i = 0; i < settings.Length; i++)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(i == 0 ? ZeroSpacing : ZeroSpacingNewPage)}</w:pPr>" +
                        DocxBuilder.PictureWatermark(image, 288, 144,
                            settings[i].Gain, settings[i].Black, id: 2070 + i) +
                        "</w:p>");

                    builder.AddParagraph($"Gain {settings[i].Gain}, black level {settings[i].Black}.",
                        ZeroSpacing, Times12);
                }

                return builder;
            },

            // A chart, with everything about it stated rather than left to Word: where the plot
            // area goes, what the value axis runs between, and how far apart its marks are. What
            // is being measured first is the plotting itself — where a bar of a given value lands
            // in a plot area of a given size — with none of Word's automatic sizing in the way.
            ["chart-column"] = () => new DocxBuilder()
                .WithChart(ColumnChart())
                .AddParagraph("Paragraph before the chart.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216) + "</w:p>")
                .AddParagraph("Paragraph after the chart.", ZeroSpacing, Times12),

            // What Word draws for error bars, one thing to a page. Eight:
            //
            //   page 1  a fixed ten, capped        -> the shaft, and how wide a cap is
            //   page 2  the same, uncapped         -> that noEndCap is the only difference
            //   page 3  a fixed ten, plus only     -> which side a one-way bar reaches
            //   page 4  twenty percent of a point  -> that a share follows the point
            //   page 5  one standard deviation     -> n or n-1, which differ by 15% here
            //   page 6  the standard error         -> and whether the stated value counts
            //   page 7  stated per point           -> plus and minus read separately
            //   page 8  a narrower plot, larger type -> whether the cap follows either
            //
            // Pages 1 and 8 together are what say the cap is a fixed width: page 8 moves the plot
            // and the type around it and the cap is expected not to move with them.
            ["chart-error-bar-probe"] = () => new DocxBuilder()
                .WithChart(ErrorBarProbeChart("fixedVal"))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("fixedVal", caps: false)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("fixedVal", type: "plus")),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("percentage", value: 20)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("stdDev", value: 1)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("stdErr")),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("cust",
                        plus: """<c:pt idx="0"><c:v>5</c:v></c:pt><c:pt idx="1"><c:v>10</c:v></c:pt><c:pt idx="2"><c:v>15</c:v></c:pt><c:pt idx="3"><c:v>20</c:v></c:pt>""",
                        minus: """<c:pt idx="0"><c:v>20</c:v></c:pt><c:pt idx="1"><c:v>15</c:v></c:pt><c:pt idx="2"><c:v>10</c:v></c:pt><c:pt idx="3"><c:v>5</c:v></c:pt>""")),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ErrorBarProbeChart("fixedVal", plotWidth: 0.5, labelSize: 18)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 571) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 572, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 573, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 574, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 575, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 576, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 577, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 578, relationshipId: "rIdChart8") +
                                 "</w:p>"),

            // What Word draws for the lines a chart hangs from its points, one question to a page:
            //
            //   page 1  drop lines, one series             -> the line itself
            //   page 2  drop lines, the scale below nought -> the axis, or the floor of the plot?
            //   page 3  drop lines, two series             -> which point does it hang from?
            //   page 4  high-low lines, two series         -> a line chart, not a stock one
            //   page 5  both together                      -> that neither displaces the other
            ["chart-drop-line-probe"] = () => new DocxBuilder()
                .WithChart(DropLineProbeChart(drop: true, highLow: false))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(DropLineProbeChart(drop: true, highLow: false, minimum: -20)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(DropLineProbeChart(drop: true, highLow: false, two: true)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(DropLineProbeChart(drop: false, highLow: true, two: true)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(DropLineProbeChart(drop: true, highLow: true, two: true)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 581) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 582, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 583, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 584, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 585, relationshipId: "rIdChart5") +
                                 "</w:p>"),

            // Where Word puts a title and a legend that the chart places by hand. Seven pages, the
            // first being the control.
            //
            // The placements are chosen to keep every baseline more than two points clear of the
            // chart's other text. That is not fussiness: the reference check groups runs into
            // lines at a two-point tolerance and then compares the whole document's text in order,
            // so a title landing a point and a half from an axis label merges with it in one file
            // and not in the other over a difference of 0.24 — one step of Word's own grid, and
            // well inside what the position comparison allows. Placing them clear measures the
            // same rule without turning a rounding into a failure.
            //
            //   page 1  neither placed            -> the automatic placement, for comparison
            //   page 2  the title placed          -> what x and y name, and whether the plot moves
            //   page 3  the legend placed         -> the same for a legend, keys and words together
            //   page 4  both placed
            //   page 5  the legend placed and told to overlay -> which of the two frees the room
            //   page 6  the title placed somewhere else  -> a constant offset, or a proportional one
            //   page 7  the legend placed somewhere else -> the same question for a legend
            //
            // A page stating a legend's w and h was written and taken out again: Word moves the
            // legend when it is given a size, and one placement is not enough to say how. See #83.
            ["chart-placement-probe"] = () => new DocxBuilder()
                .WithChart(PlacementProbeChart())
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(PlacementProbeChart(title: (0.05, 0.7))),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(PlacementProbeChart(legend: (0.1, 0.26))),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(PlacementProbeChart(title: (0.05, 0.7), legend: (0.1, 0.26))),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(PlacementProbeChart(legend: (0.1, 0.26), overlayLegend: true)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(PlacementProbeChart(title: (0.5, 0.1))),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(PlacementProbeChart(legend: (0.6, 0.5))),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 591) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 592, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 593, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 594, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 595, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 596, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 597, relationshipId: "rIdChart7") +
                                 "</w:p>"),

            // Whether a stock chart's up and down bars are painted in the colours the document
            // states. Two pages, differing only in which colour goes which way, so that a reader
            // that ignores the statement altogether cannot pass both:
            //
            //   page 1  rising green, falling red
            //   page 2  the two exchanged
            //
            // Neither colour is what the composer fills in when the document says nothing, which
            // is what the existing stock fixture states and why it could not see #80.
            ["chart-updown-bar-probe"] = () => new DocxBuilder()
                .WithChart(UpDownBarProbeChart(up: "00B050", down: "C00000"))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(UpDownBarProbeChart(up: "C00000", down: "00B050")),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 601) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 602, relationshipId: "rIdChart2") +
                                 "</w:p>"),

            // What weight a chart's titles take when the part does not say. Four pages:
            //
            //   page 1  neither title states a weight -> the default, for both kinds of title
            //   page 2  both state b="1"              -> the control, agreeing by construction
            //   page 3  both state b="0"              -> is a stated regular honoured, or overridden?
            //   page 4  the chart's title bold, the axis title regular -> that the two are separate
            //
            // A weight is measurable from a position because the chart's own title is centred: a
            // bolder title is wider, so it also begins further left.
            ["chart-title-weight-probe"] = () => new DocxBuilder()
                .WithChart(TitleWeightProbeChart(weight: null, axisWeight: null))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TitleWeightProbeChart(weight: "1", axisWeight: "1")),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TitleWeightProbeChart(weight: "0", axisWeight: "0")),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TitleWeightProbeChart(weight: "1", axisWeight: "0")),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 611) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 612, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 613, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 614, relationshipId: "rIdChart4") +
                                 "</w:p>"),

            // What a stated size does to a legend that is also placed by hand. #76 settled the
            // corner with no size; this varies the size, the corner and the number of entries,
            // because a rule about a box cannot be told from a coincidence on one measurement.
            //
            //   page 1  one entry,  corner (0.1, 0.05), size (0.3, 0.25)  -> the known point
            //   page 2  one entry,  corner (0.5, 0.40), size (0.3, 0.25)  -> the corner, or the box?
            //   page 3  one entry,  corner (0.1, 0.05), size (0.5, 0.40)  -> what the size changes
            //   page 4  two entries, corner (0.1, 0.05), no size          -> the control
            //
            // Two pages measuring a *sized* legend of several entries were written and taken out
            // again: Word lays those into the box differently — packed across a row with a gap
            // that is not the one used along a foot, and sharing a left edge down a column rather
            // than each row centring itself — and neither rule is settled. See #87, which carries
            // both measurements.
            ["chart-legend-size-probe"] = () => new DocxBuilder()
                .WithChart(LegendSizeProbeChart((0.1, 0.05), (0.3, 0.25), two: false))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendSizeProbeChart((0.5, 0.4), (0.3, 0.25), two: false)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendSizeProbeChart((0.1, 0.05), (0.5, 0.4), two: false)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendSizeProbeChart((0.1, 0.05), null, two: true)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 621) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 622, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 623, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 624, relationshipId: "rIdChart4") +
                                 "</w:p>"),

            // Where Word cuts a legend's name when the box a manual layout gives it is too
            // narrow to hold the whole of it. One entry a page, the alphabet as the name so the
            // drawn text says where the cut fell, and eight box widths so the rule is read off
            // several points rather than fitted to one.
            //
            //   pages 1-8: boxes of 0.1, 0.14, 0.18, 0.22, 0.26, 0.3, 0.45, 0.75 of the chart's width
            //   page 9:    the same box as page 3, with a name that has spaces in it, since where
            //              a name breaks cannot be read off an alphabet that has none
            ["chart-legend-cut-probe"] = () => new DocxBuilder()
                .WithChart(LegendCutProbeChart(0.1))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.14)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.18)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.22)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.26)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.3)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.45)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.75)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendCutProbeChart(0.18, "alpha beta gamma delta epsilon")),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 641) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 642, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 643, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 644, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 645, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 646, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 647, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 648, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 649, relationshipId: "rIdChart9") +
                                 "</w:p>"),

            // What room a legend takes off the plot. Filed as a question about a legend given a
            // size, but the evidence never implicated the size — Word's plot did not move when the
            // box did. So this varies the name's length as well as the placement, which is what
            // separates the two candidates.
            //
            //   a long name, an ordinary legend up the side
            //   the same name, placed by a corner
            //   the same name, placed and given a size
            //   a short name, an ordinary legend
            //   a short name, placed and given a size
            //   the long name again on charts of 240, 480, 300 and 420 -> is the width a share of
            //   the chart, or a fixed number of points?
            //   two long names on a 360 chart -> how wrapped entries space against each other
            ["chart-legend-room-probe"] = () => new DocxBuilder()
                .WithChart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "side"))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "corner")),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "sized")),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("Units", "side")),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("Units", "sized")),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "side")),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "side")),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "side")),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "side")),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendRoomProbeChart("abcdefghijklmnopqrstuvwxyz", "side",
                        second: "zyxwvutsrqponmlkjihgfedcba")),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 661) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 662, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 663, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 664, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 665, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(240, 216, id: 666, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(480, 216, id: 667, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(300, 216, id: 668, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(420, 216, id: 669, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 670, relationshipId: "rIdChart10") +
                                 "</w:p>"),

            // How a sized legend of several entries shares its box, and which of them are drawn
            // at all — #87 and #90, which cannot be settled apart. One dimension moves at a time,
            // which is what the probes that raised them did not do. The legend overlays the plot
            // so that the plot's own size cannot move with it.
            //
            //   pages 1-4:  the box 180 wide, its height 21.6, 54, 97.2 then 172.8
            //   pages 5-8:  the box 129.6 tall, its width 54, 108, 198 then 306
            //   pages 9-12: the box 54 wide, its height 108, 86.4, 64.8 then 36.72
            //
            // The last four are #90's own question. At 54 wide the third entry needs three lines,
            // so its block is 36.72 — and those four heights give it a share of 36, 28.8, 21.6 and
            // 12.24, every one of them too short for it, while the first two entries are a single
            // line and fit all four. If what decides the dropping is a block against its share,
            // this is where it shows.
            // What a three-dimensional projection measures in, which nothing before this could
            // tell: every earlier probe used one plot rectangle inside one chart frame, and in that
            // space a page-unit quantity, a plot-rectangle-unit quantity and a bare constant are the
            // same number. See #108.
            //
            // The scene is held throughout — the eye never moves — and only the frame the chart is
            // drawn in and the rectangle inside it change. The bar fills the plot box across and in
            // depth (no gaps, one category, one series) and reaches 60 of 100 up it, so its eight
            // corners sit at known places and it never touches the ceiling.
            //
            //   pages 1-6   the plot rectangle: wider, narrower, taller, shorter, and moved
            //   pages 7-8   a bigger chart and a squarer one, each with the plot rectangle placed
            //               so that it lands at exactly the same page position and size as page 1's
            //   pages 9-11  three of the same questions at a second scene
            //
            // Pages 7 and 8 are the ones that carry the argument. Same rectangle on the page, a
            // different frame around it: if the picture does not move, the chart frame plays no
            // part in the projection at all.
            // What c:hPercent does to a three-dimensional box, and what its absence does — which
            // are not the same thing, and that is the finding. See #109.
            //
            // The scene and the bar are held throughout; only the element and the plot rectangle
            // move. Pages 1 and 2 differ in one thing only: the first carries no c:view3D at all
            // and the second states every value #96 measured as the absent-element defaults, but no
            // hPercent. If they draw the same picture, hPercent has one default rather than the two
            // that rAngAx has.
            //
            //   pages 1-2   the two ways of saying nothing about it
            //   pages 3-7   25, 50, 100, 200, 400 in one rectangle
            //   pages 8-10  a taller rectangle, without it and at 50 and 200 — which separates
            //               hPercent multiplying the rectangle's aspect from replacing it
            ["chart-3d-height-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DView("", 0.200000, 0.100000, 0.600000, 0.550000))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/></c:view3D>", 0.200000, 0.100000, 0.600000, 0.550000)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"25\"/></c:view3D>", 0.200000, 0.100000, 0.600000, 0.550000)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"50\"/></c:view3D>", 0.200000, 0.100000, 0.600000, 0.550000)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"100\"/></c:view3D>", 0.200000, 0.100000, 0.600000, 0.550000)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"200\"/></c:view3D>", 0.200000, 0.100000, 0.600000, 0.550000)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"400\"/></c:view3D>", 0.200000, 0.100000, 0.600000, 0.550000)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/></c:view3D>", 0.200000, 0.050000, 0.600000, 0.800000)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"50\"/></c:view3D>", 0.200000, 0.050000, 0.600000, 0.800000)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DView("<c:view3D><c:rotX val=\"15\"/><c:rotY val=\"20\"/><c:rAngAx val=\"0\"/><c:perspective val=\"30\"/><c:depthPercent val=\"100\"/><c:hPercent val=\"200\"/></c:view3D>", 0.200000, 0.050000, 0.600000, 0.800000)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 960) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>no view3D at all</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 961, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>stated, no hPercent</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 962, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 963, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 964, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 965, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 966, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 400</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 967, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>taller, no hPercent</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 968, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>taller, hPercent 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 969, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>taller, hPercent 200</w:t></w:r></w:p>"),
            // How a value becomes a height inside the box — the one assumption in the chain that
            // nothing had tested, since every earlier probe draws 60 of 100 and nothing else.
            // See #113.
            //
            // The scene, the plot rectangle and the bar's footprint are held throughout; only the
            // value and the axis's bounds move.
            //
            //   pages 1-5   20, 40, 60, 80 and 95 of a hundred
            //   pages 6-7   60 of two hundred and of three hundred
            //   page  8     120 of two hundred — the same fraction as 60 of 100, and the control
            //               that says whether anything absolute leaks in
            //   pages 9-10  a scale running from -100, with the value above nought and below it
            ["chart-3d-value-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DValue(20, 0, 100))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(40, 0, 100)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(60, 0, 100)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(80, 0, 100)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(95, 0, 100)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(60, 0, 200)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(60, 0, 300)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(120, 0, 200)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(60, -100, 100)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DValue(-60, -100, 100)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 980) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>20 of 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 981, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40 of 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 982, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60 of 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 983, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>80 of 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 984, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>95 of 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 985, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60 of 200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 986, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60 of 300</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 987, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>120 of 200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 988, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60, min -100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 989, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>-60, min -100</w:t></w:r></w:p>"),
            // How Word shades the faces of a three-dimensional bar, and what it fills the walls
            // and floor with. See #110.
            //
            // A colour question rather than a geometry one — which pixel a face is sampled at
            // barely matters as long as it is well inside — so this could be measured while the
            // projection was still being argued about.
            //
            //   pages 1-6   six series colours, chosen to part a multiplying rule from an adding
            //               one: a dark one and a light one do that, and the saturated pair say
            //               whether the rule works per channel
            //   pages 7-9   the walls and floor stated, the floor alone, and nothing stated
            //   page 10     the same fills put inside c:plotArea instead of c:chart, where the
            //               schema does not have them and Word silently ignores them
            ["chart-3d-shading-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DShade("FF0000", 60, "none"))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("800000", 60, "none")),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("4080C0", 60, "none")),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("202020", 60, "none")),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("E0E0E0", 60, "none")),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("00C000", 60, "none")),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("FF00FF", 10, "all")),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("FF00FF", 10, "floor")),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("FF00FF", 10, "none")),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DShade("FF00FF", 10, "misplaced")),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1000) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>saturated red</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1001, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>dark red</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1002, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>mid blue</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1003, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>near black</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1004, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>near white</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1005, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>saturated green</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1006, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>walls stated</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1007, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>floor only</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1008, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>walls unstated</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1009, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>walls in the wrong place</w:t></w:r></w:p>"),
            // How much of the plot rectangle a three-dimensional scene actually takes. See #116.
            //
            // One category, one series, no gaps and the bar at the axis maximum — so the bar is the
            // whole box and its distance from the rectangle's edge is the inset itself, with no
            // projection arithmetic in between.
            //
            // c:hPercent is what makes the side that binds move: low and the box goes short and
            // wide so the width binds, absent or high and the height does. Both are needed, because
            // an inset measured on one side says nothing about the other.
            ["chart-3d-inset-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DInset(25, 0.2, 0.1, 0.6, 0.55))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(25, 0.3, 0.1, 0.4, 0.55)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(25, 0.1, 0.1, 0.8, 0.55)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(25, 0.35, 0.1, 0.3, 0.55)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(25, 0.2, 0.2, 0.6, 0.3)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(25, 0.2, 0.05, 0.6, 0.8)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(0, 0.2, 0.1, 0.6, 0.55)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(0, 0.3, 0.1, 0.4, 0.55)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(0, 0.1, 0.1, 0.8, 0.55)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DInset(400, 0.2, 0.1, 0.6, 0.55)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1020) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25, 216 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1021, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25, 144 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1022, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25, 288 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1023, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25, 108 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1024, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25, 64.8 tall</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1025, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 25, 172.8 tall</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1026, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>nothing, 216 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1027, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>nothing, 144 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1028, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>nothing, 288 wide</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1029, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>hPercent 400, 216 wide</w:t></w:r></w:p>"),
            // A three-dimensional chart with gridlines on both the value axis and the series axis,
            // each in its own colour, for #120's instrument to read. The bars are shrunk to nothing
            // and left nearly white so the floor is not covered.
            //
            // The tilt sweeps over seven pages, because what #98 wants from these is how the
            // floor's own gridlines converge as rotX moves — a ratio of two lengths in one picture,
            // which no rescaling can touch.
            //
            // Eight more hold the tilt at 25 and sweep c:depthPercent instead, which is the same
            // question asked of the depth: how much of the box the depth really is, measured by
            // something the fitting cannot absorb.
            //
            // Most of them are shallow. A deep box crowds its far gridlines past what can be
            // resolved — at 200 the convergence moves by a twelfth depending on where the detector's
            // threshold is put, which is not a measurement — so the law is pinned at the end where
            // the lines are far apart and the two deep pages are kept only to show where that stops.
            ["chart-3d-slot-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DSlot(2, 1, 0, 0, -1))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(2, 1, 150, 0, 0)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(2, 1, 300, 0, 1)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(3, 1, 0, 0, -1)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(3, 1, 150, 0, 0)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(3, 1, 150, 0, 1)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(3, 1, 300, 0, 2)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(4, 1, 0, 0, -1)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(4, 1, 150, 0, 2)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(4, 1, 300, 0, 1)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 2, 0, 0, -1)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 2, 0, 150, 0)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 2, 0, 150, 1)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 0, -1)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 150, 1)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(3, 1, 50, 0, 1)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 300, 2)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1160, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>box 2 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1161, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 cat gw150 bar0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1162, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 cat gw300 bar1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1163, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>box 3 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1164, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat gw150 bar0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1165, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat gw150 bar1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1166, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat gw300 bar2</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1167, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>box 4 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1168, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cat gw150 bar2</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1169, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cat gw300 bar1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1170, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>box 2 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1171, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 ser gd150 ser0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1172, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 ser gd150 ser1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1173, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>box 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1174, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 ser gd150 ser1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1175, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held back 3 cat gw50 bar1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1176, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held back 3 ser gd300 ser2</w:t></w:r></w:p>")
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 0, -1, 55)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 150, 0, 55)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 150, 1, 55)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart21.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 150, 2, 55)),
                    fromDocument: ("rIdChart21",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart22.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DSlot(1, 3, 0, 300, 1, 55)),
                    fromDocument: ("rIdChart22",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1180, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>steep box 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1181, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>steep 3 ser gd150 ser0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1182, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>steep 3 ser gd150 ser1</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1183, relationshipId: "rIdChart21") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>steep 3 ser gd150 ser2</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1184, relationshipId: "rIdChart22") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>steep held 3 ser gd300 ser1</w:t></w:r></w:p>"),

            ["chart-3d-perspective-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 0))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 5)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 10)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 20)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 30)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 50)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 80)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 120)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 0)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 30)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 25, 60, 80)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1290, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1291, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 5</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1292, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 10</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1293, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1294, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 30</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1295, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1296, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 80</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1297, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1298, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>persp 0 at 40/45</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1299, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 30 at 40/45</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1300, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 80 at 25/60</w:t></w:r></w:p>"),

            ["chart-3d-camera-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 90))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 160)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 240)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 120)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 160)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 15, 20, 80)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 15, 20, 80)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 50, 100)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 50, 25)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 10, 70, 50)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 60, 15, 50)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 30)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 100)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 25, 35, 140)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 20, 50, 100, 50)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1310, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p90 15/20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1311, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p160 15/20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1312, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p240 15/20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1313, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p120 30/20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1314, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p160 40/45</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1315, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p80 depth 200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1316, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p80 depth 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1317, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p50 h 100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1318, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p50 h 25</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1319, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p50 10/70</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1320, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p50 60/15</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1321, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p30 two cats</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1322, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p100 two cats</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1323, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held p140 25/35 d150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1324, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held p100 20/50 h50</w:t></w:r></w:p>"),

            ["chart-3d-eye-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 75, 15, 20, 30))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 15, 20, 30)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 30, 20, 30)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 30, 20, 30)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 45, 20, 30)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 45, 20, 30)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 15, 50, 30)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 15, 50, 30)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 40, 45, 30)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 40, 45, 30)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 60, 15, 30)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 60, 15, 30)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 30)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 100)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 2, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 30)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 2, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 100)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 200)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 200)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 50, 160)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 240)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart21.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 10, 70, 160)),
                    fromDocument: ("rIdChart21",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart22.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 30, 150)),
                    fromDocument: ("rIdChart22",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart23.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 120, 60)),
                    fromDocument: ("rIdChart23",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart24.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 130, 33, 27, 66)),
                    fromDocument: ("rIdChart24",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart25.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 80, 18, 62, 45, 130)),
                    fromDocument: ("rIdChart25",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1340, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>d75</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1341, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>d150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1342, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1343, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1344, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/20 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1345, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/20 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1346, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/50 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1347, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/50 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1348, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1349, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1350, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60/15 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1351, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60/15 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1352, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cats</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1353, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cats p100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1354, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 sers</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1355, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 sers p100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1356, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1357, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1358, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/50 p160</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1359, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 p240</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1360, relationshipId: "rIdChart21") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>10/70 p160</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1361, relationshipId: "rIdChart22") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>h150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1362, relationshipId: "rIdChart23") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>h60 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1363, relationshipId: "rIdChart24") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 33/27</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1364, relationshipId: "rIdChart25") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 18/62 h130</w:t></w:r></w:p>"),

            ["chart-3d-eye2-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 30, 100))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 30, 150)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 30, 100)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 30, 150)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 30)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 30)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 75, 60, 15, 30)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 60, 15, 30)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 75, 10, 70, 30)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 10, 70, 30)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 15, 35, 30)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 15, 35, 30)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 15, 65, 30)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 15, 65, 30)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 25, 30, 30, 50)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 100)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 140)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 180)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 160, 37, 23, 30, 90)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 60, 12, 48, 30)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1370, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 h100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1371, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 h150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1372, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 h100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1373, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 h150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1374, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 2cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1375, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 2cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1376, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60/15 d75</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1377, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60/15 d150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1378, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>10/70 d75</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1379, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>10/70 d150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1380, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/35 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1381, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/35 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1382, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/65 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1383, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/65 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1384, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>25/30 h50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1385, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1386, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p140</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1387, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p180</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1388, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 37/23 d160 h90</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1389, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 12/48 d60</w:t></w:r></w:p>"),

            ["chart-3d-branch-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 30, 20, 120))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 30, 20, 120)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 120, 100)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 120, 150)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 45, 20, 120)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 45, 20, 200)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 35, 120)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 35, 200)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 50, 200)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 120)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 40, 45, 200)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 60, 15, 120)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 50, 15, 20, 30)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 200, 15, 20, 30)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 35, 30)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 30)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 160)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 200)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 90, 28, 33, 130)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 22, 41, 70)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1400, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 p120 d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1401, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 p120 d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1402, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p120 h100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1403, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>p120 h150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1404, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/20 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1405, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/20 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1406, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/35 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1407, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/35 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1408, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>15/50 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1409, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1410, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>40/45 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1411, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>60/15 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1412, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2cat d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1413, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2cat d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1414, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2cat 15/35</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1415, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4cat p30</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1416, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4cat p160</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1417, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2cat p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1418, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 28/33 p130 d90</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1419, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 2cat 22/41 p70</w:t></w:r></w:p>"),

            ["chart-3d-floor-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 35, 120))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 35, 200)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 50, 120)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 50, 200)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 45, 35, 160)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 45, 35, 240)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 45, 20, 160)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 45, 20, 240)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 22, 20, 120)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 22, 20, 200)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 38, 20, 140)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 38, 20, 220)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 30, 20, 120)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 30, 20, 200)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 120, 100)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 30, 20, 200, 100)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 140)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 15, 20, 160)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 150, 15, 20, 50, 25)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 27, 42, 170)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart21.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0, 60, 100, 33, 22, 130, 120)),
                    fromDocument: ("rIdChart21",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1430, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/35 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1431, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/35 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1432, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/50 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1433, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/50 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1434, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/35 p160</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1435, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/35 p240</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1436, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/20 p160</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1437, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>45/20 p240</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1438, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>22/20 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1439, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>22/20 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1440, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>38/20 p140</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1441, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>38/20 p220</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1442, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 d150 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1443, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 d150 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1444, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 h100 p120</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1445, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>30/20 h100 p200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1446, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2cat p140</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1447, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2cat p160</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1448, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>h25 d150 p50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1449, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 27/42 p170</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1450, relationshipId: "rIdChart21") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held 33/22 p130 h120</w:t></w:r></w:p>"),

            ["chart-3d-rotation-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 20, 5))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 20, 10)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 20, 20)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 20, 35)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 20, 50)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 20, 65)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 5, 20)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 10, 20)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 30, 20)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 45, 20)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 60, 20)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 40, 45)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100, 25, 60)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1270, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotY 5</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1271, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotY 10</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1272, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotY 20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1273, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotY 35</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1274, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotY 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1275, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotY 65</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1276, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 5</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1277, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 10</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1278, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 30</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1279, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 45</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1280, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 60</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1281, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held rotX40 rotY45</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1282, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>held rotX25 rotY60</w:t></w:r></w:p>"),

            ["chart-3d-depth-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 20))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 50)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 100)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 150)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 200)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 300)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 500)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 3, 0.2, 0.1, 0.6, 0.55, 1, 60, 50)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 3, 0.2, 0.1, 0.6, 0.55, 1, 60, 200)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 1, 60, 200)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 2, 0.2, 0.1, 0.6, 0.55, 1, 60, 50)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1250, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1251, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1252, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d100</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1253, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1254, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1255, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d300</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1256, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c1s d500</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1257, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c3s d50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1258, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1c3s d200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1259, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3c1s d200 held</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1260, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2c2s d50 held</w:t></w:r></w:p>"),

            ["chart-3d-height-count-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(5, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(6, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(7, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(8, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 2, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 3, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 4, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 5, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 6, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 7, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 8, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 1, 20)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 1, 40)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 1, 80)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1200, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1201, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1202, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1203, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1204, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>5 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1205, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>6 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1206, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>7 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1207, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>8 cat</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1208, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1209, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1210, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1211, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1212, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>5 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1213, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>6 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1214, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>7 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1215, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>8 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1216, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat value 20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1217, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat value 40</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1218, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat value 80</w:t></w:r></w:p>")
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 3, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart21.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 4, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart21",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart22.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 4, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart22",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart23.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 2, 0.2, 0.1, 0.6, 0.55, 1, 60)),
                    fromDocument: ("rIdChart23",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1230, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1231, relationshipId: "rIdChart21") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cat 4 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1232, relationshipId: "rIdChart22") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 cat 4 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1233, relationshipId: "rIdChart23") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cat 2 ser</w:t></w:r></w:p>"),

            ["chart-3d-count-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 0))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 1, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(6, 1, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 2, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 3, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 4, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 6, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 3, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 2, 0.2, 0.1, 0.6, 0.55, 0)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.1, 0.1, 0.8, 0.55, 0)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.1, 0.1, 0.8, 0.55, 0)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 1, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 1, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart16.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart16",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart17.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(4, 1, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart17",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart18.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(6, 1, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart18",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart19.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 2, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart19",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart20.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 3, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart20",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart21.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 4, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart21",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart22.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(1, 6, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart22",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart23.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(2, 3, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart23",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart24.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 2, 0.2, 0.1, 0.6, 0.55, 1)),
                    fromDocument: ("rIdChart24",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart25.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DCounts(3, 1, 0.1, 0.1, 0.8, 0.55, 1)),
                    fromDocument: ("rIdChart25",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1120, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1121, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1122, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1123, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>4 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1124, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>6 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1125, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat 2 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1126, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1127, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat 4 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1128, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat 6 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1129, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>2 cat 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1130, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat 2 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1131, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>3 cat 1 ser wide rect</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1132, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>1 cat 1 ser wide rect</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1133, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 1 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1134, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 2 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1135, relationshipId: "rIdChart16") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 3 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1136, relationshipId: "rIdChart17") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 4 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1137, relationshipId: "rIdChart18") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 6 cat 1 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1138, relationshipId: "rIdChart19") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 1 cat 2 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1139, relationshipId: "rIdChart20") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 1 cat 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1140, relationshipId: "rIdChart21") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 1 cat 4 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1141, relationshipId: "rIdChart22") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 1 cat 6 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1142, relationshipId: "rIdChart23") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 2 cat 3 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1143, relationshipId: "rIdChart24") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 3 cat 2 ser</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1144, relationshipId: "rIdChart25") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>square 3 cat 1 ser wide</w:t></w:r></w:p>"),

            ["chart-3d-footprint-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DFootprint(0, 0))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(50, 0)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(150, 0)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(300, 0)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(0, 50)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(0, 150)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(0, 300)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(150, 150)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DFootprint(50, 300)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1100, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 0 gd 0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1101, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 50 gd 0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1102, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 150 gd 0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1103, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 300 gd 0</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1104, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 0 gd 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1105, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 0 gd 150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1106, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 0 gd 300</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1107, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 150 gd 150</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1108, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>gw 50 gd 300</w:t></w:r></w:p>"),

            ["chart-3d-condition-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DGridSeries(25, 200, 5))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 200, 5)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 200, 5)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 200, 5)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 200, 5)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 100, 9)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 100, 9)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGridSeries(25, 100, 9)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(300, 180, id: 1080, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>deep 300</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(350, 210, id: 1081, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>deep 350</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(400, 240, id: 1082, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>deep 400</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(450, 270, id: 1083, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>deep 450</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(480, 288, id: 1084, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>deep 480</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1085, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>many 360</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(420, 252, id: 1086, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>many 420</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(480, 288, id: 1087, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>many 480</w:t></w:r></w:p>"),

            ["chart-3d-size-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DGrid(25))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(240, 144, id: 1060, relationshipId: "rIdChart") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>240 by 144</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(300, 180, id: 1061, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>300 by 180</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1062, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>360 by 216</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(420, 252, id: 1063, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>420 by 252</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(480, 288, id: 1064, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>480 by 288</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}<w:ind w:left=\"74\"/></w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1065, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>360 by 216 shifted</w:t></w:r></w:p>"),

            ["chart-3d-gridline-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3DGrid(10))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(15)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(20)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(30)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(35)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(40)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 25)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 50)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 200)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 400)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 20)),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart13.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 30)),
                    fromDocument: ("rIdChart13",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart14.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 35)),
                    fromDocument: ("rIdChart14",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart15.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3DGrid(25, 75)),
                    fromDocument: ("rIdChart15",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1040) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 10</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1041, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 15</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1042, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1043, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1044, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 30</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1045, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 35</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1046, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 40</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1047, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 25</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1048, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 50</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1049, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 200</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1050, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 400</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1050, relationshipId: "rIdChart12") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 20</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1051, relationshipId: "rIdChart13") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 30</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1052, relationshipId: "rIdChart14") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 35</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 1053, relationshipId: "rIdChart15") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rotX 25, depth 75</w:t></w:r></w:p>"),


            ["chart-3d-geometry-probe"] = () => new DocxBuilder()
                .WithChart(ChartPart3D(0.200000, 0.100000, 0.600000, 0.550000, 15, 20))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.100000, 0.100000, 0.800000, 0.550000, 15, 20)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.300000, 0.100000, 0.400000, 0.550000, 15, 20)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.200000, 0.050000, 0.600000, 0.800000, 15, 20)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.200000, 0.200000, 0.600000, 0.300000, 15, 20)),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.350000, 0.300000, 0.600000, 0.550000, 15, 20)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.166667, 0.083333, 0.500000, 0.458333, 15, 20)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.240000, 0.072000, 0.720000, 0.396000, 15, 20)),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.200000, 0.100000, 0.600000, 0.550000, 30, 40)),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.200000, 0.050000, 0.600000, 0.800000, 30, 40)),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(ChartPart3D(0.300000, 0.100000, 0.400000, 0.550000, 30, 40)),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 940) +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>base</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 941, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rect wider</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 942, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rect narrower</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 943, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rect taller</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 944, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rect shorter</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 945, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>rect moved</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(432, 259.2, id: 946, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>chart bigger</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(300, 300, id: 947, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>chart squarer</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 948, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>another scene</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 949, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>that one taller</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 950, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 "<w:r><w:t>that one narrower</w:t></w:r></w:p>"),
            ["chart-legend-box-probe"] = () => new DocxBuilder()
                .WithChart(LegendBoxProbeChart((0.5, 0.1)))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.5, 0.25))),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.5, 0.45))),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.5, 0.8))),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.15, 0.6))),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.3, 0.6))),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.55, 0.6))),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart8.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.85, 0.6))),
                    fromDocument: ("rIdChart8",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart9.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.15, 0.5))),
                    fromDocument: ("rIdChart9",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart10.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.15, 0.4))),
                    fromDocument: ("rIdChart10",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart11.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.15, 0.3))),
                    fromDocument: ("rIdChart11",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart12.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(LegendBoxProbeChart((0.15, 0.17))),
                    fromDocument: ("rIdChart12",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 671) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 672, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 673, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 674, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 675, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 676, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 677, relationshipId: "rIdChart7") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 678, relationshipId: "rIdChart8") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 679, relationshipId: "rIdChart9") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 680, relationshipId: "rIdChart10") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 681, relationshipId: "rIdChart11") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 682, relationshipId: "rIdChart12") +
                                 "</w:p>"),

            // What Word draws for a trendline, one kind to a page so that a divergence names the
            // kind that caused it. Six pages:
            //
            //   page 1  linear
            //   page 2  polynomial of the second order
            //   page 3  a moving average over two points   -> which end the mean is drawn at
            //   page 4  linear, running two categories on  -> how far past the data it reaches
            //   page 5  exponential
            //   page 6  linear through a forced intercept
            //   page 7  linear, running one category back  -> the other half of the same rule
            //
            // The fitting itself is not what this measures — that is arithmetic, and
            // ChartTrendlineTests checks it against coefficients worked out independently. What
            // is measured here is what Word *draws*: where the line starts and stops, and whether
            // it is clipped by the plot when it runs past the data.
            ["chart-trendline-probe"] = () => new DocxBuilder()
                .WithChart(TrendlineProbeChart("linear"))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TrendlineProbeChart("poly", order: 2)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TrendlineProbeChart("movingAvg", period: 2)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TrendlineProbeChart("linear", forward: 2)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TrendlineProbeChart("exp")),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart6.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TrendlineProbeChart("linear", intercept: 0)),
                    fromDocument: ("rIdChart6",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart7.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TrendlineProbeChart("linear", backward: 1)),
                    fromDocument: ("rIdChart7",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 561) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 562, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 563, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 564, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 565, relationshipId: "rIdChart5") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 566, relationshipId: "rIdChart6") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 567, relationshipId: "rIdChart7") +
                                 "</w:p>"),

            // How far a chart's labels sit from the axes they belong to, which one chart cannot
            // say: four of them, varying the marks the labels may have to clear, the type they
            // are set in, and what the format calls a label offset.
            //
            //   page 1  no marks, ten point labels
            //   page 2  marks outside, the same labels   -> does a label clear the mark?
            //   page 3  no marks, twenty point labels    -> is the gap a share of the type?
            //   page 4  no marks, twice the label offset -> what the offset is a share of
            ["chart-axis-probe"] = () => new DocxBuilder()
                .WithChart(AxisProbeChart("none", 1000))
                .WithPart("word/charts/chart2.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(AxisProbeChart("out", 1000)),
                    fromDocument: ("rIdChart2",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart3.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(AxisProbeChart("none", 2000)),
                    fromDocument: ("rIdChart3",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .WithPart("word/charts/chart4.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(AxisProbeChart("none", 1000, labelOffset: 200)),
                    fromDocument: ("rIdChart4",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 501) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 502, relationshipId: "rIdChart2") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 503, relationshipId: "rIdChart3") +
                                 "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 504, relationshipId: "rIdChart4") +
                                 "</w:p>")
                .WithPart("word/charts/chart5.xml",
                    "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                    ChartPart(TwoSeriesChart()),
                    fromDocument: ("rIdChart5",
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.ChartDrawing(360, 216, id: 505, relationshipId: "rIdChart5") +
                                 "</w:p>"),

            // Where Word puts the plotting when the chart does not say, which is what every chart
            // in a real document leaves to it. Six of them, varying one thing each: how wide the
            // numbers up the axis are, how large they are set, how big the frame is, how long the
            // words under the bars are, and whether there are any labels at all.
            //
            //   page 1  the plain case
            //   page 2  numbers a hundred thousand times larger -> how the left edge follows them
            //   page 3  the same chart at twenty point          -> what the type size does
            //   page 4  a frame half the size                   -> fixed margins or proportional
            //   page 5  a long word under the bars              -> what the foot does
            //   page 6  no labels at all                        -> what is left when nothing is
            ["chart-layout-probe"] = () =>
            {
                var builder = new DocxBuilder().WithChart(AutoLayoutChart(100));

                (string Name, string Chart, double Width, double Height)[] pages =
                [
                    ("rIdChart2", AutoLayoutChart(10000000), 360, 216),
                    ("rIdChart3", AutoLayoutChart(100, labelSize: 2000), 360, 216),
                    ("rIdChart4", AutoLayoutChart(100), 180, 108),
                    ("rIdChart5", AutoLayoutChart(100, category: "Category number one"), 360, 216),
                    ("rIdChart6", AutoLayoutChart(100, tickLabels: "none"), 360, 216)
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 600) + "</w:p>");

                for (var i = 0; i < pages.Length; i++)
                {
                    builder.WithPart($"word/charts/chart{i + 2}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(pages[i].Chart),
                        fromDocument: (pages[i].Name,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(pages[i].Width, pages[i].Height,
                                                id: 601 + i, relationshipId: pages[i].Name) + "</w:p>");
                }

                return builder;
            },

            // What a value axis runs between when the chart does not say, which is the last thing
            // about a chart that Word decides for itself. Twelve of them, differing only in the
            // numbers they hold: the labels up the axis say outright what Word chose.
            ["chart-scale-probe"] = () =>
            {
                var builder = new DocxBuilder();

                for (var i = 0; i < ScaleProbeData.Length; i++)
                {
                    var id = $"rIdScale{i + 1}";

                    builder.WithPart($"word/charts/scale{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(AutoScaleChart(ScaleProbeData[i])),
                        fromDocument: (id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(i == 0 ? ZeroSpacing : ZeroSpacingNewPage)}</w:pPr>" +
                        DocxBuilder.ChartDrawing(288, 180, id: 700 + i, relationshipId: id) + "</w:p>");
                }

                return builder;
            },

            // The same question of a chart that lies down, where the axis being asked about runs
            // along the foot rather than up the side: ten charts, varying the numbers and how much
            // room the axis has for them.
            ["chart-bar-scale-probe"] = () =>
            {
                var builder = new DocxBuilder();

                for (var i = 0; i < BarScaleProbeData.Length; i++)
                {
                    var id = $"rIdBarScale{i + 1}";
                    var (values, x, width, direction, labelSize, y, height) = BarScaleProbeData[i];

                    builder.WithPart($"word/charts/barscale{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(AutoScaleBarChart(
                            values, x, width, direction, labelSize, y, height)),
                        fromDocument: (id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(i == 0 ? ZeroSpacing : ZeroSpacingNewPage)}</w:pPr>" +
                        DocxBuilder.ChartDrawing(288, 180, id: 720 + i, relationshipId: id) + "</w:p>");
                }

                return builder;
            },

            // What goes round the plotting rather than in it.
            //
            //   page 1  a title over the top
            //   page 2  the same, twice the size
            //   page 3  a title on each axis, the one up the side turned on its end
            //   page 4  a legend under the plot
            //   page 5  the same, to the right
            //   page 6  over the top
            //   page 7  to the left
            //   page 8  under the plot, four series of it, one named at length
            //   page 9  a number over each bar
            //   page 10 the same, inside the end of it
            //   page 11 a number at each point of a line
            //   page 12 a share written on each slice of a pie
            //   page 13 numbers written to a decimal place
            //   page 14 all three at once
            //   page 15 a title of ten point, 16 of thirty
            //   page 17 a title too long for one line
            //   page 18 numbers over the bars of twenty point
            //   page 19 a legend of twenty point
            ["chart-title-legend-label"] = () =>
            {
                // The heading face is set to something the body face is not, so that which of the
                // two a title takes can be read off the page rather than guessed at.
                var builder = new DocxBuilder()
                    .WithTheme("Times New Roman", "Calibri")
                    .WithChart(DressedChart(title: "Sales by quarter"));

                (string Id, string Chart)[] rest =
                [
                    ("rIdDress2", DressedChart(title: "Sales by quarter", titleSize: 2000)),
                    ("rIdDress3", DressedChart(axisTitles: true)),
                    ("rIdDress4", DressedChart(legend: "b")),
                    ("rIdDress5", DressedChart(legend: "r")),
                    ("rIdDress6", DressedChart(legend: "t")),
                    ("rIdDress7", DressedChart(legend: "l")),
                    ("rIdDress8", DressedChart(series: 4, legend: "b")),
                    ("rIdDress9", DressedChart(labels: true)),
                    ("rIdDress10", DressedChart(labels: true, labelPosition: "inEnd")),
                    ("rIdDress11", DressedChart(kind: "line", labels: true)),
                    ("rIdDress12", DressedChart(kind: "pie", series: 1, labels: true,
                        percent: true, legend: "b")),
                    ("rIdDress13", DressedChart(labels: true, labelFormat: "0.0")),
                    ("rIdDress14", DressedChart(title: "Sales by quarter", axisTitles: true,
                        legend: "b", labels: true)),

                    // What the room each of them takes is made of, which one size cannot say.
                    ("rIdDress15", DressedChart(title: "Sales by quarter", titleSize: 1000)),
                    ("rIdDress16", DressedChart(title: "Sales by quarter", titleSize: 3000)),
                    ("rIdDress17", DressedChart(
                        title: "Sales by quarter across every region we sell in")),
                    ("rIdDress18", DressedChart(labels: true, textSize: 2000)),
                    ("rIdDress19", DressedChart(legend: "b", textSize: 2000))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 980) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/dress{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 981 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // The two kinds of chart that are not a value against a category: an area, which is a
            // line filled down to the axis, and a scatter, which holds pairs of numbers and so has
            // a value axis both ways.
            //
            //   page 1  one area, placed by hand
            //   page 2  two areas over each other
            //   page 3  two areas stacked
            //   page 4  the same, filled out to the whole
            //   page 5  one area, left to Word to place and scale
            //   page 6  a scatter of markers alone
            //   page 7  a scatter of straight lines and markers
            //   page 8  a scatter of a smooth line and no markers
            //   page 9  two scatters, left to Word to scale both ways
            //   page 10 a scatter that says nothing about its markers
            //   page 11 markers of three points, 12 of five, 13 of nine
            //   page 14 two scatters saying nothing about their markers
            //   page 15 an axis that could divide itself into eleven
            //   page 16 four scatters saying nothing about their markers
            //   page 17 one of them, with no line to go with it
            //   page 18 a scatter left to Word to place
            //   page 19 an area whose first category is wider than its numbers
            ["chart-area-scatter"] = () =>
            {
                var builder = new DocxBuilder().WithChart(AreaChart("standard", 1));

                (string Id, string Chart)[] rest =
                [
                    ("rIdArea2", AreaChart("standard", 2)),
                    ("rIdArea3", AreaChart("stacked", 2, maximum: null)),
                    ("rIdArea4", AreaChart("percentStacked", 2, maximum: null,
                        numberFormat: "0%")),
                    ("rIdArea5", AreaChart("standard", 1, manualLayout: false, maximum: null)),
                    ("rIdArea6", ScatterChart("lineMarker", line: false)),
                    ("rIdArea7", ScatterChart("lineMarker")),
                    ("rIdArea8", ScatterChart("smoothMarker", marker: null, smooth: true)),
                    ("rIdArea9", ScatterChart("lineMarker", stated: false, series: 2)),
                    ("rIdArea10", ScatterChart("lineMarker", marker: null)),

                    // How large a marker of a stated size comes out, which one page cannot say,
                    // and what Word picks for a second series left to itself.
                    ("rIdArea11", ScatterChart("lineMarker", line: false, markerSize: 3)),
                    ("rIdArea12", ScatterChart("lineMarker", line: false, markerSize: 5)),
                    ("rIdArea13", ScatterChart("lineMarker", line: false, markerSize: 9)),
                    ("rIdArea14", ScatterChart("lineMarker", marker: null, series: 2)),

                    // And whether an axis will divide itself into eleven, which is the one thing
                    // the scale probes leave open: every axis they hold is either short enough for
                    // the labels to run out first or lands on ten exactly.
                    ("rIdArea15", AreaChart("standard", 1, maximum: null,
                        values: [0.6, 1, 0.4, 0.8])),

                    // And which markers Word runs through when four series in a row say nothing.
                    ("rIdArea16", ScatterChart("lineMarker", marker: null, line: false, series: 4)),

                    // The same one series again, drawn with markers and no line, which is what
                    // says whether a marker left to itself is sized by the line or by the company
                    // it keeps.
                    ("rIdArea17", ScatterChart("lineMarker", marker: null, line: false)),

                    // And the two left to Word to place: a scatter, whose numbers run along the
                    // foot as well as up the side, and an area whose first category is far wider
                    // than any of its numbers.
                    ("rIdArea18", ScatterChart("lineMarker", stated: false, manualLayout: false,
                        series: 2)),
                    ("rIdArea19", AreaChart("standard", 1, manualLayout: false, maximum: null,
                        values: [3, 5, 2, 4], firstCategory: "Category number one"))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 950) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/area{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 951 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // The two other kinds of chart a document is likely to hold: a line through the
            // categories, and a pie divided between them.
            //
            //   page 1  one line
            //   page 2  two lines
            //   page 3  a pie, its plotting placed by hand
            //   page 4  the same pie, left to Word to place
            ["chart-line-pie"] = () =>
            {
                var builder = new DocxBuilder().WithChart(LineChart(series: 1));

                (string Id, string Chart)[] rest =
                [
                    ("rIdLine2", LineChart(series: 2)),
                    ("rIdPie1", PieChart(manualLayout: true)),
                    ("rIdPie2", PieChart(manualLayout: false))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 800) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/plot{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 801 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // Bars lying along their axis rather than standing up, and bars stacked on each other
            // rather than beside each other.
            //
            //   page 1  lying along, one series, placed by hand
            //   page 2  lying along, left to Word to place  -> the labels have swapped sides
            //   page 3  standing up, two series stacked
            //   page 4  the same, with the scale left to Word -> is it the sum that decides?
            //   page 5  the same again, as a percentage of each category
            //   page 6  lying along and stacked
            ["chart-bar-stacked"] = () =>
            {
                var builder = new DocxBuilder().WithChart(BarChart("bar", "clustered", 1));

                (string Id, string Chart)[] rest =
                [
                    ("rIdBar2", BarChart("bar", "clustered", 1, manualLayout: false, maximum: null)),
                    ("rIdBar3", BarChart("col", "stacked", 2)),
                    ("rIdBar4", BarChart("col", "stacked", 2, maximum: null)),
                    ("rIdBar5", BarChart("col", "percentStacked", 2, maximum: null,
                        numberFormat: "0%")),
                    ("rIdBar6", BarChart("bar", "stacked", 2)),

                    // Where the marks along a lying-down axis reach, and where that axis goes
                    // when something is negative — the two things the upright charts cannot say.
                    ("rIdBar7", BarChart("bar", "clustered", 1, tickMark: "out")),
                    ("rIdBar8", BarChart("bar", "clustered", 2, maximum: null, tickMark: "out",
                        values: [30, -45, 20]))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 900) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/bar{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 901 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // The fourth round: what a legend draws beside a series that is a line rather than a
            // shape, and how far above its numbers an axis left to itself reaches.
            //
            //   page 1  a line chart, its legend to the right
            //   page 2  the same along the foot at twenty point  -> what the key is a share of
            //   page 3  a web that marks its points               -> whether the key marks them too
            //   page 4  a stock chart's legend                    -> what a series of marks gets
            //   page 5  a scatter's legend
            //   page 6  bars reaching 58 of an axis left to Word  -> how far above them it stops
            //   page 7  the same filled down to the axis
            ["chart-legend-key-probe"] = () =>
            {
                var builder = new DocxBuilder().WithChart(LineChart(series: 2, legend: "r"));

                (string Id, string Chart)[] rest =
                [
                    ("rIdKey2", LineChart(series: 2, legend: "b", legendSize: 20)),
                    ("rIdKey3", RadarChart(style: "marker", series: 2, legend: "b")),
                    ("rIdKey4", StockChart(legend: "b")),
                    ("rIdKey5", ScatterChart("lineMarker", series: 2)
                        .Replace("<c:plotVisOnly val=\"1\"/>",
                            "<c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend>" +
                            "<c:plotVisOnly val=\"1\"/>")),
                    ("rIdKey6", BarChart("col", "clustered", 1, manualLayout: false,
                        maximum: null, values: [30, 58, 20])),
                    ("rIdKey7", AreaChart("standard", 1, manualLayout: false, maximum: null,
                        values: [30, 58, 20, 45]))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 1400) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/key{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 1401 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // The second round of asking, for the rules the first round could not separate: how
            // large a bubble is drawn, where the words round a web go, and what a doughnut left
            // to Word to place comes out at.
            //
            //   page 1   a web of eight, placed by hand      -> the angles the words are set by
            //   page 2   the same, its words at twenty point -> what the gap is a share of
            //   page 3   a web of four                       -> north, south, east and west alone
            //   page 4   a web left to Word, at twenty point -> what the room round it is a share of
            //   page 5   a web whose markers are seven point -> the box a marker is drawn in
            //   page 6   a doughnut left to Word, with a legend and no words on it
            //   page 7   the same with the words and no legend
            //   page 8   a doughnut placed by hand, its shares written on it
            //   pages 9 to 12  bubbles at a quarter, three quarters, half again and treble
            //   page 13  the same on a larger frame          -> what the size is measured against
            //   page 14  and on a tall one
            //   page 15  and with the plotting made small    -> which of the two it follows
            //   page 16  a stock chart closing on a circle   -> whether the tick is the marker
            //   page 17  one closing on nothing at all
            //   page 18  a line chart marking its points without saying how large
            ["chart-kinds-probe"] = () =>
            {
                string[] eight = ["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight"];
                double[] eightValues = [30, 45, 20, 55, 35, 25, 50, 40];

                var builder = new DocxBuilder().WithChart(
                    RadarChart(categories: eight, values: eightValues));

                (string Id, string Chart, double Width, double Height)[] rest =
                [
                    ("rIdKind2", RadarChart(categories: eight, values: eightValues,
                        labelSize: 20), 360, 216),
                    ("rIdKind3", RadarChart(categories: ["One", "Two", "Three", "Four"],
                        values: [30, 45, 20, 55]), 360, 216),
                    ("rIdKind4", RadarChart(manualLayout: false, labelSize: 20), 360, 216),
                    ("rIdKind5", RadarChart(style: "marker", markerSize: 7), 360, 216),
                    ("rIdKind6", DoughnutChart(manualLayout: false, legend: "r"), 360, 216),
                    ("rIdKind7", DoughnutChart(manualLayout: false, labels: true), 360, 216),
                    ("rIdKind8", DoughnutChart(labels: true), 360, 216),
                    ("rIdKind9", BubbleChart(scale: 25), 360, 216),
                    ("rIdKind10", BubbleChart(scale: 75), 360, 216),
                    ("rIdKind11", BubbleChart(scale: 150), 360, 216),
                    ("rIdKind12", BubbleChart(scale: 300), 360, 216),
                    ("rIdKind13", BubbleChart(), 480, 288),
                    ("rIdKind14", BubbleChart(), 216, 360),
                    ("rIdKind15", BubbleChart(smallPlot: true), 360, 216),
                    ("rIdKind16", StockChart(closeMarker: "circle"), 360, 216),
                    ("rIdKind17", StockChart(closeMarker: "none"), 360, 216),
                    ("rIdKind18", LineChart(series: 1, marker: "circle"), 360, 216)
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 1200) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/kind{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(rest[i].Width, rest[i].Height,
                                                id: 1201 + i, relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // The third round, for the two rules the second could not separate: how large a
            // bubble is on a frame that is not the probe's own, and how much room a pie or a
            // doughnut makes for the words written on it.
            //
            //   page 1  bubbles at a quarter, on a larger frame
            //   page 2  and at treble
            //   page 3  a square frame
            //   page 4  a frame half again as tall
            //   page 5  a doughnut whose shares are written at twenty point
            //   page 6  and at fourteen
            //   page 7  a pie whose shares are written at ten  -> whether a pie gives way too
            ["chart-kinds-probe-two"] = () =>
            {
                var builder = new DocxBuilder().WithChart(BubbleChart(scale: 25));

                (string Id, string Chart, double Width, double Height)[] rest =
                [
                    ("rIdMore2", BubbleChart(scale: 300), 480, 288),
                    ("rIdMore3", BubbleChart(), 288, 288),
                    // As wide as the page will take and half again as tall as the probe's own
                    // frame: what matters is the shorter side, and a frame wider than the page
                    // would have Word drop the labels that fall off it.
                    ("rIdMore4", BubbleChart(), 468, 432),
                    ("rIdMore5", DoughnutChart(manualLayout: false, labels: true, labelSize: 20),
                        360, 216),
                    ("rIdMore6", DoughnutChart(manualLayout: false, labels: true, labelSize: 14),
                        360, 216),
                    ("rIdMore7", DoughnutChart(manualLayout: false, labels: true, pie: true),
                        360, 216)
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(480, 288, id: 1300) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/more{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(rest[i].Width, rest[i].Height,
                                                id: 1301 + i, relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // What Word does with a merged table whose rows are not all the same width: the
            // fourth page of adjacent-tables-probe shows that it refits the whole of it, and
            // these ten pages are what says by how much.
            //
            //   pages 1 to 4  a second table indented 18, 36, 72 and 108 points
            //   page 5   a second table narrow enough that its indent still fits
            //   page 6   a second table wider than the first, and not indented at all
            //   page 7   the same, indented as well
            //   page 8   the first table indented and the second not
            //   page 9   a first table that declares a width narrower than its own columns
            //   page 10  three tables: no indent, then 36 points, then 72
            ["merged-indent-probe"] = () =>
            {
                var builder = new DocxBuilder();

                void Page(params string[] tables)
                {
                    foreach (var table in tables) builder.AddRawParagraph(table);

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                }

                string Plain(string label = "Plain") =>
                    AdjacentTable(label, 2880, 1440, "FFE0E0");

                foreach (var indent in new[] { 360, 720, 1440, 2160 })
                {
                    Page(Plain(),
                        AdjacentTable($"In{indent}", 2880, 1440, "E0E0FF", indentTwips: indent));
                }

                Page(Plain(), AdjacentTable("Narrow", 1440, 720, "E0E0FF", indentTwips: 720));
                Page(Plain(), AdjacentTable("Wider", 3600, 1800, "E0E0FF"));
                Page(Plain(), AdjacentTable("Wider in", 3600, 1800, "E0E0FF", indentTwips: 720));

                Page(AdjacentTable("Front in", 2880, 1440, "FFE0E0", indentTwips: 720),
                    AdjacentTable("Behind", 2880, 1440, "E0E0FF"));

                Page(AdjacentTable("Narrow said", 2880, 1440, "FFE0E0", declaredWidth: 3600),
                    AdjacentTable("After", 2880, 1440, "E0E0FF", indentTwips: 720));

                Page(Plain(),
                    AdjacentTable("Middle", 2880, 1440, "E0E0FF", indentTwips: 720),
                    AdjacentTable("Last", 2880, 1440, "E0FFE0", indentTwips: 1440));

                return builder;
            },

            // A pie with a hole through it, and a scatter whose points carry a size.
            //
            //   page 1   a doughnut, its hole the half the format means by saying nothing
            //   page 2   a quarter hole    -> what a hole is measured against
            //   page 3   three quarters
            //   page 4   two series        -> how the rings divide what one ring would take
            //   page 5   begun a quarter turn round
            //   page 6   left to Word to place, with a legend and a share written on each
            //   page 7   a bubble chart, its sizes 10 to 40
            //   page 8   the same at half scale    -> what the scale is a scale of
            //   page 9   sized by width rather than by area
            //   page 10  two series, left to Word  -> whether one series is sized against both
            //   page 11  at twice the scale
            //   page 12  one bubble alone          -> what the largest bubble comes to
            ["chart-doughnut-bubble"] = () =>
            {
                var builder = new DocxBuilder().WithChart(DoughnutChart());

                (string Id, string Chart)[] rest =
                [
                    ("rIdRing2", DoughnutChart(hole: 25)),
                    ("rIdRing3", DoughnutChart(hole: 75)),
                    ("rIdRing4", DoughnutChart(series: 2)),
                    ("rIdRing5", DoughnutChart(firstSliceAngle: 90)),
                    ("rIdRing6", DoughnutChart(manualLayout: false, legend: "r", labels: true)),
                    ("rIdRing7", BubbleChart()),
                    ("rIdRing8", BubbleChart(scale: 50)),
                    ("rIdRing9", BubbleChart(sizeRepresents: "w")),
                    ("rIdRing10", BubbleChart(series: 2, manualLayout: false, stated: false)),
                    ("rIdRing11", BubbleChart(scale: 200)),
                    ("rIdRing12", BubbleChart(sizes: [10, 10, 10, 10], x: [4, 4, 4, 4],
                        y: [30, 30, 30, 30]))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 1000) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/ring{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 1001 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },

            // The categories set round a circle, and the two kinds of chart that draw the lines
            // between their series rather than along them.
            //
            //   page 1  a radar of five categories, its plotting placed by hand
            //   page 2  the same with a marker at every point
            //   page 3  the same filled in
            //   page 4  two series, left to Word to place, with a legend under it
            //   page 5  six categories and no scale stated  -> where the web's corners fall
            //   page 6  a stock chart of high, low and close
            //   page 7  one of open, high, low and close    -> what an up bar and a down bar are
            //   page 8  high, low and close left to Word to place
            //   page 9  open to close at half the gap       -> how wide a bar is
            ["chart-radar-stock"] = () =>
            {
                var builder = new DocxBuilder().WithChart(RadarChart());

                (string Id, string Chart)[] rest =
                [
                    ("rIdWeb2", RadarChart(style: "marker")),
                    ("rIdWeb3", RadarChart(style: "filled")),
                    ("rIdWeb4", RadarChart(series: 2, manualLayout: false, legend: "b")),
                    ("rIdWeb5", RadarChart(manualLayout: false, stated: false,
                        categories: ["One", "Two", "Three", "Four", "Five", "Six"],
                        values: [30, 45, 20, 55, 35, 25])),
                    ("rIdWeb6", StockChart()),
                    ("rIdWeb7", StockChart(series: 4)),
                    ("rIdWeb8", StockChart(manualLayout: false, stated: false)),
                    ("rIdWeb9", StockChart(series: 4, gapWidth: 50))
                ];

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                        DocxBuilder.ChartDrawing(360, 216, id: 1100) + "</w:p>");

                for (var i = 0; i < rest.Length; i++)
                {
                    builder.WithPart($"word/charts/web{i + 1}.xml",
                        "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
                        ChartPart(rest[i].Chart),
                        fromDocument: (rest[i].Id,
                            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"));

                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                            DocxBuilder.ChartDrawing(360, 216, id: 1101 + i,
                                                relationshipId: rest[i].Id) + "</w:p>");
                }

                return builder;
            },


            // Word draws an older shape a little way off from where a newer one of the same size
            // goes, and how far depends on how thick its stroke is. Eight pages, one shape each,
            // varying nothing but the stroke: whatever the rule is, it is in these numbers.
            ["vml-stroke-probe"] = () => new DocxBuilder()
                .AddRawParagraph(StrokeProbePage(null, first: true))
                .AddRawParagraph(StrokeProbePage("0.25pt"))
                .AddRawParagraph(StrokeProbePage("0.5pt"))
                .AddRawParagraph(StrokeProbePage("0.75pt"))
                .AddRawParagraph(StrokeProbePage("1pt"))
                .AddRawParagraph(StrokeProbePage("1.5pt"))
                .AddRawParagraph(StrokeProbePage("2pt"))
                .AddRawParagraph(StrokeProbePage("3pt"))
                .AddRawParagraph(StrokeProbePage("4.5pt"))
                .AddRawParagraph(StrokeProbePage("6pt"))

                // And three more asking whether the offset belongs to the shape or to where the
                // shape is: the same weight on a different geometry, and two shapes on one line.
                .AddRawParagraph(StrokeProbePage("1pt", element: "roundrect"))
                .AddRawParagraph(StrokeProbePage("1pt", element: "oval"))
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:108pt;height:54pt", element: "rect",
                                     fillColor: "#c0d8f0", strokeColor: "#000000",
                                     strokeWeight: "1pt", id: 1060) +
                                 DocxBuilder.VmlShape("width:108pt;height:54pt", element: "rect",
                                     fillColor: "#f0d8c0", strokeColor: "#000000",
                                     strokeWeight: "1pt", id: 1061) +
                                 "</w:p>")
                .AddParagraph("Two rectangles on one line.", ZeroSpacing, Times12),

            // How much taller than itself an old-style shape makes its line. vml-stroke-probe
            // showed that it does — the paragraph under a six point outline sits five points
            // lower than the shape's own height accounts for — and left the rule unexplained,
            // because ten weights at one height cannot separate what the growth follows.
            //
            // This asks properly. Fourteen weights, close enough together to show where the rule
            // steps: if it turns on the whole points, 1.25 and 1.75 both go with 2. Then the same
            // weight at a quarter of the height and at twice it, which says whether the growth is
            // the shape's business or the outline's, and the same weight on two other geometries.
            ["vml-stroke-line-probe"] = () =>
            {
                double[] weights = [1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.25, 4, 4.5, 5, 5.5, 6, 8];

                var builder = new DocxBuilder();
                var first = true;

                void Page(string weight, double height, string element = "rect")
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacingNewPage)}</w:pPr>" +
                        DocxBuilder.VmlShape($"width:108pt;height:{Number(height)}pt", element: element,
                            fillColor: "#c0d8f0", strokeColor: "#000000",
                            strokeWeight: weight, id: 1070) + "</w:p>" +
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{element} {weight} at {Number(height)}</w:t></w:r></w:p>");

                    first = false;
                }

                foreach (var weight in weights) Page(Number(weight) + "pt", 54);

                // The same outline on a short shape and a tall one.
                Page("3pt", 13.5);
                Page("3pt", 108);

                // And on shapes that are not rectangles.
                Page("3pt", 54, "oval");
                Page("3pt", 54, "roundrect");

                return builder;
            },

            // The same question again, and this time to a hundredth of a point. A single line can
            // only be read to within a step of Word's grid — 0.24pt — which is wider than the
            // differences vml-stroke-line-probe turns up. Thirty shapes stacked one under another
            // divide that by thirty.
            //
            // Eleven weights at one height, and five heights at one weight, because the short
            // shape in the other probe grew by more than the tall one and that has to be pinned
            // rather than guessed at.
            ["vml-stroke-stack-probe"] = () =>
            {
                const int Stack = 30;

                double[] weights = [0.25, 0.5, 0.75, 1, 1.25, 1.5, 2, 2.5, 3, 4, 4.5, 5, 6, 8];
                double[] heights = [4.5, 9, 13.5, 18, 27];

                var builder = new DocxBuilder();
                var first = true;

                void Page(double weight, double height)
                {
                    for (var i = 0; i < Stack; i++)
                    {
                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{(first && i == 0 ? ZeroSpacing : i == 0 ? ZeroSpacingNewPage : ZeroSpacing)}</w:pPr>" +
                            DocxBuilder.VmlShape($"width:72pt;height:{Number(height)}pt", element: "rect",
                                fillColor: "#c0d8f0", strokeColor: "#000000",
                                strokeWeight: Number(weight) + "pt", id: 1080 + i) + "</w:p>");
                    }

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{Number(weight)}pt at {Number(height)}</w:t></w:r></w:p>");

                    first = false;
                }

                foreach (var weight in weights) Page(weight, 13.5);
                foreach (var height in heights) Page(3, height);

                // A shape with no outline at all, and a picture, at heights that are not whole
                // points. Whether the line is as tall as the object or as the whole point above it
                // is not a question about strokes, and the answer decides whether the rule found
                // here belongs to old-style shapes or to everything inline.
                void Bare(double height)
                {
                    for (var i = 0; i < Stack; i++)
                    {
                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{(i == 0 ? ZeroSpacingNewPage : ZeroSpacing)}</w:pPr>" +
                            DocxBuilder.VmlShape($"width:72pt;height:{Number(height)}pt", element: "rect",
                                fillColor: "#c0d8f0", strokeColor: null, strokeWeight: null, id: 1180 + i) + "</w:p>");
                    }

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">bare at {Number(height)}</w:t></w:r></w:p>");
                }

                Bare(13.5);
                Bare(13.1);
                Bare(13.9);

                return builder;
            },

            // Whether an inline picture's line is as tall as the picture or as the whole point
            // above it. vml-stroke-stack-probe shows a shape with an outline taking the whole
            // point and a shape without one taking its own height exactly; this asks the same of
            // a picture, which is the thing an ordinary document is full of.
            //
            // Thirty to a page, so the answer is read to a hundredth rather than to a grid step.
            ["inline-picture-line-probe"] = () =>
            {
                const int Stack = 30;

                var builder = new DocxBuilder();
                var picture = builder.AddImagePart(PngWriter.Solid(8, 8, 40, 80, 160));
                var first = true;

                foreach (var height in new[] { 13.5, 13.1 })
                {
                    for (var i = 0; i < Stack; i++)
                    {
                        builder.AddImageParagraph(picture, 72, height,
                            first && i == 0 ? ZeroSpacing : i == 0 ? ZeroSpacingNewPage : ZeroSpacing);
                    }

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">picture at {Number(height)}</w:t></w:r></w:p>");

                    first = false;
                }

                return builder;
            },

            // And the same measurement again: how far inside its edges the older kind of box sets
            // its text, and whether it answers the question the same way the newer kind does.
            //
            //   page 1  whatever a box that says nothing gets
            //   page 2  no inset at all, a fine stroke
            //   page 3  no inset, a six point stroke  -> is half the stroke part of the inset?
            ["vml-inset-probe"] = () => new DocxBuilder()
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:216pt;height:72pt", ShapeText("A"),
                                     strokeWeight: "0.75pt", id: 1040) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:216pt;height:72pt", ShapeText("B"),
                                     strokeWeight: "0.75pt", inset: "0,0,0,0", id: 1041) + "</w:p>")
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>" +
                                 DocxBuilder.VmlShape("width:216pt;height:72pt", ShapeText("C"),
                                     strokeWeight: "6pt", inset: "0,0,0,0", id: 1042) + "</w:p>"),

            // Inline images: one on its own, one alongside text so that the line's height and the
            // baseline it sits on can be checked, and one with transparency.
            ["images"] = () =>
            {
                var builder = new DocxBuilder();
                var diagonal = builder.AddImagePart(PngWriter.Diagonal(48));
                var solid = builder.AddImagePart(PngWriter.Solid(16, 16, 30, 120, 200));
                var masked = builder.AddImagePart(PngWriter.HalfTransparent(32));

                return builder
                    .AddParagraph("Paragraph before the image.", ZeroSpacing, Times12)
                    .AddImageParagraph(diagonal, 72, 72, ZeroSpacing)
                    .AddImageParagraph(solid, 24, 24, ZeroSpacing, leadingText: "Inline with text ")
                    .AddImageParagraph(masked, 48, 48, ZeroSpacing)
                    .AddParagraph("Paragraph after the image.", ZeroSpacing, Times12);
            },

            // How tall a line is when a picture on it is taller than the text beside it and the
            // paragraph asks for a multiple of the line — the ordinary case in a real document,
            // where a picture is dropped into a paragraph nobody has changed the spacing of and
            // Word's own Normal asks for 1.08 lines.
            //
            // Every fixture written by hand here sets the spacing to a single line, where a
            // multiple of one hides whatever the rule is. A picture 96 points tall on a 1.08 line
            // is where the two readings part: a multiple of the whole line box would leave eight
            // points under the picture, and a multiple of the text's own line leaves one.
            //
            // Four multiples down the page, and four picture heights across each: two shorter
            // than the line the text alone would make, so that the plain text rule is measured in
            // the same document, and two taller. Each picture paragraph is followed by a marker
            // whose baseline is the whole measurement — the picture line's foot plus one ascent.
            ["image-line-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var picture = builder.AddImagePart(PngWriter.Solid(20, 20, 60, 110, 180));

                var first = true;
                foreach (var line in new[] { 240, 259, 360, 480 })
                {
                    foreach (var height in new[] { 6, 12, 24, 96 })
                    {
                        var page = first ? string.Empty : "<w:pageBreakBefore/>";
                        first = false;

                        builder.AddImageParagraph(picture, 24, height,
                            page + "<w:spacing w:before=\"0\" w:after=\"0\" " +
                            $"w:line=\"{line}\" w:lineRule=\"auto\"/>",
                            leadingText: "Picture ", leadingRunProperties: Times12);

                        builder.AddParagraph($"After {height} at {line}.", ZeroSpacing, Times12);
                    }
                }

                return builder;
            },

            // Floating images: text flowing around one on the left, one on the right, and one
            // that takes the full measure so text goes above and below it.
            ["images-floating"] = () =>
            {
                var builder = new DocxBuilder();
                var left = builder.AddImagePart(PngWriter.Diagonal(40));
                var right = builder.AddImagePart(PngWriter.Solid(20, 20, 40, 150, 90));
                var banner = builder.AddImagePart(PngWriter.Solid(60, 12, 200, 170, 40));

                var body = string.Join(' ',
                    Enumerable.Repeat("Text flows around the floating picture beside it.", 14));

                var middle = builder.AddImagePart(PngWriter.Solid(30, 30, 90, 40, 150));

                return builder
                    .AddAnchoredImageParagraph(left, 108, 90, body,
                        paragraphProperties: ZeroSpacing, runProperties: Times12)
                    .AddAnchoredImageParagraph(right, 108, 90, body, alignX: "right",
                        paragraphProperties: ZeroSpacing, runProperties: Times12)
                    // In the middle of the measure, where the text has room on both sides of it
                    // and Word runs each line through both. Written among the text rather than at
                    // the top of a page, so that its clearance reaches back over a line of the
                    // paragraph before it and that line has to be broken again round it.
                    .AddAnchoredImageParagraph(middle, 144, 72, body, alignX: "center",
                        paragraphProperties: ZeroSpacing, runProperties: Times12)
                    .AddAnchoredImageParagraph(banner, 360, 36, body, wrap: "topAndBottom",
                        paragraphProperties: ZeroSpacing, runProperties: Times12);
            },

            // A numbered list with a nested level, then a bullet list, then ordinary text — so the
            // labels, the hanging indents and the return to normal flow are all measurable.
            ["numbering"] = () => new DocxBuilder()
                .WithNumbering(
                    DocxBuilder.NumberingLevel(0, "decimal", "%1.") +
                    DocxBuilder.NumberingLevel(1, "lowerLetter", "%2)"),
                    DocxBuilder.NumberingLevel(0, "bullet", "•"))
                .AddParagraph("Steps to follow.", ZeroSpacing, Times12)
                .AddListParagraph("Open the package and read the content types.", 1, runProperties: Times12)
                .AddListParagraph("Follow the relationship to the main document part.", 1, runProperties: Times12)
                .AddListParagraph("Resolve the style cascade before measuring anything.", 1, 1, Times12)
                .AddListParagraph("Break the lines against real font metrics.", 1, 1, Times12)
                .AddListParagraph("Write the pages out.", 1, runProperties: Times12)
                .AddParagraph("Things to remember.", ZeroSpacing, Times12)
                .AddListParagraph("Twips are twentieths of a point.", 2, runProperties: Times12)
                .AddListParagraph("Half-points are for font sizes only.", 2, runProperties: Times12)
                .AddParagraph("End of the list.", ZeroSpacing, Times12),

            // A running header and a footer carrying a page number, over enough body text to run
            // to several pages so the number has to change.
            ["headers-footers"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithHeaderFooter(header: true,
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t>Quarterly Operations Review</w:t></w:r></w:p>")
                    .WithHeaderFooter(header: false,
                        $"<w:p><w:pPr>{ZeroSpacing}<w:jc w:val=\"center\"/></w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Page </w:t></w:r>" +
                        $"<w:fldSimple w:instr=\" PAGE \"><w:r><w:rPr>{Times12}</w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> of </w:t></w:r>" +
                        $"<w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:rPr>{Times12}</w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                        "</w:p>");

                for (var i = 1; i <= 70; i++)
                    builder.AddParagraph($"Body paragraph number {i} of seventy.", ZeroSpacing, Times12);

                return builder;
            },

            // Page numbering begun again in a section, which is what a document with a preface
            // does. The footer counts pages and totals them, so what is measured is which of those
            // follow the restart and which count the document through: the second section starts
            // again at one and the third at a number of its own, and PAGEREF points across all of
            // them at a bookmark in the first.
            ["page-numbering-restart"] = () =>
            {
                // The footer states no formatting of its own: Word writes a field result it has
                // recalculated in the document's default size rather than in whatever the result
                // it replaced was in, so a footer that asks for a size Word will not use compares
                // its own width against a different one.
                string Field(string instruction, string cached) =>
                    $"<w:fldSimple w:instr=\" {instruction} \"><w:r><w:t>{cached}</w:t></w:r></w:fldSimple>";

                var builder = new DocxBuilder()
                    .WithHeaderFooter(header: false,
                        $"<w:p><w:pPr>{ZeroSpacing}<w:jc w:val=\"center\"/></w:pPr>" +
                        "<w:r><w:t xml:space=\"preserve\">Page </w:t></w:r>" + Field("PAGE", "1") +
                        "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r>" + Field("NUMPAGES", "1") +
                        "<w:r><w:t xml:space=\"preserve\">, section </w:t></w:r>" + Field("SECTION", "1") +
                        "<w:r><w:t xml:space=\"preserve\"> of </w:t></w:r>" + Field("SECTIONPAGES", "1") +
                        "</w:p>");

                // The first section: two pages, and a bookmark on the second of them.
                for (var i = 1; i <= 60; i++)
                {
                    if (i == 50)
                    {
                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                            "<w:bookmarkStart w:id=\"1\" w:name=\"marked\"/>" +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>The marked paragraph.</w:t></w:r>" +
                            "<w:bookmarkEnd w:id=\"1\"/></w:p>");

                        continue;
                    }

                    builder.AddParagraph($"First section, paragraph {i}.", ZeroSpacing, Times12);
                }

                // The reference to the footer has to be repeated in each section: a section with
                // none of its own inherits from the one before, and the first has nothing before
                // it. The properties on a break describe the section it closes, so the numbering
                // stated here is the first section's.
                builder.AddParagraphWithSectionBreak(
                    "The last paragraph of the first section.",
                    DocxBuilder.Section([("footer:default", "rIdHF1")], type: "nextPage"), ZeroSpacing, Times12);

                for (var i = 1; i <= 50; i++)
                    builder.AddParagraph($"Second section, paragraph {i}.", ZeroSpacing, Times12);

                // And the second section is the one begun again at one.
                builder.AddParagraphWithSectionBreak(
                    "The last paragraph of the second section.",
                    DocxBuilder.Section([("footer:default", "rIdHF1")], type: "nextPage", pageNumberStart: 1),
                    ZeroSpacing, Times12);

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">The marked page is </w:t></w:r>" +
                    $"<w:fldSimple w:instr=\" PAGEREF marked \"><w:r><w:rPr>{Times12}</w:rPr><w:t>1</w:t></w:r></w:fldSimple>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                // The last section, whose own properties are the document's, begins again at
                // twenty so that a restart to something other than one is measured as well.
                // The final section's own properties, which the builder adds the footer reference
                // to itself — stating it here as well would draw the footer twice.
                builder.WithSection(DocxBuilder.Section(pageNumberStart: 20));

                return builder.AddParagraph("Third section, the last paragraph.", ZeroSpacing, Times12);
            },

            // Hebrew, which is written right to left. The paragraphs are the cases the
            // bidirectional algorithm exists for: text of one direction, text of both, a number
            // inside right-to-left text — a number is written left to right whatever surrounds it
            // — and brackets, which face the way the reader is going.
            ["hebrew"] = () =>
            {
                var builder = new DocxBuilder();

                const string bidi = "<w:bidi/>";

                void Line(string text, bool rightToLeft)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(rightToLeft ? bidi : string.Empty)}{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Hebrew below, in a paragraph that runs the ordinary way.", false);
                Line("שלום עולם", false);

                Line("שלום עולם", true);
                Line("שלום ISO עולם", true);
                Line("שלום 8601 עולם", true);
                Line("שלום (עולם) שלום", true);
                Line("שלום, עולם. שלום!", true);
                Line("A Latin sentence in a paragraph that runs the other way.", true);

                return builder;
            },

            // Arabic, whose letters join to one another and take a different shape according to
            // what stands beside them. The lines are the cases that decide it: a word of letters
            // that all join, one holding a letter that joins on the right only and so breaks in
            // the middle, the pair that may not be written as two, and a word with vowel marks,
            // which stand between letters without breaking the join.
            ["arabic"] = () =>
            {
                var builder = new DocxBuilder();

                var arial = "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/><w:sz w:val=\"24\"/>";

                void Line(string text, bool rightToLeft = true)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(rightToLeft ? "<w:bidi/>" : string.Empty)}{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{arial}</w:rPr><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Arabic below, joined and read from the right.", false);
                Line("مرحبا بالعالم");
                Line("كتاب ودار");
                Line("لا إله");
                Line("بِسْمِ اللَّهِ");
                Line("العربية 1445 and Latin");

                return builder;
            },

            // The Indic scripts, which neither join their letters nor draw them in the order they
            // are stored. A vowel written before the consonant it is pronounced after; consonants
            // written as one conjunct shape; a mark that belongs to the start of a cluster and is
            // drawn at the end of it. Each line below is one of those, and none of them can be
            // drawn by a converter that walks the characters and looks each one up.
            ["indic"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string text, string family, int halfPoints = 28)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>" +
                        $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\"/>" +
                        $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/></w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                const string devanagari = "Devanagari Sangam MN";

                Line("Devanagari, Tamil and Bengali below.", "Arial", 24);

                Line("नमस्ते", devanagari); // namaste: a conjunct in the middle
                Line("हिन्दी", devanagari); // hindi: a vowel drawn before the consonant
                Line("क्षत्रिय", devanagari); // kshatriya: a three-consonant conjunct
                Line("कर्म", devanagari); // karma: a repha, drawn at the end of the cluster
                Line("मुंबई 400", devanagari); // with digits, which are drawn as they are

                Line("தமிழ்", "Tamil Sangam MN"); // tamil
                Line("বাংলা", "Bangla Sangam MN"); // bangla

                return builder;
            },

            // How tall a line of East Asian text is. The wrapping fixture showed Word giving two
            // faces whose own metrics differ the same line height, so the height is measured here
            // the way line-box-probe measures a Latin one: zero spacing everywhere, two lines of
            // a face, and the gap between the baselines is that face's ascent plus its descent.
            //
            // Four faces, chosen because their metrics disagree. Mincho and Gothic call an em
            // exactly one line; KaiTi asks for 1.14 of one; MingLiU for 1.20 and at a different
            // number of units to the em. If Word gives all four the same height, the height is a
            // rule about the script; if it gives each a different one, it is read from the face.
            ["east-asian-line-box-probe"] = () =>
            {
                var builder = new DocxBuilder();

                void Pair(string family, int halfPoints, string text = "日本語")
                {
                    for (var line = 0; line < 2; line++)
                    {
                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>" +
                            $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" " +
                            $"w:cs=\"{family}\" w:eastAsia=\"{family}\"/>" +
                            $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/>" +
                            $"</w:rPr><w:t>{text}</w:t></w:r></w:p>");
                    }
                }

                Pair("MS Mincho", 24);
                Pair("MS Gothic", 24);
                Pair("KaiTi", 24);
                Pair("MingLiU", 24);

                // And one face twice over, to say whether the height is a multiple of the size.
                Pair("MS Mincho", 40);

                // The same face drawing Latin letters, which says whether the height belongs to
                // the face or to the script written in it.
                Pair("MS Mincho", 24, "Latin");

                return builder;
            },

            // The scripts that are written without spaces between the words, and so cannot be
            // broken into lines by looking for one. Each paragraph is long enough to need three
            // or four lines of it.
            ["wrapping"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string text, string family, int halfPoints = 24)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>" +
                        $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\" " +
                        $"w:eastAsia=\"{family}\"/>" +
                        $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/></w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Lines broken where no space says they may.", "Arial");

                // Japanese: broken between characters, but not before a full stop or a closing
                // bracket, and not after an opening one.
                Line("日本語の文章は単語の区切りに空白を置かないので、行の折り返しは文字と文字の" +
                     "あいだで起こります。ただし句読点や閉じ括弧を行頭に置くことはできません" +
                     "（このような括弧のことです）。長い文章を組むときはそこが問題になります。",
                    "MS Mincho");

                // Chinese, the same rules and a different set of characters.
                Line("中文的排版也不使用空格来分隔词语，所以换行发生在字与字之间。" +
                     "标点符号不能出现在一行的开头，这一点和日文相同。" +
                     "这段文字足够长，需要折成好几行才能排下。", "KaiTi");

                Line("ประเทศไทยมีประชากรประมาณเจ็ดสิบล้านคนและกรุงเทพมหานครเป็นเมืองหลวง" +
                     "ที่ใหญ่ที่สุดของประเทศและเป็นศูนย์กลางการค้าและการปกครองมาตั้งแต่อดีต",
                    "Ayuthaya");

                return builder;
            },

            // Faces that describe their shaping in Apple's tables and carry no OpenType tables
            // at all. There are a hundred and sixty of them on this machine, and a converter that
            // reads only OpenType draws their scripts as rows of unjoined letters.
            //
            // Thai is not here although Thonburi is one of these faces: asked for it, Word draws
            // the line in a font of its own instead, so there would be nothing to compare. Nor is
            // the word for kshatriya, which Word's own reading of these tables draws two points
            // wider than HarfBuzz's. Nor Malayalam, which Word does not draw at all: asked for a
            // line of it in Malayalam Sangam MN, its export holds nothing where the line should
            // be, so the table that says where that face's glyphs go can only be checked against
            // HarfBuzz. All three are, in AppleLayoutTests.
            ["apple"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string text, string family, int halfPoints = 28, bool kerned = false)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>" +
                        $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\"/>" +
                        $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/>" +
                        (kerned ? "<w:kern w:val=\"2\"/>" : string.Empty) +
                        $"</w:rPr><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Faces with no OpenType tables at all.", "Arial", 24);

                Line("नमस्ते", "Devanagari MT"); // a conjunct made by a state machine
                Line("हिन्दी", "Devanagari MT"); // a vowel drawn before its consonant
                Line("સંસ્કૃત", "Gujarati MT"); // Gujarati, the same machinery
                Line("ਪੰਜਾਬੀ", "Gurmukhi MT"); // and Gurmukhi

                return builder;
            },

            // A script shaped by rules that belong to no script in particular. Sinhala rather than
            // one of the seventy others because Word can draw it: asked for Tibetan, Javanese or
            // Cham on this machine, Word draws the letters side by side without stacking or
            // reordering anything, so there would be nothing to compare against. What the engine
            // does for the rest is compared against HarfBuzz instead, script by script.
            ["universal"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string text, string family, int halfPoints = 28)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>" +
                        $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\"/>" +
                        $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/></w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Sinhala below, shaped by rules no script owns.", "Arial", 24);

                Line("සිංහල", "Sinhala Sangam MN"); // a vowel above and one after
                Line("ශ්‍රී ලංකා", "Sinhala Sangam MN"); // with the joiner that asks for a conjunct
                Line("ක්‍ෂ", "Sinhala Sangam MN"); // two letters written as one shape
                Line("පොත", "Sinhala Sangam MN"); // a vowel written on both sides at once
                Line("කෙටි", "Sinhala Sangam MN"); // one written to the left of its letter

                return builder;
            },

            // The South-East Asian scripts. Thai and Lao are written without spaces between
            // words and stack their vowels and tone marks above and below the consonants; Khmer
            // and Myanmar reorder like the Indic scripts they descend from.
            ["southeast-asian"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string text, string family, int halfPoints = 28)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>" +
                        $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\"/>" +
                        $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/></w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Thai, Lao, Khmer and Myanmar below.", "Arial", 24);

                Line("สวัสดี", "Ayuthaya"); // sawatdi: a vowel above and one after
                Line("ภาษาไทย", "Ayuthaya"); // phasa thai: a vowel written before its consonant
                Line("ກະລຸນາ", "Lao Sangam MN"); // karuna, in Lao
                Line("ភាសាខ្មែរ", "Khmer Sangam MN"); // the Khmer language, with a subscript consonant
                Line("မြန်မာ", "Noto Sans Myanmar"); // myanmar, with a medial ra drawn before its base

                return builder;
            },

            // Pointed Hebrew, whose vowel points are marks with no place of their own: the font
            // says where each attaches, and a converter that draws them where the pen happens to
            // be puts the meaning of the words somewhere else. The Latin line carries an accent
            // with no precomposed form, which is the same question in the other direction.
            ["marks"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string text, bool rightToLeft)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{(rightToLeft ? "<w:bidi/>" : string.Empty)}{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                Line("Pointed Hebrew, and the same without its points:", false);
                Line("שָׁלוֹם עוֹלָם", true);
                Line("שלום עולם", true);
                Line("Latin with a mark of its own: q\u0301 a\u0327", false);

                return builder;
            },

            // Text set in a face that cannot draw all of it. Arial Hebrew has no Latin letters at
            // all and Times New Roman has no Japanese, so each line here asks its font for
            // something the font has not got, and what is measured is that the text arrives.
            ["font-fallback"] = () =>
            {
                var builder = new DocxBuilder();

                void Line(string family, string text)
                {
                    var run = $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\"/>" +
                              "<w:sz w:val=\"24\"/>";

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{run}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{Escape(text)}</w:t></w:r></w:p>");
                }

                // The fonts these tests pin are Latin faces and Arial Hebrew, so what can be
                // shown here is a face borrowed for the Latin. A face borrowed for a script none
                // of the pinned fonts has would depend on what the machine happens to hold.
                Line("Times New Roman", "Times New Roman, which has the Latin and the Hebrew: שלום");
                Line("Arial Hebrew", "Arial Hebrew, which has no Latin at all: שלום");
                Line("Arial Hebrew", "Numbers 8601 and punctuation, in a face with no Latin: שלום");

                return builder;
            },

            // A section of columns that ends part way down a page, which Word evens out rather
            // than filling the first column and leaving the last short. Three cases at once: a
            // section closed by a continuous break, one closed by a break to a new page, and one
            // that ends because the document does.
            ["columns-balanced"] = () =>
            {
                var builder = new DocxBuilder();

                // Long enough to wrap inside a column, so that the measure each line was broken
                // against is visible in the export as well as where the line sits.
                void Lines(string label, int count)
                {
                    for (var i = 1; i <= count; i++)
                    {
                        builder.AddParagraph(
                            $"{label} paragraph {i}, written long enough that it wraps inside a " +
                            "column rather than fitting on one line of it.",
                            ZeroSpacing, Times12);
                    }
                }

                // A dozen paragraphs over two columns, closed by a continuous break — the case
                // that is supposed to even them out.
                Lines("Continuous", 11);
                builder.AddParagraphWithSectionBreak(
                    "Continuous paragraph 12, the last of its section.",
                    DocxBuilder.Section(columns: 2, type: "continuous"), ZeroSpacing, Times12);

                // A section of one column between the two, so the break has something to change.
                builder.AddParagraphWithSectionBreak(
                    "One column between the two.",
                    DocxBuilder.Section(type: "continuous"), ZeroSpacing, Times12);

                Lines("Paged", 11);
                builder.AddParagraphWithSectionBreak(
                    "Paged paragraph 12, the last of its section.",
                    DocxBuilder.Section(columns: 2, type: "nextPage"), ZeroSpacing, Times12);

                // An even number of lines, so that where a column divides exactly in half is
                // measured too rather than left to whichever rounding looks reasonable.
                for (var i = 1; i <= 9; i++)
                    builder.AddParagraph($"Even line {i}.", ZeroSpacing, Times12);

                builder.AddParagraphWithSectionBreak("Even line 10.",
                    DocxBuilder.Section(columns: 2, type: "nextPage"), ZeroSpacing, Times12);

                builder.AddParagraphWithSectionBreak("After the even section.",
                    DocxBuilder.Section(type: "continuous"), ZeroSpacing, Times12);

                Lines("Last", 11);
                builder.AddParagraph("Last paragraph 12, the last of the document.", ZeroSpacing, Times12);

                return builder.WithSection(DocxBuilder.Section(columns: 2));
            },

            // Equations, which are a document within a document: their own markup, their own
            // face, and a typesetting of their own that Word does with the MATH table of Cambria
            // Math rather than by laying out lines.
            //
            // One to a paragraph, each named on the line before it so that a line out of place is
            // obvious, and the last two are what a reader that does not know a construct has to do
            // with it: keep what it holds rather than drop it.
            ["equations"] = () =>
            {
                static string Run(string text) =>
                    $"<m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>" +
                    $"<w:sz w:val=\"24\"/></w:rPr><m:t>{DocxBuilder.Escape(text)}</m:t></m:r>";

                static string Element(string name, string inner) => $"<m:{name}>{inner}</m:{name}>";

                static string Math(string inner) => $"<m:oMath>{inner}</m:oMath>";

                static string Fraction(string numerator, string denominator, string type = "") =>
                    "<m:f>" +
                    (type.Length > 0 ? $"<m:fPr><m:type m:val=\"{type}\"/></m:fPr>" : string.Empty) +
                    Element("num", numerator) + Element("den", denominator) + "</m:f>";

                static string Superscript(string body, string sup) =>
                    "<m:sSup>" + Element("e", body) + Element("sup", sup) + "</m:sSup>";

                static string Subscript(string body, string sub) =>
                    "<m:sSub>" + Element("e", body) + Element("sub", sub) + "</m:sSub>";

                static string SubSuperscript(string body, string sub, string sup) =>
                    "<m:sSubSup>" + Element("e", body) + Element("sub", sub) +
                    Element("sup", sup) + "</m:sSubSup>";

                static string Radical(string body, string degree = "") =>
                    "<m:rad>" +
                    (degree.Length > 0
                        ? "<m:radPr><m:degHide m:val=\"0\"/></m:radPr>"
                        : "<m:radPr><m:degHide m:val=\"1\"/></m:radPr>") +
                    Element("deg", degree) + Element("e", body) + "</m:rad>";

                static string Delimited(string inner, string open = "(", string close = ")") =>
                    "<m:d><m:dPr>" +
                    $"<m:begChr m:val=\"{open}\"/><m:endChr m:val=\"{close}\"/>" +
                    "</m:dPr>" + Element("e", inner) + "</m:d>";

                static string Nary(string character, string sub, string sup, string body) =>
                    "<m:nary><m:naryPr>" +
                    $"<m:chr m:val=\"{character}\"/><m:limLoc m:val=\"undOvr\"/>" +
                    "</m:naryPr>" + Element("sub", sub) + Element("sup", sup) +
                    Element("e", body) + "</m:nary>";

                static string Function(string name, string argument) =>
                    "<m:func>" + Element("fName", name) + Element("e", argument) + "</m:func>";

                string Line(string label, string math, bool display = false) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                    $"<w:t xml:space=\"preserve\">{label} </w:t></w:r>" +
                    (display ? $"<m:oMathPara>{math}</m:oMathPara>" : math) +
                    "</w:p>";

                var builder = new DocxBuilder();

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                    "<w:t xml:space=\"preserve\">Before </w:t></w:r>" +
                    Math(Run("x+y=z")) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>");

                builder.AddRawParagraph(Line("Fraction:",
                    Math(Fraction(Run("a"), Run("b")))));

                builder.AddRawParagraph(Line("Fraction of sums:",
                    Math(Fraction(Run("x+1"), Run("x-1")))));

                builder.AddRawParagraph(Line("Skewed:",
                    Math(Fraction(Run("a"), Run("b"), "skw"))));

                builder.AddRawParagraph(Line("Superscript:", Math(Superscript(Run("x"), Run("2")))));
                builder.AddRawParagraph(Line("Subscript:", Math(Subscript(Run("a"), Run("n")))));
                builder.AddRawParagraph(Line("Both:",
                    Math(SubSuperscript(Run("x"), Run("i"), Run("2")))));

                builder.AddRawParagraph(Line("Root:", Math(Radical(Run("x+1")))));
                builder.AddRawParagraph(Line("Cube root:", Math(Radical(Run("x"), Run("3")))));

                builder.AddRawParagraph(Line("Delimited:", Math(Delimited(Run("a+b")))));
                builder.AddRawParagraph(Line("Delimited fraction:",
                    Math(Delimited(Fraction(Run("a"), Run("b"))))));

                builder.AddRawParagraph(Line("Sum:",
                    Math(Nary("\u2211", Run("i=1"), Run("n"), Superscript(Run("i"), Run("2"))))));

                builder.AddRawParagraph(Line("Integral:",
                    Math(Nary("\u222b", Run("0"), Run("1"), Run("x dx")))));

                builder.AddRawParagraph(Line("Function:", Math(Function(Run("sin"), Run("x")))));

                // On a line of its own, centred, which is what a display equation is — so the
                // words that name it have to be on a line of their own as well.
                builder.AddRawParagraph(Line("The quadratic formula:", string.Empty));

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><m:oMathPara>" +
                    Math(Run("x=") + Fraction(
                        Run("-b\u00b1") + Radical(Superscript(Run("b"), Run("2")) + Run("-4ac")),
                        Run("2a"))) +
                    "</m:oMathPara></w:p>");

                // And two a reader may not know: what matters is that it keeps what they hold.
                builder.AddRawParagraph(Line("Matrix:",
                    Math("<m:m><m:mPr><m:mcs><m:mc><m:mcPr>" +
                         "<m:count m:val=\"2\"/><m:mcJc m:val=\"center\"/>" +
                         "</m:mcPr></m:mc></m:mcs></m:mPr>" +
                         "<m:mr>" + Element("e", Run("1")) + Element("e", Run("2")) + "</m:mr>" +
                         "<m:mr>" + Element("e", Run("3")) + Element("e", Run("4")) + "</m:mr>" +
                         "</m:m>")));

                builder.AddRawParagraph(Line("Accented:",
                    Math("<m:acc><m:accPr><m:chr m:val=\"\u0303\"/></m:accPr>" +
                         Element("e", Run("x")) + "</m:acc>")));

                return builder;
            },

            // What an equation asks of the line that holds it, which is the one part of setting
            // one that Word's own seventeen could not settle. Word grows a line with what the
            // equation in it holds, and the question is by how much and measured from what: the
            // ink of the glyphs, the ascent and descent their face states, or something between.
            //
            // Every probe stands between two rails — a two point full stop on a line of its own —
            // and carries a two point full stop of its own before the equation, so that the
            // baseline of the line itself can be found whatever the equation puts on it. The
            // paragraph marks are two point as well, so that nothing but the equation decides how
            // tall a line comes out. Then, for the nth probe:
            //
            //     ascent  = (probe - rail before) - the rail's descent
            //     descent = (rail after - probe)  - the rail's ascent
            //
            // and the rail's own two are had from the pair of rails at the top, which have no
            // probe between them.
            //
            // The eighteen are chosen so that each pair differs in one thing only:
            //
            //   x, b, y      a letter whose ink is low, one that reaches up, one that hangs down
            //   sum          one glyph far taller than any letter
            //   x at 24, 6   the same letter twice more, to see what the answer is a share of
            //   x², x^x      a raised script whose ink is tall, and one whose ink is low
            //   x_i          a lowered one
            //   x/x, 1/1     two fractions differing only in the ink of what is in them
            //   x/y          and one whose denominator hangs below its own baseline
            //   root, (x)    a glyph stretched a little, and a bracket at its plain size
            //   (a/b)        a bracket grown to a taller shape than the face's plain one
            //   sum with     an operator with limits beside it
            //   a/b alone    the same fraction on a line of its own, which Word sets larger
            //   x^(x^x)      a script of a script, which is the smallest thing an equation holds
            ["math-line-box-probe"] = () =>
            {
                const string Mark =
                    "<w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
                    "<w:sz w:val=\"4\"/></w:rPr>";

                static string Run(string text, int halfPoints = 24) =>
                    $"<m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>" +
                    $"<w:sz w:val=\"{halfPoints}\"/></w:rPr>" +
                    $"<m:t>{DocxBuilder.Escape(text)}</m:t></m:r>";

                static string Element(string name, string inner) => $"<m:{name}>{inner}</m:{name}>";

                static string Fraction(string numerator, string denominator) =>
                    "<m:f>" + Element("num", numerator) + Element("den", denominator) + "</m:f>";

                static string Superscript(string body, string sup) =>
                    "<m:sSup>" + Element("e", body) + Element("sup", sup) + "</m:sSup>";

                static string Subscript(string body, string sub) =>
                    "<m:sSub>" + Element("e", body) + Element("sub", sub) + "</m:sSub>";

                static string Radical(string body) =>
                    "<m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr>" +
                    Element("deg", string.Empty) + Element("e", body) + "</m:rad>";

                static string Delimited(string inner) =>
                    "<m:d><m:dPr><m:begChr m:val=\"(\"/><m:endChr m:val=\")\"/></m:dPr>" +
                    Element("e", inner) + "</m:d>";

                static string Nary(string character, string sub, string sup, string body) =>
                    "<m:nary><m:naryPr>" +
                    $"<m:chr m:val=\"{character}\"/><m:limLoc m:val=\"subSup\"/>" +
                    "</m:naryPr>" + Element("sub", sub) + Element("sup", sup) +
                    Element("e", body) + "</m:nary>";

                var builder = new DocxBuilder();

                // A rail: nothing on it but a two point stop, so that its own ascent and descent
                // are as small as a line can be made and are known.
                void Rail() => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");

                void Probe(string math, bool display = false) => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    (display ? $"<m:oMathPara><m:oMath>{math}</m:oMath></m:oMathPara>"
                             : $"<m:oMath>{math}</m:oMath>") +
                    "</w:p>");

                // The pair that says what a rail's own ascent and descent come to.
                Rail();
                Rail();

                foreach (var equation in new[]
                         {
                             Run("x"),
                             Run("b"),
                             Run("y"),
                             Run("\u2211"),
                             Run("x", 48),
                             Run("x", 12),
                             Superscript(Run("x"), Run("2")),
                             Superscript(Run("x"), Run("x")),
                             Subscript(Run("x"), Run("i")),
                             Fraction(Run("x"), Run("x")),
                             Fraction(Run("1"), Run("1")),
                             Fraction(Run("x"), Run("y")),
                             Radical(Run("x")),
                             Delimited(Run("x")),
                             Delimited(Fraction(Run("a"), Run("b"))),
                             Nary("\u2211", Run("i=1"), Run("n"), Run("x")),
                             Superscript(Run("x"), Superscript(Run("x"), Run("x")))
                         })
                {
                    Probe(equation);
                    Rail();
                }

                // A second round, on a page of its own, to settle what the first could not: what
                // the room over an equation is a share of, and what the smallest a line holding
                // one can be is a share of. Both are answered by asking the same questions at
                // four times the size, where a quarter point of rounding stops mattering.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:pageBreakBefore/>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");
                Rail();

                foreach (var equation in new[]
                         {
                             Superscript(Run("x", 48), Run("2", 48)),
                             Subscript(Run("x", 48), Run("i", 48)),
                             Fraction(Run("1", 48), Run("1", 48)),
                             Fraction(Run("x", 48), Run("y", 48)),
                             Radical(Run("x", 48)),
                             Delimited(Fraction(Run("a", 48), Run("b", 48))),

                             // The same construct set small, to say whether what a line comes to
                             // follows the equation's own type or the paragraph's.
                             Superscript(Run("x", 12), Run("2", 12)),

                             // Two sizes in one equation, to say which of them it follows.
                             Run("x", 12) + Run("x", 48)
                         })
                {
                    Probe(equation);
                    Rail();
                }

                // Nothing here stands on a line of its own: an oMathPara has to be the whole of
                // its paragraph for Word to set it as one, which leaves nothing on the line to
                // say where its baseline is. What a display equation comes to is measured in the
                // equations fixture instead, against Word's own setting of the quadratic formula.

                return builder;
            },

            // Where a script sits along the letter it is on. The face states two things about
            // that — how far the letter leans (its italic correction) and a kern for the corner
            // the script sits in, which MathKernInfo gives as a staircase of values by height —
            // and Word's own equations use them in some places and not others. This says where.
            //
            // Every probe is a script on a letter chosen for what the face says about it:
            //
            //   x^2, x_2      50 units of top-right kern whatever the height, and a bottom-right
            //                 staircase that turns from -20 to 0 at 690
            //   b^2           a top-right staircase that turns from 0 to 20 at 444
            //   i^2           one that turns from 0 to 40 at 984, which a script of twelve point
            //                 does not reach and one of twenty does
            //   n^2           one that turns from -20 to 0 at 612
            //   A^2           one that turns from 0 to -75 at 620, the only negative one here
            //   f_x, f^x      the largest of all: -400 units under 420 and -320 under 720
            //   x^A, x_A      where what the face says belongs to the script rather than the
            //                 letter: A leans into the corner it sits in
            //
            // The first three are the same equation three ways over — nothing stated, twelve point
            // stated, sixteen point stated — because Word's own equations show the kern applied
            // where the letters are the size the equation is set at and not where they are larger,
            // and one of the three has to say whether that is really the rule.
            ["math-kern-probe"] = () =>
            {
                const string Mark =
                    "<w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
                    "<w:sz w:val=\"4\"/></w:rPr>";

                static string Run(string text, int? halfPoints = null) =>
                    "<m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>" +
                    (halfPoints is { } size ? $"<w:sz w:val=\"{size}\"/>" : string.Empty) +
                    $"</w:rPr><m:t>{DocxBuilder.Escape(text)}</m:t></m:r>";

                static string Element(string name, string inner) => $"<m:{name}>{inner}</m:{name}>";

                static string Superscript(string body, string sup) =>
                    "<m:sSup>" + Element("e", body) + Element("sup", sup) + "</m:sSup>";

                static string Subscript(string body, string sub) =>
                    "<m:sSub>" + Element("e", body) + Element("sub", sub) + "</m:sSub>";

                var builder = new DocxBuilder().WithStyles(
                    """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:docDefaults>
                        <w:rPrDefault>
                          <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr>
                        </w:rPrDefault>
                      </w:docDefaults>
                      <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                      <w:style w:type="paragraph" w:styleId="Big">
                        <w:name w:val="Big"/>
                        <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="32"/></w:rPr>
                      </w:style>
                    </w:styles>
                    """);

                void Rail() => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");

                void Probe(string math) => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    $"<m:oMath>{math}</m:oMath></w:p>");

                Rail();
                Rail();

                foreach (var equation in new[]
                         {
                             Superscript(Run("x"), Run("2")),
                             Superscript(Run("x", 24), Run("2", 24)),
                             Superscript(Run("x", 32), Run("2", 32)),

                             Subscript(Run("x"), Run("2")),
                             Superscript(Run("b"), Run("2")),
                             Superscript(Run("i"), Run("2")),
                             Superscript(Run("n"), Run("2")),
                             Superscript(Run("A"), Run("2")),
                             Subscript(Run("f"), Run("x")),
                             Superscript(Run("f"), Run("x")),
                             Superscript(Run("x"), Run("A")),
                             Subscript(Run("x"), Run("A")),

                             // The two that say which height the staircase is read at: the ink of
                             // the letter itself, or the ink of what is attached to it. A full
                             // stop is small enough to be under the step where a two is over it —
                             // i's turns at 984 and A's at 616.
                             Superscript(Run("i"), Run(".")),
                             Subscript(Run("."), Run("A"))
                         })
                {
                    Probe(equation);
                    Rail();
                }

                // And the same equation again in a sixteen point paragraph with twelve point
                // runs, to say whether what stops Word kerning is the letters being larger than
                // the equation or merely being a different size from it.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:pStyle w:val=\"Big\"/>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    $"<m:oMath>{Superscript(Run("x", 24), Run("2", 24))}</m:oMath></w:p>");
                Rail();

                return builder;
            },

            // When a bracket takes the next shape up. The face keeps a series of them — eight for
            // a round bracket, each taller and wider than the last — and Word reaches further up
            // the series the more the bracket has to cover. How much further was measured from two
            // brackets at twelve point as nine tenths of what the bracket holds, which is TeX's
            // own factor, and math-line-box-probe showed that answer failing for a bracket round
            // something twice the size the equation is set at.
            //
            // This walks a bracket up the whole series. Every probe is a bracket round a single
            // letter, the letter growing from twelve point to seventy-two while the equation stays
            // at twelve, so the bracket is drawn at twelve throughout and only the shape changes.
            // Which shape Word picked is read straight off the page: the eight differ in width.
            //
            // The second page does the same at twenty-four point, to say whether the rule is a
            // share of what is held or a distance.
            ["math-bracket-probe"] = () =>
            {
                const string Mark =
                    "<w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
                    "<w:sz w:val=\"4\"/></w:rPr>";

                static string Run(string text, int halfPoints) =>
                    $"<m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>" +
                    $"<w:sz w:val=\"{halfPoints}\"/></w:rPr>" +
                    $"<m:t>{DocxBuilder.Escape(text)}</m:t></m:r>";

                static string Element(string name, string inner) => $"<m:{name}>{inner}</m:{name}>";

                static string Delimited(string inner) =>
                    "<m:d><m:dPr><m:begChr m:val=\"(\"/><m:endChr m:val=\")\"/></m:dPr>" +
                    Element("e", inner) + "</m:d>";

                static string Fraction(string numerator, string denominator) =>
                    "<m:f>" + Element("num", numerator) + Element("den", denominator) + "</m:f>";

                var builder = new DocxBuilder().WithStyles(
                    """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:docDefaults>
                        <w:rPrDefault>
                          <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr>
                        </w:rPrDefault>
                      </w:docDefaults>
                      <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                      <w:style w:type="paragraph" w:styleId="Big">
                        <w:name w:val="Big"/>
                        <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="48"/></w:rPr>
                      </w:style>
                    </w:styles>
                    """);

                void Rail(string style = "") => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{style}{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");

                void Probe(string math, string style = "") => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{style}{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    $"<m:oMath>{math}</m:oMath></w:p>");

                Rail();

                // A letter, growing. Fine steps low down, where the first shapes are close
                // together, and coarser ones after.
                foreach (var halfPoints in new[] { 24, 28, 32, 36, 40, 44, 48, 56, 64, 72, 88, 104 })
                {
                    Probe(Delimited(Run("x", halfPoints)));
                    Rail();
                }

                // And a fraction, which reaches further under the line than any letter does.
                foreach (var halfPoints in new[] { 24, 36, 48, 72 })
                {
                    Probe(Delimited(Fraction(Run("a", halfPoints), Run("b", halfPoints))));
                    Rail();
                }

                // The same again in a twenty-four point paragraph, where the bracket is drawn at
                // twenty-four and the same letters ask less of it.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:pStyle w:val=\"Big\"/><w:pageBreakBefore/>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");

                foreach (var halfPoints in new[] { 48, 64, 88, 104, 144 })
                {
                    Probe(Delimited(Run("x", halfPoints)), "<w:pStyle w:val=\"Big\"/>");
                    Rail("<w:pStyle w:val=\"Big\"/>");
                }

                // And last of all, past the end of the face's own series: a bracket round a
                // seventy-two point letter in a twelve point equation, which has to be built out
                // of pieces. It goes last because Word writes each piece as a character of its
                // own where this writes the bracket once, so its page holds lines of text that
                // ours does not and nothing should follow them.
                Probe(Delimited(Run("x", 144)));
                Rail();

                return builder;
            },

            // How a line divides itself above and below its baseline. The height of a single
            // spaced line is the face's ascender, descender and line gap added up, which
            // line-box-probe settled; where the baseline sits inside that height is a second
            // question, and this asks it.
            //
            // A page whose first paragraph is one letter answers it directly: the top of the text
            // is the margin, so Word's first baseline is the margin plus the ascent of that line,
            // and nothing else can be in the way. Four faces at eight sizes, each on its own page,
            // each with its paragraph mark set to the same face and size so the mark cannot be
            // what decides the height. The largest sizes pin the answer closest: at forty-eight
            // point the grid Word rounds to is a four-hundredth of the size.
            ["line-ascent-probe"] = () =>
            {
                // Times New Roman and Arial at every half point from six to twenty, which is
                // dense enough to show the shape of the rule rather than a handful of points on
                // it, and Cambria and Calibri at a spread wide enough to say the rule is the same
                // one. Seventy-four pages.
                (string Face, int[] Sizes)[] faces =
                [
                    ("Times New Roman", [.. Enumerable.Range(12, 29)]),
                    ("Arial", [.. Enumerable.Range(12, 29)]),
                    ("Cambria", [12, 16, 20, 22, 24, 32, 48, 96]),
                    ("Calibri", [12, 16, 20, 22, 24, 32, 48, 96])
                ];

                var builder = new DocxBuilder();
                var first = true;

                foreach (var (face, halfPoints) in faces)
                {
                    foreach (var size in halfPoints)
                    {
                        var font = $"<w:rFonts w:ascii=\"{face}\" w:hAnsi=\"{face}\"/>" +
                                   $"<w:sz w:val=\"{size}\"/>";

                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacingNewPage)}" +
                            $"<w:rPr>{font}</w:rPr></w:pPr>" +
                            $"<w:r><w:rPr>{font}</w:rPr><w:t>H</w:t></w:r></w:p>");

                        first = false;
                    }
                }

                return builder;
            },

            // Where a line lands on the page. Word puts every baseline it writes on a grid of one
            // three-hundredth of an inch, which is the same grid it rounds a font size to, and the
            // question this asks is what it rounds: the height of each line, or the place the line
            // ends up.
            //
            // Nine pages, forty single-spaced lines each, nothing on a line but a hyphen. The
            // first four are Times New Roman at sizes picked so that the stated size and the size
            // Word draws at come apart: two point is drawn at 1.92, five at 5.04, eleven at 11.04
            // — the first rounding down and the others up, so the two answers differ in sign as
            // well as size — and twelve point, which is already on the grid, is the control. Over
            // thirty-nine gaps the two answers stand three and a half points apart at two point,
            // which no amount of rounding could confuse.
            //
            // The remaining five say what the line height of a face actually is, to within a
            // hundredth of a point: thirty-nine gaps divide the grid step down that far. Which
            // face-and-size pairs are here is not arbitrary — they are the ones where the ascent
            // measured in line-ascent-probe turns on the fourth decimal of the height.
            ["line-grid-probe"] = () =>
            {
                (string Face, int HalfPoints)[] blocks =
                [
                    ("Times New Roman", 4), ("Times New Roman", 10), ("Times New Roman", 22),
                    ("Times New Roman", 24), ("Cambria", 12), ("Cambria", 22),
                    ("Arial", 24), ("Arial", 12), ("Calibri", 22)
                ];

                var builder = new DocxBuilder();
                var first = true;

                foreach (var (face, halfPoints) in blocks)
                {
                    var font = $"<w:rFonts w:ascii=\"{face}\" w:hAnsi=\"{face}\"/>" +
                               $"<w:sz w:val=\"{halfPoints}\"/>";

                    for (var line = 0; line < 40; line++)
                    {
                        var spacing = line == 0 && !first ? ZeroSpacingNewPage : ZeroSpacing;

                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{spacing}<w:rPr>{font}</w:rPr></w:pPr>" +
                            $"<w:r><w:rPr>{font}</w:rPr><w:t>-</w:t></w:r></w:p>");
                    }

                    first = false;
                }

                return builder;
            },

            // How a line divides itself above and below its baseline. The height of a single
            // spaced line is the face's ascender, descender and line gap added up, which
            // line-box-probe settled; where the baseline sits inside that height is a second
            // question, and this asks it.
            //
            // A page whose first paragraph is one letter answers it directly: the top of the text
            // is the margin, so Word's first baseline is the margin plus the ascent of that line,
            // and nothing else can be in the way. Four faces at eight sizes, each on its own page,
            // each with its paragraph mark set to the same face and size so the mark cannot be
            // what decides the height. The largest sizes pin the answer closest: at forty-eight
            // point the grid Word rounds to is a four-hundredth of the size.
            // Where a line lands on the page. Word puts every baseline it writes on a grid of one
            // three-hundredth of an inch, which is the same grid it rounds a font size to, and the
            // question this asks is what it rounds: the height of each line, or the place the line
            // ends up.
            //
            // Four pages, forty single-spaced lines each, nothing on a line but a hyphen. The sizes
            // are picked so that the stated size and the size Word draws at come apart: two point
            // is drawn at 1.92, five at 4.8, eleven at 11.04 — the first two rounding down and the
            // third up, so the two answers differ in sign as well as size — and twelve point, which
            // is already on the grid, holds the fourth page as a control. Over thirty-nine gaps the
            // two answers stand three and a half points apart at two point and nine at five, which
            // no amount of rounding could confuse.
            // Where the limits of a sum or an integral go when they stand beside it. The equations
            // fixture holds one of each and they disagree: Word places the integral's by the same
            // rules a script follows, from the operator's own ink, and places the sum's at the
            // plain shifts the table states. Both of them say undOvr in the markup and both are
            // set inline, so the markup cannot be what decides it.
            //
            // This asks the question directly. Each operator appears twice over — once saying its
            // limits go above and below, once saying they go beside — so that what the markup says
            // and what the operator is can be told apart. Two of each kind: a sum and a product,
            // which Word writes with their limits above and below; an integral and a contour
            // integral, which take theirs beside.
            ["math-nary-probe"] = () =>
            {
                const string Mark =
                    "<w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
                    "<w:sz w:val=\"4\"/></w:rPr>";

                static string Run(string text) =>
                    "<m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>" +
                    $"</w:rPr><m:t>{DocxBuilder.Escape(text)}</m:t></m:r>";

                static string Element(string name, string inner) => $"<m:{name}>{inner}</m:{name}>";

                static string Nary(string character, string location, string sub, string sup) =>
                    "<m:nary><m:naryPr>" +
                    $"<m:chr m:val=\"{character}\"/><m:limLoc m:val=\"{location}\"/>" +
                    "</m:naryPr>" +
                    (sub.Length > 0 ? Element("sub", Run(sub)) : "<m:sub/>") +
                    (sup.Length > 0 ? Element("sup", Run(sup)) : "<m:sup/>") +
                    Element("e", Run("x")) + "</m:nary>";

                var builder = new DocxBuilder().WithStyles(
                    """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:docDefaults>
                        <w:rPrDefault>
                          <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="24"/></w:rPr>
                        </w:rPrDefault>
                      </w:docDefaults>
                      <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                      <w:style w:type="paragraph" w:styleId="Big">
                        <w:name w:val="Big"/>
                        <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="48"/></w:rPr>
                      </w:style>
                    </w:styles>
                    """);

                void Rail() => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");

                void Probe(string math) => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    $"<m:oMath>{math}</m:oMath></w:p>");

                Rail();
                Rail();

                foreach (var equation in new[]
                         {
                             // A sum: what the markup says, both ways.
                             Nary("\u2211", "undOvr", "i=1", "n"),
                             Nary("\u2211", "subSup", "i=1", "n"),

                             // An integral, the same two ways.
                             Nary("\u222b", "undOvr", "0", "1"),
                             Nary("\u222b", "subSup", "0", "1"),

                             // A product and a contour integral, to say whether the answer follows
                             // the kind of operator rather than the one character.
                             Nary("\u220f", "undOvr", "i=1", "n"),
                             Nary("\u220f", "subSup", "i=1", "n"),
                             Nary("\u222e", "undOvr", "0", "1"),
                             Nary("\u222e", "subSup", "0", "1"),

                             // One limit at a time, so that the gap between two of them is out of
                             // the way.
                             Nary("\u2211", "subSup", "i=1", ""),
                             Nary("\u2211", "subSup", "", "n"),
                             Nary("\u222b", "subSup", "0", ""),
                             Nary("\u222b", "subSup", "", "1"),

                             // The same sum with limits of different ink, to say whether where
                             // they sit follows what is in them: an x reaches to the middle of the
                             // line, a 1 to the top of it, and an i higher still for its dot.
                             Nary("\u2211", "subSup", "x", ""),
                             Nary("\u2211", "subSup", "1", ""),
                             Nary("\u2211", "subSup", "", "x"),
                             Nary("\u2211", "subSup", "", "1"),
                             Nary("\u2211", "subSup", "x", "x"),
                             Nary("\u222b", "subSup", "x", "")
                         })
                {
                    Probe(equation);
                    Rail();
                }

                // And a sum at twice the size, to say what the answer is a share of.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:pStyle w:val=\"Big\"/>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    $"<m:oMath>{Nary("\u2211", "subSup", "i=1", "n")}</m:oMath></w:p>");
                Rail();

                return builder;
            },

            // What size an equation is set at, which the line box probe could not say: both its
            // fixtures declare eleven point and Word set every equation in them at 11.04, which
            // is eleven rounded to the three hundredth of an inch it rounds a size to — and is
            // also 0.92 of the twelve point the runs of the first one state. This one declares
            // twenty point and states twelve on every run in it, so the two answers are 20.16 and
            // 11.04 and nothing can be both.
            //
            // The brackets and the radical are what say which: those are drawn at whatever the
            // equation is set at, where a letter is drawn at the size its own run states.
            ["math-structure-probe"] = () =>
            {
                const string Mark =
                    "<w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
                    "<w:sz w:val=\"4\"/></w:rPr>";

                static string Run(string text, int halfPoints = 24) =>
                    $"<m:r><w:rPr><w:rFonts w:ascii=\"Cambria Math\" w:hAnsi=\"Cambria Math\"/>" +
                    $"<w:sz w:val=\"{halfPoints}\"/></w:rPr>" +
                    $"<m:t>{DocxBuilder.Escape(text)}</m:t></m:r>";

                static string Element(string name, string inner) => $"<m:{name}>{inner}</m:{name}>";

                var builder = new DocxBuilder().WithStyles(
                    """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:docDefaults>
                        <w:rPrDefault>
                          <w:rPr><w:rFonts w:ascii="Times New Roman" w:hAnsi="Times New Roman"/><w:sz w:val="40"/></w:rPr>
                        </w:rPrDefault>
                      </w:docDefaults>
                      <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                    </w:styles>
                    """);

                void Rail() => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>-</w:t></w:r></w:p>");

                void Probe(string math) => builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}{Mark}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(4)}</w:rPr><w:t>.</w:t></w:r>" +
                    $"<m:oMath>{math}</m:oMath></w:p>");

                Rail();
                Rail();

                foreach (var equation in new[]
                         {
                             // A bracket round a letter, and a radical over one: both are drawn at
                             // whatever the equation is set at.
                             "<m:d><m:dPr><m:begChr m:val=\"(\"/><m:endChr m:val=\")\"/></m:dPr>" +
                             Element("e", Run("x")) + "</m:d>",

                             "<m:rad><m:radPr><m:degHide m:val=\"1\"/></m:radPr>" +
                             Element("deg", string.Empty) + Element("e", Run("x")) + "</m:rad>",

                             // And a script, whose size and rise say the same thing again.
                             "<m:sSup>" + Element("e", Run("x")) + Element("sup", Run("2")) +
                             "</m:sSup>",

                             // The same three with nothing stated on their runs at all, which
                             // leaves them the paragraph's own twenty point.
                             "<m:d><m:dPr><m:begChr m:val=\"(\"/><m:endChr m:val=\")\"/></m:dPr>" +
                             Element("e", "<m:r><m:t>x</m:t></m:r>") + "</m:d>",

                             "<m:sSup>" + Element("e", "<m:r><m:t>x</m:t></m:r>") +
                             Element("sup", "<m:r><m:t>2</m:t></m:r>") + "</m:sSup>"
                         })
                {
                    Probe(equation);
                    Rail();
                }

                return builder;
            },

            // Content wrapped in something that is neither a paragraph nor a table: a content
            // control, a compatibility alternative, the old custom-XML wrapper. Word writes these
            // round whole paragraphs and whole tables — a cover page, a table of contents, every
            // placeholder in a template — and a reader that walks a body looking only for
            // paragraphs and tables loses everything inside them without saying so.
            //
            // Every line names where it is, so that one gone missing is obvious in the comparison
            // rather than merely a page that lays out a little differently.
            ["content-controls"] = () =>
            {
                var times = Times12;

                // One column, ruled, so that a table wrapped in a control is obviously a table.
                const string rules =
                    """
                    <w:tblPr><w:tblW w:w="4000" w:type="dxa"/>
                      <w:tblBorders>
                        <w:top w:val="single" w:sz="4"/><w:left w:val="single" w:sz="4"/>
                        <w:bottom w:val="single" w:sz="4"/><w:right w:val="single" w:sz="4"/>
                      </w:tblBorders><w:tblLayout w:type="fixed"/>
                    </w:tblPr><w:tblGrid><w:gridCol w:w="4000"/></w:tblGrid>
                    """;

                static string Wrapped(int id, string inner) => $"""
                    <w:sdt>
                      <w:sdtPr><w:id w:val="{id}"/><w:lock w:val="sdtLocked"/></w:sdtPr>
                      <w:sdtContent>{inner}</w:sdtContent>
                    </w:sdt>
                    """;

                string Line(string text) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{times}</w:rPr>" +
                    $"<w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>";

                string Cell(string text) =>
                    $"<w:tc><w:tcPr><w:tcW w:w=\"4000\" w:type=\"dxa\"/></w:tcPr>{text}</w:tc>";

                var builder = new DocxBuilder();

                var note = builder.AddFootnote(Wrapped(70,
                    DocxBuilder.FootnoteBody("A note wrapped in a control.", Times10)));

                builder.AddRawParagraph(Line("Before the control."));
                builder.AddRawParagraph(Wrapped(71, Line("Inside a control.")));
                builder.AddRawParagraph(Line("After the control."));

                builder.AddRawParagraph(Wrapped(72,
                    $"<w:tbl>{rules}<w:tr>" +
                    Cell(Line("A table inside a control.")) + "</w:tr></w:tbl>"));

                // A line between the two tables, since Word joins two that touch into one and
                // that is a question of its own.
                builder.AddRawParagraph(Line("Between the tables."));

                builder.AddRawParagraph(
                    $"<w:tbl>{rules}<w:tr>" +
                    Cell(Wrapped(73, Line("A control inside a cell."))) + "</w:tr></w:tbl>");

                builder.AddRawParagraph(Wrapped(74, Wrapped(75, Line("Two controls deep."))));

                // Which branch of an alternative Word takes, said outright: the two hold
                // different words, so the export names the one it read.
                builder.AddRawParagraph($"""
                    <mc:AlternateContent
                        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                        xmlns:w14="http://schemas.microsoft.com/office/word/2010/wordml">
                      <mc:Choice Requires="w14">{Line("The choice, not the fallback.")}</mc:Choice>
                      <mc:Fallback>{Line("The fallback, not the choice.")}</mc:Fallback>
                    </mc:AlternateContent>
                    """);

                builder.AddRawParagraph(
                    $"<w:customXml w:element=\"thing\">{Line("Inside custom XML.")}</w:customXml>");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{times}</w:rPr>" +
                    "<w:t xml:space=\"preserve\">A line with a note in a control</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(note) +
                    $"<w:r><w:rPr>{times}</w:rPr><w:t>.</w:t></w:r></w:p>");

                return builder.WithHeaderFooter(header: true,
                    Wrapped(76, Line("A running head in a control.")));
            },

            // Notes in a section of two columns, with a reference in each of them and a third in
            // the first column of the second page. What is measured is which measure a note is
            // set to and where it sits: under the column that refers to it, or across the page.
            ["footnote-columns"] = () =>
            {
                var builder = new DocxBuilder().WithSection(
                    DocxBuilder.Section(columns: 2, columnSeparator: true));

                string WithNote(int i, int id) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Paragraph {i}, with a note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(id) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>";

                var note = 0;

                for (var i = 1; i <= 90; i++)
                {
                    // Two notes in the first column and one in the second, so that the columns
                    // cannot stop in the same place if their notes are set under them separately.
                    if (i is 3 or 6 or 50)
                    {
                        builder.AddRawParagraph(WithNote(i,
                            builder.AddFootnote(DocxBuilder.FootnoteBody(
                                $"Note {++note}, which belongs under one column.", Times10))));

                        continue;
                    }

                    builder.AddParagraph($"Paragraph {i} of ninety.", ZeroSpacing, Times12);
                }

                return builder;
            },

            // Footnotes on two pages, over enough body text that the space the notes take out of
            // the page decides where the text breaks.
            ["footnotes"] = () =>
            {
                var builder = new DocxBuilder();

                var first = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("The first note, at the foot of the first page.", Times10));
                var second = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("A second note on the same page as the first.", Times10));
                var third = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("A note belonging to the second page.", Times10));

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">A sentence with a note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(first) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> and another one</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(second) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                for (var i = 1; i <= 60; i++)
                {
                    if (i == 45)
                    {
                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph number {i} of sixty, which carries a note</w:t></w:r>" +
                            DocxBuilder.FootnoteReference(third) +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");
                    }
                    else
                    {
                        builder.AddParagraph($"Body paragraph number {i} of sixty.", ZeroSpacing, Times12);
                    }
                }

                return builder;
            },

            // Endnotes, which collect at the end of the document rather than the foot of a page.
            // Two of them, referenced out of order, over enough body text to reach a second page:
            // what Word does with the notes when the body ends part-way down a page, how it
            // numbers them, and whether it rules them off are all read back from its export.
            ["endnotes"] = () =>
            {
                var builder = new DocxBuilder();

                var first = builder.AddEndnote(DocxBuilder.EndnoteBody("The first note.", Times10));
                var second = builder.AddEndnote(DocxBuilder.EndnoteBody("The second note.", Times10));

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">A sentence with a note</w:t></w:r>" +
                    DocxBuilder.EndnoteReference(second) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> and another</w:t></w:r>" +
                    DocxBuilder.EndnoteReference(first) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                for (var i = 1; i <= 50; i++)
                    builder.AddParagraph($"Body paragraph number {i} of fifty.", ZeroSpacing, Times12);

                return builder;
            },

            // A heading kept with the paragraph that follows it, and a paragraph whose lines are
            // kept together, each placed so that a page boundary falls where it would separate
            // them. The filler is single lines so the boundary lands where it is meant to.
            ["keep-together"] = () =>
            {
                var builder = new DocxBuilder();

                // Room for one more line, which the heading takes and the body cannot follow into.
                for (var i = 1; i <= 44; i++)
                    builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

                builder.AddParagraph("A heading kept with what follows it", "<w:keepNext/>" + ZeroSpacing,
                    Times(32, bold: true));
                builder.AddRawParagraph(BrokenParagraph("Body line", 3, ZeroSpacing));

                // Fills the second page to three lines short of the bottom, where a paragraph of
                // four lines that may not be split has to move whole.
                for (var i = 45; i <= 83; i++)
                    builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

                builder.AddRawParagraph(BrokenParagraph("Kept line", 4, "<w:keepLines/>" + ZeroSpacing));

                return builder;
            },

            // Paragraphs that wrap to three lines each, over enough of them that a page boundary
            // has to fall inside one. Word will not leave a single line of a paragraph at the foot
            // of a page or carry one alone to the next, so where it puts the paragraph the break
            // lands in is what this fixture asks it.
            ["widow-orphan"] = () =>
            {
                // Two lines to open with, which is what puts the page boundary inside a paragraph
                // rather than neatly between two of them.
                var builder = new DocxBuilder().AddParagraph(
                    "An opening paragraph of its own, written to run to two lines and no further, " +
                    "so that what follows starts part-way through the page.",
                    ZeroSpacing, Times12);

                for (var i = 1; i <= 26; i++)
                {
                    builder.AddParagraph(
                        $"Paragraph number {i} of twenty-six, written at some length so that it " +
                        "runs to a third line on a page of this width rather than stopping short " +
                        "on the second one, which is what makes a break fall inside it.",
                        ZeroSpacing, Times12);
                }

                return builder;
            },

            // Two equal columns with a rule between them, holding more text than one column can
            // take: where Word puts the overflow, and whether it evens the two out when the text
            // runs out part-way down, are what the reference answers.
            ["columns"] = () =>
            {
                var builder = new DocxBuilder().WithSection(
                    DocxBuilder.Section(columns: 2, columnSeparator: true));

                // Paragraphs that wrap inside a column, which is what proves the measure the text
                // is broken against — and enough of them that a column boundary falls inside one,
                // so that where it goes is decided by widow control as well.
                for (var i = 1; i <= 26; i++)
                {
                    builder.AddParagraph(
                        $"Paragraph number {i} of twenty-six, written long enough that it has to " +
                        "wrap inside the column rather than fitting on one line of it.",
                        ZeroSpacing, Times12);
                }

                return builder;
            },

            // Three columns of stated, unequal widths, with a column break part-way through the
            // first one.
            ["columns-uneven"] = () => new DocxBuilder()
                .WithSection(DocxBuilder.Section(
                    columns: 3,
                    columnWidths: [(2880, 720), (2160, 720), (2880, 0)]))
                .AddParagraph("First column, first line.", ZeroSpacing, Times12)
                .AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>First column, second line.</w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:br w:type=\"column\"/></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Second column opens here.</w:t></w:r></w:p>")
                .AddParagraph("Second column, another line.", ZeroSpacing, Times12)
                .AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:br w:type=\"column\"/></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Third column opens here.</w:t></w:r></w:p>"),

            // How far inside a table's edge Word starts a cell's text. The border weight is
            // varied against a margin of nothing, and then the margin against a fixed border, so
            // that whichever of the two the inset follows can be read straight off the export.
            ["table-inset-weights-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                // Each on a page of its own: stacked, every table's height would carry into the
                // next one's position and a difference of a fraction at the top would read as
                // twenty points at the bottom.
                void Add(string label, int eighths, int? margin)
                {
                    builder.AddRawParagraph(InsetProbeTable(label, eighths, margin, pageBreak: !first));
                    first = false;
                }

                foreach (var eighths in new[] { 0, 2, 4, 8, 12, 16, 24, 48 })
                    Add($"b{eighths}", eighths, 0);

                foreach (var margin in new[] { 72, 108, 288 })
                    Add($"m{margin}", 8, margin);

                // And one with a margin and no border at all, which says what the margin does on
                // its own.
                Add("n108", 0, 108);

                // Then the same again with no cell margin declared, which is not the same as
                // declaring none: Word fills the element in itself, and the tables fixture — which
                // says nothing about margins, as a document written by hand usually does not —
                // sits a quarter point away from where a margin of zero would put it.
                foreach (var eighths in new[] { 0, 4, 16 })
                    Add($"d{eighths}", eighths, null);

                return builder;
            },

            // Cells merged down the page: a cell that says vMerge restart owns every continuing
            // cell beneath it, and the text it holds belongs to all of them together. Each table
            // is on a page of its own so that one's height cannot carry into the next one's
            // position.
            ["table-vertical-merge"] = () =>
            {
                var builder = new DocxBuilder();

                // Three lines in a cell merged down three rows, against a column of one line each:
                // this says whether the merged text runs on past its own row's foot, and where the
                // rows below it begin.
                builder.AddRawParagraph(MergeTable(2, pageBreak: false,
                    MergeCell("restart", lines: ["Merged one", "Merged two", "Merged three"]) +
                    MergeCell(null, lines: ["Right one"]),
                    MergeCell("continue") + MergeCell(null, lines: ["Right two"]),
                    MergeCell("continue") + MergeCell(null, lines: ["Right three"])));

                // The same shape, with a single line placed by each vertical alignment in turn:
                // top, centre and bottom of the whole merged run rather than of its first row.
                foreach (var alignment in new[] { "top", "center", "bottom" })
                    builder.AddRawParagraph(MergeTable(2, pageBreak: true,
                        MergeCell("restart", alignment: alignment, lines: [$"Aligned {alignment}"]) +
                        MergeCell(null, lines: ["First"]),
                        MergeCell("continue") + MergeCell(null, lines: ["Second"]),
                        MergeCell("continue") + MergeCell(null, lines: ["Third"])));

                // More text than the rows it is merged across can hold, which has to push the
                // merged run taller than its rows would otherwise be.
                builder.AddRawParagraph(MergeTable(2, pageBreak: true,
                    MergeCell("restart", lines: ["Tall one", "Tall two", "Tall three", "Tall four"]) +
                    MergeCell(null, lines: ["Short one"]),
                    MergeCell("continue") + MergeCell(null, lines: ["Short two"])));

                // Two merged runs in the same column, one after the other, and a third overlapping
                // both in the next column along: a merge belongs to a column rather than to the
                // table, so neither of these can be read off the other.
                builder.AddRawParagraph(MergeTable(3, pageBreak: true,
                    MergeCell("restart", lines: ["First pair"]) +
                    MergeCell(null, lines: ["Middle one"]) +
                    MergeCell(null, lines: ["Last one"]),
                    MergeCell("continue") +
                    MergeCell("restart", lines: ["Straddling"]) +
                    MergeCell(null, lines: ["Last two"]),
                    MergeCell("restart", lines: ["Second pair"]) +
                    MergeCell("continue") +
                    MergeCell(null, lines: ["Last three"]),
                    MergeCell("continue") +
                    MergeCell(null, lines: ["Middle four"]) +
                    MergeCell(null, lines: ["Last four"])));

                // A merged cell that is shaded, which says how far its fill reaches.
                builder.AddRawParagraph(MergeTable(2, pageBreak: true,
                    MergeCell("restart", shading: "D9D9D9", lines: ["Shaded"]) +
                    MergeCell(null, lines: ["Plain one"]),
                    MergeCell("continue") + MergeCell(null, lines: ["Plain two"])));

                return builder;
            },

            // A merged run reaching the foot of the page: once inside the row it begins in, and
            // once between two of the rows it covers. What the merged cell holds has to divide
            // with it either way.
            ["table-merge-split"] = () =>
            {
                var builder = new DocxBuilder();

                // Eight lines short of the foot of the page, against a row twelve lines tall whose
                // merged cell holds twenty: the break falls inside the row the run begins in.
                for (var i = 1; i <= 38; i++)
                    builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

                builder.AddRawParagraph(MergeTable(2, pageBreak: false,
                    MergeCell("restart", lines: [.. Lines("Merged", 20)]) +
                    MergeCell(null, lines: [.. Lines("Beside", 12)]),
                    MergeCell("continue") + MergeCell(null, lines: [.. Lines("After", 3)])));

                // And again with the break between the run's rows rather than inside one: three
                // rows of four lines each, begun with six lines of the page left.
                for (var i = 1; i <= 40; i++)
                    builder.AddParagraph($"Second filler {i}.",
                        i == 1 ? ZeroSpacingNewPage : ZeroSpacing, Times12);

                builder.AddRawParagraph(MergeTable(2, pageBreak: false,
                    MergeCell("restart", lines: [.. Lines("Down", 12)]) +
                    MergeCell(null, lines: [.. Lines("Row one", 4)]),
                    MergeCell("continue") + MergeCell(null, lines: [.. Lines("Row two", 4)]),
                    MergeCell("continue") + MergeCell(null, lines: [.. Lines("Row three", 4)])));

                return builder;
            },

            // Every field this converter evaluates, against Word's own answer for each. Word only
            // recalculates the page-dependent ones when it exports, so the reference for this one
            // is made with its fields updated first — see tools/make-reference-pdfs.sh.
            //
            // DATE and TIME are deliberately absent: they are whatever the clock says, so Word's
            // answer would be the day the reference was made and ours the day the test runs.
            // Their formatting is covered by FieldTests instead, against a pinned instant.
            ["fields"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithDocumentProperties(
                        title: "Analytical Engine",
                        subject: "Fields",
                        creator: "Ada Lovelace",
                        keywords: "engine, tables",
                        description: "A note on the engine.",
                        lastModifiedBy: "Charles Babbage",
                        // Word's dates start in 1900: earlier ones come back as its zero date,
                        // which is what the engine's own century did when it was tried here.
                        // Word reads these as UTC and shows them in the local zone, so they are
                        // written at noon: a date at noon is the same date either side of every
                        // offset a reader is likely to be in.
                        created: "2019-03-04T12:00:00Z",
                        modified: "2021-11-30T12:00:00Z",
                        company: "Difference Ltd",
                        manager: "Luigi Menabrea",
                        custom: [("Category", "Reference"), ("Reviewer", "A Reader")]);

                // Every run of the field carries the same formatting, the opening one included:
                // Word throws the old result away when it updates a field and sets the new one in
                // whatever the field began in, so a field that opens unstyled comes back in the
                // document's default font rather than the one its result was written in.
                void Field(string label, string instruction) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{label}: </w:t></w:r>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

                // What the document says about itself.
                Field("author", " AUTHOR ");
                Field("title", " TITLE ");
                Field("subject", " SUBJECT ");
                Field("keywords", " KEYWORDS ");
                Field("comments", " COMMENTS ");
                Field("lastsavedby", " LASTSAVEDBY ");
                Field("docproperty", " DOCPROPERTY \"Category\" ");
                Field("docproperty-two", " DOCPROPERTY Reviewer ");

                // Dates the document carries rather than the clock. The time of day is left to
                // FieldTests: Word shows it in the reader's own zone, which is not the same in
                // two places at once.
                Field("createdate", " CREATEDATE \\@ \"yyyy-MM-dd\" ");
                Field("savedate", " SAVEDATE \\@ \"d MMMM yyyy\" ");

                // Text of its own.
                Field("quote", " QUOTE \"Some quoted text\" ");

                // Numbers, and the switches that spell them.
                Field("page", " PAGE ");
                Field("page-roman", " PAGE \\* roman ");
                Field("page-ROMAN", " PAGE \\* ROMAN ");
                Field("page-alphabetic", " PAGE \\* alphabetic ");
                Field("page-ALPHABETIC", " PAGE \\* ALPHABETIC ");
                Field("page-ordinal", " PAGE \\* Ordinal ");
                Field("page-cardtext", " PAGE \\* CardText ");
                Field("page-ordtext", " PAGE \\* OrdText ");
                Field("page-hex", " PAGE \\* Hex ");
                Field("page-dollartext", " PAGE \\* DollarText ");
                Field("page-mergeformat", " PAGE \\* MERGEFORMAT ");
                Field("numpages", " NUMPAGES ");

                // Counters, which run on through the document by name.
                Field("seq-one", " SEQ Figure ");
                Field("seq-two", " SEQ Figure ");
                Field("seq-table", " SEQ Table ");
                Field("seq-three", " SEQ Figure ");
                Field("seq-repeat", " SEQ Figure \\c ");
                Field("seq-reset", " SEQ Figure \\r 7 ");
                Field("seq-after-reset", " SEQ Figure ");
                Field("seq-roman", " SEQ Table \\* roman ");

                // Case, which applies to whatever the field produced.
                Field("upper", " AUTHOR \\* Upper ");
                Field("lower", " AUTHOR \\* Lower ");
                Field("firstcap", " KEYWORDS \\* FirstCap ");
                Field("caps", " KEYWORDS \\* Caps ");

                // A place in the document, referred to from elsewhere in it.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr><w:pageBreakBefore/>{ZeroSpacing}</w:pPr>" +
                    "<w:bookmarkStart w:id=\"1\" w:name=\"target\"/>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>The bookmarked text.</w:t></w:r>" +
                    "<w:bookmarkEnd w:id=\"1\"/></w:p>");

                Field("ref", " REF target ");
                Field("pageref", " PAGEREF target ");
                Field("pageref-roman", " PAGEREF target \\* roman ");
                Field("page-two", " PAGE ");
                Field("section", " SECTION ");
                Field("sectionpages", " SECTIONPAGES ");

                // A second section, which starts the section counters again.
                builder.AddParagraphWithSectionBreak(
                    "Second section.", DocxBuilder.Section(), ZeroSpacing, Times12);

                Field("section-two", " SECTION ");
                Field("sectionpages-two", " SECTIONPAGES ");
                Field("page-three", " PAGE ");

                // And one nobody evaluates, which has to keep showing what Word last computed.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">cached: </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr>" +
                    "<w:instrText xml:space=\"preserve\"> ADDIN SOMETHING </w:instrText></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>what Word last computed</w:t></w:r>" +
                    "<w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

                return builder;
            },

            // What STYLEREF picks up, which is the field a running head is made of. Word's rule
            // depends on where the field is: a header looks down the page it is on, a footer
            // looks up it, and the body looks backwards from the field. This puts each of them
            // over pages that hold two headings, one heading and none at all, so that the three
            // can be told apart.
            ["styleref"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithExtraStyles(
                        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\">" +
                        "<w:name w:val=\"heading 1\"/><w:pPr>" +
                        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>" +
                        $"</w:pPr><w:rPr>{Times(24, bold: true)}</w:rPr></w:style>")
                    .WithHeaderFooter(header: true,
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">head: </w:t></w:r>" +
                        StyleRefRuns(" STYLEREF \"Heading 1\" ") +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> / last: </w:t></w:r>" +
                        StyleRefRuns(" STYLEREF \"Heading 1\" \\l ") + "</w:p>")
                    .WithHeaderFooter(header: false,
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">foot: </w:t></w:r>" +
                        StyleRefRuns(" STYLEREF \"Heading 1\" ") +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> / last: </w:t></w:r>" +
                        StyleRefRuns(" STYLEREF \"Heading 1\" \\l ") + "</w:p>");

                void Heading(string text) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>{text}</w:t></w:r></w:p>");

                void Body(string label, string instruction) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{label}: </w:t></w:r>" +
                        StyleRefRuns(instruction) + "</w:p>");

                void Filler(string label, int count)
                {
                    for (var i = 1; i <= count; i++)
                        builder.AddParagraph($"{label} {i}.", ZeroSpacing, Times12);
                }

                // Before any heading at all, which is the case that can only look forwards.
                Body("before-any", " STYLEREF \"Heading 1\" ");

                Heading("Alpha");
                Filler("Under alpha", 8);
                Body("after-alpha", " STYLEREF \"Heading 1\" ");

                // A second heading on the same page, so that first and last differ there.
                Heading("Beta");
                Filler("Under beta", 30);

                // A page with no heading on it at all: the running head has to carry over from
                // the page before, which is the case a document of any length is mostly made of.
                Filler("Second page", 46);

                Heading("Gamma");
                Filler("Under gamma", 8);
                Body("after-gamma", " STYLEREF \"Heading 1\" ");

                return builder;
            },

            // A table of contents: the field that reads the document's own headings back to it,
            // with the page each is on. The styles are the ones Word writes into a document when
            // it builds one, so that what it exports is measured against the same definitions
            // this reads.
            ["toc"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithExtraStyles(
                        Style("Heading1", "heading 1", outline: "<w:outlineLvl w:val=\"0\"/>", bold: true) +
                        Style("Heading2", "heading 2", outline: "<w:outlineLvl w:val=\"1\"/>", bold: true) +
                        Style("TOC1", "toc 1", tabs: TocTab) +
                        Style("TOC2", "toc 2", tabs: TocTab, indent: "<w:ind w:left=\"220\"/>"));

                void Heading(int level, string text) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>{text}</w:t></w:r></w:p>");

                void Filler(string label, int count)
                {
                    for (var i = 1; i <= count; i++)
                        builder.AddParagraph($"{label} {i}.", ZeroSpacing, Times12);
                }

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    StyleRefRuns(" TOC \\o \"1-2\" \\h \\z \\u ") + "</w:p>");

                Heading(1, "The first chapter");
                Filler("Under the first chapter", 6);
                Heading(2, "A section of it");
                Filler("Under the section", 30);

                Heading(1, "The second chapter");
                Filler("Under the second chapter", 20);
                Heading(2, "Another section");
                Filler("Under another section", 40);

                Heading(1, "The third chapter, whose title is long enough to say something about " +
                           "how a line of the table of contents that will not fit is broken");
                Filler("Under the third chapter", 4);

                return builder;
            },

            // An index: the entries are marked where they occur, with XE fields that show
            // nothing, and the INDEX field gathers them into a list of terms and the pages they
            // were marked on. The styles are the ones Word writes for one.
            ["index"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithExtraStyles(
                        Style("Index1", "index 1") +
                        Style("Index2", "index 2", indent: "<w:ind w:left=\"220\"/>") +
                        Style("IndexHeading", "index heading", bold: true));

                // An entry marker, which draws nothing itself.
                void Mark(string term) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">Marking {term}. </w:t></w:r>" +
                        StyleRefRuns($" XE \"{term}\" ") + "</w:p>");

                void Filler(string label, int count)
                {
                    for (var i = 1; i <= count; i++)
                        builder.AddParagraph($"{label} {i}.", ZeroSpacing, Times12);
                }

                // The first page: two terms, one of which is marked again further on.
                Mark("Analysis");
                Mark("Babbage");
                Mark("Engine:difference");
                Filler("First page", 40);

                // The second: the same term again, a subentry, and one that sorts before both.
                Mark("Analysis");
                Mark("Engine:analytical");
                Mark("Arithmetic");
                Filler("Second page", 42);

                // The third: a term marked twice on the same page, which is one page number.
                Mark("Zero");
                Mark("Zero");
                Filler("Third page", 20);

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    StyleRefRuns(" INDEX \\h \"A\" \\c \"1\" ") + "</w:p>");

                return builder;
            },

            // The two fields that work something out rather than look it up: IF, which chooses
            // between two pieces of text, and the formula field, which is arithmetic — over
            // numbers written into it, or over the cells of the table it stands in.
            ["formulas"] = () =>
            {
                var builder = new DocxBuilder();

                void Field(string label, string instruction) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{label}: </w:t></w:r>" +
                        StyleRefRuns(instruction) + "</w:p>");

                // Choosing between two pieces of text.
                Field("if-equal", " IF 1 = 1 \"yes\" \"no\" ");
                Field("if-greater", " IF 2 > 3 \"yes\" \"no\" ");
                Field("if-at-least", " IF 5 >= 5 \"yes\" \"no\" ");
                Field("if-text", " IF \"abc\" = \"abc\" \"yes\" \"no\" ");
                Field("if-unequal-text", " IF \"abc\" <> \"abd\" \"yes\" \"no\" ");
                Field("if-wildcard", " IF \"abcdef\" = \"abc*\" \"yes\" \"no\" ");
                Field("if-no-else", " IF 1 = 2 \"yes\" ");

                // Arithmetic written into the field.
                Field("sum-of-terms", " =2+3*4 ");
                Field("brackets", " =(2+3)*4 ");
                Field("division", " =10/4 ");
                Field("power", " =2^10 ");
                Field("negative", " =7-9 ");
                Field("percent", " =50%*8 ");
                Field("recurring", " =10/3 ");
                Field("eighth", " =1/8 ");

                // The functions it knows.
                Field("sum", " =SUM(1,2,3) ");
                Field("average", " =AVERAGE(2,4,9) ");
                Field("product", " =PRODUCT(2,3,4) ");
                Field("count", " =COUNT(2,9,4) ");
                Field("max", " =MAX(2,9,4) ");
                Field("min", " =MIN(2,9,4) ");
                Field("round", " =ROUND(3.14159,2) ");
                Field("abs", " =ABS(-7) ");
                Field("int", " =INT(7.9) ");
                Field("mod", " =MOD(7,3) ");
                Field("sign", " =SIGN(-3) ");
                Field("if-function", " =IF(2>1,10,20) ");
                Field("and", " =AND(1,1) ");
                Field("or", " =OR(0,1) ");
                Field("not", " =NOT(0) ");

                // How a number is spelled, which the \# switch says.
                Field("picture-decimals", " =10/4 \\# \"0.00\" ");
                Field("picture-thousands", " =1234567 \\# \"#,##0\" ");
                Field("picture-money", " =5 \\# \"$#,##0.00\" ");
                Field("picture-negative", " =0-5 \\# \"0.00;(0.00)\" ");
                Field("picture-hash", " =7 \\# \"##\" ");

                // And the same over the cells of a table: the directions name the cells around
                // the one the formula is in, and a cell can be named outright.
                builder.AddRawParagraph(FormulaTable());

                return builder;
            },

            // The merge fields, in a document with no data behind them — which is what a letter
            // written for a merge looks like before it is merged, and what a converter is handed.
            ["merge"] = () =>
            {
                var builder = new DocxBuilder();

                void Field(string label, string instruction) =>
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{label}: </w:t></w:r>" +
                        StyleRefRuns(instruction) + "</w:p>");

                Field("plain", " MERGEFIELD FirstName ");
                Field("quoted", " MERGEFIELD \"Company Name\" ");
                Field("mergeformat", " MERGEFIELD Surname \\* MERGEFORMAT ");
                Field("upper", " MERGEFIELD Surname \\* Upper ");
                Field("before-and-after", " MERGEFIELD Title \\b \"Dear \" \\f \",\" ");
                Field("record", " MERGEREC ");
                Field("sequence", " MERGESEQ ");
                Field("next", " NEXT ");
                Field("skip-if", " SKIPIF 1 = 2 ");

                // FILLIN and ASK are deliberately absent: updating one asks the reader a question
                // in a dialog, and a reference is made by updating every field there is.

                return builder;
            },

            // The picture formats beyond PNG and JPEG, each holding the same picture: what is
            // measured here is that Word places them where this places them, since a comparison
            // of positions says nothing about the pixels inside.
            ["images-formats"] = () =>
            {
                var pixels = ImageWriter.Sample(24, 24);

                var builder = new DocxBuilder();
                var gif = builder.AddImagePart(ImageWriter.Gif(24, 24, pixels), "gif");
                var bmp = builder.AddImagePart(ImageWriter.Bmp(24, 24, pixels), "bmp");
                var tiff = builder.AddImagePart(ImageWriter.Tiff(24, 24, pixels), "tiff");
                var palette = builder.AddImagePart(ImageWriter.Bmp(24, 24, pixels, bits: 8), "bmp");

                return builder
                    .AddParagraph("Paragraph before the pictures.", ZeroSpacing, Times12)
                    .AddImageParagraph(gif, 48, 48, ZeroSpacing, leadingText: "GIF ")
                    .AddImageParagraph(bmp, 48, 48, ZeroSpacing, leadingText: "BMP ")
                    .AddImageParagraph(tiff, 48, 48, ZeroSpacing, leadingText: "TIFF ")
                    .AddImageParagraph(palette, 48, 48, ZeroSpacing, leadingText: "Paletted BMP ")
                    .AddParagraph("Paragraph after the pictures.", ZeroSpacing, Times12);
            },

            // A metafile: a drawing kept as the commands that make it rather than as pixels. What
            // is measured against Word here is where it puts the drawing and how big it is; what
            // is inside it is compared by rendering both pages, in EmfTests.
            ["images-metafile"] = () =>
            {
                var writer = new EmfWriter(200, 120);

                var pen = writer.CreatePen(180, 20, 30, 2);
                var brush = writer.CreateBrush(40, 90, 200);
                writer.Select(pen).Select(brush).Rectangle(10, 10, 95, 70);

                var hollow = writer.CreateHollowBrush();
                writer.Select(hollow).Ellipse(105, 10, 190, 70);

                writer.MoveTo(10, 85).LineTo(190, 110);

                var font = writer.CreateFont("Times New Roman", 14);
                writer.Select(font).TextColor(0, 110, 60).Text(12, 112, "Drawn by its records");

                var builder = new DocxBuilder();
                var metafile = builder.AddImagePart(writer.Build(), "emf");

                return builder
                    .AddParagraph("Paragraph before the drawing.", ZeroSpacing, Times12)
                    .AddImageParagraph(metafile, 200, 120, ZeroSpacing)
                    .AddParagraph("Paragraph after the drawing.", ZeroSpacing, Times12);
            },

            // A metafile written the way anything modern writes one: the same drawing recorded
            // twice over, once in the newer records and once in the old ones that travel around
            // them. The newer are what draws it here and the old are what Word draws, so this
            // fixture is the one place the newer records are measured against another
            // implementation — the two halves have to reach the page in the same place.
            ["images-metafile-plus"] = () =>
            {
                var writer = new EmfWriter(200, 120);

                // The newer records, which say the file draws itself both ways.
                writer.PlusHeader(dual: true);
                writer.PlusFillRectangle(40, 90, 200, 10, 10, 85, 60);
                writer.PlusPen(1, 180, 20, 30, 2);
                writer.PlusDrawLines(1, closed: true, (10, 10), (95, 10), (95, 70), (10, 70));
                writer.PlusDrawLines(1, closed: false, (10, 85), (190, 110));
                writer.PlusFont(2, "Times New Roman", 14);
                writer.PlusString(2, 0, 110, 60, 12, 100, "Drawn either way");

                // And the same drawing again in the old ones, for a reader that has never heard
                // of the others. Both halves draw one picture, which is what makes the comparison
                // against Word worth anything: it draws this half and this draws the other.
                var pen = writer.CreatePen(180, 20, 30, 2);
                var brush = writer.CreateBrush(40, 90, 200);
                writer.Select(pen).Select(brush).Rectangle(10, 10, 95, 70);
                writer.MoveTo(10, 85).LineTo(190, 110);

                var font = writer.CreateFont("Times New Roman", 14);
                writer.Select(font).TextColor(0, 110, 60).Text(12, 100, "Drawn either way");

                var builder = new DocxBuilder();
                var metafile = builder.AddImagePart(writer.Build(), "emf");

                return builder
                    .AddParagraph("Paragraph before the drawing.", ZeroSpacing, Times12)
                    .AddImageParagraph(metafile, 200, 120, ZeroSpacing)
                    .AddParagraph("Paragraph after the drawing.", ZeroSpacing, Times12);
            },

            // Numbering down the margin: where the numbers sit, which lines get one, what a
            // count of more than one leaves out, and where the count begins again. Three sections,
            // and one export answers all of it:
            //
            //   1  every line, beginning again on each page, at whatever distance a section that
            //      says nothing gets — and an empty paragraph among them, and one that asks to be
            //      passed over
            //   2  every fifth line, counting on from the section before, starting at ten
            //   3  every line again, half an inch out, beginning again with the section
            // A table that floats: w:tblpPr takes it out of the flow and the text runs round it.
            // Seven pages, so that one export answers the whole of it:
            //
            //   1  against the left margin, anchored to the text it was written among
            //   2  against the right margin
            //   3  anchored to the page rather than the text, at a stated place
            //   4  half an inch of daylight on every side of it
            //   5  no daylight at all
            //   6  a stated distance down from the paragraph it belongs to
            //   7  the same in the left-hand place, drawn with a three point border
            //
            // A table with room on both sides of it is floating-table-sides-probe, which is a
            // fixture of its own because Word does something there this does not.
            ["floating-table-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Page(string label, string positioning, int rows = 4, int lines = 12,
                    int borderSize = 4, bool above = true)
                {
                    if (!first) builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                    first = false;

                    // The table is written among the text rather than before it, so that what it
                    // is anchored to is a paragraph with lines of its own. The one exception is the
                    // page whose clearance is half an inch: a clearance that reaches back over a
                    // line already written is one of the two things floating-table-wrap-probe is
                    // for, and this page is about the room the table takes beside and below it.
                    if (above)
                        builder.AddParagraph($"{label}: the first line of the page, above the table.",
                            ZeroSpacing, Times12);
                    builder.AddRawParagraph(PositionedTable(label, rows, positioning, borderSize));

                    for (var i = 1; i <= lines; i++)
                        builder.AddParagraph(
                            $"{label} line {i}, long enough to reach the table and be shortened by it.",
                            ZeroSpacing, Times12);
                }

                // What Word itself writes for a floating table: an eighth of an inch of daylight
                // either side, and anchored to the text. Six points down from where the table
                // would have stood, so that its rows' baselines and the text's fall clear of one
                // another and each can be read off the export on its own.
                const string Daylight = "w:leftFromText=\"180\" w:rightFromText=\"180\" ";

                Page("Left", $"<w:tblpPr {Daylight}w:vertAnchor=\"text\" w:horzAnchor=\"margin\" " +
                             "w:tblpX=\"0\" w:tblpY=\"120\"/>");

                Page("Right", $"<w:tblpPr {Daylight}w:vertAnchor=\"text\" w:horzAnchor=\"margin\" " +
                              "w:tblpXSpec=\"right\" w:tblpY=\"120\"/>");

                // Two inches down the paper and one inch across it, wherever the text is.
                Page("Page", $"<w:tblpPr {Daylight}w:vertAnchor=\"page\" w:horzAnchor=\"page\" " +
                             "w:tblpX=\"1440\" w:tblpY=\"2880\"/>");

                Page("Wide", "<w:tblpPr w:leftFromText=\"720\" w:rightFromText=\"720\" " +
                             "w:topFromText=\"720\" w:bottomFromText=\"720\" " +
                             "w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpX=\"0\" w:tblpY=\"120\"/>",
                    above: false);

                Page("Tight", "<w:tblpPr w:leftFromText=\"0\" w:rightFromText=\"0\" " +
                              "w:topFromText=\"0\" w:bottomFromText=\"0\" " +
                              "w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpX=\"0\" w:tblpY=\"120\"/>");

                Page("Down", $"<w:tblpPr {Daylight}w:vertAnchor=\"text\" w:horzAnchor=\"margin\" " +
                             "w:tblpX=\"0\" w:tblpY=\"720\"/>");

                // The same place with a three point border rather than half a point: the table
                // stands a border's width left of where it is put, and this says whether that is
                // the border's width or a fixed step.
                Page("Thick", $"<w:tblpPr {Daylight}w:vertAnchor=\"text\" w:horzAnchor=\"margin\" " +
                              "w:tblpX=\"0\" w:tblpY=\"300\"/>", borderSize: 24);

                return builder;
            },

            // Phonetic guides set over East Asian text, from w:ruby. One page, a line for each
            // thing the markup can say:
            //
            //   1  a guide over a word, centred, which is what Word writes by default
            //   2  the same aligned to the left of the word, 3 to the right
            //   4  spread between the letters, 5 spread between and outside them
            //   6  a guide wider than the word it stands over
            //   7  a guide narrower than it
            //   8  two guided words in a line of ordinary text, to see what the line does
            ["ruby-probe"] = () =>
            {
                var builder = new DocxBuilder();

                const string Mincho =
                    "<w:rFonts w:ascii=\"MS Mincho\" w:hAnsi=\"MS Mincho\" w:eastAsia=\"MS Mincho\"/>" +
                    "<w:sz w:val=\"24\"/>";

                const string Small =
                    "<w:rFonts w:ascii=\"MS Mincho\" w:hAnsi=\"MS Mincho\" w:eastAsia=\"MS Mincho\"/>" +
                    "<w:sz w:val=\"12\"/>";

                // CT_RubyPr is a sequence: how it is aligned, then the three sizes, then the
                // language it is written in.
                static string Ruby(string guide, string word, string align) =>
                    "<w:r><w:ruby>" +
                    $"<w:rubyPr><w:rubyAlign w:val=\"{align}\"/><w:hps w:val=\"12\"/>" +
                    "<w:hpsRaise w:val=\"22\"/><w:hpsBaseText w:val=\"24\"/>" +
                    "<w:lid w:val=\"ja-JP\"/></w:rubyPr>" +
                    $"<w:rt><w:r><w:rPr>{Small}</w:rPr><w:t>{guide}</w:t></w:r></w:rt>" +
                    $"<w:rubyBase><w:r><w:rPr>{Mincho}</w:rPr><w:t>{word}</w:t></w:r></w:rubyBase>" +
                    "</w:ruby></w:r>";

                void Line(string label, string guide, string word, string align)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(label)} </w:t></w:r>" +
                        Ruby(guide, word, align) +
                        $"<w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>");
                }

                // 振仮名 read ふりがな: three characters under four, which is the ordinary case.
                Line("Centre:", "\u3075\u308A\u304C\u306A", "\u632F\u4EEE\u540D", "center");
                Line("Left:", "\u3075\u308A\u304C\u306A", "\u632F\u4EEE\u540D", "left");
                Line("Right:", "\u3075\u308A\u304C\u306A", "\u632F\u4EEE\u540D", "right");
                Line("Letters:", "\u3075\u308A\u304C\u306A", "\u632F\u4EEE\u540D", "distributeLetter");
                Line("Spaces:", "\u3075\u308A\u304C\u306A", "\u632F\u4EEE\u540D", "distributeSpace");

                // A guide of eight characters over one, and one of two over three.
                Line("Wide guide:", "\u3042\u3044\u3046\u3048\u304A\u304B\u304D\u304F", "\u5B57", "center");
                Line("Narrow guide:", "\u3042\u3044", "\u632F\u4EEE\u540D", "center");

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Two of them: </w:t></w:r>" +
                    Ruby("\u3075\u308A", "\u632F", "center") +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> and </w:t></w:r>" +
                    Ruby("\u304C\u306A", "\u540D", "center") +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>");

                builder.AddParagraph("An ordinary line beneath them all.", ZeroSpacing, Times12);

                return builder;
            },

            // Two tables written one after the other with nothing in between, which Word reads as
            // one table rather than two. Four pages:
            //
            //   1  two tables of the same grid, touching
            //   2  the same two with a paragraph between them, for that to be set against
            //   3  two tables of different grids, touching: whose columns win
            //   4  a second table that asks to be indented, touching the first — the one page
            //      where Word does something this does not follow, and the difference is written
            //      up in AdjacentTableTests
            ["adjacent-tables-probe"] = () =>
            {
                var builder = new DocxBuilder();

                void Page(string one, string two, bool between = false)
                {
                    builder.AddRawParagraph(one);
                    if (between) builder.AddParagraph("Between them.", ZeroSpacing, Times12);
                    builder.AddRawParagraph(two);
                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                }

                Page(AdjacentTable("First", 2880, 1440, "FFE0E0"),
                    AdjacentTable("Second", 2880, 1440, "E0E0FF"));

                Page(AdjacentTable("Apart one", 2880, 1440, "FFE0E0"),
                    AdjacentTable("Apart two", 2880, 1440, "E0E0FF"), between: true);

                // The second names its columns differently: 1440 and 2880 where the first had
                // 2880 and 1440.
                Page(AdjacentTable("Wide", 2880, 1440, "FFE0E0"),
                    AdjacentTable("Narrow", 1440, 2880, "E0E0FF"));

                Page(AdjacentTable("Plain", 2880, 1440, "FFE0E0"),
                    AdjacentTable("Indented", 2880, 1440, "E0E0FF", indentTwips: 720));

                return builder;
            },

            // The boxes a form is filled in by: w:checkBox inside a legacy form field, which draws
            // nothing of its own — the box is the field. Six of them on one page:
            //
            //   1  empty, sized to the text round it
            //   2  ticked, the same
            //   3  empty at fourteen point, stated rather than left to the text
            //   4  ticked at fourteen point
            //   5  empty in a run of twenty point text, to see what "sized to the text" follows
            //   6  the modern kind, a content control, which carries its own character
            ["checkbox-probe"] = () =>
            {
                var builder = new DocxBuilder();

                // CT_FFData is a sequence: the name comes first, then whether it may be filled in,
                // then the box itself.
                static string Field(bool ticked, int? halfPoints, string runProperties)
                {
                    var size = halfPoints is { } stated
                        ? $"<w:size w:val=\"{stated}\"/>"
                        : "<w:sizeAuto/>";

                    var data =
                        "<w:ffData><w:name w:val=\"Box\"/><w:enabled/>" +
                        $"<w:checkBox>{size}<w:default w:val=\"0\"/>" +
                        (ticked ? "<w:checked w:val=\"1\"/>" : string.Empty) +
                        "</w:checkBox></w:ffData>";

                    return
                        $"<w:r><w:rPr>{runProperties}</w:rPr>" +
                        $"<w:fldChar w:fldCharType=\"begin\">{data}</w:fldChar></w:r>" +
                        $"<w:r><w:rPr>{runProperties}</w:rPr>" +
                        "<w:instrText xml:space=\"preserve\"> FORMCHECKBOX </w:instrText></w:r>" +
                        $"<w:r><w:rPr>{runProperties}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r>";
                }

                void Line(string label, string field, string runProperties)
                {
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{runProperties}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(label)} </w:t></w:r>" +
                        field +
                        $"<w:r><w:rPr>{runProperties}</w:rPr>" +
                        "<w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>");
                }

                Line("Empty:", Field(false, null, Times12), Times12);
                Line("Ticked:", Field(true, null, Times12), Times12);
                Line("Empty at fourteen:", Field(false, 28, Times12), Times12);
                Line("Ticked at fourteen:", Field(true, 28, Times12), Times12);

                var large = Times(halfPoints: 40);
                Line("Empty in twenty point:", Field(false, null, large), large);

                // More sizes, so that how big a box is can be read off rather than guessed at:
                // three more stated, and three more taken from the text round them.
                foreach (var halfPoints in new[] { 16, 20, 32, 48, 72, 96, 144 })
                    Line($"Stated {halfPoints / 2}:", Field(false, halfPoints, Times12), Times12);

                foreach (var halfPoints in new[] { 16, 32, 48 })
                {
                    var run = Times(halfPoints: halfPoints);
                    Line($"Text {halfPoints / 2}:", Field(false, null, run), run);
                }

                // The modern kind: a content control holding the character itself, which Word
                // writes in a face of its own naming.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Modern: </w:t></w:r>" +
                    "<w:sdt><w:sdtPr><w:id w:val=\"11\"/></w:sdtPr><w:sdtContent>" +
                    "<w:r><w:rPr><w:rFonts w:ascii=\"MS Gothic\" w:hAnsi=\"MS Gothic\" " +
                    "w:eastAsia=\"MS Gothic\"/><w:sz w:val=\"24\"/></w:rPr>" +
                    "<w:t>\u2612</w:t></w:r></w:sdtContent></w:sdt>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>");

                return builder;
            },

            // Word breaking words at the ends of lines, from w:autoHyphenation. The same paragraph
            // six times over, so that what changes is only what the setting says:
            //
            //   1  hyphenated, with Word's own quarter-inch zone
            //   2  not hyphenated at all, for the others to be set against
            //   3  an inch of zone, which leaves fewer words worth breaking
            //   4  a paragraph that says it is not to be hyphenated
            //   5  justified rather than ranged left, which changes what the zone measures
            //   6  capitals left alone, in a paragraph written in them
            ["hyphenation-probe"] = () =>
            {
                // Long enough words, and enough of them, that a narrow measure has to break some.
                const string Body =
                    "Hyphenation is the business of breaking a word between two lines when the " +
                    "remainder would otherwise be unreasonably conspicuous, and typographers " +
                    "have argued about it interminably. Consider representative examples: " +
                    "communication, extraordinary, misunderstanding, particularly, " +
                    "responsibility, understanding, international, development, organisation.";

                const string Capitals =
                    "COMMUNICATION EXTRAORDINARY MISUNDERSTANDING PARTICULARLY RESPONSIBILITY " +
                    "UNDERSTANDING INTERNATIONAL DEVELOPMENT ORGANISATION CONSIDERATION.";

                // A narrow measure, so that the ends of lines fall where a word might be broken.
                const string Narrow = "<w:ind w:left=\"0\" w:right=\"5040\"/>";

                var builder = new DocxBuilder().WithAutoHyphenation();

                // suppressAutoHyphens comes before the indent in CT_PPr and jc after it, so the
                // two are given separately rather than bolted on at the end.
                void Page(string label, string body, string before = "", string after = "")
                {
                    builder.AddParagraph(label, ZeroSpacing, Times12);
                    builder.AddParagraph(body, before + ZeroSpacing + Narrow + after, Times12);
                    builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                }

                Page("Hyphenated.", Body);
                Page("Suppressed.", Body, before: "<w:suppressAutoHyphens/>");
                Page("Justified.", Body, after: "<w:jc w:val=\"both\"/>");
                Page("Capitals.", Capitals);

                return builder;
            },

            // The same text again, with an inch of hyphenation zone rather than Word's own quarter
            // of one: a word is broken only where the line would otherwise be left with more white
            // than the zone, so a wide zone leaves most words whole.
            ["hyphenation-zone-probe"] = () => HyphenationBody(
                new DocxBuilder().WithAutoHyphenation(zoneTwips: 1440)),

            // And again with no more than two lines in a row allowed to end in a hyphen.
            ["hyphenation-limit-probe"] = () => HyphenationBody(
                new DocxBuilder().WithAutoHyphenation(consecutive: 2)),

            // And again with words in capitals left whole.
            ["hyphenation-caps-probe"] = () => HyphenationBody(
                new DocxBuilder().WithAutoHyphenation(doNotHyphenateCaps: true), capitals: true),

            // Which way the columns of a table run, from w:bidiVisual. Five tables, each on a
            // page of its own:
            //
            //   1  the ordinary way round, for the others to be set against
            //   2  the other way round
            //   3  the other way round with the table indented, to see which side the indent is
            //   4  the other way round with two cells joined, to see which end the join is at
            //   5  the ordinary way round with the same join, for that to be set against
            ["column-order-probe"] = () =>
            {
                var builder = new DocxBuilder();

                builder.AddRawParagraph(ColumnOrderTable("Plain", mirrored: false));
                builder.AddRawParagraph(ColumnOrderTable("Mirrored", mirrored: true, pageBreak: true));
                builder.AddRawParagraph(ColumnOrderTable(
                    "Indented", mirrored: true, indentTwips: 720, pageBreak: true));
                builder.AddRawParagraph(ColumnOrderTable(
                    "Joined", mirrored: true, span: true, pageBreak: true));
                builder.AddRawParagraph(ColumnOrderTable(
                    "Plain joined", mirrored: false, span: true, pageBreak: true));

                return builder;
            },

            // Text turned on its side in a table cell, from w:textDirection. Ten tables, each on
            // a page of its own so that one's height cannot carry into the next one's place:
            //
            //   1  btLr, which is what a narrow heading is usually written in
            //   2  tbRl, turned the other way
            //   3  btLr with the row's height fixed, so where the text sits in it can be read
            //   4  tbRl with the same
            //   5  btLr with more text than the row is tall, to see where it breaks
            //   6  the row left to find its own height round a turned cell
            //   7  btLr against the top of the cell, 8 the middle, 9 the foot
            //   10 btLr with the paragraph's own alignment set, which is along the turned line
            ["cell-direction-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Table(string label, string direction, string text, int? height = null,
                    string? align = null, string? valign = null)
                {
                    builder.AddRawParagraph(TurnedTable(
                        label, direction, text, height, align, valign, pageBreak: !first));
                    first = false;
                }

                Table("Up", "btLr", "Turned up");
                Table("Down", "tbRl", "Turned down");
                Table("Up, two inches", "btLr", "Turned up", height: 2880);
                Table("Down, two inches", "tbRl", "Turned down", height: 2880);

                // Longer than an inch and a half of row, so it has to break somewhere.
                Table("Up, breaking", "btLr",
                    "Turned up and long enough to want more room than the row has", height: 2160);

                // No height stated: the row takes whatever the turned text asks for.
                Table("Up, unstated", "btLr", "Turned up and rather long");

                Table("Up, top", "btLr", "Turned up", height: 2880, valign: "top");
                Table("Up, middle", "btLr", "Turned up", height: 2880, valign: "center");
                Table("Up, foot", "btLr", "Turned up", height: 2880, valign: "bottom");
                Table("Up, centred", "btLr", "Turned up", height: 2880, align: "center");

                // And one left upright in a cell far too narrow for the word in it, which says
                // whether the breaking inside words that a turned cell shows is the turning's
                // doing or the narrowness'.
                Table("Upright, narrow", "lrTb", "Unturnable", height: 2880);

                return builder;
            },

            // A floating table with less of the page left than it needs. Four pages, so that one
            // export says what Word does with each:
            //
            //   1  anchored to the text near the foot of the page, and twice as tall as the room
            //   2  the same table with room enough, so the two can be set against each other
            //   3  anchored to the paper, put a foot down a page that has nine inches of text
            //   4  taller than any page, which cannot fit wherever it is put
            ["floating-table-break-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Page(string label, string positioning, int rows, int before, int after = 4)
                {
                    if (!first) builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                    first = false;

                    for (var i = 1; i <= before; i++)
                        builder.AddParagraph($"{label} above {i}.", ZeroSpacing, Times12);

                    builder.AddRawParagraph(PositionedTable(label, rows, positioning));

                    // Short lines, and set at sixteen point rather than twelve: where the text
                    // beside a floating table goes is settled by floating-table-probe, and this is
                    // about where the table's rows go. What matters here is that a line of the
                    // text and a line of a cell can be told apart in the export, which they cannot
                    // where the two fall within a point of each other — and at sixteen point the
                    // text keeps a rhythm of its own against the rows'.
                    for (var i = 1; i <= after; i++)
                        builder.AddParagraph($"{label} after {i}.", ZeroSpacing, Times(halfPoints: 32));
                }

                const string Daylight = "w:leftFromText=\"180\" w:rightFromText=\"180\" ";
                const string ToText = "w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpX=\"0\" w:tblpY=\"120\"/>";

                // Forty lines of text leaves about four inches of the page, and twenty rows of it
                // want rather more than that.
                Page("Tall", $"<w:tblpPr {Daylight}{ToText}", rows: 20, before: 40);

                // The same twenty rows with the whole page in front of them.
                Page("Room", $"<w:tblpPr {Daylight}{ToText}", rows: 20, before: 2);

                // A foot down the paper, on a page whose text runs to the bottom of it.
                Page("Paper", $"<w:tblpPr {Daylight}w:vertAnchor=\"page\" w:horzAnchor=\"page\" " +
                              "w:tblpX=\"1440\" w:tblpY=\"12960\"/>", rows: 12, before: 4, after: 44);

                // Sixty rows is taller than the paper, let alone what is left of it.
                Page("Longer", $"<w:tblpPr {Daylight}{ToText}", rows: 60, before: 4);

                return builder;
            },

            // The two things Word does with a floating table that this does not, each on a page
            // of its own, so that the fixtures compared against Word elsewhere stay exact and
            // these stand out as the differences they are:
            //
            //   1  room either side of the table, where Word puts text down both sides of it and
            //      this puts it down the wider one
            //   2  a clearance above the table that reaches back over a line already written,
            //      where Word shortens that line and this leaves it whole
            ["floating-table-wrap-probe"] = () =>
            {
                var builder = new DocxBuilder();

                void Lines(string label, int count)
                {
                    for (var i = 1; i <= count; i++)
                        builder.AddParagraph(
                            $"{label} line {i}, long enough to reach the table and be shortened by it.",
                            ZeroSpacing, Times12);
                }

                builder.AddParagraph("The line above the table, which is not shortened at all.",
                    ZeroSpacing, Times12);
                builder.AddRawParagraph(PositionedTable("Middle", 4,
                    "<w:tblpPr w:leftFromText=\"180\" w:rightFromText=\"180\" " +
                    "w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpXSpec=\"center\" w:tblpY=\"120\"/>"));
                Lines("Middle", 12);

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                builder.AddParagraph("The line above the table, which Word shortens and this does not.",
                    ZeroSpacing, Times12);
                builder.AddRawParagraph(PositionedTable("Reach", 4,
                    "<w:tblpPr w:leftFromText=\"180\" w:rightFromText=\"180\" " +
                    "w:topFromText=\"720\" w:bottomFromText=\"720\" " +
                    "w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpX=\"0\" w:tblpY=\"120\"/>"));
                Lines("Reach", 12);

                return builder;
            },

            // Where an exact line puts its baseline. w:lineRule="exact" fixes the height of the
            // line and says nothing about how the room is divided above and below the baseline,
            // and at twelve point every reading of that is within a step of every other. At
            // fifty-six they are points apart, which is what this measures: nine pages, each one
            // line of large text on an exact line, and the last three in other faces to show
            // whether the share is the font's or the same for all of them.
            ["exact-line-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                void Line(string label, int exactTwips, string font = TimesNewRoman, int halfPoints = 112)
                {
                    var before = first ? string.Empty : "<w:pageBreakBefore/>";
                    first = false;

                    // Two lines rather than one: the gap between their baselines is the height
                    // the exact rule actually produced, which is a different question from where
                    // the first of them sits.
                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{before}<w:spacing w:before=\"0\" w:after=\"0\" " +
                        $"w:line=\"{exactTwips}\" w:lineRule=\"exact\"/></w:pPr>" +
                        $"<w:r><w:rPr>{DocxBuilder.RunProperties(font: font, halfPoints: halfPoints)}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{label}</w:t><w:br/>" +
                        $"<w:t xml:space=\"preserve\">{label}</w:t></w:r></w:p>");

                    builder.AddParagraph("And an ordinary line beneath it.", ZeroSpacing, Times12);
                }

                // Eighteen heights of the same text, so the share of the line above the baseline
                // can be read off eighteen times over rather than fitted to one. The first nine
                // are the round ones; the rest are the heights that say what happens to the last
                // step of the grid, which a whole-point sweep cannot reach:
                //
                //   405, 411, 423   four fifths lands exactly half way between two steps, and
                //                   which way it goes depends on how many whole steps are under
                //                   it: up at 411, down at the other two
                //   416, 440        a third of a step over a whole one, and Word takes the step
                //                   anyway — the one place the nudge of a twip shows plainly
                //   420, 540        the height and its fifth both land half way, and Word takes
                //                   a further step
                //   300, 900        the same, and Word does not: the exception that made the
                //                   pattern base five rather than base twenty-five
                //   444             the ordinary half-way height, for the other side of it
                foreach (var twips in new[]
                         {
                             400, 500, 600, 800, 827, 1000, 1100, 1200, 1400,
                             405, 411, 423, 416, 440, 420, 540, 300, 900, 444
                         })
                    Line("Hxg", twips);

                // The same height in two other faces. Times keeps 0.1953 of its own line below
                // the baseline, Arial 0.1897 and Calibri 0.2200: were the share the font's, the
                // Calibri line's baseline would stand five steps of the grid from the Times one.
                // It stands in the same place. (Verdana and Georgia say the same and are left out
                // only because the pinned library has neither, so this machine cannot draw what
                // Word drew; Helvetica is out because Word sets it in Arial.)
                Line("Hxg", 1000, font: "Arial");
                Line("Hxg", 1000, font: "Calibri");

                return builder;
            },

            // How an exact-spaced paragraph gets from one line to the next. Six pages, each a
            // single paragraph of twenty lines, at heights whose fraction of a step of the grid
            // is different in each: a paragraph whose advance was rounded would drift by up to
            // three points over twenty lines, and one advancing by the height itself cannot drift
            // at all. The gaps between Word's own baselines are not all equal, which is what says
            // the advance is the height rather than a whole number of steps.
            ["exact-line-advance-probe"] = () =>
            {
                var builder = new DocxBuilder();
                var first = true;

                foreach (var twips in new[] { 401, 405, 411, 420, 423, 500 })
                {
                    var before = first ? string.Empty : "<w:pageBreakBefore/>";
                    first = false;

                    var lines = string.Concat(Enumerable.Range(2, 19)
                        .Select(i => $"<w:br/><w:t xml:space=\"preserve\">Line {i}</w:t>"));

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr>{before}<w:spacing w:before=\"0\" w:after=\"0\" " +
                        $"w:line=\"{twips}\" w:lineRule=\"exact\"/></w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">Height {twips}</w:t>{lines}</w:r></w:p>");
                }

                return builder;
            },

            // A dropped capital, which is a frame rather than a run: the letter is a paragraph
            // of its own that the paragraph after it wraps around. Written as Word writes it —
            // Word's own AppleScript was asked to make one, and this is what it produced, exact
            // line spacing, keepNext, baseline alignment and all. Six pages, so that one export
            // answers the whole of it:
            //
            //   1  Word's own three-line cap: 56 point, dropped 5.5, on an exact 41.35pt line
            //   2  Word's own two-line cap, with a ninth of an inch of daylight beside it
            //   3  the same dropped into the margin, which Word anchors to the page instead
            //   4  a paragraph shorter than the frame, then another: how far the wrap reaches
            //   5  a frame of three lines holding a letter of ordinary size: which one governs
            //   6  a cap written by hand, with no exact spacing to say how tall the frame is
            ["drop-cap-probe"] = () =>
            {
                var builder = new DocxBuilder();

                // Long enough to run to five lines at twelve point across a six-and-a-half inch
                // measure, so that the lines the frame reaches and the lines past it are both in
                // the same paragraph.
                const string Flowing =
                    "he rest of the paragraph follows the letter and has to make room for it, " +
                    "line after line, until the frame is passed and the measure comes back to " +
                    "what it was. This sentence is here to carry the paragraph past the foot of " +
                    "the frame so that both the shortened lines and the full ones can be " +
                    "measured against Word's own, which is the only thing that can settle where " +
                    "the room came from.";

                // A cap paragraph in Word's own terms. Word keeps it with the paragraph it
                // belongs to, pins the line to the height of the frame, and sets the letter on
                // the baseline of the last line the frame covers by dropping it.
                void Cap(string frame, int halfPoints, int? dropHalfPoints = null,
                    int? exactTwips = null, bool firstPage = false)
                {
                    var before = firstPage ? string.Empty : "<w:pageBreakBefore/>";
                    var spacing = exactTwips is { } twips
                        ? $"<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"{twips}\" w:lineRule=\"exact\"/>"
                        : ZeroSpacing;

                    builder.AddRawParagraph(
                        $"<w:p><w:pPr><w:keepNext/>{before}{frame}{spacing}" +
                        "<w:textAlignment w:val=\"baseline\"/></w:pPr>" +
                        $"<w:r><w:rPr>{Times(halfPoints: halfPoints, positionHalfPoints: dropHalfPoints)}</w:rPr>" +
                        "<w:t>T</w:t></w:r></w:p>");
                }

                const string Drop3 =
                    "<w:framePr w:dropCap=\"drop\" w:lines=\"3\" w:wrap=\"around\" " +
                    "w:vAnchor=\"text\" w:hAnchor=\"text\"/>";

                Cap(Drop3, halfPoints: 112, dropHalfPoints: -11, exactTwips: 827, firstPage: true);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                Cap("<w:framePr w:dropCap=\"drop\" w:lines=\"2\" w:hSpace=\"180\" w:wrap=\"around\" " +
                    "w:vAnchor=\"text\" w:hAnchor=\"text\"/>",
                    halfPoints: 70, dropHalfPoints: -6, exactTwips: 551);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                // Word anchors a margin cap to the page rather than to the text, which is the
                // whole of the difference between the two kinds.
                Cap("<w:framePr w:dropCap=\"margin\" w:lines=\"3\" w:wrap=\"around\" " +
                    "w:vAnchor=\"text\" w:hAnchor=\"page\"/>",
                    halfPoints: 112, dropHalfPoints: -11, exactTwips: 827);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                Cap(Drop3, halfPoints: 112, dropHalfPoints: -11, exactTwips: 827);
                builder.AddParagraph("wo words.", ZeroSpacing, Times12);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                // A frame that says three lines round a letter of ordinary size: if the count is
                // what shortens the lines, three of them are shortened, and if it is the letter,
                // one is.
                Cap(Drop3, halfPoints: 24);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                // No exact spacing, which is what a document written by hand rather than by Word
                // is likely to have: the frame is as tall as the letter's own line.
                Cap(Drop3, halfPoints: 104);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                // The last two are the same letter on an exact line and not dropped at all, which
                // is the only way to see where an exact line puts its baseline without the drop
                // sitting on top of the answer. Two heights, so the share of the line that falls
                // above the baseline can be read off rather than guessed at.
                Cap(Drop3, halfPoints: 112, exactTwips: 827);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                Cap(Drop3, halfPoints: 112, exactTwips: 600);
                builder.AddParagraph(Flowing, ZeroSpacing, Times12);

                return builder;
            },

            ["line-number-probe"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithSection(DocxBuilder.Section(
                        lineNumbers: DocxBuilder.LineNumbers(countBy: 1, restart: "newSection",
                            distanceTwips: 720)));

                void Lines(string label, int count, string? properties = null)
                {
                    for (var i = 1; i <= count; i++)
                        builder.AddParagraph($"{label} {i}.", properties ?? ZeroSpacing, Times12);
                }

                void Break(string numbers) =>
                    builder.AddParagraphWithSectionBreak(string.Empty,
                        DocxBuilder.Section(type: "nextPage", lineNumbers: numbers), ZeroSpacing, Times12);

                Lines("Every line", 6);

                // Nothing on it, which is still a line of the document.
                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr></w:p>");

                // And one that asks not to be counted.
                Lines("Passed over", 2, "<w:suppressLineNumbers/>" + ZeroSpacing);
                Lines("Counting again", 4);

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                Lines("A second page", 5);
                Break(DocxBuilder.LineNumbers(countBy: 1, restart: "newPage"));

                Lines("Every fifth", 12);
                Break(DocxBuilder.LineNumbers(countBy: 5, start: 10, restart: "continuous"));

                Lines("Half an inch out", 5);

                return builder;
            },

            // The border round a page: where its line falls, which pages get one, and whether the
            // four edges are asked for one at a time. Four sections, so that one export answers
            // all of it:
            //
            //   1  measured from the page, 24pt in, a point thick, on both of its pages
            //   2  measured from the text, right against it
            //   3  three points thick and asked for on the first page only
            //   4  a top and a left edge and nothing else
            ["page-border-probe"] = () =>
            {
                // The last section's properties are the document's own; every other section states
                // its own on the break that ends it, which is why each of these follows the pages
                // it describes rather than preceding them.
                var builder = new DocxBuilder()
                    .WithSection(DocxBuilder.Section(
                        pageBorders: DocxBuilder.PageBorders(offsetFrom: "page", space: 24, size: 8,
                            bottom: false, right: false)));

                void Page(string label, int lines = 3)
                {
                    for (var i = 1; i <= lines; i++)
                        builder.AddParagraph($"{label} line {i}.", ZeroSpacing, Times12);
                }

                void Break(string borders) =>
                    builder.AddParagraphWithSectionBreak(string.Empty,
                        DocxBuilder.Section(type: "nextPage", pageBorders: borders), ZeroSpacing, Times12);

                // Measured from the page, on both pages of the section.
                Page("From the page");
                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                Page("Second page of the same section");
                Break(DocxBuilder.PageBorders(offsetFrom: "page", space: 24, size: 8));

                // Measured from the text, right against it.
                Page("From the text");
                Break(DocxBuilder.PageBorders(offsetFrom: "text", space: 0, size: 8));

                // Three points thick, and asked for on the first page of the section only.
                Page("Thick, first page only");
                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                Page("Second page, which asked for none");
                Break(DocxBuilder.PageBorders(offsetFrom: "page", space: 24, size: 24,
                    display: "firstPage"));

                // And the last section, which is the document's own: a top and a left edge only.
                Page("Two edges");

                return builder;
            },

            // A character named by its code in a font of its own — <w:sym> — which is how Word
            // writes anything from the symbol fonts: a tick, an arrow, a bullet from Wingdings.
            // The run says which font and which code, and the code is written in the private-use
            // block those fonts keep their glyphs in.
            //
            // Every line pairs the symbol with plain text, so the export says where the symbol
            // was put and how wide it came out as well as whether it was drawn at all. The last
            // two lines ask the two things the format leaves open: a code written without the
            // private-use prefix, and a symbol in a run that also carries text.
            ["symbols"] = () =>
            {
                static string Line(string label, string font, string code, string? text = null) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{label} </w:t></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr>" +
                    (text is null ? string.Empty : $"<w:t xml:space=\"preserve\">{text}</w:t>") +
                    $"<w:sym w:font=\"{font}\" w:char=\"{code}\"/></w:r>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> after</w:t></w:r></w:p>";

                return new DocxBuilder()
                    .AddParagraph("Plain line for the measure.", ZeroSpacing, Times12)
                    .AddRawParagraph(Line("Wingdings arrow", "Wingdings", "F0E0"))
                    .AddRawParagraph(Line("Wingdings tick", "Wingdings", "F0FC"))
                    .AddRawParagraph(Line("Symbol pi", "Symbol", "F070"))
                    .AddRawParagraph(Line("Webdings globe", "Webdings", "F057"))
                    .AddRawParagraph(Line("Wingdings 2", "Wingdings 2", "F050"))

                    // The same tick, its code written without the F0 the private-use block adds.
                    .AddRawParagraph(Line("Unprefixed", "Wingdings", "00FC"))

                    // And a symbol at the end of a run that also carries text, which is how Word
                    // writes one that follows a word in the same formatting.
                    .AddRawParagraph(Line("After text", "Wingdings", "F0E0", "before"));
            },

            // Which rows Word puts at the top of a table's second page, and the several questions
            // that turns out to be. Each table runs past the foot of a page, and every row says in
            // its own text what it is, so the export says outright which were repeated:
            //
            //   one heading    the plainest case
            //   two headings   whether a run of them all repeats
            //   a late one     a row marked heading that is not at the top of the table
            //   headings only  a table with nothing but headings, which cannot repeat forever
            ["table-heading-probe"] = () =>
            {
                var builder = new DocxBuilder();

                void Filler(int from, int to)
                {
                    for (var i = from; i <= to; i++)
                        builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);
                }

                Filler(1, 35);
                builder.AddRawParagraph(HeadingTable("One", 16, 1));

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                Filler(36, 70);
                builder.AddRawParagraph(HeadingTable("Two", 16, 1, 2));

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                Filler(71, 105);
                builder.AddRawParagraph(HeadingTable("Late", 16, 3));

                builder.AddRawParagraph($"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr></w:p>");
                Filler(106, 140);
                builder.AddRawParagraph(HeadingTable("Only", 16, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
                    11, 12, 13, 14, 15, 16));

                return builder;
            },

            // A table row taller than what is left of the page, which Word breaks across the two
            // unless it is told not to. The borders at the break are what this really asks about:
            // nothing else says whether a row that continues is closed off where it stops.
            ["table-split"] = () =>
            {
                var builder = new DocxBuilder();

                for (var i = 1; i <= 38; i++)
                    builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

                builder.AddRawParagraph(SplittableTable("Splitting", 20, cantSplit: false));

                // And the same again on the next page, told to stay whole.
                for (var i = 39; i <= 70; i++)
                    builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

                return builder.AddRawParagraph(SplittableTable("Whole", 12, cantSplit: true));
            },

            // The four ways a section can sit its text on the page. Each is its own section so
            // that one export answers all of them, and the last has several paragraphs because
            // justified alignment has to put its extra space somewhere.
            ["vertical-alignment"] = () =>
            {
                var builder = new DocxBuilder();

                foreach (var alignment in new[] { "top", "center", "bottom" })
                {
                    builder.AddParagraph($"A {alignment}-aligned page.", ZeroSpacing, Times12);
                    builder.AddParagraphWithSectionBreak(
                        "The second line of it.",
                        DocxBuilder.Section(verticalAlignment: alignment),
                        ZeroSpacing, Times12);
                }

                for (var i = 1; i <= 4; i++)
                    builder.AddParagraph($"Justified paragraph {i} of four.", ZeroSpacing, Times12);

                return builder.WithSection(DocxBuilder.Section(verticalAlignment: "both"));
            },

            // Four sections on different paper with different margins, one of each break type the
            // engine treats differently: a next-page break onto landscape, a continuous break that
            // changes the margins part-way down a page, and an even-page break that has to leave a
            // blank page behind.
            ["sections"] = () =>
            {
                var builder = new DocxBuilder();

                for (var i = 1; i <= 3; i++)
                    builder.AddParagraph($"Portrait paragraph {i}.", ZeroSpacing, Times12);

                builder.AddParagraphWithSectionBreak(
                    "Last line of the portrait section.",
                    DocxBuilder.Section(),
                    ZeroSpacing, Times12);

                for (var i = 1; i <= 3; i++)
                    builder.AddParagraph($"Landscape paragraph {i}.", ZeroSpacing, Times12);

                // Landscape US Letter with half-inch margins.
                builder.AddParagraphWithSectionBreak(
                    "Last line of the landscape section.",
                    DocxBuilder.Section(
                        widthTwips: 15840, heightTwips: 12240, landscape: true,
                        top: 720, right: 720, bottom: 720, left: 720),
                    ZeroSpacing, Times12);

                for (var i = 1; i <= 3; i++)
                    builder.AddParagraph($"Wide-margin paragraph {i}.", ZeroSpacing, Times12);

                // Same paper, deeper left margin, carrying on down the same page.
                builder.AddParagraphWithSectionBreak(
                    "Last line of the indented section.",
                    DocxBuilder.Section(
                        type: "continuous",
                        widthTwips: 15840, heightTwips: 12240, landscape: true,
                        top: 720, right: 720, bottom: 720, left: 2880),
                    ZeroSpacing, Times12);

                for (var i = 1; i <= 3; i++)
                    builder.AddParagraph($"Final paragraph {i}.", ZeroSpacing, Times12);

                // Back to portrait, on the next even page — which is two away, so a blank one has
                // to be left behind.
                return builder.WithSection(DocxBuilder.Section(type: "evenPage"));
            },

            // Both kinds of note on one page, with the body sized so that the footnote area at the
            // foot and the endnotes flowing after the text nearly meet: if the space reserved for
            // the footnotes were wrong by more than a line, this would paginate differently from
            // Word.
            ["notes-mixed"] = () =>
            {
                var builder = new DocxBuilder();

                var foot = builder.AddFootnote(DocxBuilder.FootnoteBody("A note at the foot.", Times10));
                var end = builder.AddEndnote(DocxBuilder.EndnoteBody("A note at the end.", Times10));

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">A sentence with a footnote</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(foot) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> and an endnote</w:t></w:r>" +
                    DocxBuilder.EndnoteReference(end) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                for (var i = 1; i <= 40; i++)
                    builder.AddParagraph($"Body paragraph number {i} of forty.", ZeroSpacing, Times12);

                return builder;
            },

            // How far a raised or lowered run moves, to a thousandth of an em. superscript-probe
            // carries three sizes of one face, which was enough to say the shift is a share of
            // the size and not enough to say which share: each reading is a difference of two
            // baselines, and both are rounded to Word's grid, so twelve point can only say the
            // raise is between 0.32 and 0.36 of it. Ninety-six point says it to a four-hundredth.
            //
            // Three faces as well, because the shift turns out to depend on the face. Eleven were
            // measured while this was written — the eight beyond these three are not pinned by the
            // suite, so they cannot be compared against Word and are not kept here — and what they
            // showed is written up in ResolvedRunFormat.BaselineShiftPoints: Calibri and Candara
            // agree on every vertical metric a face carries and Word raises a superscript 0.3325
            // of the size in one and 0.4525 in the other.
            ["superscript-shift-probe"] = () =>
            {
                string[] faces = ["Times New Roman", "Arial", "Calibri"];
                int[] sizes = [8, 12, 24, 48, 96];

                var builder = new DocxBuilder();
                var first = true;

                foreach (var face in faces)
                {
                    foreach (var shift in new[] { "superscript", "subscript" })
                    {
                        foreach (var size in sizes)
                        {
                            var font = $"<w:rFonts w:ascii=\"{face}\" w:hAnsi=\"{face}\"/>" +
                                       $"<w:sz w:val=\"{size * 2}\"/>";

                            builder.AddRawParagraph(
                                $"<w:p><w:pPr>{(first ? ZeroSpacing : ZeroSpacingNewPage)}" +
                                $"<w:rPr>{font}</w:rPr></w:pPr>" +
                                $"<w:r><w:rPr>{font}</w:rPr><w:t xml:space=\"preserve\">H</w:t></w:r>" +
                                $"<w:r><w:rPr>{font}<w:vertAlign w:val=\"{shift}\"/></w:rPr>" +
                                $"<w:t>H</w:t></w:r></w:p>");

                            first = false;
                        }
                    }
                }

                return builder;
            },

            // What a raised or lowered run does to the line that holds it, and how far it is
            // raised. Each line is a plain run and a shifted one, so the difference from the
            // control line is the whole of what the shift did; the shifted run's own baseline
            // comes out of the export beside it, which is the raise itself. The sizes are chosen
            // so that the raise cannot be a fraction of any one of them by coincidence.
            ["superscript-probe"] = () =>
            {
                var builder = new DocxBuilder();

                string Line(string label, string shifted, string? shiftedProperties) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{label} </w:t></w:r>" +
                    (shiftedProperties is null
                        ? string.Empty
                        : $"<w:r><w:rPr>{shiftedProperties}</w:rPr><w:t>{shifted}</w:t></w:r>") +
                    "</w:p>";

                var super12 = Times12 + "<w:vertAlign w:val=\"superscript\"/>";
                var sub12 = Times12 + "<w:vertAlign w:val=\"subscript\"/>";
                var super20 = Times(40) + "<w:vertAlign w:val=\"superscript\"/>";

                builder.AddRawParagraph(Line("Plain twelve point", "", null));
                builder.AddRawParagraph(Line("Raised twelve", "888", super12));
                builder.AddRawParagraph(Line("Lowered twelve", "888", sub12));
                builder.AddRawParagraph(Line("Raised twenty", "888", super20));
                builder.AddRawParagraph(Line("Plain again", "", null));
                builder.AddRawParagraph(Line("Same digits plain", "888", Times12));

                // A raised run beside text far larger than it, where the raise cannot reach above
                // the big run's own ascent: the line should be the big run's and nothing more.
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times(40)}</w:rPr><w:t xml:space=\"preserve\">Twenty point </w:t></w:r>" +
                    $"<w:r><w:rPr>{super12}</w:rPr><w:t>888</w:t></w:r></w:p>");

                builder.AddRawParagraph(Line("Plain last", "", null));

                // The same two shifts at a size far enough from the first to say what they are a
                // fraction of, rather than fitting a ratio to one measurement.
                var super40 = Times(80) + "<w:vertAlign w:val=\"superscript\"/>";
                var sub40 = Times(80) + "<w:vertAlign w:val=\"subscript\"/>";

                builder.AddRawParagraph(Line("Raised forty", "888", super40));
                builder.AddRawParagraph(Line("Plain between", "", null));
                builder.AddRawParagraph(Line("Lowered forty", "888", sub40));
                builder.AddRawParagraph(Line("Plain after", "", null));

                return builder;
            },

            // Notes set under the text rather than at the foot of the page, which the section
            // asks for with w:pos. The two are the same thing on a page whose text reaches the
            // bottom margin, so what says which was done is a page whose text stops early: the
            // first page here is full and the second holds four lines.
            ["footnote-beneath-text"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithSection(DocxBuilder.Section(footnotePosition: "beneathText"));

                string WithNote(int i, int id) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph {i}, with a note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(id) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>";

                var first = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("A note on the page whose text fills it.", Times10));

                builder.AddRawParagraph(WithNote(1, first));

                for (var i = 2; i <= 44; i++)
                    builder.AddParagraph($"Body paragraph {i} of forty-six.", ZeroSpacing, Times12);

                var second = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("A note on the page whose text stops early.", Times10));

                builder.AddRawParagraph(WithNote(45, second));
                builder.AddParagraph("Body paragraph 46 of forty-six.", ZeroSpacing, Times12);

                return builder;
            },

            // A reference on the very last line a page has room for, with the notes at the foot
            // as a document has them by default. What is measured is what gives: does the line
            // carrying the reference move to the next page so its note can begin under it, or
            // does the line stay and the whole note go over?
            ["footnote-carry-probe"] = () =>
            {
                var builder = new DocxBuilder();

                for (var i = 1; i <= 44; i++)
                    builder.AddParagraph($"Body paragraph {i} of forty-six.", ZeroSpacing, Times12);

                var note = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("A note whose reference is on the last line.", Times10));

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph 45, with a note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(note) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                return builder.AddParagraph("Body paragraph 46 of forty-six.", ZeroSpacing, Times12);
            },

            // Endnotes gathered at the end of each section rather than of the document, which the
            // section asks for with w:pos. Two sections, each with two notes and each short enough
            // that where its notes go is unmistakable.
            ["endnote-section-end"] = () =>
            {
                // Word writes this in both places and reads it from the settings, so the fixture
                // is written the way Word writes one.
                var builder = new DocxBuilder()
                    .WithEndnotePosition("sectEnd")
                    .WithSection(DocxBuilder.Section(endnotePosition: "sectEnd"));

                var note = 0;

                string WithNote(int i, int id) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph {i}, with a note</w:t></w:r>" +
                    DocxBuilder.EndnoteReference(id) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>";

                for (var i = 1; i <= 4; i++)
                {
                    if (i is 2 or 4)
                    {
                        builder.AddRawParagraph(WithNote(i,
                            builder.AddEndnote(
                                DocxBuilder.EndnoteBody($"Note {++note}, of the first section.", Times10))));

                        continue;
                    }

                    builder.AddParagraph($"Body paragraph {i} of the first section.", ZeroSpacing, Times12);
                }

                builder.AddParagraphWithSectionBreak(
                    "The last paragraph of the first section.",
                    DocxBuilder.Section(type: "nextPage", endnotePosition: "sectEnd"), ZeroSpacing, Times12);

                for (var i = 1; i <= 4; i++)
                {
                    if (i is 2 or 4)
                    {
                        builder.AddRawParagraph(WithNote(i + 10,
                            builder.AddEndnote(DocxBuilder.EndnoteBody($"Note {++note}, of the second section.",
                                Times10))));

                        continue;
                    }

                    builder.AddParagraph($"Body paragraph {i + 10} of the second section.", ZeroSpacing, Times12);
                }

                return builder;
            },

            // Notes numbered again from one on every page, which is what a document of many notes
            // a page usually asks for. What is measured is which page a note counts as being on,
            // since a reference near a page's edge may be moved by the line that carries it.
            ["footnote-restart-page"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithSection(DocxBuilder.Section(footnoteRestart: "eachPage"));

                return WithNotesThroughout(builder, sections: false);
            },

            // The same, restarted at each section instead. The one section break is at a
            // paragraph no page break falls on, so a section here runs over more than one page and
            // the two ways of restarting cannot give the same answer.
            ["footnote-restart-section"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithSection(DocxBuilder.Section(footnoteRestart: "eachSect"));

                return WithNotesThroughout(builder, sections: true, restart: "eachSect");
            },

            // Endnotes restarted at each section while still gathered at the end of the document,
            // which is a combination the format allows and Word's own dialog offers. What is
            // measured is whether the numbers really do repeat down one list.
            ["endnote-restart-section"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithSection(DocxBuilder.Section(endnoteRestart: "eachSect"));

                var note = 0;

                for (var i = 1; i <= 60; i++)
                {
                    if (i % 10 == 3 || i % 10 == 7)
                    {
                        var id = builder.AddEndnote(
                            DocxBuilder.EndnoteBody($"Note {++note} of the document.", Times10));

                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph {i}, with a note</w:t></w:r>" +
                            DocxBuilder.EndnoteReference(id) +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                        continue;
                    }

                    if (i == 45)
                    {
                        builder.AddParagraphWithSectionBreak(
                            $"Body paragraph {i} of sixty, closing a section.",
                            DocxBuilder.Section(type: "nextPage", endnoteRestart: "eachSect"),
                            ZeroSpacing, Times12);

                        continue;
                    }

                    builder.AddParagraph($"Body paragraph {i} of sixty.", ZeroSpacing, Times12);
                }

                return builder;
            },

            // The same footnote arrangement with the separator paragraph's mark three times the
            // size. Word's export says both how the separator's line box is sized and whether the
            // rule's offset within that box is fixed or proportional to it.
            ["footnote-separator-probe"] = () =>
            {
                var builder = new DocxBuilder().WithFootnoteSeparatorMark(Times36);
                var note = builder.AddFootnote(DocxBuilder.FootnoteBody("The note.", Times10));

                return builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">A sentence with a note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(note) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");
            },

            // A note too long for the room left under the page its reference falls on, which Word
            // divides between that page and the next. What is measured here is where it divides:
            // how much of the page the notes are allowed to take, how many lines of the note stay
            // with the reference, and what stands above the rest of it on the page after.
            ["footnote-split-probe"] = () =>
            {
                var builder = new DocxBuilder();

                var body = string.Join("", Enumerable.Range(1, 20).Select(i =>
                    $"<w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/>{ZeroSpacing}</w:pPr>" +
                    (i == 1
                        ? $"<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r>"
                        : string.Empty) +
                    $"<w:r><w:rPr>{Times10}</w:rPr><w:t xml:space=\"preserve\">" +
                    $"Line {i} of a note far too long for the foot of one page." +
                    "</w:t></w:r></w:p>"));

                var note = builder.AddFootnote(body);

                for (var i = 1; i <= 40; i++)
                {
                    if (i == 30)
                    {
                        builder.AddRawParagraph(
                            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Body paragraph {i}, which carries the long note</w:t></w:r>" +
                            DocxBuilder.FootnoteReference(note) +
                            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                        continue;
                    }

                    builder.AddParagraph($"Body paragraph {i} of forty.", ZeroSpacing, Times12);
                }

                return builder;
            },

            // A note that outlasts the document: one short paragraph carrying a note so long that
            // it cannot be finished on the page the reference is on, nor on the page after, with
            // no body text left to carry the pages. What is measured is whether Word makes pages
            // for the rest of it and what it puts on them.
            ["footnote-overrun-probe"] = () =>
            {
                var builder = new DocxBuilder();

                var body = string.Join("", Enumerable.Range(1, 90).Select(i =>
                    $"<w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/>{ZeroSpacing}</w:pPr>" +
                    (i == 1
                        ? "<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r>"
                        : string.Empty) +
                    $"<w:r><w:rPr>{Times10}</w:rPr><w:t xml:space=\"preserve\">" +
                    $"Line {i} of a note that outlasts the document it belongs to." +
                    "</w:t></w:r></w:p>"));

                var note = builder.AddFootnote(body);

                return builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">The only paragraph, which carries the note</w:t></w:r>" +
                    DocxBuilder.FootnoteReference(note) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");
            },

            // External and internal links, the latter pointing across a page break at a bookmark.
            // The links carry the blue underline directly rather than through the Hyperlink
            // character style, because what is under test here is the target, not the cascade.
            ["hyperlinks"] = () =>
            {
                var builder = new DocxBuilder();
                var home = builder.AddExternalHyperlink("https://example.com/");
                var deep = builder.AddExternalHyperlink("https://example.com/docs/reference?page=2#top");
                var mail = builder.AddExternalHyperlink("mailto:someone@example.com");

                var linkStyle = Times(color: "0563C1", underline: "single");

                builder
                    .AddParagraph("Links to other places.", ZeroSpacing, Times12)
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Start at </w:t></w:r>" +
                        DocxBuilder.Hyperlink("the home page", home, runProperties: linkStyle) +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> and then read the </w:t></w:r>" +
                        DocxBuilder.Hyperlink("reference", deep, runProperties: linkStyle) +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>")
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Write to </w:t></w:r>" +
                        DocxBuilder.Hyperlink("someone@example.com", mail, runProperties: linkStyle) +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">, or jump to </w:t></w:r>" +
                        DocxBuilder.Hyperlink("the appendix", anchor: "appendix", runProperties: linkStyle) +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>")
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        DocxBuilder.Hyperlink("A large link", home,
                            runProperties: Times(48, color: "0563C1", underline: "single")) +
                        "</w:p>")
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacingNewPage}</w:pPr>{DocxBuilder.Bookmark("appendix")}" +
                        $"<w:r><w:rPr>{Times24}</w:rPr><w:t>Appendix</w:t></w:r></w:p>")
                    .AddParagraph("The place the internal link points at.", ZeroSpacing, Times12);

                return builder;
            },

            // The style cascade end to end, including the toggle-property cancellation.
            ["styles"] = () => new DocxBuilder()
                .WithStyles("""
                            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                              <w:docDefaults>
                                <w:rPrDefault><w:rPr><w:rFonts w:ascii="Times New Roman"/><w:sz w:val="24"/></w:rPr></w:rPrDefault>
                              </w:docDefaults>
                              <w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
                              <w:style w:type="paragraph" w:styleId="Heading1">
                                <w:name w:val="heading 1"/>
                                <w:basedOn w:val="Normal"/>
                                <w:pPr><w:spacing w:before="240" w:after="120"/></w:pPr>
                                <w:rPr><w:b/><w:sz w:val="40"/></w:rPr>
                              </w:style>
                              <w:style w:type="character" w:styleId="Strong"><w:rPr><w:b/></w:rPr></w:style>
                            </w:styles>
                            """)
                .AddParagraph("Heading from a style", "<w:pStyle w:val=\"Heading1\"/>")
                .AddParagraph("Body text inheriting the document defaults.")
                .AddRawParagraph(
                    """
                    <w:p>
                      <w:pPr><w:pStyle w:val="Heading1"/></w:pPr>
                      <w:r><w:rPr><w:rStyle w:val="Strong"/></w:rPr><w:t>Bold style inside a bold heading cancels to regular</w:t></w:r>
                    </w:p>
                    """)
        };

    /// <summary>
    /// Source documents for the real-Word fixtures.
    /// </summary>
    /// <remarks>
    /// These are not fixtures themselves. They are opened in Word and saved back out by it, and
    /// the result is what lands in Fixtures/Real — a package Word wrote, carrying its full
    /// styles.xml with every latent style, its settings.xml with the compatibility flags, its own
    /// theme and docProps. None of that can be produced by hand, and it is the whole reason for
    /// having real documents: what is being tested is Word's markup, not the content.
    ///
    /// The content deliberately sticks to constructs that are implemented, so that a failure
    /// means Word's markup broke something rather than simply naming a feature that does not
    /// exist yet. See tools/make-real-fixtures.sh.
    /// </remarks>
    public static IReadOnlyDictionary<string, Func<DocxBuilder>> RealSeeds { get; } =
        new Dictionary<string, Func<DocxBuilder>>(StringComparer.Ordinal)
        {
            // A diagram, which is the one thing here that has to come back through Word to be
            // worth anything: a document describes a diagram twice over, as what it means and as
            // the arrangement it last came to, and Word rebuilds the second from the first every
            // time it opens one. Every other reader draws the cached arrangement, and so does
            // this — so the only cache worth comparing against Word's own drawing is the one Word
            // itself wrote, which is what saving this seed through Word produces.
            ["smartart"] = () => new DocxBuilder()
                .WithSmartArt(DocxBuilder.SmartArtCachedDrawing(
                    DocxBuilder.SmartArtShape("One", 0, 0, 144, 54),
                    DocxBuilder.SmartArtShape("Two", 108, 63, 144, 54, geometry: "ellipse",
                        fillHex: "ED7D31"),
                    DocxBuilder.SmartArtShape("Three", 216, 126, 144, 54, geometry: "rect",
                        fillHex: "70AD47")))
                .AddParagraph("Paragraph before the diagram.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.SmartArtDrawing(360, 180) + "</w:p>")
                .AddParagraph("Paragraph after the diagram.", ZeroSpacing, Times12),

            // The same diagram again, and the reason for a second one: in the first, every box
            // holds a line or two, and how the text is placed in a box can be read two ways that
            // no number there tells apart — that the block is the wrong height, or that its first
            // baseline sits wrong inside it. A box of five lines separates them, since the first
            // reading grows with the line count and the second does not.
            //
            // The boxes are tall and hold one, two and three short paragraphs, so that every
            // block fits inside its box. That matters: a block too tall for its box brings a
            // second rule into it — Word grows the whole drawing to hold the overflow — and one
            // question at a time is the point of a probe. Wrapping was tried and would not do,
            // because Word picks the type size for a diagram itself and kept it at thirty-six.
            //
            // And the text is anchored to the top of its box rather than centred, which is what
            // makes the whole thing readable: centred, the height of the block and the place of
            // its first baseline inside it enter the answer added together, and no measurement
            // can separate them. Against the top, the first baseline is the frame plus one ascent
            // and nothing else. The centred case is what the smartart fixture already holds.
            ["smartart-lines"] = () => new DocxBuilder { DiagramTextAnchor = "t" }
                .WithSmartArt(DocxBuilder.SmartArtCachedDrawing(
                    DocxBuilder.SmartArtShape("One", 0, 0, 150, 216, sizeHundredths: 1200),
                    DocxBuilder.SmartArtShape("Two", 152, 0, 150, 216,
                        sizeHundredths: 1200, fillHex: "ED7D31"),
                    DocxBuilder.SmartArtShape("Three", 304, 0, 150, 216,
                        sizeHundredths: 1200, fillHex: "70AD47")),
                    "One", "Two\nlines", "Three\nlines\nhere")
                .AddParagraph("Paragraph before the diagram.", ZeroSpacing, Times12)
                .AddRawParagraph($"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                                 DocxBuilder.SmartArtDrawing(456, 216) + "</w:p>")
                .AddParagraph("Paragraph after the diagram.", ZeroSpacing, Times12),

            ["report"] = () => new DocxBuilder()
                .AddParagraph("Quarterly Operations Review",
                    "<w:spacing w:after=\"240\"/><w:jc w:val=\"center\"/>", Times(40, bold: true))
                .AddParagraph("Summary", "<w:spacing w:before=\"240\" w:after=\"120\"/>", Times(28, bold: true))
                .AddParagraph(
                    "Throughput improved across every region this quarter, with the northern "
                    + "territories accounting for the larger share of the gain. The figures below "
                    + "are drawn from the regional ledgers and have not been adjusted for seasonal "
                    + "variation, which typically flatters the second half of the year.",
                    "<w:spacing w:after=\"120\"/><w:jc w:val=\"both\"/>", Times12)
                .AddParagraph("Regional detail", "<w:spacing w:before=\"240\" w:after=\"120\"/>", Times(28, bold: true))
                .AddRawParagraph($"""
                                  <w:tbl>
                                    <w:tblPr>
                                      <w:tblW w:w="0" w:type="auto"/>
                                      <w:tblBorders>
                                        <w:top w:val="single" w:sz="4" w:color="auto"/>
                                        <w:left w:val="single" w:sz="4" w:color="auto"/>
                                        <w:bottom w:val="single" w:sz="4" w:color="auto"/>
                                        <w:right w:val="single" w:sz="4" w:color="auto"/>
                                        <w:insideH w:val="single" w:sz="4" w:color="auto"/>
                                        <w:insideV w:val="single" w:sz="4" w:color="auto"/>
                                      </w:tblBorders>
                                    </w:tblPr>
                                    <w:tr>
                                      <w:tc><w:p><w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>Region</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>Units</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times(24, bold: true)}</w:rPr><w:t>Change</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                    <w:tr>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>Northern territories</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>12,480</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>+18%</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                    <w:tr>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>Southern territories</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>9,240</w:t></w:r></w:p></w:tc>
                                      <w:tc><w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>+4%</w:t></w:r></w:p></w:tc>
                                    </w:tr>
                                  </w:tbl>
                                  """)
                .AddParagraph("Notes", "<w:pageBreakBefore/><w:spacing w:after=\"120\"/>", Times(28, bold: true))
                .AddParagraphWithRuns([
                    ("Figures are ", Times12),
                    ("provisional", Times(24, italic: true)),
                    (" and subject to revision. Contact the ", Times12),
                    ("operations desk", Times(24, bold: true)),
                    (" with corrections.", Times12)
                ], "<w:spacing w:after=\"120\"/>")
                .AddParagraph("Adjustments applied after the ledger close are listed separately.",
                    "<w:ind w:left=\"720\" w:hanging=\"360\"/>", Times12),

            ["memo"] = () => new DocxBuilder()
                .WithPage(left: 1080, right: 1080, top: 1080, bottom: 1080)
                .AddParagraph("Internal memorandum", "<w:spacing w:after=\"240\"/>", Times(32, bold: true))
                .AddParagraphWithRuns([("To: ", Times(24, bold: true)), ("All staff", Times12)])
                .AddParagraphWithRuns([("Date: ", Times(24, bold: true)), ("12 August 2026", Times12)])
                .AddParagraphWithRuns([("Subject: ", Times(24, bold: true)), ("Office closure", Times12)],
                    "<w:spacing w:after=\"240\"/>")
                .AddParagraph(
                    "The building will be closed for maintenance on the last Friday of the month. "
                    + "Staff who need access on that day should arrange it in advance; the usual "
                    + "entry cards will not work while the work is under way.",
                    "<w:jc w:val=\"both\"/>", Times12)
                .AddParagraph("Thank you for your patience.",
                    "<w:spacing w:before=\"240\"/><w:jc w:val=\"right\"/>", Times(24, italic: true)),

            // A newsletter: a running head, a footer that counts the pages, an address to follow,
            // and a body set in two columns. None of that lives in the body — a header is a part
            // of its own reached by a relationship, and an address never appears in the document
            // at all, only in a relationship marked external — so what a round trip through Word
            // rewrites here is the shape of the package rather than the words in it. The columns
            // are the other half of it: Word works their widths out and writes them down instead
            // of leaving the measure to be divided.
            ["newsletter"] = () =>
            {
                var builder = new DocxBuilder()
                    .WithHeaderFooter(header: true,
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t>The Ledger, August</w:t></w:r></w:p>")
                    .WithHeaderFooter(header: false,
                        $"<w:p><w:pPr>{ZeroSpacing}<w:jc w:val=\"center\"/></w:pPr>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Page </w:t></w:r>" +
                        $"<w:fldSimple w:instr=\" PAGE \"><w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t>1</w:t></w:r></w:fldSimple>" +
                        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> of </w:t></w:r>" +
                        $"<w:fldSimple w:instr=\" NUMPAGES \"><w:r><w:rPr>{Times12}</w:rPr>" +
                        "<w:t>1</w:t></w:r></w:fldSimple></w:p>");

                var address = builder.AddExternalHyperlink("https://example.com/ledger/august");

                builder.AddParagraph("Notes from the desk",
                    "<w:spacing w:after=\"120\"/>", Times(28, bold: true));

                for (var i = 1; i <= 14; i++)
                {
                    builder.AddParagraph(
                        $"Item {i}. The figures behind this note were taken from the regional " +
                        "ledgers on the day of publication, and the wording is long enough that " +
                        "it wraps inside its column rather than fitting on a single line of it.",
                        "<w:spacing w:after=\"120\"/><w:jc w:val=\"both\"/>", Times12);
                }

                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">The full figures are at </w:t></w:r>" +
                    DocxBuilder.Hyperlink("example.com/ledger/august", address,
                        runProperties: Times(24, color: "0563C1", underline: "single")) +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>");

                return builder.WithSection(DocxBuilder.Section(columns: 2, columnSeparator: true));
            },

            // Notes at the foot of the page and at the end of the document. Word keeps each kind
            // in a part of its own, and writes into it two notes nobody asked for: the rule above
            // the notes and the one above a note carried on to the next page. Ours are written by
            // hand from what Word produces; these are Word's own, along with the styles it adds
            // to a document the moment a note is put in one.
            ["notes"] = () =>
            {
                var builder = new DocxBuilder();

                string Paragraph(string text, string tail) =>
                    $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                    $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r>" +
                    tail + $"<w:r><w:rPr>{Times12}</w:rPr><w:t>.</w:t></w:r></w:p>";

                var first = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("The ledgers are closed on the last working day.", Times(20)));
                var second = builder.AddFootnote(
                    DocxBuilder.FootnoteBody("Seasonal adjustment is applied afterwards.", Times(20)));
                var ending = builder.AddEndnote(
                    DocxBuilder.EndnoteBody("Figures for the previous year are held separately.", Times(20)));

                builder.AddParagraph("Sources and their treatment",
                    "<w:spacing w:after=\"120\"/>", Times(28, bold: true));

                builder.AddRawParagraph(Paragraph(
                    "Throughput is counted at the point of dispatch",
                    DocxBuilder.FootnoteReference(first, Times(20))));

                for (var i = 1; i <= 6; i++)
                {
                    builder.AddParagraph(
                        $"Paragraph {i} between the notes, written long enough to wrap and so to " +
                        "push the note that follows it further down the page.",
                        ZeroSpacing, Times12);
                }

                builder.AddRawParagraph(Paragraph(
                    "The northern figures are gathered a week later than the rest",
                    DocxBuilder.FootnoteReference(second, Times(20))));

                builder.AddRawParagraph(Paragraph(
                    "Comparisons with last year are made on the closing figures only",
                    DocxBuilder.EndnoteReference(ending, Times(20))));

                return builder;
            },

            // Lists. A numbering part is the one thing in a document that is never written as it
            // is read: the paragraphs say which list and which level, and everything about how
            // the label looks is somewhere else again. Word rewrites that part wholesale — its
            // own identifiers, its own template numbers, a full nine levels where this asks for
            // two — and this is the seed that makes it do so.
            ["minutes"] = () => new DocxBuilder()
                .WithNumbering(
                    DocxBuilder.NumberingLevel(0, "decimal", "%1.") +
                    DocxBuilder.NumberingLevel(1, "lowerLetter", "%2)"),
                    DocxBuilder.NumberingLevel(0, "bullet", "•"))
                .AddParagraph("Minutes of the operations meeting",
                    "<w:spacing w:after=\"240\"/>", Times(32, bold: true))
                .AddParagraph("Matters arising", "<w:spacing w:after=\"120\"/>", Times(28, bold: true))
                .AddListParagraph(
                    "The northern depot reported a shortfall against the forecast for July.",
                    1, runProperties: Times12)
                .AddListParagraph("The cause was traced to a late delivery.", 1, 1, Times12)
                .AddListParagraph("The forecast for August has been revised accordingly.", 1, 1, Times12)
                .AddListParagraph(
                    "The southern depot is unchanged and needs no action this month.",
                    1, runProperties: Times12)
                .AddParagraph("Actions", "<w:spacing w:before=\"240\" w:after=\"120\"/>", Times(28, bold: true))
                .AddListParagraph("Circulate the revised forecast before the month end.", 2,
                    runProperties: Times12)
                .AddListParagraph("Confirm the delivery schedule with the carrier.", 2,
                    runProperties: Times12)
                .AddParagraph("Next meeting on the first Tuesday of September.",
                    "<w:spacing w:before=\"240\"/>", Times12),

            // A picture and a text box, which is where Word's markup stops resembling anything
            // written by hand: a drawing it has saved carries an alternative for older readers
            // beside the one it means, and the two describe the same box in two different
            // languages. Ours writes the modern half alone, so this is the seed that says whether
            // a document holding both is read the way Word reads it.
            ["brochure"] = () =>
            {
                var builder = new DocxBuilder();
                var badge = builder.AddImagePart(PngWriter.Diagonal(64));

                var body = string.Join(' ', Enumerable.Repeat(
                    "The service runs from the depot every hour and takes the coast road.", 8));

                builder
                    .AddParagraph("The coast service", "<w:spacing w:after=\"120\"/>", Times(32, bold: true))
                    .AddImageParagraph(badge, 96, 96, "<w:spacing w:after=\"120\"/>",
                        leadingText: "Route badge ")
                    .AddRawParagraph(
                        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                        DocxBuilder.AnchoredShape(144, 72,
                            ShapeText("Timetable"), alignX: "right", fillHex: "DEEBF7",
                            lineHex: "2E74B5") +
                        $"<w:r><w:rPr>{Times12}</w:rPr>" +
                        $"<w:t xml:space=\"preserve\">{DocxBuilder.Escape(body)}</w:t></w:r></w:p>")
                    .AddParagraph("Fares are unchanged for the season.",
                        "<w:spacing w:before=\"120\"/>", Times12);

                return builder;
            }
        };

    /// <summary>Builds a fixture's bytes by name.</summary>
    public static byte[] Build(string name) => All[name]().Build();

    /// <summary>
    /// Writes the seed documents that <c>tools/make-real-fixtures.sh</c> feeds through Word.
    /// </summary>
    public static IReadOnlyList<string> MaterializeRealSeeds(string directory)
    {
        Directory.CreateDirectory(directory);

        var written = new List<string>();
        foreach (var (name, build) in RealSeeds)
        {
            var path = Path.Combine(directory, name + ".docx");
            File.WriteAllBytes(path, build().Build());
            written.Add(path);
        }

        return written;
    }

    /// <summary>
    /// Writes every fixture to <c>Fixtures/Minimal</c> so the documents can be opened in Word.
    /// </summary>
    public static IReadOnlyList<string> MaterializeAll()
    {
        Directory.CreateDirectory(TestPaths.MinimalFixtures);

        var written = new List<string>();
        foreach (var (name, _) in All)
        {
            var path = Path.Combine(TestPaths.MinimalFixtures, name + ".docx");
            File.WriteAllBytes(path, Build(name));
            written.Add(path);
        }

        return written;
    }
}