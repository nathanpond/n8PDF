namespace n8PDF.Ooxml;

/// <summary>Anything that can appear at block level in the document body.</summary>
public abstract class BlockElement;

/// <summary>Anything that can appear inside a run.</summary>
public abstract class InlineElement;

/// <summary>A literal text span from a <c>w:t</c> element.</summary>
public sealed class TextInline(string text) : InlineElement
{
    public string Text { get; } = text;

    public override string ToString() => Text;
}

/// <summary>
/// A field, such as a page number.
/// </summary>
/// <remarks>
/// A field carries both the instruction that produces its value and the value Word last computed,
/// which it stores so that readers that cannot evaluate the instruction still show something. The
/// cached result is used for anything not evaluated here.
/// </remarks>
public sealed class FieldInline(string instruction, string cachedText) : InlineElement
{
    /// <summary>The instruction text, for example " PAGE " or " NUMPAGES ".</summary>
    public string Instruction { get; } = instruction;

    /// <summary>What Word last rendered for this field.</summary>
    public string CachedText { get; } = cachedText;

    /// <summary>The instruction's leading keyword, upper-cased.</summary>
    public string Keyword
    {
        get
        {
            var trimmed = Instruction.TrimStart();
            var end = trimmed.IndexOf(' ');
            return (end < 0 ? trimmed : trimmed[..end]).ToUpperInvariant();
        }
    }
}

/// <summary>A tab character from <c>w:tab</c>.</summary>
public sealed class TabInline : InlineElement;

/// <summary>
/// The start of a bookmark, which an internal hyperlink can point at.
/// </summary>
/// <remarks>
/// Zero-width: it marks a place rather than drawing anything.
/// </remarks>
public sealed class BookmarkInline(string name, int id = 0) : InlineElement
{
    public string Name { get; } = name;

    /// <summary>
    /// The number the document gives the bookmark, which is how its end is found: the end marker
    /// carries the same number and no name.
    /// </summary>
    public int Id { get; } = id;
}

/// <summary>
/// The end of a bookmark, which says how far the text it covers reaches. REF shows that text.
/// </summary>
public sealed class BookmarkEndInline(int id) : InlineElement
{
    public int Id { get; } = id;
}

/// <summary>
/// The two kinds of note, which differ in where the note's text goes: a footnote to the foot of
/// the page its reference lands on, an endnote to the end of the document.
/// </summary>
public enum NoteKind
{
    Footnote,
    Endnote
}

/// <summary>
/// A reference to a note, which draws as that note's number where it appears and sends the note's
/// text to wherever notes of its kind collect.
/// </summary>
public sealed class NoteReferenceInline(int id, NoteKind kind) : InlineElement
{
    /// <summary>The id of the note in its part, not its printed number.</summary>
    public int Id { get; } = id;

    public NoteKind Kind { get; } = kind;
}

/// <summary>
/// A note's own number, from <c>w:footnoteRef</c> or <c>w:endnoteRef</c>, which opens the note's
/// text. It carries no id: it means whichever note it appears inside.
/// </summary>
public sealed class NoteMarkInline(NoteKind kind) : InlineElement
{
    public NoteKind Kind { get; } = kind;
}

/// <summary>Where a page's footnotes are set, from <c>w:pos</c>.</summary>
public enum NotePosition
{
    /// <summary>At the foot of the page, above the bottom margin. What a document means by default.</summary>
    PageBottom,

    /// <summary>
    /// Directly under the last line of text on the page, which is the same place on a page whose
    /// text reaches the bottom margin and a long way above it on a page whose text stops early.
    /// </summary>
    BeneathText
}

/// <summary>Where a document gathers its endnotes, from <c>w:pos</c>.</summary>
public enum EndnotePosition
{
    /// <summary>All of them after the body, which is what a document means by default.</summary>
    DocumentEnd,

    /// <summary>Each section's own at the end of it, before the next section begins.</summary>
    SectionEnd
}

