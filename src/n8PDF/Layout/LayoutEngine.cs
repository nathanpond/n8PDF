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

    public LaidOutDocument Layout(WordDocument document)
    {
        var section = document.Section;
        var result = new LaidOutDocument { Section = section };

        var contentTop = Units.TwipsToPoints(section.MarginTopTwips);

        var cursor = new Cursor
        {
            Document = result,
            Section = section,
            Page = NewPage(result, section),
            Y = contentTop,
            Left = Units.TwipsToPoints(section.MarginLeftTwips + section.GutterTwips),
            Width = section.ContentWidthPoints,
            ContentTop = contentTop,
            ContentBottom = contentTop + section.ContentHeightPoints,
            Paginate = true
        };

        LayoutBlocks(cursor, document.Body);

        // The final paragraph's space-after still occupies the page even though nothing follows
        // it, which matters for how much content a page is considered to hold.
        cursor.Y += cursor.PendingSpaceAfter;

        return result;
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

        foreach (var line in ComposeParagraph(paragraph, format, cursor.Width))
        {
            if (line.ForcePageBreak && cursor.CanBreak) cursor.BreakPage();

            // A line that does not fit starts a new page. Widow and orphan control would
            // move whole groups of lines here; it is not implemented yet.
            if (cursor.Paginate && cursor.Y + line.Height > cursor.ContentBottom && cursor.CanBreak)
                cursor.BreakPage();

            EmitLine(cursor.Page, line, cursor.Left, cursor.Y, index);
            cursor.Y += line.Height;
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

        var tableLeft = cursor.Left + Units.TwipsToPoints(properties.IndentTwips);
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

            if (cursor.Paginate && cursor.Y + rowHeight > cursor.ContentBottom && cursor.CanBreak)
                cursor.BreakPage();

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

                // Padding is part of what the column has to accommodate.
                var padding =
                    Units.TwipsToPoints(cell.MarginLeftTwips ?? properties.CellMarginLeftTwips) +
                    Units.TwipsToPoints(cell.MarginRightTwips ?? properties.CellMarginRightTwips);

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
        var section = new SectionProperties();
        var document = new LaidOutDocument { Section = section };

        var page = new LaidOutPage { WidthPoints = width, HeightPoints = double.MaxValue };
        document.Pages.Add(page);

        var cursor = new Cursor
        {
            Document = document,
            Section = section,
            Page = page,
            Y = 0,
            Left = 0,
            Width = width,
            ContentTop = 0,
            ContentBottom = double.MaxValue,
            Paginate = false
        };

        LayoutBlocks(cursor, blocks);
        cursor.Y += cursor.PendingSpaceAfter;

        return new DetachedFlow(page, cursor.Y);
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

    private static LaidOutPage NewPage(LaidOutDocument document, SectionProperties section)
    {
        var page = new LaidOutPage
        {
            WidthPoints = section.PageWidthPoints,
            HeightPoints = section.PageHeightPoints
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
                WordSpacing = segment.WordSpacing
            };

            laidOut.Texts.Add(text);
            AddDecorations(page, text);
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

    /// <summary>Breaks one paragraph into lines that fit the available width.</summary>
    private List<ComposedLine> ComposeParagraph(Paragraph paragraph, ResolvedParagraphFormat format, double contentWidth)
    {
        var atoms = BuildAtoms(paragraph, format);
        var lines = new List<ComposedLine>();

        var isFirstLine = true;
        var index = 0;
        var forceBreakOnNextLine = false;

        while (index < atoms.Count || lines.Count == 0)
        {
            var indentLeft = format.IndentLeftPoints + (isFirstLine ? Math.Max(0, format.IndentFirstLinePoints) : 0);

            // A hanging indent pulls the first line left of the others, so it applies to the
            // first line as a negative offset rather than to the rest as a positive one.
            if (isFirstLine && format.IndentFirstLinePoints < 0)
                indentLeft = format.IndentLeftPoints + format.IndentFirstLinePoints;

            var available = Math.Max(1, contentWidth - indentLeft - format.IndentRightPoints);

            var line = new ComposedLine
            {
                ForcePageBreak = forceBreakOnNextLine,
                IndentLeft = indentLeft
            };
            forceBreakOnNextLine = false;

            var consumed = FillLine(atoms, index, available, line, out var hardBreak, out var pageBreak);
            index += consumed;

            var isLastLine = index >= atoms.Count;
            FinishLine(line, format, indentLeft, available, isLastLine || hardBreak);
            lines.Add(line);

            if (pageBreak) forceBreakOnNextLine = true;

            isFirstLine = false;

            // The loop condition allows an empty paragraph one pass so that it still occupies a
            // line; break out once that pass is done.
            if (consumed == 0 && index >= atoms.Count) break;
        }

        // An empty paragraph has no atoms but still takes up a line, sized by its mark.
        foreach (var line in lines.Where(l => l.Segments.Count == 0))
            ApplyEmptyLineMetrics(line, format);

        return lines;
    }

    /// <summary>
    /// Greedily packs atoms onto one line. Trailing spaces are allowed to overflow the measure,
    /// which is what Word does — a line ending in a space does not wrap because of it.
    /// </summary>
    private static int FillLine(
        List<Atom> atoms, int start, double available, ComposedLine line, out bool hardBreak, out bool pageBreak)
    {
        hardBreak = false;
        pageBreak = false;

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
        var maxAscent = 0.0;
        var maxHeight = 0.0;

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

            var textAtom = (TextAtom)item.Atom;
            var extra = textAtom.IsSpace ? wordSpacing : 0;

            if (current is not null &&
                ReferenceEquals(current.Format, textAtom.Format) &&
                ReferenceEquals(current.Font, textAtom.Font) &&
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
                    SpaceCount = textAtom.IsSpace ? 1 : 0
                };

                line.Segments.Add(current);
            }

            pen += textAtom.Width + extra;

            maxAscent = Math.Max(maxAscent, textAtom.Ascent);
            maxHeight = Math.Max(maxHeight, textAtom.NaturalHeight);
        }

        ApplyLineMetrics(line, format, maxAscent, maxHeight);
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

        foreach (var run in paragraph.Runs)
        {
            var runFormat = _styles.ResolveRun(paragraph.Properties, run.Properties);
            if (runFormat.Hidden) continue;

            var selection = _fonts.Resolve(runFormat.FontFamily, runFormat.Bold, runFormat.Italic);
            var size = runFormat.EffectiveFontSizePoints;
            var ascent = TextMeasurer.GetAscent(selection.Font, size);
            var naturalHeight = TextMeasurer.GetNaturalLineHeight(selection.Font, size);

            foreach (var inline in run.Content)
            {
                switch (inline)
                {
                    case TextInline text:
                        AddTextAtoms(atoms, TextMeasurer.ApplyTextTransform(text.Text, runFormat),
                            runFormat, selection, ascent, naturalHeight);
                        break;

                    case TabInline:
                        atoms.Add(new TabAtom
                        {
                            Stops = format.TabStops,
                            DefaultIntervalPoints = defaultTab,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight
                        });
                        break;

                    case BreakInline breakInline:
                        atoms.Add(new BreakAtom
                        {
                            Kind = breakInline.Kind,
                            Ascent = ascent,
                            NaturalHeight = naturalHeight
                        });
                        break;
                }
            }
        }

        return atoms;
    }

    /// <summary>
    /// Splits text into word and space atoms. Spaces are separate atoms because they are both
    /// the break opportunities and the things justification stretches.
    /// </summary>
    private void AddTextAtoms(
        List<Atom> atoms, string text, ResolvedRunFormat format, FontSelection font, double ascent, double naturalHeight)
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
        public required LaidOutDocument Document { get; init; }

        public required SectionProperties Section { get; init; }

        public required LaidOutPage Page { get; set; }

        public required double Y { get; set; }

        public required double Left { get; init; }

        public required double Width { get; init; }

        public required double ContentTop { get; init; }

        public required double ContentBottom { get; init; }

        /// <summary>False inside a table cell, whose height is measured before it is placed.</summary>
        public required bool Paginate { get; init; }

        public ResolvedParagraphFormat? PreviousFormat { get; set; }

        public double PendingSpaceAfter { get; set; }

        /// <summary>
        /// True when a page break would achieve anything. Breaking an empty page just produces
        /// another empty one, and inside a cell there are no pages to break at all.
        /// </summary>
        public bool CanBreak => Paginate && (Page.Lines.Count > 0 || Page.Rectangles.Count > 0);

        public void BreakPage()
        {
            Page = NewPage(Document, Section);
            Y = ContentTop;
        }
    }

    /// <summary>
    /// Content laid out at the origin of a detached page, ready to be translated into position.
    /// </summary>
    private sealed class DetachedFlow(LaidOutPage page, double height)
    {
        public static readonly DetachedFlow Empty =
            new(new LaidOutPage { WidthPoints = 0, HeightPoints = 0 }, 0);

        public double Height { get; } = height;

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
                {
                    moved.Texts.Add(new PositionedText
                    {
                        X = text.X + dx,
                        BaselineY = text.BaselineY + dy,
                        Text = text.Text,
                        Format = text.Format,
                        Font = text.Font,
                        Width = text.Width,
                        WordSpacing = text.WordSpacing
                    });
                }

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
    }

    private sealed class TextAtom : Atom
    {
        public required string Text { get; init; }

        public required bool IsSpace { get; init; }

        public required ResolvedRunFormat Format { get; init; }

        public required FontSelection Font { get; init; }

        public required double Width { get; init; }
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
    }

    private sealed class ComposedLine
    {
        public List<PlacedAtom> Items { get; } = [];

        public List<Segment> Segments { get; } = [];

        public double Height { get; set; }

        public double Ascent { get; set; }

        public double IndentLeft { get; init; }

        public bool ForcePageBreak { get; init; }
    }
}
