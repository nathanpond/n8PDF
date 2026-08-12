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
        string? underline = null) =>
        DocxBuilder.RunProperties(
            font: TimesNewRoman, halfPoints: halfPoints, bold: bold, italic: italic,
            strike: strike, color: color, underline: underline);

    private static readonly string Times12 = Times();
    private static readonly string Times24 = Times(48);

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

    /// <summary>Builds a fixture's bytes by name.</summary>
    public static byte[] Build(string name) => All[name]().Build();

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
