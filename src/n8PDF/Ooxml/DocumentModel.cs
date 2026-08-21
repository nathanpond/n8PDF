namespace n8PDF.Ooxml;

/// <summary>Anything that can appear at block level in the document body.</summary>
internal abstract class BlockElement;

/// <summary>Anything that can appear inside a run.</summary>
internal abstract class InlineElement;

/// <summary>A literal text span from a <c>w:t</c> element.</summary>
internal sealed class TextInline(string text) : InlineElement
{
    public string Text { get; } = text;

    public override string ToString() => Text;
}

/// <summary>
/// A character named by its code in a face of its own: <c>w:sym</c>.
/// </summary>
/// <remarks>
/// How Word writes anything from the symbol faces — a tick, an arrow, a bullet from Wingdings.
/// The face belongs to the character rather than to the run, since a run may carry text in one
/// face and end with a character from another, which is what Word writes for a symbol typed at
/// the end of a word.
/// </remarks>
internal sealed class SymbolInline(string text, string? font) : InlineElement
{
    public string Text { get; } = text;

    /// <summary>The face the character is named in, or null where the run's own face is meant.</summary>
    public string? Font { get; } = font;

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
internal sealed class FieldInline(string instruction, string cachedText) : InlineElement
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
internal sealed class TabInline : InlineElement;

/// <summary>
/// The start of a bookmark, which an internal hyperlink can point at.
/// </summary>
/// <remarks>
/// Zero-width: it marks a place rather than drawing anything.
/// </remarks>
internal sealed class BookmarkInline(string name, int id = 0) : InlineElement
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
internal sealed class BookmarkEndInline(int id) : InlineElement
{
    public int Id { get; } = id;
}

/// <summary>
/// The two kinds of note, which differ in where the note's text goes: a footnote to the foot of
/// the page its reference lands on, an endnote to the end of the document.
/// </summary>
internal enum NoteKind
{
    Footnote,
    Endnote
}

/// <summary>
/// A reference to a note, which draws as that note's number where it appears and sends the note's
/// text to wherever notes of its kind collect.
/// </summary>
internal sealed class NoteReferenceInline(int id, NoteKind kind) : InlineElement
{
    /// <summary>The id of the note in its part, not its printed number.</summary>
    public int Id { get; } = id;

    public NoteKind Kind { get; } = kind;
}

/// <summary>
/// A note's own number, from <c>w:footnoteRef</c> or <c>w:endnoteRef</c>, which opens the note's
/// text. It carries no id: it means whichever note it appears inside.
/// </summary>
internal sealed class NoteMarkInline(NoteKind kind) : InlineElement
{
    public NoteKind Kind { get; } = kind;
}

/// <summary>Where a page's footnotes are set, from <c>w:pos</c>.</summary>
internal enum NotePosition
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
internal enum EndnotePosition
{
    /// <summary>All of them after the body, which is what a document means by default.</summary>
    DocumentEnd,

    /// <summary>Each section's own at the end of it, before the next section begins.</summary>
    SectionEnd
}

/// <summary>How often a document begins its note numbering again, from <c>w:numRestart</c>.</summary>
internal enum NoteNumberRestart
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
internal sealed class SeparatorInline(bool continuation = false) : InlineElement
{
    public bool Continuation { get; } = continuation;
}

/// <summary>Where a hyperlink leads.</summary>
/// <param name="RelationshipId">
/// The relationship naming an external target. Resolved to a URL when the package is read, since
/// the run itself only carries the id.
/// </param>
/// <param name="Anchor">A bookmark within the document, for an internal link.</param>
internal sealed record HyperlinkTarget(string? RelationshipId, string? Anchor);

/// <summary>The kind of break a <c>w:br</c> represents.</summary>
internal enum BreakKind
{
    Line,
    Page,
    Column
}

/// <summary>
/// An equation, which is a document of its own inside a run: its own markup, its own face, and a
/// setting that has nothing to do with lines of text.
/// </summary>
internal sealed class MathInline(MathNode node, bool display) : InlineElement
{
    public MathNode Node { get; } = node;

