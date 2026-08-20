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
public sealed class LayoutEngine(FontLibrary fonts, StyleResolver styles, LayoutOptions? options = null)
{
    private readonly FontLibrary _fonts = fonts;

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
                    LayoutTable(cursor, table);
                    break;
            }
        }
    }

    private void LayoutParagraph(
        Cursor cursor, Paragraph paragraph, IReadOnlyList<BlockElement> siblings, int index)
    {
        var format = _styles.ResolveParagraph(paragraph.Properties);

        var startedNewPage = false;
        if (format.PageBreakBefore && cursor.CanBreak)
        {
            cursor.BreakPage();
            startedNewPage = true;
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

        // Anchored drawings are placed before the paragraph's own text is composed, so that its
        // very first line already flows around them.
        PlaceAnchoredDrawings(cursor, paragraph);

        _pendingBookmarks.Clear();
        _pendingMarks.Clear();
        var composer = new ParagraphComposer(
            BuildAtoms(paragraph, format), format, TabSettings(), MarkMetrics(format));
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

            var band = ResolveBandForLine(cursor, composer.ProvisionalHeight);
            var line = composer.Next(band.Left, band.Width);

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

                    foreach (var mark in marks) _markPages[mark] = cursor.Page;
                }

                firstLine = false;
            }

            Place(cursor, line, index, ordinal, format.KeepNext, footnoteIds, footnotes);
            emitted++;
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
    }

    // ----- widow and orphan control -----

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
            footnoteIds, footnotes.Flows.Count, footnotes.Height,
            ordinal, paragraphIndex, keepNext);

        cursor.ColumnLines.Add(placed);
        cursor.PagePlaced.Add((cursor.ColumnIndex, _sectionOrdinal, placed));

        RecordFieldPages(cursor.Page, line);
        EmitLine(cursor.Page, line, cursor.Left, cursor.Y, paragraphIndex, TabSettings());
        CommitFootnotes(cursor, footnotes, line.Height);
        cursor.Y += line.Height;
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

    /// <summary>
    /// Finds the free horizontal band for the next line, moving down past any float that blocks
    /// the full measure.
    /// </summary>
    private static (double Left, double Width) ResolveBandForLine(Cursor cursor, double provisionalHeight)
    {
        var height = Math.Max(1, provisionalHeight);

        // A wrapTopAndBottom float, or two floats meeting in the middle, can leave no usable
        // width at all. The line then belongs below them.
        for (var guard = 0; guard < 64; guard++)
        {
            var band = cursor.ResolveBand(cursor.Y, height);
            if (band.Width > 1) return band;

            var clear = cursor.NextClearY(cursor.Y, height);
            if (clear is null || clear <= cursor.Y) return (0, cursor.Width);

            cursor.Y = clear.Value;
        }

        return (0, cursor.Width);
    }

    /// <summary>
    /// Positions the anchored drawings of a paragraph and registers the areas text must avoid.
    /// </summary>
    private void PlaceAnchoredDrawings(Cursor cursor, Paragraph paragraph)
    {
        foreach (var anchored in paragraph.Runs.SelectMany(run => run.Content).OfType<AnchoredDrawing>())
        {
            var width = anchored.WidthPoints;
            var height = anchored.HeightPoints;
            if (width <= 0 || height <= 0) continue;

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

            var x = ResolveHorizontalPosition(cursor, anchored, width);
            var y = ResolveVerticalPosition(cursor, anchored, height);

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

            // The distances are the clearance Word keeps between the picture and the text; they
            // are part of the area text has to avoid, not part of the picture.
            var left = x - Units.EmuToPoints(anchored.DistanceLeftEmu);
            var right = x + width + Units.EmuToPoints(anchored.DistanceRightEmu);

            if (anchored.Wrap == TextWrapMode.TopAndBottom)
            {
                // Nothing sits beside it, so the exclusion spans the whole measure.
                left = cursor.Left;
                right = cursor.Left + cursor.Width;
            }

            var region = new FloatRegion(
                left,
                y - Units.EmuToPoints(anchored.DistanceTopEmu),
                right,
                y + height + Units.EmuToPoints(anchored.DistanceBottomEmu));

            cursor.Floats.Add(region);

            // A float's clearance can reach back over text already on the page — the top
            // clearance of a picture anchored to a paragraph overlaps the last line of the one
            // before it. Word moves that text down; the picture stays where its anchor put it.
            if (anchored.Wrap == TextWrapMode.TopAndBottom) DisplaceOverlappedLines(cursor, region);
        }
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

        // Everything from the first overlapping line down moves by the same amount, which keeps
        // the spacing between them intact.
        for (var i = first; i < lines.Count; i++)
            lines[i] = ShiftLine(lines[i], delta);

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
            var delta = shift(line.BaselineY - line.Ascent);

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
        var columns = ComputeColumnWidths(table, cursor.Width);
        if (columns.Count == 0) return;

        var properties = table.Properties;
        var totalWidth = columns.Sum();

        // A declared indent is measured to the cell content edge, not to the table edge: the
        // first column's margin and border are absorbed into it rather than added on top. Verified
        // against Word with table-inset-probe, where a 12pt indent put content exactly 12pt from
        // the margin whether the cell declared a 12pt margin, a border, both or neither. Word
        // writes this element on every table it saves, so real documents always take this path.
        var tableLeft = cursor.Left;
        if (properties.IndentTwips is { } indent)
            tableLeft += Units.TwipsToPoints(indent) - LeadingCellInset(table, columns.Count);

        tableLeft += properties.Justification switch
        {
            Justification.Center => Math.Max(0, cursor.Width - totalWidth) / 2,
            Justification.Right => Math.Max(0, cursor.Width - totalWidth),
            _ => 0
        };

        // A table interrupts the paragraph spacing chain: its own edge is the boundary, so a
        // following paragraph has nothing to collapse against.
        cursor.Y += cursor.PendingSpaceAfter;
        cursor.PendingSpaceAfter = 0;
        cursor.PreviousFormat = null;

        // Merged runs open here and close some rows further down, so they outlive any one row.
        var merges = new Dictionary<int, OpenMerge>();

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var placed = MeasureRow(table, row, rowIndex, columns, tableLeft);
            if (placed.Count == 0) continue;

            // A row that ends a merged run has to be tall enough for whatever of that run's
            // content the rows above it did not account for.
            double HeightOf(List<PlacedCell> cells) =>
                ComputeRowHeight(row, cells, PendingMergeHeight(merges, cells, cursor.Y));

            var rowHeight = HeightOf(placed);

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
                    PlaceRow(cursor, fitted, cursor.Y, fittedHeight);
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

                rowHeight = HeightOf(placed);
                if (rowFootnotes.Flows.Count > 0) rowFootnotes = PrepareFootnotes(cursor, rowFootnoteIds);
            }

            if (!placedEverything) PlaceRow(cursor, placed, cursor.Y, rowHeight);

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
    }

    /// <summary>
    /// Draws a row: its shading, then its cells' contents, then its borders on top — a border
    /// sits on the cell edge and would otherwise be half-covered by the neighbouring cell's fill.
    /// </summary>
    private static void PlaceRow(Cursor cursor, List<PlacedCell> placed, double top, double height)
    {
        // A cell merged with the row below has neither fill nor content of its own here: both
        // belong to the run, and are drawn when it closes.
        foreach (var cell in placed)
        {
            if (cell.MergedBelow || cell.Source.ShadingColorHex is not { } fill) continue;

            cursor.Page.Rectangles.Add(new PositionedRectangle
            {
                X = cell.Left,
                Y = top,
                Width = cell.Width,
                Height = height,
                Color = ParseHexColor(fill)
            });
        }

        foreach (var cell in placed)
        {
            if (cell.MergedBelow) continue;

            var offset = VerticalOffset(cell, height - cell.MarginTop - cell.MarginBottom);

            cell.Content.PlaceOnto(cursor.Page, cell.Left + cell.MarginLeft, top + cell.MarginTop + offset);
        }

        DrawRowBorders(cursor.Page, placed, top, height);
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

    /// <summary>Lays out each cell of a row into its own detached page and records its geometry.</summary>
    private List<PlacedCell> MeasureRow(
        Table table, TableRow row, int rowIndex, List<double> columns, double tableLeft)
    {
        var properties = table.Properties;
        var placed = new List<PlacedCell>(row.Cells.Count);

        var x = tableLeft;
        var column = 0;

        foreach (var cell in row.Cells)
        {
            if (column >= columns.Count) break;

            var span = Math.Min(cell.GridSpan, columns.Count - column);

            var width = 0.0;
            for (var i = 0; i < span; i++) width += columns[column + i];

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
                : MeasureBlocks(cell.Content, Math.Max(1, width - marginLeft - marginRight));

            Fields.Cells = outer;

            placed.Add(new PlacedCell(cell, x, width, column, span, content,
                marginLeft, marginRight, marginTop, marginBottom, borders,
                MergedBelow(table, rowIndex, column)));

            x += width;
            column += span;
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
    private static void DrawRowBorders(LaidOutPage page, List<PlacedCell> placed, double top, double height)
    {
        foreach (var cell in placed)
        {
            AddEdge(page, cell.Borders.Top, cell.Left, top, cell.Width, horizontal: true);

            // No line between a merged cell and the one below it: that is what makes the run read
            // as one tall cell. Word's own export draws the inside rule across every column but
            // the merged one.
            if (!cell.MergedBelow)
                AddEdge(page, cell.Borders.Bottom, cell.Left, top + height, cell.Width, horizontal: true);

            AddEdge(page, cell.Borders.Left, cell.Left, top, height, horizontal: false);
            AddEdge(page, cell.Borders.Right, cell.Left + cell.Width, top, height, horizontal: false);
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
    private List<double> ComputeColumnWidths(Table table, double availableWidth)
    {
        // Word ignores the declared grid entirely when a table is left on autofit, which is its
        // default. Measured: a table given an equal-width grid produced exactly the same columns
        // as the same table with no grid at all.
        if (table.Properties.FixedLayout != true && table.Rows.Count > 0)
            return ComputeAutofitColumnWidths(table, availableWidth);

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
    /// and the width it would like, which is its content unwrapped. If every column can have what
    /// it wants the table is only as wide as its contents; otherwise the columns start at their
    /// minimums and share out what is left in proportion to how much more each one asked for.
    ///
    /// This is an approximation. It reproduces the two behaviours that were measured directly —
    /// content-width columns when the table fits, and a table filling the text area exactly when
    /// it does not — but Word's own algorithm is undocumented and this does not match it to the
    /// fraction of a point that the paragraph-level rules do. See the table-autofit-probe fixture
    /// for how far apart they are.
    /// </remarks>
    private List<double> ComputeAutofitColumnWidths(Table table, double availableWidth)
    {
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

                min += padding;
                max += padding;

                if (span == 1)
                {
                    minimums[column] = Math.Max(minimums[column], min);
                    maximums[column] = Math.Max(maximums[column], max);
                }
                else
                {
                    // A spanning cell constrains its columns only as a group: it is satisfied as
                    // long as they add up, so it is spread evenly and only where they fall short.
                    SpreadAcrossSpan(minimums, column, span, min);
                    SpreadAcrossSpan(maximums, column, span, max);
                }

                column += span;
            }
        }

        for (var i = 0; i < columnCount; i++)
            maximums[i] = Math.Max(maximums[i], minimums[i]);

        var totalMax = maximums.Sum();

        // Everything fits: each column is exactly as wide as its content.
        if (totalMax <= availableWidth) return [.. maximums];

        var totalMin = minimums.Sum();

        // Not even the minimums fit; scale them down together rather than overflow the page.
        if (totalMin >= availableWidth)
        {
            var scale = totalMin > 0 ? availableWidth / totalMin : 0;
            return [.. minimums.Select(m => m * scale)];
        }

        // Start from the minimums and share the remainder in proportion to what each column
        // still wants.
        var slack = availableWidth - totalMin;
        var demand = totalMax - totalMin;

        return [.. Enumerable.Range(0, columnCount)
            .Select(i => minimums[i] + slack * (maximums[i] - minimums[i]) / demand)];
    }

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
                    var widths = ComputeAutofitColumnWidths(nested, double.MaxValue / 4);
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
        var plan = ChartComposer.Arrange(chart, width, height, MeasureLabel, LabelBox, WrapLabel);

        var frame = new Images.ImageData(1, 1, [],
            Images.ImageEncoding.Raw, Images.ImageColorSpace.Rgb)
        {
            Drawing = ChartComposer.Draw(chart, plan, width, height, _styles.Theme)
        };

        var page = new LaidOutPage { WidthPoints = width, HeightPoints = height };

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
    /// Lays out what a shape holds, which is measured like anything else but breaks a word that
    /// will not fit rather than letting it overrun the shape.
    /// </summary>
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

        document.Pages.Add(page);
        return page;
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

    private static void EmitLine(
        LaidOutPage page, ComposedLine line, double contentLeft, double top, int paragraphIndex,
        TabOptions tabs)
    {
        var baselineY = top + line.Ascent;

        var laidOut = new LaidOutLine
        {
            BaselineY = baselineY,
            Height = line.Height,
            Ascent = line.Ascent,
            ParagraphIndex = paragraphIndex
        };

        foreach (var segment in line.Segments)
        {
            if (segment.Text.Length == 0) continue;

            var text = new PositionedText
            {
                X = contentLeft + segment.X,
                BaselineY = baselineY - segment.Format.BaselineShiftPoints,
                Text = segment.Text,
                Format = segment.Format,
                Font = segment.Font,
                Width = segment.Width,
                WordSpacing = segment.WordSpacing,
                Link = segment.Link,
                Kerned = segment.Kerned,
                RightToLeft = (segment.Level & 1) != 0
            };

            laidOut.Texts.Add(text);
            AddDecorations(page, text);
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

        foreach (var (image, x) in line.Images)
        {
            // The image rests on the baseline, so its top edge is its own height above it.
            page.Images.Add(new PositionedImage
            {
                X = contentLeft + x,
                Y = baselineY - image.Height,
                Width = image.Width,
                Height = image.Height,
                Image = image.Image
            });

            // A shape's text goes down after its frame, so the frame is under it.
            image.Content?.PlaceOnto(page,
                contentLeft + x + image.ContentLeft,
                baselineY - image.Height + image.ContentTop);
        }

        page.Lines.Add(laidOut);
    }

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
            BaselineY = baselineY - leader.Format.BaselineShiftPoints,
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
        (double Ascent, double Height) markMetrics)
    {
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
        /// A height to resolve the float band with, before the line's real height is known. The
        /// tallest thing in the paragraph is a safe over-estimate: it can only make the band more
        /// conservative, never place a line where it does not fit.
        /// </summary>
        public double ProvisionalHeight { get; } =
            atoms.Count == 0 ? 0 : atoms.Max(atom => atom.NaturalHeight);

        public ComposedLine Next(double bandLeft, double bandWidth)
        {
            var indentLeft = format.IndentLeftPoints +
                             (_isFirstLine ? Math.Max(0, format.IndentFirstLinePoints) : 0);

            // A hanging indent pulls the first line left of the others, so it applies to the
            // first line as a negative offset rather than to the rest as a positive one.
            if (_isFirstLine && format.IndentFirstLinePoints < 0)
                indentLeft = format.IndentLeftPoints + format.IndentFirstLinePoints;

            // The line sits in whichever is the tighter of the indents and the free band.
            var left = Math.Max(indentLeft, bandLeft);
            var right = Math.Min(bandLeft + bandWidth, bandLeft + bandWidth) - format.IndentRightPoints;
            var available = Math.Max(1, right - left);

            var line = new ComposedLine
            {
                IndentLeft = left
            };

            _forceBreakOnNextLine = false;
            _forceColumnBreakOnNextLine = false;

            var consumed = FillLine(
                atoms, _index, available, line, _tabs,
                out var hardBreak, out var pageBreak, out var columnBreak);
            _index += consumed;
            _producedAny = true;

            var isLastLine = _index >= atoms.Count;
            FinishLine(line, format, left, available, isLastLine || hardBreak);

            if (pageBreak) _forceBreakOnNextLine = true;
            if (columnBreak) _forceColumnBreakOnNextLine = true;
            _isFirstLine = false;

            // An empty paragraph has no atoms but still takes up a line, sized by its mark.
            if (line.Segments.Count == 0) ApplyEmptyLineMetrics(line, format, _markMetrics);

            // Nothing was consumed and nothing remains: the one pass an empty paragraph gets.
            if (consumed == 0 && _index >= atoms.Count) _index = atoms.Count;

            return line;
        }
    }

    /// <summary>
    /// Greedily packs atoms onto one line. Trailing spaces are allowed to overflow the measure,
    /// which is what Word does — a line ending in a space does not wrap because of it.
    /// </summary>
    private static int FillLine(
        List<Atom> atoms, int start, double available, ComposedLine line, TabOptions tabs,
        out bool hardBreak, out bool pageBreak, out bool columnBreak)
    {
        hardBreak = false;
        pageBreak = false;
        columnBreak = false;

        var x = 0.0;
        var index = start;
        var placedAnything = false;
        PendingTab? pending = null;

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

            // Spaces at the end of a line hang past the margin rather than forcing a wrap.
            if (!textAtom.IsSpace && placedAnything && !beyondMargin &&
                x + width > available + 0.001)
            {
                break;
            }

            line.Items.Add(new PlacedAtom(atom, x, width));
            x += width;
            index++;
            placedAnything = true;

            // A single word longer than the measure has to go somewhere; it overflows rather
            // than looping forever. Breaking inside a word would need hyphenation rules.
            if (!textAtom.IsSpace && x > available && line.Items.Count == 1) break;
        }

        ClosePendingTab(line, ref pending, x, tabs);
        return index - start;
    }

    /// <summary>
    /// Converts the placed atoms into drawable segments, applying alignment and merging adjacent
    /// atoms that share formatting so the content stream carries one show-text per run rather
    /// than one per word.
    /// </summary>
    private static void FinishLine(
        ComposedLine line, ResolvedParagraphFormat format, double indentLeft, double available, bool isLastLine)
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

        Segment? current = null;
        var pen = 0.0;

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

            if (item.Atom is ImageAtom image)
            {
                line.Images.Add((image, indentLeft + offset + pen));
                pen += image.Width;
                current = null;

                maxImageAscent = Math.Max(maxImageAscent, image.Ascent);
                continue;
            }

            var textAtom = (TextAtom)item.Atom;
            var extra = textAtom.IsSpace ? wordSpacing : 0;

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
                    WordSpacing = wordSpacing,
                    SpaceCount = textAtom.IsSpace ? 1 : 0,
                    Link = textAtom.Link,
                    Kerned = textAtom.Kerned
                };

                line.Segments.Add(current);
            }

            pen += width + extra;

            if (!textAtom.InLineBox) continue;

            maxTextAscent = Math.Max(maxTextAscent, textAtom.Ascent);
            maxTextDescent = Math.Max(maxTextDescent, textAtom.Descent);
            maxTextNatural = Math.Max(maxTextNatural, textAtom.NaturalHeight);
        }

        // A bar stop is not somewhere text lands: it asks for a rule down every line of the
        // paragraph, whether or not the line holds a tab at all.
        foreach (var stop in format.TabStops)
        {
            if (stop.Alignment == TabAlignment.Bar)
                line.Bars.Add(Units.TwipsToPoints(stop.PositionTwips));
        }

        var ascent = Math.Max(maxTextAscent, maxImageAscent);

        // The line box is the tallest ascent over the deepest descent, which is not the same as
        // the tallest of the runs' own boxes: a line of twelve point Times with an eleven point
        // Calibri mark at the end of it takes the Times ascent and the Calibri descent, and is
        // deeper than either font would make it alone. Word measured a line that way in every
        // fixture here that mixes two fonts on one line.
        var natural = Math.Max(maxTextAscent + maxTextDescent, maxImageAscent + maxTextDescent);

        // Nothing about a run's own box is lost by that: a single-font line is the same either
        // way, since one run's ascent and descent are its natural height.
        natural = Math.Max(natural, maxTextNatural);

        ApplyLineMetrics(line, format, ascent, natural);
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

        ApplyLineMetrics(line, format, mark.Ascent, mark.Height);
    }

    private static void ApplyLineMetrics(
        ComposedLine line, ResolvedParagraphFormat format, double maxAscent, double naturalHeight)
    {
        if (naturalHeight <= 0) return;

        switch (format.LineRule)
        {
            case LineSpacingRule.Exact:
                line.Height = format.LineSpacingPoints;

                // With an exact rule the baseline sits proportionally where it would naturally,
                // so that text does not drift to the top of a tightened line.
                line.Ascent = naturalHeight > 0 ? line.Height * (maxAscent / naturalHeight) : line.Height;
                break;

            case LineSpacingRule.AtLeast:
                line.Height = Math.Max(naturalHeight, format.LineSpacingPoints);
                line.Ascent = maxAscent;
                break;

            default:
                line.Height = naturalHeight * format.LineSpacingMultiple;

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

            atoms.Add(new ImageAtom
            {
                Image = composed.Frame,
                Width = width,
                Height = height,
                Ascent = height,
                NaturalHeight = height,
                Descent = 0,
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
            ? Images.ImageReader.TryRead(bytes)
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
            if (_breakInsideWords is { } measure && width > measure && slice.Length > 1 && !isSpace)
            {
                foreach (var rune in slice.EnumerateRunes())
                {
                    var letter = rune.ToString();

                    atoms.Add(new TextAtom
                    {
                        Text = letter,
                        Level = level,
                        IsSpace = false,
                        Format = format,
                        Font = face,
                        Ascent = pieceAscent,
                        NaturalHeight = pieceHeight,
                        Descent = pieceHeight - pieceAscent,
                        Link = link,
                        Kerned = false,
                        Width = TextMeasurer.Measure(
                            face.Font, letter, format.EffectiveFontSizePoints,
                            format.CharacterSpacingPoints, applyKerning: false) * format.ScaleFactor
                    });
                }

                continue;
            }

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
        /// <param name="textBottoms">
        /// How far down each column of the page its text reached, which is where notes set under
        /// the text go.
        /// </param>
        public Action<LaidOutPage, IReadOnlyDictionary<int, double>>? OnPageComplete { get; init; }

        /// <summary>How far down each column of this page has been filled.</summary>
        public Dictionary<int, double> ColumnBottoms { get; } = [];

        /// <summary>
        /// Where footnotes found while composing are collected instead of being placed. Set while
        /// measuring a detached flow, whose page is not the one the content will end up on.
        /// </summary>
        public List<int>? FootnoteSink { get; init; }

        /// <summary>False inside a table cell, whose height is measured before it is placed.</summary>
        public required bool Paginate { get; init; }

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
        public (double Left, double Width) ResolveBand(double top, double height)
        {
            if (Floats.Count == 0) return (0, Width);

            var boxLeft = Left;
            var boxRight = Left + Width;

            var blocked = Floats
                .Where(f => f.Top < top + height && f.Bottom > top)
                .Select(f => (Left: Math.Max(boxLeft, f.Left), Right: Math.Min(boxRight, f.Right)))
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
        public void PlaceOnto(LaidOutPage target, double dx, double dy)
        {
            foreach (var line in page.Lines)
            {
                var moved = new LaidOutLine
                {
                    BaselineY = line.BaselineY + dy,
                    Height = line.Height,
                    Ascent = line.Ascent,
                    ParagraphIndex = line.ParagraphIndex
                };

                foreach (var text in line.Texts)
                    moved.Texts.Add(text.Translate(dx, dy));

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
    private readonly record struct FloatRegion(double Left, double Top, double Right, double Bottom);

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
            _shadingAt = Cell.Source.ShadingColorHex is null ? -1 : Page.Rectangles.Count;

        /// <summary>
        /// Fills the run in, at the place reserved for it when it opened — underneath the borders
        /// of the rows it runs through rather than over the top of them.
        /// </summary>
        private void Shade()
        {
            if (_shadingAt < 0 || Cell.Source.ShadingColorHex is not { } fill) return;

            Page.Rectangles.Insert(_shadingAt, new PositionedRectangle
            {
                X = Cell.Left,
                Y = Top,
                Width = Cell.Width,
                Height = Bottom - Top,
                Color = ParseHexColor(fill)
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
    private sealed class ImageAtom : Atom
    {
        public required Images.ImageData Image { get; init; }

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

        public double WordSpacing { get; init; }

        public int SpaceCount { get; set; }

        public ResolvedHyperlink? Link { get; init; }

        public bool Kerned { get; init; }
    }

    private sealed class ComposedLine
    {
        public List<PlacedAtom> Items { get; } = [];

        public List<Segment> Segments { get; } = [];

        public List<(ImageAtom Atom, double X)> Images { get; } = [];

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

    }
}
