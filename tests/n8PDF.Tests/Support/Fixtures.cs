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
        string? underline = null,
        int? kerningHalfPoints = null) =>
        DocxBuilder.RunProperties(
            font: TimesNewRoman, halfPoints: halfPoints, bold: bold, italic: italic,
            strike: strike, color: color, underline: underline,
            kerningHalfPoints: kerningHalfPoints);

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
                    "<w:pageBreakBefore/><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"480\" w:lineRule=\"auto\"/>", Times12)
                .AddParagraph("One and a half spaced first line.",
                    "<w:pageBreakBefore/><w:spacing w:before=\"0\" w:after=\"0\" w:line=\"360\" w:lineRule=\"auto\"/>", Times12),

            ["line-spacing"] = () => new DocxBuilder()
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("Single spaced text.", 8)),
                    "<w:spacing w:line=\"240\" w:lineRule=\"auto\" w:after=\"0\"/>", Times12)
                .AddParagraph(
                    string.Join(' ', Enumerable.Repeat("Double spaced text.", 8)),
                    "<w:spacing w:line=\"480\" w:lineRule=\"auto\" w:after=\"0\"/>", Times12),

            ["tabs"] = () => new DocxBuilder()
                .AddRawParagraph($"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>A</w:t><w:tab/><w:t>B</w:t><w:tab/><w:t>C</w:t></w:r></w:p>")
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
                .AddRawParagraph($"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>Line one</w:t><w:br/><w:t>Line two</w:t></w:r></w:p>")
                .AddRawParagraph($"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>Before the page break</w:t><w:br w:type=\"page\"/><w:t>After the page break</w:t></w:r></w:p>"),

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
                        <w:tblLayout w:type="fixed"/>
                        <w:tblBorders>
                          <w:top w:val="single" w:sz="4" w:color="000000"/>
                          <w:left w:val="single" w:sz="4" w:color="000000"/>
                          <w:bottom w:val="single" w:sz="4" w:color="000000"/>
                          <w:right w:val="single" w:sz="4" w:color="000000"/>
                          <w:insideH w:val="single" w:sz="4" w:color="000000"/>
                          <w:insideV w:val="single" w:sz="4" w:color="000000"/>
                        </w:tblBorders>
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

                return builder
                    .AddAnchoredImageParagraph(left, 108, 90, body,
                        paragraphProperties: ZeroSpacing, runProperties: Times12)
                    .AddAnchoredImageParagraph(right, 108, 90, body, alignX: "right",
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

                Line("नमस्ते", devanagari);          // namaste: a conjunct in the middle
                Line("हिन्दी", devanagari);           // hindi: a vowel drawn before the consonant
                Line("क्षत्रिय", devanagari);          // kshatriya: a three-consonant conjunct
                Line("कर्म", devanagari);            // karma: a repha, drawn at the end of the cluster
                Line("मुंबई 400", devanagari);        // with digits, which are drawn as they are

                Line("தமிழ்", "Tamil Sangam MN");   // tamil
                Line("বাংলা", "Bangla Sangam MN");   // bangla

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

                Line("नमस्ते", "Devanagari MT");       // a conjunct made by a state machine
                Line("हिन्दी", "Devanagari MT");        // a vowel drawn before its consonant
                Line("સંસ્કૃત", "Gujarati MT");         // Gujarati, the same machinery
                Line("ਪੰਜਾਬੀ", "Gurmukhi MT");         // and Gurmukhi

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

                Line("සිංහල", "Sinhala Sangam MN");        // a vowel above and one after
                Line("ශ්‍රී ලංකා", "Sinhala Sangam MN");     // with the joiner that asks for a conjunct
                Line("ක්‍ෂ", "Sinhala Sangam MN");           // two letters written as one shape
                Line("පොත", "Sinhala Sangam MN");          // a vowel written on both sides at once
                Line("කෙටි", "Sinhala Sangam MN");          // one written to the left of its letter

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

                Line("สวัสดี", "Ayuthaya");          // sawatdi: a vowel above and one after
                Line("ภาษาไทย", "Ayuthaya");        // phasa thai: a vowel written before its consonant
                Line("ກະລຸນາ", "Lao Sangam MN");     // karuna, in Lao
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
                            builder.AddEndnote(DocxBuilder.EndnoteBody($"Note {++note}, of the first section.", Times10))));

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
                            builder.AddEndnote(DocxBuilder.EndnoteBody($"Note {++note}, of the second section.", Times10))));

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
                    "<w:spacing w:before=\"240\"/><w:jc w:val=\"right\"/>", Times(24, italic: true))
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