    /// <summary>
    /// True where it stands on a line of its own rather than in the middle of a sentence, which
    /// changes how it is set as well as where it goes.
    /// </summary>
    public bool Display { get; } = display;
}

internal sealed class BreakInline(BreakKind kind) : InlineElement
{
    public BreakKind Kind { get; } = kind;
}

/// <summary>
/// A drawing or picture. Only the extent is captured so far, which is enough for layout to
/// reserve the right space once image rendering lands.
/// </summary>
internal sealed class DrawingInline(long widthEmu, long heightEmu, string? relationshipId) : InlineElement
{
    public long WidthEmu { get; } = widthEmu;

    public long HeightEmu { get; } = heightEmu;

    /// <summary>
    /// The picture this frame draws. Rewritten to a key naming the part as well as the id where
    /// the drawing lives in a part of its own: a header's "rId1" and the body's are different
    /// pictures, and a document holding both would otherwise draw one of them twice.
    /// </summary>
    public string? RelationshipId { get; set; } = relationshipId;

    /// <summary>The shape drawn here, where this frame holds one rather than a picture.</summary>
    public ShapeFrame? Shape { get; init; }

    /// <summary>What was done to the picture's colours, for a watermark of one.</summary>
    public PictureWash? Wash { get; init; }

    /// <summary>
    /// The relationship the diagram's data is reached by, where this frame holds a diagram. What
    /// is drawn comes from a part beside that one, which is why the shapes are filled in later.
    /// </summary>
    public string? DiagramRelationshipId { get; set; }

    /// <summary>The shapes of that diagram, once they have been read.</summary>
    public IReadOnlyList<DiagramShape>? Diagram { get; set; }

    /// <summary>The relationship the chart's own part is reached by, where this frame holds one.</summary>
    public string? ChartRelationshipId { get; set; }

    /// <summary>That chart, once it has been read.</summary>
    public ChartDefinition? Chart { get; set; }

    public double WidthPoints => Units.EmuToPoints(WidthEmu);

    public double HeightPoints => Units.EmuToPoints(HeightEmu);
}

/// <summary>How text behaves around a floating drawing.</summary>
internal enum TextWrapMode
{
    /// <summary>Text ignores the drawing entirely; the two overlap.</summary>
    None,

    /// <summary>Text flows beside the drawing, avoiding its rectangle.</summary>
    Square,

    /// <summary>Text is pushed above and below; nothing sits beside it.</summary>
    TopAndBottom
}

/// <summary>What a floating drawing's horizontal position is measured from.</summary>
internal enum HorizontalAnchor
{
    Column,
    Margin,
    Page,
    Character,
    LeftMargin,
    RightMargin
}

/// <summary>What a floating drawing's vertical position is measured from.</summary>
internal enum VerticalAnchor
{
    Paragraph,
    Line,
    Margin,
    Page,
    TopMargin,
    BottomMargin
}

/// <summary>
/// How a picture is washed out: the two numbers a watermark of one carries.
/// </summary>
/// <remarks>
/// Measured from watermark-washout-probe, which holds the same bands of flat colour six times over
/// at different settings. What comes out of a channel, everything in nought to one, is
///
///     gain × in + (1 − gain) ÷ 2 + black × (1 + gain)
///
/// clamped at both ends. The gain is a contrast about mid grey — half a gain leaves grey alone and
/// pulls black and white halfway towards it — and the black level a brightness on top of that.
/// Word writes a gain of 19661 and a black level of 22938, both in sixty-fourths of a thousand, for
/// every picture watermark it makes: three tenths of the contrast, and pale enough to read through.
///
/// Why the black level counts for more when the gain is high is not explained here. The formula is
/// what six settings fit, two of them at the ends of the scale, and not something derived.
/// </remarks>
internal sealed record PictureWash(double Gain, double BlackLevel)
{
    /// <summary>Nothing done to it, which is what a picture with no washing gets.</summary>
    public static readonly PictureWash None = new(1, 0);

    public bool IsIdentity => Math.Abs(Gain - 1) < 0.0001 && Math.Abs(BlackLevel) < 0.0001;