/// <summary>How often a document begins its note numbering again, from <c>w:numRestart</c>.</summary>
public enum NoteNumberRestart
{
    /// <summary>Numbered straight through the document, which is what a document says by default.</summary>
    Continuous,

    /// <summary>Numbered again from the beginning in each section.</summary>
    EachSection,

    /// <summary>Numbered again from the beginning on each page. Footnotes only.</summary>
    EachPage
}

/// <summary>
/// The separator drawn above a page's footnotes, from <c>w:separator</c>.
/// </summary>
/// <remarks>
/// Word stores the separator as a footnote of its own whose body is a paragraph holding this
/// element, which is what gives the line its height and the space around it.
/// </remarks>
/// <param name="continuation">
/// Whether this is the rule above a note carried over from the page before, from
/// <c>w:continuationSeparator</c>. Word draws that one right across the measure rather than the
/// two inches it draws above a note that begins where it stands, which is how a reader can tell
/// at a glance that what follows is the rest of something.
/// </param>
public sealed class SeparatorInline(bool continuation = false) : InlineElement
{
    public bool Continuation { get; } = continuation;
}

/// <summary>Where a hyperlink leads.</summary>
/// <param name="RelationshipId">
/// The relationship naming an external target. Resolved to a URL when the package is read, since
/// the run itself only carries the id.
/// </param>
/// <param name="Anchor">A bookmark within the document, for an internal link.</param>
public sealed record HyperlinkTarget(string? RelationshipId, string? Anchor);

/// <summary>The kind of break a <c>w:br</c> represents.</summary>
public enum BreakKind
{
    Line,
    Page,
    Column
}

public sealed class BreakInline(BreakKind kind) : InlineElement
{
    public BreakKind Kind { get; } = kind;
}

/// <summary>
/// A drawing or picture. Only the extent is captured so far, which is enough for layout to
/// reserve the right space once image rendering lands.
/// </summary>
public sealed class DrawingInline(long widthEmu, long heightEmu, string? relationshipId) : InlineElement
{
    public long WidthEmu { get; } = widthEmu;

    public long HeightEmu { get; } = heightEmu;

    public string? RelationshipId { get; } = relationshipId;

    public double WidthPoints => Units.EmuToPoints(WidthEmu);

    public double HeightPoints => Units.EmuToPoints(HeightEmu);
}

/// <summary>How text behaves around a floating drawing.</summary>
public enum TextWrapMode
{
    /// <summary>Text ignores the drawing entirely; the two overlap.</summary>
    None,

    /// <summary>Text flows beside the drawing, avoiding its rectangle.</summary>
    Square,

    /// <summary>Text is pushed above and below; nothing sits beside it.</summary>
    TopAndBottom
}

/// <summary>What a floating drawing's horizontal position is measured from.</summary>
public enum HorizontalAnchor
{
    Column,
    Margin,
    Page,
    Character,
    LeftMargin,
    RightMargin
}

/// <summary>What a floating drawing's vertical position is measured from.</summary>
public enum VerticalAnchor
{
    Paragraph,
    Line,
    Margin,
    Page,
    TopMargin,
    BottomMargin
}

/// <summary>
/// A drawing positioned independently of the text flow, from a <c>wp:anchor</c>.
/// </summary>
/// <remarks>
/// It still lives inside a run, because that run is what it is anchored to, but it does not
/// occupy space on the line the way an inline drawing does. Its rectangle is computed from the
/// anchor point and the text then flows around it.
/// </remarks>
public sealed class AnchoredDrawing : InlineElement
{
    public required long WidthEmu { get; init; }

    public required long HeightEmu { get; init; }

    public string? RelationshipId { get; init; }

    public TextWrapMode Wrap { get; init; } = TextWrapMode.Square;

    /// <summary>Drawn behind the text rather than over it. Only meaningful without wrapping.</summary>
    public bool BehindText { get; init; }

    public HorizontalAnchor HorizontalFrom { get; init; } = HorizontalAnchor.Column;

