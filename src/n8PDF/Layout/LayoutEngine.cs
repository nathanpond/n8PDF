using System.Globalization;
using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Styling;

namespace n8PDF.Layout;

/// <summary>What line filling needs to know to place a tab that aligns its text.</summary>
/// <param name="DecimalSymbol">
/// What counts as a decimal separator, from the document's settings. A comma in much of the
/// world, so a decimal tab that assumed a full stop would leave those columns ragged.
/// </param>
internal readonly record struct TabOptions(bool ApplyKerning, string DecimalSymbol);

/// <summary>Knobs that change how layout is performed.</summary>
public sealed class LayoutOptions
{
    /// <summary>
    /// Apply pair kerning when measuring. Off by default to match Word, which does not kern
    /// unless a document asks it to.
    /// </summary>
    public bool ApplyKerning { get; set; }

    /// <summary>Default tab stop interval in twips. Word's default is half an inch.</summary>
    public int DefaultTabStopTwips { get; set; } = 720;
}

/// <summary>
/// Turns a parsed document into positioned text on pages: measurement, line breaking, vertical
/// stacking and pagination.
/// </summary>
internal sealed class LayoutEngine(
    FontLibrary fonts,
    StyleResolver styles,
    LayoutOptions? options = null,
    Packaging.PackageLimits? limits = null)
{
    private readonly FontLibrary _fonts = fonts;

    /// <summary>
    /// Whether words are broken at the ends of lines, and on what terms. The document's own
    /// habit, from settings.xml.
    /// </summary>
    private Hyphenation _hyphenation = new(false, 18, 0, false);

    /// <summary>
    /// The measure a word must fit, where a word that does not is to be broken between its
    /// letters. Set only while the inside of a shape is being laid out; null everywhere else,
    /// because nothing on a page is broken that way.
    /// </summary>
    private double? _breakInsideWords;
    private readonly StyleResolver _styles = styles;
    private readonly LayoutOptions _options = options ?? new LayoutOptions();
    private IReadOnlyDictionary<string, byte[]> _images = new Dictionary<string, byte[]>();
    private IReadOnlyDictionary<string, string> _hyperlinks = new Dictionary<string, string>();
    private LaidOutDocument? _result;

    // Bookmarks seen while building a paragraph's atoms, recorded once the paragraph is placed.
    private readonly List<string> _pendingBookmarks = [];

    // Decoded images, keyed by relationship id. A picture used several times is decoded once and
    // yields the same instance every time, which is what lets the writer embed it once.
    private readonly Dictionary<string, Images.ImageData?> _decodedImages = [];

    // List counters, which advance as paragraphs are laid out. A list item's number depends on
    // every item before it, so this is per-document state rather than per-paragraph.
    private NumberingCounter _numbering = new(new NumberingDefinitions());

    // Note bodies by id, and the label each has been given. Labels are assigned as references are
    // met, because a note's number is its position in the document rather than anything stored in
    // the file.
    private IReadOnlyDictionary<int, Note> _footnotes = new Dictionary<int, Note>();
    private IReadOnlyDictionary<int, Note> _endnotes = new Dictionary<int, Note>();
    private readonly Dictionary<int, string> _footnoteLabels = [];

    /// <summary>
    /// How often each kind of note is numbered again from the beginning, which the section says.
    /// </summary>
    /// <summary>Where the notes of a page are set, which the section says.</summary>
    private NotePosition _footnotePosition;

    private NoteNumberRestart _footnoteRestart;

    private NoteNumberRestart _endnoteRestart;

    /// <summary>
    /// The next number each kind of note takes, and what was true of the last one numbered — the
    /// page it fell on and the section it was in, which is what says whether to begin again.
    /// </summary>
    private (int Next, int Page, int Section) _footnoteCounter = (1, 0, 0);

    private (int Next, int Page, int Section) _endnoteCounter = (1, 0, 0);

    /// <summary>The page each note's mark fell on, which per-page numbering is worked out from.</summary>
    private readonly Dictionary<int, LaidOutPage> _notePages = [];
    private readonly Dictionary<int, string> _endnoteLabels = [];
    private NumberFormat _footnoteFormat = NumberFormat.Decimal;
    private NumberFormat _endnoteFormat = NumberFormat.LowerRoman;

    // Endnote ids in the order their references appeared, which is the order they are written out
    // in at the end of the document.
    /// <summary>The endnotes in reference order, and which section each was referred to from.</summary>
    private readonly List<(int Id, int Section)> _endnoteOrder = [];

    /// <summary>Where the endnotes are gathered, which the document says.</summary>
    private EndnotePosition _endnotePosition;

    /// <summary>Notes already laid out, by the note and the measure it was set to.</summary>
    /// <summary>
    /// The face borrowed for a character its run's own cannot draw, asked once for each, and null
    /// where the run's own face draws it — which is nearly always.
    /// </summary>
    private readonly Dictionary<(TrueTypeFont Font, int CodePoint, bool Bold, bool Italic), FontSelection?> _faces = [];

    private readonly Dictionary<(int Id, double Width), DetachedFlow> _measuredFootnotes = [];

    /// <summary>The separators, by kind and measure.</summary>
    private readonly Dictionary<(string Type, double Width), DetachedFlow?> _separatorFlows = [];

    // Footnotes waiting to be written into the foot of the page being filled.
    /// <summary>
    /// The notes collected for the page being filled, kept by the column they belong under. Word
    /// sets a note under the column that refers to it rather than across the measure, so a page of
    /// two columns has two footnote areas and each takes its space out of its own column.
    /// </summary>
    private readonly Dictionary<int, List<DetachedFlow>> _pageFootnotes = [];

    /// <summary>
    /// What is left of a note too long for the foot of the page its reference fell on, waiting for
    /// the page after. A note may run over several pages, so what is carried is carried again.
    /// </summary>
    private readonly List<DetachedFlow> _carriedFootnotes = [];

    /// <summary>Whether the page being filled opened with the rest of a note from the page before.</summary>
    private bool _footnotesContinue;


    // Where the footnote area sits, fixed for the document by its section.
    /// <summary>
    /// The measure a separator is being laid out against, which the wide rule spans. Set only
    /// while one is measured, since it is the one thing on a line whose width is the line's.
    /// </summary>
    private double _separatorMeasure;
    private double _footnoteBottom;

    // The label of the note whose body is being laid out, for the w:footnoteRef or w:endnoteRef
    // that opens it.
    private string? _currentNoteLabel;

    // What the document calls a decimal separator, which is what a decimal tab lines up.
    private string _decimalSymbol = ".";

    // Counts the paragraphs laid out, so the lines in a column can say which one they came from.
    private int _paragraphOrdinal;

    // How many pages the section being laid out has produced, which is what a title page and a
    // section's own numbering are counted against.
    private int _pagesInSection;

    /// <summary>
    /// How thick the lines of a checkbox are: the square, and the cross drawn through a ticked
    /// one. Word's own, whatever the size of the box — a seventy-two point box is drawn with the
    /// same three quarters of a point as an eight point one.
    /// </summary>
    private const double CheckBoxLinePoints = 0.72;

    private const double CheckBoxCrossPoints = 0.48;

    /// <summary>The number the next line of the body takes, where the section numbers its lines.</summary>
    private int _nextLineNumber = 1;

    /// <summary>
    /// What is left of a floating table that ran off the foot of a page, waiting for the next one.
    /// </summary>
    private CarriedTable? _carriedTable;

    /// <summary>How far down the last floating table laid reached, before its daylight.</summary>
    private double _tableBottom;

    /// <summary>
    /// The paragraph last laid out, and everything needed to lay it again. A float's clearance can
    /// reach back over lines already written, and Word breaks those lines again round it.
    /// </summary>
    /// <remarks>
    /// Null where the last thing laid out cannot be done twice: a paragraph that broke across a
    /// page, or one that anchored a picture of its own, whose picture is on the page already and
    /// would be put there again. Those keep their lines whole, which is what everything did before
    /// this existed.
    /// </remarks>
    private ReflowableParagraph? _reflowable;

    /// <summary>
    /// The ordinal given to a line that belongs to no paragraph of the flow: a number down the
    /// margin, or a dropped capital's own line.
    /// </summary>
    private const int ParagraphIndexNone = -1;

    /// <summary>
    /// The number the last page made was printed as, and the number the next section's first page
    /// is to be printed as where it begins its numbering again.
    /// </summary>
    private int _printedPage;

    private int? _pendingPageNumber;

    // Which section is being laid out, counting from one. Only a first pass needs it: after that
    // the sections are read off the pages the last pass produced.
    private int _sectionOrdinal = 1;

    // Which page is being laid out, for fields that depend on it. Zero means the body, where the
    // page a field lands on is only known once the line holding it has been placed; headers and
    // footers set it before they run.
    private int _currentPage;
    private int _totalPages;

    // Fields are numbered as they are met, in document order, and the page each landed on is
    // recorded when its line is placed. That is what a second pass reads: a page number cannot be
    // known while the page it is on is still being filled, and Word settles it the same way, by
    // laying the document out and then again.
    private int _fieldOccurrence;
    private readonly Dictionary<int, LaidOutPage> _fieldPages = [];

    // The paragraphs a STYLEREF can pick up, in the order they were placed: which page each landed
    // on, the style it was set in, and the text it holds. A running head is made of these.
    private readonly List<StyledParagraph> _styledParagraphs = [];

    // The same read straight off the document, for the one case the placed ones cannot answer: a
    // field with no paragraph of that style before it looks forward instead.
    private List<(string StyleId, string Text)> _documentStyled = [];

    // The page each heading landed on, by the paragraph itself: a table of contents cannot say
    // which page a heading is on until the document has been laid out once, so this is filled as
    // the headings are placed and read back on the pass after. Keyed by the paragraph rather than
    // by its position, since both passes lay out the same document.
    private readonly Dictionary<Paragraph, LaidOutPage> _headingPages = [];

    // The body being laid out, which a table of contents reads to find the headings. It is the
    // document rather than the section, since a table gathers the whole of it.
    private IReadOnlyList<BlockElement> _body = [];

    // Index entries met while building a paragraph's atoms, recorded with the page once the
    // paragraph is placed — the same way its bookmarks are. An XE field draws nothing, so there is
    // no line of its own to put it on.
    private readonly List<FieldInline> _pendingMarks = [];
    private readonly Dictionary<FieldInline, LaidOutPage> _markPages = [];

    /// <summary>What a field needs to know beyond its instruction.</summary>
    public FieldEnvironment Fields { get; set; } = new();

    /// <summary>What an earlier pass learned about where the fields fell, if there was one.</summary>
    public FieldPagination? Pagination { get; set; }

    /// <summary>
    /// True where a field was met whose value depends on pagination and was not yet known, so
    /// that laying the document out again would say more than this pass could.
    /// </summary>
    public bool NeedsPagination { get; private set; }

    public LaidOutDocument Layout(WordDocument document)
    {
        _images = document.Images;
        _hyperlinks = document.Hyperlinks;
        _footnotes = document.Footnotes;
        _endnotes = document.Endnotes;
        _decimalSymbol = string.IsNullOrEmpty(document.DecimalSymbol) ? "." : document.DecimalSymbol;
        _hyphenation = document.Hyphenation;
        _footnoteFormat = document.FootnoteNumberFormat;
        _endnotePosition = document.EndnotePosition;
        _endnoteFormat = document.EndnoteNumberFormat;
        _footnoteLabels.Clear();
        _endnoteLabels.Clear();
        _notePages.Clear();
        _footnoteCounter = (1, 0, 0);
        _endnoteCounter = (1, 0, 0);
        _endnoteOrder.Clear();
        _measuredFootnotes.Clear();
        _pageFootnotes.Clear();
        _carriedFootnotes.Clear();
        _footnotesContinue = false;
        _separatorFlows.Clear();
        _currentNoteLabel = null;
        _decodedImages.Clear();
        _numbering = new NumberingCounter(_styles.Numbering);
        _fieldOccurrence = 0;
        _fieldPages.Clear();
        _sectionOrdinal = 1;
        _styledParagraphs.Clear();
        _headingPages.Clear();
        _pendingMarks.Clear();
        _markPages.Clear();
        _body = document.Body;
        _documentStyled = CollectStyledParagraphs(document.Body);
        Fields.StyleReference = StyleReference;
        Fields.Sequences.Clear();
        NeedsPagination = false;

        var sections = SplitIntoSections(document);
        var section = sections[0].Section;

        var result = new LaidOutDocument { Section = document.Section };
        _result = result;
        _pagesInSection = 0;

        // The first section's numbering begins where it says, or at one.
        _printedPage = 0;
        _pendingPageNumber = section.PageNumberStart;

        var contentTop = Units.TwipsToPoints(section.MarginTopTwips);

        _footnoteBottom = contentTop + section.ContentHeightPoints;

        var cursor = new Cursor
        {
            Engine = this,
            Document = result,
            Section = section,
            Page = NewPage(result, section),
            Y = contentTop,
            Left = 0,
            Width = 0,
            ContentTop = contentTop,
            ContentLimit = contentTop + section.ContentHeightPoints,
            Paginate = true,
            OnPageComplete = FlushFootnotes
        };

        ApplySection(cursor, section);

        for (var index = 0; index < sections.Count; index++)
        {
            if (index > 0) StartSection(cursor, sections[index].Section);
            LayoutBlocks(cursor, sections[index].Blocks);

            // A document that gathers its endnotes by section writes each group where that section
            // stops, which is before the break that opens the next one.
            if (_endnotePosition == EndnotePosition.SectionEnd) LayoutEndnotes(cursor, _sectionOrdinal);
        }

        // Endnotes follow the body in ordinary flow, so they are laid out through the same cursor
        // and paginate like anything else.
        LayoutEndnotes(cursor);

        // The final paragraph's space-after still occupies the page even though nothing follows
        // it, which matters for how much content a page is considered to hold.
        cursor.Y += cursor.PendingSpaceAfter;

        // A note may outlast the document it belongs to: one referenced near the end and long
        // enough to fill several pages has nothing left to carry it. Word makes the pages anyway,
        // each holding nothing but the rest of the note, and so does this — dropping the end of a
        // note would lose text the document has.
        DrainFootnotes(cursor);

        // And a floating table may outlast it the same way: one begun near the end with more rows
        // than the page can hold has nothing left to carry it over. The pages are made for it, as
        // Word makes them — the probe's sixty rows come out forty on one page and twenty on a page
        // of their own, with nothing else on it.
        for (var guard = 0; _carriedTable is not null && guard < 256; guard++) cursor.BreakPage();

        // The last page never breaks, so nothing has settled it yet.
        cursor.FinishPage();

        // Headers and footers are laid out last, once the page count is known — a footer saying
        // "page 2 of 7" cannot be composed before the seven exists.
        LayoutHeadersAndFooters(document, result);

        return result;
    }

    /// <summary>
    /// Splits the body into the runs of blocks each section owns.
    /// </summary>
    /// <remarks>
    /// A section break is stored on the last paragraph <em>before</em> it, carrying the outgoing
    /// section's properties — so the paragraph holding the break belongs to the section it
    /// describes, not to the one that follows. Whatever trails the last break belongs to the
    /// body-level <c>sectPr</c>, which is the final section; a document without any breaks is that
    /// one section over the whole body.
    ///
    /// A section that states no running head of some kind inherits the previous section's, which
    /// is what Word's "link to previous" means and what it relies on when it omits them.
    /// </remarks>
    private static List<(SectionProperties Section, List<BlockElement> Blocks)> SplitIntoSections(
        WordDocument document)
    {
        var sections = new List<(SectionProperties Section, List<BlockElement> Blocks)>();
        var current = new List<BlockElement>();

        foreach (var block in document.Body)
        {
            current.Add(block);

            if (block is not Paragraph { SectionBreak: { } properties }) continue;

            sections.Add((properties, current));
            current = [];
        }

        // The trailing blocks, and any document with no breaks at all, take the final section.
        if (current.Count > 0 || sections.Count == 0) sections.Add((document.Section, current));

        for (var i = 1; i < sections.Count; i++)
        {
            var previous = sections[i - 1].Section;
            var section = sections[i].Section;

            // Linking is per kind, not all or nothing: a section that gives its own first page a
            // header and says nothing about the rest keeps the previous section's for the rest.
            foreach (var (kind, id) in previous.HeaderReferences)
                section.HeaderReferences.TryAdd(kind, id);

            foreach (var (kind, id) in previous.FooterReferences)
                section.FooterReferences.TryAdd(kind, id);
        }

        return sections;
    }

    /// <summary>
    /// Moves the cursor into a new section, taking on its page geometry.
    /// </summary>
    /// <remarks>
    /// A continuous break carries on down the same page under the new margins. Word only honours
    /// that when the paper itself is unchanged: a section that alters the page size cannot share a
    /// page with the one before it however it was declared, so it starts a new one regardless.
    ///
    /// An even or odd break may leave a blank page behind, which is the point of it — a chapter
    /// that must open on a right-hand page does so whether or not the previous one ended on a
    /// left-hand one.
    /// </remarks>
    private void StartSection(Cursor cursor, SectionProperties section)
    {
        var previous = cursor.Section;

        var samePaper = section.PageWidthTwips == previous.PageWidthTwips &&
                        section.PageHeightTwips == previous.PageHeightTwips;

        var samePage = samePaper &&
                       section.BreakType is SectionBreakType.Continuous or SectionBreakType.NextColumn;

        // A section of columns closed by a continuous break has its last page evened out, which is
        // what a continuous break is usually inserted to do. One closed by a break to a new page
        // is not, nor is the last section of a document: measured from Word's export of
        // columns-balanced, which holds all three.
        if (section.BreakType == SectionBreakType.Continuous) BalanceColumns(cursor);

        // Pages are counted from the start of each section, and the count has to be reset before
        // the section's first page is made rather than after it.
        _pagesInSection = 0;
        _sectionOrdinal++;
        _pendingPageNumber = section.PageNumberStart;

        // The outgoing section's last paragraph still occupies its space, and nothing across the
        // boundary collapses against it.
        cursor.Y += cursor.PendingSpaceAfter;
        cursor.PendingSpaceAfter = 0;
        cursor.PreviousFormat = null;

        if (samePage)
        {
            ApplySection(cursor, section);
            return;
        }

        // The page being left behind is settled under the geometry it was laid out with, before
        // the new section's replaces it.
        cursor.FinishPage();
        ApplySection(cursor, section);
        cursor.StartNewPage();

        // At most one blank page: each break flips the parity.
        var wantsEven = section.BreakType == SectionBreakType.EvenPage;
        var wantsOdd = section.BreakType == SectionBreakType.OddPage;

        if ((wantsEven && cursor.Document.Pages.Count % 2 != 0) ||
            (wantsOdd && cursor.Document.Pages.Count % 2 == 0))
        {
            cursor.BreakPage();
        }
    }

    /// <summary>
    /// Sets the current section's content on this page out again over its columns, so that they
    /// come to much the same depth rather than the first being full and the last empty.
    /// </summary>
    /// <remarks>
    /// The lines are already placed, so this takes them off the page and puts them down again
    /// against a depth of its own: the content's total height divided by the number of columns,
    /// filled to the first line boundary at or past that depth. Word divides thirty-five lines
    /// eighteen and seventeen, which is that rule and not the other rounding.
    ///
    /// Only the section's own lines on the page move. A page may carry the end of one section and
    /// the beginning of another, and evening out the one must leave the other where it is.
    /// </remarks>
    private void BalanceColumns(Cursor cursor)
    {
        if (cursor.Columns.Count < 2 || !cursor.Paginate) return;

        var placed = cursor.PagePlaced.Where(entry => entry.Section == _sectionOrdinal).ToList();
        if (placed.Count == 0) return;

        // Already spread over the columns and reaching the foot of the page? Then there is nothing
        // to even out: the page is full and Word leaves it alone.
        var lines = placed.Select(entry => entry.Line).ToList();
        var origin = lines.Min(line => line.Top);

        // What the content comes to, as though it were set in one column.
        var total = 0.0;
        var previous = double.NaN;

        foreach (var (column, _, line) in placed)
        {
            total += double.IsNaN(previous) || column != placed[0].Column && line.Top <= previous
                ? line.Line.Height
                : Math.Max(line.Line.Height, line.Top + line.Line.Height - previous);

            previous = line.Top + line.Line.Height;
        }

        var target = total / cursor.Columns.Count;

        // Take them off the page. Everything placed after the first of them goes with it, which is
        // this section's own content and nothing else: it is the last thing on the page.
        var first = lines[0];

        cursor.Page.Lines.RemoveRange(first.LineIndex, cursor.Page.Lines.Count - first.LineIndex);
        cursor.Page.Rules.RemoveRange(first.RuleIndex, cursor.Page.Rules.Count - first.RuleIndex);
        cursor.Page.Images.RemoveRange(first.ImageIndex, cursor.Page.Images.Count - first.ImageIndex);

        cursor.PagePlaced.RemoveAll(entry => entry.Section == _sectionOrdinal);
        cursor.ColumnLines.Clear();

        cursor.ColumnIndex = 0;
        cursor.ApplyColumn();
        cursor.Y = origin;

        var bottom = origin;
        var top = origin;
        var last = double.NaN;

        foreach (var line in lines)
        {
            // The gap this line kept from the one before it — the spacing between two paragraphs
            // is part of what moves with them.
            var gap = double.IsNaN(last) ? 0 : Math.Max(0, line.Top - last);

            // A column takes lines while it is still short of the depth they are to be divided
            // at, so the line that reaches it is the last one in. Thirty-five lines divide
            // eighteen and seventeen and ten divide five and five, which is that rule and neither
            // of the roundings either side of it.
            if (cursor.Y + gap >= top + target - 0.001 && cursor.ColumnIndex + 1 < cursor.Columns.Count)
            {
                cursor.ColumnIndex++;
                cursor.MaxColumnUsed = Math.Max(cursor.MaxColumnUsed, cursor.ColumnIndex);
                cursor.ApplyColumn();
                cursor.Y = top;
            }
            else
            {
                cursor.Y += gap;
            }

            last = line.Top + line.Line.Height;

            Place(cursor, line.Line, line.ParagraphIndex, line.ParagraphOrdinal, line.KeepNext,
                line.FootnoteIds, Prepared.None);

            bottom = Math.Max(bottom, cursor.Y);
        }

        // What follows the section begins under the deepest of its columns.
        cursor.ColumnIndex = 0;
        cursor.ApplyColumn();
        cursor.Y = bottom;
    }

    /// <summary>
    /// How wide the rule of a bar tab stop is.
    /// </summary>
    /// <remarks>
    /// Word strokes it with the default pen rather than setting a width, which under the scale it
    /// draws at comes to this — a hairline. The stroke straddles its path and Word offsets the
    /// path by half of it, so the rule covers exactly the stop and the sliver to the right of it.
    /// </remarks>
    private const double BarTabWidthPoints = 0.24;

    /// <summary>
    /// How far a paragraph's background reaches past its own edges: a fiftieth of an inch, on
    /// every side that has an edge to reach past.
    /// </summary>
    /// <remarks>
    /// paragraph-shading-probe measures it. A shaded paragraph at the margins of a letter page
    /// comes out of Word as a rectangle from 70.56 to 541.44 where the text runs from 72 to 540,
    /// and half an inch of indent moves the near edge with it — 106.56 against text at 108. The
    /// far edge stays where it was, so it is the paragraph's own edges the fill is measured from
    /// and not the page's.
    /// </remarks>
    private const double ShadingBleedPoints = 1.44;

    /// <summary>
    /// The box a paragraph asks for, while it is still open: paragraphs of the same box in a row
    /// share one, which is what Word draws.
    /// </summary>
    /// <remarks>
    /// paragraph-border-probe measures every part of it. The line stands a fiftieth of an inch
    /// clear of the text on each side and its own declared space beyond that, the space rounded
    /// down to the grid; above the text there is one step more than the space says, and below it
    /// exactly the space. The line grows outward from there, as thick as its weight rounded down
    /// to the grid. Three paragraphs bordered alike come out as one box with no line between them
    /// unless <c>w:between</c> asks for one, and where it does the line sits at the foot of the
    /// paragraph above with the usual step under it.
    /// </remarks>
    private sealed class BorderBox
    {
        public required ParagraphBorders Borders { get; init; }

        public required LaidOutPage Page { get; init; }

        public required int Column { get; init; }

        /// <summary>The edges of the area the box encloses, which the lines are drawn outside of.</summary>
        public required double Left { get; init; }

        public required double Right { get; init; }

        public required double Top { get; init; }

        public double Bottom { get; set; }

        /// <summary>Where a line was asked for between two paragraphs sharing the box.</summary>
        public List<double> Between { get; } = [];

        /// <summary>
        /// The background of each paragraph in it, by where that paragraph's fill begins: a fill
        /// runs to where the next begins, and the last of them to the foot of the box, so that a
        /// shaded paragraph in a box is filled to the box rather than to its own lines.
        /// </summary>
        public List<(double Top, (double Red, double Green, double Blue) Colour)> Fills { get; } = [];
    }

    private BorderBox? _openBox;

    /// <summary>
    /// A mark over a character: the glyph Word draws for it, where its ink sits inside that glyph,
    /// and how far its baseline stands from the text's.
    /// </summary>
    /// <remarks>
    /// emphasis-mark-probe reads all of it off Word's own page. The mark is a character in its own
    /// right, drawn at the text's size in whatever face carries it — a fullwidth stop for the dot
    /// and the dot below, an ideographic comma for the comma, a ring above for the circle — and it
    /// is the mark's **ink** that is centred over the character, not its advance: Word's fullwidth
    /// stop carries its dot a sixth of an em from the glyph's own edge, and the mark still lands in
    /// the middle of the letter.
    ///
    /// The dot and the comma stand the type size and a step of the grid above the baseline, which
    /// is exact at twelve, twenty-four and forty-eight point and a step out at eight. The ring
    /// stands three tenths of the size above it, and the dot below three eighths of the size under
    /// it; both are measured at one size only.
    /// </remarks>
    private readonly record struct Emphasis(
        FontSelection Font, int CodePoint, double InkCentre, double InkTop, double Offset);

    private readonly Dictionary<(EmphasisMark, string, double, bool, bool), Emphasis?> _emphasis = [];

    private Emphasis? ResolveEmphasis(ResolvedRunFormat format, FontSelection selection)
    {
        if (format.Emphasis is EmphasisMark.None) return null;

        var key = (format.Emphasis, format.FontFamily, format.FontSizePoints, format.Bold, format.Italic);
        if (_emphasis.TryGetValue(key, out var known)) return known;

        var codePoint = format.Emphasis switch
        {
            EmphasisMark.Comma => 0x3001,
            EmphasisMark.Circle => 0x02DA,
            _ => 0xFF0E
        };

        var font = selection.Font.GetGlyphIndex(codePoint) != 0
            ? selection
            : _fonts.ResolveForCharacter(codePoint, selection, format.Bold, format.Italic);

        var glyph = font?.Font.GetGlyphIndex(codePoint) ?? 0;

        if (font is null || glyph == 0)
        {
            _emphasis[key] = null;
            return null;
        }

        var size = format.FontSizePoints;
        var em = font.Font.UnitsPerEm;
        var bounds = font.Font.GetGlyphBounds(glyph);

        var centre = bounds is { } box ? (box.MinX + box.MaxX) / 2.0 * size / em : size / 2;
        var top = bounds is { } ink ? Math.Max(0, ink.MaxY * size / em) : size * 0.2;

        var offset = format.Emphasis switch
        {
            EmphasisMark.Circle => -(size * 0.3),
            EmphasisMark.UnderDot => size * 0.38,
            _ => -(size + Grid.Step)
        };

        var resolved = new Emphasis(font, codePoint, centre, top, offset);
        _emphasis[key] = resolved;

        return resolved;
    }

    /// <summary>How far a border stands from the text: its own space, and the usual fiftieth.</summary>
    private static double BorderReach(ParagraphBorderEdge? edge) =>
        edge is null ? 0 : Grid.Width(edge.SpacePoints);

    /// <summary>The room a side takes over and above what it stands clear of.</summary>
    private static double BorderWeight(ParagraphBorderEdge? edge) =>
        edge is null ? 0 : Grid.Width(edge.Line.WidthPoints);

    /// <summary>
    /// What a box round a run takes on each side of it: the space it asks to stand clear by, and
    /// its own weight beyond that. run-border-probe measures both — a space of four points widens
    /// the run by eight and heightens its line by the same on each side.
    /// </summary>
    private static double RunBorderRoom(ParagraphBorderEdge? edge) =>
        edge is null ? 0 : BorderWeight(edge) + edge.SpacePoints;

    /// <summary>
    /// Opens a box for a paragraph, or carries on the one the paragraph before it opened, and
    /// takes the room the top of it needs out of the flow.
    /// </summary>
    private void OpenOrContinueBox(Cursor cursor, ResolvedParagraphFormat format)
    {
        // A paragraph with no box of its own closes whatever box stood before it.
        if (format.Borders is not { } borders)
        {
            CloseBox(cursor);
            return;
        }

        var left = cursor.Left + format.IndentLeftPoints -
                   ShadingBleedPoints - BorderReach(borders.Left);

        var right = cursor.Left + cursor.Width - format.IndentRightPoints +
                    ShadingBleedPoints + BorderReach(borders.Right);

        if (_openBox is { } open && open.Borders.SameAs(borders) &&
            ReferenceEquals(open.Page, cursor.Page) && open.Column == cursor.ColumnIndex &&
            Math.Abs(open.Left - left) < 0.001 && Math.Abs(open.Right - right) < 0.001)
        {
            // Carrying on: a line between them where the box asks for one, and nothing at all
            // where it does not — three paragraphs bordered alike run on in Word with no room
            // between them beyond their own lines.
            if (borders.Between is { } between)
            {
                open.Between.Add(Grid.Snap(cursor.Y));
                cursor.Y += BorderWeight(between) + Grid.Step;
            }

            return;
        }

        CloseBox(cursor);

        cursor.Y += BorderWeight(borders.Top);

        // Where the box is drawn is on the grid, as everything drawn is; what the flow advances by
        // stays exact, as everything in the flow is.
        var top = Grid.Snap(cursor.Y);

        _openBox = new BorderBox
        {
            Borders = borders,
            Page = cursor.Page,
            Column = cursor.ColumnIndex,
            Left = left,
            Right = right,
            Top = top,
            Bottom = top
        };

        // The step Word leaves between the top of the box and the text inside it.
        cursor.Y += Grid.Step + BorderReach(borders.Top);
    }

    /// <summary>
    /// Draws the box that was open, if any, and takes the room its foot needs out of the flow.
    /// </summary>
    private void CloseBox(Cursor cursor)
    {
        if (_openBox is not { } box) return;

        _openBox = null;

        // A box left behind on another page or column is still drawn, but the flow it belonged to
        // is not this one and is not moved.
        var ours = ReferenceEquals(box.Page, cursor.Page) && box.Column == cursor.ColumnIndex;

        var borders = box.Borders;
        var bottom = box.Bottom + BorderReach(borders.Bottom);

        var leftWeight = BorderWeight(borders.Left);
        var rightWeight = BorderWeight(borders.Right);
        var topWeight = BorderWeight(borders.Top);
        var bottomWeight = BorderWeight(borders.Bottom);

        // The background first: it fills what the box encloses rather than what the lines cover,
        // which is why a shaded paragraph inside a box reaches further than one without.
        for (var i = 0; i < box.Fills.Count; i++)
        {
            var (top, colour) = box.Fills[i];
            var foot = i + 1 < box.Fills.Count ? box.Fills[i + 1].Top : bottom;

            if (foot <= top) continue;

            box.Page.Rectangles.Add(new PositionedRectangle
            {
                X = box.Left, Y = top, Width = box.Right - box.Left, Height = foot - top,
                Color = colour
            });
        }

        void Bar(double x, double y, double width, double height, BorderEdge line)
        {
            if (width <= 0 || height <= 0) return;

            box.Page.Rectangles.Add(new PositionedRectangle
            {
                X = x, Y = y, Width = width, Height = height, Color = line.GetColor()
            });
        }

        // The sides stop at the box; the top and bottom run the whole way across it, corners and
        // all, which is the same ground Word covers with a bar and a square at each end.
        if (borders.Top is { } top2)
            Bar(box.Left - leftWeight, box.Top - topWeight,
                box.Right - box.Left + leftWeight + rightWeight, topWeight, top2.Line);

        if (borders.Bottom is { } foot2)
            Bar(box.Left - leftWeight, bottom,
                box.Right - box.Left + leftWeight + rightWeight, bottomWeight, foot2.Line);

        if (borders.Left is { } side)
            Bar(box.Left - leftWeight, box.Top, leftWeight, bottom - box.Top, side.Line);

        if (borders.Right is { } other)
            Bar(box.Right, box.Top, rightWeight, bottom - box.Top, other.Line);

        if (borders.Between is { } between)
        {
            foreach (var y in box.Between)
                Bar(box.Left, y, box.Right - box.Left, BorderWeight(between), between.Line);
        }

        if (ours) cursor.Y = bottom + bottomWeight;
    }

    /// <summary>
    /// Paints the background of the paragraph a line belongs to, behind that line.
    /// </summary>
    /// <remarks>
    /// One rectangle per line rather than one per paragraph: Word writes them that way, two lines
    /// of a paragraph coming out as two fills that meet, and so do two shaded paragraphs one after
    /// the other. What the fill covers vertically is the line box — the exact top and the exact
    /// foot, each put on the grid — so the fills of a column tile it with no seam between them and
    /// no overlap, which a height rounded on its own would not do.
    ///
    /// The first-line indent is left out of it deliberately: the probe's indented-first-line
    /// paragraph is shaded across the full measure, so it is the paragraph's indents that the fill
    /// follows and not the line's. A centred paragraph is shaded the same way, whatever its text
    /// does.
    /// </remarks>
    private static void ShadeLine(
        LaidOutPage page, ComposedLine line, double contentLeft, double contentWidth, double top)
    {
        if (line.Shading is not { } fill) return;

        var left = Grid.Snap(contentLeft + line.ShadeLeft - ShadingBleedPoints);
        var right = Grid.Snap(contentLeft + contentWidth - line.ShadeRight + ShadingBleedPoints);
        if (right <= left) return;

        page.Rectangles.Add(new PositionedRectangle
        {
            X = left,
            Y = Grid.Snap(top),
            Width = right - left,
            Height = Grid.Snap(top + line.Height) - Grid.Snap(top),
            Color = fill
        });
    }

    /// <summary>
    /// Word draws the rule between columns a hundredth of an inch and a bit wide, measured from
    /// its export of the <c>columns</c> fixture.
    /// </summary>
    private const double ColumnSeparatorWidthPoints = 0.96;

    /// <summary>
    /// Rules the gaps between the columns a page actually used.
    /// </summary>
    /// <remarks>
    /// The rule runs from the top of the content down to the bottom of the fullest column, not to
    /// the bottom margin: on a page whose columns are half empty Word stops the line where the
    /// text does. Gaps beyond the last column reached get no rule at all, which is why a page
    /// whose text never left the first column has none.
    /// </remarks>
    private static void DrawColumnSeparators(Cursor cursor)
    {
        if (!cursor.Section.ColumnSeparator || cursor.MaxColumnUsed < 1) return;

        var bottom = Math.Max(cursor.PageMaxY, cursor.Y);
        if (bottom <= cursor.ContentTop) return;

        for (var i = 0; i < cursor.MaxColumnUsed && i + 1 < cursor.Columns.Count; i++)
        {
            var gapLeft = cursor.SectionLeft + cursor.Columns[i].Left + cursor.Columns[i].Width;
            var gapRight = cursor.SectionLeft + cursor.Columns[i + 1].Left;
            var centre = (gapLeft + gapRight) / 2;

            cursor.Page.Rectangles.Add(new PositionedRectangle
            {
                X = centre - ColumnSeparatorWidthPoints / 2,
                Y = cursor.ContentTop,
                Width = ColumnSeparatorWidthPoints,
                Height = bottom - cursor.ContentTop,
                Color = (0, 0, 0)
            });
        }
    }

    /// <summary>Points the cursor, and the footnote area, at a section's geometry.</summary>
    private void ApplySection(Cursor cursor, SectionProperties section)
    {
        var contentTop = Units.TwipsToPoints(section.MarginTopTwips);

        // A section numbering its lines from its own beginning starts here. One that carries on
        // from the section before does not, whatever number it says to start at — which is what
        // Word does with it, and what line-number-probe's middle section shows.
        if (section.LineNumbers is { Restart: LineNumberRestart.NewSection } perSection)
            _nextLineNumber = perSection.Start;

        cursor.Section = section;
        cursor.SectionLeft = Units.TwipsToPoints(section.MarginLeftTwips + section.GutterTwips);
        cursor.SectionWidth = section.ContentWidthPoints;
        cursor.Columns = section.GetColumns();
        cursor.ColumnIndex = 0;
        cursor.MaxColumnUsed = 0;
        cursor.ApplyColumn();
        cursor.ContentTop = contentTop;
        cursor.ContentLimit = contentTop + section.ContentHeightPoints;

        // Where the notes go, and how often they begin their numbering again. Word reads this from the section and
        // from nowhere else: a document stating it in its settings, where the format has it as a
        // document-wide default, is numbered straight through regardless.
        _footnotePosition = section.FootnotePosition ?? NotePosition.PageBottom;
        _footnoteRestart = section.FootnoteNumberRestart ?? NoteNumberRestart.Continuous;
        _endnoteRestart = section.EndnoteNumberRestart ?? NoteNumberRestart.Continuous;

        // A note sits under the column that refers to it, so where it goes across the page is
        // read from that column when it is written rather than settled here. What is settled here
        // is how far down the page they reach, which is the same for every column of it.
        _footnoteBottom = cursor.ContentLimit;
    }

    /// <summary>
    /// Lays out a sequence of block-level elements at the cursor, advancing it.
    /// </summary>
    /// <remarks>
    /// Used for the document body and, with pagination disabled, for the contents of a table
    /// cell — a cell's height has to be known before its row can be placed, so it cannot be
    /// breaking pages while it is measured.
    /// </remarks>
    private void LayoutBlocks(Cursor cursor, IReadOnlyList<BlockElement> blocks)
    {
        for (var index = 0; index < blocks.Count; index++)
        {
            switch (blocks[index])
            {
                // A table of contents is not content the document wrote but a field's answer to
                // it, so it is worked out again rather than laid out — and what it produced last
                // time, which is the paragraphs that follow, is passed over.
                case Paragraph opening when Generated(cursor, opening) is { } entries:
                {
                    LayoutBlocks(cursor, entries);

                    // What the field produced last time follows it in the body, and is passed
                    // over: these entries stand for it now.
                    var last = opening;
                    while (index + 1 < blocks.Count &&
                           blocks[index + 1] is Paragraph { InsideField: true } inside)
                    {
                        last = inside;
                        index++;
                    }

                    // The paragraph the field closes in outlives it, empty. Word's own export
                    // shows the mark of it on a line of its own below the entries, set in the
                    // document's default rather than in a table-of-contents style.
                    LayoutBlocks(cursor, [new Paragraph { Properties = last.Properties }]);

                    break;
                }

                case Paragraph paragraph:
                    LayoutParagraph(cursor, paragraph, blocks, index);
                    break;

                case Table table:
                    CloseBox(cursor);
                    LayoutTable(cursor, table);
                    break;
            }
        }

        // Whatever box the last paragraph left open is drawn where the flow ends.
        CloseBox(cursor);
    }

    private void LayoutParagraph(
        Cursor cursor, Paragraph paragraph, IReadOnlyList<BlockElement> siblings, int index)
    {
        var format = _styles.ResolveParagraph(paragraph.Properties);

        // Where this paragraph began, kept in case a float coming after it reaches back over its
        // lines and they have to be broken again.
        var startedAt = (Page: cursor.Page, cursor.ColumnIndex, cursor.Y, Placed: cursor.ColumnLines.Count,
            Ordinal: _paragraphOrdinal, Number: _nextLineNumber,
            cursor.PendingSpaceAfter, cursor.PreviousFormat,
            Before: (cursor.Page.Lines.Count, cursor.Page.Rules.Count, cursor.Page.Images.Count,
                cursor.Page.Rectangles.Count, cursor.Floats.Count));

        var startedNewPage = false;
        if (format.PageBreakBefore && cursor.CanBreak)
        {
            cursor.BreakPage();
            startedNewPage = true;
        }

        // A dropped capital is a frame rather than a line of the paragraph flow: it takes no room
        // of its own and the paragraph after it makes room for it instead.
        if (format.Frame is { DropCap: not DropCapKind.None } frame)
        {
            LayoutDropCap(cursor, paragraph, format, frame);
            return;
        }

        // Contextual spacing suppresses spacing between paragraphs sharing a style, which is
        // what keeps list items tight.
        var spaceBefore = format.SpaceBeforePoints;
        if (cursor.PreviousFormat is not null &&
            format.ContextualSpacing &&
            cursor.PreviousFormat.StyleId == format.StyleId)
        {
            spaceBefore = 0;
        }

        // Word collapses adjacent paragraph spacing to the larger of the two rather than
        // adding them, the way CSS margins collapse. Verified against Word with the
        // paragraph-spacing-asymmetric fixture: with 12pt after and 24pt before it produces
        // 24pt, and with 24pt after and 12pt before it also produces 24pt — which is only
        // consistent with a maximum. Summing them put every paragraph after the first 12pt
        // too low.
        var stoodAt = cursor.Y;

        if (cursor.PreviousFormat is null)
        {
            cursor.Y += spaceBefore;
        }
        else if (startedNewPage)
        {
            // The collapse carries across a page break, but the previous paragraph's
            // space-after is absorbed by the page it ended on — it falls below the bottom
            // margin where nothing can show it — so only the excess appears at the top of
            // the new page. Verified against Word: with 12pt before, a paragraph opening a
            // page sits 12pt down when the previous paragraph had no space-after and flush
            // against the top margin when the previous paragraph had 12pt of it.
            cursor.Y += Math.Max(0, spaceBefore - cursor.PendingSpaceAfter);
        }
        else
        {
            cursor.Y += Math.Max(cursor.PendingSpaceAfter, spaceBefore);
        }

        // The box round the paragraph, which the one before it may already have opened. Closing
        // the old one takes the room its foot needs, so it happens before this paragraph's own
        // lines are placed and after the space between them has been made.
        OpenOrContinueBox(cursor, format);

        // Where this paragraph's own background begins inside the box: at the box's top if it
        // opened it, and where the paragraph before it ended if it did not.
        var boxFillTop = _openBox is { } opened
            ? opened.Fills.Count == 0 ? opened.Top : opened.Bottom
            : 0;

        // Anchored drawings are placed before the paragraph's own text is composed, so that its
        // very first line already flows around them.
        PlaceAnchoredDrawings(cursor, paragraph, cursor.Y - stoodAt);

        _pendingBookmarks.Clear();
        _pendingMarks.Clear();
        var composer = new ParagraphComposer(
            BuildAtoms(paragraph, format), format, TabSettings(), MarkMetrics(format),
            breakInsideWords: true, _hyphenation);
        var bookmarks = _pendingBookmarks.ToList();
        var marks = _pendingMarks.ToList();
        var firstLine = true;

        var ordinal = _paragraphOrdinal++;
        var emitted = 0;

        while (composer.HasMore)
        {
            // A break carried over from the previous line is applied before this one is composed,
            // not after: a column break changes the measure, and a line broken against the width
            // it was leaving would be wrapped in the wrong place.
            if (composer.PendingPageBreak && cursor.CanBreak) cursor.BreakPage();
            else if (composer.PendingColumnBreak && cursor.CanAdvance) cursor.AdvanceColumn();

            var stood = composer.Mark;
            var bands = ResolveBandsForLine(cursor, composer.ProvisionalHeight);
            var line = composer.Next(bands);

            // The paragraph a section break is written on takes no number and does not advance
            // the count: line-number-probe's middle section carries on from six to seven across a
            // break whose own paragraph is empty, where counting it would have made that eight.
            // Whether Word lays the paragraph out at all is another question, and one its export
            // cannot answer — it falls at the foot of a page, where a line nobody can see and a
            // line that is not there look alike.
            if (paragraph.SectionBreak is not null && line.Segments.Count == 0) line.SuppressNumber = true;

            // A footnote goes to the foot of the page its reference lands on, so its space has to
            // come out of the page before the line that refers to it is fitted into what is left.
            var footnoteIds = FootnotesOn(line);
            if (cursor.FootnoteSink is not null) cursor.FootnoteSink.AddRange(footnoteIds);

            var footnotes = cursor.FootnoteSink is null && footnoteIds.Count > 0
                ? PrepareFootnotes(cursor, footnoteIds)
                : Prepared.None;

            // A line that does not fit moves to the next column, or off the page when it was in
            // the last of them — and may take the lines above it along, so that a paragraph is
            // never split with only one of its lines on either side of the break.
            // The notes a line refers to are not part of what has to fit: Word never moves a line
            // to make room for its own note. What room is left under it is what the note gets,
            // which may be none of it at all — footnote-carry-probe has a reference on the last
            // line a page holds and Word keeps it there, squeezing the whole note in beneath it,
            // and footnote-beneath-text has one with no room left at all and Word still keeps the
            // line, carrying the whole note over instead.
            if (cursor.Paginate && cursor.Y + line.Height > cursor.ContentBottom && cursor.CanAdvance)
            {
                var pull = PullBackForBreak(format, cursor, ordinal, isLastLine: !composer.HasMore);

                // Whether this paragraph's own first line is among the lines going with it.
                var takesTheOpening = pull > 0 && emitted > 0 && CountOwnedBy(cursor, ordinal) >= emitted;

                var pulled = pull > 0 ? UnplaceLines(cursor, pull) : [];

                cursor.AdvanceColumn();
                RePlaceLines(cursor, pulled);

                // The measure here is not always the measure the line was broken against: a float
                // on the page just left behind narrowed it and there may be none here, or one in
                // another place. Word breaks such a line again rather than carrying its old shape
                // over — floating-table-break-probe has a line composed beside a table at the foot
                // of one page and set at the full measure at the head of the next.
                var moved = ResolveBandsForLine(cursor, composer.ProvisionalHeight);

                if (!SameBands(bands, moved))
                {
                    composer.Rewind(stood);
                    line = composer.Next(moved);
                    bands = moved;

                    if (paragraph.SectionBreak is not null && line.Segments.Count == 0)
                        line.SuppressNumber = true;

                    footnoteIds = FootnotesOn(line);
                    if (cursor.FootnoteSink is not null) cursor.FootnoteSink.AddRange(footnoteIds);
                }

                // A pulled-back first line takes its paragraph's bookmarks with it.
                if (takesTheOpening) firstLine = true;

                // The new page carries no separator yet, so what the notes cost is not what they
                // cost on the page just left behind.
                if (footnotes.Flows.Count > 0) footnotes = PrepareFootnotes(cursor, footnoteIds);
            }

            if (firstLine && _result is not null)
            {
                // A detached flow composes onto a scratch page that is not part of the document
                // yet, so its index is unknown here. Rather than record a destination that would
                // point at the wrong place, the bookmark is left unrecorded and the link that
                // wanted it simply does not become clickable — and a paragraph composed there is
                // not one a running head can pick up either.
                var pageIndex = _result.Pages.IndexOf(cursor.Page);
                if (pageIndex >= 0)
                {
                    foreach (var name in bookmarks)
                        _result.Bookmarks[name] = new BookmarkDestination(pageIndex, cursor.Left, cursor.Y);

                    RecordStyledParagraph(ordinal, pageIndex, paragraph);

                    // A heading is where a table of contents points, so where it landed is worth
                    // knowing whether or not the document has a table in it yet.
                    if (format.OutlineLevel is not null) _headingPages[paragraph] = cursor.Page;

                    // And a heading is what the PDF's navigation pane lists (#66), recorded with
                    // its level and text at the line's own place on the page. w:outlineLvl 9 is
                    // the schema's "none", and a paragraph inside a field - a generated table of
                    // contents entry, say - is a pointer to a heading rather than one itself.
                    if (format.OutlineLevel is { } outline && outline < 9 && !paragraph.InsideField)
                    {
                        _result.Headings.Add(new OutlineHeading(
                            outline, TextOf(paragraph), pageIndex, cursor.Left, cursor.Y));
                    }

                    foreach (var mark in marks) _markPages[mark] = cursor.Page;
                }

                firstLine = false;
            }

            Place(cursor, line, index, ordinal, format.KeepNext, footnoteIds, footnotes);
            emitted++;
        }

        if (format.Borders is not null && _openBox is { } grown &&
            format.Shading.Resolve() is { } fill)
        {
            grown.Fills.Add((boxFillTop, fill));
        }

        var spaceAfter = format.SpaceAfterPoints;
        if (format.ContextualSpacing &&
            index + 1 < siblings.Count &&
            siblings[index + 1] is Paragraph next &&
            _styles.ResolveParagraph(next.Properties).StyleId == format.StyleId)
        {
            spaceAfter = 0;
        }

        // Held rather than added, so the next paragraph can collapse it against its own
        // space-before.
        cursor.PendingSpaceAfter = spaceAfter;
        cursor.PreviousFormat = format;

        // Whether this paragraph could be laid again where it stands. It could not if it broke
        // across a page or a column on the way: its lines are no longer one run at the end of one
        // column, and what it put on the page it left behind is not this page's to take off.
        var placed = cursor.ColumnLines.Count - startedAt.Placed;

        _reflowable = placed > 0 &&
                      ReferenceEquals(cursor.Page, startedAt.Page) &&
                      cursor.ColumnIndex == startedAt.ColumnIndex &&
                      cursor.ColumnLines.Count >= placed
            ? new ReflowableParagraph(
                paragraph, siblings, index, cursor.Page, cursor.ColumnIndex, placed, startedAt.Y,
                startedAt.Ordinal, startedAt.Number, startedAt.PendingSpaceAfter,
                startedAt.PreviousFormat, startedAt.Before)
            : null;
    }

    /// <summary>
    /// Breaks the lines a float's clearance reaches back over again, with the float in place.
    /// </summary>
    /// <remarks>
    /// A float is not known until the flow reaches the paragraph it is anchored to, and by then
    /// the lines above it have been written. Where its clearance reaches back over them Word
    /// breaks them again round it — the second page of floating-table-wrap-probe has a table with
    /// half an inch of daylight above it, and the line already written above the table is set
    /// beside the table rather than across it. So that line is taken off the page here and the
    /// paragraph laid again from where it began, with the room the float wants already spoken for.
    ///
    /// Only the paragraph immediately before is offered this, and only when it can be laid twice
    /// — see <see cref="ReflowableParagraph"/>. That is the case Word's own behaviour shows up in
    /// and the one a document is likely to have; a clearance deep enough to reach back over two
    /// paragraphs leaves the further one alone.
    ///
    /// The float is given the room above the flow only. What it takes below is registered by
    /// whoever placed it, once it knows how far down it reaches.
    /// </remarks>
    private void ReflowLinesReachedBy(Cursor cursor, FloatRegion reach, double room = 0)
    {
        if (_reflowable is not { } previous) return;

        // Nothing to do where the clearance stops at the flow's own position, which is the usual
        // case: a float takes its room from the lines that come after it.
        if (reach.Top >= cursor.Y - 0.001) return;

        // Nor where it stands clear of the text altogether — a picture out in the margin.
        if (reach.Right <= cursor.Left + 0.001 || reach.Left >= cursor.Left + cursor.Width - 0.001) return;

        if (!ReferenceEquals(previous.Page, cursor.Page) || previous.Column != cursor.ColumnIndex) return;
        if (previous.Lines > cursor.ColumnLines.Count) return;

        // Off the page, and the flow back to where the paragraph began. Its lines go first, which
        // gives the page back the room it set aside for their notes; then everything else the
        // paragraph put there, which is what it anchored before its first line was written.
        UnplaceLines(cursor, previous.Lines);
        cursor.ColumnLines.RemoveRange(cursor.ColumnLines.Count - previous.Lines, previous.Lines);
        cursor.PagePlaced.RemoveRange(cursor.PagePlaced.Count - previous.Lines, previous.Lines);

        Trim(cursor.Page.Lines, previous.Before.Lines);
        Trim(cursor.Page.Rules, previous.Before.Rules);
        Trim(cursor.Page.Images, previous.Before.Images);
        Trim(cursor.Page.Rectangles, previous.Before.Rectangles);
        Trim(cursor.Floats, previous.Before.Floats);

        static void Trim<T>(List<T> list, int count)
        {
            if (list.Count > count) list.RemoveRange(count, list.Count - count);
        }

        _paragraphOrdinal = previous.Ordinal;
        _nextLineNumber = previous.NextLineNumber;
        cursor.Y = previous.Top;
        cursor.PendingSpaceAfter = previous.PendingSpaceAfter;
        cursor.PreviousFormat = previous.PreviousFormat;

        // The room the float wants, so that the lines being written again make way for it. It is
        // taken back out afterwards: whoever placed the float registers it properly, and a float
        // counted twice would be no wider but would cost every line a second look.
        cursor.Floats.Add(reach);

        // Not offered twice: a paragraph laid again is not laid a third time for the same float.
        _reflowable = null;
        LayoutParagraph(cursor, previous.Paragraph, previous.Siblings, previous.Index);

        // The flow is back at that paragraph's foot with its space-after pending again, but the
        // paragraph holding the float has already made the room between the two. It is made again
        // here rather than left to be made twice or not at all: brochure, whose text box has nine
        // points of clearance over a picture paragraph six points above it, put every line of the
        // paragraph six points high without this.
        cursor.Y += room;

        cursor.Floats.Remove(reach);
    }

    // ----- widow and orphan control -----

    /// <summary>
    /// The rest of a floating table, to be laid at the top of the next page.
    /// </summary>
    /// <param name="From">The first row still to be laid.</param>
    private sealed record CarriedTable(
        Table Table,
        List<double> Columns,
        TablePosition Position,
        double Left,
        double Width,
        (double Left, double Top, double Right, double Bottom) Edges,
        int From);

    /// <summary>
    /// A paragraph that has been laid out and could be laid again from where it began.
    /// </summary>
    /// <param name="Lines">How many lines it placed, all of them in one column of one page.</param>
    /// <param name="Top">Where the cursor stood before it, ahead of any space before it.</param>
    /// <param name="Page">
    /// How much of each of the page's lists, and of the floats, belonged to it before the
    /// paragraph began. A paragraph anchors its pictures before its first line, so undoing the
    /// lines is not enough to undo the paragraph: these are.
    /// </param>
    private sealed record ReflowableParagraph(
        Paragraph Paragraph,
        IReadOnlyList<BlockElement> Siblings,
        int Index,
        LaidOutPage Page,
        int Column,
        int Lines,
        double Top,
        int Ordinal,
        int NextLineNumber,
        double PendingSpaceAfter,
        ResolvedParagraphFormat? PreviousFormat,
        (int Lines, int Rules, int Images, int Rectangles, int Floats) Before);

    /// <summary>
    /// A line of the paragraph being laid out, and everything placing it added to the page, so
    /// that it can be taken back off again.
    /// </summary>
    /// <param name="Top">Where the line started, which is where the cursor returns to.</param>
    /// <param name="LineIndex">
    /// How much of each of the page's lists belonged to the page before this line was placed.
    /// Placing only ever appends, so these are all that is needed to undo it.
    /// </param>
    private readonly record struct PlacedLine(
        ComposedLine Line,
        double Top,
        int LineIndex,
        int RuleIndex,
        int ImageIndex,
        int RectangleIndex,
        IReadOnlyList<int> FootnoteIds,
        int FootnoteCount,
        double FootnoteHeight,
        int ParagraphOrdinal,
        int ParagraphIndex,
        bool KeepNext);

    /// <summary>Puts a composed line on the page and records what that took.</summary>
    private void Place(
        Cursor cursor, ComposedLine line, int paragraphIndex, int ordinal, bool keepNext,
        IReadOnlyList<int> footnoteIds, Prepared footnotes)
    {
        var placed = new PlacedLine(
            line, cursor.Y,
            cursor.Page.Lines.Count, cursor.Page.Rules.Count, cursor.Page.Images.Count,
            cursor.Page.Rectangles.Count,
            footnoteIds, footnotes.Flows.Count, footnotes.Height,
            ordinal, paragraphIndex, keepNext);

        cursor.ColumnLines.Add(placed);
        cursor.PagePlaced.Add((cursor.ColumnIndex, _sectionOrdinal, placed));

        RecordFieldPages(cursor.Page, line);

        // A paragraph inside a box is shaded by the box, which reaches further than a line does.
        if (_openBox is null) ShadeLine(cursor.Page, line, cursor.Left, cursor.Width, cursor.Y);

        var baseline = EmitLine(cursor.Page, line, cursor.Left, cursor.Y, paragraphIndex, TabSettings());
        NumberLine(cursor, line, baseline);

        CommitFootnotes(cursor, footnotes, line.Height);
        cursor.Y += line.Height;

        // The box round the paragraph reaches to the foot of its last line, which is where the
        // line was drawn to rather than where the arithmetic left the flow.
        if (_openBox is { } box) box.Bottom = Grid.Snap(cursor.Y);
    }


    /// <summary>
    /// Places a table that floats: it stands where <c>w:tblpPr</c> puts it, takes no room in the
    /// flow, and the lines it reaches make way for it.
    /// </summary>
    /// <remarks>
    /// Measured from floating-table-probe, eight pages of one export:
    ///
    ///   * The place names the cell's own text edge, not the table's edge — the same rule a
    ///     declared indent follows. A table put at the margin has its first column's text on the
    ///     margin and its border hanging outside it, and the probe shows the border growing
    ///     outward from there when it is thickened from half a point to three.
    ///   * <c>tblpXSpec</c> names a place instead of measuring one: left, centre and right of
    ///     whatever the anchor is.
    ///   * Anchored to the text, the place is measured from where the table would have stood had
    ///     it not been floating — the flow's own position, which is why a table written among
    ///     paragraphs lands beside the ones that follow it rather than the ones above.
    ///   * The daylight is part of what the text keeps away from, and nothing else: the table is
    ///     drawn at its place whatever the distances say.
    ///
    /// A floating table is not broken across pages here. Word moves one that will not fit rather
    /// than splitting it, and a probe for that is work still to do.
    /// </remarks>
    private void LayoutFloatingTable(
        Cursor cursor, Table table, TablePosition position, List<double> columns)
    {
        var width = columns.Sum();
        var inset = LeadingCellInset(table, columns.Count);

        var left = position.HorizontalAnchor switch
        {
            TableAnchor.Page => 0.0,
            TableAnchor.Margin => cursor.SectionLeft,
            _ => cursor.Left
        };

        // The width the named places are measured across: the text's own for the margin and for
        // the text, the whole paper for the page.
        var measure = position.HorizontalAnchor == TableAnchor.Page
            ? cursor.Page.WidthPoints
            : cursor.SectionWidth;

        // Where the table's own box begins. A place stated is measured to the cell's text edge,
        // so the box hangs its border and margin outside it; a place named puts the same text box
        // against the same place at the other end, which for the middle is the box itself since
        // the two ends hang out equally.
        var boxLeft = position.XSpec switch
        {
            TableAlignSpec.Center => left + Math.Max(0, measure - width) / 2,
            TableAlignSpec.Right => left + measure + inset - width,
            _ => left + position.XPoints - inset
        };

        var edges = OuterBorderHalves(table, columns.Count);

        double Top() => position.VerticalAnchor switch
        {
            TableAnchor.Page => position.YPoints,
            TableAnchor.Margin => cursor.ContentTop + position.YPoints,
            _ => cursor.Y + position.YPoints
        };

        // How much of the page belonged to it before the table went on, so that the table can be
        // taken off again: everything below only ever appends.
        var before = (cursor.Page.Lines.Count, cursor.Page.Rules.Count, cursor.Page.Images.Count,
            cursor.Page.Rectangles.Count, cursor.Floats.Count);

        // The paragraph before the table, kept across the table's own layout: the paragraphs
        // inside its cells are laid out too, and the last of those would otherwise be the one
        // offered for breaking again.
        var above = _reflowable;
        var stood = Top();
        var region = PlaceRows(stood);
        _reflowable = above;

        // A table anchored to the paper does not break, so one that runs off the foot of it is
        // moved up until it ends at the paper's own edge — bottom margin and all. Word's own: the
        // probe puts a table a foot down the page whose height carries it past the edge, and Word
        // draws it 28 points higher, ending exactly at 792.
        if (position.VerticalAnchor == TableAnchor.Page && _tableBottom > cursor.Page.HeightPoints)
        {
            var lifted = Math.Max(0, stood - (_tableBottom - cursor.Page.HeightPoints));

            Undo();
            region = PlaceRows(lifted);
        }

        // A table whose daylight reaches back over lines already written has those lines broken
        // again round it. The table itself comes off the page while that happens — it is anchored
        // to the text, so it stands wherever the flow ends up — and goes back on afterwards, at
        // whatever place the flow has reached by then.
        if (region.Top < cursor.Y - 0.001 && _reflowable is not null && _carriedTable is null)
        {
            Undo();
            ReflowLinesReachedBy(cursor, region);

            // Back where it stood, not where the flow has got to: breaking those lines again can
            // lengthen the paragraph they belong to, and the table does not follow it down. Word's
            // own export says so — the lines above its table are broken round a table standing
            // where the flow first reached.
            _reflowable = null;
            PlaceRows(stood);
        }

        // A table that broke takes the rest of the page with it: Word writes nothing beside the
        // part that stayed, and the text that follows the table begins on the page the rest of it
        // carries on to. So the flow is put below what was laid rather than back where it was —
        // which leaves it at the foot of the page, and the next line breaks to the next page.
        if (_carriedTable is not null) cursor.Y = Math.Max(cursor.Y, _tableBottom);

        return;

        // Takes the table back off the page, leaving it as it was before any of it was laid.
        void Undo()
        {
            cursor.Page.Lines.RemoveRange(before.Item1, cursor.Page.Lines.Count - before.Item1);
            cursor.Page.Rules.RemoveRange(before.Item2, cursor.Page.Rules.Count - before.Item2);
            cursor.Page.Images.RemoveRange(before.Item3, cursor.Page.Images.Count - before.Item3);
            cursor.Page.Rectangles.RemoveRange(before.Item4, cursor.Page.Rectangles.Count - before.Item4);
            cursor.Floats.RemoveRange(before.Item5, cursor.Floats.Count - before.Item5);
            _carriedTable = null;
        }

        // Lays the table's rows at the place given, from the row given, and registers the room it
        // takes. The flow is put back exactly as it was afterwards: a float takes none of it.
        FloatRegion PlaceRows(double top, int from = 0)
        {
            var savedLeft = cursor.Left;
            var savedWidth = cursor.Width;
            var savedY = cursor.Y;
            var savedPaginate = cursor.Paginate;
            var savedSpaceAfter = cursor.PendingSpaceAfter;
            var savedFormat = cursor.PreviousFormat;

            cursor.Left = boxLeft;
            cursor.Width = width;
            cursor.Y = top;
            cursor.PendingSpaceAfter = 0;

            // Nothing here may break the page: the flow's own place on it is being borrowed.
            cursor.Paginate = false;

            // A table anchored to the text breaks at the foot of the page and carries on at the
            // top of the next, which is what Word does with one. A table anchored to the paper
            // does not: floating-table-break-probe puts one a foot down a page whose text fills
            // it, and Word runs the table on past the bottom margin to the paper's own edge.
            var breaks = position.VerticalAnchor != TableAnchor.Page;
            var next = LayoutTableRows(cursor, table, columns, cursor.Left, from, breaks);

            var bottom = cursor.Y;
            _tableBottom = bottom;

            _carriedTable = next < table.Rows.Count
                ? new CarriedTable(table, columns, position, boxLeft, width, edges, next)
                : null;

            cursor.Left = savedLeft;
            cursor.Width = savedWidth;
            cursor.Y = savedY;
            cursor.Paginate = savedPaginate;
            cursor.PendingSpaceAfter = savedSpaceAfter;
            cursor.PreviousFormat = savedFormat;

            // The daylight is measured from the outside of the line the table is drawn with rather
            // than from the box the rows sit in — sideways and below, at least. Above it is
            // measured from the place itself, because that is where Word's own outer edge falls:
            // Word draws the line inside the table's box where this straddles the edge with it,
            // which is why the probe's thick border reaches a step and a half higher here than in
            // Word's own export and why the text inside it stands in the same place all the same.
            var taken = new FloatRegion(
                boxLeft - edges.Left - position.LeftFromTextPoints,
                top - position.TopFromTextPoints,
                boxLeft + width + edges.Right + position.RightFromTextPoints,
                bottom + edges.Bottom + position.BottomFromTextPoints);

            cursor.Floats.Add(taken);
            return taken;
        }
    }

    /// <summary>
    /// Lays what is left of a floating table that ran off the foot of the page before, at the top
    /// of the page just started.
    /// </summary>
    /// <remarks>
    /// Word breaks a floating table at a row and carries the rest over rather than moving the
    /// whole of it: floating-table-break-probe has twenty rows where six of them fit, and Word
    /// writes six at the foot of one page and fourteen at the head of the next, in the same place
    /// across the measure and with the text beside them shortened on both pages. Sixty rows come
    /// out forty and twenty, which is the same rule twice over.
    ///
    /// The rest begins at the top margin, whatever the table's own place said: what put the table
    /// where it stands belongs to the page it began on.
    /// </remarks>
    private void ResumeCarriedTable(Cursor cursor)
    {
        if (_carriedTable is not { } carried) return;
        _carriedTable = null;

        var savedLeft = cursor.Left;
        var savedWidth = cursor.Width;
        var savedY = cursor.Y;
        var savedPaginate = cursor.Paginate;
        var savedSpaceAfter = cursor.PendingSpaceAfter;
        var savedFormat = cursor.PreviousFormat;

        cursor.Left = carried.Left;
        cursor.Width = carried.Width;
        cursor.Y = cursor.ContentTop;
        cursor.PendingSpaceAfter = 0;
        cursor.Paginate = false;

        var next = LayoutTableRows(
            cursor, carried.Table, carried.Columns, carried.Left, carried.From, floating: true);

        var bottom = cursor.Y;

        cursor.Left = savedLeft;
        cursor.Width = savedWidth;
        cursor.Y = savedY;
        cursor.Paginate = savedPaginate;
        cursor.PendingSpaceAfter = savedSpaceAfter;
        cursor.PreviousFormat = savedFormat;

        cursor.Floats.Add(new FloatRegion(
            carried.Left - carried.Edges.Left - carried.Position.LeftFromTextPoints,
            cursor.ContentTop,
            carried.Left + carried.Width + carried.Edges.Right + carried.Position.RightFromTextPoints,
            bottom + carried.Edges.Bottom + carried.Position.BottomFromTextPoints));

        // Still more of it than this page can hold: the rest goes on to the next.
        if (next < carried.Table.Rows.Count) _carriedTable = carried with { From = next };
    }

    /// <summary>
    /// Places a dropped capital: one letter set large, standing beside the lines that follow it
    /// rather than above them.
    /// </summary>
    /// <remarks>
    /// All of it measured from drop-cap-probe, whose caps are written the way Word writes them —
    /// its own AppleScript was asked for a dropped capital and this is the markup it produced:
    ///
    ///   * The frame is as wide as the letter's advance at the size the run states, plus
    ///     w:hSpace, rounded to the grid. Word's own: a fifty-six point T is 34.2167 wide and the
    ///     lines beside it begin 34.32 in, and with 180 twips of space they begin 30.48 in from
    ///     a thirty-five point one measuring 21.3823.
    ///   * The letter sits in a paragraph of its own with the height Word worked out written on
    ///     it as exact spacing, and the drop written on the run as w:position. Both are honoured
    ///     as written: nothing here works out how tall a cap of three lines should be, because
    ///     nothing in the document asks it to.
    ///   * Which lines make room for it follows from the frame's height and nothing else — it is
    ///     the lines the frame reaches, whether they belong to one paragraph or two, and w:lines
    ///     has no say in it.
    ///   * A cap in the margin hangs its own width to the left of the text, which then keeps the
    ///     whole measure. That falls out of the frame standing outside the text's box.
    /// </remarks>
    private void LayoutDropCap(
        Cursor cursor, Paragraph paragraph, ResolvedParagraphFormat format, FrameProperties frame)
    {
        _pendingBookmarks.Clear();
        _pendingMarks.Clear();

        var composer = new ParagraphComposer(
            BuildAtoms(paragraph, format), format, TabSettings(), MarkMetrics(format));

        if (!composer.HasMore) return;

        // A measure wide enough that the letter is never broken across lines: a frame holds one
        // letter, and the paragraph mark after it is not to take a line of its own.
        var line = composer.Next([(0, cursor.Width * 4)]);
        if (line.Segments.Count == 0) return;

        var text = line.Segments.Max(segment => segment.X + segment.Width);
        var width = Grid.Snap(text + frame.HorizontalSpacePoints);
        var left = frame.DropCap == DropCapKind.Margin ? cursor.Left - width : cursor.Left;

        EmitLine(cursor.Page, line, left, cursor.Y, ParagraphIndexNone, TabSettings());

        // The room the letter takes is what the lines after it flow around. A cap in the margin
        // registers its room too, which lies outside the text's box and so shortens nothing.
        cursor.Floats.Add(new FloatRegion(left, cursor.Y, left + width, cursor.Y + line.Height));
    }

    /// <summary>
    /// Writes the line's number down the margin, where the section asks for numbering.
    /// </summary>
    /// <remarks>
    /// What line-number-probe shows Word doing, and all of it measured there rather than read off
    /// the format:
    ///
    ///   * Every line of the body is counted, an empty paragraph among them.
    ///   * A paragraph that asks to be passed over is neither numbered nor counted, so the line
    ///     after two of them carries the number the first of them would have had.
    ///   * Only numbers that divide by the count are written: five means 10 and 15 are written and
    ///     the eight lines between them are not.
    ///   * The number is written right against a place the stated distance in from the text —
    ///     eighteen points where the section states none — so that tens reach further left than
    ///     units do.
    ///   * It is set in the document's own face rather than the paragraph's: eleven point Calibri
    ///     beside twelve point Times, on the same baseline.
    ///   * Where the count begins again is the section's business, and a section that says nothing
    ///     begins again on every page. A section counting on from the one before ignores whatever
    ///     number it says to start at, having nowhere to start.
    /// </remarks>
    private void NumberLine(Cursor cursor, ComposedLine line, double baseline)
    {
        if (cursor.Section.LineNumbers is not { } numbering || line.SuppressNumber) return;
        if (!cursor.Paginate) return;

        var number = _nextLineNumber;
        _nextLineNumber++;

        if (number % numbering.CountBy != 0) return;

        // The document's own face, at the size Word draws a size at: eleven point is written and
        // measured as 11.04, which is eleven rounded to the grid. Everything else here is measured
        // at the size a run states and written at the same, since a flowed run's advances have to
        // agree with the size they were measured at; a number written on its own has no such tie,
        // and Word's own is 11.04 wide for 11.04 of type.
        var defaults = _styles.ResolveRun(null, null);
        var size = Grid.Snap(defaults.EffectiveFontSizePoints);
        var format = defaults with { FontSizePoints = size };

        var selection = _fonts.Resolve(format.FontFamily, format.Bold, format.Italic);
        var text = number.ToString(CultureInfo.InvariantCulture);

        // Each figure takes a whole number of steps of the grid, and the number is set right
        // against its place by the sum of those rather than by its true width. Word's own say so:
        // a single figure stands at 48.48 and two at 42.96, which is 5.52 apart where the figure
        // itself is 5.597 wide and 5.52 is what that rounds to. It follows that the number always
        // lands on the grid, which Word's always do.
        var width = text.Sum(figure =>
            Grid.Snap(TextMeasurer.Measure(selection.Font, figure.ToString(), size)));

        cursor.Page.Lines.Add(new LaidOutLine
        {
            BaselineY = baseline,
            Height = 0,
            Ascent = 0,
            ParagraphIndex = ParagraphIndexNone,
            Texts =
            {
                new PositionedText
                {
                    // Right against the place the distance names, so the numbers line up under
                    // one another however many figures they have.
                    X = cursor.SectionLeft - numbering.Distance - width,
                    BaselineY = baseline,
                    Text = text,
                    Format = format,
                    Font = selection,
                    Width = width
                }
            }
        });
    }

    /// <summary>
    /// Whether a run's text is kerned. Word does not kern unless the document asks it to, with a
    /// type size at or above which to do it, and the option here forces it on regardless.
    /// </summary>
    private bool Kerned(ResolvedRunFormat format) => _options.ApplyKerning || format.Kerned;

    /// <summary>What the tab stops in this document align against.</summary>
    private TabOptions TabSettings() => new(_options.ApplyKerning, _decimalSymbol);

    /// <summary>
    /// The line box of a paragraph's own mark, which is what sizes a line with nothing on it.
    /// </summary>
    private (double Ascent, double Height) MarkMetrics(ResolvedParagraphFormat format)
    {
        var mark = format.MarkFormat;
        var selection = _fonts.Resolve(mark.FontFamily, mark.Bold, mark.Italic);
        var size = mark.LineBoxFontSizePoints;

        return (TextMeasurer.GetAscent(selection.Font, size),
            TextMeasurer.GetNaturalLineHeight(selection.Font, size));
    }

    /// <summary>How many of the column's trailing lines belong to one paragraph.</summary>
    private static int CountOwnedBy(Cursor cursor, int ordinal)
    {
        var lines = cursor.ColumnLines;
        var count = 0;

        while (count < lines.Count && lines[^(count + 1)].ParagraphOrdinal == ordinal) count++;

        return count;
    }

    /// <summary>
    /// How many of the lines already in this column must follow the break rather than staying
    /// above it.
    /// </summary>
    /// <remarks>
    /// Three rules meet here, all of them about what may not be separated.
    ///
    /// Widow and orphan control wants two lines of a paragraph on each side of a break: one left
    /// at the foot of a column is an orphan, one carried alone to the next is a widow, and Word
    /// will have neither. A paragraph of three lines cannot satisfy both at once, so all of it
    /// moves. <c>w:keepLines</c> says the paragraph is never split at all.
    ///
    /// <c>w:keepNext</c> reaches further back: it keeps a paragraph with the one that follows, so
    /// when a paragraph moves, anything kept with it moves too, and anything kept with that. It
    /// only applies when the whole of the following paragraph is moving — a paragraph that keeps
    /// some of its lines above the break is still next to the one before it.
    ///
    /// Nothing is ever pushed off a column it already starts. There would be nothing above it to
    /// gain by moving, and the next column is no roomier, so the paragraph would march across the
    /// page never fitting anywhere. Where the full chain cannot be moved for that reason, the
    /// smaller move that satisfies the paragraph's own rules is tried instead.
    /// </remarks>
    private static int PullBackForBreak(
        ResolvedParagraphFormat format, Cursor cursor, int ordinal, bool isLastLine)
    {
        var lines = cursor.ColumnLines;
        if (lines.Count == 0) return 0;

        // How many of the lines at the end of the column belong to the paragraph being laid out.
        // None, when the break falls before its very first line.
        var own = 0;
        while (own < lines.Count && lines[^(own + 1)].ParagraphOrdinal == ordinal) own++;

        var pull = own == 0 ? 0
            : format.KeepLines ? own
            : !format.WidowControl ? 0
            : own switch
            {
                1 => 1,
                2 when isLastLine => 2,
                _ when isLastLine => 1,
                _ => 0
            };

        var withoutChain = pull;

        // Anything kept with what is moving comes along, and so does anything kept with that.
        if (own == 0 || pull >= own)
        {
            while (pull < lines.Count && lines[^(pull + 1)].KeepNext)
            {
                var previous = lines[^(pull + 1)].ParagraphOrdinal;
                while (pull < lines.Count && lines[^(pull + 1)].ParagraphOrdinal == previous) pull++;
            }
        }

        if (pull > 0 && lines[^pull].Top <= cursor.ContentTop + 0.001) pull = withoutChain;
        if (pull == 0 || lines[^pull].Top <= cursor.ContentTop + 0.001) return 0;

        return pull;
    }

    /// <summary>
    /// Takes the last few lines back off the page, undoing everything placing them did, and
    /// returns them in the order they were placed so they can go down again elsewhere.
    /// </summary>
    private List<PlacedLine> UnplaceLines(Cursor cursor, int count)
    {
        var placed = cursor.ColumnLines;
        var pulled = placed.GetRange(placed.Count - count, count);
        var first = pulled[0];
        var page = cursor.Page;

        page.Lines.RemoveRange(first.LineIndex, page.Lines.Count - first.LineIndex);
        page.Rules.RemoveRange(first.RuleIndex, page.Rules.Count - first.RuleIndex);
        page.Images.RemoveRange(first.ImageIndex, page.Images.Count - first.ImageIndex);

        // What the lines painted goes with them: a paragraph's background, a highlight behind a
        // run, the bar of a tab stop, the box of a form field. A line taken off the page and laid
        // again on the next would otherwise leave its fill behind on the page it left.
        page.Rectangles.RemoveRange(
            first.RectangleIndex, page.Rectangles.Count - first.RectangleIndex);

        // The notes these lines referred to belong to wherever the lines end up, so the page gets
        // back both them and the space it set aside for them.
        var flows = 0;
        var height = 0.0;
        foreach (var line in pulled)
        {
            flows += line.FootnoteCount;
            height += line.FootnoteHeight;
        }

        if (flows > 0)
        {
            var column = FootnotesOfColumn(cursor.ColumnIndex);
            column.RemoveRange(column.Count - flows, flows);
            cursor.Reserved -= height;
        }

        cursor.Y = first.Top;
        return pulled;
    }

    /// <summary>
    /// Puts pulled-back lines down again where the cursor now stands, keeping the gaps that were
    /// between them — the spacing between two paragraphs that moved together is part of what
    /// moved.
    /// </summary>
    private void RePlaceLines(Cursor cursor, List<PlacedLine> pulled)
    {
        if (pulled.Count == 0) return;

        var origin = pulled[0].Top;
        var top = cursor.Y;

        foreach (var line in pulled)
        {
            cursor.Y = top + (line.Top - origin);

            var footnotes = line.FootnoteIds.Count > 0 && cursor.FootnoteSink is null
                ? PrepareFootnotes(cursor, line.FootnoteIds)
                : Prepared.None;

            Place(cursor, line.Line, line.ParagraphIndex, line.ParagraphOrdinal, line.KeepNext,
                line.FootnoteIds, footnotes);
        }
    }

    /// <summary>Whether two sets of free bands are the same measure.</summary>
    private static bool SameBands(
        List<(double Left, double Width)> first, List<(double Left, double Width)> second)
    {
        if (first.Count != second.Count) return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (Math.Abs(first[i].Left - second[i].Left) > 0.001 ||
                Math.Abs(first[i].Width - second[i].Width) > 0.001)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Finds the free horizontal bands for the next line, moving down past any float that blocks
    /// the full measure.
    /// </summary>
    /// <remarks>
    /// More than one where a float has room on both sides of it. They come back in the order they
    /// stand across the page, and the line is run through them left to right.
    /// </remarks>
    private static List<(double Left, double Width)> ResolveBandsForLine(
        Cursor cursor, double provisionalHeight)
    {
        var height = Math.Max(1, provisionalHeight);
        var whole = new List<(double Left, double Width)> { (0, cursor.Width) };

        // A wrapTopAndBottom float, or two floats meeting in the middle, can leave no usable
        // width at all. The line then belongs below them.
        for (var guard = 0; guard < 64; guard++)
        {
            var bands = cursor.ResolveBands(cursor.Y, height);
            if (bands.Count > 0) return bands;

            var clear = cursor.NextClearY(cursor.Y, height);
            if (clear is null || clear <= cursor.Y) return whole;

            cursor.Y = clear.Value;
        }

        return whole;
    }

    /// <summary>
    /// Positions the anchored drawings of a paragraph and registers the areas text must avoid.
    /// </summary>
    /// <param name="room">
    /// The space this paragraph has already made between itself and the one before it, which a
    /// float reaching back over that paragraph unmakes by laying it again.
    /// </param>
    private void PlaceAnchoredDrawings(Cursor cursor, Paragraph paragraph, double room)
    {
        foreach (var anchored in paragraph.Runs.SelectMany(run => run.Content).OfType<AnchoredDrawing>())
        {
            var width = anchored.WidthPoints;
            var height = anchored.HeightPoints;
            if (width <= 0 || height <= 0) continue;

            // Where it goes, worked out before anything else happens and kept. Breaking the lines
            // above it again can lengthen the paragraph they belong to and so move the flow, and
            // the picture does not follow: Word's own export has the picture standing where the
            // flow first reached, with the lines above it broken round it where it stands.
            var region = AnchoredRegion(cursor, anchored, width, height);

            // A clearance reaching back over lines already written breaks those lines again. Only
            // a picture with text beside it: one that takes the whole measure has nothing to give
            // them, so they are moved down instead, which is what DisplaceOverlappedLines does and
            // what Word does with them.
            if (anchored.Wrap is not (TextWrapMode.None or TextWrapMode.TopAndBottom))
                ReflowLinesReachedBy(cursor, region, room);

            (Images.ImageData Frame, DetachedFlow? Content, double Left, double Top)? composed =
                anchored.Chart is { } chart
                    ? ComposeChart(chart, width, height)
                    : anchored.Diagram is { Count: > 0 } diagram
                        ? ComposeDiagram(diagram, width, height)
                        : anchored.Shape is { } shape
                            ? ComposeShape(shape, width, height)
                            : null;

            var image = composed?.Frame ??
                        (anchored.RelationshipId is null
                            ? null
                            : DecodeImage(anchored.RelationshipId, anchored.Wash));

            if (image is null) continue;

            // The picture's own corner, inside the room it keeps clear.
            var x = region.Left + Units.EmuToPoints(anchored.DistanceLeftEmu);
            var y = region.Top + Units.EmuToPoints(anchored.DistanceTopEmu);

            if (anchored.Wrap == TextWrapMode.TopAndBottom)
                x = ResolveHorizontalPosition(cursor, anchored, width);

            cursor.Page.Images.Add(new PositionedImage
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Image = image
            });

            composed?.Content?.PlaceOnto(cursor.Page, x + composed.Value.Left, y + composed.Value.Top);

            if (anchored.Wrap == TextWrapMode.None) continue;

            cursor.Floats.Add(region);

            // A float's clearance can reach back over text already on the page — the top
            // clearance of a picture anchored to a paragraph overlaps the last line of the one
            // before it. Word moves that text down; the picture stays where its anchor put it.
            if (anchored.Wrap == TextWrapMode.TopAndBottom) DisplaceOverlappedLines(cursor, region);
        }
    }

    /// <summary>
    /// The area text keeps clear of an anchored drawing: where it stands, grown by the distances
    /// it asks the text to stay away by.
    /// </summary>
    /// <remarks>
    /// The distances are the clearance Word keeps between the picture and the text; they are part
    /// of the area text has to avoid, not part of the picture. Worked out here rather than where
    /// the picture is placed because it is needed twice — once before, to see whether it reaches
    /// back over lines already written, and once after.
    /// </remarks>
    private static FloatRegion AnchoredRegion(
        Cursor cursor, AnchoredDrawing anchored, double width, double height)
    {
        var x = ResolveHorizontalPosition(cursor, anchored, width);
        var y = ResolveVerticalPosition(cursor, anchored, height);

        var left = x - Units.EmuToPoints(anchored.DistanceLeftEmu);
        var right = x + width + Units.EmuToPoints(anchored.DistanceRightEmu);

        if (anchored.Wrap == TextWrapMode.TopAndBottom)
        {
            // Nothing sits beside it, so the exclusion spans the whole measure.
            left = cursor.Left;
            right = cursor.Left + cursor.Width;
        }

        // A tight or through wrap follows its polygon (#65): the points come off the 21600
        // canvas of the extent into page coordinates at the picture's own corner, and the wrap
        // distances inflate each blocked interval rather than the box.
        IReadOnlyList<(double X, double Y)>? polygon = null;

        if (anchored.Wrap is TextWrapMode.Tight or TextWrapMode.Through &&
            anchored.WrapPolygon is { Count: >= 3 } wrapPolygon)
        {
            polygon = wrapPolygon
                .Select(point => (x + point.X * width / 21600.0, y + point.Y * height / 21600.0))
                .ToList();
        }

        return new FloatRegion(
            left,
            y - Units.EmuToPoints(anchored.DistanceTopEmu),
            right,
            y + height + Units.EmuToPoints(anchored.DistanceBottomEmu),
            polygon,
            anchored.Wrap == TextWrapMode.Through,
            Units.EmuToPoints(anchored.DistanceLeftEmu),
            Units.EmuToPoints(anchored.DistanceRightEmu));
    }

    /// <summary>
    /// Moves lines already placed on the page out from under a float that reaches back over them.
    /// </summary>
    /// <remarks>
    /// Only applied to floats spanning the whole measure. There the displaced line keeps the same
    /// width, so its existing line breaks stay correct and moving it is enough. A float that
    /// blocks only part of the measure would change the width available to the line and require
    /// it to be broken again, which single-pass layout cannot do — see the note in the README.
    /// </remarks>
    private static void DisplaceOverlappedLines(Cursor cursor, FloatRegion region)
    {
        var lines = cursor.Page.Lines;

        var first = -1;
        var delta = 0.0;

        for (var i = 0; i < lines.Count; i++)
        {
            var top = lines[i].BaselineY - lines[i].Ascent;
            var bottom = top + lines[i].Height;

            if (bottom <= region.Top || top >= region.Bottom) continue;

            first = i;
            delta = region.Bottom - top;
            break;
        }

        if (first < 0 || delta <= 0) return;

        // Lines already stand on the grid, so moving them by a whole number of steps keeps them
        // there, which is where Word writes them however far down the page a float pushes them.
        delta = Grid.Snap(delta);

        // Everything from the first overlapping line down moves by the same amount, which keeps
        // the spacing between them intact.
        for (var i = first; i < lines.Count; i++)
            lines[i] = ShiftLine(lines[i], delta);

        // A background, a highlight, a bar tab and a form field's box are all rectangles held
        // apart from the lines that put them there, so they move with the text or they stay
        // behind under empty space.
        var rectangles = cursor.Page.Rectangles;
        for (var i = 0; i < rectangles.Count; i++)
        {
            if (rectangles[i].Y < region.Top) continue;

            rectangles[i] = new PositionedRectangle
            {
                X = rectangles[i].X,
                Y = rectangles[i].Y + delta,
                Width = rectangles[i].Width,
                Height = rectangles[i].Height,
                Color = rectangles[i].Color
            };
        }

        // Underlines and strikethroughs are held separately from the text they belong to, so
        // they have to move with it or they stay behind under empty space.
        var rules = cursor.Page.Rules;
        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i].Y < region.Top) continue;

            rules[i] = new PositionedRule
            {
                X = rules[i].X,
                Y = rules[i].Y + delta,
                Width = rules[i].Width,
                Thickness = rules[i].Thickness,
                Color = rules[i].Color
            };
        }

        cursor.Y += delta;
    }

    private static LaidOutLine ShiftLine(LaidOutLine line, double delta)
    {
        var moved = new LaidOutLine
        {
            BaselineY = line.BaselineY + delta,
            Height = line.Height,
            Ascent = line.Ascent,
            ParagraphIndex = line.ParagraphIndex
        };

        foreach (var text in line.Texts)
            moved.Texts.Add(text.Translate(0, delta));

        return moved;
    }

    private static double ResolveHorizontalPosition(Cursor cursor, AnchoredDrawing anchored, double width)
    {
        var frame = cursor.Frame;
        var section = frame?.Section ?? cursor.Section;
        var pageWidth = section.PageWidthPoints;

        // In a flow of its own, everything is measured from where that flow will be put; taking
        // the page's frame back off leaves a position that lands where the page says once it is.
        var textLeft = frame is null ? cursor.Left : Units.TwipsToPoints(
            section.MarginLeftTwips + section.GutterTwips) - frame.Left;
        var textWidth = frame is null ? cursor.Width : section.ContentWidthPoints;

        var (origin, available) = anchored.HorizontalFrom switch
        {
            HorizontalAnchor.Page => (frame is null ? 0.0 : -frame.Left, pageWidth),
            HorizontalAnchor.LeftMargin => (frame is null ? 0.0 : -frame.Left, textLeft),
            HorizontalAnchor.RightMargin => (textLeft + textWidth, pageWidth - textLeft - textWidth),
            _ => (textLeft, textWidth)
        };

        if (anchored.HorizontalOffsetEmu is { } offset)
            return origin + Units.EmuToPoints(offset);

        return anchored.HorizontalAlign switch
        {
            "center" => origin + (available - width) / 2,
            // "inside" and "outside" alternate with the page side in a bound document; without
            // that concept they are the nearest equivalent fixed edge.
            "right" or "outside" => origin + available - width,
            _ => origin
        };
    }

    private static double ResolveVerticalPosition(Cursor cursor, AnchoredDrawing anchored, double height)
    {
        var frame = cursor.Frame;
        var pageHeight = (frame?.Section ?? cursor.Section).PageHeightPoints;

        var contentTop = frame is null ? cursor.ContentTop : frame.ContentTop - frame.Top;
        var contentBottom = frame is null ? cursor.ContentBottom : frame.ContentBottom - frame.Top;

        var (origin, available) = anchored.VerticalFrom switch
        {
            VerticalAnchor.Page => (frame is null ? 0.0 : -frame.Top, pageHeight),
            VerticalAnchor.Margin or VerticalAnchor.TopMargin =>
                (contentTop, contentBottom - contentTop),
            VerticalAnchor.BottomMargin => (contentBottom, pageHeight - contentBottom),
            // Paragraph and line are both relative to where the text has reached, and in a flow
            // of its own that is where the flow has reached.
            _ => frame is null
                ? (cursor.Y, cursor.ContentBottom - cursor.Y)
                : (cursor.Y, contentBottom - cursor.Y)
        };

        if (anchored.VerticalOffsetEmu is { } offset)
            return origin + Units.EmuToPoints(offset);

        return anchored.VerticalAlign switch
        {
            "center" => origin + (available - height) / 2,
            "bottom" or "outside" => origin + available - height,
            _ => origin
        };
    }

    // ----- headers and footers -----

    /// <summary>
    /// Lays out the header and footer of every page into the margins.
    /// </summary>
    /// <remarks>
    /// Each page gets its own pass, because the content can differ per page in three ways: a
    /// document may give its first page or its even pages their own, a field such as a page number
    /// resolves differently on each, and pages of different sections take different parts
    /// altogether — which is why the geometry here comes from the page rather than the document.
    /// </remarks>
    private void LayoutHeadersAndFooters(WordDocument document, LaidOutDocument result)
    {
        if (result.Pages.All(p =>
                p.Section.HeaderReferences.Count == 0 && p.Section.FooterReferences.Count == 0))
        {
            return;
        }

        _totalPages = result.Pages.Count;

        for (var index = 0; index < result.Pages.Count; index++)
        {
            var page = result.Pages[index];
            _currentPage = index + 1;

            var section = page.Section;
            var left = Units.TwipsToPoints(section.MarginLeftTwips + section.GutterTwips);
            var width = section.ContentWidthPoints;

            var kind = SelectKind(section, document.EvenAndOddHeaders, page.IndexInSection, index);

            if (Resolve(document, section.HeaderReferences, kind) is { } header)
            {
                // The header starts at its declared distance from the top of the page and grows
                // downwards, into the margin it was given.
                var top = Units.TwipsToPoints(section.HeaderDistanceTwips);
                var flow = MeasureBlocks(header.Body, width, PageFrameOf(section, left, top));
                flow.PlaceOnto(page, left, top);
            }

            if (Resolve(document, section.FooterReferences, kind) is { } footer)
            {
                // The footer's distance is measured from the bottom of the page to its own
                // bottom, so its top depends on how tall it turned out to be.
                var bottom = section.PageHeightPoints - Units.TwipsToPoints(section.FooterDistanceTwips);

                // How tall it is decides where it starts, so it is measured twice: once to find
                // that out, and once knowing where on the page it will be put.
                var height = MeasureBlocks(footer.Body, width).Height;
                var flow = MeasureBlocks(footer.Body, width,
                    PageFrameOf(section, left, bottom - height));

                flow.PlaceOnto(page, left, bottom - flow.Height);
            }
        }

        _currentPage = 0;
    }

    /// <summary>Where a running head will sit on the page it belongs to.</summary>
    private static PageFrame PageFrameOf(SectionProperties section, double left, double top) =>
        new(section, left, top,
            Units.TwipsToPoints(section.MarginTopTwips),
            section.PageHeightPoints - Units.TwipsToPoints(section.MarginBottomTwips));

    // ----- vertical alignment -----

    /// <summary>
    /// Moves what a page holds to sit where its section asks for it.
    /// </summary>
    /// <remarks>
    /// The text is laid out from the top margin down like any other, and moved once the page is
    /// finished and how much of it was used is known. Only the body moves: footnotes are written
    /// into the foot of the page afterwards and belong to the page rather than to the text, and
    /// running heads live in the margins.
    ///
    /// A page with nothing spare — a full one — is left alone, which is why a long section aligned
    /// to the bottom looks unchanged until its last page.
    /// </remarks>
    private static void AlignPageVertically(Cursor cursor)
    {
        var alignment = cursor.Section.VerticalAlignment;
        if (alignment == VerticalPageAlignment.Top) return;

        var used = Math.Max(cursor.PageMaxY, cursor.Y);
        var free = cursor.ContentLimit - cursor.Reserved - used;
        if (free <= 0.001) return;

        var shift = alignment switch
        {
            VerticalPageAlignment.Center => Constant(free / 2),
            VerticalPageAlignment.Bottom => Constant(free),
            _ => Justified(cursor.Page, free, cursor.ContentTop)
        };

        MovePage(cursor.Page, shift);

        // The rule between columns is drawn from what the page reached, so it has to know the
        // text moved.
        cursor.PageMaxY = used + shift(used);
        cursor.Y += shift(cursor.Y);
    }

    private static Func<double, double> Constant(double shift) => _ => shift;

    /// <summary>
    /// Spreads the spare height between the paragraphs, which is where Word puts it: the first
    /// stays against the top margin and the last ends against the bottom, with what is left over
    /// divided equally into the gaps between them.
    /// </summary>
    private static Func<double, double> Justified(LaidOutPage page, double free, double contentTop)
    {
        // Where each paragraph starts, in the order they sit on the page.
        var starts = new List<double>();
        var seen = new HashSet<int>();

        foreach (var line in page.Lines.OrderBy(line => line.BaselineY))
        {
            if (seen.Add(line.ParagraphIndex)) starts.Add(line.BaselineY - line.Ascent);
        }

        if (starts.Count < 2) return Constant(0);

        var gap = free / (starts.Count - 1);

        return y =>
        {
            // Everything down to the second paragraph stays put, and each paragraph after that
            // takes one more gap with it.
            var before = 0;
            for (var i = 1; i < starts.Count; i++)
            {
                if (y >= starts[i] - 0.001) before = i;
            }

            return y < contentTop - 0.001 ? 0 : before * gap;
        };
    }

    /// <summary>Moves everything already on a page, by however much it is standing.</summary>
    private static void MovePage(LaidOutPage page, Func<double, double> shift)
    {
        var lines = page.Lines.ToList();
        page.Lines.Clear();

        foreach (var line in lines)
        {
            var delta = Grid.Snap(shift(line.BaselineY - line.Ascent));

            var moved = new LaidOutLine
            {
                BaselineY = line.BaselineY + delta,
                Height = line.Height,
                Ascent = line.Ascent,
                ParagraphIndex = line.ParagraphIndex
            };

            foreach (var text in line.Texts) moved.Texts.Add(text.Translate(0, delta));

            page.Lines.Add(moved);
        }

        var rules = page.Rules.ToList();
        page.Rules.Clear();

        foreach (var rule in rules)
        {
            page.Rules.Add(new PositionedRule
            {
                X = rule.X,
                Y = rule.Y + shift(rule.Y),
                Width = rule.Width,
                Thickness = rule.Thickness,
                Color = rule.Color
            });
        }

        var rectangles = page.Rectangles.ToList();
        page.Rectangles.Clear();

        foreach (var rectangle in rectangles)
        {
            page.Rectangles.Add(new PositionedRectangle
            {
                X = rectangle.X,
                Y = rectangle.Y + shift(rectangle.Y),
                Width = rectangle.Width,
                Height = rectangle.Height,
                Color = rectangle.Color
            });
        }

        var images = page.Images.ToList();
        page.Images.Clear();

        foreach (var image in images)
        {
            page.Images.Add(new PositionedImage
            {
                X = image.X,
                Y = image.Y + shift(image.Y),
                Width = image.Width,
                Height = image.Height,
                Image = image.Image
            });
        }
    }

    // ----- footnotes -----

    /// <summary>
    /// The rule Word draws above a page's footnotes: two inches long, a hundredth of an inch
    /// thick, and sitting a fixed distance above the first note.
    /// </summary>
    /// <remarks>
    /// All three were measured from Word's exports, since the document says only that there is a
    /// separator, never what it looks like. Word fills a path rather than stroking a line, which
    /// is what <see cref="PositionedRule"/> does too.
    ///
    /// The gap is measured up from the bottom of the separator's line box — that is, down from the
    /// first note — rather than from the top of that box. <c>footnote-separator-probe</c> gives the
    /// separator's paragraph a mark three times the usual size, and Word draws the rule in exactly
    /// the same place as it does for an ordinary one: the rule follows the notes, not its own
    /// paragraph.
    /// </remarks>
    private const double FootnoteSeparatorWidthPoints = 144;

    private const double FootnoteSeparatorThicknessPoints = 0.72;

    private const double FootnoteSeparatorGapPoints = 5.07;

    /// <summary>The notes gathered under one column of the page being filled.</summary>
    private List<DetachedFlow> FootnotesOfColumn(int column)
    {
        if (_pageFootnotes.TryGetValue(column, out var flows)) return flows;

        return _pageFootnotes[column] = [];
    }

    /// <summary>
    /// Measures the footnotes some content refers to and reports how much more of the column they
    /// need, so the content can be fitted against what is left rather than against the whole of it.
    /// </summary>
    private Prepared PrepareFootnotes(Cursor cursor, IEnumerable<int> ids)
    {
        var flows = new List<DetachedFlow>();
        var height = 0.0;

        foreach (var id in ids)
        {
            if (!_footnotes.TryGetValue(id, out var footnote) || footnote.IsSeparator) continue;

            var flow = MeasureFootnote(id, footnote, cursor.Width);
            flows.Add(flow);
            height += flow.Height;
        }

        if (flows.Count == 0) return Prepared.None;

        // The separator is paid for once per column, by whichever note reaches it first.
        var separator = FootnotesOfColumn(cursor.ColumnIndex).Count == 0
            ? SeparatorFlow(cursor.Width)?.Height ?? 0
            : 0;

        return new Prepared(flows, height + separator);
    }

    /// <summary>
    /// Footnotes measured and waiting for the line that refers to them to be placed.
    /// </summary>
    /// <param name="Height">What they take altogether, including the separator where it is due.</param>
    private readonly record struct Prepared(List<DetachedFlow> Flows, double Height)
    {
        public static readonly Prepared None = new([], 0);
    }

    /// <summary>
    /// Adds prepared footnotes to the page being filled and takes their space out of it, dividing
    /// what will not fit and leaving the rest for the page after.
    /// </summary>
    /// <param name="lineHeight">
    /// The height of the line that refers to them, which is going onto the page as well and so is
    /// not room the notes can have.
    /// </param>
    private void CommitFootnotes(Cursor cursor, Prepared prepared, double lineHeight)
    {
        if (prepared.Flows.Count == 0) return;

        var room = cursor.ContentLimit - (cursor.Y + lineHeight) - cursor.Reserved;

        var column = FootnotesOfColumn(cursor.ColumnIndex);

        if (prepared.Height <= room + 0.001 || !cursor.Paginate)
        {
            column.AddRange(prepared.Flows);
            cursor.Reserved += prepared.Height;

            return;
        }

        // What is left after the separator is what the notes themselves have.
        var left = room - (prepared.Height - Total(prepared.Flows));

        foreach (var flow in prepared.Flows)
        {
            if (_carriedFootnotes.Count > 0)
            {
                // Once one note has been divided, everything after it belongs with the remainder:
                // a note cannot begin below one that has not finished.
                _carriedFootnotes.Add(flow);
                continue;
            }

            if (flow.Height <= left + 0.001)
            {
                column.Add(flow);
                left -= flow.Height;

                continue;
            }

            var (fitted, remaining) = flow.SplitAt(left);

            if (fitted.Height > 0) column.Add(fitted);
            _carriedFootnotes.Add(remaining);
        }

        // The notes now reach the bottom margin, so nothing else goes on this page.
        cursor.Reserved = cursor.ContentLimit - (cursor.Y + lineHeight);
    }

    /// <summary>
    /// Makes pages for what is left of a note once there is no more document to carry it. Each is
    /// a page with nothing on it but the note, which is where Word puts it too.
    /// </summary>
    private void DrainFootnotes(Cursor cursor)
    {
        // Every page takes at least one line of what is left, so this ends; the bound is only
        // there so that a note which somehow cannot be divided cannot spin for ever.
        for (var guard = 0; _carriedFootnotes.Count > 0 && guard < 10_000; guard++)
        {
            var before = Total(_carriedFootnotes);

            cursor.BreakPage();

            if (Total(_carriedFootnotes) >= before - 0.001) break;
        }
    }

    private static double Total(IEnumerable<DetachedFlow> flows)
    {
        var height = 0.0;
        foreach (var flow in flows) height += flow.Height;

        return height;
    }

    /// <summary>
    /// Puts the rest of a divided note at the foot of the page that follows, before anything else
    /// is placed on it — a note carried over is not something the page can be asked to find room
    /// for later. Where it will not fit either, it is divided again.
    /// </summary>
    private void ResumeFootnotes(Cursor cursor)
    {
        if (_carriedFootnotes.Count == 0) return;

        var carried = new List<DetachedFlow>(_carriedFootnotes);
        _carriedFootnotes.Clear();

        _footnotesContinue = true;

        var column = FootnotesOfColumn(cursor.ColumnIndex);

        var separator = ContinuationFlow(cursor.Width)?.Height ?? 0;
        var room = cursor.ContentLimit - cursor.ContentTop - separator;
        var height = separator;

        foreach (var flow in carried)
        {
            if (_carriedFootnotes.Count > 0)
            {
                _carriedFootnotes.Add(flow);
                continue;
            }

            if (flow.Height <= room + 0.001)
            {
                column.Add(flow);
                room -= flow.Height;
                height += flow.Height;

                continue;
            }

            var (fitted, remaining) = flow.SplitAt(room);

            if (fitted.Height > 0)
            {
                column.Add(fitted);
                height += fitted.Height;
            }

            _carriedFootnotes.Add(remaining);
        }

        cursor.Reserved += height;
    }

    /// <summary>Footnote ids referenced by the marks on a composed line, in order.</summary>
    private static List<int> FootnotesOn(ComposedLine line)
    {
        List<int>? ids = null;

        foreach (var item in line.Items)
        {
            if (item.Atom is TextAtom { FootnoteId: { } id }) (ids ??= []).Add(id);
        }

        return ids ?? [];
    }

    /// <summary>
    /// Writes the footnotes a page collected into the foot of it.
    /// </summary>
    /// <remarks>
    /// The notes are bottom-aligned against the bottom margin, which is where Word puts them, with
    /// the separator immediately above the first. The space they take was reserved as each note
    /// was committed, so nothing above them has been placed where they are about to go.
    ///
    /// A section may ask for them under the last line of text instead. That is the same place on a
    /// page whose text reaches the bottom margin — which is most pages — and a long way above it
    /// on one whose text stops early, which is what the last page of a document usually does.
    /// </remarks>
    private void FlushFootnotes(LaidOutPage page, IReadOnlyDictionary<int, double> textBottoms)
    {
        foreach (var index in _pageFootnotes.Keys.OrderBy(key => key).ToList())
            FlushColumnFootnotes(page, index, textBottoms.GetValueOrDefault(index));

        _pageFootnotes.Clear();
        _footnotesContinue = false;
    }

    /// <summary>
    /// Writes one column's notes under it, which happens as that column is finished rather than
    /// when the page is: Word writes a column's text and then its notes before going on to the
    /// next column, and a PDF that says the same things in a different order is a different file
    /// to anything comparing the two.
    /// </summary>
    private void FlushColumnFootnotes(LaidOutPage page, int index, double textBottom)
    {
        if (!_pageFootnotes.TryGetValue(index, out var flows) || flows.Count == 0) return;

        _pageFootnotes.Remove(index);

        {
            var (left, width) = ColumnOf(page, index);

            var height = Total(flows);

            // A column that opens with the rest of a note carries the other rule: right across the
            // measure it is set to, so that a reader can see the note above it was not finished.
            var continued = _footnotesContinue && index == 0;
            var rule = continued ? ContinuationFlow(width) : SeparatorFlow(width);

            var y = _footnotePosition == NotePosition.BeneathText
                ? Math.Min(textBottom + (rule?.Height ?? 0), _footnoteBottom - height)
                : _footnoteBottom - height;

            rule?.PlaceOnto(page, left, y - rule.Height);

            foreach (var flow in flows)
            {
                flow.PlaceOnto(page, left, y);
                y += flow.Height;
            }
        }
    }

    /// <summary>
    /// Where one column of a page begins and how wide it is, which is the measure the notes under
    /// it are set to. A section of one column is the whole measure, as it always was.
    /// </summary>
    private static (double Left, double Width) ColumnOf(LaidOutPage page, int index)
    {
        var section = page.Section;
        var columns = section.GetColumns();
        var at = Math.Clamp(index, 0, columns.Count - 1);

        return (Units.TwipsToPoints(section.MarginLeftTwips + section.GutterTwips) + columns[at].Left,
            columns[at].Width);
    }

    /// <summary>
    /// Lays out one footnote's body, once. A note is referenced from one place and appears once,
    /// so measuring it again would only repeat the work.
    /// </summary>
    private DetachedFlow MeasureFootnote(int id, Note footnote, double width)
    {
        // Keyed by the measure as well as by the note: a note under a column is set to that
        // column's width, and a section may hold columns of different widths.
        if (_measuredFootnotes.TryGetValue((id, width), out var cached)) return cached;

        // The note's own number opens its text, and it is the number the reference was given.
        var previous = _currentNoteLabel;
        _currentNoteLabel = _footnoteLabels.GetValueOrDefault(id);

        var flow = MeasureBlocks(footnote.Body, width);

        _currentNoteLabel = previous;
        _measuredFootnotes[(id, width)] = flow;
        return flow;
    }

    /// <summary>
    /// Writes the endnotes out where the body left off.
    /// </summary>
    /// <remarks>
    /// Endnotes are not an area of the page the way footnotes are: Word carries straight on from
    /// the last body paragraph with the separator and then the notes, breaking to a new page only
    /// when it runs out of room like any other content. Laying them out through the body's own
    /// cursor is what gives them that, and it also keeps them clear of any footnote area on the
    /// page they land on.
    /// </remarks>
    /// <param name="section">
    /// The section whose notes to write, or null for all of them that are left. A document may ask
    /// for each section's own at the end of it, which is where a book puts the notes of a chapter,
    /// and then each group is written where that section stops — before the break that opens the
    /// next one, so they belong to the pages of the section they came from.
    /// </param>
    private void LayoutEndnotes(Cursor cursor, int? section = null)
    {
        var ids = _endnoteOrder
            .Where(entry => section is null || entry.Section == section)
            .Select(entry => entry.Id)
            .ToList();

        if (ids.Count == 0) return;

        // Each group is introduced by the separator, as Word draws one above every one of them.
        if (_endnotes.Values.FirstOrDefault(n => n.Type == "separator") is { } separator)
            LayoutBlocks(cursor, separator.Body);

        foreach (var id in ids)
        {
            if (!_endnotes.TryGetValue(id, out var note)) continue;

            _currentNoteLabel = _endnoteLabels.GetValueOrDefault(id);
            LayoutBlocks(cursor, note.Body);
        }

        _currentNoteLabel = null;

        _endnoteOrder.RemoveAll(entry => ids.Contains(entry.Id));
    }

    /// <summary>
    /// The separator's own content, or null when the document has none. Measured once: it is the
    /// same on every page.
    /// </summary>
    private DetachedFlow? SeparatorFlow(double width) => Separator("separator", width);

    /// <summary>The rule of the given kind, measured against the width it is drawn over.</summary>
    private DetachedFlow? Separator(string type, double width)
    {
        if (_separatorFlows.TryGetValue((type, width), out var cached)) return cached;

        var separator = _footnotes.Values.FirstOrDefault(note => note.Type == type);

        if (separator is null) return _separatorFlows[(type, width)] = null;

        _separatorMeasure = width;
        var flow = MeasureBlocks(separator.Body, width);
        _separatorMeasure = 0;

        return _separatorFlows[(type, width)] = flow;
    }

    /// <summary>
    /// The rule above a note carried over from the page before, which the document keeps as a note
    /// of its own in the same way. Word draws it right across the measure rather than the two
    /// inches of the ordinary one.
    /// </summary>
    private DetachedFlow? ContinuationFlow(double width) =>
        // A document with no continuation separator of its own still needs the space, so the
        // ordinary one stands in for it.
        Separator("continuationSeparator", width) ?? SeparatorFlow(width);

    /// <summary>
    /// Chooses which of the three header and footer kinds applies to a page.
    /// </summary>
    private static string SelectKind(
        SectionProperties section, bool evenAndOdd, int indexInSection, int pageIndex)
    {
        // A title page is the first page of its own section, not of the document.
        if (section.TitlePage && indexInSection == 0) return "first";

        // Odd and even are the printed page numbers, which run through the whole document.
        return evenAndOdd && (pageIndex + 1) % 2 == 0 ? "even" : "default";
    }

    /// <summary>
    /// Finds the part for a kind, falling back to the default pair. A document declaring only a
    /// default header uses it on every page.
    /// </summary>
    private static HeaderFooter? Resolve(
        WordDocument document, Dictionary<string, string> references, string kind)
    {
        if (references.TryGetValue(kind, out var id) && document.HeadersAndFooters.TryGetValue(id, out var part))
            return part;

        return references.TryGetValue("default", out var fallback) &&
               document.HeadersAndFooters.TryGetValue(fallback, out var defaultPart)
            ? defaultPart
            : null;
    }

    /// <summary>
    /// The paragraphs standing for a table of contents, where this paragraph opens one and its
    /// entries can be worked out. Null for anything else, which is then laid out as it stands.
    /// </summary>
    /// <remarks>
    /// The entries are worked out from the document rather than read from what the field last
    /// produced: a table of contents is the one field whose answer is a run of paragraphs, and a
    /// stale one is as wrong as a stale page number. What it produced last time follows it in the
    /// body and is passed over.
    ///
    /// A heading's page is only known once the document has been laid out, so on a first pass the
    /// entries are built without their numbers — the same entries, over the same lines, so that
    /// the pass which fills the numbers in paginates the same way.
    /// </remarks>
    private List<BlockElement>? TableOfContents(Cursor cursor, Paragraph paragraph)
    {
        // The field may run past this paragraph, holding its first entry, or it may be closed and
        // empty — a table of contents that has never been built is written the second way, and one
        // Word has built the first.
        if (FieldIn(paragraph, "TOC") is not { } field) return null;

        var instruction = FieldInstruction.Parse(field.Instruction);

        var scope = TableOfContentsBuilder.ScopeOf(instruction);
        var numbered = TableOfContentsBuilder.ShowsPageNumbers(instruction);

        // The far margin, which is where a table of contents that has no style to say puts the
        // stop its page numbers hang from.
        var tabPosition = (int)Math.Round(Units.PointsToTwips(cursor.Width));

        var entries = new List<BlockElement>();

        foreach (var block in _body)
        {
            if (block is not Paragraph heading || heading.InsideField) continue;

            var format = _styles.ResolveParagraph(heading.Properties);
            if (scope.LevelOf(format.OutlineLevel, format.StyleId) is not { } level) continue;

            var text = TextOf(heading);
            if (text.Length == 0) continue;

            var page = Pagination?.PageOfHeading(heading) ?? 0;
            var styleId = $"TOC{level}";

            entries.Add(TableOfContentsBuilder.Build(
                new TableOfContentsBuilder.Entry(level, text, page),
                numbered, _styles.Styles.ById.ContainsKey(styleId), tabPosition));
        }

        // Nothing to say: the document has no headings the field asks for, and what it last
        // produced is left to stand rather than replaced with an empty space.
        if (entries.Count == 0) return null;

        // The numbers come from the pass before this one, so a document with a table of contents
        // is one that has to be laid out twice.
        if (numbered && Pagination is null) NeedsPagination = true;

        return entries;
    }

    /// <summary>
    /// The paragraphs standing for a field this paragraph holds whose answer is paragraphs of its
    /// own — a table of contents or an index. Null for anything else, which is laid out as it
    /// stands.
    /// </summary>
    private List<BlockElement>? Generated(Cursor cursor, Paragraph paragraph) =>
        TableOfContents(cursor, paragraph) ?? Index(paragraph);

    /// <summary>
    /// The paragraphs standing for an index, where this paragraph holds an INDEX field. Null for
    /// anything else.
    /// </summary>
    /// <remarks>
    /// The entries come from the XE fields the document carries, each with the page the paragraph
    /// holding it landed on. Like a table of contents it is worked out again rather than read back
    /// from what the field last produced, and for the same reason.
    /// </remarks>
    private List<BlockElement>? Index(Paragraph paragraph)
    {
        if (FieldIn(paragraph, "INDEX") is not { } field) return null;

        var instruction = FieldInstruction.Parse(field.Instruction);
        var marks = new List<(IndexBuilder.Mark Mark, int Page)>();

        foreach (var (mark, source) in CollectIndexMarks(_body))
        {
            var page = Pagination?.PageOfMark(source) ??
                       (_markPages.TryGetValue(source, out var placed) && _result is not null
                           ? _result.Pages.IndexOf(placed) + 1
                           : 0);

            marks.Add((mark, page));
        }

        if (marks.Count == 0) return null;

        var entries = IndexBuilder.Build(marks, instruction, _styles.Styles.ById.ContainsKey);
        if (entries.Count == 0) return null;

        // An entry marked after the index itself has no page until the document has been laid out
        // once, and an index of any length moves the pages of everything after it.
        if (Pagination is null) NeedsPagination = true;

        return entries;
    }

    /// <summary>Every index entry the document marks, in the order it marks them.</summary>
    private static List<(IndexBuilder.Mark Mark, FieldInline Field)> CollectIndexMarks(
        IEnumerable<BlockElement> blocks)
    {
        var marks = new List<(IndexBuilder.Mark, FieldInline)>();

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var run in paragraph.Runs)
                    foreach (var content in run.Content)
                    {
                        if (content is not FieldInline field) continue;
                        if (IndexBuilder.Read(FieldInstruction.Parse(field.Instruction)) is { } mark)
                            marks.Add((mark, field));
                    }

                    break;

                case Table table:
                    foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        marks.AddRange(CollectIndexMarks(cell.Content));

                    break;
            }
        }

        return marks;
    }

    /// <summary>The TOC field a paragraph holds, if it holds one.</summary>
    private static FieldInline? FieldIn(Paragraph paragraph, string keyword)
    {
        if (paragraph.OpensField is { } opened)
            return FieldInstruction.Parse(opened.Instruction).Keyword == keyword ? opened : null;

        foreach (var run in paragraph.Runs)
        foreach (var content in run.Content)
        {
            if (content is FieldInline field &&
                FieldInstruction.Parse(field.Instruction).Keyword == keyword)
            {
                return field;
            }
        }

        return null;
    }

    /// <summary>
    /// A paragraph a running head can pick up: the page it landed on, the style it was set in, and
    /// the text it holds.
    /// </summary>
    /// <param name="Ordinal">
    /// Which paragraph of the document it is, so that one moved to another page by a pull-back
    /// replaces its own earlier record rather than being counted twice.
    /// </param>
    private readonly record struct StyledParagraph(int Ordinal, int Page, string StyleId, string Text);

    /// <summary>Notes a paragraph as it is placed, for the STYLEREF fields that look for it.</summary>
    private void RecordStyledParagraph(int ordinal, int page, Paragraph paragraph)
    {
        if (paragraph.Properties?.StyleId is not { Length: > 0 } styleId) return;

        // A line pulled back to the next page brings its paragraph with it, and the page recorded
        // when it was first placed is no longer where it is.
        if (_styledParagraphs.Count > 0 && _styledParagraphs[^1].Ordinal == ordinal)
            _styledParagraphs.RemoveAt(_styledParagraphs.Count - 1);

        _styledParagraphs.Add(new StyledParagraph(ordinal, page, styleId, TextOf(paragraph)));
    }

    /// <summary>
    /// Every styled paragraph of the document in the order it is written, which is what answers a
    /// STYLEREF with nothing of its style before it.
    /// </summary>
    private static List<(string StyleId, string Text)> CollectStyledParagraphs(
        IEnumerable<BlockElement> blocks)
    {
        var collected = new List<(string, string)>();

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph when paragraph.Properties?.StyleId is { Length: > 0 } styleId:
                    collected.Add((styleId, TextOf(paragraph)));
                    break;

                case Table table:
                    foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                        collected.AddRange(CollectStyledParagraphs(cell.Content));

                    break;
            }
        }

        return collected;
    }

    /// <summary>The plain text of a paragraph, which is what a field showing it displays.</summary>
    private static string TextOf(Paragraph paragraph)
    {
        var text = new System.Text.StringBuilder();

        foreach (var run in paragraph.Runs)
        foreach (var content in run.Content)
        {
            switch (content)
            {
                case TextInline plain:
                    text.Append(plain.Text);
                    break;

                case TabInline:
                    text.Append('\t');
                    break;
            }
        }

        return text.ToString().Trim();
    }

    /// <summary>
    /// The paragraph a STYLEREF picks up, which depends on where the field itself is.
    /// </summary>
    /// <remarks>
    /// Word's rules, read off its export of the styleref fixture:
    ///
    ///   - In a header or a footer, the first paragraph of that style on the page — a footer looks
    ///     down the page like a header rather than up it, which is not what would be guessed. The
    ///     <c>\l</c> switch is what asks for the last one on the page instead.
    ///   - On a page holding none of that style, the last one before the page, which is what
    ///     carries a running head through the pages of a chapter.
    ///   - In the body, the nearest one before the field; failing that, the first in the document.
    ///
    /// The style is named rather than identified: Word answers " STYLEREF Heading1 " with an error
    /// telling the reader to apply the style, so an id is not a name here even where it looks like
    /// one.
    /// </remarks>
    private string? StyleReference(FieldInstruction instruction)
    {
        if (instruction.Argument is not { Length: > 0 } name) return null;
        if (StyleIdNamed(name) is not { } styleId) return null;

        if (_currentPage > 0)
        {
            var page = _currentPage - 1;
            var onPage = _styledParagraphs.Where(p => p.StyleId == styleId && p.Page == page).ToList();

            if (onPage.Count > 0)
                return instruction.HasSwitch('l') ? onPage[^1].Text : onPage[0].Text;

            for (var i = _styledParagraphs.Count - 1; i >= 0; i--)
            {
                if (_styledParagraphs[i].StyleId == styleId && _styledParagraphs[i].Page < page)
                    return _styledParagraphs[i].Text;
            }

            return null;
        }

        // In the body only what has been placed is behind the field, which is what makes the last
        // of these the nearest one before it.
        for (var i = _styledParagraphs.Count - 1; i >= 0; i--)
        {
            if (_styledParagraphs[i].StyleId == styleId) return _styledParagraphs[i].Text;
        }

        foreach (var (candidate, text) in _documentStyled)
        {
            if (string.Equals(candidate, styleId, StringComparison.OrdinalIgnoreCase)) return text;
        }

        return null;
    }

    /// <summary>The style of the given name, which is how a field asks for one.</summary>
    private string? StyleIdNamed(string name)
    {
        foreach (var (id, style) in _styles.Styles.ById)
        {
            if (string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }

        return null;
    }

    /// <summary>
    /// Produces the text a field should display, falling back to whatever Word last computed for
    /// the ones nothing here can work out.
    /// </summary>
    /// <remarks>
    /// Every field is numbered as it is met so that a second pass can tell them apart: their order
    /// is the same both times, since what changes between passes is what the fields say rather
    /// than which of them there are.
    /// </remarks>
    private string ResolveField(FieldInline field, out int occurrence)
    {
        occurrence = ++_fieldOccurrence;

        var instruction = FieldInstruction.Parse(field.Instruction);
        var page = PageOfField(occurrence);

        var section = Pagination?.SectionOfPage(page) ?? 0;

        // On a first pass none of this is settled, and a field left empty would leave nothing on
        // the line to say which page it landed on. It is given the page being filled instead,
        // which is right unless the paragraph moves — and the pass after this one, which is what
        // these values are being collected for, corrects it either way.
        // A page number field shows the number the page is printed as, which is not where the
        // page stands in the document once a section has begun its numbering again.
        Fields.Page = PrintedPage(page > 0 ? page : _result?.Pages.Count ?? 0);
        Fields.TotalPages = _totalPages > 0 ? _totalPages : Pagination?.TotalPages ?? 0;
        Fields.Section = section > 0 ? section : _sectionOrdinal;
        Fields.SectionPages = section > 0
            ? Pagination!.PagesInSection(section)
            : Math.Max(1, _pagesInSection);

        var value = FieldEvaluator.Evaluate(instruction, Fields);

        // Whether laying the document out again would say more than this pass could. A header
        // knows its page already; the body does not until it has been paginated once.
        if (_currentPage == 0 && Pagination is null &&
            FieldEvaluator.DependsOnPagination(instruction.Keyword))
        {
            NeedsPagination = true;
        }

        return value ?? field.CachedText;
    }

    /// <summary>
    /// The number a page is printed as, given its place in the document. The pass before this one
    /// worked it out for every page; on a first pass, where only the pages made so far exist, the
    /// page itself is asked.
    /// </summary>
    private int PrintedPage(int page)
    {
        if (page <= 0) return 0;
        if (Pagination is not null) return Pagination.PrintedPage(page);

        return _result is not null && page <= _result.Pages.Count ? _result.Pages[page - 1].PageNumber : page;
    }

    /// <summary>
    /// The page a field is on, counting from one. A header or footer is laid out for a page that
    /// is already known; a field in the body has to be told by an earlier pass.
    /// </summary>
    private int PageOfField(int occurrence) =>
        _currentPage > 0 ? _currentPage : Pagination?.PageOfField(occurrence) ?? 0;

    /// <summary>
    /// Notes which page a line's fields landed on, for the next pass to read. Only the body is
    /// recorded: a header knows its page already, and content composed away from the page it will
    /// end up on — a table cell, a footnote — has none to record yet.
    /// </summary>
    private void RecordFieldPages(LaidOutPage page, ComposedLine line)
    {
        if (_currentPage > 0) return;

        foreach (var item in line.Items)
        {
            if (item.Atom is TextAtom { FieldOccurrence: { } occurrence })
                _fieldPages[occurrence] = page;

            // Where a note's mark landed, which is what numbering by page is worked out from. A
            // line that is taken back off a page and put on the next records it again.
            if (item.Atom is TextAtom { FootnoteId: { } note }) _notePages[note] = page;
        }
    }

    /// <summary>What this pass learned about where the fields fell.</summary>
    public FieldPagination CollectPagination(LaidOutDocument result)
    {
        var pages = new Dictionary<int, int>();

        foreach (var (occurrence, page) in _fieldPages)
        {
            var index = result.Pages.IndexOf(page);
            if (index >= 0) pages[occurrence] = index + 1;
        }

        var sections = new List<int>(result.Pages.Count);
        var counts = new List<int>();

        foreach (var page in result.Pages)
        {
            // A page whose section index is zero begins the document; a new section begins wherever
            // the page's own section is not the one before it.
            if (counts.Count == 0 || page.IndexInSection == 0) counts.Add(0);

            counts[^1]++;
            sections.Add(counts.Count);
        }

        var headings = new Dictionary<Paragraph, int>();

        foreach (var (paragraph, page) in _headingPages)
        {
            var index = result.Pages.IndexOf(page);
            if (index >= 0) headings[paragraph] = index + 1;
        }

        var marks = new Dictionary<FieldInline, int>();

        foreach (var (mark, page) in _markPages)
        {
            var index = result.Pages.IndexOf(page);
            if (index >= 0) marks[mark] = index + 1;
        }

        var notes = new Dictionary<int, int>();

        foreach (var (id, page) in _notePages)
        {
            var index = result.Pages.IndexOf(page);
            if (index >= 0) notes[id] = index + 1;
        }

        var printed = result.Pages.Select(page => page.PageNumber).ToList();

        return new FieldPagination(
            result.Pages.Count, pages, sections, counts, headings, marks, notes, printed);
    }

    // ----- tables -----

    /// <summary>
    /// Lays out a table row by row.
    /// </summary>
    /// <remarks>
    /// A row's height is the tallest of its cells, so every cell has to be laid out before any of
    /// it can be placed. Cells are therefore measured into a detached page and translated into
    /// position once the row's geometry is known.
    ///
    /// Rows are kept whole: one that does not fit moves to the next page rather than splitting.
    /// Word will split a row unless <c>w:cantSplit</c> says otherwise, so a row taller than the
    /// text area is the one case this handles differently — it overflows rather than breaking.
    /// </remarks>
    private void LayoutTable(Cursor cursor, Table table)
    {
        var columns = ComputeColumnWidths(table, cursor.Width, out var exact);
        if (columns.Count == 0) return;

        // A table with a place of its own is taken out of the flow and the text runs round it.
        if (table.Properties.Position is { } position)
        {
            LayoutFloatingTable(cursor, table, position, columns);
            return;
        }

        var properties = table.Properties;
        var totalWidth = columns.Sum();

        // A declared indent is measured to the cell content edge, not to the table edge: the
        // first column's margin and border are absorbed into it rather than added on top. Verified
        // against Word with table-inset-probe, where a 12pt indent put content exactly 12pt from
        // the margin whether the cell declared a 12pt margin, a border, both or neither. Word
        // writes this element on every table it saves, so real documents always take this path.
        var tableLeft = cursor.Left;

        // A table whose columns run the other way is laid from the right-hand margin, and its
        // indent is measured from there as well. Word's own: column-order-probe's mirrored table
        // stands against the right margin, and indenting it by half an inch moves it half an inch
        // to the left rather than to the right.
        if (properties.Mirrored)
        {
            tableLeft += Math.Max(0, cursor.Width - totalWidth);

            if (properties.IndentTwips is { } fromRight)
                tableLeft -= Units.TwipsToPoints(fromRight) - LeadingCellInset(table, columns.Count);
        }
        else
        {
            if (properties.IndentTwips is { } indent)
                tableLeft += Units.TwipsToPoints(indent) - LeadingCellInset(table, columns.Count);

            tableLeft += properties.Justification switch
            {
                Justification.Center => Math.Max(0, cursor.Width - totalWidth) / 2,
                Justification.Right => Math.Max(0, cursor.Width - totalWidth),
                _ => 0
            };
        }

        // A table interrupts the paragraph spacing chain: its own edge is the boundary, so a
        // following paragraph has nothing to collapse against.
        cursor.Y += cursor.PendingSpaceAfter;
        cursor.PendingSpaceAfter = 0;
        cursor.PreviousFormat = null;

        LayoutTableRows(cursor, table, columns, tableLeft, exact: exact);
    }

    /// <summary>
    /// Lays the rows of a table down the page from where the cursor stands, at the left edge it
    /// is given. Separated from the table's own placement so that a floating table can borrow it.
    /// </summary>
    /// <param name="from">The first row to lay, which is not the first where a table was carried
    /// over from the page before.</param>
    /// <param name="floating">
    /// Whether the table stands out of the flow. A floating table breaks at a row rather than
    /// running past the foot of the page: the rows that fit are laid here and the number of the
    /// first that did not is returned, for the next page to carry on from.
    /// </param>
    /// <returns>The row to carry on at, which is the count of rows where none was left.</returns>
    private int LayoutTableRows(
        Cursor cursor, Table table, List<double> columns, double tableLeft,
        int from = 0, bool floating = false, List<double>? exact = null)
    {
        var properties = table.Properties;

        // What the rows of a merged table have to give up for the widest of them to fit.
        var squeeze = MergedSqueeze(table, columns);

        // Merged runs open here and close some rows further down, so they outlive any one row.
        var merges = new Dictionary<int, OpenMerge>();

        // The rows Word puts again at the top of every page the table runs onto: the run of them
        // at the very top of the table, and only that run. table-heading-probe asks the four
        // questions this answers — one heading repeats, two repeat, a row marked further down the
        // table does not repeat at all, and a table that is nothing but headings repeats none of
        // them, since there would be no body to put under them.
        var headings = 0;
        while (headings < table.Rows.Count && table.Rows[headings].IsHeader == true) headings++;
        if (headings == table.Rows.Count) headings = 0;

        // Puts them there, and says how much of the page they took. Their footnotes are not
        // collected again: a note called for in a heading belongs to the page the heading was
        // first written on, and Word does not repeat it either.
        double RepeatHeadings()
        {
            var taken = 0.0;

            for (var i = 0; i < headings; i++)
            {
                var cells = MeasureRow(table, table.Rows[i], i, columns, tableLeft, squeeze, exact);
                if (cells.Count == 0) continue;

                var height = ComputeRowHeight(table.Rows[i], cells);

                // A heading that would fill the page it is repeated on is not repeated: what is
                // left has to hold something of the table besides the heading.
                if (cursor.Y + height > cursor.ContentBottom) break;

                PlaceRow(cursor, cells, cursor.Y, height, properties.Mirrored);
                cursor.Y += height;
                taken += height;
            }

            return taken;
        }

        for (var rowIndex = from; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var placed = MeasureRow(table, row, rowIndex, columns, tableLeft, squeeze, exact);
            if (placed.Count == 0) continue;

            // A row that ends a merged run has to be tall enough for whatever of that run's
            // content the rows above it did not account for.
            double HeightOf(List<PlacedCell> cells) =>
                ComputeRowHeight(row, cells, PendingMergeHeight(merges, cells, cursor.Y));

            var rowHeight = HeightOf(placed);

            // A floating table breaks at the foot of the page and carries on at the top of the
            // next, which is what Word does with one: floating-table-break-probe puts twenty rows
            // where six of them fit and Word writes six, then fourteen. One row goes down whatever
            // is left, so that a page always takes something and the table cannot carry for ever.
            if (floating && rowIndex > from && cursor.Y + rowHeight > cursor.ContentBottom + 0.001)
                return rowIndex;

            // Opened before the row is drawn, so that a merged cell's fill goes into the page
            // underneath the borders of every row it runs through rather than over the top of
            // them — and before the row is divided, so that dividing it divides the run too.
            OpenMerges(cursor, merges, placed);

            // A cell's contents were composed on a page of their own, so any footnote they refer
            // to is still waiting to be given to the page the row lands on.
            var rowFootnoteIds = placed.SelectMany(cell => cell.Content.Footnotes).ToList();
            if (cursor.FootnoteSink is not null) cursor.FootnoteSink.AddRange(rowFootnoteIds);

            var rowFootnotes = cursor.FootnoteSink is null && rowFootnoteIds.Count > 0
                ? PrepareFootnotes(cursor, rowFootnoteIds)
                : Prepared.None;

            // A row too tall for what is left of the page is broken across the two unless it says
            // it may not be, and what will not fit follows on the next page as a row of its own —
            // bordered like one, which is how Word draws it. A row taller than a whole page is
            // broken again and again, which is why splitting counts as progress on a fresh page
            // where moving the row would not.
            var placedEverything = false;

            while (cursor.Paginate && cursor.Y + rowHeight > cursor.ContentBottom - rowFootnotes.Height)
            {
                if (row.CantSplit != true &&
                    SplitRow(cursor, placed, rowFootnotes.Height, out var fitted, out var remaining,
                        out var fittedHeight))
                {
                    PlaceRow(cursor, fitted, cursor.Y, fittedHeight, properties.Mirrored);
                    cursor.Y += fittedHeight;

                    // A merged run reaching down through this row has just been given as much of
                    // the page as the part that stayed behind.
                    foreach (var merge in merges.Values) merge.Bottom = cursor.Y;

                    placed = remaining;

                    // A merged cell's content is never used up by the row it is in: the run holds
                    // it, and carries what is left over the page break.
                    if (placed.All(cell => cell.MergedBelow || cell.Content.Height <= 0))
                    {
                        placedEverything = true;
                        break;
                    }
                }
                else if (!cursor.CanAdvance)
                {
                    // Nowhere better to put it: the row is placed where it stands and runs over.
                    break;
                }

                cursor.AdvanceColumn();

                // Anything still merged began on the page just left behind. It is closed off
                // there, with as much of its content as that page had room for, and opens again
                // at the top of this one holding the rest.
                foreach (var merge in merges.Values) merge.CarryOver(cursor);

                // The heading rows go at the top of the fresh page, above whatever of the table
                // follows — but not above themselves, where it is a heading that has moved.
                if (headings > 0 && rowIndex >= headings) RepeatHeadings();

                rowHeight = HeightOf(placed);
                if (rowFootnotes.Flows.Count > 0) rowFootnotes = PrepareFootnotes(cursor, rowFootnoteIds);
            }

            if (!placedEverything) PlaceRow(cursor, placed, cursor.Y, rowHeight, properties.Mirrored);

            CommitFootnotes(cursor, rowFootnotes, placedEverything ? 0 : rowHeight);
            if (!placedEverything) cursor.Y += rowHeight;

            CloseMerges(merges, placed, cursor.Y);

            // The final row's bottom edge is not shared with anything below it, so it is the one
            // border that adds to the table's overall height.
            if (rowIndex == table.Rows.Count - 1)
            {
                var bottom = 0.0;
                foreach (var cell in placed) bottom = Math.Max(bottom, BorderWidth(cell.Borders.Bottom));
                cursor.Y += bottom;
            }
        }

        return table.Rows.Count;
    }

    /// <summary>
    /// Draws a row: its shading, then its cells' contents, then its borders on top — a border
    /// sits on the cell edge and would otherwise be half-covered by the neighbouring cell's fill.
    /// </summary>
    private static void PlaceRow(
        Cursor cursor, List<PlacedCell> placed, double top, double height, bool mirrored = false)
    {
        // A cell merged with the row below has neither fill nor content of its own here: both
        // belong to the run, and are drawn when it closes.
        foreach (var cell in placed)
        {
            if (cell.MergedBelow || cell.Source.ShadingPaint is not { } fill) continue;

            cursor.Page.Rectangles.Add(new PositionedRectangle
            {
                X = cell.Left,
                Y = top,
                Width = cell.Width,
                Height = height,
                Color = fill
            });
        }

        foreach (var cell in placed)
        {
            if (cell.MergedBelow) continue;

            // A turned cell's content was laid in a frame of its own, turned a quarter circle: it
            // is put back the same way, and what its vertical alignment moves is the stack of
            // lines across the cell rather than down it.
            if (cell.Source.TextDirection != CellTextDirection.LeftToRight)
            {
                var across = cell.Width - cell.MarginLeft - cell.MarginRight;
                var stack = VerticalOffset(cell, across);

                cell.Content.PlaceTurnedOnto(
                    cursor.Page, cell.Source.TextDirection,
                    cell.Left + cell.MarginLeft + stack,
                    top + cell.MarginTop,
                    cell.Left + cell.Width - cell.MarginRight - stack,
                    top + height - cell.MarginBottom);

                continue;
            }

            var offset = VerticalOffset(cell, height - cell.MarginTop - cell.MarginBottom);

            cell.Content.PlaceOnto(cursor.Page, cell.Left + cell.MarginLeft, top + cell.MarginTop + offset);
        }

        DrawRowBorders(cursor.Page, placed, top, height, mirrored);
    }

    /// <summary>
    /// Where a cell's content sits in the height it has been given, by its vertical alignment.
    /// </summary>
    private static double VerticalOffset(PlacedCell cell, double available) =>
        cell.Source.VerticalAlignment switch
        {
            VerticalCellAlignment.Center => Math.Max(0, (available - cell.Content.Height) / 2),
            VerticalCellAlignment.Bottom => Math.Max(0, available - cell.Content.Height),
            _ => 0
        };

    /// <summary>
    /// What a merged run ending in this row still needs of it: the run's content, less the rows it
    /// has already run through.
    /// </summary>
    private static double PendingMergeHeight(
        Dictionary<int, OpenMerge> merges, List<PlacedCell> placed, double rowTop)
    {
        var needed = 0.0;

        foreach (var cell in placed)
        {
            if (cell.MergedBelow || !merges.TryGetValue(cell.Column, out var merge)) continue;

            needed = Math.Max(needed, merge.Outstanding(rowTop));
        }

        return needed;
    }

    /// <summary>Opens a run for each cell in this row that is merged with the row below it.</summary>
    private static void OpenMerges(
        Cursor cursor, Dictionary<int, OpenMerge> merges, List<PlacedCell> placed)
    {
        foreach (var cell in placed)
        {
            // A cell in the middle of a run is merged with the row below as well, and belongs to
            // the run already open rather than starting another.
            if (!cell.MergedBelow || merges.ContainsKey(cell.Column)) continue;

            merges[cell.Column] = new OpenMerge(cell, cursor.Page, cursor.Y);
        }
    }

    /// <summary>
    /// Carries every open run down past this row, and draws the ones that end in it.
    /// </summary>
    private static void CloseMerges(
        Dictionary<int, OpenMerge> merges, List<PlacedCell> placed, double rowBottom)
    {
        foreach (var merge in merges.Values) merge.Bottom = rowBottom;

        foreach (var cell in placed)
        {
            if (cell.MergedBelow || !merges.TryGetValue(cell.Column, out var merge)) continue;

            merge.Close(rowBottom);
            merges.Remove(cell.Column);
        }
    }

    /// <summary>
    /// Divides a row between the page it is on and the next, at a line boundary inside its cells.
    /// </summary>
    /// <remarks>
    /// Word breaks a row rather than moving it whole unless it is told not to, and what stays
    /// behind is closed off with a full border box as though the row ended there — which is what
    /// its own export shows, and what falls out of drawing each part as a row.
    ///
    /// Nothing is split unless a line of it actually fits. A row that would leave an empty box at
    /// the foot of the page moves whole instead.
    /// </remarks>
    private static bool SplitRow(
        Cursor cursor, List<PlacedCell> placed, double reserved,
        out List<PlacedCell> fitted, out List<PlacedCell> remaining, out double fittedHeight)
    {
        fitted = [];
        remaining = [];
        fittedHeight = 0;

        var available = cursor.ContentBottom - reserved - cursor.Y;
        if (available <= 0) return false;

        var anything = false;

        foreach (var cell in placed)
        {
            // A merged cell's content belongs to the run rather than to this row, and is divided
            // with the run when the page ends rather than here.
            if (cell.MergedBelow)
            {
                fitted.Add(cell);
                remaining.Add(cell);
                continue;
            }

            var (top, rest) = cell.Content.SplitAt(available - cell.MarginTop - cell.MarginBottom);

            fitted.Add(cell with { Content = top });
            remaining.Add(cell with { Content = rest });

            if (top.Height > 0)
            {
                anything = true;
                fittedHeight = Math.Max(fittedHeight, top.Height + cell.MarginTop + cell.MarginBottom);
            }
        }

        return anything && fittedHeight > 0;
    }

    /// <summary>
    /// The columns a row is laid on: the table's own, or the ones the row was written with where
    /// it was folded in from a table that used to follow this one.
    /// </summary>
    private static List<double> RowColumns(Table table, TableRow row, List<double> columns)
    {
        if (row.Grid is not { Count: > 0 } own) return columns;

        var mine = own.Select(twips => Units.TwipsToPoints(twips)).Where(width => width > 0).ToList();

        return mine.Count > 0 ? mine : columns;
    }

    /// <summary>
    /// How far in a row of a merged table stands, over and above wherever the table itself does.
    /// </summary>
    /// <remarks>
    /// An indent names the edge the cell's <em>text</em> stands at rather than the edge of the
    /// table, so the border and the cell margin are absorbed into it — the same rule an ordinary
    /// table's own indent follows, and the reason a row indented by half an inch has its border
    /// drawn a shade less than that in. A row asking for no indent at all is not moved: absent and
    /// zero are the same thing here, since the table has already been put where its own indent
    /// says.
    /// </remarks>
    private double RowIndent(Table table, TableRow row, int columnCount) =>
        row.IndentTwips is { } indent && indent > 0
            ? Math.Max(0, Units.TwipsToPoints(indent) - LeadingCellInset(table, columnCount))
            : 0;

    /// <summary>
    /// How much the rows of a merged table are squeezed so that the widest of them fits the width
    /// the table declares.
    /// </summary>
    /// <remarks>
    /// Word reads two tables written one after the other as one, and a row folded in from the
    /// second that will not fit — because its own columns are wider, or because it asks to be
    /// indented, or both — does not overrun: Word squeezes the whole merged table, every row's
    /// columns and every row's indent together, until the widest of them ends exactly at the width
    /// the first table declared. Measured from merged-indent-probe, ten pages:
    ///
    ///   a second table indented 18, 36, 72 and 108 points, against a first 216 points wide, comes
    ///   out at scales of 0.925, 0.859, 0.751 and 0.668 — which is 216 divided by the widest row
    ///   each time, that row being its indent (less the inset the indent absorbs) and its columns
    ///
    ///   a second table 270 points wide and not indented at all squeezes just the same, to 0.8,
    ///   so it is the width that decides it and not the indent
    ///
    ///   a second table narrow enough to fit indent and all is left alone
    ///
    ///   a first table declaring itself 180 points wide over a grid of 216 squeezes to 180, so
    ///   what the rows are fitted to is the width the table declares rather than what its own
    ///   columns come to
    ///
    /// Everything lands within a third of a point of Word, which is Word's own rounding of the
    /// share each column takes of the squeezed total: its first column comes out a whisker
    /// narrower than two thirds every time, and what decides that is not measurable from here.
    /// </remarks>
    private double MergedSqueeze(Table table, List<double> columns)
    {
        // Only a merged table has rows carrying a width or an indent of their own, and only a
        // merged table pays for any of this.
        if (!table.Rows.Any(row => row.Grid is { Count: > 0 } || row.IndentTwips is > 0)) return 1;

        var declared = table.Properties.WidthTwips is { } width and > 0
            ? Units.TwipsToPoints(width)
            : columns.Sum();

        if (declared <= 0) return 1;

        var widest = 0.0;

        foreach (var row in table.Rows)
        {
            var mine = RowColumns(table, row, columns);

            widest = Math.Max(widest, RowIndent(table, row, mine.Count) + mine.Sum());
        }

        return widest > declared + 0.01 ? declared / widest : 1;
    }

    /// <summary>Lays out each cell of a row into its own detached page and records its geometry.</summary>
    private List<PlacedCell> MeasureRow(
        Table table, TableRow row, int rowIndex, List<double> columns, double tableLeft,
        double squeeze = 1, List<double>? exact = null)
    {
        var properties = table.Properties;
        var placed = new List<PlacedCell>(row.Cells.Count);

        // The columns as the arithmetic left them, which is what the content is broken against —
        // see ComputeColumnWidths. A row carrying a grid of its own is measured by that grid and
        // has nothing for the grid of the table to say about it.
        var measured = exact is null || row.Grid is { Count: > 0 } ? columns : exact;

        // A row folded in from the table that used to follow this one keeps the columns and the
        // indent it was written with, which is what Word does with two tables written one after
        // the other; where the widest of them will not fit, every row of the merged table is
        // squeezed until it does. Neither is set on a row of a table nobody folded.
        measured = [.. RowColumns(table, row, measured).Select(width => width * squeeze)];
        columns = [.. RowColumns(table, row, columns).Select(width => width * squeeze)];

        tableLeft += RowIndent(table, row, columns.Count) * squeeze;

        // A mirrored table is filled from its right-hand end: the first cell of the row is the
        // rightmost, and each that follows stands to the left of the last.
        var mirrored = properties.Mirrored;
        var x = mirrored ? tableLeft + columns.Sum() : tableLeft;
        var column = 0;

        foreach (var cell in row.Cells)
        {
            if (column >= columns.Count) break;

            var span = Math.Min(cell.GridSpan, columns.Count - column);

            var width = 0.0;
            var inside = 0.0;

            for (var i = 0; i < span; i++)
            {
                width += columns[column + i];
                inside += column + i < measured.Count ? measured[column + i] : columns[column + i];
            }

            if (mirrored) x -= width;

            var borders = ResolveCellBorders(table, cell, rowIndex, column, span, columns.Count);

            // How far content sits inside a cell edge: whichever is the greater of the margin and
            // the half of the border that falls inside the cell. The two do not add — a margin
            // wide enough to clear the border swallows it — which is what
            // table-inset-weights-probe shows at every weight from a quarter point to three.
            var marginLeft = CellInset(
                cell.MarginLeftTwips ?? properties.CellMarginLeft, borders.Left);
            var marginRight = CellInset(
                cell.MarginRightTwips ?? properties.CellMarginRight, borders.Right);
            // The top is not the same: there Word puts the content a whole border below the
            // edge rather than half of one, which the same probe shows at every weight.
            var marginTop = Units.TwipsToPoints(cell.MarginTopTwips ?? properties.CellMarginTop)
                            + BorderWidth(borders.Top);
            // The bottom border is deliberately not counted here. Adjacent rows share an edge —
            // one row's bottom border is the next row's top border — so charging it to both
            // makes every row a border-width too tall and the error accumulates down the table.
            // The last row's bottom border is added once, after the loop.
            var marginBottom = Units.TwipsToPoints(cell.MarginBottomTwips ?? properties.CellMarginBottom);

            // A formula in a cell reads the table around it, so which cell it is in has to be
            // known while its content is being laid out.
            var outer = Fields.Cells;
            Fields.Cells = new TableCells(table, rowIndex, column);

            // A cell continuing a vertical merge draws no content of its own; the cell that
            // started the merge owns it.
            var content = cell.VerticalMerge == "continue"
                ? DetachedFlow.Empty
                : MeasureInside(cell.Content, Math.Max(1, inside - marginLeft - marginRight));

            Fields.Cells = outer;

            placed.Add(new PlacedCell(cell, x, width, column, span, content,
                marginLeft, marginRight, marginTop, marginBottom, borders,
                MergedBelow(table, rowIndex, column)));

            if (!mirrored) x += width;
            column += span;
        }

        // A turned cell's line runs along the row's height, so it can only be composed once the
        // height is known — and the height is settled by the cells that are not turned, because
        // Word does not grow a row to hold turned text. cell-direction-probe shows what that costs
        // where the text is long: a turned cell in a row one line tall wraps every two characters
        // and runs out of the cell to the right of it, and Word draws it there.
        for (var i = 0; i < placed.Count; i++)
        {
            var cell = placed[i];
            if (cell.Source.TextDirection == CellTextDirection.LeftToRight) continue;
            if (cell.MergedBelow || cell.Source.VerticalMerge == "continue") continue;

            var along = ComputeRowHeight(row, placed) - cell.MarginTop - cell.MarginBottom;

            var outer = Fields.Cells;
            Fields.Cells = new TableCells(table, rowIndex, cell.Column);

            placed[i] = cell with
            {
                Content = MeasureInside(cell.Source.Content, Math.Max(1, along))
            };

            Fields.Cells = outer;
        }

        return placed;
    }

    /// <summary>
    /// How tall a row is: its tallest cell, or what a declared height asks for.
    /// </summary>
    /// <param name="pending">
    /// What a merged run ending in this row still needs, over and above the rows it has already
    /// run through. A merged cell's own content does not count towards the row it begins in — the
    /// run's height belongs to its last row, which is where Word puts the overflow.
    /// </param>
    private static double ComputeRowHeight(TableRow row, List<PlacedCell> placed, double pending = 0)
    {
        var natural = pending;
        foreach (var cell in placed)
        {
            if (cell.MergedBelow) continue;

            // A turned cell asks for nothing: its lines stack across the row rather than down it,
            // and Word makes no more room for them than the row already has.
            if (cell.Source.TextDirection != CellTextDirection.LeftToRight) continue;

            natural = Math.Max(natural, cell.Content.Height + cell.MarginTop + cell.MarginBottom);
        }

        if (row.HeightTwips is not { } declared) return natural;

        var height = Units.TwipsToPoints(declared);
        return row.HeightRule switch
        {
            RowHeightRule.Exact => height,
            RowHeightRule.AtLeast => Math.Max(natural, height),
            _ => natural
        };
    }

    /// <summary>
    /// Draws the borders of one row's cells.
    /// </summary>
    /// <remarks>
    /// Each cell draws all four of its own edges, so a shared edge is drawn twice. That is
    /// deliberate: resolving which of two adjacent cells owns an edge is Word's conflict
    /// resolution, and drawing the same black line twice is invisible, whereas getting the
    /// ownership wrong leaves gaps.
    /// </remarks>
    private static void DrawRowBorders(
        LaidOutPage page, List<PlacedCell> placed, double top, double height, bool mirrored = false)
    {
        foreach (var cell in placed)
        {
            AddEdge(page, cell.Borders.Top, cell.Left, top, cell.Width, horizontal: true);

            // No line between a merged cell and the one below it: that is what makes the run read
            // as one tall cell. Word's own export draws the inside rule across every column but
            // the merged one.
            if (!cell.MergedBelow)
                AddEdge(page, cell.Borders.Bottom, cell.Left, top + height, cell.Width, horizontal: true);

            // A table whose columns run the other way draws the upright pair the other way round
            // with them: what a cell calls its left border is drawn on its right. Where the text
            // inside sits is not turned about with it — Word insets the content of a mirrored
            // cell by the border it calls its left however that border is drawn, which
            // column-order-probe shows twice over.
            var leading = mirrored ? cell.Borders.Right : cell.Borders.Left;
            var trailing = mirrored ? cell.Borders.Left : cell.Borders.Right;

            AddEdge(page, leading, cell.Left, top, height, horizontal: false);
            AddEdge(page, trailing, cell.Left + cell.Width, top, height, horizontal: false);
        }
    }

    /// <summary>
    /// Works out which border applies to each edge of a cell. A cell's own border wins; failing
    /// that an outer edge takes the table's matching border and an inner edge takes the
    /// corresponding inside border.
    /// </summary>
    private static CellBorders ResolveCellBorders(
        Table table, TableCell cell, int rowIndex, int column, int span, int columnCount)
    {
        var borders = table.Properties.Borders;
        var isFirstRow = rowIndex == 0;
        var isLastRow = rowIndex == table.Rows.Count - 1;
        var isFirstColumn = column == 0;
        var isLastColumn = column + span >= columnCount;

        var top = cell.Borders.Top ?? (isFirstRow ? borders.Top : borders.InsideHorizontal);

        // A cell continuing a vertical merge has no line above it, which is half of what makes a
        // run of merged cells read as one tall cell. The other half is at the foot of a merged
        // cell, and is left to the drawing: the edge is resolved here either way, because a run
        // interrupted by the end of a page is closed off with it.
        if (cell.VerticalMerge == "continue") top = null;

        var bottom = cell.Borders.Bottom ?? (isLastRow ? borders.Bottom : borders.InsideHorizontal);

        return new CellBorders(
            cell.Borders.Left ?? (isFirstColumn ? borders.Left : borders.InsideVertical),
            cell.Borders.Right ?? (isLastColumn ? borders.Right : borders.InsideVertical),
            top,
            bottom);
    }

    /// <summary>
    /// Whether the cell at this position is merged with the row below it: the next row down holds
    /// a cell at the same column saying it continues a merge.
    /// </summary>
    private static bool MergedBelow(Table table, int rowIndex, int column)
    {
        if (rowIndex + 1 >= table.Rows.Count) return false;

        return CellAt(table.Rows[rowIndex + 1], column)?.VerticalMerge == "continue";
    }

    /// <summary>The cell covering the given grid column of a row, allowing for horizontal spans.</summary>
    private static TableCell? CellAt(TableRow row, int column)
    {
        var at = 0;

        foreach (var cell in row.Cells)
        {
            var span = Math.Max(1, cell.GridSpan);
            if (column < at + span) return cell;

            at += span;
        }

        return null;
    }

    /// <summary>
    /// How far the first column's content sits inside the table's left edge: its left cell margin
    /// plus its left border.
    /// </summary>
    /// <summary>
    /// Half of each line the table is drawn round with, which is how far its outer edge stands
    /// outside the box the rows are laid in.
    /// </summary>
    /// <remarks>
    /// It is the outer edge that the daylight round a floating table is measured from, not the
    /// box: floating-table-probe draws the same table with a half point border and a three point
    /// one, and the text beside the thick one stands a point and a half further out.
    /// </remarks>
    private static (double Left, double Top, double Right, double Bottom) OuterBorderHalves(
        Table table, int columnCount)
    {
        var firstRow = table.Rows.FirstOrDefault();
        var first = firstRow?.Cells.FirstOrDefault();
        if (firstRow is null || first is null) return (0, 0, 0, 0);

        var span = Math.Min(Math.Max(1, first.GridSpan), Math.Max(1, columnCount));
        var leading = ResolveCellBorders(table, first, 0, 0, span, columnCount);

        var lastRow = table.Rows[^1];
        var last = firstRow.Cells[^1];
        var lastSpan = Math.Min(Math.Max(1, last.GridSpan), Math.Max(1, columnCount));

        var trailing = ResolveCellBorders(
            table, last, 0, Math.Max(0, columnCount - lastSpan), lastSpan, columnCount);

        var bottom = lastRow.Cells.FirstOrDefault() is { } foot
            ? ResolveCellBorders(table, foot, table.Rows.Count - 1, 0,
                Math.Min(Math.Max(1, foot.GridSpan), Math.Max(1, columnCount)), columnCount)
            : leading;

        return (BorderWidth(leading.Left) / 2, BorderWidth(leading.Top) / 2,
            BorderWidth(trailing.Right) / 2, BorderWidth(bottom.Bottom) / 2);
    }

    private static double LeadingCellInset(Table table, int columnCount)
    {
        var first = table.Rows.FirstOrDefault()?.Cells.FirstOrDefault();
        if (first is null) return 0;

        var span = Math.Min(Math.Max(1, first.GridSpan), Math.Max(1, columnCount));
        var borders = ResolveCellBorders(table, first, 0, 0, span, columnCount);

        return CellInset(first.MarginLeftTwips ?? table.Properties.CellMarginLeft, borders.Left);
    }

    private static double BorderWidth(BorderEdge? edge) =>
        edge is not null && edge.IsVisible ? edge.WidthPoints : 0;

    /// <summary>
    /// How far inside a cell edge its content starts: the margin, or the half of the border that
    /// falls inside the cell where that is further in.
    /// </summary>
    private static double CellInset(int marginTwips, BorderEdge? edge) =>
        Math.Max(Units.TwipsToPoints(marginTwips), BorderWidth(edge) / 2);

    private static void AddEdge(
        LaidOutPage page, BorderEdge? edge, double x, double y, double length, bool horizontal)
    {
        if (edge is null || !edge.IsVisible) return;

        var thickness = edge.WidthPoints;

        page.Rectangles.Add(new PositionedRectangle
        {
            // Edges are centred on the cell boundary, so half the line falls either side of it.
            X = horizontal ? x : x - thickness / 2,
            Y = horizontal ? y - thickness / 2 : y,
            Width = horizontal ? length : thickness,
            Height = horizontal ? thickness : length,
            Color = edge.GetColor()
        });
    }

    /// <summary>
    /// Determines the width of each grid column in points.
    /// </summary>
    /// <remarks>
    /// The table grid is authoritative when present. Failing that the first row's declared cell
    /// widths are used, and failing that the available width is divided evenly. A grid wider than
    /// the text area is scaled down rather than allowed to run off the page.
    /// </remarks>
    private List<double> ComputeColumnWidths(Table table, double availableWidth) =>
        ComputeColumnWidths(table, availableWidth, out _);

    /// <param name="exact">
    /// The same columns before the grid took hold of them. What a cell's content is broken against
    /// is this and not what is drawn: Word lays a table out against the widths the arithmetic gave
    /// and writes it on the grid, which is why a word can end a hair past the column it is in
    /// rather than being broken to fit what was drawn. break-tolerance-probe says there is no
    /// tolerance in the breaking itself — a word a twentieth of a point too wide for the measure
    /// is broken — so the hair has to come from somewhere, and this is where.
    /// </param>
    private List<double> ComputeColumnWidths(
        Table table, double availableWidth, out List<double> exact)
    {
        exact = ComputeColumnWidthsExactly(table, availableWidth, out _);

        return OnTheGrid(exact);
    }

    /// <summary>
    /// Puts every column edge on the grid, which is where Word writes them.
    /// </summary>
    /// <remarks>
    /// It is the edges that are put there and not the widths, so a column's width is whatever the
    /// gap between two snapped edges comes to and no two columns of the same declared width need
    /// be equal. column-grid-probe says so plainly: three columns declaring fifty points each —
    /// 208 steps and a third — come out of Word 49.92, 50.16 and 49.92, which is exactly where
    /// 122, 172 and 222 land when each is rounded to the nearest step. Five of its six pages
    /// follow that rule to the last hundredth, over declared widths, awkward widths, a stated
    /// grid, and widths scaled down to fit the measure.
    ///
    /// The sixth is the one where the columns are sized by their contents, and it is a step out on
    /// one edge in three. That is not the text: text-measure-probe shows our measure of a run to be
    /// Word's exactly, to a ten-thousandth of a point over forty letters. It is what Word makes of
    /// a *cell's* content width, which its own page says is rounded up to a whole twip before the
    /// edges accumulate — 3.35, 10.7 and 8.7 rather than 3.334, 10.670 and 8.666, which lands all
    /// three of its edges. Not implemented, because the same page also shows Word keeping a word
    /// in a column a fiftieth of a point too narrow for it rather than breaking it, and that
    /// tolerance has not been measured.
    ///
    /// The table's own left edge is taken as being on the grid, which it is for any indent a
    /// document actually states.
    /// </remarks>
    private static List<double> OnTheGrid(List<double> widths)
    {
        var snapped = new List<double>(widths.Count);
        var exact = 0.0;
        var placed = 0.0;

        foreach (var width in widths)
        {
            exact += width;

            var edge = Grid.Snap(exact);
            snapped.Add(edge - placed);
            placed = edge;
        }

        return snapped;
    }

    private List<double> ComputeColumnWidthsExactly(
        Table table, double availableWidth, out double[]? floors)
    {
        floors = null;

        // Word ignores the declared grid entirely when a table is left on autofit, which is its
        // default. Measured: a table given an equal-width grid produced exactly the same columns
        // as the same table with no grid at all.
        if (table.Properties.FixedLayout != true && table.Rows.Count > 0)
            return ComputeAutofitColumnWidths(table, availableWidth, out floors);

        var widths = table.Grid.Select(twips => Units.TwipsToPoints(twips)).Where(w => w > 0).ToList();

        if (widths.Count == 0 && table.Rows.Count > 0)
        {
            var first = table.Rows[0];
            var columns = first.Cells.Sum(c => Math.Max(1, c.GridSpan));

            foreach (var cell in first.Cells)
            {
                var span = Math.Max(1, cell.GridSpan);
                var width = cell.WidthTwips is { } declared and > 0
                    ? Units.TwipsToPoints(declared)
                    : availableWidth / Math.Max(1, columns);

                for (var i = 0; i < span; i++) widths.Add(width / span);
            }
        }

        if (widths.Count == 0) widths.Add(availableWidth);

        var total = widths.Sum();
        if (total > availableWidth + 0.01 && total > 0)
        {
            var scale = availableWidth / total;
            for (var i = 0; i < widths.Count; i++) widths[i] *= scale;
        }

        return widths;
    }

    /// <summary>
    /// Sizes columns from their contents, the way Word does when a table is left on autofit.
    /// </summary>
    /// <remarks>
    /// Each column is measured for the width it needs at minimum — its widest unbreakable word —
    /// and the width it would like, which is its content unwrapped, or the width its cells asked
    /// for where any of them did. If every column can have what it wants the table is only as wide
    /// as that; otherwise the columns start at their minimums and share out what is left in
    /// proportion to how much more each one asked for.
    ///
    /// A declared width (<c>w:tcW</c>) is a preference and not a measurement, which
    /// table-width-probe settles in five pages: widths that fit are taken exactly (72, 108 and 144
    /// points come out as those); a column whose content will not fit the width it asks for grows
    /// to hold it and its neighbours keep theirs (36/36/36 with an unbreakable word in the middle
    /// comes out 36/142.56/36); widths adding to more than the measure are scaled down together
    /// (three of 200 come out three of 156); a column asking for nothing is sized by its content
    /// beside ones that ask; and where two rows ask for different widths the wider wins.
    ///
    /// This is an approximation. It reproduces the two behaviours that were measured directly —
    /// content-width columns when the table fits, and a table filling the text area exactly when
    /// it does not — but Word's own algorithm is undocumented and this does not match it to the
    /// fraction of a point that the paragraph-level rules do. See the table-autofit-probe fixture
    /// for how far apart they are.
    /// </remarks>
    private List<double> ComputeAutofitColumnWidths(
        Table table, double availableWidth, out double[]? floors)
    {
        floors = null;

        var columnCount = 0;
        foreach (var row in table.Rows)
        {
            var count = row.Cells.Sum(cell => Math.Max(1, cell.GridSpan));
            columnCount = Math.Max(columnCount, count);
        }

        columnCount = Math.Max(columnCount, table.Grid.Count);
        if (columnCount == 0) return [availableWidth];

        var minimums = new double[columnCount];
        var maximums = new double[columnCount];

        // What the cells of each column asked for, which stands in place of the content's own
        // width where anything asked at all, and the share of the table any of them asked for
        // instead.
        var preferred = new double[columnCount];
        var shares = new double[columnCount];

        var properties = table.Properties;

        foreach (var row in table.Rows)
        {
            var column = 0;

            foreach (var cell in row.Cells)
            {
                if (column >= columnCount) break;

                var span = Math.Min(Math.Max(1, cell.GridSpan), columnCount - column);
                var (min, max) = MeasureBlockWidths(cell.Content);

                // Padding is part of what the column has to accommodate, and it has to be the
                // same padding layout will subtract later — margins and borders both. Counting
                // only the margins here sized columns that could never fit their own contents,
                // so a cell with borders wrapped text that was measured to fit.
                var borders = ResolveCellBorders(table, cell, 0, column, span, columnCount);
                var padding =
                    Units.TwipsToPoints(cell.MarginLeftTwips ?? properties.CellMarginLeft) +
                    Units.TwipsToPoints(cell.MarginRightTwips ?? properties.CellMarginRight) +
                    BorderWidth(borders.Left) + BorderWidth(borders.Right);

                // What a cell wants is rounded up to a whole twip before any of it is shared out,
                // which is Word's own arithmetic showing through: column-grid-probe's three
                // content-sized columns want 3.334, 10.670 and 8.666 points and Word puts their
                // edges where 3.35, 10.7 and 8.7 put them.
                min = ToWholeTwip(min + padding);
                max = ToWholeTwip(max + padding);

                // A declared width names the cell's own edge to edge, padding included, which is
                // what the columns here are measured in.
                var asked = cell.PreferredWidthPoints;
                var share = cell.PreferredWidthShare;

                if (span == 1)
                {
                    minimums[column] = Math.Max(minimums[column], min);
                    maximums[column] = Math.Max(maximums[column], max);

                    // Two rows asking for different widths of one column: the wider wins.
                    if (asked is { } width) preferred[column] = Math.Max(preferred[column], width);
                    if (share is { } part) shares[column] = Math.Max(shares[column], part);
                }
                else
                {
                    // A spanning cell constrains its columns only as a group: it is satisfied as
                    // long as they add up, so it is spread evenly and only where they fall short.
                    SpreadAcrossSpan(minimums, column, span, min);
                    SpreadAcrossSpan(maximums, column, span, max);

                    if (asked is { } width) SpreadAcrossSpan(preferred, column, span, width);
                }

                column += span;
            }
        }

        for (var i = 0; i < columnCount; i++)
        {
            // A column that asked for a width takes it in place of what its content would have
            // wanted — narrower as readily as wider, since asking for less is how a document
            // makes a column wrap. What it cannot do is ask for less than the content's own
            // minimum: an unbreakable word has to go somewhere, and Word gives the column to it.
            if (preferred[i] > 0) maximums[i] = preferred[i];

            maximums[i] = Math.Max(maximums[i], minimums[i]);
        }

        floors = minimums;

        var totalMax = maximums.Sum();

        // A table that states its own width is made that wide, whether that is wider than its
        // contents want or narrower, and whether or not it fits the page. A table that states
        // none but whose cells ask for shares of it is made as narrow as those shares allow.
        var stated = StatedTableWidth(table, availableWidth) ?? WidthTheSharesNeed(shares, maximums, availableWidth);

        if (stated is { } target) return ShareOut(target, minimums, maximums, preferred, shares);

        var limit = availableWidth;

        // Nothing stated: the table is only as wide as its contents, where they fit.
        if (totalMax <= limit) return [.. maximums];

        var totalMin = minimums.Sum();

        // Not even the minimums fit; scale them down together rather than overflow the page.
        if (totalMin >= limit)
        {
            var scale = totalMin > 0 ? limit / totalMin : 0;
            return [.. minimums.Select(m => m * scale)];
        }

        // Start from the minimums and share the remainder in proportion to what each column
        // still wants.
        var slack = limit - totalMin;
        var demand = totalMax - totalMin;

        return [.. Enumerable.Range(0, columnCount)
            .Select(i => minimums[i] + slack * (maximums[i] - minimums[i]) / demand)];
    }

    /// <summary>
    /// The width a table states for itself, or null where it leaves that to its contents.
    /// </summary>
    /// <remarks>
    /// table-preferred-width-probe measures what such a width does. It is met exactly, and it is
    /// not held to the page: a table stating twice the measure is written straight off the paper's
    /// edge, which is what Word does with one. A share (<c>w:type="pct"</c>) is a share of the
    /// measure — half of a 468 point column comes out 234.
    ///
    /// How the width is then divided is the part that is fitted rather than derived: the columns
    /// grow in proportion to what each wanted, which follows Word's own division to within about
    /// half a point on the probe — closest where the columns differ most (0.14pt) and furthest
    /// where they are nearly equal (0.55pt). Word's own measurement of what a cell wants is not
    /// the sum of the advances its PDF writes, and nothing measurable from the page reproduces the
    /// last fraction of it. See TablePreferredWidthTests, which states the residual outright.
    /// </remarks>
    private static double? StatedTableWidth(Table table, double availableWidth)
    {
        if (table.Properties.WidthFraction is { } fraction and > 0) return availableWidth * fraction;

        return table.Properties.WidthTwips is { } twips and > 0
            ? Units.TwipsToPoints(twips)
            : null;
    }

    /// <summary>
    /// How wide a table has to be for the shares its cells ask for to hold what they hold, where
    /// the table itself states no width. Null where no cell asks in shares.
    /// </summary>
    /// <remarks>
    /// cell-percent-width-probe measures it: three cells asking for a quarter, a half and a
    /// quarter of a table that states nothing come out 5.28, 10.8 and 5.28 — a table of 21.36
    /// points, which is the narrowest at which a quarter still holds the letter that has to fit in
    /// it. Put a column of text in the middle cell and the same table fills the measure instead,
    /// its half being 234 points wide: the requirement is capped at the room there is.
    /// </remarks>
    private static double? WidthTheSharesNeed(double[] shares, double[] maximums, double availableWidth)
    {
        var needed = 0.0;

        for (var i = 0; i < shares.Length; i++)
        {
            if (shares[i] > 0) needed = Math.Max(needed, maximums[i] / shares[i]);
        }

        return needed > 0 ? Math.Min(availableWidth, needed) : null;
    }

    /// <summary>
    /// Divides a width the table has settled on between its columns.
    /// </summary>
    /// <remarks>
    /// Measured on cell-percent-width-probe and table-preferred-width-probe, in this order:
    ///
    ///   * a column asking for a share of the table takes that share of it, and takes it before
    ///     anything else does. Shares that do not add up to the whole are stretched to fill it,
    ///     but only where every column asks in shares: a half beside a stated 72 points and a
    ///     column asking nothing leaves the half at exactly half. Shares that add up to more than
    ///     the whole are taken in order until it is spent, which is what leaves the second of two
    ///     three-quarters with the remaining quarter.
    ///   * a column stating a width in points takes it;
    ///   * a column asking for nothing takes what its content wants;
    ///   * and whatever is left over goes to the columns that asked for nothing, in proportion to
    ///     what their contents wanted — or, where every column asked for something, is shared out
    ///     among them all in the same proportion. Three columns each stating 72 points inside a
    ///     table stating 324 come out 108 apiece, and a half beside a stated 72 and a column
    ///     asking nothing comes out 162, 72 and 90.
    /// </remarks>
    private static List<double> ShareOut(
        double target, double[] minimums, double[] maximums, double[] preferred, double[] shares)
    {
        var count = maximums.Length;
        var widths = new double[count];

        // Shares that fall short of the whole are stretched to fill it, but only where the whole
        // table is divided in shares.
        var shareTotal = shares.Sum();
        var stretch = shareTotal > 0 && shareTotal < 1 && shares.All(share => share > 0)
            ? 1 / shareTotal
            : 1;

        var spent = 0.0;

        for (var i = 0; i < count; i++)
        {
            if (shares[i] <= 0) continue;

            widths[i] = Math.Max(0, Math.Min(shares[i] * stretch * target, target - spent));
            spent += widths[i];
        }

        // What is left is the room the rest of the columns divide.
        var room = Math.Max(0, target - spent);
        var rest = Enumerable.Range(0, count).Where(i => shares[i] <= 0).ToList();

        if (rest.Count == 0) return [.. widths];

        var wanted = rest.Sum(i => maximums[i]);

        if (wanted >= room)
        {
            // No room to spare: the columns that are left share what there is between them, from
            // their minimums up, which is the ordinary autofit rule.
            var floors = rest.Sum(i => minimums[i]);

            foreach (var i in rest)
            {
                widths[i] = floors >= room
                    ? (floors > 0 ? minimums[i] * room / floors : room / rest.Count)
                    : minimums[i] + (room - floors) * (maximums[i] - minimums[i]) /
                    Math.Max(0.0001, wanted - floors);
            }

            return [.. widths];
        }

        // Room to spare. It goes to the columns that asked for nothing at all; where every column
        // asked, they grow together instead.
        var free = rest.Where(i => preferred[i] <= 0).ToList();
        var growing = free.Count > 0 ? free : rest;
        var share = growing.Sum(i => maximums[i]);

        foreach (var i in rest) widths[i] = maximums[i];

        foreach (var i in growing)
        {
            widths[i] += share > 0
                ? (room - wanted) * maximums[i] / share
                : (room - wanted) / growing.Count;
        }

        return [.. widths];
    }

    /// <summary>The next whole twip at or above a width, twips being what a document is written in.</summary>
    private static double ToWholeTwip(double points) =>
        Units.TwipsToPoints(Math.Ceiling(Units.PointsToTwips(points) - 0.0001));

    private static void SpreadAcrossSpan(double[] widths, int start, int span, double required)
    {
        var current = 0.0;
        for (var i = 0; i < span; i++) current += widths[start + i];
        if (current >= required) return;

        var extra = (required - current) / span;
        for (var i = 0; i < span; i++) widths[start + i] += extra;
    }

    /// <summary>
    /// Measures how narrow a cell's contents can be squeezed and how wide they would like to be.
    /// </summary>
    /// <remarks>
    /// The minimum is the widest single word, since that is the narrowest a column can be without
    /// the text overflowing. The maximum is the whole paragraph on one line. Trailing spaces are
    /// excluded from both — they hang past the edge rather than demanding room.
    /// </remarks>
    private (double Min, double Max) MeasureBlockWidths(IReadOnlyList<BlockElement> blocks)
    {
        var min = 0.0;
        var max = 0.0;

        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                {
                    var format = _styles.ResolveParagraph(paragraph.Properties);
                    var indent = Math.Max(0, format.IndentLeftPoints) + Math.Max(0, format.IndentRightPoints);

                    var line = 0.0;
                    var pendingSpace = 0.0;

                    foreach (var atom in BuildAtoms(paragraph, format))
                    {
                        switch (atom)
                        {
                            case TextAtom { IsSpace: true } space:
                                // Held back: it only counts if more text follows it.
                                pendingSpace += space.Width;
                                break;

                            case TextAtom word:
                                line += pendingSpace + word.Width;
                                pendingSpace = 0;
                                min = Math.Max(min, word.Width + indent);
                                break;

                            case ImageAtom image:
                                line += pendingSpace + image.Width;
                                pendingSpace = 0;
                                min = Math.Max(min, image.Width + indent);
                                break;

                            case TabAtom tab:
                                line += pendingSpace + tab.DefaultIntervalPoints;
                                pendingSpace = 0;
                                break;

                            case BreakAtom:
                                max = Math.Max(max, line + indent);
                                line = 0;
                                pendingSpace = 0;
                                break;
                        }
                    }

                    max = Math.Max(max, line + indent);
                    break;
                }

                case Table nested:
                {
                    // A nested table needs at least the sum of its own columns.
                    var widths = ComputeAutofitColumnWidths(nested, double.MaxValue / 4, out _);
                    var total = widths.Sum();
                    min = Math.Max(min, total);
                    max = Math.Max(max, total);
                    break;
                }
            }
        }

        return (min, max);
    }

    /// <summary>
    /// Draws a chart: its plotting into one drawing, and the words along its axes into one flow
    /// of text over the top.
    /// </summary>
    /// <remarks>
    /// The labels are laid out by the ordinary machinery rather than measured and placed by hand,
    /// which is what centres a category under its bars and ranges the numbers against the axis: a
    /// label is a paragraph in a box, and a paragraph in a box is what the engine does.
    /// </remarks>
    private (Images.ImageData Frame, DetachedFlow? Content, double Left, double Top) ComposeChart(
        ChartDefinition chart, double width, double height)
    {
        // A title's words are set in whatever it names, and what it names may be a slot in the
        // theme rather than a face: the heading font for a title is written "+mj-lt".
        Resolve(chart.Title);
        Resolve(chart.CategoryAxis?.Title);
        Resolve(chart.ValueAxis?.Title);

        var room = ChartComposer.Room(chart, width, height, MeasureLabel, LabelBox, MeasureBlock);

        var plan = ChartComposer.Arrange(
            chart, width, height, MeasureLabel, LabelBox, WrapLabel, MeasureBlock);

        var drawing = ChartComposer.Draw(
            chart, plan, width, height, _styles.Theme, MeasureLabel, LabelBox, room.Title);

        // The title up the side is turned on its end, which no line of text can be: it is drawn
        // into the chart's own picture instead, where a turned string is something that can be
        // drawn.
        if (Turned(chart, plan, width, height) is { } turned)
        {
            drawing = new Images.VectorDrawing(drawing.Width, drawing.Height,
                [.. drawing.Operations, turned]);
        }

        var frame = new Images.ImageData(1, 1, [],
            Images.ImageEncoding.Raw, Images.ImageColorSpace.Rgb)
        {
            Drawing = drawing
        };

        var page = new LaidOutPage { WidthPoints = width, HeightPoints = height };

        // The chart's own title, over the top and centred on the frame rather than on the plot.
        // Where its first line sits is measured from the top of the frame down to its baseline,
        // which is why it is placed by that rather than by the top of its box.
        if (chart.Title is { Overlay: false, Paragraphs.Count: > 0 } title)
        {
            var box = Math.Max(1, width * ChartComposer.TitleWidth);

            // Put by hand, the title is set from its own stated corner rather than centred on the
            // frame — and it is set flush there, since centring it in a box it was never given
            // would move it. See ChartComposer.PlacedTitleAcross.
            var flow = title.Layout is null
                ? MeasureInside(Centred(title.Paragraphs), box)
                : MeasureInside(title.Paragraphs, box);

            var (ascent, _) = BlockBox(title.Paragraphs);

            if (title.Layout is { } corner)
            {
                flow.PlaceOnto(page,
                    corner.X * width + ChartComposer.PlacedTitleAcross,
                    corner.Y * height + ChartComposer.PlacedTitleDown + ascent - flow.FirstAscent);
            }
            else
            {
                flow.PlaceOnto(page, (width - box) / 2,
                    ChartComposer.TitleTop + ascent - flow.FirstAscent);
            }
        }

        // And the title under the foot, which ends a fixed distance inside the frame instead.
        if ((chart.Lying ? chart.ValueAxis : chart.CategoryAxis)?.Title
            is { Overlay: false, Paragraphs.Count: > 0 } under)
        {
            var box = Math.Max(1, width * ChartComposer.TitleWidth);
            var flow = MeasureInside(Centred(under.Paragraphs), box);

            var (ascent, descent) = BlockBox(under.Paragraphs);
            var lines = Math.Max(1, flow.LineCount);

            var baseline = height - room.LegendBottom - ChartComposer.AxisTitleEdge - descent -
                           (lines - 1) * (ascent + descent);

            flow.PlaceOnto(page, plan.Left + (plan.Width - box) / 2, baseline - flow.FirstAscent);
        }

        // The legend: its keys are drawn with the rest of the picture, its words with the rest of
        // the text.
        foreach (var entry in ChartComposer.Legend(
            chart, width, height, MeasureLabel, LabelBox, room.Title))
        {
            var size = chart.Legend!.LabelSizePoints;
            var label = ChartLabel(entry.Text, Justification.Left, size);
            var flow = MeasureInside([label], Math.Max(1, MeasureLabel(entry.Text, size) + 1));

            flow.PlaceOnto(page, entry.TextX, entry.Baseline - flow.FirstAscent);
        }

        // The depth axis's labels, one against each receding row (#100).
        foreach (var (text, x, baseline) in ChartComposer.DepthAxisLabels(chart, plan))
        {
            var size = chart.DepthAxis?.LabelSizePoints ?? 10;
            var label = ChartLabel(text, Justification.Left, size);
            var flow = MeasureInside([label], Math.Max(1, MeasureLabel(text, size) + 1));

            flow.PlaceOnto(page, x, baseline - flow.FirstAscent);
        }

        // And what is written at the points themselves.
        foreach (var written in ChartComposer.DataLabels(chart, plan, LabelBox))
        {
            var size = LabelSize(chart);
            var width0 = MeasureLabel(written.Text, size) + 1;

            var label = ChartLabel(written.Text,
                written.Centred ? Justification.Center : Justification.Left, size);

            var flow = MeasureInside([label], Math.Max(1, width0));

            flow.PlaceOnto(page, written.Centred ? written.X - width0 / 2 : written.X,
                written.Baseline - flow.FirstAscent);
        }

        // A web is labelled round its rim rather than along an axis, and up its middle rather
        // than up its side.
        if (chart.Kind == ChartKind.Radar)
        {
            WebLabels(page, chart, plan);

            return (frame, new DetachedFlow(page, height), 0, 0);
        }

        // The numbers along the value axis, each set against its own mark: ranged up against the
        // axis where they are written beside it, and centred on the mark where they are under it.
        if (chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" } valueAxis)
        {
            var size = valueAxis.LabelSizePoints;

            // A chart of pairs keeps its numbers beside the axis they belong to, and that axis
            // stands where the foot reads nought rather than at the edge of the plot.
            var standing = plan.Paired ? plan.AcrossCrossing : plan.Left;

            foreach (var value in ChartComposer.Marks(plan))
            {
                var text = ChartComposer.Format(value, valueAxis.NumberFormat);

                if (plan.Lying)
                {
                    Under(page, text, size, plan.PositionOf(value),
                        plan.Bottom + size * CategoryLabelBaseline);
                }
                else
                {
                    Beside(page, text, size, standing, plan.PositionOf(value));
                }
            }
        }

        // And the categories along their own axis, each against the bars it belongs to: under them
        // on an upright chart, and beside them on one lying down.
        if (chart.CategoryAxis is { Deleted: false, TickLabelPosition: not "none" } categoryAxis)
        {
            var size = categoryAxis.LabelSizePoints;

            // How far off the axis the words go, which is a share of the type they are set in and
            // nothing to do with the marks along it: Word puts the baseline 1.584 times the type
            // size below the axis at ten point and at twenty alike, and a chart whose marks are
            // drawn outside puts it in exactly the same place. A label offset shifts them, which
            // is measured for words written under the axis and taken to do the same beside it.
            var step = (categoryAxis.LabelOffset - 100) / 100.0 * CategoryLabelStep;
            var baseline = plan.Crossing + size * (CategoryLabelBaseline + step);

            if (plan.Paired)
            {
                // A foot that is a scale of its own is labelled where its own numbers fall.
                foreach (var value in ChartComposer.MarksAcross(plan))
                {
                    var text = ChartComposer.Format(value, categoryAxis.NumberFormat);

                    Under(page, text, size, plan.AcrossOf(value), baseline);
                }
            }
            else
            {
                var categories = chart.Categories;
                var slot = plan.Slot(categories.Count);
                var spanning = ChartComposer.Spanning(chart);

                for (var i = 0; i < categories.Count; i++)
                {
                    if (plan.Lying)
                    {
                        Beside(page, categories[i], size, plan.Crossing - size * step,
                            plan.SlotAt(i, categories.Count) + slot / 2);
                    }
                    else
                    {
                        Under(page, categories[i], size,
                            plan.PointAt(i, categories.Count, spanning), baseline,
                            Math.Max(1, slot));
                    }
                }
            }
        }

        return (frame, new DetachedFlow(page, height), 0, 0);
    }

    /// <summary>
    /// The words round a web: a category at every spoke, and the numbers up the middle.
    /// </summary>
    /// <remarks>
    /// Where each goes is measured from chart-kinds-probe and written up in
    /// <see cref="ChartComposer"/>, whose constants these are. A category beside the web is set
    /// against a circle a little outside the rim — ranged left of it on the right of the web and
    /// right of it on the left — and one at the very top or foot is centred on its spoke instead.
    /// </remarks>
    private void WebLabels(LaidOutPage page, ChartDefinition chart, ChartComposer.Plan plan)
    {
        var centre = plan.Middle;

        if (chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" } valueAxis)
        {
            var size = valueAxis.LabelSizePoints;
            var (ascent, descent) = LabelBox(size);

            var right = centre.X - (size * ChartComposer.WebValueGap +
                                    ChartComposer.WebValueGapFixed);

            foreach (var value in ChartComposer.Marks(plan))
            {
                var text = ChartComposer.Format(value, valueAxis.NumberFormat);

                Ranged(page, text, size, right,
                    centre.Y - plan.OutOf(value) + (ascent - descent) / 2);
            }
        }

        if (chart.CategoryAxis is not { Deleted: false, TickLabelPosition: not "none" } axis)
            return;

        var labelSize = axis.LabelSizePoints;
        var (up, down) = LabelBox(labelSize);

        var categories = chart.Categories;
        var reach = plan.Radius * ChartComposer.WebLabelReach;

        for (var i = 0; i < categories.Count; i++)
        {
            var angle = ChartComposer.Plan.Spoke(i, categories.Count);
            var (sin, cos) = (Math.Sin(angle), Math.Cos(angle));

            // At the very top or the very foot the label is centred on its spoke, and clears the
            // rim by its own ascender or descender and a gap.
            if (Math.Abs(sin) < 1e-9)
            {
                Under(page, categories[i], labelSize, centre.X, cos > 0
                    ? centre.Y - (reach + ChartComposer.WebLabelTopGap) - down
                    : centre.Y + reach + up);

                continue;
            }

            var baseline = centre.Y - reach * cos + (up - down) / 2 - ChartComposer.WebLabelDrop;

            if (sin > 0) Ranged(page, categories[i], labelSize, 0, baseline, centre.X + reach * sin);
            else Ranged(page, categories[i], labelSize, centre.X - reach * -sin, baseline);
        }
    }

    /// <summary>
    /// One label of a chart set against a place rather than under a point: ranged right where it
    /// is given the edge it ends at, and left where it is given the one it begins at.
    /// </summary>
    private void Ranged(
        LaidOutPage page, string text, double size, double right, double baseline,
        double left = double.NaN)
    {
        var ranged = double.IsNaN(left);

        var label = ChartLabel(text, ranged ? Justification.Right : Justification.Left, size);

        // Ranged against an edge, the label is set in a box ending there and pushed to its end;
        // set from one, in a box of its own width beginning there.
        var flow = MeasureInside([label],
            Math.Max(1, ranged ? right : MeasureLabel(text, size) + 1));

        flow.PlaceOnto(page, ranged ? 0 : left, baseline - flow.FirstAscent);
    }

    /// <summary>
    /// What size the numbers written at a chart's points are set in.
    /// </summary>
    private static double LabelSize(ChartDefinition chart) =>
        (chart.Series.Select(series => series.Labels).FirstOrDefault(labels => labels is not null)
         ?? chart.Labels)?.SizePoints ?? 10;

    /// <summary>
    /// A copy of a title's text with every paragraph centred, which is how a title is set whatever
    /// its own paragraphs say — Word's own titles all carry the alignment and this only matters for
    /// one that does not.
    /// </summary>
    private static IReadOnlyList<BlockElement> Centred(IReadOnlyList<BlockElement> blocks)
    {
        var centred = new List<BlockElement>();

        foreach (var block in blocks)
        {
            if (block is not Paragraph paragraph) { centred.Add(block); continue; }

            var copy = new Paragraph { Properties = paragraph.Properties };
            copy.Properties.Justification = Justification.Center;

            foreach (var run in paragraph.Runs) copy.Runs.Add(run);

            centred.Add(copy);
        }

        return centred;
    }

    /// <summary>
    /// Names the faces a title's runs are set in, where they are named as slots in the theme
    /// rather than outright.
    /// </summary>
    private void Resolve(ChartTitle? title)
    {
        if (title is null) return;

        foreach (var block in title.Paragraphs)
        {
            if (block is not Paragraph paragraph) continue;

            foreach (var run in paragraph.Runs)
            {
                var face = run.Properties.AsciiFont switch
                {
                    "+mj-lt" => _styles.Theme.MajorLatinFont,
                    "+mn-lt" => _styles.Theme.MinorLatinFont,
                    _ => null
                };

                if (face is null) continue;

                run.Properties.AsciiFont = face;
                run.Properties.HighAnsiFont = face;
            }
        }
    }

    /// <summary>
    /// How much room a title's text takes, once laid out into the width it has.
    /// </summary>
    /// <remarks>
    /// Not the height the engine gives it, but the height Word reckons it by: a line of a title is
    /// the face as Windows reads it, ascent and descent and nothing besides. For Times New Roman
    /// that is 1.1074 ems against the 1.1499 a line of body text comes to, and it is the first
    /// that accounts for the room Word leaves a title at ten point, eighteen, twenty and thirty,
    /// and for a title of two lines.
    /// </remarks>
    private (double Width, double Height) MeasureBlock(IReadOnlyList<BlockElement> blocks, double width)
    {
        var flow = MeasureInside(blocks, Math.Max(1, width));
        var (ascent, descent) = BlockBox(blocks);

        return (flow.WidestLine, Math.Max(1, flow.LineCount) * (ascent + descent));
    }

    /// <summary>
    /// How far the face a passage is set in reaches above and below its baseline, as Windows
    /// reads it.
    /// </summary>
    private (double Ascent, double Descent) BlockBox(IReadOnlyList<BlockElement> blocks)
    {
        var run = blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Runs).FirstOrDefault();
        var format = _styles.ResolveRun(null, run?.Properties);

        var size = format.FontSizePoints;

        if (!_fonts.TryResolve(format.FontFamily, format.Bold, format.Italic, out var selection))
            return (size * 0.75, size * 0.25);

        var metrics = selection.Font.Metrics;

        return (metrics.WinAscent * size / metrics.UnitsPerEm,
            metrics.WinDescent * size / metrics.UnitsPerEm);
    }

    /// <summary>
    /// The title up the side of a chart, turned on its end. It reads from the foot upwards, its
    /// baseline 12.5pt inside the frame and its words centred on the plotting.
    /// </summary>
    private Images.WordArtOperation? Turned(
        ChartDefinition chart, ChartComposer.Plan plan, double width, double height)
    {
        if ((chart.Lying ? chart.CategoryAxis : chart.ValueAxis)?.Title
            is not { Overlay: false, Paragraphs.Count: > 0 } title) return null;

        var runs = title.Paragraphs.OfType<Paragraph>().SelectMany(block => block.Runs).ToList();
        if (runs.Count == 0) return null;

        var text = string.Concat(runs.SelectMany(run => run.Content.OfType<TextInline>())
            .Select(inline => inline.Text));

        if (text.Length == 0) return null;

        var format = _styles.ResolveRun(null, runs[0].Properties);
        var size = format.FontSizePoints;

        var (ascent, _) = BlockBox(title.Paragraphs);
        var measured = _fonts.TryResolve(format.FontFamily, format.Bold, format.Italic, out var face)
            ? TextMeasurer.Measure(face.Font, text, size)
            : text.Length * size * 0.5;

        return new Images.WordArtOperation(text,
            ChartComposer.AxisTitleEdge + ascent,
            plan.Top + plan.Height / 2 + measured / 2,
            format.FontFamily, size, format.Bold, format.Italic,
            new Images.DrawingColor(0, 0, 0), AngleDegrees: -90);
    }

    /// <summary>
    /// One label ranged up against an axis running down the chart, ending a little short of it and
    /// set about the point it belongs to rather than under it.
    /// </summary>
    /// <remarks>
    /// What is centred on the point is the box from the top of the ascenders to the foot of the
    /// descenders, which puts the baseline half their difference below it. Measured at ten point
    /// and at twenty, where Word sets the baseline 2.64pt and 5.04pt below the mark.
    /// </remarks>
    private void Beside(LaidOutPage page, string text, double size, double axis, double at)
    {
        var (ascent, descent) = LabelMetrics(size);

        var label = ChartLabel(text, Justification.Right, size);
        var flow = MeasureInside([label], Math.Max(1, axis - size * ValueLabelGap));

        flow.PlaceOnto(page, 0, at + (ascent - descent) / 2 - flow.FirstAscent);
    }

    /// <summary>One label written under an axis running across the chart, centred on its point.</summary>
    /// <remarks>
    /// The box it is centred in is its own width where nothing else says — a number takes what it
    /// takes — and a category's own share of the plot where it has one, which is what gives a long
    /// category somewhere to wrap. Both centre what they hold on the point, line by line.
    /// </remarks>
    private void Under(
        LaidOutPage page, string text, double size, double at, double baseline, double box = 0)
    {
        var width = box > 0 ? box : MeasureLabel(text, size) + 1;

        var label = ChartLabel(text, Justification.Center, size);
        var flow = MeasureInside([label], width);

        flow.PlaceOnto(page, at - width / 2, baseline - flow.FirstAscent);
    }

    /// <summary>
    /// How wide a chart's label comes out and how many lines it takes, once it has been wrapped
    /// into the room it has. What the plot area has to leave for it depends on both.
    /// </summary>
    private (double Width, int Lines) WrapLabel(string text, double sizePoints, double box)
    {
        var flow = MeasureInside([ChartLabel(text, Justification.Left, sizePoints)],
            Math.Max(1, box));

        return (flow.WidestLine, Math.Max(1, flow.LineCount));
    }

    /// <summary>
    /// How much a hundred of label offset moves a category's line, as a share of its type size.
    /// The two rules it works with — the gap a number keeps from its axis, and where a category's
    /// baseline falls — are <see cref="ChartComposer"/>'s, since the placing of the plot area has
    /// to make room for them.
    /// </summary>
    private const double CategoryLabelStep = 0.312;

    private const double ValueLabelGap = ChartComposer.ValueLabelGap;

    private const double CategoryLabelBaseline = ChartComposer.CategoryLabelBaseline;

    /// <summary>How wide a chart's label is in the face it is set in.</summary>
    private double MeasureLabel(string text, double sizePoints)
    {
        var format = _styles.ResolveRun(null, null);

        return _fonts.TryResolve(format.FontFamily, format.Bold, format.Italic, out var selection)
            ? TextMeasurer.Measure(selection.Font, text, sizePoints)
            : text.Length * sizePoints * 0.5;
    }

    /// <summary>
    /// How much room a chart's label takes above and below its baseline, for leaving space around
    /// the plotting.
    /// </summary>
    /// <remarks>
    /// The face as Windows reads it, which is not the pair the label is set by: Calibri's 1950 and
    /// 550 of 2048 rather than its 1536 and 512. The margins Word leaves are the first pair's — a
    /// twenty point label leaves 17.21pt above the plot, which is five points and half of 1.221
    /// ems — and where the baseline itself falls is the second's. Two questions, two answers.
    /// </remarks>
    private (double Ascent, double Descent) LabelBox(double sizePoints)
    {
        var format = _styles.ResolveRun(null, null);

        if (!_fonts.TryResolve(format.FontFamily, format.Bold, format.Italic, out var selection))
            return (sizePoints * 0.75, sizePoints * 0.25);

        var metrics = selection.Font.Metrics;

        return (metrics.WinAscent * sizePoints / metrics.UnitsPerEm,
            metrics.WinDescent * sizePoints / metrics.UnitsPerEm);
    }

    /// <summary>
    /// How far a chart's label reaches above and below its baseline, for setting it against a mark.
    /// </summary>
    private (double Ascent, double Descent) LabelMetrics(double sizePoints)
    {
        var format = _styles.ResolveRun(null, null);

        if (!_fonts.TryResolve(format.FontFamily, format.Bold, format.Italic, out var selection))
            return (sizePoints * 0.75, sizePoints * 0.25);

        // The typographic ascent and descent rather than the ones a line is measured by: Calibri
        // says 1536 and 512 of its 2048 for the first pair and 1950 and 550 for the second, and it
        // is the first that puts a label where Word puts it — a quarter of the type size below its
        // mark, against the 0.342 the second would give.
        var metrics = selection.Font.Metrics;

        var ascender = metrics.TypoAscender != 0 ? metrics.TypoAscender : metrics.Ascender;
        var descender = metrics.TypoDescender != 0 ? metrics.TypoDescender : metrics.Descender;

        return (ascender * sizePoints / metrics.UnitsPerEm,
            -descender * sizePoints / metrics.UnitsPerEm);
    }

    /// <summary>
    /// One label of a chart, in the document's own face at the size the axis asks for.
    /// </summary>
    private static Paragraph ChartLabel(string text, Justification alignment, double sizePoints)
    {
        var paragraph = new Paragraph();

        paragraph.Properties.Justification = alignment;
        paragraph.Properties.SpacingBeforeTwips = 0;
        paragraph.Properties.SpacingAfterTwips = 0;
        paragraph.Properties.Line = 240;
        paragraph.Properties.LineRule = LineSpacingRule.Auto;

        var run = new Run { Properties = { SizeHalfPoints = (int)Math.Round(sizePoints * 2) } };
        run.Content.Add(new TextInline(text));
        paragraph.Runs.Add(run);

        return paragraph;
    }

    /// <summary>
    /// Draws a diagram: every shape of it into one drawing, and every shape's words into one flow
    /// of text over the top.
    /// </summary>
    /// <remarks>
    /// Both are one thing rather than many because a diagram moves as a whole — it sits on a line
    /// like a picture, or is anchored like one — and because the machinery that carries a shape
    /// to the page carries exactly one drawing and one flow. The words are laid out into the
    /// rectangle the diagram set aside for each shape, and set in it by that shape's anchor.
    /// </remarks>
    private (Images.ImageData Frame, DetachedFlow? Content, double Left, double Top) ComposeDiagram(
        IReadOnlyList<DiagramShape> diagram, double width, double height)
    {
        var frame = new Images.ImageData(1, 1, [],
            Images.ImageEncoding.Raw, Images.ImageColorSpace.Rgb)
        {
            Drawing = ShapeOutline.Draw(diagram, width, height, _styles.Theme)
        };

        var page = new LaidOutPage { WidthPoints = width, HeightPoints = height };

        foreach (var shape in diagram)
        {
            if (!shape.Shape.HasText || shape.TextWidth <= 0) continue;

            var content = MeasureInside(shape.Shape.Content, shape.TextWidth);

            // Text taller than the box it is in is still set in the middle of it, standing out
            // above and below rather than starting at the top: Word's own drawing of the diagram
            // fixture has a box whose three lines overrun it at both ends.
            var top = shape.Shape.Anchor switch
            {
                ShapeTextAnchor.Center => shape.TextY + (shape.TextHeight - content.Height) / 2,
                ShapeTextAnchor.Bottom => shape.TextY + shape.TextHeight - content.Height,
                _ => shape.TextY
            };

            content.PlaceOnto(page, shape.TextX, top);
        }

        return (frame, new DetachedFlow(page, height), 0, 0);
    }

    /// <summary>
    /// Draws a shape's frame and lays out what it holds, in the shape's own coordinates.
    /// </summary>
    /// <remarks>
    /// The text clears the inset and half the outline, the half of it that falls inside the
    /// shape. Measured from shape-inset-probe, whose third page sets a six point outline against
    /// no inset at all: the text there begins 3.12pt inside the shape, where half the outline is
    /// 3pt and the whole of it is 6pt.
    /// </remarks>
    private (Images.ImageData Frame, DetachedFlow? Content, double Left, double Top) ComposeShape(
        ShapeFrame shape, double width, double height)
    {
        var frame = new Images.ImageData(1, 1, [],
            Images.ImageEncoding.Raw, Images.ImageColorSpace.Rgb)
        {
            Drawing = ShapeOutline.Draw(shape, width, height, _styles.Theme, _fonts)
        };

        if (!shape.HasText || shape.WordArt is not null) return (frame, null, 0, 0);

        var edge = shape.Line is null ? 0 : shape.LineWidthPoints / 2;

        // Where the shape is drawn away from its own box, its text follows it by half as far.
        var carried = shape.DrawnOffsetPoints / 2;

        var left = shape.InsetLeftPoints + edge + carried;
        var available = Math.Max(1, width - shape.InsetLeftPoints - shape.InsetRightPoints - 2 * edge);
        var content = MeasureInside(shape.Content, available);

        // Where in the height the text sits, which is what the shape's anchor decides. A box
        // whose text is taller than it is runs on out of the bottom of it, which is what Word
        // does with one too.
        var box = height - shape.InsetTopPoints - shape.InsetBottomPoints - 2 * edge;

        var top = carried + shape.Anchor switch
        {
            ShapeTextAnchor.Center => shape.InsetTopPoints + edge + Math.Max(0, (box - content.Height) / 2),
            ShapeTextAnchor.Bottom => Math.Max(shape.InsetTopPoints + edge,
                height - shape.InsetBottomPoints - edge - content.Height),
            _ => shape.InsetTopPoints + edge
        };

        return (frame, content, left, top);
    }

    /// <summary>
    /// Lays out what a shape or a table cell holds, which is measured like anything else but
    /// breaks a word that will not fit rather than letting it overrun the box.
    /// </summary>
    /// <remarks>
    /// The width a box gives its content, which is what its words are broken against. A page is no
    /// different — break-tolerance-probe breaks ten capital Ms into nine and one the moment the
    /// measure falls a twip short of them, exactly as a box does — so the breaking itself is not
    /// what this carries; it is the measure.
    /// </remarks>
    private DetachedFlow MeasureInside(IReadOnlyList<BlockElement> blocks, double width)
    {
        var outer = _breakInsideWords;
        _breakInsideWords = width;

        try
        {
            return MeasureBlocks(blocks, width);
        }
        finally
        {
            _breakInsideWords = outer;
        }
    }

    /// <summary>
    /// Lays out blocks into a detached page so their height can be measured before placement.
    /// </summary>
    private DetachedFlow MeasureBlocks(
        IReadOnlyList<BlockElement> blocks, double width, PageFrame? frame = null)
    {
        var footnotes = new List<int>();
        var section = new SectionProperties();
        var document = new LaidOutDocument { Section = section };

        var page = new LaidOutPage { WidthPoints = width, HeightPoints = double.MaxValue };
        document.Pages.Add(page);

        var cursor = new Cursor
        {
            Engine = this,
            Document = document,
            Section = section,
            Page = page,
            Y = 0,
            Left = 0,
            Width = width,
            ContentTop = 0,
            ContentLimit = double.MaxValue,
            Paginate = false,
            FootnoteSink = footnotes,
            SectionWidth = width,
            Columns = [(0, width)],
            Frame = frame
        };

        LayoutBlocks(cursor, blocks);
        cursor.Y += cursor.PendingSpaceAfter;

        return new DetachedFlow(page, cursor.Y, footnotes);
    }

    private static (double Red, double Green, double Blue) ParseHexColor(string value)
    {
        if (value.Length != 6) return (1, 1, 1);

        try
        {
            return (Convert.ToInt32(value[..2], 16) / 255.0,
                Convert.ToInt32(value.Substring(2, 2), 16) / 255.0,
                Convert.ToInt32(value.Substring(4, 2), 16) / 255.0);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return (1, 1, 1);
        }
    }

    private LaidOutPage NewPage(LaidOutDocument document, SectionProperties section)
    {
        // A section may begin its numbering again, which the first page of it takes up; every
        // other page carries on from the page before, whatever section it is in.
        _printedPage = _pendingPageNumber ?? _printedPage + 1;
        _pendingPageNumber = null;

        var page = new LaidOutPage
        {
            WidthPoints = section.PageWidthPoints,
            HeightPoints = section.PageHeightPoints,
            Section = section,
            IndexInSection = _pagesInSection++,
            PageNumber = _printedPage
        };

        // A section numbering its lines from the top of every page begins again here; one
        // numbering from the top of the section began when the section did.
        if (section.LineNumbers is { Restart: LineNumberRestart.NewPage } perPage)
            _nextLineNumber = perPage.Start;

        document.Pages.Add(page);
        DrawPageBorders(page, section);

        return page;
    }

    /// <summary>
    /// Draws the border round a page.
    /// </summary>
    /// <remarks>
    /// Where the line falls is measured in page-border-probe, and it is not the same question
    /// either side of <c>w:offsetFrom</c>. Offset from the page, the space is to the <em>outside</em>
    /// of the line: a border 24 points from the page has its outer edge at 24 and its ink from 24
    /// to 24.96. Offset from the text, the space is to the <em>inside</em> of it: a border against
    /// the text with no space at all has its inner edge on the margin and its ink just outside.
    ///
    /// An edge runs the length of the paper rather than of the border. Word draws each side as a
    /// bar between the corners and then fills the corners in; the same ink comes of drawing one
    /// bar from the outside of one neighbour to the outside of the other, and where a neighbour is
    /// missing the bar runs on to the edge of the paper — which the probe's last section shows,
    /// its top border running the full width where it asked for no right one.
    /// </remarks>
    private static void DrawPageBorders(LaidOutPage page, SectionProperties section)
    {
        if (section.PageBorders is not { } borders) return;

        var wanted = borders.Display switch
        {
            PageBorderDisplay.FirstPage => page.IndexInSection == 0,
            PageBorderDisplay.NotFirstPage => page.IndexInSection > 0,
            _ => true
        };

        if (!wanted) return;

        // How thick each side's line is drawn, on the grid Word draws it on.
        double Thickness(PageBorderEdge? edge) => edge is null ? 0 : Grid.Width(edge.Line.WidthPoints);

        // The outer edge of a side: from the paper where the border is offset from the page, and
        // from the text less the line's own thickness where it is offset from the text.
        double Outer(PageBorderEdge? edge, double margin) => edge is null
            ? 0
            : borders.FromText
                ? margin - edge.Space - Grid.Snap(edge.Line.WidthPoints)
                : edge.Space;

        var left = Outer(borders.Left, Units.TwipsToPoints(section.MarginLeftTwips));
        var top = Outer(borders.Top, Units.TwipsToPoints(section.MarginTopTwips));
        var right = page.WidthPoints - Outer(borders.Right, Units.TwipsToPoints(section.MarginRightTwips));
        var bottom = page.HeightPoints - Outer(borders.Bottom, Units.TwipsToPoints(section.MarginBottomTwips));

        // Where a side is missing, the sides meeting it run on to the paper's edge.
        var acrossFrom = borders.Left is null ? 0 : left;
        var acrossTo = borders.Right is null ? page.WidthPoints : right;
        var downFrom = borders.Top is null ? 0 : top;
        var downTo = borders.Bottom is null ? page.HeightPoints : bottom;

        void Bar(PageBorderEdge? edge, double x, double y, double width, double height)
        {
            if (edge is null) return;

            page.Rectangles.Add(new PositionedRectangle
            {
                X = x,
                Y = y,
                Width = width,
                Height = height,
                Color = edge.Line.GetColor()
            });
        }

        Bar(borders.Top, acrossFrom, top, acrossTo - acrossFrom, Thickness(borders.Top));
        Bar(borders.Bottom, acrossFrom, bottom - Thickness(borders.Bottom),
            acrossTo - acrossFrom, Thickness(borders.Bottom));
        Bar(borders.Left, left, downFrom, Thickness(borders.Left), downTo - downFrom);
        Bar(borders.Right, right - Thickness(borders.Right), downFrom,
            Thickness(borders.Right), downTo - downFrom);
    }

    /// <summary>Places a composed line's segments at their final page coordinates.</summary>
    /// <summary>
    /// The things on a line in the order they are drawn, which is not the order they are stored in
    /// wherever the line holds anything that runs right to left. This is the standard's rule L2,
    /// applied to whole words rather than to characters: a word runs one way throughout, having
    /// been broken where its direction changes.
    /// </summary>
    private static List<PlacedAtom> ForDrawing(List<PlacedAtom> items)
    {
        var mixed = false;

        foreach (var item in items)
        {
            if (item.Atom.Level == 0) continue;

            mixed = true;
            break;
        }

        if (!mixed) return items;

        var levels = new byte[items.Count];
        for (var i = 0; i < items.Count; i++) levels[i] = items[i].Atom.Level;

        var order = Text.Bidi.Reorder(levels);
        var drawn = new List<PlacedAtom>(items.Count);

        foreach (var at in order) drawn.Add(items[at]);

        return drawn;
    }

    private double EmitLine(
        LaidOutPage page, ComposedLine line, double contentLeft, double top, int paragraphIndex,
        TabOptions tabs)
    {
        // Word works the height of a line out exactly and then writes its baseline on the grid;
        // the line below it starts from the exact height all the same, so the rounding never
        // accumulates. Grid holds what says so — and a line of an exact height is rounded by a
        // rule of its own, since it keeps no room below the baseline to round instead.
        var baselineY = line.ExactHeight
            ? Grid.ExactBaseline(top, line.Ascent)
            : Grid.Baseline(top, line.Ascent, line.Height);

        // A picture sits on the line's own baseline rather than on the rounded one the text is
        // written at. Word says so: vml-stroke-probe puts every shape at exactly the margin plus
        // its offset, never a grid step off it, however the rounding of the line falls.
        var restingY = top + line.Ascent;

        // What a highlight covers, on the other hand, is the line box exactly as the flow reached
        // it: both edges put on the grid from the unrounded position, so that the boxes of one
        // line and the next meet. Reading them off the rounded baseline instead leaves a quarter
        // point of daylight between every other pair, which highlight-probe shows Word has none of.
        var boxTop = Grid.Snap(top);
        var boxHeight = Grid.Snap(top + line.Height) - boxTop;

        // What the line draws, on the other hand — a bar tab down its side, a footnote's separator
        // — hangs off the line box as it was written, or it stands a fraction of a step away from
        // the text it belongs to.
        top = baselineY - line.Ascent;

        var laidOut = new LaidOutLine
        {
            BaselineY = baselineY,
            Height = line.Height,
            Ascent = line.Ascent,
            ParagraphIndex = paragraphIndex
        };

        // A guided word is written where it stands in the line rather than after everything else
        // on it: what a reader copies out of the page should read as the document does, guide and
        // all, and a PDF is read in the order it was written.
        var guided = new List<(double X, List<PositionedText> Texts)>();

        foreach (var (ruby, x) in line.Rubies) guided.Add((contentLeft + x, RubyTexts(ruby, contentLeft + x, baselineY)));

        var nextGuided = 0;

        void GuidedBefore(double x)
        {
            while (nextGuided < guided.Count && guided[nextGuided].X <= x + 0.001)
                laidOut.Texts.AddRange(guided[nextGuided++].Texts);
        }

        foreach (var segment in line.Segments)
        {
            if (segment.Text.Length == 0) continue;

            GuidedBefore(contentLeft + segment.X);

            var text = new PositionedText
            {
                X = contentLeft + segment.X,
                // A raised or lowered run is written on the grid like any other: Word's own pages
                // have no baseline off it, superscripts and footnote marks included.
                BaselineY = Grid.Snap(baselineY - segment.Format.BaselineShiftPoints),
                Text = segment.Text,
                Format = segment.Format,
                Font = segment.Font,
                Width = segment.Width,
                WordSpacing = segment.WordSpacing,
                Link = segment.Link,
                Kerned = segment.Kerned,
                RightToLeft = (segment.Level & 1) != 0
            };

            // A mark over each character of the run, written before the run itself, which is the
            // order Word writes them in.
            if (ResolveEmphasis(segment.Format, segment.Font) is { } mark)
            {
                var pen = text.X;
                var em = segment.Font.Font.UnitsPerEm;
                var marked = char.ConvertFromUtf32(mark.CodePoint);

                foreach (var character in segment.Text)
                {
                    var glyph = segment.Font.Font.GetGlyphIndex(character);
                    var advance = segment.Font.Font.GetAdvanceWidth(glyph) *
                                  segment.Format.FontSizePoints / em;

                    // A space carries no mark; everything else does, punctuation included.
                    if (!char.IsWhiteSpace(character))
                    {
                        laidOut.Texts.Add(new PositionedText
                        {
                            X = pen + advance / 2 - mark.InkCentre,
                            BaselineY = Grid.Snap(baselineY + mark.Offset),
                            Text = marked,
                            Format = segment.Format,
                            Font = mark.Font,
                            Width = 0
                        });
                    }

                    pen += advance;
                }
            }

            laidOut.Texts.Add(text);

            // What goes behind the run: its highlight, or the background it asks for itself where
            // it has no highlight. Both are the same rectangle — the run as the line set it, a
            // space inside the line included and one dropped at a break not, and the height of the
            // line rather than of the run, so that a twelve point run beside a thirty-six point
            // one is covered the full forty-one points of the line they share. It goes under the
            // text and over the paragraph's own background, which is the order they are added in.
            //
            // A highlight and a background on one run is a case Word settles by drawing the
            // highlight alone: run-shading-probe's last line asks for an orange background under a
            // yellow highlight and Word's page has one rectangle on it, the yellow.
            var behind = HighlightColors.Resolve(segment.Format.HighlightColor)
                         ?? segment.Format.Shading.Resolve();

            if (behind is { } lit && boxHeight > 0)
            {
                var litLeft = Grid.Snap(text.X);

                page.Rectangles.Add(new PositionedRectangle
                {
                    X = litLeft,
                    Y = boxTop,
                    Width = Grid.Snap(text.X + segment.Width) - litLeft,
                    Height = boxHeight,
                    Color = lit
                });
            }

            AddDecorations(page, text);
        }

        // The box round a bordered run, which is the run's own extent by the line's box with a
        // step of the grid over it, and the line drawn outward from that. Runs bordered alike and
        // standing next to each other share one box, as run-border-probe shows: "two" and "three"
        // written as two runs come out of Word inside a single box, where the same two with a
        // space between them come out in two.
        for (var i = 0; i < line.Segments.Count; i++)
        {
            if (line.Segments[i].Format.Border is not { } edge) continue;

            var from = line.Segments[i];
            var to = from;
            var ascent = from.Ascent;
            var descent = from.Descent;

            while (i + 1 < line.Segments.Count &&
                   line.Segments[i + 1].Format.Border is { } next &&
                   next.Equals(edge) &&
                   Math.Abs(line.Segments[i + 1].X - (to.X + to.Width)) < 0.01)
            {
                to = line.Segments[++i];
                ascent = Math.Max(ascent, to.Ascent);
                descent = Math.Max(descent, to.Descent);
            }

            var weight = BorderWeight(edge);
            if (weight <= 0) continue;

            // A run's space is taken as it stands, where a paragraph's is rounded down to the
            // grid: four points widens the run by eight, not by twice 3.84.
            var clear = edge.SpacePoints;
            var innerLeft = Grid.Snap(contentLeft + from.X - clear);
            var innerRight = Grid.Snap(contentLeft + to.X + to.Width + clear);

            // The run's own box — its ascent rounded to the grid and its descent rounded down to
            // it, the way a line box is measured — with a step over it and the line outside that.
            var innerTop = baselineY - Grid.Snap(ascent) - clear - Grid.Step;
            var innerBottom = baselineY + Grid.Width(descent) + clear;

            if (innerRight <= innerLeft || innerBottom <= innerTop) continue;

            void Side(double x, double y, double width, double height) =>
                page.Rectangles.Add(new PositionedRectangle
                {
                    X = x, Y = y, Width = width, Height = height, Color = edge.Line.GetColor()
                });

            Side(innerLeft - weight, innerTop - weight,
                innerRight - innerLeft + 2 * weight, weight);
            Side(innerLeft - weight, innerBottom, innerRight - innerLeft + 2 * weight, weight);
            Side(innerLeft - weight, innerTop, weight, innerBottom - innerTop);
            Side(innerRight, innerTop, weight, innerBottom - innerTop);
        }

        foreach (var bar in line.Bars)
        {
            page.Rectangles.Add(new PositionedRectangle
            {
                X = contentLeft + bar,
                Y = top,
                Width = BarTabWidthPoints,
                Height = line.Height,
                Color = (0, 0, 0)
            });
        }

        foreach (var leader in line.Leaders)
            EmitLeader(laidOut, leader, contentLeft, baselineY, tabs);

        foreach (var (separator, x) in line.Separators)
        {
            page.Rules.Add(new PositionedRule
            {
                X = contentLeft + x,
                Y = top + line.Height - FootnoteSeparatorGapPoints - separator.Thickness,
                Width = separator.Width,
                Thickness = separator.Thickness,
                Color = (0, 0, 0)
            });
        }

        GuidedBefore(double.MaxValue);

        // The boxes a form is filled in by: four bars round a square, and a cross through it where
        // it is ticked. Word strokes the square and this fills its four sides, which covers the
        // same ground; the cross has to be strokes, there being no other way to draw a diagonal.
        foreach (var (box, x) in line.Boxes)
        {
            var left = Grid.Snap(contentLeft + x + (box.Width - box.Side) / 2);
            // Measured from the baseline the text is written on, not from the line's own: the
            // box stands with the letters beside it, and Word's is a step lower than the line's
            // unrounded baseline would put it.
            var foot = Grid.Snap(baselineY + box.Below);
            var head = foot - box.Side;
            var colour = box.Format.GetColor();

            void Bar(double barX, double barY, double width, double height) =>
                page.Rectangles.Add(new PositionedRectangle
                {
                    X = barX,
                    Y = barY,
                    Width = width,
                    Height = height,
                    Color = colour
                });

            // The line is drawn about the edge, half of it either side, which is how Word strokes
            // the square: its own path runs corner to corner and the ink spreads from there.
            const double Line = CheckBoxLinePoints;
            var half = Line / 2;

            Bar(left - half, head - half, box.Side + Line, Line);
            Bar(left - half, foot - half, box.Side + Line, Line);
            Bar(left - half, head - half, Line, box.Side + Line);
            Bar(left + box.Side - half, head - half, Line, box.Side + Line);

            if (!box.Ticked) continue;

            page.Strokes.Add(new PositionedStroke
            {
                FromX = left, FromY = head, ToX = left + box.Side, ToY = foot,
                Thickness = CheckBoxCrossPoints, Color = colour
            });

            page.Strokes.Add(new PositionedStroke
            {
                FromX = left + box.Side, FromY = head, ToX = left, ToY = foot,
                Thickness = CheckBoxCrossPoints, Color = colour
            });
        }

        foreach (var (image, x) in line.Images)
        {
            // The image rests on the baseline, so its top edge is its own height above it. An
            // equation has none — it is text and rules — and only its content is placed.
            if (image.Image is { } picture)
            {
                page.Images.Add(new PositionedImage
                {
                    X = contentLeft + x,
                    Y = restingY - image.Height,
                    Width = image.Width,
                    Height = image.Height,
                    Image = picture
                });
            }

            // A shape's text goes down after its frame, so the frame is under it.
            image.Content?.PlaceOnto(page,
                contentLeft + x + image.ContentLeft,
                baselineY - image.Height + image.ContentTop);
        }

        page.Lines.Add(laidOut);

        return baselineY;
    }

    /// <summary>
    /// Sets a phonetic guide over the word it belongs to, and gives back the pair as text.
    /// </summary>
    /// <remarks>
    /// All of it measured from ruby-probe, which puts every alignment the markup has to Word:
    ///
    ///   * The wider of the guide and the word decides the room the pair takes, and the narrower
    ///     is set in the middle of it. A guide of eight letters over one takes forty-eight points,
    ///     with the word centred underneath.
    ///   * left and right set the guide against one end of the word; center between them.
    ///   * distributeLetter spreads the guide's letters so that the ends meet the word's ends —
    ///     four letters over three take three gaps of four points each.
    ///   * distributeSpace spreads them the same way but leaves space outside as well: half a gap
    ///     at each end, which is what Word's own 33 points inside a 36 point word comes to.
    ///   * The guide sits on a baseline of its own, raised off the word's by w:hpsRaise.
    /// </remarks>
    private static List<PositionedText> RubyTexts(RubyAtom ruby, double left, double baselineY)
    {
        var texts = new List<PositionedText>();

        // The wider of the two decides the room; the narrower is set in the middle of it.
        var wordAt = left + (ruby.Width - ruby.WordWidth) / 2;
        var guideBaseline = Grid.Snap(baselineY - ruby.Raise);

        var guideAt = ruby.Alignment switch
        {
            RubyAlignment.Left => wordAt,
            RubyAlignment.Right => wordAt + ruby.WordWidth - ruby.GuideWidth,
            _ => left + (ruby.Width - ruby.GuideWidth) / 2
        };

        // Spread, where the guide is asked to reach the ends of the word it stands over.
        var spare = ruby.Alignment is RubyAlignment.DistributeLetter or RubyAlignment.DistributeSpace
            ? Math.Max(0, ruby.WordWidth - ruby.GuideWidth)
            : 0;

        var between = 0.0;

        if (spare > 0 && ruby.GuideLetters > 1)
        {
            if (ruby.Alignment == RubyAlignment.DistributeLetter)
            {
                between = spare / (ruby.GuideLetters - 1);
                guideAt = wordAt;
            }
            else
            {
                between = spare / ruby.GuideLetters;
                guideAt = wordAt + between / 2;
            }
        }

        var guidePen = guideAt;

        // The guide first, as the document writes it: w:rt comes before w:rubyBase.
        foreach (var piece in ruby.Guide)
        {
            if (between <= 0)
            {
                texts.Add(Piece(piece, guidePen, guideBaseline));
                guidePen += piece.Width;
                continue;
            }

            // Spread: each letter set on its own, since what stands between them is not the space
            // the face gives them.
            foreach (var letter in piece.Text.EnumerateRunes())
            {
                var text = letter.ToString();
                var width = TextMeasurer.Measure(
                    piece.Font.Font, text, piece.Format.EffectiveFontSizePoints,
                    piece.Format.CharacterSpacingPoints) * piece.Format.ScaleFactor;

                texts.Add(Piece(piece with { Text = text, Width = width }, guidePen, guideBaseline));
                guidePen += width + between;
            }
        }

        var pen = wordAt;

        foreach (var piece in ruby.Word)
        {
            // Not on the grid: Word's own guided words stand where the arithmetic puts them, a
            // hundredth of a point off it, and the word and its guide have to agree with each
            // other more than with the grid.
            texts.Add(Piece(piece, pen, baselineY));
            pen += piece.Width;
        }

        return texts;
    }

    /// <summary>One piece of a guided word, ready to go on the page.</summary>
    private static PositionedText Piece(RubyPiece piece, double x, double baseline) => new()
    {
        X = x,
        BaselineY = baseline,
        Text = piece.Text,
        Format = piece.Format,
        Font = piece.Font,
        Width = piece.Width
    };

    /// <summary>
    /// Fills a tab's gap with its leader.
    /// </summary>
    /// <remarks>
    /// The characters sit on a grid of their own width measured from the left edge of the
    /// <em>page</em>, not from the margin or from where the gap begins — so two leaders on
    /// different lines line up with each other however far along their text ran out. Measured from
    /// Word's export of the <c>tab-leaders</c> fixture, where a hyphen leader starting at 131.871pt
    /// is an exact multiple of the 3.996pt hyphen and nothing else.
    ///
    /// Only whole characters are drawn, and only up to where the text after the tab begins, which
    /// can leave a fraction of the gap empty at the right-hand end.
    /// </remarks>
    private static void EmitLeader(
        LaidOutLine line, LeaderRun leader, double contentLeft, double baselineY, TabOptions tabs)
    {
        var character = LeaderCharacter(leader.Kind);
        if (character == '\0') return;

        var text = character.ToString();
        var size = leader.Format.EffectiveFontSizePoints;

        var width = TextMeasurer.Measure(
            leader.Font.Font, text, size, leader.Format.CharacterSpacingPoints,
            tabs.ApplyKerning || leader.Format.Kerned) * leader.Format.ScaleFactor;

        if (width <= 0.01) return;

        var from = contentLeft + leader.Start;
        var to = contentLeft + leader.End;

        var start = Math.Ceiling(from / width - 0.001) * width;
        var count = (int)Math.Floor((to - start) / width + 0.001);
        if (count <= 0) return;

        line.Texts.Add(new PositionedText
        {
            X = start,
            BaselineY = Grid.Snap(baselineY - leader.Format.BaselineShiftPoints),
            Text = new string(character, count),
            Format = leader.Format,
            Font = leader.Font,
            Width = count * width
        });
    }

    /// <summary>Adds the rules that draw underline and strikethrough for a placed run.</summary>
    private static void AddDecorations(LaidOutPage page, PositionedText text)
    {
        var format = text.Format;
        var metrics = text.Font.Font.Metrics;
        var size = format.EffectiveFontSizePoints;

        // Scale the thickness with the size so that headings get a proportionate rule.
        var thickness = Math.Max(0.5, size / 14.0);

        if (format.Underline != UnderlineStyle.None)
        {
            var offset = metrics.ToPoints(Math.Abs(metrics.Descender) * 0.45, size);
            page.Rules.Add(new PositionedRule
            {
                X = text.X,
                Y = text.BaselineY + offset,
                Width = text.Width,
                Thickness = format.Underline == UnderlineStyle.Thick ? thickness * 2 : thickness,
                Color = format.GetColor()
            });

            if (format.Underline == UnderlineStyle.Double)
            {
                page.Rules.Add(new PositionedRule
                {
                    X = text.X,
                    Y = text.BaselineY + offset + thickness * 2,
                    Width = text.Width,
                    Thickness = thickness,
                    Color = format.GetColor()
                });
            }
        }

        if (format.Strike)
        {
            // Strike through at roughly a third of the cap height above the baseline.
            page.Rules.Add(new PositionedRule
            {
                X = text.X,
                Y = text.BaselineY - metrics.ToPoints(metrics.XHeight, size) * 0.5,
                Width = text.Width,
                Thickness = thickness,
                Color = format.GetColor()
            });
        }
    }

    // ----- line composition -----

    /// <summary>
    /// Breaks a paragraph into lines, one at a time.
    /// </summary>
    /// <remarks>
    /// Incremental rather than all at once because the width available to a line depends on where
    /// that line lands: a floating image excludes part of the measure, and which part depends on
    /// the vertical position the line has reached. The caller therefore resolves the free band
    /// for each line and hands it in.
    /// </remarks>
    private sealed class ParagraphComposer(
        List<Atom> atoms, ResolvedParagraphFormat format, TabOptions tabs,
        (double Ascent, double Height) markMetrics, bool breakInsideWords = false,
        Hyphenation? hyphenation = null)
    {
        /// <summary>How many lines in a row have ended in a hyphen, for the limit on them.</summary>
        private int _hyphenated;

        private int _index;
        private bool _isFirstLine = true;
        private readonly TabOptions _tabs = tabs;
        private readonly (double Ascent, double Height) _markMetrics = markMetrics;
        private bool _forceBreakOnNextLine;
        private bool _forceColumnBreakOnNextLine;

        /// <summary>
        /// Whether the line about to be composed follows a break. Read before composing, because
        /// where the line lands decides how wide it may be.
        /// </summary>
        public bool PendingPageBreak => _forceBreakOnNextLine;

        public bool PendingColumnBreak => _forceColumnBreakOnNextLine;
        private bool _producedAny;

        /// <summary>An empty paragraph still gets one pass, so that it occupies a line.</summary>
        public bool HasMore => _index < atoms.Count || !_producedAny;

        /// <summary>
        /// Where the composer stands, so that a line can be composed again. A line that does not
        /// fit moves to the next page, where the measure may not be the one it was broken
        /// against — a float narrowed it here and there is none there.
        /// </summary>
        public (int Index, bool FirstLine, bool Produced) Mark => (_index, _isFirstLine, _producedAny);

        public void Rewind((int Index, bool FirstLine, bool Produced) mark)
        {
            _index = mark.Index;
            _isFirstLine = mark.FirstLine;
            _producedAny = mark.Produced;
        }

        /// <summary>
        /// A height to resolve the float band with, before the line's real height is known. The
        /// tallest thing in the paragraph is a safe over-estimate: it can only make the band more
        /// conservative, never place a line where it does not fit.
        /// </summary>
        public double ProvisionalHeight { get; } =
            atoms.Count == 0 ? 0 : atoms.Max(atom => atom.NaturalHeight);

        /// <summary>
        /// Composes the next line into the free bands it has been given, in the order they stand
        /// across the page.
        /// </summary>
        /// <remarks>
        /// One band is the ordinary case and the only one for most of a document. Two or more
        /// happen where something floats with room on both sides of it — a table put in the middle
        /// of the measure, a picture with text either side — and Word runs the line through all of
        /// them, left to right, as though the float were a hole in the paper. floating-table-wrap-probe
        /// measures that against Word.
        ///
        /// Each band is filled and finished on its own, so that a justified line is stretched to
        /// the edge of every band it passes through rather than to the last one only, which is
        /// what Word does with it. The pieces are then one line: they share a baseline, and the
        /// height and ascent of the tallest of them.
        /// </remarks>
        public ComposedLine Next(IReadOnlyList<(double Left, double Width)> bands)
        {
            _forceBreakOnNextLine = false;
            _forceColumnBreakOnNextLine = false;

            var line = Fill(bands[0], first: true, out var stopped);

            // The rest of the bands take what is left of the line, if anything is. A break ends
            // the line where it stands: what follows a line break belongs to the next line
            // wherever the room is.
            for (var i = 1; i < bands.Count && !stopped && _index < atoms.Count; i++)
            {
                var band = bands[i];
                var mark = _index;
                var piece = Fill(band, first: false, out stopped);

                // A band too narrow for the next word takes nothing rather than overflowing into
                // whatever stands beside it. The first band is not held to that: a word too long
                // for the whole measure has to go somewhere, and Word lets it overflow.
                if (piece.Segments.Count > 0 && LineWidth(piece) > band.Width + 0.001)
                {
                    _index = mark;
                    stopped = false;
                    continue;
                }

                Absorb(line, piece);
            }

            _isFirstLine = false;

            // An empty paragraph has no atoms but still takes up a line, sized by its mark.
            if (line.Segments.Count == 0) ApplyEmptyLineMetrics(line, format, _markMetrics);

            // Whether the margin's numbering passes it over belongs to the paragraph, and the line
            // has to carry it: a line outlives the paragraph that composed it.
            line.SuppressNumber = format.SuppressLineNumbers;

            // The background behind the paragraph rides on the line for the same reason, and the
            // indents with it: a shaded paragraph is one filled rectangle per line, and each is
            // drawn where the paragraph's own edges are rather than where its text reached.
            line.Shading = format.Shading.Resolve();
            line.ShadeLeft = format.IndentLeftPoints;
            line.ShadeRight = format.IndentRightPoints;

            return line;
        }

        /// <summary>Fills one band, and says whether the line ended inside it.</summary>
        private ComposedLine Fill((double Left, double Width) band, bool first, out bool stopped)
        {
            var indentLeft = format.IndentLeftPoints +
                             (_isFirstLine ? Math.Max(0, format.IndentFirstLinePoints) : 0);

            // A hanging indent pulls the first line left of the others, so it applies to the
            // first line as a negative offset rather than to the rest as a positive one.
            if (_isFirstLine && format.IndentFirstLinePoints < 0)
                indentLeft = format.IndentLeftPoints + format.IndentFirstLinePoints;

            // The line sits in whichever is the tighter of the indents and the free band. Only the
            // first band of a line is indented: an indent is measured from the margin, and a band
            // further across the page has already left the margin behind.
            var left = first ? Math.Max(indentLeft, band.Left) : band.Left;
            var right = band.Left + band.Width - (first ? format.IndentRightPoints : 0);
            var available = Math.Max(1, right - left);

            var line = new ComposedLine
            {
                IndentLeft = left
            };

            // A word is broken at the end of a line only where the document asks for it, the
            // paragraph does not refuse it, and no more lines have ended in a hyphen in a row than
            // the document allows.
            var breaking = hyphenation is { Automatic: true } terms &&
                           !format.SuppressAutoHyphens &&
                           (terms.ConsecutiveLimit == 0 || _hyphenated < terms.ConsecutiveLimit)
                ? hyphenation
                : null;

            var consumed = FillLine(
                atoms, _index, available, line, _tabs, breakInsideWords, breaking,
                out var hardBreak, out var pageBreak, out var columnBreak, out var hyphenated);

            _hyphenated = hyphenated ? _hyphenated + 1 : 0;
            _index += consumed;
            _producedAny = true;

            // The line is finished here if nothing is left of the paragraph or a break ended it;
            // otherwise it may carry on into the band beyond this one.
            stopped = hardBreak || pageBreak || columnBreak;

            var isLastLine = _index >= atoms.Count;
            FinishLine(line, format, left, available, isLastLine || hardBreak, _markMetrics.Height);

            if (pageBreak) _forceBreakOnNextLine = true;
            if (columnBreak) _forceColumnBreakOnNextLine = true;

            // Nothing was consumed and nothing remains: the one pass an empty paragraph gets.
            if (consumed == 0 && _index >= atoms.Count) _index = atoms.Count;

            return line;
        }

        /// <summary>How far the drawn part of a composed piece reaches from where it began.</summary>
        private static double LineWidth(ComposedLine line) =>
            line.Segments.Count == 0
                ? 0
                : line.Segments.Max(segment => segment.X + segment.Width) - line.IndentLeft;

        /// <summary>Takes a piece composed in a further band into the line it belongs to.</summary>
        private static void Absorb(ComposedLine line, ComposedLine piece)
        {
            line.Items.AddRange(piece.Items);
            line.Segments.AddRange(piece.Segments);
            line.Images.AddRange(piece.Images);
            line.Boxes.AddRange(piece.Boxes);
            line.Rubies.AddRange(piece.Rubies);
            line.Separators.AddRange(piece.Separators);
            line.Leaders.AddRange(piece.Leaders);
            line.Bars.AddRange(piece.Bars);

            // One line, so one baseline: the tallest piece decides where it falls and how much
            // room the line takes.
            line.Ascent = Math.Max(line.Ascent, piece.Ascent);
            line.Height = Math.Max(line.Height, piece.Height);
        }
    }

    /// <summary>
    /// Greedily packs atoms onto one line. Trailing spaces are allowed to overflow the measure,
    /// which is what Word does — a line ending in a space does not wrap because of it.
    /// </summary>
    private static int FillLine(
        List<Atom> atoms, int start, double available, ComposedLine line, TabOptions tabs,
        bool breakInsideWords, Hyphenation? hyphenation,
        out bool hardBreak, out bool pageBreak, out bool columnBreak, out bool hyphenated)
    {
        hardBreak = false;
        pageBreak = false;
        columnBreak = false;
        hyphenated = false;

        var x = 0.0;
        var index = start;
        var placedAnything = false;
        PendingTab? pending = null;

        // The box a run asks for takes room along the line, so the line has to be filled with that
        // room in hand: what is open when a word is measured has to be closed after it.
        ParagraphBorderEdge? openBorder = null;

        // Set once a tab has taken the pen past the measure. Word honours a stop that lies beyond
        // the right margin and lets the rest of the line run into it — and off the page, if that
        // is where the stops lead — rather than wrapping, so once that has happened nothing on
        // this line wraps either.
        var beyondMargin = false;

        while (index < atoms.Count)
        {
            var atom = atoms[index];

            if (atom is BreakAtom breakAtom)
            {
                index++;
                hardBreak = true;
                pageBreak = breakAtom.Kind == BreakKind.Page;
                columnBreak = breakAtom.Kind == BreakKind.Column;
                break;
            }

            if (atom is TabAtom tab)
            {
                // Where the previous aligning tab put its text decides which stop this one takes.
                x = ClosePendingTab(line, ref pending, x, tabs);
                if (x >= available - 0.001) beyondMargin = true;

                // Tab stops are measured from the margin rather than from the paragraph's own
                // indent — an indented paragraph's stops stay where the unindented ones are, which
                // is what lines the page numbers of a table of contents up however deep the entry
                // is. The line works in its own coordinates, so the two are converted between.
                var (stop, alignment, leader) =
                    NextTabStop(x + line.IndentLeft, tab.Stops, tab.DefaultIntervalPoints);

                var next = stop - line.IndentLeft;

                if (alignment == TabAlignment.Left)
                {
                    line.Items.Add(new PlacedAtom(atom, x, next - x, leader));
                    x = next;
                }
                else
                {
                    line.Items.Add(new PlacedAtom(atom, x, 0, leader));
                    pending = new PendingTab(line.Items.Count - 1, next, alignment, x);
                }

                if (x >= available - 0.001) beyondMargin = true;

                index++;
                placedAnything = true;
                continue;
            }

            if (atom is SeparatorAtom)
            {
                line.Items.Add(new PlacedAtom(atom, x, 0));
                index++;
                placedAnything = true;
                continue;
            }

            if (atom is RubyAtom ruby)
            {
                if (placedAnything && !beyondMargin && x + ruby.Width > available + 0.001) break;

                line.Items.Add(new PlacedAtom(atom, x, ruby.Width));
                x += ruby.Width;
                index++;
                placedAnything = true;
                continue;
            }

            if (atom is CheckBoxAtom checkBox)
            {
                if (placedAnything && !beyondMargin && x + checkBox.Width > available + 0.001) break;

                line.Items.Add(new PlacedAtom(atom, x, checkBox.Width));
                x += checkBox.Width;
                index++;
                placedAnything = true;
                continue;
            }

            if (atom is ImageAtom image)
            {
                // An image behaves like a very wide word: it cannot be broken, and it wraps to
                // the next line rather than overflowing unless it is alone on the line.
                if (placedAnything && !beyondMargin && x + image.Width > available + 0.001) break;

                line.Items.Add(new PlacedAtom(atom, x, image.Width));
                x += image.Width;
                index++;
                placedAnything = true;
                continue;
            }

            var textAtom = (TextAtom)atom;

            // An atom opening a line has nothing before it to be kerned against, so whatever was
            // folded into its width for the atom that used to precede it comes back off.
            var width = placedAnything ? textAtom.Width : textAtom.Width - textAtom.LeadingKern;

            // A box opening or closing here takes its room before the word is weighed against
            // what is left, and the box still open at the end of the line takes its own.
            if (!Equals(textAtom.Format.Border, openBorder))
            {
                x += RunBorderRoom(openBorder) + RunBorderRoom(textAtom.Format.Border);
                openBorder = textAtom.Format.Border;
            }

            var closing = RunBorderRoom(openBorder);

            // A word too wide for a line of its own is broken inside rather than overrunning:
            // there is no tolerance in it at all, and break-tolerance-probe measures that a twip
            // at a time — ten capital Ms stay whole in 106.7 points of measure and come apart in
            // 106.65, their own width being 106.6992. That is as true of a page as of a box; the
            // note that used to stand here said otherwise, from two probes that both held boxes.
            //
            // The break is put off until the word has a line to itself: Word ends the line before
            // it first and breaks what is left, which is why cell-direction-probe's "and rather"
            // comes out "an", "d", "rat" and not "an", "d r", "at".
            if (breakInsideWords && !placedAnything && !textAtom.IsSpace &&
                width > available + 0.001 && textAtom.Text.Length > 1)
            {
                textAtom = SplitToFit(atoms, index, textAtom, available);
                width = textAtom.Width - textAtom.LeadingKern;

            }

            // Spaces at the end of a line hang past the margin rather than forcing a wrap.
            if (!textAtom.IsSpace && placedAnything && !beyondMargin &&
                x + width + closing > available + 0.001)
            {
                // Before the line is given up on: where the document asks for it and the white
                // left over is worth filling, the word is broken and as much of it as fits stays.
                if (hyphenation is { } terms && available - x > terms.ZonePoints &&
                    Hyphenate(atoms, index, textAtom, available - x, terms) is { } divided)
                {
                    line.Items.Add(new PlacedAtom(divided, x, divided.Width));
                    index++;
                    hyphenated = true;
                }

                break;
            }

            // The atom rather than what was read from the list: a word divided to fit is not the
            // word the list held when this line began.
            line.Items.Add(new PlacedAtom(textAtom, x, width));
            x += width;
            index++;
            placedAnything = true;

            // A single word longer than the measure has to go somewhere. Where the box says it may
            // be divided it has been, above; where it may not — the page's own measure — it
            // overflows rather than looping for ever.
            if (!textAtom.IsSpace && x > available && line.Items.Count == 1) break;
        }

        ClosePendingTab(line, ref pending, x, tabs);
        return index - start;
    }

    /// <summary>
    /// Breaks a word at the end of a line, where the language allows it and enough of the word
    /// fits. Returns the part that stays, hyphen and all, or null where the word is left whole.
    /// </summary>
    /// <remarks>
    /// Where the word may be broken is the hyphenation table's business; which of those places is
    /// used is this one's, and it is the last that fits — Word's own, measured in
    /// hyphenation-probe, breaks conspicuous after "conspicu" and organisation after "or", each
    /// being as much of the word as the line had room for.
    ///
    /// The pieces either side of a break are measured without kerning: there is nothing across a
    /// line's end to be kerned against.
    /// </remarks>
    private static TextAtom? Hyphenate(
        List<Atom> atoms, int index, TextAtom atom, double room, Hyphenation terms)
    {
        // Only a word: punctuation on either side of it is left where it is, and the letters
        // between are what the table knows about.
        var text = atom.Text;
        var from = 0;
        var to = text.Length;

        while (from < to && !char.IsLetter(text[from])) from++;
        while (to > from && !char.IsLetter(text[to - 1])) to--;

        var word = text[from..to];
        if (word.Length < 5) return null;

        // A word in capitals is left whole where the document says so.
        if (terms.LeaveCapitalsAlone && word.All(letter => !char.IsLower(letter))) return null;

        var points = Text.Hyphenator.Points(word);
        if (points.Count == 0) return null;

        var format = atom.Format;
        var face = atom.Font;

        double Measure(string piece) =>
            TextMeasurer.Measure(
                face.Font, piece, format.EffectiveFontSizePoints,
                format.CharacterSpacingPoints, applyKerning: false) * format.ScaleFactor;

        // The last place that fits, hyphen included.
        for (var i = points.Count - 1; i >= 0; i--)
        {
            var head = string.Concat(text[..(from + points[i])], "-");
            var width = Measure(head);

            if (width > room + 0.001) continue;

            var rest = text[(from + points[i])..];

            atoms[index] = atom.Divide(head, width, leadingKern: 0);
            atoms.Insert(index + 1, atom.Divide(rest, Measure(rest), leadingKern: 0));

            return (TextAtom)atoms[index];
        }

        return null;
    }

    /// <summary>
    /// Divides a word too wide for the line it is on, leaving as much of it as fits in the list
    /// and the rest to follow. Returns the part that stays.
    /// </summary>
    /// <remarks>
    /// Measured character by character rather than by taking a share of the width: the letters of
    /// a word are not all the same width, and Word fits what fits. Its own drawing of the diagram
    /// fixture sets "Three" as "Thre" and "e", and of cell-direction-probe's fifth-of-an-inch cell
    /// "Unturnable" as "Unt", "urn", "abl" and "e".
    ///
    /// The two parts are measured without kerning: what is drawn on either side of the break has
    /// nothing to be kerned against there.
    /// </remarks>
    private static TextAtom SplitToFit(List<Atom> atoms, int index, TextAtom atom, double available)
    {
        var format = atom.Format;
        var face = atom.Font;

        double Measure(string text) =>
            TextMeasurer.Measure(
                face.Font, text, format.EffectiveFontSizePoints,
                format.CharacterSpacingPoints, applyKerning: false) * format.ScaleFactor;

        var runes = atom.Text.EnumerateRunes().Select(rune => rune.ToString()).ToList();
        if (runes.Count < 2) return atom;

        var taken = 1;
        var width = Measure(runes[0]);

        // At least one letter, however narrow the box: a line has to take something or the
        // paragraph would never end.
        for (var i = 1; i < runes.Count; i++)
        {
            var next = Measure(string.Concat(runes.Take(i + 1)));
            if (next > available + 0.001) break;

            taken = i + 1;
            width = next;
        }

        if (taken >= runes.Count) return atom;

        var head = atom.Divide(string.Concat(runes.Take(taken)), width, leadingKern: 0);
        var rest = string.Concat(runes.Skip(taken));
        var tail = atom.Divide(rest, Measure(rest), leadingKern: 0);

        atoms[index] = head;
        atoms.Insert(index + 1, tail);

        return head;
    }

    /// <summary>
    /// Converts the placed atoms into drawable segments, applying alignment and merging adjacent
    /// atoms that share formatting so the content stream carries one show-text per run rather
    /// than one per word.
    /// </summary>
    private static void FinishLine(
        ComposedLine line, ResolvedParagraphFormat format, double indentLeft, double available,
        bool isLastLine, double markHeight)
    {
        // Trailing spaces do not participate in alignment: a centred line ending in a space
        // would otherwise sit visibly off-centre.
        var content = line.Items;
        var lastVisible = content.Count - 1;
        while (lastVisible >= 0 && content[lastVisible].Atom is TextAtom { IsSpace: true })
            lastVisible--;

        var lineWidth = lastVisible >= 0
            ? content[lastVisible].X + content[lastVisible].Width
            : 0;

        var offset = 0.0;
        var wordSpacing = 0.0;

        switch (format.Justification)
        {
            case Justification.Center:
                offset = (available - lineWidth) / 2;
                break;
            case Justification.Right:
                offset = available - lineWidth;
                break;
            case Justification.Both or Justification.Distribute when !isLastLine:
                // Justification stretches the spaces rather than the words. The last line of a
                // paragraph is left alone, which is why it needs to be identified.
                var spaceCount = content.Take(lastVisible + 1).Count(item => item.Atom is TextAtom { IsSpace: true });
                if (spaceCount > 0 && lineWidth < available)
                    wordSpacing = (available - lineWidth) / spaceCount;
                break;
        }

        offset = Math.Max(offset, 0);

        // Merge runs of atoms that share a format into single segments.
        // Text and images size a line differently. A text run brings its whole line box, so two
        // fonts on one line give the taller of the two boxes rather than the tallest ascent bolted
        // onto the deepest descent — mixing a label's font with the text's that way made every
        // list item a quarter point too tall. An image has no line box: it rests on the baseline
        // and the descent beneath it still comes from the text.
        var maxTextNatural = 0.0;
        var maxTextAscent = 0.0;
        var maxTextDescent = 0.0;
        var maxImageAscent = 0.0;

        // A picture sits on the baseline and hangs nothing below it; an equation is not a picture
        // and a fraction reaches under the line as far as it reaches over it.
        var maxImageDescent = 0.0;

        Segment? current = null;
        var pen = 0.0;

        // The box a run asks for, while the pen is inside it.
        ParagraphBorderEdge? openBorder = null;

        // Trailing spaces are dropped rather than emitted. They draw nothing, and keeping them
        // would make the line measure wider than its visible content — which in a justified
        // paragraph pushes the visible text past the right margin.
        foreach (var item in ForDrawing(content.Take(lastVisible + 1).ToList()))
        {
            if (item.Atom is TabAtom tabAtom)
            {
                current = null;
                pen = item.X + item.Width;

                if (item.Leader != TabLeader.None && item.Width > 0.5)
                {
                    var from = indentLeft + offset + item.X;
                    line.Leaders.Add(new LeaderRun(
                        from, from + item.Width, item.Leader, tabAtom.Format, tabAtom.Font));
                }

                continue;
            }

            if (item.Atom is SeparatorAtom separator)
            {
                line.Separators.Add((separator, indentLeft + offset + pen));
                current = null;

                // The rule draws nothing on the baseline but its run's font still sets the line
                // box, which is the space Word leaves between the body text and the notes.
                maxTextAscent = Math.Max(maxTextAscent, separator.Ascent);
                maxTextDescent = Math.Max(maxTextDescent, separator.Descent);
                maxTextNatural = Math.Max(maxTextNatural, separator.NaturalHeight);
                continue;
            }

            if (item.Atom is RubyAtom ruby)
            {
                line.Rubies.Add((ruby, indentLeft + offset + pen));
                pen += ruby.Width;
                current = null;

                maxTextAscent = Math.Max(maxTextAscent, ruby.Ascent);
                maxTextDescent = Math.Max(maxTextDescent, ruby.Descent);
                maxTextNatural = Math.Max(maxTextNatural, ruby.NaturalHeight);
                continue;
            }

            if (item.Atom is CheckBoxAtom box)
            {
                line.Boxes.Add((box, indentLeft + offset + pen));
                pen += box.Width;
                current = null;

                maxTextAscent = Math.Max(maxTextAscent, box.Ascent);
                maxTextDescent = Math.Max(maxTextDescent, box.Descent);
                maxTextNatural = Math.Max(maxTextNatural, box.NaturalHeight);
                continue;
            }

            if (item.Atom is ImageAtom image)
            {
                line.Images.Add((image, indentLeft + offset + pen));
                pen += image.Width;
                current = null;

                maxImageAscent = Math.Max(maxImageAscent, image.Ascent);
                maxImageDescent = Math.Max(maxImageDescent, image.Descent);
                continue;
            }

            var textAtom = (TextAtom)item.Atom;
            var extra = textAtom.IsSpace ? wordSpacing : 0;

            // A box round a run takes room along the line as well as above and below it: the pen
            // moves on by the weight where one opens and again where it closes, which is what
            // puts Word's bordered run a line's weight further along than an unbordered one.
            if (!Equals(textAtom.Format.Border, openBorder))
            {
                if (openBorder is { } closing) pen += RunBorderRoom(closing);
                if (textAtom.Format.Border is { } opening) pen += RunBorderRoom(opening);

                openBorder = textAtom.Format.Border;
                current = null;
            }

            // The width the line settled on, which is the atom's own less any leading kern the
            // line's first atom gave up.
            var width = item.Width;

            if (current is not null &&
                current.Level == textAtom.Level &&
                ReferenceEquals(current.Format, textAtom.Format) &&
                Equals(current.Font, textAtom.Font) &&
                Equals(current.Link, textAtom.Link) &&
                Math.Abs(current.X + current.Width - (indentLeft + offset + pen)) < 0.001)
            {
                // The words arrive in the order they are drawn, and a segment holds the order they
                // are read in — the two are the same only one way round. A word joined onto a
                // right-to-left segment goes on the front of it, because what is drawn further
                // left was read later.
                current.Text = (textAtom.Level & 1) != 0
                    ? textAtom.Text + current.Text
                    : current.Text + textAtom.Text;

                current.Width += width + extra;
                current.SpaceCount += textAtom.IsSpace ? 1 : 0;
                current.Ascent = Math.Max(current.Ascent, textAtom.Ascent);
                current.Descent = Math.Max(current.Descent, textAtom.Descent);
            }
            else
            {
                current = new Segment
                {
                    X = indentLeft + offset + pen,
                    Level = textAtom.Level,
                    Text = textAtom.Text,
                    Format = textAtom.Format,
                    Font = textAtom.Font,
                    Width = width + extra,
                    Ascent = textAtom.Ascent,
                    Descent = textAtom.Descent,
                    WordSpacing = wordSpacing,
                    SpaceCount = textAtom.IsSpace ? 1 : 0,
                    Link = textAtom.Link,
                    Kerned = textAtom.Kerned
                };

                line.Segments.Add(current);
            }

            pen += width + extra;

            if (!textAtom.InLineBox) continue;

            // A run with a box round it makes the line taller by what the box takes: its own
            // weight below the text, and the weight and a step of the grid above it, which is
            // what run-border-probe measures at every size from eight point to forty-eight.
            var edging = RunBorderRoom(textAtom.Format.Border);
            var overhead = edging > 0 ? edging + Grid.Step : 0;

            maxTextAscent = Math.Max(maxTextAscent, textAtom.Ascent + overhead);
            maxTextDescent = Math.Max(maxTextDescent, textAtom.Descent + edging);
            maxTextNatural = Math.Max(maxTextNatural, textAtom.NaturalHeight + overhead + edging);
        }

        // A bar stop is not somewhere text lands: it asks for a rule down every line of the
        // paragraph, whether or not the line holds a tab at all.
        foreach (var stop in format.TabStops)
        {
            if (stop.Alignment == TabAlignment.Bar)
                line.Bars.Add(Units.TwipsToPoints(stop.PositionTwips));
        }

        if (openBorder is { } last) pen += RunBorderRoom(last);

        var ascent = Math.Max(maxTextAscent, maxImageAscent);

        // The line box is the tallest ascent over the deepest descent, which is not the same as
        // the tallest of the runs' own boxes: a line of twelve point Times with an eleven point
        // Calibri mark at the end of it takes the Times ascent and the Calibri descent, and is
        // deeper than either font would make it alone. Word measured a line that way in every
        // fixture here that mixes two fonts on one line.
        var descent = Math.Max(maxTextDescent, maxImageDescent);

        var natural = Math.Max(maxTextAscent + descent, maxImageAscent + descent);

        // Nothing about a run's own box is lost by that: a single-font line is the same either
        // way, since one run's ascent and descent are its natural height.
        natural = Math.Max(natural, maxTextNatural);

        // The same box with no picture in it, which is what a multiple of the line is measured
        // against: see ApplyLineMetrics.
        var textBox = Math.Max(Math.Max(maxTextAscent + descent, maxTextNatural), 0);
        var textAscent = maxTextAscent;

        // A line holding nothing but a picture is no shorter than the paragraph's own mark.
        // vml-stroke-stack-probe stacks shapes four and a half and nine points tall under an
        // eleven point mark, and Word gives both the mark's own line of 13.43 rather than the
        // shape's height — and puts the shape at the foot of it, which is what resting on the
        // baseline of a line that tall comes to.
        //
        // Only such a line. Flooring every line at its mark is what this first tried, and fifteen
        // fixtures said no: where a line has text of its own, that text sizes it and the mark of a
        // different size does not lift it.
        if (maxTextNatural <= 0 && line.Images.Any(entry => entry.Atom.Image is not null) &&
            markHeight > natural)
        {
            // The room the mark adds goes above the picture, not below it: the picture keeps the
            // descent it asked for and the baseline rises to leave the rest over it, which is what
            // standing at the foot of the line comes to. Word draws the 4½pt shape of the probe
            // 8.43 points down its 13.43 point line, and that is 13.43 less the seven the shape
            // asks for, plus the two its outline is offset by.
            natural = markHeight;
            ascent = natural - descent;

            // With nothing but a picture on it, the mark's line is the line the multiple has to
            // work on, since there is no text box of its own to take.
            textBox = natural;
            textAscent = ascent;
        }

        ApplyLineMetrics(line, format, ascent, natural, textBox, textAscent);
    }

    /// <summary>
    /// Sizes a line that nothing was placed on, from the paragraph mark's own font.
    /// </summary>
    /// <remarks>
    /// The metrics are measured from that font like any other line's rather than estimated from
    /// the type size. The estimate this replaced left an empty paragraph 0.8pt short of Word at
    /// eleven point, which the rule of a bar tab stop — drawn down the whole line box, empty or
    /// not — made plain to see.
    /// </remarks>
    private static void ApplyEmptyLineMetrics(
        ComposedLine line, ResolvedParagraphFormat format, (double Ascent, double Height) mark)
    {
        if (line.Height > 0) return;

        ApplyLineMetrics(line, format, mark.Ascent, mark.Height, mark.Height, mark.Ascent);
    }

    /// <summary>
    /// Where the baseline of a line of an exact height falls: four fifths of the way down it, on
    /// the step of the grid Word draws on.
    /// </summary>
    /// <remarks>
    /// The share is Word's own and not the font's. A sweep of fifty-three heights was run twice
    /// over — fifty-six point Times and twenty-four point Verdana — and Word put every baseline of
    /// the second in exactly the place it put the first; the probe's own last three pages set the
    /// same height in Times, Arial and Calibri, whose descents are five steps of the grid apart,
    /// and Word sets all three on one baseline.
    ///
    /// Four fifths alone lands one step of the grid out on about a fifth of the heights, and what
    /// it takes to land on all of them was measured by sweeping every height a twip at a time —
    /// 865 of them, from fifteen points to a hundred and fifty. Two things come out of that sweep,
    /// and neither is derived from anything:
    ///
    ///   * the height behaves as though it were **one twip larger or smaller** before the four
    ///     fifths is taken, by where the answer falls: a twip larger where the whole steps of the
    ///     ascent leave one over four, a twip smaller where they leave two or three, and the
    ///     height itself where they divide evenly. That is 779 of the 865 exactly, and the whole
    ///     of the rest is the case below.
    ///
    ///   * where the height and its fifth **both** land half way between two steps of the grid —
    ///     which is every odd multiple of three points — Word takes a further step, except at one
    ///     height in five and then at one of those in five again. Written in fifths, with j the
    ///     number of such heights below this one: the step is taken where j's last digit in base
    ///     five is under three and its next digit is not two. That accounts for all 128 of the
    ///     heights the sweeps hold, from fifteen points to a hundred and forty-nine.
    ///
    /// The second rule is the one to be suspicious of: it is a measured pattern in base five and
    /// nothing here explains why base five should come into it, beyond the four fifths itself.
    /// It was checked against a second sweep at heights the first never reached and predicted
    /// all sixty-three of them, which is the only reason it is here rather than left as a step
    /// out. ExactLineTests holds both, and the sweep that measured them.
    /// </remarks>
    private static double ExactAscent(int heightTwips)
    {
        if (heightTwips <= 0) return 0;

        // The ascent in whole steps of the grid, before anything is done about the last one: the
        // height in twips is six twips to the step, four fifths of the 4.8 a step of the grid is.
        var steps = heightTwips / 6;

        var nudged = heightTwips + (steps % 4) switch { 1 => 1, 2 or 3 => -1, _ => 0 };

        // Rounded to the nearest step, halves away from nought, in whole numbers so that nothing
        // of the twip the nudge added is lost on the way.
        var ascent = (2 * nudged + 6) / 12;

        if (heightTwips % 24 == 12)
        {
            var j = (heightTwips - 12) / 24;

            if (j % 5 <= 2 && j % 25 is not (10 or 11 or 12)) ascent++;
        }

        return ascent * Grid.Step;
    }

    private static void ApplyLineMetrics(
        ComposedLine line, ResolvedParagraphFormat format, double maxAscent, double naturalHeight,
        double textBox, double textAscent)
    {
        if (naturalHeight <= 0) return;

        switch (format.LineRule)
        {
            case LineSpacingRule.Exact:
                line.Height = format.LineSpacingPoints;
                line.Ascent = ExactAscent(format.Line);
                line.ExactHeight = true;
                break;

            case LineSpacingRule.AtLeast:
                line.Height = Math.Max(naturalHeight, format.LineSpacingPoints);
                line.Ascent = maxAscent;
                break;

            // A drawing's multiple takes its room off the top: the descent stays whole and the
            // baseline moves. See LineSpacingRule.Scaled for the measurement.
            case LineSpacingRule.Scaled:
                line.Height = naturalHeight * format.LineSpacingMultiple;
                line.Ascent = line.Height - (naturalHeight - maxAscent);
                break;

            default:
                // A multiple is a multiple of the line the *text* would have made, not of the
                // line a picture on it has made taller. image-line-probe puts a picture of six,
                // twelve, twenty-four and ninety-six points on a line of twelve point Times at
                // multiples of one, 1.08, one and a half and two, and in all sixteen Word leaves
                // exactly the room under the picture that it leaves under the text alone: a
                // ninety-six point picture on a 1.08 line is 99.6 points tall, which is the
                // picture plus the 3.6 the text asks for, and not the 106.8 that multiplying the
                // whole box gives. Word's own Normal asks for 1.08, so every picture dropped into
                // a real document lands on this rule.
                line.Height = maxAscent + (textBox * format.LineSpacingMultiple - textAscent);

                // Extra leading from a multiple goes *below* the baseline, not above it, so the
                // first line of a paragraph sits at its natural ascent no matter what the
                // multiple is. Verified against Word: its first baseline moves by a fifth of a
                // point as the multiple goes from single to double, while adding the leading
                // above moved ours by a full 13.8pt.
                line.Ascent = maxAscent;
                break;
        }
    }

    /// <summary>
    /// The next tab stop at or beyond the pen, and how it aligns what follows it.
    /// </summary>
    /// <remarks>
    /// A cleared stop is one the paragraph removed from what it inherited. A bar stop is not a
    /// stop at all — it asks for a vertical rule at that position and a tab passes straight
    /// through it — so neither is a candidate. The rule itself is not drawn.
    ///
    /// Past the last stop a document declares, tabs fall on the document's default interval, and
    /// those are always left-aligned.
    /// </remarks>
    private static (double Position, TabAlignment Alignment, TabLeader Leader) NextTabStop(
        double x, IReadOnlyList<TabStop> stops, double defaultInterval)
    {
        foreach (var stop in stops.OrderBy(s => s.PositionTwips))
        {
            if (stop.Alignment is TabAlignment.Clear or TabAlignment.Bar) continue;

            var position = Units.TwipsToPoints(stop.PositionTwips);
            if (position > x + 0.001) return (position, stop.Alignment, stop.Leader);
        }

        if (defaultInterval <= 0) return (x, TabAlignment.Left, TabLeader.None);

        var next = Math.Floor(x / defaultInterval + 1) * defaultInterval;
        return (next <= x ? x + defaultInterval : next, TabAlignment.Left, TabLeader.None);
    }

    /// <summary>
    /// A tab whose stop aligns what comes after it, held while that text is being placed.
    /// </summary>
    /// <param name="PenBefore">Where the line had reached when the tab was met.</param>
    private readonly record struct PendingTab(
        int Index, double Stop, TabAlignment Alignment, double PenBefore);

    /// <summary>The characters Word fills a tab's gap with, one per kind of leader.</summary>
    private static char LeaderCharacter(TabLeader leader) => leader switch
    {
        TabLeader.Dot => '.',
        TabLeader.Hyphen => '-',
        TabLeader.Underscore => '_',
        TabLeader.MiddleDot => '\u00b7',
        _ => '\0'
    };

    /// <summary>
    /// Settles where the text after a centre, right or decimal tab starts, now that there is
    /// enough of it to measure.
    /// </summary>
    /// <remarks>
    /// The run is placed from the pen while it is being filled, because where it belongs is not
    /// known until it ends: a right-aligned run at the margin would otherwise look like an
    /// overflowing line and wrap. Once its width is known the whole run shifts right, and the tab
    /// itself takes up the difference.
    ///
    /// Text never starts before the pen. A stop the line has already passed cannot pull text
    /// backwards over what is already there, so the tab does nothing and the text simply follows
    /// on — which is what Word does with a stop that has been overrun.
    /// </remarks>
    private static double ClosePendingTab(
        ComposedLine line, ref PendingTab? pending, double x, TabOptions options)
    {
        if (pending is not { } tab) return x;
        pending = null;

        var start = tab.Index + 1;
        var width = 0.0;
        for (var i = start; i < line.Items.Count; i++) width += line.Items[i].Width;

        var offset = tab.Alignment switch
        {
            TabAlignment.Center => width / 2,
            TabAlignment.Decimal => DecimalOffset(line, start, width, options),
            _ => width
        };

        var startX = Math.Max(tab.Stop - offset, tab.PenBefore);
        var delta = startX - tab.PenBefore;

        if (delta > 0.001)
        {
            line.Items[tab.Index] = line.Items[tab.Index] with { Width = delta };

            for (var i = start; i < line.Items.Count; i++)
                line.Items[i] = line.Items[i] with { X = line.Items[i].X + delta };
        }

        return startX + width;
    }

    /// <summary>
    /// How far into a run its decimal separator sits, which is the part that lands on the stop.
    /// </summary>
    /// <remarks>
    /// A run with no separator in it is aligned by its right edge instead, the way Word treats a
    /// decimal stop with nothing to align — which is what makes a column of figures with a total
    /// line reading "Total" line up at all.
    /// </remarks>
    private static double DecimalOffset(ComposedLine line, int start, double width, TabOptions options)
    {
        var before = 0.0;

        for (var i = start; i < line.Items.Count; i++)
        {
            if (line.Items[i].Atom is not TextAtom text)
            {
                before += line.Items[i].Width;
                continue;
            }

            var at = text.Text.IndexOf(options.DecimalSymbol, StringComparison.Ordinal);
            if (at < 0)
            {
                before += line.Items[i].Width;
                continue;
            }

            return before + TextMeasurer.Measure(
                text.Font.Font, text.Text[..at], text.Format.EffectiveFontSizePoints,
                text.Format.CharacterSpacingPoints, options.ApplyKerning || text.Format.Kerned)
                * text.Format.ScaleFactor;
        }

        return width;
    }

    // ----- atom construction -----

    /// <summary>
    /// Flattens a paragraph into the smallest units line breaking works with: words, individual
    /// spaces, tabs and explicit breaks.
    /// </summary>
    private List<Atom> BuildAtoms(Paragraph paragraph, ResolvedParagraphFormat format)
    {
        var atoms = new List<Atom>();
        var defaultTab = Units.TwipsToPoints(_options.DefaultTabStopTwips);

        // Which way each character of the paragraph runs. It has to be worked out over the whole
        // paragraph rather than run by run: what a comma or a digit does depends on what stands
        // either side of it, and a run boundary is nothing to the reader.
        var levels = ParagraphLevels(paragraph, format);
        var at = 0;

        AddNumberingLabel(atoms, paragraph, format, defaultTab);

        foreach (var run in paragraph.Runs)
        {
            var runFormat = _styles.ResolveRun(paragraph.Properties, run.Properties);
            if (runFormat.Hidden) continue;

            var link = ResolveHyperlink(run.Hyperlink);

            var selection = _fonts.Resolve(runFormat.FontFamily, runFormat.Bold, runFormat.Italic);
            var size = runFormat.EffectiveFontSizePoints;

            // The line is measured from the size the run declares, not the size it is drawn at: a
            // raised or lowered run keeps the box of its own size and moves inside it.
            var box = runFormat.LineBoxFontSizePoints;
            var ascent = TextMeasurer.GetAscent(selection.Font, box);
            var naturalHeight = TextMeasurer.GetNaturalLineHeight(selection.Font, box);
            var descent = naturalHeight - ascent;

            foreach (var inline in run.Content)
            {
                switch (inline)
                {
                    case TextInline text:
                        AddTextAtoms(atoms, TextMeasurer.ApplyTextTransform(text.Text, runFormat),
                            runFormat, selection, ascent, naturalHeight, descent, link, levels, at);

                        at += text.Text.Length;
                        break;

                    case RubyInline ruby:
                        atoms.Add(RubyOf(
                            ruby, paragraph.Properties, runFormat, ascent, naturalHeight, descent));
                        break;

                    case SymbolInline symbol:
                    {
                        // A symbol brings its own face, and brings it only for itself: the run
                        // around it keeps the one it had. Its own line box comes with it, so a
                        // Wingdings character in a line of Times makes the line as tall as
                        // Wingdings asks for.
                        var symbolFormat = symbol.Font is null
                            ? runFormat
                            : runFormat with { FontFamily = symbol.Font };

                        var symbolFont = symbol.Font is null
                            ? selection
                            : _fonts.Resolve(symbol.Font, runFormat.Bold, runFormat.Italic);

                        var symbolBox = symbolFormat.LineBoxFontSizePoints;
                        var symbolAscent = TextMeasurer.GetAscent(symbolFont.Font, symbolBox);
                        var symbolNatural = TextMeasurer.GetNaturalLineHeight(symbolFont.Font, symbolBox);

                        AddTextAtoms(atoms, symbol.Text, symbolFormat, symbolFont,
                            symbolAscent, symbolNatural, symbolNatural - symbolAscent,
                            link, levels, at);

                        at += symbol.Text.Length;
                        break;
                    }

                    case BookmarkInline bookmark:
                        // Recorded where the paragraph has reached, which is the line the mark
                        // appears on; a reader following the link lands on that line.
                        _pendingBookmarks.Add(bookmark.Name);
                        break;

                    case TabInline:
                        atoms.Add(new TabAtom
                        {
                            Stops = format.TabStops,
                            DefaultIntervalPoints = defaultTab,
                            Format = runFormat,
                            Font = selection,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight,
                            Descent = descent
                        });
                        break;

                    case BreakInline breakInline:
                        atoms.Add(new BreakAtom
                        {
                            Kind = breakInline.Kind,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight,
                            Descent = descent
                        });
                        break;

                    case DrawingInline drawing:
                        AddImageAtom(atoms, drawing);
                        break;

                    case MathInline math:
                        AddMathAtom(atoms, math, runFormat);
                        break;

                    case NoteReferenceInline reference:
                        AddNoteMark(atoms, reference, runFormat, selection,
                            ascent, naturalHeight, descent, link);
                        break;

                    case NoteMarkInline when _currentNoteLabel is { } label:
                        AddTextAtoms(atoms, label, runFormat, selection,
                            ascent, naturalHeight, descent, link);
                        break;

                    case SeparatorInline separator:
                        atoms.Add(new SeparatorAtom
                        {
                            // The rule above a note carried over runs right across the measure;
                            // the one above a note that begins where it stands is two inches.
                            Width = separator.Continuation ? _separatorMeasure : FootnoteSeparatorWidthPoints,
                            Thickness = FootnoteSeparatorThicknessPoints,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight,
                            Descent = descent
                        });
                        break;

                    case FieldInline field when FieldInstruction.Parse(field.Instruction).Keyword == "XE":
                        // An entry marker: it draws nothing, and is recorded with the page once
                        // the paragraph carrying it has been placed.
                        _pendingMarks.Add(field);
                        break;

                    // A checkbox draws no text at all: the box is the field, and Word draws it
                    // with lines rather than setting a character from a face.
                    case FieldInline { CheckBox: { } ticked }:
                        atoms.Add(CheckBoxOf(ticked, runFormat, selection));
                        break;

                    case FieldInline field:
                    {
                        var text = ResolveField(field, out var occurrence);
                        var first = atoms.Count;

                        AddTextAtoms(atoms, TextMeasurer.ApplyTextTransform(text, runFormat),
                            runFormat, selection, ascent, naturalHeight, descent, link);

                        // Tagged so that the line these end up on says which page the field is on,
                        // the way a footnote mark says which page its note belongs to.
                        for (var i = first; i < atoms.Count; i++)
                        {
                            if (atoms[i] is TextAtom atom) atom.FieldOccurrence = occurrence;
                        }

                        break;
                    }
                }
            }
        }

        return atoms;
    }

    /// <summary>
    /// Measures a phonetic guide and the word it stands over, as one thing on the line.
    /// </summary>
    /// <remarks>
    /// What ruby-probe shows Word doing, all of it read off one export:
    ///
    ///   * The guide is set at the size w:hps names — six point over twelve — and raised off the
    ///     word's baseline by w:hpsRaise, which is eleven points and comes out on the grid at
    ///     11.04.
    ///   * The pair takes as much room in the line as the wider of the two. A guide of eight
    ///     letters over one takes forty-eight points and the word is centred under it; a guide
    ///     narrower than its word takes the word's own room.
    ///   * The line grows to hold the guide, which is what the raise and the guide's own ascent
    ///     ask for above the baseline.
    /// </remarks>
    private RubyAtom RubyOf(
        RubyInline ruby, ParagraphProperties? paragraph, ResolvedRunFormat format,
        double ascent, double naturalHeight, double descent)
    {
        IReadOnlyList<RubyPiece> Pieces(List<Run> runs, double? size)
        {
            var pieces = new List<RubyPiece>();

            foreach (var run in runs)
            {
                var resolved = _styles.ResolveRun(paragraph, run.Properties);
                if (size is { } points) resolved = resolved with { FontSizePoints = points };

                var text = string.Concat(run.Content.OfType<TextInline>().Select(piece => piece.Text));
                if (text.Length == 0) continue;

                var face = _fonts.Resolve(resolved.FontFamily, resolved.Bold, resolved.Italic);

                pieces.Add(new RubyPiece(text, resolved, face,
                    TextMeasurer.Measure(face.Font, text, resolved.EffectiveFontSizePoints,
                        resolved.CharacterSpacingPoints) * resolved.ScaleFactor));
            }

            return pieces;
        }

        var word = Pieces(ruby.Base, null);
        var guide = Pieces(ruby.Guide, ruby.GuideHalfPoints is { } half and > 0 ? half / 2.0 : null);

        var wordWidth = word.Sum(piece => piece.Width);
        var guideWidth = guide.Sum(piece => piece.Width);

        var raise = ruby.RaiseHalfPoints is { } raised
            ? raised / 2.0
            : format.EffectiveFontSizePoints;

        // The line box is the word's, not the run's that wraps it: a guided word set in twelve
        // point Mincho gives the line a twelve point Mincho box however the run round it is
        // written. Word's own line spacing says so — the probe's lines are 20.4 points apart,
        // which is the guide lifted eleven over a Mincho descent rather than a Calibri one.
        var above = ascent;
        var below = descent;

        foreach (var piece in word)
        {
            var size = piece.Format.EffectiveFontSizePoints;
            var wordAscent = TextMeasurer.GetAscent(piece.Font.Font, size);

            above = Math.Max(above, wordAscent);
            below = Math.Max(below, TextMeasurer.GetNaturalLineHeight(piece.Font.Font, size) - wordAscent);
        }

        // And it has to hold the guide as well: its own box, lifted.
        if (guide.Count > 0)
        {
            var tallest = guide.Max(piece =>
                TextMeasurer.GetAscent(piece.Font.Font, piece.Format.EffectiveFontSizePoints));

            above = Math.Max(above, raise + tallest);
        }

        return new RubyAtom
        {
            Word = word,
            Guide = guide,
            Alignment = ruby.Alignment,
            Raise = raise,
            Width = Math.Max(wordWidth, guideWidth),
            WordWidth = wordWidth,
            GuideWidth = guideWidth,
            GuideLetters = guide.Sum(piece => piece.Text.Length),
            Ascent = above,
            NaturalHeight = above + below,
            Descent = below
        };
    }

    /// <summary>
    /// How big a checkbox is and where it sits, all of it measured from Word's own export.
    /// </summary>
    /// <remarks>
    /// checkbox-probe puts ten sizes to Word, from eight point to seventy-two, stated on the field
    /// and taken from the text round it, and the three numbers come straight off the drawing:
    ///
    ///   * The field is 1.15 times the size wide. Exactly that, at every size measured.
    ///   * The box is drawn in the middle of it, 2.2 points narrower — 1.1 either side.
    ///   * Its foot sits below the baseline by a little over a fifth of the size, less 1.2 points:
    ///     nothing at eight point, and a fifth of an inch at seventy-two.
    ///
    /// A box left to the text round it takes that text's size; one that states its own takes what
    /// it states, whatever the text is set in. Neither is the font's business — the same numbers
    /// come out of a twelve point box in a twelve point run and a twelve point box stated in a
    /// twenty point one.
    /// </remarks>
    private static CheckBoxAtom CheckBoxOf(CheckBox box, ResolvedRunFormat format, FontSelection selection)
    {
        var size = box.SizeHalfPoints is { } stated and > 0
            ? stated / 2.0
            : format.EffectiveFontSizePoints;

        // A box makes the line as tall as a letter of its own size would: a fourteen point box in
        // a line of twelve point text gives the line a fourteen point box, which is what Word's
        // own line spacing does with it.
        var height = TextMeasurer.GetNaturalLineHeight(selection.Font, size);
        var above = TextMeasurer.GetAscent(selection.Font, size);

        return new CheckBoxAtom
        {
            Ticked = box.Ticked,
            Width = size * 1.15,
            Side = Grid.Snap(size * 1.15 - 2.2),
            Below = Grid.Snap(size * 0.216 - 1.2),
            Format = format,

            Ascent = above,
            NaturalHeight = height,
            Descent = height - above
        };
    }

    /// <summary>
    /// Puts a note's number where its reference appears.
    /// </summary>
    /// <remarks>
    /// The number is the note's position in the document, not its id: Word stores ids in the order
    /// the notes were created and renumbers on the page. A reference to a note that is not in the
    /// part draws nothing, since there would be no note for the number to lead to.
    ///
    /// The mark is raised and shrunk by the FootnoteReference or EndnoteReference character style,
    /// so nothing is done here to make it superscript — that comes through the cascade like any
    /// other formatting.
    /// </remarks>
    private void AddNoteMark(
        List<Atom> atoms, NoteReferenceInline reference, ResolvedRunFormat format,
        FontSelection font, double ascent, double naturalHeight, double descent, ResolvedHyperlink? link)
    {
        var footnote = reference.Kind == NoteKind.Footnote;
        var notes = footnote ? _footnotes : _endnotes;
        var labels = footnote ? _footnoteLabels : _endnoteLabels;

        if (!notes.TryGetValue(reference.Id, out var note) || note.IsSeparator) return;

        if (!labels.TryGetValue(reference.Id, out var text))
        {
            text = NumberFormatter.Format(
                NextNoteNumber(footnote, reference.Id), footnote ? _footnoteFormat : _endnoteFormat);

            labels[reference.Id] = text;
            if (!footnote) _endnoteOrder.Add((reference.Id, _sectionOrdinal));
        }

        atoms.Add(new TextAtom
        {
            Text = text,
            IsSpace = false,
            Format = format,
            Font = font,
            Ascent = ascent,
            NaturalHeight = naturalHeight,
            Descent = descent,
            Link = link,
            Kerned = Kerned(format),
            FootnoteId = footnote ? reference.Id : null,
            Width = TextMeasurer.Measure(
                font.Font, text, format.EffectiveFontSizePoints,
                format.CharacterSpacingPoints, Kerned(format)) * format.ScaleFactor
        });
    }

    /// <summary>
    /// The number the next note takes, beginning again where the section says to.
    /// </summary>
    /// <remarks>
    /// Per section is settled as the marks are composed, since a mark is composed inside the
    /// section it belongs to. Per page cannot be: which page a reference falls on is not known
    /// while that page is still being filled, and the line carrying it may yet move to the next.
    /// So the first pass records where each mark landed and the second numbers from that, which is
    /// how everything else here that depends on the page is done — and converges the same way.
    /// </remarks>
    private int NextNoteNumber(bool footnote, int id)
    {
        var restart = footnote ? _footnoteRestart : _endnoteRestart;
        var counter = footnote ? _footnoteCounter : _endnoteCounter;

        var page = restart == NoteNumberRestart.EachPage ? Pagination?.PageOfNote(id) ?? 0 : 0;

        var begins = restart switch
        {
            NoteNumberRestart.EachSection => counter.Section != _sectionOrdinal,
            NoteNumberRestart.EachPage => page != counter.Page,
            _ => false
        };

        // A document numbering by page has to be laid out twice, since the first pass has no
        // pages to number from.
        if (restart == NoteNumberRestart.EachPage && Pagination is null) NeedsPagination = true;

        var next = begins ? 1 : counter.Next;
        counter = (next + 1, page, _sectionOrdinal);

        if (footnote) _footnoteCounter = counter;
        else _endnoteCounter = counter;

        return next;
    }

    /// <summary>
    /// The kerning between two characters, in points, or zero when the pair is not kerned.
    /// </summary>
    private static double KerningBetween(FontSelection font, ResolvedRunFormat format, char left, char right)
    {
        if (left == '\0' || char.IsSurrogate(left) || char.IsSurrogate(right)) return 0;

        var kerning = font.Font.GetKerning(font.Font.GetGlyphIndex(left), font.Font.GetGlyphIndex(right));
        if (kerning == 0) return 0;

        return font.Font.Metrics.ToPoints(kerning, format.EffectiveFontSizePoints) * format.ScaleFactor;
    }

    /// <summary>
    /// An equation, set and put on the line as one thing.
    /// </summary>
    /// <remarks>
    /// What comes back from the composer is text and rules at exact places, and both go onto the
    /// page as ordinary text and ordinary rules — so an equation is selectable, searchable and in
    /// the layout trace like anything else, rather than a picture of itself.
    ///
    /// It straddles the baseline rather than standing on it, which is the one way it differs from
    /// an inline picture: a fraction reaches below the line as well as above it.
    /// </remarks>
    private void AddMathAtom(List<Atom> atoms, MathInline math, ResolvedRunFormat format)
    {
        var box = new MathComposer(_fonts, _styles).Compose(math.Node, format, math.Display);
        if (box.Pieces.Count == 0 && box.Rules.Count == 0) return;

        var page = new LaidOutPage { WidthPoints = box.Width, HeightPoints = box.Height };

        foreach (var piece in box.Pieces)
        {
            var metrics = piece.Font.Font.Metrics;
            var ascent = metrics.WinAscent * piece.SizePoints / metrics.UnitsPerEm;
            var descent = metrics.WinDescent * piece.SizePoints / metrics.UnitsPerEm;

            // Rounded the way Word rounds a position: every offset in Word's own equations is a
            // whole number of three hundredths of an inch away from the line's baseline.
            var baseline = box.Ascent + MathComposer.Quantised(piece.Baseline);

            var line = new LaidOutLine
            {
                BaselineY = baseline,
                Height = ascent + descent,
                Ascent = ascent
            };

            line.Texts.Add(new PositionedText
            {
                X = piece.X,
                BaselineY = baseline,
                Text = piece.Text,
                Format = format with
                {
                    FontSizePoints = piece.SizePoints,
                    FontFamily = piece.Font.Font.FamilyName,
                    Bold = false,
                    Italic = false
                },
                Font = piece.Font,
                Width = piece.Width,
                Glyph = piece.Glyph
            });

            page.Lines.Add(line);
        }

        foreach (var rule in box.Rules)
        {
            // How thick a bar is rounds like a position does: the table asks for 0.717 of a point
            // in a sentence and 0.779 on a line of its own, and Word draws both at 0.72. Where it
            // begins and how far it runs are not rounded — Word's own are a shade wider than what
            // stands over them and land on the grid more often than not but not always, and
            // rounding them moves as many away from Word's as towards them.
            page.Rules.Add(new PositionedRule
            {
                X = rule.X,
                Y = box.Ascent + rule.Y,
                Width = rule.Width,
                Thickness = MathComposer.Quantised(rule.Thickness),
                Color = (0, 0, 0)
            });
        }

        atoms.Add(new ImageAtom
        {
            Image = null,
            Width = box.Width,
            Height = box.Height,
            Ascent = box.LineAscent,
            NaturalHeight = box.Height,
            Descent = box.LineDescent,
            Content = new DetachedFlow(page, box.Height),
            ContentLeft = 0,
            ContentTop = box.Descent
        });
    }

    /// <summary>
    /// Turns a drawing into an atom, if its picture is one we can decode.
    /// </summary>
    /// <remarks>
    /// An image whose format is unsupported or whose part is missing is dropped rather than
    /// failing the conversion, the way a word processor skips a picture it cannot render.
    /// </remarks>
    private void AddImageAtom(List<Atom> atoms, DrawingInline drawing)
    {
        var width = drawing.WidthPoints;
        var height = drawing.HeightPoints;
        if (width <= 0 || height <= 0) return;

        if (drawing.Diagram is { Count: > 0 } || drawing.Shape is not null || drawing.Chart is not null)
        {
            var composed = drawing.Chart is { } chart
                ? ComposeChart(chart, width, height)
                : drawing.Diagram is { Count: > 0 } diagram
                    ? ComposeDiagram(diagram, width, height)
                    : ComposeShape(drawing.Shape!, width, height);

            // An old-style shape with an outline asks its line for more than its own height: the
            // height rounded up to the whole point, and a point for every whole point of outline
            // past the first. What grows is the room under the shape — the shape itself stays at
            // the top of the line, which is where Word draws it — so the extra is a descent and
            // not an ascent. Vml has the measurements.
            var outline = drawing.Shape?.OutlineWholePoints ?? 0;

            var under = outline > 0 ? Math.Ceiling(height) + outline - 1 - height : 0;

            atoms.Add(new ImageAtom
            {
                Image = composed.Frame,
                Width = width,
                Height = height,
                Ascent = height,
                NaturalHeight = height + under,
                Descent = under,
                Content = composed.Content,
                ContentLeft = composed.Left,
                ContentTop = composed.Top
            });

            return;
        }

        if (drawing.RelationshipId is null) return;

        var image = DecodeImage(drawing.RelationshipId, drawing.Wash);
        if (image is null) return;

        atoms.Add(new ImageAtom
        {
            Image = image,
            Width = width,
            Height = height,
            // An inline image sits on the baseline, so the whole of it is above and it sets the
            // line's ascent.
            Ascent = height,
            // An image sits on the baseline with nothing below it. Any descent on the line comes
            // from the text beside it, which is tracked separately.
            NaturalHeight = height,
            Descent = 0
        });
    }

    /// <summary>
    /// Puts a list item's number or bullet at the front of its first line.
    /// </summary>
    /// <remarks>
    /// The label is emitted as ordinary atoms rather than as a special case, so it takes part in
    /// measurement and line breaking like anything else. A list item's hanging indent starts the
    /// first line left of the rest, which is where the label goes; the tab that follows it then
    /// carries the text to the paragraph's left indent, and that indent has to be offered as a
    /// tab stop or the tab would land on the next default one instead.
    /// </remarks>
    private void AddNumberingLabel(
        List<Atom> atoms, Paragraph paragraph, ResolvedParagraphFormat format, double defaultTab)
    {
        if (format.NumberingId is not { } numId) return;

        var label = _numbering.Advance(numId, format.NumberingLevel);
        if (string.IsNullOrEmpty(label)) return;

        var definition = _styles.Numbering.GetLevel(numId, format.NumberingLevel);

        // The label is styled by the level's own run properties over the paragraph mark's, which
        // is how a bullet gets its symbol font without affecting the item's text.
        var labelFormat = _styles.ResolveRun(paragraph.Properties, definition?.RunProperties);
        var selection = _fonts.Resolve(labelFormat.FontFamily, labelFormat.Bold, labelFormat.Italic);
        var size = labelFormat.EffectiveFontSizePoints;

        var box = labelFormat.LineBoxFontSizePoints;
        var ascent = TextMeasurer.GetAscent(selection.Font, box);
        var naturalHeight = TextMeasurer.GetNaturalLineHeight(selection.Font, box);
        var descent = naturalHeight - ascent;

        var from = atoms.Count;

        AddTextAtoms(atoms, label, labelFormat, selection, ascent, naturalHeight, descent);

        // The number is drawn on the line but is not part of its box.
        for (var i = from; i < atoms.Count; i++)
        {
            if (atoms[i] is TextAtom text) atoms[i] = text.OutsideTheLineBox();
        }

        switch (definition?.Suffix ?? NumberSuffix.Tab)
        {
            case NumberSuffix.Nothing:
                break;

            case NumberSuffix.Space:
                AddTextAtoms(atoms, " ", labelFormat, selection, ascent, naturalHeight, descent);
                break;

            default:
                // A hanging indent starts the first line left of the paragraph's own indent, and
                // the label's tab is what carries the text out to it. Stops are measured from the
                // margin, so that is where the paragraph's indent stands.
                var hanging = format.IndentFirstLinePoints < 0;

                var stops = new List<TabStop>(format.TabStops);
                if (hanging)
                {
                    stops.Add(new TabStop(
                        Units.PointsToTwips(format.IndentLeftPoints), TabAlignment.Left, TabLeader.None));
                }

                atoms.Add(new TabAtom
                {
                    Stops = stops,
                    DefaultIntervalPoints = defaultTab,
                    Format = labelFormat,
                    Font = selection,
                    Ascent = ascent,
                    NaturalHeight = naturalHeight,
                    Descent = descent
                });
                break;
        }
    }

    /// <summary>
    /// Turns a hyperlink's stored reference into a resolved target, dropping one that leads
    /// nowhere: a relationship id with no matching relationship is not a link.
    /// </summary>
    private ResolvedHyperlink? ResolveHyperlink(HyperlinkTarget? target)
    {
        if (target is null) return null;

        if (target.RelationshipId is { } id && _hyperlinks.TryGetValue(id, out var url))
            return new ResolvedHyperlink(url, null);

        return string.IsNullOrEmpty(target.Anchor) ? null : new ResolvedHyperlink(null, target.Anchor);
    }

    /// <summary>
    /// Decodes the picture behind a relationship id, once per document.
    /// </summary>
    /// <remarks>
    /// A failure is cached too, so an unreadable picture is not retried at every placement, and a
    /// picture used several times yields the same instance — which is what lets the writer embed
    /// it once.
    /// </remarks>
    private Images.ImageData? DecodeImage(string relationshipId, PictureWash? wash = null)
    {
        var key = wash is null ? relationshipId : $"{relationshipId}|{wash.Gain}|{wash.BlackLevel}";

        if (_decodedImages.TryGetValue(key, out var cached)) return cached;

        var image = _images.TryGetValue(relationshipId, out var bytes)
            ? Images.ImageReader.TryRead(bytes, (limits ?? new Packaging.PackageLimits()).MaximumImagePixels)
            : null;

        if (image is not null && wash is not null) image = Washed(image, wash);

        _decodedImages[key] = image;
        return image;
    }

    /// <summary>
    /// A picture with its colours washed out, which is what a watermark of one is.
    /// </summary>
    /// <remarks>
    /// Done to the samples rather than left to the PDF, because a PDF has no such transform: it
    /// can carry a transfer function on the graphics state, but not one that a reader is obliged
    /// to apply to an image. A picture that arrived as a JPEG is left alone — it would have to be
    /// decoded and written out again to touch it, and no watermark this has met is one.
    /// </remarks>
    private static Images.ImageData Washed(Images.ImageData image, PictureWash wash)
    {
        if (wash.IsIdentity || image.Encoding != Images.ImageEncoding.Raw || image.IsDrawing)
            return image;

        // Sixteen bits a sample is left alone too: nothing that needs washing out is that
        // precise, and the arithmetic below is written for bytes.
        if (image.BitsPerComponent != 8) return image;

        var table = new byte[256];
        for (var i = 0; i < table.Length; i++)
            table[i] = (byte)Math.Round(wash.Apply(i / 255.0) * 255);

        var data = new byte[image.Data.Length];
        for (var i = 0; i < data.Length; i++) data[i] = table[image.Data[i]];

        return image with { Data = data };
    }

    /// <summary>
    /// Splits text into word and space atoms. Spaces are separate atoms because they are both
    /// the break opportunities and the things justification stretches.
    /// </summary>
    /// <summary>
    /// The face that draws a character: the run's own where it can, and another where it cannot.
    /// </summary>
    /// <remarks>
    /// Cached, since the answer depends only on the character and the face asked for and the same
    /// question is asked of every character of every document.
    /// </remarks>
    private FontSelection FaceFor(string text, int index, ResolvedRunFormat format, FontSelection font)
    {
        int codePoint = text[index];

        if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length &&
            char.IsLowSurrogate(text[index + 1]))
        {
            codePoint = char.ConvertToUtf32(text[index], text[index + 1]);
        }
        else if (char.IsLowSurrogate(text[index]) && index > 0 && char.IsHighSurrogate(text[index - 1]))
        {
            codePoint = char.ConvertToUtf32(text[index - 1], text[index]);
        }

        // A space is drawn by nothing and measured by everything, so it stays with the run rather
        // than dragging a borrowed face into the middle of a word.
        if (codePoint == ' ') return font;

        // What is remembered is the answer, not the face that gave it: two runs of one family are
        // two selections of one font, and handing the second run the first one's would split every
        // word at the characters the first had already asked about.
        var key = (font.Font, codePoint, format.Bold, format.Italic);

        if (!_faces.TryGetValue(key, out var borrowed))
        {
            var found = _fonts.ResolveForCharacter(codePoint, font, format.Bold, format.Italic);

            _faces[key] = borrowed = found is null || ReferenceEquals(found, font) ? null : found;
        }

        return borrowed ?? font;
    }

    /// <summary>
    /// Which way each character of a paragraph runs.
    /// </summary>
    /// <remarks>
    /// Worked out over the paragraph's own text — everything its runs say, in the order they say
    /// it — because the algorithm reads what stands either side of a character and a run boundary
    /// is nothing to a reader. What is not text takes the paragraph's own direction, which is what
    /// a tab or a picture standing between two words does.
    /// </remarks>
    private static byte[] ParagraphLevels(Paragraph paragraph, ResolvedParagraphFormat format)
    {
        var text = new System.Text.StringBuilder();

        foreach (var run in paragraph.Runs)
        foreach (var inline in run.Content)
        {
            if (inline is TextInline piece) text.Append(piece.Text);
        }

        if (text.Length == 0) return [];

        // A paragraph says which way it runs; the text says only what it is made of.
        var direction = format.RightToLeft
            ? Text.Bidi.Direction.RightToLeft
            : Text.Bidi.Direction.LeftToRight;

        return Text.Bidi.Resolve(text.ToString(), direction).Levels;
    }

    private void AddTextAtoms(
        List<Atom> atoms, string text, ResolvedRunFormat format, FontSelection font,
        double ascent, double naturalHeight, double descent, ResolvedHyperlink? link = null,
        byte[]? levels = null, int offset = 0)
    {
        var kerned = Kerned(format);
        var previous = '\0';

        // Where a line may be broken, which is not the same question as where the spaces are:
        // Chinese and Japanese have none, Thai has none between its words, and English has both
        // spaces that may not be broken at and breaks where there is no space.
        var breaks = Text.LineBreaker.Opportunities(text);

        // Which face draws each character. Nearly always the run's own; where that face has no
        // glyph for a character, another that has.
        FontSelection FaceAt(int index) => FaceFor(text, index, format, font);

        byte LevelAt(int index) =>
            levels is not null && offset + index < levels.Length
                ? levels[offset + index]
                : (byte)0;

        var index = 0;
        while (index < text.Length)
        {
            var isSpace = text[index] == ' ';
            var start = index;
            var level = LevelAt(index);
            var face = FaceAt(index);

            // A word is broken where the direction changes as well as at a space: what runs one
            // way and what runs the other are drawn in different places, so they cannot be one
            // thing. It is broken where the face changes for the same reason — a run is set in
            // one font, and a character its font cannot draw is set in another.
            // The faces are compared by what they are rather than by which object they are: two
            // borrowed faces of one family are two selections of one font, and a word set in a
            // borrowed face would otherwise come apart at every character.
            // And it is broken wherever a line may be, since the line filler takes atoms whole and
            // an atom it cannot break is one it has to carry over entire.
            index++;

            while (index < text.Length && !breaks[index] && (text[index] == ' ') == isSpace &&
                   LevelAt(index) == level && Equals(FaceAt(index), face))
            {
                index++;
            }

            var slice = text[start..index];

            // A run that goes right to left keeps the order it is read in, which is the order it
            // has to be shaped in; what changes here is only the brackets, which face the way the
            // reader is going. Turning it round happens to the glyphs, on the way to the page.
            if ((level & 1) != 0) slice = Text.BidiText.Mirrored(slice, level);

            // Splitting at spaces puts the pair straddling each split into neither atom, and Word
            // kerns those like any other — a V before a space is drawn tighter to it. The pair is
            // measured here and carried on the atom that follows it, which is also what lets it be
            // taken off again when that atom turns out to open a line.
            var leadingKern = kerned
                ? KerningBetween(font, format, previous, slice[0])
                : 0;

            previous = slice[^1];

            // A borrowed face brings its own metrics: the line is as tall as what is drawn on it.
            var box = format.LineBoxFontSizePoints;

            var pieceAscent = Equals(face, font) ? ascent : TextMeasurer.GetAscent(face.Font, box);
            var pieceHeight = Equals(face, font)
                ? naturalHeight
                : TextMeasurer.GetNaturalLineHeight(face.Font, box);

            var width = TextMeasurer.Measure(
                face.Font, slice, format.EffectiveFontSizePoints,
                format.CharacterSpacingPoints, kerned) * format.ScaleFactor + leadingKern;

            // A word too wide for the box it is in comes apart between its letters, where the box
            // is a shape's rather than the page's. Nothing on a page does that — a long word
            // overruns the margin and stays whole — but a shape holds its text and Word breaks a
            // word that will not fit wherever it has to. Its own drawing of the diagram fixture
            // sets "Three" across two lines as "Thre" and "e".
            atoms.Add(new TextAtom
            {
                Text = slice,
                Level = level,
                IsSpace = isSpace,
                Format = format,
                Font = face,
                Ascent = pieceAscent,
                NaturalHeight = pieceHeight,
                Descent = pieceHeight - pieceAscent,
                Link = link,
                Kerned = kerned,
                LeadingKern = leadingKern,
                Width = width
            });
        }
    }

    // ----- internal composition types -----

    /// <summary>
    /// Where content is being laid out and how far down it has reached. Carries the paragraph
    /// spacing state too, since collapsing depends on what came immediately before.
    /// </summary>
    private sealed class Cursor
    {
        public required LayoutEngine Engine { get; init; }

        public required LaidOutDocument Document { get; init; }

        /// <summary>
        /// The section being laid out. Settable because a section break changes the page geometry
        /// part-way through a document without starting the layout over.
        /// </summary>
        public required SectionProperties Section { get; set; }

        public required LaidOutPage Page { get; set; }

        public required double Y { get; set; }

        /// <summary>The current column's left edge and measure.</summary>
        public required double Left { get; set; }

        public required double Width { get; set; }

        /// <summary>The content box the columns divide up, which is the section's own measure.</summary>
        /// <summary>
        /// Where the page sits, for a flow that is not the page's own: a header is laid out on a
        /// detached page of its own and put onto the real one afterwards, and a shape in it
        /// anchored to the page or to the margins has to be placed as though it knew that. Null
        /// for the body, which is already on the page it will be drawn on.
        /// </summary>
        public PageFrame? Frame { get; init; }

        public double SectionLeft { get; set; }

        public double SectionWidth { get; set; }

        /// <summary>Each column's offset from the content box and its width, in points.</summary>
        public IReadOnlyList<(double Left, double Width)> Columns { get; set; } = [(0, 0)];

        public int ColumnIndex { get; set; }

        /// <summary>The last column reached on this page, for deciding which gaps get a rule.</summary>
        public int MaxColumnUsed { get; set; }

        /// <summary>How far down the fullest column of this page has reached.</summary>
        public double PageMaxY { get; set; }

        /// <summary>
        /// The lines placed in the column being filled, in order. Kept so that the rules about
        /// what may not be separated can take some of them back off it again, which is why the
        /// list outlives the paragraph that put them there.
        /// </summary>
        public List<PlacedLine> ColumnLines { get; } = [];

        /// <summary>
        /// Everything placed on this page, with the column it went in and the section it belongs
        /// to. Kept so that a section's own content on its last page can be found again and set
        /// out afresh, which is what evening out its columns amounts to.
        /// </summary>
        public List<(int Column, int Section, PlacedLine Line)> PagePlaced { get; } = [];

        public required double ContentTop { get; set; }

        /// <summary>The bottom margin: where content would stop with nothing reserved.</summary>
        public required double ContentLimit { get; set; }

        /// <summary>
        /// Height reserved at the foot of the current page, which is the footnote area. It grows
        /// as footnotes are referenced and is given back when a new page starts.
        /// </summary>
        public double Reserved { get; set; }

        /// <summary>How far down content may reach on this page.</summary>
        public double ContentBottom => ContentLimit - Reserved;

        /// <summary>
        /// Called with the page being left behind, so that whatever was reserved on it can be
        /// filled in before the cursor moves on.
        /// </summary>
        /// <remarks>
        /// What it is handed along with the page is how far down each column of it the text
        /// reached, which is where notes set under the text go.
        /// </remarks>
        public Action<LaidOutPage, IReadOnlyDictionary<int, double>>? OnPageComplete { get; init; }

        /// <summary>How far down each column of this page has been filled.</summary>
        public Dictionary<int, double> ColumnBottoms { get; } = [];

        /// <summary>
        /// Where footnotes found while composing are collected instead of being placed. Set while
        /// measuring a detached flow, whose page is not the one the content will end up on.
        /// </summary>
        public List<int>? FootnoteSink { get; init; }

        /// <summary>False inside a table cell, whose height is measured before it is placed.</summary>
        /// <summary>
        /// Whether what is being laid out may break the page. Settable because a floating table
        /// borrows the flow's own place on the page and must not break it while it does.
        /// </summary>
        public required bool Paginate { get; set; }

        public ResolvedParagraphFormat? PreviousFormat { get; set; }

        public double PendingSpaceAfter { get; set; }

        /// <summary>
        /// Rectangles on the current page that text must flow around, in page coordinates.
        /// Cleared when a page breaks, since a float belongs to the page its anchor landed on.
        /// </summary>
        public List<FloatRegion> Floats { get; } = [];

        /// <summary>
        /// The widest run of free horizontal space across a vertical band, as an offset from the
        /// content box's left edge and a width. Zero width means the band is fully blocked.
        /// </summary>
        /// <summary>
        /// The free horizontal bands across a vertical strip, as offsets from the content box's
        /// left edge, in the order they stand across the page. Empty where the strip is blocked
        /// from side to side.
        /// </summary>
        /// <remarks>
        /// A line is run through all of them, which is what Word does with text beside a float
        /// that has room on either side of it. Bands narrower than a point are dropped: nothing
        /// can be set in them, and a band of nothing would only cost the line a pass.
        /// </remarks>
        public List<(double Left, double Width)> ResolveBands(double top, double height)
        {
            var free = new List<(double Left, double Width)>();
            if (Floats.Count == 0) return [(0, Width)];

            var boxLeft = Left;
            var boxRight = Left + Width;

            var blocked = Floats
                .Where(f => f.Top < top + height && f.Bottom > top)
                .SelectMany(f => f.BlockedIntervals(top, top + height))
                .Select(i => (Left: Math.Max(boxLeft, i.Left), Right: Math.Min(boxRight, i.Right)))
                .Where(interval => interval.Right > interval.Left)
                .OrderBy(interval => interval.Left)
                .ToList();

            if (blocked.Count == 0) return [(0, Width)];

            var x = boxLeft;

            foreach (var interval in blocked)
            {
                if (interval.Left - x > 1) free.Add((x - Left, interval.Left - x));
                x = Math.Max(x, interval.Right);
            }

            if (boxRight - x > 1) free.Add((x - Left, boxRight - x));

            return free;
        }

        public (double Left, double Width) ResolveBand(double top, double height)
        {
            if (Floats.Count == 0) return (0, Width);

            var boxLeft = Left;
            var boxRight = Left + Width;

            var blocked = Floats
                .Where(f => f.Top < top + height && f.Bottom > top)
                .SelectMany(f => f.BlockedIntervals(top, top + height))
                .Select(i => (Left: Math.Max(boxLeft, i.Left), Right: Math.Min(boxRight, i.Right)))
                .Where(interval => interval.Right > interval.Left)
                .OrderBy(interval => interval.Left)
                .ToList();

            if (blocked.Count == 0) return (0, Width);

            // Walk the gaps between blocked intervals and keep the widest.
            var bestLeft = boxLeft;
            var bestWidth = 0.0;
            var x = boxLeft;

            foreach (var interval in blocked)
            {
                if (interval.Left > x && interval.Left - x > bestWidth)
                {
                    bestLeft = x;
                    bestWidth = interval.Left - x;
                }

                x = Math.Max(x, interval.Right);
            }

            if (boxRight - x > bestWidth)
            {
                bestLeft = x;
                bestWidth = boxRight - x;
            }

            return (bestLeft - Left, Math.Max(0, bestWidth));
        }

        /// <summary>
        /// The nearest y below which a blocked band opens up again, or null when nothing blocks it.
        /// </summary>
        public double? NextClearY(double top, double height)
        {
            double? clear = null;

            foreach (var region in Floats)
            {
                if (region.Top >= top + height || region.Bottom <= top) continue;
                if (clear is null || region.Bottom < clear) clear = region.Bottom;
            }

            return clear;
        }

        /// <summary>
        /// True when a page break would achieve anything. Breaking an empty page just produces
        /// another empty one, and inside a cell there are no pages to break at all.
        /// </summary>
        public bool CanBreak => Paginate && (Page.Lines.Count > 0 || Page.Rectangles.Count > 0);

        /// <summary>
        /// True when moving on would achieve anything. An empty column is no more room than the
        /// one just tried, so content too tall for a column overflows rather than marching across
        /// the page looking for a column that could hold it.
        /// </summary>
        public bool CanAdvance => Paginate && Y > ContentTop + 0.001;

        /// <summary>
        /// Moves to the next column, or to the next page when this was the last of them.
        /// </summary>
        public void AdvanceColumn()
        {
            PageMaxY = Math.Max(PageMaxY, Y);

            if (ColumnIndex + 1 >= Columns.Count)
            {
                BreakPage();
                return;
            }

            // Each column keeps its own footnote area, so what one column reserved for its notes
            // is not taken out of the next — and its notes are written under it before the next
            // column's text is, which is the order Word writes them in.
            ColumnBottoms[ColumnIndex] = Math.Max(ColumnBottoms.GetValueOrDefault(ColumnIndex), Y);

            if (FootnoteSink is null) Engine.FlushColumnFootnotes(Page, ColumnIndex, Y);

            ColumnIndex++;
            MaxColumnUsed = Math.Max(MaxColumnUsed, ColumnIndex);
            Y = ContentTop;
            Reserved = 0;
            ColumnLines.Clear();
            ApplyColumn();
        }

        /// <summary>Points the cursor at its current column within the content box.</summary>
        public void ApplyColumn()
        {
            var index = Math.Clamp(ColumnIndex, 0, Columns.Count - 1);

            Left = SectionLeft + Columns[index].Left;
            Width = Columns[index].Width;
        }

        /// <summary>
        /// Settles the page being left behind: where its text sits, what is ruled between its
        /// columns, and what goes in its foot.
        /// </summary>
        /// <remarks>
        /// All three read the geometry of the section the page belongs to, so this happens before
        /// a section break changes it. Otherwise a page is aligned to the margins of the section
        /// that follows it rather than its own.
        /// </remarks>
        public void FinishPage()
        {
            PageMaxY = Math.Max(PageMaxY, Y);

            AlignPageVertically(this);
            DrawColumnSeparators(this);
            ColumnBottoms[ColumnIndex] = Math.Max(ColumnBottoms.GetValueOrDefault(ColumnIndex), Y);

            OnPageComplete?.Invoke(Page, ColumnBottoms);
        }

        /// <summary>Moves to a fresh page, leaving the one behind as it stands.</summary>
        public void StartNewPage()
        {
            Page = Engine.NewPage(Document, Section);
            Y = ContentTop;
            Reserved = 0;

            // The rest of a note divided on the page before belongs at the foot of this one, and
            // has to be given its room before anything else asks for any.
            if (FootnoteSink is null) Engine.ResumeFootnotes(this);

            ColumnIndex = 0;
            MaxColumnUsed = 0;
            PageMaxY = 0;
            ColumnLines.Clear();
            ColumnBottoms.Clear();
            PagePlaced.Clear();
            ApplyColumn();

            // A float belongs to the page its anchor landed on; it does not follow the text.
            Floats.Clear();

            // Except what is left of a floating table too tall for the page it began on, which
            // carries on at the top of this one.
            Engine.ResumeCarriedTable(this);
        }

        public void BreakPage()
        {
            FinishPage();
            StartNewPage();
        }
    }

    /// <summary>
    /// Content laid out at the origin of a detached page, ready to be translated into position.
    /// </summary>
    /// <summary>
    /// Where a detached flow will end up: the page it will be put onto, and where on it.
    /// </summary>
    private sealed record PageFrame(
        SectionProperties Section, double Left, double Top, double ContentTop, double ContentBottom);

    private sealed class DetachedFlow(LaidOutPage page, double height, List<int>? footnotes = null)
    {
        public static readonly DetachedFlow Empty =
            new(new LaidOutPage { WidthPoints = 0, HeightPoints = 0 }, 0);

        public double Height { get; } = height;

        /// <summary>
        /// How far the first line reaches above its own baseline, for placing a flow by where its
        /// words sit rather than by where its box begins.
        /// </summary>
        public double FirstAscent => page.Lines.Count > 0 ? page.Lines[0].Ascent : 0;

        /// <summary>How many lines it came to, and how wide the widest of them is.</summary>
        public int LineCount => page.Lines.Count;

        public double WidestLine => page.Lines
            .Select(line => line.Texts.Count == 0
                ? 0
                : line.Texts.Max(text => text.X + text.Width) - line.Texts.Min(text => text.X))
            .DefaultIfEmpty(0)
            .Max();

        /// <summary>
        /// Footnotes referenced by this content, which belong to the page it is placed on rather
        /// than to the detached page it was composed on.
        /// </summary>
        public IReadOnlyList<int> Footnotes { get; } = footnotes ?? [];

        /// <summary>
        /// Divides this flow at the last line boundary that fits within the given height.
        /// </summary>
        /// <remarks>
        /// Cutting between lines rather than at the height asked for is what keeps a line whole
        /// where a cell runs over a page boundary. Nothing fits until a whole line does, so a
        /// cell with one very tall line in it moves rather than splits.
        ///
        /// Whatever the content referred to goes with the part that stays, which is where the
        /// reference usually is.
        /// </remarks>
        public (DetachedFlow Fitted, DetachedFlow Remaining) SplitAt(double limit)
        {
            var cut = 0.0;

            foreach (var line in page.Lines)
            {
                var bottom = line.BaselineY - line.Ascent + line.Height;
                if (bottom <= limit + 0.001) cut = Math.Max(cut, bottom);
            }

            if (cut <= 0) return (Empty, this);
            if (cut >= Height - 0.001) return (this, Empty);

            var fitted = new LaidOutPage { WidthPoints = page.WidthPoints, HeightPoints = page.HeightPoints };
            var remaining = new LaidOutPage { WidthPoints = page.WidthPoints, HeightPoints = page.HeightPoints };

            foreach (var line in page.Lines)
            {
                var bottom = line.BaselineY - line.Ascent + line.Height;

                if (bottom <= cut + 0.001)
                {
                    fitted.Lines.Add(line);
                    continue;
                }

                var moved = new LaidOutLine
                {
                    BaselineY = line.BaselineY - cut,
                    Height = line.Height,
                    Ascent = line.Ascent,
                    ParagraphIndex = line.ParagraphIndex
                };

                foreach (var text in line.Texts) moved.Texts.Add(text.Translate(0, -cut));

                remaining.Lines.Add(moved);
            }

            foreach (var rule in page.Rules)
            {
                if (rule.Y < cut) fitted.Rules.Add(rule);
                else remaining.Rules.Add(new PositionedRule
                {
                    X = rule.X, Y = rule.Y - cut, Width = rule.Width,
                    Thickness = rule.Thickness, Color = rule.Color
                });
            }

            foreach (var rectangle in page.Rectangles)
            {
                if (rectangle.Y < cut) fitted.Rectangles.Add(rectangle);
                else remaining.Rectangles.Add(new PositionedRectangle
                {
                    X = rectangle.X, Y = rectangle.Y - cut, Width = rectangle.Width,
                    Height = rectangle.Height, Color = rectangle.Color
                });
            }

            foreach (var image in page.Images)
            {
                if (image.Y + image.Height <= cut + 0.001) fitted.Images.Add(image);
                else remaining.Images.Add(new PositionedImage
                {
                    X = image.X, Y = image.Y - cut, Width = image.Width,
                    Height = image.Height, Image = image.Image
                });
            }

            return (new DetachedFlow(fitted, cut, [.. Footnotes]),
                new DetachedFlow(remaining, Height - cut));
        }

        /// <summary>Copies this flow's content onto a real page, offset by the given origin.</summary>
        /// <summary>
        /// Lands a flow that was composed for a turned cell, turning it a quarter circle as it
        /// goes. The frame it was composed in has the line running along x and the lines stacking
        /// down y; where that frame lands on the page depends on which way the cell runs.
        /// </summary>
        /// <remarks>
        /// Measured against Word in cell-direction-probe:
        ///
        ///   * <c>btLr</c> reads from the foot of the cell upwards and stacks its lines from the
        ///     left, so the frame's x runs up the page and its y runs across it.
        ///   * <c>tbRl</c> reads from the head downwards and stacks from the right, so the frame's
        ///     x runs down the page and its y runs back across it.
        ///
        /// A rule — an underline, a strike — is drawn along the line, so a turned one is a bar
        /// standing on its end. Word's own do the same, and a rectangle says it as well as a rule.
        /// </remarks>
        public void PlaceTurnedOnto(
            LaidOutPage target, CellTextDirection direction,
            double left, double top, double right, double bottom)
        {
            var up = direction == CellTextDirection.BottomToTop;

            // Where a point of the composed frame lands: (along the line, across the lines).
            (double X, double Y) At(double along, double across) =>
                up ? (left + across, bottom - along) : (right - across, top + along);

            foreach (var line in page.Lines)
            {
                // A line of the page holds the baseline its text sits on, and a turned line's
                // sits across the cell rather than down it: the place this line stacks at.
                var across = line.Texts.Count > 0 ? line.Texts[0].BaselineY : 0;

                var moved = new LaidOutLine
                {
                    BaselineY = Grid.Snap(up ? left + across : right - across),
                    Height = line.Height,
                    Ascent = line.Ascent,
                    ParagraphIndex = line.ParagraphIndex
                };

                foreach (var text in line.Texts)
                {
                    var (x, y) = At(text.X, text.BaselineY);

                    moved.Texts.Add(new PositionedText
                    {
                        X = Grid.Snap(x),
                        BaselineY = Grid.Snap(y),
                        TurnDegrees = up ? 90 : -90,
                        Text = text.Text,
                        Format = text.Format,
                        Font = text.Font,
                        Width = text.Width,
                        WordSpacing = text.WordSpacing,
                        Glyph = text.Glyph,
                        Link = text.Link,
                        Kerned = text.Kerned,
                        RightToLeft = text.RightToLeft
                    });

                }

                target.Lines.Add(moved);
            }

            foreach (var rule in page.Rules)
            {
                // The far end of the rule and the far side of its thickness, which between them
                // give the bar whichever way round it has been turned.
                var (x1, y1) = At(rule.X, rule.Y);
                var (x2, y2) = At(rule.X + rule.Width, rule.Y + rule.Thickness);

                target.Rectangles.Add(new PositionedRectangle
                {
                    X = Math.Min(x1, x2),
                    Y = Math.Min(y1, y2),
                    Width = Math.Abs(x2 - x1),
                    Height = Math.Abs(y2 - y1),
                    Color = rule.Color
                });
            }

            foreach (var rectangle in page.Rectangles)
            {
                var (x1, y1) = At(rectangle.X, rectangle.Y);
                var (x2, y2) = At(rectangle.X + rectangle.Width, rectangle.Y + rectangle.Height);

                target.Rectangles.Add(new PositionedRectangle
                {
                    X = Math.Min(x1, x2),
                    Y = Math.Min(y1, y2),
                    Width = Math.Abs(x2 - x1),
                    Height = Math.Abs(y2 - y1),
                    Color = rectangle.Color
                });
            }
        }

        public void PlaceOnto(LaidOutPage target, double dx, double dy)
        {
            // A detached flow was laid out against an origin of its own, and against a grid drawn
            // from that origin. Landing it where it belongs puts every line back on the page's own
            // grid, which is the one Word writes on.
            //
            // Only the lines: what a flow draws is placed by the arithmetic, not by the grid, and
            // moving it by anything else takes it off Word's own position. The rule above a
            // carried footnote says so — footnote-split-probe has it within a hundredth of a point
            // of Word's when the move is exact, and a twentieth out when it is rounded.
            foreach (var line in page.Lines)
            {
                var shift = Grid.Snap(line.BaselineY + dy) - line.BaselineY;

                var moved = new LaidOutLine
                {
                    BaselineY = line.BaselineY + shift,
                    Height = line.Height,
                    Ascent = line.Ascent,
                    ParagraphIndex = line.ParagraphIndex
                };

                foreach (var text in line.Texts)
                    moved.Texts.Add(text.Translate(dx, shift));

                target.Lines.Add(moved);
            }

            foreach (var rule in page.Rules)
            {
                target.Rules.Add(new PositionedRule
                {
                    X = rule.X + dx,
                    Y = rule.Y + dy,
                    Width = rule.Width,
                    Thickness = rule.Thickness,
                    Color = rule.Color
                });
            }

            foreach (var image in page.Images)
            {
                target.Images.Add(new PositionedImage
                {
                    X = image.X + dx,
                    Y = image.Y + dy,
                    Width = image.Width,
                    Height = image.Height,
                    Image = image.Image
                });
            }

            foreach (var rectangle in page.Rectangles)
            {
                target.Rectangles.Add(new PositionedRectangle
                {
                    X = rectangle.X + dx,
                    Y = rectangle.Y + dy,
                    Width = rectangle.Width,
                    Height = rectangle.Height,
                    Color = rectangle.Color
                });
            }
        }
    }

    /// <summary>A rectangle on the page that text flows around, in page coordinates.</summary>
    /// <summary>
    /// A region text keeps clear of, in page coordinates. Usually its rectangle; a tight or
    /// through wrap carries the polygon it follows (#65), and then answers per-strip.
    /// </summary>
    /// <param name="Polygon">The wrap polygon in page points, closed implicitly.</param>
    /// <param name="Through">
    /// Whether text may enter the polygon's interior gaps — a through wrap does, a tight one
    /// takes the hull interval of each strip instead.
    /// </param>
    /// <param name="InflateLeft">The wrap distance added left of every blocked interval.</param>
    /// <param name="InflateRight">And to the right.</param>
    private readonly record struct FloatRegion(
        double Left, double Top, double Right, double Bottom,
        IReadOnlyList<(double X, double Y)>? Polygon = null, bool Through = false,
        double InflateLeft = 0, double InflateRight = 0)
    {
        /// <summary>
        /// The horizontal intervals this region blocks across a vertical strip. A rectangle
        /// blocks one; a polygon blocks where its edges cross the strip — the hull of the
        /// crossings for a tight wrap, their union for a through one, so only through lets text
        /// between two lobes.
        /// </summary>
        public List<(double Left, double Right)> BlockedIntervals(double top, double bottom)
        {
            if (Polygon is not { Count: >= 3 } polygon)
                return [(Left, Right)];

            // The polygon sampled at three heights of the strip: the crossings of each sample
            // line, paired off, are what the polygon covers there. Word quantises to lines just
            // the same, and three samples catch an edge that enters and leaves inside the strip.
            var intervals = new List<(double Left, double Right)>();

            foreach (var y in new[] { top + 0.1, (top + bottom) / 2, bottom - 0.1 })
            {
                var crossings = new List<double>();

                for (var i = 0; i < polygon.Count; i++)
                {
                    var (x0, y0) = polygon[i];
                    var (x1, y1) = polygon[(i + 1) % polygon.Count];

                    if (y0 == y1) continue;
                    if (y < Math.Min(y0, y1) || y >= Math.Max(y0, y1)) continue;

                    crossings.Add(x0 + (x1 - x0) * (y - y0) / (y1 - y0));
                }

                crossings.Sort();

                for (var i = 0; i + 1 < crossings.Count; i += 2)
                    intervals.Add((crossings[i] - InflateLeft, crossings[i + 1] + InflateRight));
            }

            if (intervals.Count == 0) return [];

            if (!Through)
                return [(intervals.Min(i => i.Left), intervals.Max(i => i.Right))];

            // The union: overlapping intervals merge, gaps between lobes stay free.
            intervals.Sort((a, b) => a.Left.CompareTo(b.Left));
            var merged = new List<(double Left, double Right)> { intervals[0] };

            foreach (var interval in intervals.Skip(1))
            {
                if (interval.Left <= merged[^1].Right + 1)
                    merged[^1] = (merged[^1].Left, Math.Max(merged[^1].Right, interval.Right));
                else
                    merged.Add(interval);
            }

            return merged;
        }
    }

    /// <summary>A cell with its resolved geometry and its measured contents.</summary>
    /// <param name="MergedBelow">
    /// True where a cell is merged with the one below it, so that its content, its shading and its
    /// bottom edge all belong to the run as a whole rather than to this row.
    /// </param>
    private sealed record PlacedCell(
        TableCell Source,
        double Left,
        double Width,
        int Column,
        int Span,
        DetachedFlow Content,
        double MarginLeft,
        double MarginRight,
        double MarginTop,
        double MarginBottom,
        CellBorders Borders,
        bool MergedBelow = false);

    /// <summary>
    /// A run of vertically merged cells while it is still open: where it began, and the content of
    /// the cell that started it, which belongs to the whole run rather than to any one row.
    /// </summary>
    /// <remarks>
    /// Word gives the rows a merged run covers the heights their own cells ask for and lets the
    /// merged content run down through them, so a three-line cell merged across three one-line
    /// rows leaves those rows a line tall each. Only what will not fit in them makes the run
    /// taller, and it makes the last row of the run taller rather than sharing itself out.
    /// </remarks>
    private sealed class OpenMerge
    {
        /// <summary>
        /// Where in the page's rectangles the run's fill goes, recorded when the run opens: it has
        /// to sit underneath the borders of every row it runs through, and those are drawn before
        /// its height is known.
        /// </summary>
        private int _shadingAt = -1;

        public OpenMerge(PlacedCell cell, LaidOutPage page, double top)
        {
            Cell = cell;
            Page = page;
            Top = top;
            Bottom = top;

            Reserve();
        }

        private PlacedCell Cell { get; set; }

        private LaidOutPage Page { get; set; }

        private double Top { get; set; }

        /// <summary>The foot of the last row the run has been carried through.</summary>
        public double Bottom { get; set; }

        /// <summary>
        /// How much height the run's content still wants of a row beginning here.
        /// </summary>
        public double Outstanding(double rowTop) =>
            Cell.Content.Height + Cell.MarginTop + Cell.MarginBottom - (rowTop - Top);

        /// <summary>Draws the run, now that the last of the rows it covers has been placed.</summary>
        public void Close(double bottom)
        {
            Bottom = bottom;
            Shade();

            var offset = VerticalOffset(Cell, bottom - Top - Cell.MarginTop - Cell.MarginBottom);

            Cell.Content.PlaceOnto(Page, Cell.Left + Cell.MarginLeft, Top + Cell.MarginTop + offset);
        }

        /// <summary>
        /// Ends the run on the page it began on and opens it again at the top of the next, holding
        /// whatever of its content that page had no room for.
        /// </summary>
        public void CarryOver(Cursor cursor)
        {
            // A run that had no room at all on the page it opened on — its first row moved whole
            // rather than dividing — leaves nothing behind to close off.
            if (Bottom > Top)
            {
                Shade();

                var (fitted, rest) = Cell.Content.SplitAt(Bottom - Top - Cell.MarginTop - Cell.MarginBottom);
                fitted.PlaceOnto(Page, Cell.Left + Cell.MarginLeft, Top + Cell.MarginTop);
                Cell = Cell with { Content = rest };

                // Word closes a run off where the page ends and opens it again at the top of the
                // next, so the merged cell is ruled at both, unlike where a run passes from one
                // row to the next within a page.
                AddEdge(Page, Cell.Borders.Bottom, Cell.Left, Bottom, Cell.Width, horizontal: true);
            }

            Page = cursor.Page;
            Top = cursor.Y;
            Bottom = cursor.Y;

            AddEdge(Page, Cell.Borders.Top, Cell.Left, Top, Cell.Width, horizontal: true);

            Reserve();
        }

        /// <summary>Reserves the place in the page where the run's fill will go.</summary>
        private void Reserve() =>
            _shadingAt = Cell.Source.ShadingPaint is null ? -1 : Page.Rectangles.Count;

        /// <summary>
        /// Fills the run in, at the place reserved for it when it opened — underneath the borders
        /// of the rows it runs through rather than over the top of them.
        /// </summary>
        private void Shade()
        {
            if (_shadingAt < 0 || Cell.Source.ShadingPaint is not { } fill) return;

            Page.Rectangles.Insert(_shadingAt, new PositionedRectangle
            {
                X = Cell.Left,
                Y = Top,
                Width = Cell.Width,
                Height = Bottom - Top,
                Color = fill
            });

            _shadingAt = -1;
        }
    }

    /// <summary>The border edges that actually apply to a cell, after resolution.</summary>
    private sealed record CellBorders(BorderEdge? Left, BorderEdge? Right, BorderEdge? Top, BorderEdge? Bottom);

    private abstract class Atom
    {
        public double Ascent { get; init; }

        public double NaturalHeight { get; init; }

        /// <summary>
        /// How far this atom reaches below the baseline. Tracked separately from the height
        /// because a line mixing text with an image needs the image's ascent and the text's
        /// descent — taking the tallest of the two whole boxes loses the descent entirely.
        /// </summary>
        public double Descent { get; init; }

        /// <summary>
        /// Which way this atom runs, as the bidirectional algorithm settled it: even for left to
        /// right, odd for right to left, and higher for each nesting. Everything on a line is put
        /// in the order these say before any of it is drawn.
        /// </summary>
        public byte Level { get; init; }

        /// <summary>
        /// Whether this atom's own box is part of the line's. A list's number is the one thing
        /// that is not: Word draws it in the paragraph mark's font, which may be a different one
        /// from the text's, and sizes the line from the text alone — the numbering fixture has an
        /// eleven point Calibri number against twelve point Times text, and its lines are the
        /// height of the Times alone. A note's mark in the same two fonts does count, so this is
        /// about the number rather than about mixing fonts.
        /// </summary>
        public bool InLineBox { get; init; } = true;
    }

    private sealed class TextAtom : Atom
    {
        public required string Text { get; init; }

        /// <summary>
        /// The footnote this atom is the reference mark for, if it is one. Carried on the atom so
        /// that the line a mark lands on is known, which is the page the footnote belongs on.
        /// </summary>
        public int? FootnoteId { get; init; }

        /// <summary>
        /// The field this atom came from, numbered in the order the fields were met. Carried for
        /// the same reason as a footnote's id: it is the line that says which page it is on.
        /// </summary>
        public int? FieldOccurrence { get; set; }

        public required bool IsSpace { get; init; }

        public required ResolvedRunFormat Format { get; init; }

        public required FontSelection Font { get; init; }

        public required double Width { get; init; }

        public ResolvedHyperlink? Link { get; init; }

        /// <summary>Whether this atom's width was measured with kerning applied.</summary>
        public bool Kerned { get; init; }

        /// <summary>
        /// How much of this atom's width is the kerning between it and the atom before it, which
        /// is nothing when it opens a line.
        /// </summary>
        public double LeadingKern { get; init; }

        /// <summary>Part of this atom, with a width of its own: what dividing a word gives.</summary>
        public TextAtom Divide(string text, double width, double leadingKern) => new()
        {
            Text = text,
            FootnoteId = FootnoteId,
            FieldOccurrence = FieldOccurrence,
            IsSpace = IsSpace,
            Format = Format,
            Font = Font,
            Link = Link,
            Level = Level,
            Kerned = false,
            Width = width,
            LeadingKern = leadingKern,
            Ascent = Ascent,
            NaturalHeight = NaturalHeight,
            Descent = Descent,
            InLineBox = InLineBox
        };

        /// <summary>The same atom, drawn on the line but taking no part in its box.</summary>
        public TextAtom OutsideTheLineBox() => new()
        {
            Text = Text,
            FootnoteId = FootnoteId,
            FieldOccurrence = FieldOccurrence,
            IsSpace = IsSpace,
            Format = Format,
            Font = Font,
            Link = Link,
            Kerned = Kerned,
            Width = Width,
            LeadingKern = LeadingKern,
            Ascent = Ascent,
            NaturalHeight = NaturalHeight,
            Descent = Descent,
            InLineBox = false
        };
    }

    /// <summary>
    /// The rule drawn above a page's footnotes. It occupies a line of its own and draws nothing
    /// horizontally, so it advances the pen by nothing.
    /// </summary>
    private sealed class SeparatorAtom : Atom
    {
        public required double Width { get; init; }

        public required double Thickness { get; init; }
    }

    private sealed class TabAtom : Atom
    {
        public required IReadOnlyList<TabStop> Stops { get; init; }

        public required double DefaultIntervalPoints { get; init; }

        /// <summary>What a leader filling this tab's gap would be drawn with.</summary>
        public required ResolvedRunFormat Format { get; init; }

        public required FontSelection Font { get; init; }
    }

    private sealed class BreakAtom : Atom
    {
        public required BreakKind Kind { get; init; }
    }

    /// <summary>
    /// An inline image. It occupies a fixed box on the line and sits on the baseline, so its
    /// height becomes the line's ascent.
    /// </summary>
    /// <summary>
    /// A phonetic guide and the word it stands over, set as one thing on the line.
    /// </summary>
    private sealed class RubyAtom : Atom
    {
        /// <summary>The word, and the guide over it, each as the pieces it is written in.</summary>
        public required IReadOnlyList<RubyPiece> Word { get; init; }

        public required IReadOnlyList<RubyPiece> Guide { get; init; }

        public required RubyAlignment Alignment { get; init; }

        /// <summary>How far the guide is raised off the word's baseline.</summary>
        public required double Raise { get; init; }

        /// <summary>How much room the pair takes on the line, which is the wider of the two.</summary>
        public required double Width { get; init; }

        public required double WordWidth { get; init; }

        public required double GuideWidth { get; init; }

        /// <summary>How many characters the guide is written in, for spreading it.</summary>
        public required int GuideLetters { get; init; }
    }

    /// <summary>One run of a phonetic guide or of the word beneath it.</summary>
    private sealed record RubyPiece(
        string Text, ResolvedRunFormat Format, FontSelection Font, double Width);

    /// <summary>
    /// The box a form is filled in by, which draws itself rather than standing for text.
    /// </summary>
    private sealed class CheckBoxAtom : Atom
    {
        public required bool Ticked { get; init; }

        /// <summary>How wide the field is, which is what the pen advances by.</summary>
        public required double Width { get; init; }

        /// <summary>The side of the box drawn inside that.</summary>
        public required double Side { get; init; }

        /// <summary>How far below the baseline the box's foot sits.</summary>
        public required double Below { get; init; }

        public required ResolvedRunFormat Format { get; init; }
    }

    private sealed class ImageAtom : Atom
    {
        /// <summary>
        /// What is drawn behind the content, or null where there is nothing to draw: an equation
        /// is text and rules and no picture at all.
        /// </summary>
        public required Images.ImageData? Image { get; init; }

        public required double Width { get; init; }

        public required double Height { get; init; }

        /// <summary>
        /// What a shape holds, already laid out, or null for a picture. It is placed onto the
        /// page as ordinary lines rather than kept inside anything, so text in a box is text on
        /// the page: selectable, searchable, and in the trace like any other.
        /// </summary>
        public DetachedFlow? Content { get; init; }

        /// <summary>Where that content sits inside the shape.</summary>
        public double ContentLeft { get; init; }

        public double ContentTop { get; init; }
    }

    private readonly record struct PlacedAtom(
        Atom Atom, double X, double Width, TabLeader Leader = TabLeader.None);

    /// <summary>
    /// A gap to be filled with a leader, in the line's own coordinates.
    /// </summary>
    private readonly record struct LeaderRun(
        double Start, double End, TabLeader Kind, ResolvedRunFormat Format, FontSelection Font);

    private sealed class Segment
    {
        public double X { get; set; }

        /// <summary>Which way this segment runs, which is the level of every atom in it.</summary>
        public byte Level { get; init; }

        public string Text { get; set; } = string.Empty;

        public required ResolvedRunFormat Format { get; init; }

        public required FontSelection Font { get; init; }

        public double Width { get; set; }

        /// <summary>
        /// The run's own box, which is what a border round it is drawn to — not the line's, as a
        /// highlight is. run-border-probe's twelve point run beside a thirty-six point one is
        /// boxed to its own thirteen and a half points where its highlight would take all
        /// forty-one of the line.
        /// </summary>
        public double Ascent { get; set; }

        public double Descent { get; set; }

        public double WordSpacing { get; init; }

        public int SpaceCount { get; set; }

        public ResolvedHyperlink? Link { get; init; }

        public bool Kerned { get; init; }
    }

    private sealed class ComposedLine
    {
        /// <summary>
        /// Whether the margin's numbering passes this line over, from the paragraph it belongs to.
        /// Kept on the line because a line outlives the paragraph that composed it: balancing a
        /// column places it again from what was recorded.
        /// </summary>
        public bool SuppressNumber { get; set; }

        /// <summary>
        /// True where the line's height was fixed by <c>w:lineRule="exact"</c>, which decides how
        /// its baseline is rounded onto the grid: see <see cref="Grid.ExactBaseline"/>.
        /// </summary>
        public bool ExactHeight { get; set; }

        public List<PlacedAtom> Items { get; } = [];

        public List<Segment> Segments { get; } = [];

        public List<(ImageAtom Atom, double X)> Images { get; } = [];

        /// <summary>The boxes a form is filled in by, and where each stands on the line.</summary>
        public List<(CheckBoxAtom Atom, double X)> Boxes { get; } = [];

        /// <summary>The guided words, and where each stands on the line.</summary>
        public List<(RubyAtom Atom, double X)> Rubies { get; } = [];

        /// <summary>Footnote separator rules on this line, as atom and x offset.</summary>
        public List<(SeparatorAtom Atom, double X)> Separators { get; } = [];

        /// <summary>Gaps this line's tabs asked to have filled with a leader.</summary>
        public List<LeaderRun> Leaders { get; } = [];

        /// <summary>
        /// Where this line's paragraph asked for a vertical rule, as offsets from the text margin.
        /// </summary>
        public List<double> Bars { get; } = [];

        public double Height { get; set; }

        public double Ascent { get; set; }

        public double IndentLeft { get; init; }

        /// <summary>The colour behind this line, from its paragraph's <c>w:shd</c>.</summary>
        public (double Red, double Green, double Blue)? Shading { get; set; }

        /// <summary>The paragraph's own indents, which are where its background begins and ends.</summary>
        public double ShadeLeft { get; set; }

        public double ShadeRight { get; set; }
    }
}