    /// <summary>What one sample comes to, in nought to one.</summary>
    public double Apply(double value) =>
        Math.Clamp(Gain * value + (1 - Gain) / 2 + BlackLevel * (1 + Gain), 0, 1);
}

/// <summary>
/// One shape of a diagram: what it is drawn as, where it is in the frame, and where inside it the
/// words go.
/// </summary>
/// <remarks>
/// The text rectangle is given outright rather than as an inset from the shape, because a diagram
/// works out for itself how much of an odd shape its words will fit in — the text of a chevron
/// clears its point, and no inset says that.
/// </remarks>
internal sealed record DiagramShape(
    ShapeFrame Shape,
    double X, double Y, double Width, double Height,
    double TextX, double TextY, double TextWidth, double TextHeight);

/// <summary>Where a shape's text sits in the height the shape gives it.</summary>
internal enum ShapeTextAnchor
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// A colour a drawing names: either outright, or as a slot in the document's theme.
/// </summary>
/// <remarks>
/// Which of the two it is cannot be resolved while the document is being read, since the theme is
/// a part of its own and is not loaded yet. It is carried as written and looked up at layout.
/// </remarks>
internal sealed record DrawingColorReference(string? Hex, string? ThemeSlot);

/// <summary>
/// A word drawn as a shape: one string, in one face, stretched to fill the shape it is in.
/// </summary>
/// <remarks>
/// The size the document gives it means nothing — Word writes a single point — because the shape
/// type says the text is to be fitted to the shape, and that is what decides how large it comes
/// out. What is stretched is the ink itself rather than the box the face would set it in, so a
/// word with a tail below the line is squashed to the same height as one without.
/// </remarks>
internal sealed record ShapeWordArt(string Text, string FontFamily, bool Bold = false, bool Italic = false);

/// <summary>
/// A shape drawn in the text: an outline of some geometry, filled or not, holding text or not.
/// </summary>
/// <remarks>
/// A text box is a shape with text in it, and nothing else tells the two apart — the same element
/// carries a rectangle drawn round a paragraph and a plain rectangle drawn on its own. What it
/// holds is a document of its own: whole paragraphs, and tables, laid out into a box that is not
/// the page's.
/// </remarks>
internal sealed class ShapeFrame
{
    /// <summary>
    /// The preset geometry, as the drawing names it: <c>rect</c>, <c>roundRect</c>,
    /// <c>ellipse</c>, <c>triangle</c>. Anything else is drawn as a rectangle.
    /// </summary>
    public string Geometry { get; set; } = "rect";

    /// <summary>What it is filled with, or null where it is not filled at all.</summary>
    public DrawingColorReference? Fill { get; set; }

    /// <summary>What its outline is drawn in, or null where it has none.</summary>
    public DrawingColorReference? Line { get; set; }

    /// <summary>
    /// How thick that outline is. Three quarters of a point is what Word draws where a shape
    /// gives its outline a colour and no width, that being the weight its own gallery uses.
    /// </summary>
    public double LineWidthPoints { get; set; } = 0.75;

    /// <summary>The blocks inside it, empty for a shape holding no text.</summary>
    public List<BlockElement> Content { get; } = [];

    /// <summary>
    /// How far inside its edges the text sits. Word's defaults are a tenth of an inch at the
    /// sides and half of that above and below, which is what a shape declaring none of them gets.
    /// </summary>
    public double InsetLeftPoints { get; set; } = 7.2;

    public double InsetTopPoints { get; set; } = 3.6;

    public double InsetRightPoints { get; set; } = 7.2;

    public double InsetBottomPoints { get; set; } = 3.6;

    public ShapeTextAnchor Anchor { get; set; } = ShapeTextAnchor.Top;

    /// <summary>
    /// A word set to fill the shape rather than paragraphs laid out inside it, or null for an
    /// ordinary shape. This is what a watermark is.
    /// </summary>
    public ShapeWordArt? WordArt { get; set; }

    /// <summary>How far the shape is turned, clockwise, in degrees.</summary>
    public double RotationDegrees { get; set; }

    /// <summary>
    /// How solid its fill is, from nought for invisible to one for opaque. A watermark is set at
    /// a half, which is what makes it a watermark rather than a heading across the page.
    /// </summary>
    public double FillOpacity { get; set; } = 1;

