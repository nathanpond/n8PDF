namespace n8PDF.Ooxml;

public enum Justification
{
    Left,
    Center,
    Right,
    Both,
    Distribute
}

/// <summary>How the <c>w:spacing/@w:line</c> value should be interpreted.</summary>
public enum LineSpacingRule
{
    /// <summary>A multiple of single spacing, in 240ths (240 = single, 360 = 1.5 lines).</summary>
    Auto,

    /// <summary>An exact height in twips; content taller than the line is clipped.</summary>
    Exact,

    /// <summary>A minimum height in twips; the line grows for taller content.</summary>
    AtLeast
}

public enum UnderlineStyle
{
    None,
    Single,
    Double,
    Thick,
    Dotted,
    Dashed,
    Wave,
    Words
}

public enum VerticalTextAlignment
{
    Baseline,
    Superscript,
    Subscript
}

public enum TabAlignment
{
    Left,
    Center,
    Right,
    Decimal,
    Bar,
    Clear
}

public enum TabLeader
{
    None,
    Dot,
    Hyphen,
    Underscore,
    MiddleDot
}

/// <summary>A tab stop declared on a paragraph.</summary>
/// <param name="PositionTwips">Distance from the left text margin.</param>
public sealed record TabStop(double PositionTwips, TabAlignment Alignment, TabLeader Leader);

/// <summary>
/// Run properties exactly as written in one <c>w:rPr</c>, with no inheritance applied. Every
/// member is nullable so that "not specified here" stays distinguishable from "specified as
/// off", which is the distinction the whole style cascade turns on.
/// </summary>
public sealed class RunProperties
{
    /// <summary>Font for Latin text (<c>w:rFonts/@w:ascii</c>).</summary>
    public string? AsciiFont { get; set; }

    /// <summary>Font for high-ANSI text; in practice almost always the same as ascii.</summary>
    public string? HighAnsiFont { get; set; }

    public string? EastAsiaFont { get; set; }

    public string? ComplexScriptFont { get; set; }

    /// <summary>
    /// Theme font slot for Latin text (<c>minorHAnsi</c>, <c>majorHAnsi</c>, …). When present it
    /// takes priority over the literal font name and must be looked up in the theme part.
    /// </summary>
    public string? AsciiTheme { get; set; }

    public string? HighAnsiTheme { get; set; }

    /// <summary>Font size in half-points (<c>w:sz</c>).</summary>
    public int? SizeHalfPoints { get; set; }

    /// <summary>Bold. A toggle property.</summary>
    public bool? Bold { get; set; }

    /// <summary>Italic. A toggle property.</summary>
    public bool? Italic { get; set; }

    /// <summary>All capitals. A toggle property.</summary>
    public bool? Caps { get; set; }

    /// <summary>Small capitals. A toggle property.</summary>
    public bool? SmallCaps { get; set; }

    /// <summary>Strikethrough. A toggle property.</summary>
    public bool? Strike { get; set; }

    /// <summary>Hidden text. A toggle property; hidden runs are not rendered.</summary>
    public bool? Vanish { get; set; }

    public UnderlineStyle? Underline { get; set; }

    /// <summary>Text colour as RRGGBB, or null when unspecified or "auto".</summary>
    public string? Color { get; set; }

    /// <summary>Highlight colour name, or null.</summary>
    public string? Highlight { get; set; }

    public VerticalTextAlignment? VerticalAlignment { get; set; }

    /// <summary>Extra character spacing in twips (<c>w:spacing</c> inside rPr).</summary>
    public int? CharacterSpacingTwips { get; set; }

    /// <summary>Character scaling as a percentage; 100 is unscaled.</summary>
    public int? ScalePercent { get; set; }

    /// <summary>
    /// The type size, in half-points, at or above which the font's own kerning applies. Zero or
    /// absent means the document does not want kerning at all, which is Word's default.
    /// </summary>
    public int? KerningMinimumHalfPoints { get; set; }

    /// <summary>Referenced character style id (<c>w:rStyle</c>).</summary>
    public string? StyleId { get; set; }

    public RunProperties Clone() => (RunProperties)MemberwiseClone();
}

/// <summary>
/// Paragraph properties exactly as written in one <c>w:pPr</c>, with no inheritance applied.
/// </summary>
public sealed class ParagraphProperties
{
    /// <summary>Referenced paragraph style id (<c>w:pStyle</c>).</summary>
    public string? StyleId { get; set; }

