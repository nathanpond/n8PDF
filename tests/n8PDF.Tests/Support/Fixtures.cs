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