    /// <summary>
    /// How far down and to the right the shape is drawn from where its size puts it, in points.
    /// Nought for everything but a thickly outlined shape in the older spelling.
    /// </summary>
    /// <remarks>
    /// Word draws an old-style shape offset from its own box by an amount that depends on how
    /// thick its outline is, and the text inside it by half of that. See <see cref="Vml"/>, where
    /// the ten weights it was measured at are listed.
    /// </remarks>
    public double DrawnOffsetPoints { get; set; }

    /// <summary>
    /// The outline's weight in whole points, at least one, and zero for a shape that has no
    /// outline or is not an old-style one. Both of the rules Word applies to such a shape turn on
    /// this single number: see <see cref="n8PDF.Ooxml.Vml"/>.
    /// </summary>
    public double OutlineWholePoints { get; set; }

    public bool HasText => Content.Count > 0;
}

/// <summary>
/// A drawing positioned independently of the text flow, from a <c>wp:anchor</c>.
/// </summary>
/// <remarks>
/// It still lives inside a run, because that run is what it is anchored to, but it does not
/// occupy space on the line the way an inline drawing does. Its rectangle is computed from the
/// anchor point and the text then flows around it.
/// </remarks>
internal sealed class AnchoredDrawing : InlineElement
{
    public required long WidthEmu { get; init; }

    public required long HeightEmu { get; init; }

    /// <summary>The picture, by the key its part reaches it under.</summary>
    public string? RelationshipId { get; set; }

    /// <summary>The shape drawn here, where this frame holds one rather than a picture.</summary>
    public ShapeFrame? Shape { get; init; }

    /// <summary>What was done to the picture's colours, for a watermark of one.</summary>
    public PictureWash? Wash { get; init; }

    /// <summary>The relationship a diagram's data is reached by, where this frame holds one.</summary>
    public string? DiagramRelationshipId { get; set; }

    /// <summary>The shapes of that diagram, once they have been read.</summary>
    public IReadOnlyList<DiagramShape>? Diagram { get; set; }

    /// <summary>The relationship the chart's own part is reached by, where this frame holds one.</summary>
    public string? ChartRelationshipId { get; set; }

    /// <summary>That chart, once it has been read.</summary>
    public ChartDefinition? Chart { get; set; }

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
internal sealed class Run
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
internal sealed class Paragraph : BlockElement
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
internal sealed class FieldScope
{
    private int _depth;

    public bool IsOpen => _depth > 0;

    public void Open() => _depth++;

    public void Close()
    {
        if (_depth > 0) _depth--;
    }
}

/// <summary>What a floating table's position is measured from.</summary>
internal enum TableAnchor
{
    /// <summary>The text: where the table would have stood had it not been floating.</summary>
    Text,

    /// <summary>The margin, which is the text's own box on the page.</summary>
    Margin,

    /// <summary>The paper.</summary>
    Page
}

/// <summary>A place named rather than measured, as <c>left</c> or <c>center</c>.</summary>
internal enum TableAlignSpec
{
    None,
    Left,
    Center,
    Right,
    Inside,
    Outside,
    Top,
    Bottom,
    Inline
}

/// <summary>
/// A table taken out of the flow, from <c>w:tblpPr</c>: it stands where it is put and the text
/// runs round it.
/// </summary>
/// <remarks>
/// Measured against Word in floating-table-probe. The distances are the daylight Word keeps
/// between the table and the text — an eighth of an inch either side is what Word writes itself —
/// and the anchors say what the place is measured from.
///
/// The place names the *cell's own text edge* rather than the table's edge, which is the same rule
/// a declared indent follows: a table put at the margin has its first column's text on the margin
/// and its border hanging outside it. The probe says so twice over, once with a half point border
/// and once with a three point one: the border grows outward and the text stays where it was.
/// </remarks>
internal sealed record TablePosition(
    double LeftFromTextPoints,
    double RightFromTextPoints,
    double TopFromTextPoints,
    double BottomFromTextPoints,
    TableAnchor HorizontalAnchor,
    TableAnchor VerticalAnchor,
    double XPoints,
    TableAlignSpec XSpec,
    double YPoints,
    TableAlignSpec YSpec);

/// <summary>
/// Which way a table cell's text runs, from <c>w:textDirection</c>.
/// </summary>
/// <remarks>
/// Measured against Word in cell-direction-probe. A turned cell is laid out in a frame of its own
/// turned a quarter circle: the line runs along the cell's height and the lines stack across its
/// width. Word does not make the row any taller to hold them — a turned cell in a row of one line
/// wraps its text every two characters and runs out of the cell rather than growing it.
/// </remarks>
internal enum CellTextDirection
{
    /// <summary>Across the cell, as text usually runs.</summary>
    LeftToRight,

