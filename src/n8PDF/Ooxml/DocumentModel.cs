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

/// <summary>
/// A table. Parsed into the model but not yet laid out; it is here so that a document containing
/// one round-trips through parsing rather than losing content silently.
/// </summary>
public sealed class Table : BlockElement
{
    public List<TableRow> Rows { get; } = [];
}

public sealed class TableRow
{
    public List<TableCell> Cells { get; } = [];
}

public sealed class TableCell
{
    /// <summary>Preferred cell width in twips, when the table declares one.</summary>
    public int? WidthTwips { get; set; }

    /// <summary>Number of grid columns this cell spans.</summary>
    public int GridSpan { get; set; } = 1;

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