    /// <summary>Horizontal offset in EMUs, when the anchor gives one rather than an alignment.</summary>
    public long? HorizontalOffsetEmu { get; init; }

    /// <summary>"left", "center", "right", "inside" or "outside".</summary>
    public string? HorizontalAlign { get; init; }

    public VerticalAnchor VerticalFrom { get; init; } = VerticalAnchor.Paragraph;

    public long? VerticalOffsetEmu { get; init; }

    /// <summary>"top", "center", "bottom", "inside" or "outside".</summary>
    public string? VerticalAlign { get; init; }

    /// <summary>Clearance kept between the drawing and the text around it, in EMUs.</summary>
    public long DistanceLeftEmu { get; init; }

    public long DistanceRightEmu { get; init; }

    public long DistanceTopEmu { get; init; }

    public long DistanceBottomEmu { get; init; }

    public double WidthPoints => Units.EmuToPoints(WidthEmu);

    public double HeightPoints => Units.EmuToPoints(HeightEmu);
}

/// <summary>A run: a span of content sharing one set of character properties.</summary>
public sealed class Run
{
    public RunProperties Properties { get; set; } = new();

    /// <summary>
    /// The hyperlink this run belongs to, if any. Held on the run rather than as a wrapper
    /// because a link's extent is exactly the runs inside it, and layout deals in runs.
    /// </summary>
    public HyperlinkTarget? Hyperlink { get; set; }

    public List<InlineElement> Content { get; } = [];

    /// <summary>The run's text with tabs and breaks flattened out, for diagnostics.</summary>
    public string GetText() =>
        string.Concat(Content.OfType<TextInline>().Select(t => t.Text));
}

/// <summary>A paragraph and its runs.</summary>
public sealed class Paragraph : BlockElement
{
    public ParagraphProperties Properties { get; set; } = new();

    public List<Run> Runs { get; } = [];

    /// <summary>
    /// Section properties attached to this paragraph. Word stores a section break by hanging the
    /// outgoing section's properties off the last paragraph before the break.
    /// </summary>
    public SectionProperties? SectionBreak { get; set; }

    /// <summary>
    /// The field this paragraph opens and does not close, if it opens one. A table of contents is
    /// written that way: the field begins in the paragraph holding its first entry and ends in the
    /// one holding its last.
    /// </summary>
    public FieldInline? OpensField { get; set; }

    /// <summary>
    /// True where this paragraph is part of what a field begun in an earlier paragraph produced,
    /// rather than content the document wrote itself. A field that can be worked out again
    /// replaces all of them.
    /// </summary>
    public bool InsideField { get; set; }

    public string GetText() => string.Concat(Runs.Select(r => r.GetText()));

    public override string ToString() => GetText();
}

/// <summary>
/// How far into a field the reader is, carried from one paragraph to the next.
/// </summary>
/// <remarks>
/// Fields are a sequence of markers rather than a container, and nothing says a field has to end
/// in the paragraph it began in. Reading each paragraph on its own would lose the ones that do
/// not — the first entry of a table of contents lives in the same paragraph as the instruction
/// that produced it.
/// </remarks>
public sealed class FieldScope
{
    private int _depth;

    public bool IsOpen => _depth > 0;

    public void Open() => _depth++;

    public void Close()
    {
        if (_depth > 0) _depth--;
    }
}

/// <summary>One edge of a border, as declared by <c>w:top</c>, <c>w:insideV</c> and friends.</summary>
/// <param name="Style">The <c>w:val</c> line style; "none" and "nil" mean no line.</param>
/// <param name="SizeEighthPoints">Line width in eighths of a point.</param>
/// <param name="ColorHex">RRGGBB, or null for automatic (rendered black).</param>
public sealed record BorderEdge(string Style, double SizeEighthPoints, string? ColorHex)
{
    public bool IsVisible => Style is not ("none" or "nil" or "") && SizeEighthPoints > 0;

    /// <summary>Line width in points, floored so that a hairline still renders.</summary>
    public double WidthPoints => Math.Max(0.25, Units.EighthPointsToPoints(SizeEighthPoints));