    /// <summary>
    /// Up the cell: the line reads from the foot to the head and the lines stack left to right,
    /// which is <c>btLr</c> and what a narrow heading is usually written in.
    /// </summary>
    BottomToTop,

    /// <summary>
    /// Down the cell: the line reads from the head to the foot and the lines stack right to left,
    /// which is <c>tbRl</c>.
    /// </summary>
    TopToBottom
}

/// <summary>Where a dropped capital sits.</summary>
internal enum DropCapKind
{
    /// <summary>Nowhere: the frame is not a dropped capital at all.</summary>
    None,

    /// <summary>Inside the text, which is shortened to make room for it.</summary>
    Drop,

    /// <summary>Out in the margin, where the text is left at its full measure.</summary>
    Margin
}

/// <summary>
/// A frame round a paragraph, from <c>w:framePr</c>. Only the dropped capital is honoured: the
/// general case, a paragraph placed anywhere on the page with text flowing round it, is not.
/// </summary>
/// <remarks>
/// Measured against Word in drop-cap-probe, and written the way Word itself writes it — its own
/// AppleScript was asked for a dropped capital and this is the markup that came back:
///
///   * <c>w:lines</c> is a record of what was asked for, not what is drawn. Word writes the size
///     it worked out onto the run and pins the paragraph's line to the height of the frame, and
///     the drawing follows those. A frame of three lines round a letter of ordinary size shortens
///     one line, not three.
///   * The frame is as wide as the letter's own advance plus <c>w:hSpace</c>, rounded to the
///     1/300 inch grid, and the lines it reaches are shortened by exactly that.
///   * <c>margin</c> differs from <c>drop</c> in where the frame sits and nowhere else: Word
///     anchors it to the page instead of the text, so the letter hangs its own width out to the
///     left and the text keeps the whole measure.
/// </remarks>
internal sealed record FrameProperties(DropCapKind DropCap, int Lines, double HorizontalSpacePoints);

/// <summary>
/// Whether words are broken at the ends of lines, and on what terms.
/// </summary>
/// <param name="ZonePoints">
/// How much white a line may be left with before a word is broken to fill it, from
/// <c>w:hyphenationZone</c>. Word's own default is a quarter of an inch.
/// </param>
/// <param name="ConsecutiveLimit">
/// How many lines in a row may end in a hyphen, from <c>w:consecutiveHyphenLimit</c>. Zero means
/// no limit, which is Word's default.
/// </param>
/// <param name="LeaveCapitalsAlone">A word in capitals is not broken, from <c>w:doNotHyphenateCaps</c>.</param>
internal sealed record Hyphenation(
    bool Automatic, double ZonePoints, int ConsecutiveLimit, bool LeaveCapitalsAlone);

/// <summary>Where the count of lines begins again.</summary>
internal enum LineNumberRestart
{
    /// <summary>At the top of every page, which is what a section that says nothing gets.</summary>
    NewPage,

    /// <summary>At the top of every section.</summary>
    NewSection,

