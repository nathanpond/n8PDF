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
    private readonly Dictionary<int, string> _endnoteLabels = [];
    private NumberFormat _footnoteFormat = NumberFormat.Decimal;
    private NumberFormat _endnoteFormat = NumberFormat.LowerRoman;

    // Endnote ids in the order their references appeared, which is the order they are written out
    // in at the end of the document.
    private readonly List<int> _endnoteOrder = [];

    private readonly Dictionary<int, DetachedFlow> _measuredFootnotes = [];

    // Footnotes waiting to be written into the foot of the page being filled.
    private readonly List<DetachedFlow> _pageFootnotes = [];
    private DetachedFlow? _separatorFlow;
    private bool _separatorMeasured;

    // Where the footnote area sits, fixed for the document by its section.
    private double _footnoteLeft;
    private double _footnoteWidth;
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

    // Which page is being laid out, for fields that depend on it. Zero means the body, where a
    // page number is not yet known; headers and footers set it before they run.
    private int _currentPage;
    private int _totalPages;

    public LaidOutDocument Layout(WordDocument document)
    {
        _images = document.Images;
        _hyperlinks = document.Hyperlinks;
        _footnotes = document.Footnotes;
        _endnotes = document.Endnotes;
        _decimalSymbol = string.IsNullOrEmpty(document.DecimalSymbol) ? "." : document.DecimalSymbol;
        _footnoteFormat = document.FootnoteNumberFormat;
        _endnoteFormat = document.EndnoteNumberFormat;
        _footnoteLabels.Clear();
        _endnoteLabels.Clear();
        _endnoteOrder.Clear();
        _measuredFootnotes.Clear();
        _pageFootnotes.Clear();
        _separatorFlow = null;
        _separatorMeasured = false;
        _currentNoteLabel = null;
        _decodedImages.Clear();
        _numbering = new NumberingCounter(_styles.Numbering);

        var sections = SplitIntoSections(document);
        var section = sections[0].Section;

        var result = new LaidOutDocument { Section = document.Section };
        _result = result;
        _pagesInSection = 0;

        var contentTop = Units.TwipsToPoints(section.MarginTopTwips);

        _footnoteLeft = Units.TwipsToPoints(section.MarginLeftTwips + section.GutterTwips);
        _footnoteWidth = section.ContentWidthPoints;
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
        }

        // Endnotes follow the body in ordinary flow, so they are laid out through the same cursor
        // and paginate like anything else.
        LayoutEndnotes(cursor);

        // The final paragraph's space-after still occupies the page even though nothing follows
        // it, which matters for how much content a page is considered to hold.
        cursor.Y += cursor.PendingSpaceAfter;

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

        // Pages are counted from the start of each section, and the count has to be reset before
        // the section's first page is made rather than after it.
        _pagesInSection = 0;

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

        // Footnotes sit under the whole measure rather than under the column that refers to them,
        // which is not what Word does in a multi-column section.
        _footnoteLeft = cursor.SectionLeft;
        _footnoteWidth = cursor.SectionWidth;
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
        var composer = new ParagraphComposer(
            BuildAtoms(paragraph, format), format, TabSettings(), MarkMetrics(format));
        var bookmarks = _pendingBookmarks.ToList();
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
                ? PrepareFootnotes(footnoteIds)
                : ([], 0);

            // A line that does not fit moves to the next column, or off the page when it was in
            // the last of them — and may take the lines above it along, so that a paragraph is
            // never split with only one of its lines on either side of the break.
            if (cursor.Paginate && cursor.Y + line.Height > cursor.ContentBottom - footnotes.Height && cursor.CanAdvance)
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
                if (footnotes.Flows.Count > 0) footnotes = PrepareFootnotes(footnoteIds);
            }

            if (firstLine && bookmarks.Count > 0 && _result is not null)
            {
                // A detached flow composes onto a scratch page that is not part of the document
                // yet, so its index is unknown here. Rather than record a destination that would
                // point at the wrong place, the bookmark is left unrecorded and the link that
                // wanted it simply does not become clickable.
                var pageIndex = _result.Pages.IndexOf(cursor.Page);
                if (pageIndex >= 0)
                {
                    foreach (var name in bookmarks)
                        _result.Bookmarks[name] = new BookmarkDestination(pageIndex, cursor.Left, cursor.Y);
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
        IReadOnlyList<int> footnoteIds, (List<DetachedFlow> Flows, double Height) footnotes)
    {
        cursor.ColumnLines.Add(new PlacedLine(
            line, cursor.Y,
            cursor.Page.Lines.Count, cursor.Page.Rules.Count, cursor.Page.Images.Count,
            footnoteIds, footnotes.Flows.Count, footnotes.Height,
            ordinal, paragraphIndex, keepNext));

        EmitLine(cursor.Page, line, cursor.Left, cursor.Y, paragraphIndex, TabSettings());
        CommitFootnotes(cursor, footnotes);
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
        var size = mark.EffectiveFontSizePoints;

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
            _pageFootnotes.RemoveRange(_pageFootnotes.Count - flows, flows);
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
                ? PrepareFootnotes(line.FootnoteIds)
                : ([], 0);

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
            if (anchored.RelationshipId is null) continue;

            var image = DecodeImage(anchored.RelationshipId);
            if (image is null) continue;

            var width = anchored.WidthPoints;
            var height = anchored.HeightPoints;
            if (width <= 0 || height <= 0) continue;

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
        var pageWidth = cursor.Section.PageWidthPoints;

        var (origin, available) = anchored.HorizontalFrom switch
        {
            HorizontalAnchor.Page => (0.0, pageWidth),
            HorizontalAnchor.LeftMargin => (0.0, cursor.Left),
            HorizontalAnchor.RightMargin => (cursor.Left + cursor.Width, pageWidth - cursor.Left - cursor.Width),
            _ => (cursor.Left, cursor.Width)
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
        var pageHeight = cursor.Section.PageHeightPoints;

        var (origin, available) = anchored.VerticalFrom switch
        {
            VerticalAnchor.Page => (0.0, pageHeight),
            VerticalAnchor.Margin or VerticalAnchor.TopMargin =>
                (cursor.ContentTop, cursor.ContentBottom - cursor.ContentTop),
            VerticalAnchor.BottomMargin => (cursor.ContentBottom, pageHeight - cursor.ContentBottom),
            // Paragraph and line are both relative to where the text has reached.
            _ => (cursor.Y, cursor.ContentBottom - cursor.Y)
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
                var flow = MeasureBlocks(header.Body, width);
                flow.PlaceOnto(page, left, Units.TwipsToPoints(section.HeaderDistanceTwips));
            }

            if (Resolve(document, section.FooterReferences, kind) is { } footer)
            {
                // The footer's distance is measured from the bottom of the page to its own
                // bottom, so its top depends on how tall it turned out to be.
                var flow = MeasureBlocks(footer.Body, width);
                var bottom = section.PageHeightPoints - Units.TwipsToPoints(section.FooterDistanceTwips);
                flow.PlaceOnto(page, left, bottom - flow.Height);
            }
        }

        _currentPage = 0;
    }

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

    /// <summary>
    /// Measures the footnotes some content refers to and reports how much more of the page they
    /// need, so the content can be fitted against what is left rather than against the whole page.
    /// </summary>
    private (List<DetachedFlow> Flows, double Height) PrepareFootnotes(IEnumerable<int> ids)
    {
        var flows = new List<DetachedFlow>();
        var height = 0.0;

        foreach (var id in ids)
        {
            if (!_footnotes.TryGetValue(id, out var footnote) || footnote.IsSeparator) continue;

            var flow = MeasureFootnote(id, footnote);
            flows.Add(flow);
            height += flow.Height;
        }

        // The separator is paid for once per page, by whichever note reaches it first.
        if (flows.Count > 0 && _pageFootnotes.Count == 0)
            height += SeparatorFlow()?.Height ?? 0;

        return (flows, height);
    }

    /// <summary>Adds prepared footnotes to the page being filled and takes their space out of it.</summary>
    private void CommitFootnotes(Cursor cursor, (List<DetachedFlow> Flows, double Height) prepared)
    {
        if (prepared.Flows.Count == 0) return;

        _pageFootnotes.AddRange(prepared.Flows);
        cursor.Reserved += prepared.Height;
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
    /// </remarks>
    private void FlushFootnotes(LaidOutPage page)
    {
        if (_pageFootnotes.Count == 0) return;

        var height = 0.0;
        foreach (var flow in _pageFootnotes) height += flow.Height;

        var y = _footnoteBottom - height;

        if (SeparatorFlow() is { } separator)
            separator.PlaceOnto(page, _footnoteLeft, y - separator.Height);

        foreach (var flow in _pageFootnotes)
        {
            flow.PlaceOnto(page, _footnoteLeft, y);
            y += flow.Height;
        }

        _pageFootnotes.Clear();
    }

    /// <summary>
    /// Lays out one footnote's body, once. A note is referenced from one place and appears once,
    /// so measuring it again would only repeat the work.
    /// </summary>
    private DetachedFlow MeasureFootnote(int id, Note footnote)
    {
        if (_measuredFootnotes.TryGetValue(id, out var cached)) return cached;

        // The note's own number opens its text, and it is the number the reference was given.
        var previous = _currentNoteLabel;
        _currentNoteLabel = _footnoteLabels.GetValueOrDefault(id);

        var flow = MeasureBlocks(footnote.Body, _footnoteWidth);

        _currentNoteLabel = previous;
        _measuredFootnotes[id] = flow;
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
    private void LayoutEndnotes(Cursor cursor)
    {
        if (_endnoteOrder.Count == 0) return;

        if (_endnotes.Values.FirstOrDefault(n => n.Type == "separator") is { } separator)
            LayoutBlocks(cursor, separator.Body);

        foreach (var id in _endnoteOrder)
        {
            if (!_endnotes.TryGetValue(id, out var note)) continue;

            _currentNoteLabel = _endnoteLabels.GetValueOrDefault(id);
            LayoutBlocks(cursor, note.Body);
        }

        _currentNoteLabel = null;
    }

    /// <summary>
    /// The separator's own content, or null when the document has none. Measured once: it is the
    /// same on every page.
    /// </summary>
    private DetachedFlow? SeparatorFlow()
    {
        if (_separatorMeasured) return _separatorFlow;

        _separatorMeasured = true;

        var separator = _footnotes.Values.FirstOrDefault(n => n.Type == "separator");
        if (separator is not null) _separatorFlow = MeasureBlocks(separator.Body, _footnoteWidth);

        return _separatorFlow;
    }

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
    /// Produces the text a field should display, evaluating the ones that depend on the page and
    /// falling back to whatever Word last computed for the rest.
    /// </summary>
    private string ResolveField(FieldInline field) => field.Keyword switch
    {
        "PAGE" when _currentPage > 0 => _currentPage.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "NUMPAGES" when _totalPages > 0 => _totalPages.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => field.CachedText
    };

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
                ? PrepareFootnotes(rowFootnoteIds)
                : ([], 0);

            // A row too tall for what is left of the page is broken across the two unless it says
            // it may not be, and what will not fit follows on the next page as a row of its own —
            // bordered like one, which is how Word draws it. A row taller than a whole page is
            // broken again and again, which is why splitting counts as progress on a fresh page
            // where moving the row would not.
            var placedEverything = false;

            while (cursor.Paginate && cursor.Y + rowHeight > cursor.ContentBottom - rowFootnotes.Height)
            {
                if (!row.CantSplit &&
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
                if (rowFootnotes.Flows.Count > 0) rowFootnotes = PrepareFootnotes(rowFootnoteIds);
            }

            if (!placedEverything) PlaceRow(cursor, placed, cursor.Y, rowHeight);

            CommitFootnotes(cursor, rowFootnotes);
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
            if (cell.MergedBelow || cell.Source.ShadingFill is not { } fill) continue;

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
                cell.MarginLeftTwips ?? properties.CellMarginLeftTwips, borders.Left);
            var marginRight = CellInset(
                cell.MarginRightTwips ?? properties.CellMarginRightTwips, borders.Right);
            // The top is not the same: there Word puts the content a whole border below the
            // edge rather than half of one, which the same probe shows at every weight.
            var marginTop = Units.TwipsToPoints(cell.MarginTopTwips ?? properties.CellMarginTopTwips)
                            + BorderWidth(borders.Top);
            // The bottom border is deliberately not counted here. Adjacent rows share an edge —
            // one row's bottom border is the next row's top border — so charging it to both
            // makes every row a border-width too tall and the error accumulates down the table.
            // The last row's bottom border is added once, after the loop.
            var marginBottom = Units.TwipsToPoints(cell.MarginBottomTwips ?? properties.CellMarginBottomTwips);

            // A cell continuing a vertical merge draws no content of its own; the cell that
            // started the merge owns it.
            var content = cell.VerticalMerge == "continue"
                ? DetachedFlow.Empty
                : MeasureBlocks(cell.Content, Math.Max(1, width - marginLeft - marginRight));

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

        return CellInset(first.MarginLeftTwips ?? table.Properties.CellMarginLeftTwips, borders.Left);
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
        if (!table.Properties.FixedLayout && table.Rows.Count > 0)
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
                    Units.TwipsToPoints(cell.MarginLeftTwips ?? properties.CellMarginLeftTwips) +
                    Units.TwipsToPoints(cell.MarginRightTwips ?? properties.CellMarginRightTwips) +
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
    /// Lays out blocks into a detached page so their height can be measured before placement.
    /// </summary>
    private DetachedFlow MeasureBlocks(IReadOnlyList<BlockElement> blocks, double width)
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
            Columns = [(0, width)]
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
        var page = new LaidOutPage
        {
            WidthPoints = section.PageWidthPoints,
            HeightPoints = section.PageHeightPoints,
            Section = section,
            IndexInSection = _pagesInSection++
        };

        document.Pages.Add(page);
        return page;
    }

    /// <summary>Places a composed line's segments at their final page coordinates.</summary>
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
                Kerned = segment.Kerned
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

                var (next, alignment, leader) = NextTabStop(x, tab.Stops, tab.DefaultIntervalPoints);

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
        foreach (var item in content.Take(lastVisible + 1))
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
                ReferenceEquals(current.Format, textAtom.Format) &&
                ReferenceEquals(current.Font, textAtom.Font) &&
                Equals(current.Link, textAtom.Link) &&
                Math.Abs(current.X + current.Width - (indentLeft + offset + pen)) < 0.001)
            {
                current.Text += textAtom.Text;
                current.Width += width + extra;
                current.SpaceCount += textAtom.IsSpace ? 1 : 0;
            }
            else
            {
                current = new Segment
                {
                    X = indentLeft + offset + pen,
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
        var natural = Math.Max(maxTextNatural, maxImageAscent + maxTextDescent);

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

        AddNumberingLabel(atoms, paragraph, format, defaultTab);

        foreach (var run in paragraph.Runs)
        {
            var runFormat = _styles.ResolveRun(paragraph.Properties, run.Properties);
            if (runFormat.Hidden) continue;

            var link = ResolveHyperlink(run.Hyperlink);

            var selection = _fonts.Resolve(runFormat.FontFamily, runFormat.Bold, runFormat.Italic);
            var size = runFormat.EffectiveFontSizePoints;
            var ascent = TextMeasurer.GetAscent(selection.Font, size);
            var naturalHeight = TextMeasurer.GetNaturalLineHeight(selection.Font, size);
            var descent = naturalHeight - ascent;

            foreach (var inline in run.Content)
            {
                switch (inline)
                {
                    case TextInline text:
                        AddTextAtoms(atoms, TextMeasurer.ApplyTextTransform(text.Text, runFormat),
                            runFormat, selection, ascent, naturalHeight, descent, link);
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

                    case SeparatorInline:
                        atoms.Add(new SeparatorAtom
                        {
                            Width = FootnoteSeparatorWidthPoints,
                            Thickness = FootnoteSeparatorThicknessPoints,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight,
                            Descent = descent
                        });
                        break;

                    case FieldInline field:
                        AddTextAtoms(atoms, TextMeasurer.ApplyTextTransform(ResolveField(field), runFormat),
                            runFormat, selection, ascent, naturalHeight, descent, link);
                        break;
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
                labels.Count + 1, footnote ? _footnoteFormat : _endnoteFormat);

            labels[reference.Id] = text;
            if (!footnote) _endnoteOrder.Add(reference.Id);
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
        if (drawing.RelationshipId is null) return;

        var image = DecodeImage(drawing.RelationshipId);
        if (image is null) return;

        var width = drawing.WidthPoints;
        var height = drawing.HeightPoints;
        if (width <= 0 || height <= 0) return;

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

        var ascent = TextMeasurer.GetAscent(selection.Font, size);
        var naturalHeight = TextMeasurer.GetNaturalLineHeight(selection.Font, size);
        var descent = naturalHeight - ascent;

        AddTextAtoms(atoms, label, labelFormat, selection, ascent, naturalHeight, descent);

        switch (definition?.Suffix ?? NumberSuffix.Tab)
        {
            case NumberSuffix.Nothing:
                break;

            case NumberSuffix.Space:
                AddTextAtoms(atoms, " ", labelFormat, selection, ascent, naturalHeight, descent);
                break;

            default:
                // Tab stops are measured from the line's own left edge, and a hanging indent puts
                // that edge left of the paragraph's indent by exactly the hanging amount — so the
                // indent sits at that distance along the first line.
                var toIndent = Math.Max(0, -format.IndentFirstLinePoints);

                var stops = new List<TabStop>(format.TabStops);
                if (toIndent > 0) stops.Add(new TabStop(Units.PointsToTwips(toIndent), TabAlignment.Left, TabLeader.None));

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
    private Images.ImageData? DecodeImage(string relationshipId)
    {
        if (_decodedImages.TryGetValue(relationshipId, out var cached)) return cached;

        var image = _images.TryGetValue(relationshipId, out var bytes)
            ? Images.ImageReader.TryRead(bytes)
            : null;

        _decodedImages[relationshipId] = image;
        return image;
    }

    /// <summary>
    /// Splits text into word and space atoms. Spaces are separate atoms because they are both
    /// the break opportunities and the things justification stretches.
    /// </summary>
    private void AddTextAtoms(
        List<Atom> atoms, string text, ResolvedRunFormat format, FontSelection font,
        double ascent, double naturalHeight, double descent, ResolvedHyperlink? link = null)
    {
        var kerned = Kerned(format);
        var previous = '\0';

        var index = 0;
        while (index < text.Length)
        {
            var isSpace = text[index] == ' ';
            var start = index;

            while (index < text.Length && (text[index] == ' ') == isSpace)
                index++;

            var slice = text[start..index];

            // Splitting at spaces puts the pair straddling each split into neither atom, and Word
            // kerns those like any other — a V before a space is drawn tighter to it. The pair is
            // measured here and carried on the atom that follows it, which is also what lets it be
            // taken off again when that atom turns out to open a line.
            var leadingKern = kerned
                ? KerningBetween(font, format, previous, slice[0])
                : 0;

            previous = slice[^1];
            atoms.Add(new TextAtom
            {
                Text = slice,
                IsSpace = isSpace,
                Format = format,
                Font = font,
                Ascent = ascent,
                NaturalHeight = naturalHeight,
                Descent = descent,
                Link = link,
                Kerned = kerned,
                LeadingKern = leadingKern,
                Width = TextMeasurer.Measure(
                    font.Font, slice, format.EffectiveFontSizePoints,
                    format.CharacterSpacingPoints, kerned) * format.ScaleFactor + leadingKern
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
        public Action<LaidOutPage>? OnPageComplete { get; init; }

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

            ColumnIndex++;
            MaxColumnUsed = Math.Max(MaxColumnUsed, ColumnIndex);
            Y = ContentTop;
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
            OnPageComplete?.Invoke(Page);
        }

        /// <summary>Moves to a fresh page, leaving the one behind as it stands.</summary>
        public void StartNewPage()
        {
            Page = Engine.NewPage(Document, Section);
            Y = ContentTop;
            Reserved = 0;

            ColumnIndex = 0;
            MaxColumnUsed = 0;
            PageMaxY = 0;
            ColumnLines.Clear();
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
    private sealed class DetachedFlow(LaidOutPage page, double height, List<int>? footnotes = null)
    {
        public static readonly DetachedFlow Empty =
            new(new LaidOutPage { WidthPoints = 0, HeightPoints = 0 }, 0);

        public double Height { get; } = height;

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
            _shadingAt = Cell.Source.ShadingFill is null ? -1 : Page.Rectangles.Count;

        /// <summary>
        /// Fills the run in, at the place reserved for it when it opened — underneath the borders
        /// of the rows it runs through rather than over the top of them.
        /// </summary>
        private void Shade()
        {
            if (_shadingAt < 0 || Cell.Source.ShadingFill is not { } fill) return;

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
    }

    private sealed class TextAtom : Atom
    {
        public required string Text { get; init; }

        /// <summary>
        /// The footnote this atom is the reference mark for, if it is one. Carried on the atom so
        /// that the line a mark lands on is known, which is the page the footnote belongs on.
        /// </summary>
        public int? FootnoteId { get; init; }

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
