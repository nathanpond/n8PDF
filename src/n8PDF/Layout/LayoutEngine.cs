using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Styling;

namespace n8PDF.Layout;

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

        // The last page never breaks, so its footnotes are still waiting and its column rules
        // have not been drawn.
        DrawColumnSeparators(cursor);
        FlushFootnotes(cursor.Page);

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

        // The footnotes of the page being left behind belong to the geometry it was laid out
        // under, so they are written into it before the new section's takes over.
        FlushFootnotes(cursor.Page);
        ApplySection(cursor, section);
        cursor.BreakPage();

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
        var composer = new ParagraphComposer(BuildAtoms(paragraph, format), format);
        var bookmarks = _pendingBookmarks.ToList();
        var firstLine = true;

        // This paragraph's lines in the column being filled, so that widow and orphan control can
        // take some of them back off it. Cleared whenever the cursor moves on.
        var placed = new List<PlacedLine>();
        var emitted = 0;

        while (composer.HasMore)
        {
            // A break carried over from the previous line is applied before this one is composed,
            // not after: a column break changes the measure, and a line broken against the width
            // it was leaving would be wrapped in the wrong place.
            if (composer.PendingPageBreak && cursor.CanBreak)
            {
                cursor.BreakPage();
                placed.Clear();
            }
            else if (composer.PendingColumnBreak && cursor.CanAdvance)
            {
                cursor.AdvanceColumn();
                placed.Clear();
            }

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
                var pull = WidowOrphanPullBack(format, placed, isLastLine: !composer.HasMore, cursor);
                var pulled = pull > 0 ? UnplaceLines(cursor, placed, pull) : [];

                cursor.AdvanceColumn();
                placed.Clear();

                foreach (var moved in pulled) RePlaceLine(cursor, moved, index, placed);

                // A pulled-back first line takes its paragraph's bookmarks with it.
                if (pull > 0 && emitted - pull == 0) firstLine = true;

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

            placed.Add(Place(cursor, line, index, footnoteIds, footnotes));
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
        double FootnoteHeight);

    /// <summary>Puts a composed line on the page and records what that took.</summary>
    private PlacedLine Place(
        Cursor cursor, ComposedLine line, int paragraphIndex,
        IReadOnlyList<int> footnoteIds, (List<DetachedFlow> Flows, double Height) footnotes)
    {
        var placed = new PlacedLine(
            line, cursor.Y,
            cursor.Page.Lines.Count, cursor.Page.Rules.Count, cursor.Page.Images.Count,
            footnoteIds, footnotes.Flows.Count, footnotes.Height);

        EmitLine(cursor.Page, line, cursor.Left, cursor.Y, paragraphIndex);
        CommitFootnotes(cursor, footnotes);
        cursor.Y += line.Height;

        return placed;
    }

    /// <summary>
    /// How many of a paragraph's lines must follow the break rather than staying above it.
    /// </summary>
    /// <remarks>
    /// Word's rule is two lines on each side: one line of a paragraph left at the foot of a column
    /// is an orphan, one carried alone to the next is a widow, and it will have neither. A
    /// paragraph of three lines cannot satisfy both at once, so all of it moves.
    ///
    /// Lines are never pushed off a column they already start. There would be nothing above them
    /// to gain by it, and the next column would be no roomier than the one they were pushed out
    /// of, so the paragraph would march across the page never fitting anywhere.
    /// </remarks>
    private static int WidowOrphanPullBack(
        ResolvedParagraphFormat format, List<PlacedLine> placed, bool isLastLine, Cursor cursor)
    {
        if (!format.WidowControl || placed.Count == 0) return 0;

        var pull = placed.Count switch
        {
            1 => 1,
            2 when isLastLine => 2,
            _ when isLastLine => 1,
            _ => 0
        };

        if (pull == 0) return 0;

        return placed[^pull].Top > cursor.ContentTop + 0.001 ? pull : 0;
    }

    /// <summary>
    /// Takes the last few lines back off the page, undoing everything placing them did, and
    /// returns them in the order they were placed so they can go down again elsewhere.
    /// </summary>
    private List<PlacedLine> UnplaceLines(Cursor cursor, List<PlacedLine> placed, int count)
    {
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

    /// <summary>Puts a pulled-back line down again where the cursor now stands.</summary>
    private void RePlaceLine(Cursor cursor, PlacedLine line, int paragraphIndex, List<PlacedLine> placed)
    {
        var footnotes = line.FootnoteIds.Count > 0 && cursor.FootnoteSink is null
            ? PrepareFootnotes(line.FootnoteIds)
            : ([], 0);

        placed.Add(Place(cursor, line.Line, paragraphIndex, line.FootnoteIds, footnotes));
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

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var placed = MeasureRow(table, row, rowIndex, columns, tableLeft);
            if (placed.Count == 0) continue;

            var rowHeight = ComputeRowHeight(row, placed);

            // A cell's contents were composed on a page of their own, so any footnote they refer
            // to is still waiting to be given to the page the row lands on.
            var rowFootnoteIds = placed.SelectMany(cell => cell.Content.Footnotes).ToList();
            if (cursor.FootnoteSink is not null) cursor.FootnoteSink.AddRange(rowFootnoteIds);

            var rowFootnotes = cursor.FootnoteSink is null && rowFootnoteIds.Count > 0
                ? PrepareFootnotes(rowFootnoteIds)
                : ([], 0);

            if (cursor.Paginate && cursor.Y + rowHeight > cursor.ContentBottom - rowFootnotes.Height && cursor.CanAdvance)
            {
                cursor.AdvanceColumn();
                if (rowFootnotes.Flows.Count > 0) rowFootnotes = PrepareFootnotes(rowFootnoteIds);
            }

            var top = cursor.Y;

            // Shading first, then content, then borders on top: a border sits on the cell edge
            // and would otherwise be half-covered by the neighbouring cell's fill.
            foreach (var cell in placed)
            {
                if (cell.Source.ShadingFill is not { } fill) continue;

                cursor.Page.Rectangles.Add(new PositionedRectangle
                {
                    X = cell.Left,
                    Y = top,
                    Width = cell.Width,
                    Height = rowHeight,
                    Color = ParseHexColor(fill)
                });
            }

            foreach (var cell in placed)
            {
                var available = rowHeight - cell.MarginTop - cell.MarginBottom;
                var offset = cell.Source.VerticalAlignment switch
                {
                    VerticalCellAlignment.Center => Math.Max(0, (available - cell.Content.Height) / 2),
                    VerticalCellAlignment.Bottom => Math.Max(0, available - cell.Content.Height),
                    _ => 0
                };

                cell.Content.PlaceOnto(cursor.Page, cell.Left + cell.MarginLeft, top + cell.MarginTop + offset);
            }

            DrawRowBorders(cursor.Page, placed, top, rowHeight);

            CommitFootnotes(cursor, rowFootnotes);
            cursor.Y += rowHeight;

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

            // Content is inset by the border as well as the margin: a border occupies space
            // inside the cell rather than being painted over its contents.
            var marginLeft = Units.TwipsToPoints(cell.MarginLeftTwips ?? properties.CellMarginLeftTwips)
                             + BorderWidth(borders.Left);
            var marginRight = Units.TwipsToPoints(cell.MarginRightTwips ?? properties.CellMarginRightTwips)
                              + BorderWidth(borders.Right);
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
                marginLeft, marginRight, marginTop, marginBottom, borders));

            x += width;
            column += span;
        }

        return placed;
    }

    private static double ComputeRowHeight(TableRow row, List<PlacedCell> placed)
    {
        var natural = 0.0;
        foreach (var cell in placed)
            natural = Math.Max(natural, cell.Content.Height + cell.MarginTop + cell.MarginBottom);

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

        // A cell continuing a vertical merge has no line above it, which is what makes the merge
        // read as one tall cell.
        if (cell.VerticalMerge == "continue") top = null;

        return new CellBorders(
            cell.Borders.Left ?? (isFirstColumn ? borders.Left : borders.InsideVertical),
            cell.Borders.Right ?? (isLastColumn ? borders.Right : borders.InsideVertical),
            top,
            cell.Borders.Bottom ?? (isLastRow ? borders.Bottom : borders.InsideHorizontal));
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

        return Units.TwipsToPoints(first.MarginLeftTwips ?? table.Properties.CellMarginLeftTwips)
               + BorderWidth(borders.Left);
    }

    private static double BorderWidth(BorderEdge? edge) =>
        edge is not null && edge.IsVisible ? edge.WidthPoints : 0;

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
    private static void EmitLine(LaidOutPage page, ComposedLine line, double contentLeft, double top, int paragraphIndex)
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
                Link = segment.Link
            };

            laidOut.Texts.Add(text);
            AddDecorations(page, text);
        }

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
    private sealed class ParagraphComposer(List<Atom> atoms, ResolvedParagraphFormat format)
    {
        private int _index;
        private bool _isFirstLine = true;
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
                atoms, _index, available, line, out var hardBreak, out var pageBreak, out var columnBreak);
            _index += consumed;
            _producedAny = true;

            var isLastLine = _index >= atoms.Count;
            FinishLine(line, format, left, available, isLastLine || hardBreak);

            if (pageBreak) _forceBreakOnNextLine = true;
            if (columnBreak) _forceColumnBreakOnNextLine = true;
            _isFirstLine = false;

            // An empty paragraph has no atoms but still takes up a line, sized by its mark.
            if (line.Segments.Count == 0) ApplyEmptyLineMetrics(line, format);

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
        List<Atom> atoms, int start, double available, ComposedLine line,
        out bool hardBreak, out bool pageBreak, out bool columnBreak)
    {
        hardBreak = false;
        pageBreak = false;
        columnBreak = false;

        var x = 0.0;
        var index = start;
        var placedAnything = false;

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
                var next = NextTabStop(x, tab.Stops, tab.DefaultIntervalPoints);
                if (next > available && placedAnything) break;

                line.Items.Add(new PlacedAtom(atom, x, next - x));
                x = next;
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
                if (placedAnything && x + image.Width > available + 0.001) break;

                line.Items.Add(new PlacedAtom(atom, x, image.Width));
                x += image.Width;
                index++;
                placedAnything = true;
                continue;
            }

            var textAtom = (TextAtom)atom;

            // Spaces at the end of a line hang past the margin rather than forcing a wrap.
            if (!textAtom.IsSpace && placedAnything && x + textAtom.Width > available + 0.001)
                break;

            line.Items.Add(new PlacedAtom(atom, x, textAtom.Width));
            x += textAtom.Width;
            index++;
            placedAnything = true;

            // A single word longer than the measure has to go somewhere; it overflows rather
            // than looping forever. Breaking inside a word would need hyphenation rules.
            if (!textAtom.IsSpace && x > available && line.Items.Count == 1) break;
        }

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
            if (item.Atom is TabAtom)
            {
                current = null;
                pen = item.X + item.Width;
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

            if (current is not null &&
                ReferenceEquals(current.Format, textAtom.Format) &&
                ReferenceEquals(current.Font, textAtom.Font) &&
                Equals(current.Link, textAtom.Link) &&
                Math.Abs(current.X + current.Width - (indentLeft + offset + pen)) < 0.001)
            {
                current.Text += textAtom.Text;
                current.Width += textAtom.Width + extra;
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
                    Width = textAtom.Width + extra,
                    WordSpacing = wordSpacing,
                    SpaceCount = textAtom.IsSpace ? 1 : 0,
                    Link = textAtom.Link
                };

                line.Segments.Add(current);
            }

            pen += textAtom.Width + extra;

            maxTextAscent = Math.Max(maxTextAscent, textAtom.Ascent);
            maxTextDescent = Math.Max(maxTextDescent, textAtom.Descent);
            maxTextNatural = Math.Max(maxTextNatural, textAtom.NaturalHeight);
        }

        var ascent = Math.Max(maxTextAscent, maxImageAscent);
        var natural = Math.Max(maxTextNatural, maxImageAscent + maxTextDescent);

        ApplyLineMetrics(line, format, ascent, natural);
    }

    private static void ApplyEmptyLineMetrics(ComposedLine line, ResolvedParagraphFormat format)
    {
        // Nothing was placed, so the paragraph mark's own formatting sets the height.
        if (line.Height > 0) return;

        var size = format.MarkFormat.FontSizePoints;
        ApplyLineMetrics(line, format, size * 0.9, size * 1.15);
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

    private static double NextTabStop(double x, IReadOnlyList<TabStop> stops, double defaultInterval)
    {
        foreach (var stop in stops.OrderBy(s => s.PositionTwips))
        {
            if (stop.Alignment == TabAlignment.Clear) continue;

            var position = Units.TwipsToPoints(stop.PositionTwips);
            // Left-aligned stops are the only kind handled so far; centre, right and decimal
            // stops need the following text measured before the position can be resolved.
            if (position > x + 0.001) return position;
        }

        if (defaultInterval <= 0) return x;

        var next = Math.Floor(x / defaultInterval + 1) * defaultInterval;
        return next <= x ? x + defaultInterval : next;
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
            FootnoteId = footnote ? reference.Id : null,
            Width = TextMeasurer.Measure(
                font.Font, text, format.EffectiveFontSizePoints,
                format.CharacterSpacingPoints, _options.ApplyKerning) * format.ScaleFactor
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
        var index = 0;
        while (index < text.Length)
        {
            var isSpace = text[index] == ' ';
            var start = index;

            while (index < text.Length && (text[index] == ' ') == isSpace)
                index++;

            var slice = text[start..index];
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
                Width = TextMeasurer.Measure(
                    font.Font, slice, format.EffectiveFontSizePoints,
                    format.CharacterSpacingPoints, _options.ApplyKerning) * format.ScaleFactor
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
            ApplyColumn();
        }

        /// <summary>Points the cursor at its current column within the content box.</summary>
        public void ApplyColumn()
        {
            var index = Math.Clamp(ColumnIndex, 0, Columns.Count - 1);

            Left = SectionLeft + Columns[index].Left;
            Width = Columns[index].Width;
        }

        public void BreakPage()
        {
            PageMaxY = Math.Max(PageMaxY, Y);
            DrawColumnSeparators(this);
            OnPageComplete?.Invoke(Page);

            Page = Engine.NewPage(Document, Section);
            Y = ContentTop;
            Reserved = 0;

            ColumnIndex = 0;
            MaxColumnUsed = 0;
            PageMaxY = 0;
            ApplyColumn();

            // A float belongs to the page its anchor landed on; it does not follow the text.
            Floats.Clear();
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
        CellBorders Borders);

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

    private readonly record struct PlacedAtom(Atom Atom, double X, double Width);

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
    }

    private sealed class ComposedLine
    {
        public List<PlacedAtom> Items { get; } = [];

        public List<Segment> Segments { get; } = [];

        public List<(ImageAtom Atom, double X)> Images { get; } = [];

        /// <summary>Footnote separator rules on this line, as atom and x offset.</summary>
        public List<(SeparatorAtom Atom, double X)> Separators { get; } = [];

        public double Height { get; set; }

        public double Ascent { get; set; }

        public double IndentLeft { get; init; }

    }
}