    public (double Red, double Green, double Blue) GetColor()
    {
        if (ColorHex is null || ColorHex.Length != 6) return (0, 0, 0);

        try
        {
            return (Convert.ToInt32(ColorHex[..2], 16) / 255.0,
                Convert.ToInt32(ColorHex.Substring(2, 2), 16) / 255.0,
                Convert.ToInt32(ColorHex.Substring(4, 2), 16) / 255.0);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return (0, 0, 0);
        }
    }
}

/// <summary>The six border edges a table or cell can declare.</summary>
public sealed class BorderSet
{
    public BorderEdge? Top { get; set; }

    public BorderEdge? Left { get; set; }

    public BorderEdge? Bottom { get; set; }

    public BorderEdge? Right { get; set; }

    /// <summary>Border between rows. Only meaningful on a table.</summary>
    public BorderEdge? InsideHorizontal { get; set; }

    /// <summary>Border between columns. Only meaningful on a table.</summary>
    public BorderEdge? InsideVertical { get; set; }
}

/// <summary>How a row's declared height should be interpreted.</summary>
public enum RowHeightRule
{
    /// <summary>Height is determined by the content.</summary>
    Auto,

    /// <summary>Content may grow the row beyond the declared height.</summary>
    AtLeast,

    /// <summary>The row is exactly this tall.</summary>
    Exact
}

public enum VerticalCellAlignment
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Which of a table style's conditional formats are in force, from <c>w:tblLook</c>.
/// </summary>
/// <remarks>
/// A style can describe a first row, a last row, two edge columns, four corner cells and two
/// kinds of banding; this says which of them the table wants. The banding is the odd one out,
/// declared as <c>noHBand</c> and <c>noVBand</c> and so on by default. The older spelling packs
/// the same six answers into the hexadecimal <c>w:val</c>, and Word still writes both.
/// </remarks>
public sealed class TableLook
{
    public bool FirstRow { get; set; }

    public bool LastRow { get; set; }

    public bool FirstColumn { get; set; }

    public bool LastColumn { get; set; }

    /// <summary>Rows are banded, which is what <c>noHBand</c> turns off.</summary>
    public bool HorizontalBanding { get; set; } = true;

    /// <summary>Columns are banded, which is what <c>noVBand</c> turns off.</summary>
    public bool VerticalBanding { get; set; } = true;
}

/// <summary>Table-level properties from <c>w:tblPr</c>.</summary>
public sealed class TableProperties
{
    /// <summary>
    /// Half a point at the sides and none above or below: what a table gets when nothing —
    /// neither the table, nor a style, nor Word's own <c>TableNormal</c> — says otherwise.
    /// </summary>
    /// <remarks>
    /// The familiar 108 twips (0.075 inch) of left and right padding comes from the built-in
    /// <c>TableNormal</c> style, not from the format itself — every table Word saves inherits it
    /// from there. A table in a document with no table styles gets none of it, which is what
    /// table-indent-probe measures; defaulting to 108 here put cell text 5.4pt right of Word's.
    ///
    /// It does not get nothing either. table-inset-weights-probe holds the same table twice, once
    /// declaring a margin of zero and once declaring no margin at all, and Word sets the second
    /// half a point further in — so an absent element is not the same as a zero one. Ten twips is
    /// as close as the export can pin it: Word rounds every position to 1/300 inch, which puts the
    /// true value somewhere between seven and twelve.
    /// </remarks>
    public const int DefaultSideCellMarginTwips = 10;

    /// <summary>The style this table wears, from <c>w:tblStyle</c>.</summary>
    public string? StyleId { get; set; }

    /// <summary>Which of that style's conditional formats are in force.</summary>
    public TableLook Look { get; set; } = new();

    /// <summary>How many rows make up one horizontal band.</summary>
    public int RowBandSize { get; set; } = 1;

    /// <summary>How many columns make up one vertical band.</summary>
    public int ColumnBandSize { get; set; } = 1;