    /// <summary>Never: the count carries on from the section before.</summary>
    Continuous
}

/// <summary>
/// Numbering down the margin, from <c>w:lnNumType</c>.
/// </summary>
/// <remarks>
/// Measured against Word in line-number-probe. Every line of the body is counted, an empty
/// paragraph among them; a paragraph asking to be passed over is neither numbered nor counted, so
/// the line after two of them carries the number the first of them would have had. Only the
/// numbers that divide by <see cref="CountBy"/> are written, and they are written right against a
/// place <see cref="Distance"/> in from the text, in the document's own face rather than the
/// paragraph's.
/// </remarks>
internal sealed record LineNumbering(
    int CountBy,
    int Start,
    LineNumberRestart Restart,
    double Distance);

/// <summary>Which pages of a section carry the border round the page.</summary>
internal enum PageBorderDisplay
{
    AllPages,
    FirstPage,
    NotFirstPage
}

/// <summary>
/// One edge of the border round a page: the line, and how far it stands off.
/// </summary>
/// <param name="Space">
/// In points. Measured from the page's edge to the outside of the line where the border is offset
/// from the page, and from the text's edge to the inside of it where it is offset from the text —
/// which is what page-border-probe shows: a border 24 points from the page has its outer edge at
/// 24, and one against the text with no space at all has its inner edge on the margin.
/// </param>
internal sealed record PageBorderEdge(BorderEdge Line, double Space);

/// <summary>
/// The border round a page, from <c>w:pgBorders</c>.
/// </summary>
/// <remarks>
/// An edge runs the length of the page rather than of the border: where the border has no right
/// edge, the top one runs on to the paper's edge, which is what Word draws.
/// </remarks>
internal sealed class PageBorders
{
    public PageBorderEdge? Top { get; set; }

    public PageBorderEdge? Left { get; set; }

    public PageBorderEdge? Bottom { get; set; }

    public PageBorderEdge? Right { get; set; }

    /// <summary>Whether the offsets are measured from the text rather than from the page.</summary>
    public bool FromText { get; set; }

    public PageBorderDisplay Display { get; set; }

    public bool IsEmpty => Top is null && Left is null && Bottom is null && Right is null;
}

/// <summary>One edge of a border, as declared by <c>w:top</c>, <c>w:insideV</c> and friends.</summary>
/// <param name="Style">The <c>w:val</c> line style; "none" and "nil" mean no line.</param>
/// <param name="SizeEighthPoints">Line width in eighths of a point.</param>
/// <param name="ColorHex">RRGGBB, or null for automatic (rendered black).</param>
internal sealed record BorderEdge(string Style, double SizeEighthPoints, string? ColorHex)
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
internal sealed class BorderSet
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
internal enum RowHeightRule
{
    /// <summary>Height is determined by the content.</summary>
    Auto,

    /// <summary>Content may grow the row beyond the declared height.</summary>
    AtLeast,

    /// <summary>The row is exactly this tall.</summary>
    Exact
}

internal enum VerticalCellAlignment
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
internal sealed class TableLook
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
internal sealed class TableProperties
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

    /// <summary>Where the table floats, or null where it stands in the flow like any other.</summary>
    public TablePosition? Position { get; set; }

    /// <summary>
    /// Whether the columns run the other way, from <c>w:bidiVisual</c>: the first cell of a row
    /// stands at the right and the rest follow leftwards.
    /// </summary>
    /// <remarks>
    /// Measured against Word in column-order-probe. The whole table is turned about, not merely
    /// the cells: it is laid from the right margin, its indent is measured from the right, and the
    /// border a cell calls its left is drawn on its right.
    /// </remarks>
    public bool Mirrored { get; set; }

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
internal sealed class Table : BlockElement
{
    public TableProperties Properties { get; set; } = new();

    /// <summary>Column widths in twips from <c>w:tblGrid</c>, one entry per grid column.</summary>
    public List<int> Grid { get; } = [];

    public List<TableRow> Rows { get; } = [];
}

internal sealed class TableRow
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

internal sealed class TableCell
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

    /// <summary>Which way the cell's text runs, from <c>w:textDirection</c>.</summary>
    public CellTextDirection TextDirection { get; set; } = CellTextDirection.LeftToRight;

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
internal sealed class Note(int id, string type)
{
    public int Id { get; } = id;

    public string Type { get; } = type;

    public bool IsSeparator => Type is "separator" or "continuationSeparator";

    public List<BlockElement> Body { get; } = [];
}

/// <summary>The contents of one header or footer part.</summary>
internal sealed class HeaderFooter
{
    public List<BlockElement> Body { get; } = [];
}

/// <summary>The parsed main document part.</summary>
internal sealed class WordDocument
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

    /// <summary>
    /// Whether Word may break a word at the end of a line, from <c>w:autoHyphenation</c>, and the
    /// terms it is allowed to do it on. Declared in settings.xml: it is the document's habit
    /// rather than any one paragraph's, though a paragraph may say it is to be left alone.
    /// </summary>
    public Hyphenation Hyphenation { get; set; } = new(false, 18, 0, false);

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