    public Justification? Justification { get; set; }

    /// <summary>
    /// Whether this paragraph runs right to left, from <c>w:bidi</c>. It says which way the
    /// paragraph goes as a whole, not which way its characters do — that is theirs to say.
    /// </summary>
    public bool? RightToLeft { get; set; }

    public int? IndentLeftTwips { get; set; }

    public int? IndentRightTwips { get; set; }

    /// <summary>First-line indent in twips. Mutually exclusive with <see cref="IndentHangingTwips"/>.</summary>
    public int? IndentFirstLineTwips { get; set; }

    /// <summary>Hanging indent in twips: the first line starts this far left of the others.</summary>
    public int? IndentHangingTwips { get; set; }

    public int? SpacingBeforeTwips { get; set; }

    public int? SpacingAfterTwips { get; set; }

    /// <summary>
    /// Line spacing value. Its meaning depends on <see cref="LineRule"/>: 240ths of a line for
    /// <see cref="LineSpacingRule.Auto"/>, twips otherwise.
    /// </summary>
    public int? Line { get; set; }

    public LineSpacingRule? LineRule { get; set; }

    /// <summary>Suppresses space before and after between paragraphs of the same style.</summary>
    public bool? ContextualSpacing { get; set; }

    public bool? KeepNext { get; set; }

    public bool? KeepLines { get; set; }

    public bool? PageBreakBefore { get; set; }

    /// <summary>Suppress widow and orphan control. Word enables the control by default.</summary>
    public bool? WidowControl { get; set; }

    /// <summary>
    /// The heading level a paragraph stands at, from <c>w:outlineLvl</c>: zero for the topmost,
    /// eight for the lowest, and absent for body text. It is what a table of contents gathers by,
    /// and it usually comes from the heading style rather than from the paragraph.
    /// </summary>
    public int? OutlineLevel { get; set; }

    /// <summary>Numbering definition id referenced by <c>w:numPr/w:numId</c>.</summary>
    public int? NumberingId { get; set; }

    /// <summary>Numbering level referenced by <c>w:numPr/w:ilvl</c>.</summary>
    public int? NumberingLevel { get; set; }

    public List<TabStop> TabStops { get; } = [];

    /// <summary>
    /// Run properties attached to the paragraph mark. These style the pilcrow itself and act as
    /// the defaults an empty paragraph is measured with.
    /// </summary>
    public RunProperties? MarkRunProperties { get; set; }
}

/// <summary>Page geometry and section-level settings from a <c>w:sectPr</c>.</summary>
/// <summary>Where the content of a new section starts.</summary>
public enum SectionBreakType
{
    /// <summary>On the next page. Word's default, and what a section break usually means.</summary>
    NextPage,

    /// <summary>Straight after the previous section, on the same page.</summary>
    Continuous,

    /// <summary>On the next even-numbered page, leaving a blank one behind if need be.</summary>
    EvenPage,

    /// <summary>On the next odd-numbered page.</summary>
    OddPage,

    /// <summary>In the next column, which without column support behaves as continuous.</summary>
    NextColumn
}

/// <summary>Where a section's text sits between the top and bottom margins.</summary>
public enum VerticalPageAlignment
{
    Top,
    Center,
    Bottom,

    /// <summary>Spread to fill the page, with the space going between the paragraphs.</summary>
    Both
}

public sealed class SectionProperties
{
    /// <summary>Where this section's text sits on the page. Top unless it says otherwise.</summary>
    public VerticalPageAlignment VerticalAlignment { get; set; } = VerticalPageAlignment.Top;

    /// <summary>Where this section's content begins relative to the one before it.</summary>
    public SectionBreakType BreakType { get; set; } = SectionBreakType.NextPage;

    /// <summary>Page width in twips. Defaults to US Letter.</summary>
    public int PageWidthTwips { get; set; } = 12240;

    /// <summary>Page height in twips. Defaults to US Letter.</summary>
    public int PageHeightTwips { get; set; } = 15840;

    public bool Landscape { get; set; }

    public int MarginTopTwips { get; set; } = 1440;

    public int MarginRightTwips { get; set; } = 1440;

    public int MarginBottomTwips { get; set; } = 1440;

    public int MarginLeftTwips { get; set; } = 1440;

    public int HeaderDistanceTwips { get; set; } = 720;