    /// <summary>Preferred table width in twips, when declared as <c>dxa</c>.</summary>
    public int? WidthTwips { get; set; }

    /// <summary>Preferred width as a fraction of the available width, when declared as <c>pct</c>.</summary>
    public double? WidthFraction { get; set; }

    /// <summary>
    /// Indent from the left text margin, in twips, or null when the table declares none.
    /// </summary>
    /// <remarks>
    /// Absent and zero are not the same thing. Measured against Word: when a table declares an
    /// indent, the cell content edge sits exactly that far from the margin — the cell margin and
    /// border are absorbed into it rather than added on top. When it declares none, the content
    /// edge is the table edge plus the margin and border in the usual way. Word writes this
    /// element on every table it saves, so real documents always take the first path.
    /// </remarks>
    public int? IndentTwips { get; set; }

    public BorderSet Borders { get; } = new();

    /// <summary>
    /// Default cell padding in twips, or null where nothing has declared any. Absent and zero
    /// are different answers: see <see cref="DefaultSideCellMarginTwips"/>.
    /// </summary>
    public int? CellMarginLeftTwips { get; set; }

    public int? CellMarginRightTwips { get; set; }

    public int? CellMarginTopTwips { get; set; }

    public int? CellMarginBottomTwips { get; set; }

    /// <summary>The left cell padding in force, in twips.</summary>
    public int CellMarginLeft => CellMarginLeftTwips ?? DefaultSideCellMarginTwips;

    public int CellMarginRight => CellMarginRightTwips ?? DefaultSideCellMarginTwips;

    public int CellMarginTop => CellMarginTopTwips ?? 0;

    public int CellMarginBottom => CellMarginBottomTwips ?? 0;

    /// <summary>
    /// True when the table declares <c>w:tblLayout w:type="fixed"</c>, null when it says nothing.
    /// </summary>
    /// <remarks>
    /// Word's default is to autofit columns to their contents, which resizes the grid and can
    /// change where lines wrap. Only fixed layout is implemented; a table relying on autofit will
    /// use its declared grid widths instead, which is the closest reasonable approximation.
    /// </remarks>
    public bool? FixedLayout { get; set; }

    public Justification? Justification { get; set; }
}

/// <summary>A table.</summary>
public sealed class Table : BlockElement
{
    public TableProperties Properties { get; set; } = new();

    /// <summary>Column widths in twips from <c>w:tblGrid</c>, one entry per grid column.</summary>
    public List<int> Grid { get; } = [];

    public List<TableRow> Rows { get; } = [];
}

public sealed class TableRow
{
    public List<TableCell> Cells { get; } = [];

    /// <summary>Declared row height in twips, if any.</summary>
    public int? HeightTwips { get; set; }

    public RowHeightRule HeightRule { get; set; } = RowHeightRule.Auto;

    /// <summary>The row may not be split across a page boundary.</summary>
    public bool? CantSplit { get; set; }

    /// <summary>The row repeats at the top of each page the table continues onto.</summary>
    public bool? IsHeader { get; set; }
}

public sealed class TableCell
{
    /// <summary>Preferred cell width in twips, when the cell declares one.</summary>
    public int? WidthTwips { get; set; }

    /// <summary>Number of grid columns this cell spans.</summary>
    public int GridSpan { get; set; } = 1;

    public BorderSet Borders { get; } = new();

    /// <summary>
    /// Background fill as RRGGBB, "auto" for a declared absence of one, or null where nothing was
    /// declared at all.
    /// </summary>
    /// <remarks>
    /// The three are not two: a cell whose style shades it and which declares <c>fill="auto"</c>
    /// of its own comes out unshaded, and telling that from a cell that said nothing is the whole
    /// difference. <see cref="ShadingColorHex"/> is what to draw with.
    /// </remarks>
    public string? ShadingFill { get; set; }

    /// <summary>The colour to fill the cell with, or null where it takes none.</summary>
    public string? ShadingColorHex => ShadingFill is null or "auto" ? null : ShadingFill;

