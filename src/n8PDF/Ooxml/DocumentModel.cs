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

/// <summary>A tab character from <c>w:tab</c>.</summary>
public sealed class TabInline : InlineElement;

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

/// <summary>A run: a span of content sharing one set of character properties.</summary>
public sealed class Run
{
    public RunProperties Properties { get; set; } = new();

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

    public string GetText() => string.Concat(Runs.Select(r => r.GetText()));

    public override string ToString() => GetText();
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

/// <summary>Table-level properties from <c>w:tblPr</c>.</summary>
public sealed class TableProperties
{
    /// <summary>Preferred table width in twips, when declared as <c>dxa</c>.</summary>
    public int? WidthTwips { get; set; }

    /// <summary>Preferred width as a fraction of the available width, when declared as <c>pct</c>.</summary>
    public double? WidthFraction { get; set; }

    /// <summary>Indent from the left text margin, in twips.</summary>
    public int IndentTwips { get; set; }

    public BorderSet Borders { get; } = new();

    /// <summary>
    /// Default cell padding in twips, zero on every side when nothing declares otherwise.
    /// </summary>
    /// <remarks>
    /// The familiar 108 twips (0.075 inch) of left and right padding comes from Word's built-in
    /// <c>TableNormal</c> style, not from the format itself — every table Word saves inherits it
    /// from there. A table in a document with no table styles gets none, which is what Word does
    /// and what the table-indent-probe fixture measures. Defaulting to 108 here put cell text
    /// 5.4pt right of Word's.
    /// </remarks>
    public int CellMarginLeftTwips { get; set; }

    public int CellMarginRightTwips { get; set; }

    public int CellMarginTopTwips { get; set; }

    public int CellMarginBottomTwips { get; set; }

    /// <summary>
    /// True when the table declares <c>w:tblLayout w:type="fixed"</c>.
    /// </summary>
    /// <remarks>
    /// Word's default is to autofit columns to their contents, which resizes the grid and can
    /// change where lines wrap. Only fixed layout is implemented; a table relying on autofit will
    /// use its declared grid widths instead, which is the closest reasonable approximation.
    /// </remarks>
    public bool FixedLayout { get; set; }

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
    public bool CantSplit { get; set; }

    /// <summary>The row repeats at the top of each page the table continues onto.</summary>
    public bool IsHeader { get; set; }
}

public sealed class TableCell
{
    /// <summary>Preferred cell width in twips, when the cell declares one.</summary>
    public int? WidthTwips { get; set; }

    /// <summary>Number of grid columns this cell spans.</summary>
    public int GridSpan { get; set; } = 1;

    public BorderSet Borders { get; } = new();

    /// <summary>Background fill as RRGGBB, or null for none.</summary>
    public string? ShadingFill { get; set; }

    public VerticalCellAlignment VerticalAlignment { get; set; } = VerticalCellAlignment.Top;

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

/// <summary>The parsed main document part.</summary>
public sealed class WordDocument
{
    public List<BlockElement> Body { get; } = [];

    /// <summary>
    /// The section properties for the final section, taken from the <c>w:sectPr</c> at the end of
    /// the body. A single-section document has only this one.
    /// </summary>
    public SectionProperties Section { get; set; } = new();

    public IEnumerable<Paragraph> Paragraphs => Body.OfType<Paragraph>();
}