    /// <summary>
    /// Note numbering formats stated on this section, or null where it says nothing and the
    /// document's settings or Word's defaults decide.
    /// </summary>
    public NumberFormat? FootnoteNumberFormat { get; set; }

    /// <summary>
    /// Whether this section numbers its notes again from the beginning, and how often.
    /// </summary>
    /// <remarks>
    /// Word reads this from the section and nowhere else. A document stating it in its settings
    /// instead — which the format allows, and which is where it reads as a document-wide default —
    /// is numbered straight through by Word regardless, which was measured rather than assumed.
    /// </remarks>
    public NoteNumberRestart? FootnoteNumberRestart { get; set; }

    public NoteNumberRestart? EndnoteNumberRestart { get; set; }

    /// <summary>
    /// Where this section sets the footnotes of a page: at its foot, or under the last line of
    /// text on it. Read from the section, as everything else about how a note is set is.
    /// </summary>
    public NotePosition? FootnotePosition { get; set; }

    /// <summary>
    /// The number this section's first page is printed as, from <c>w:pgNumType/@w:start</c>. Null
    /// where the section says nothing, and then its numbering carries on from the section before.
    /// </summary>
    public int? PageNumberStart { get; set; }

    public NumberFormat? EndnoteNumberFormat { get; set; }

    public int FooterDistanceTwips { get; set; } = 720;

    public int GutterTwips { get; set; }

    /// <summary>Number of text columns.</summary>
    public int ColumnCount { get; set; } = 1;

    /// <summary>Gap between columns in twips, where they are evenly divided.</summary>
    public int ColumnSpaceTwips { get; set; } = 720;

    /// <summary>Whether a rule is drawn down the gap between columns.</summary>
    public bool ColumnSeparator { get; set; }

    /// <summary>
    /// Individually stated column widths, in twips, each with the gap that follows it. Empty when
    /// the columns are evenly divided, which is the usual case.
    /// </summary>
    public List<(int WidthTwips, int SpaceTwips)> ColumnWidths { get; } = [];

    /// <summary>
    /// Where each column starts, as an offset from the content box's left edge, and how wide it
    /// is — both in points.
    /// </summary>
    /// <remarks>
    /// Stated widths are used as they are. Otherwise the space left after the gaps is divided
    /// evenly, which is what Word does and what nearly every document asks for.
    /// </remarks>
    public IReadOnlyList<(double Left, double Width)> GetColumns()
    {
        var total = ContentWidthPoints;

        if (ColumnWidths.Count > 1)
        {
            var stated = new List<(double, double)>(ColumnWidths.Count);
            var x = 0.0;

            foreach (var (width, space) in ColumnWidths)
            {
                stated.Add((x, Units.TwipsToPoints(width)));
                x += Units.TwipsToPoints(width + space);
            }

            return stated;
        }

        if (ColumnCount <= 1) return [(0, total)];

        var gap = Units.TwipsToPoints(ColumnSpaceTwips);
        var each = (total - gap * (ColumnCount - 1)) / ColumnCount;

        // A gap wider than the page would leave columns with no width at all.
        if (each <= 0) return [(0, total)];

        var columns = new List<(double, double)>(ColumnCount);
        for (var i = 0; i < ColumnCount; i++) columns.Add((i * (each + gap), each));

        return columns;
    }

    /// <summary>
    /// Header parts by type — "default", "first" or "even" — as relationship ids.
    /// </summary>
    public Dictionary<string, string> HeaderReferences { get; } = [];

    public Dictionary<string, string> FooterReferences { get; } = [];

    /// <summary>The first page takes its own header and footer rather than the default pair.</summary>
    public bool TitlePage { get; set; }

    /// <summary>Page width in points.</summary>
    public double PageWidthPoints => Units.TwipsToPoints(PageWidthTwips);

    /// <summary>Page height in points.</summary>
    public double PageHeightPoints => Units.TwipsToPoints(PageHeightTwips);

    /// <summary>Width available to body text, between the left and right margins.</summary>
    public double ContentWidthPoints =>
        Units.TwipsToPoints(PageWidthTwips - MarginLeftTwips - MarginRightTwips - GutterTwips);

    /// <summary>Height available to body text, between the top and bottom margins.</summary>
    public double ContentHeightPoints =>
        Units.TwipsToPoints(PageHeightTwips - MarginTopTwips - MarginBottomTwips);
}