    public VerticalCellAlignment? VerticalAlignment { get; set; }

    /// <summary>
    /// Vertical merge state: "restart" begins a merged span, "continue" is absorbed by the cell
    /// above, and null means the cell is not merged.
    /// </summary>
    public string? VerticalMerge { get; set; }

    /// <summary>Per-cell margin overrides, in twips; null falls back to the table's.</summary>
    public int? MarginLeftTwips { get; set; }

    public int? MarginRightTwips { get; set; }

    public int? MarginTopTwips { get; set; }

    public int? MarginBottomTwips { get; set; }

    public List<BlockElement> Content { get; } = [];
}

/// <summary>
/// One note: its id, and the blocks that make up its text.
/// </summary>
/// <param name="id">
/// The id the body's references use. Word reserves the ids below 1 for the separators, which are
/// not notes in their own right.
/// </param>
/// <param name="type">
/// "normal" for a real note, or "separator" and "continuationSeparator" for the rules Word draws
/// above them.
/// </param>
public sealed class Note(int id, string type)
{
    public int Id { get; } = id;

    public string Type { get; } = type;

    public bool IsSeparator => Type is "separator" or "continuationSeparator";

    public List<BlockElement> Body { get; } = [];
}

/// <summary>The contents of one header or footer part.</summary>
public sealed class HeaderFooter
{
    public List<BlockElement> Body { get; } = [];
}

/// <summary>The parsed main document part.</summary>
public sealed class WordDocument
{
    /// <summary>External hyperlink targets by relationship id.</summary>
    public Dictionary<string, string> Hyperlinks { get; } = [];

    /// <summary>The footnotes part, by note id.</summary>
    public Dictionary<int, Note> Footnotes { get; } = [];

    /// <summary>The endnotes part, by note id.</summary>
    public Dictionary<int, Note> Endnotes { get; } = [];

    /// <summary>
    /// How note numbers are printed.
    /// </summary>
    /// <remarks>
    /// The defaults are Word's own, measured from its exports: footnotes count in arabic numerals
    /// and endnotes in lower-case roman ones. A document can say otherwise in its settings or on
    /// its section.
    /// </remarks>
    /// <summary>
    /// What the document uses as a decimal separator, from <c>w:decimalSymbol</c> in its settings.
    /// A decimal tab stop lines this character up.
    /// </summary>
    public string DecimalSymbol { get; set; } = ".";

    public NumberFormat FootnoteNumberFormat { get; set; } = NumberFormat.Decimal;

    /// <summary>
    /// Where the endnotes are gathered. Unlike everything else about how a note is set, Word reads
    /// this from the settings part rather than from the section: a document asking for it in its
    /// sections alone is gathered at the end regardless, which was measured rather than assumed.
    /// </summary>
    public EndnotePosition EndnotePosition { get; set; } = EndnotePosition.DocumentEnd;

    public NumberFormat EndnoteNumberFormat { get; set; } = NumberFormat.LowerRoman;

    /// <summary>Header and footer parts by relationship id.</summary>
    public Dictionary<string, HeaderFooter> HeadersAndFooters { get; } = [];

    /// <summary>
    /// Odd and even pages take different headers and footers. Declared in settings.xml rather
    /// than on the section, because it applies to the whole document.
    /// </summary>
    public bool EvenAndOddHeaders { get; set; }

    public List<BlockElement> Body { get; } = [];

    /// <summary>
    /// Image parts by relationship id, as stored in the package.
    /// </summary>
    /// <remarks>
    /// Loaded when the document is read, because the package is closed before layout runs and a
    /// drawing only carries the relationship id, not the picture itself.
    /// </remarks>
    public Dictionary<string, byte[]> Images { get; } = [];

    /// <summary>
    /// The section properties for the final section, taken from the <c>w:sectPr</c> at the end of
    /// the body. A single-section document has only this one.
    /// </summary>
    public SectionProperties Section { get; set; } = new();

    public IEnumerable<Paragraph> Paragraphs => Body.OfType<Paragraph>();
}
