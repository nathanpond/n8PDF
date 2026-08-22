using n8PDF.Ooxml;

namespace n8PDF.Styling;

/// <summary>
/// Applies table styles: what a table takes from the style it names rather than from itself.
/// </summary>
/// <remarks>
/// A table style is unlike every other kind. A paragraph style says one thing about every
/// paragraph that wears it; a table style says up to thirteen, and which one a cell gets depends
/// on where the cell is. There is what the whole table gets, then the banding across the rows and
/// down the columns, then the two edge rows and two edge columns, then the four corner cells, each
/// overriding the one before it. What the table asks for out of that list is its
/// <see cref="TableLook"/>, so the same style draws a table with a heading row and one without.
///
/// This runs over the parsed document before it is laid out and fills in what the table itself
/// left unsaid — the table's own properties always win, which is why every one of them had to
/// become nullable. Whether a cell is ruled cannot be answered until this has run: the borders of
/// the table Word inserts from its gallery live in the style and nowhere else, so before this
/// existed every table in every real document came out with no rules on it at all.
///
/// Which formats reach a cell is worked out from where the cell is. Word writes its own answer
/// onto each row and cell as a <c>w:cnfStyle</c>, but that is a cache of what it decided rather
/// than an instruction, and working it out agrees with Word on every cell of the probe.
///
/// What the style says about text is not filled in here, because it does not belong to the table:
/// it sits between the document's defaults and the paragraph's own style in the cascade, and only
/// <see cref="StyleResolver"/> knows where that is. It is hung on each cell paragraph instead, as
/// <see cref="ParagraphProperties.FromTableStyle"/>, in the order it is to be applied.
/// </remarks>
internal static class TableStyles
{
    /// <summary>
    /// What one style says about each part of a table: a list rather than a single format, since
    /// a style based on another adds to what that one said instead of replacing it, and the two
    /// are applied one after the other.
    /// </summary>
    private sealed class ResolvedStyle : Dictionary<TableConditional, List<TableStyleFormat>>;

    /// <summary>Applies the styles of every table in a document, however deeply buried.</summary>
    public static void Apply(WordDocument document, StyleDefinitions styles)
    {
        Apply(document.Body, styles);

        foreach (var note in document.Footnotes.Values.Concat(document.Endnotes.Values))
            Apply(note.Body, styles);

        foreach (var part in document.HeadersAndFooters.Values)
            Apply(part.Body, styles);
    }

    /// <summary>Applies them to every table among a run of blocks, and inside every cell.</summary>
    public static void Apply(IEnumerable<BlockElement> blocks, StyleDefinitions styles)
    {
        foreach (var block in blocks)
        {
            if (block is not Table table) continue;

            Apply(table, styles);

            foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                Apply(cell.Content, styles);
        }
    }

    /// <summary>Fills in everything one table takes from its style.</summary>
    public static void Apply(Table table, StyleDefinitions styles)
    {
        var style = Resolve(table.Properties.StyleId, styles);
        if (style.Count == 0) return;

        table.Properties = MergeTableProperties(table.Properties, style);

        var columnCount = ColumnCount(table);

        for (var row = 0; row < table.Rows.Count; row++)
        {
            MergeRow(table.Rows[row], Applicable(style, table.Properties, row, -1,
                table.Rows.Count, columnCount));

            var column = 0;

            foreach (var cell in table.Rows[row].Cells)
            {
                var applicable = Applicable(style, table.Properties, row, column,
                    table.Rows.Count, columnCount);

                MergeCell(cell, applicable);
                HangText(cell, applicable);

                column += Math.Max(1, cell.GridSpan);
            }
        }
    }

    /// <summary>
    /// What a style says, gathered down its inheritance chain from the most general ancestor to
    /// the style itself.
    /// </summary>
    private static ResolvedStyle Resolve(string? styleId, StyleDefinitions styles)
    {
        var resolved = new ResolvedStyle();
        if (styleId is null) return resolved;

        foreach (var style in styles.GetInheritanceChain(styleId))
        foreach (var (which, format) in style.TableFormats)
        {
            if (!resolved.TryGetValue(which, out var accumulated))
                resolved[which] = accumulated = [];

            accumulated.Add(format);
        }

        return resolved;
    }

    /// <summary>
    /// The formats reaching one cell — or one row, where the column is negative — in the order
    /// they are applied: least particular first, so the last one to speak decides.
    /// </summary>
    private static List<TableStyleFormat> Applicable(
        ResolvedStyle style, TableProperties properties,
        int row, int column, int rowCount, int columnCount)
    {
        var applicable = new List<TableStyleFormat>();

        foreach (var which in Enum.GetValues<TableConditional>())
        {
            if (!Reaches(which, properties, row, rowCount, column, columnCount)) continue;

            if (style.TryGetValue(which, out var formats)) applicable.AddRange(formats);
        }

        return applicable;
    }

    /// <summary>Whether one conditional format covers a cell.</summary>
    private static bool Reaches(
        TableConditional which, TableProperties properties,
        int row, int rowCount, int column, int columnCount)
    {
        var look = properties.Look;

        // A table of one row has a first row and no last one, and a table of one column a first
        // column and no last one: measured from the fixture's sixth page, where the single row of
        // a four-column table comes out in the first row's size and its corners in the northern
        // pair rather than the southern.
        var firstRow = look.FirstRow && row == 0;
        var lastRow = look.LastRow && row == rowCount - 1 && rowCount > 1;
        var firstColumn = look.FirstColumn && column == 0;
        var lastColumn = look.LastColumn && column == columnCount - 1 && columnCount > 1;

        return which switch
        {
            TableConditional.WholeTable => true,

            TableConditional.Band1Horizontal =>
                Band(look.HorizontalBanding, row, look.FirstRow, properties.RowBandSize) == 1,
            TableConditional.Band2Horizontal =>
                Band(look.HorizontalBanding, row, look.FirstRow, properties.RowBandSize) == 2,
            TableConditional.Band1Vertical =>
                Band(look.VerticalBanding, column, look.FirstColumn, properties.ColumnBandSize) == 1,
            TableConditional.Band2Vertical =>
                Band(look.VerticalBanding, column, look.FirstColumn, properties.ColumnBandSize) == 2,

            TableConditional.FirstColumn => firstColumn,
            TableConditional.LastColumn => lastColumn,
            TableConditional.FirstRow => firstRow,
            TableConditional.LastRow => lastRow,

            // A corner is where an edge row meets an edge column, so it takes both of them.
            TableConditional.NorthWestCell => firstRow && firstColumn,
            TableConditional.NorthEastCell => firstRow && lastColumn,
            TableConditional.SouthWestCell => lastRow && firstColumn,
            TableConditional.SouthEastCell => lastRow && lastColumn,
            _ => false
        };
    }

    /// <summary>
    /// Which band a row or column falls in: 1 for the odd bands, 2 for the even ones, and 0 where
    /// it is not banded at all.
    /// </summary>
    /// <remarks>
    /// Where there is an edge row or column it is not itself banded and the count begins after
    /// it, which the fixture shows directly: with a first row in force, rows two to five band one,
    /// two, one, two. The last row is counted like any other — the format for it wins where there
    /// is one, and where there is not, the banding runs on through it.
    /// </remarks>
    private static int Band(bool banded, int index, bool edgeInForce, int bandSize)
    {
        if (!banded || index < 0) return 0;

        var start = edgeInForce ? 1 : 0;
        if (index < start) return 0;

        return (index - start) / Math.Max(1, bandSize) % 2 == 0 ? 1 : 2;
    }

    private static int ColumnCount(Table table)
    {
        var widest = table.Grid.Count;

        foreach (var row in table.Rows)
            widest = Math.Max(widest, row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)));

        return widest;
    }

    /// <summary>
    /// The table's own properties over the style's. Everything the table declared wins; everything
    /// it left alone comes from the style, and which style that was is the table's own business.
    /// </summary>
    /// <remarks>
    /// Only the whole-table format bears on the table itself. A conditional format may carry a
    /// <c>w:tblPr</c> too, but what it says there is about the part of the table it describes, and
    /// the borders and margins of a part are a cell's business rather than the table's.
    /// </remarks>
    private static TableProperties MergeTableProperties(TableProperties table, ResolvedStyle style)
    {
        var merged = new TableProperties();

        if (style.TryGetValue(TableConditional.WholeTable, out var formats))
        {
            foreach (var format in formats)
            {
                if (format.Table is { } from) OverwriteTable(from, merged);
            }
        }

        OverwriteTable(table, merged);

        merged.StyleId = table.StyleId;
        merged.Look = table.Look;
        merged.RowBandSize = table.RowBandSize;
        merged.ColumnBandSize = table.ColumnBandSize;

        return merged;
    }

    private static void OverwriteTable(TableProperties from, TableProperties to)
    {
        if (from.WidthTwips is { } width) to.WidthTwips = width;
        if (from.WidthFraction is { } fraction) to.WidthFraction = fraction;
        if (from.IndentTwips is { } indent) to.IndentTwips = indent;
        if (from.Mirrored) to.Mirrored = true;
        if (from.FixedLayout is { } layout) to.FixedLayout = layout;
        if (from.Justification is { } justification) to.Justification = justification;
        if (from.CellMarginLeftTwips is { } left) to.CellMarginLeftTwips = left;
        if (from.CellMarginRightTwips is { } right) to.CellMarginRightTwips = right;
        if (from.CellMarginTopTwips is { } top) to.CellMarginTopTwips = top;
        if (from.CellMarginBottomTwips is { } bottom) to.CellMarginBottomTwips = bottom;

        OverwriteBorders(from.Borders, to.Borders);
    }

    private static void OverwriteBorders(BorderSet from, BorderSet to)
    {
        to.Top = from.Top ?? to.Top;
        to.Left = from.Left ?? to.Left;
        to.Bottom = from.Bottom ?? to.Bottom;
        to.Right = from.Right ?? to.Right;
        to.InsideHorizontal = from.InsideHorizontal ?? to.InsideHorizontal;
        to.InsideVertical = from.InsideVertical ?? to.InsideVertical;
    }

    private static void MergeRow(TableRow row, List<TableStyleFormat> applicable)
    {
        var accumulated = new TableStyleRowProperties();

        foreach (var format in applicable)
        {
            if (format.Row is not { } from) continue;

            accumulated.CantSplit = from.CantSplit ?? accumulated.CantSplit;
            accumulated.IsHeader = from.IsHeader ?? accumulated.IsHeader;
            accumulated.HeightTwips = from.HeightTwips ?? accumulated.HeightTwips;
            accumulated.HeightRule = from.HeightRule ?? accumulated.HeightRule;
        }

        row.CantSplit ??= accumulated.CantSplit;
        row.IsHeader ??= accumulated.IsHeader;

        if (row.HeightTwips is null && accumulated.HeightTwips is { } height)
        {
            row.HeightTwips = height;
            row.HeightRule = accumulated.HeightRule ?? RowHeightRule.AtLeast;
        }
    }

    private static void MergeCell(TableCell cell, List<TableStyleFormat> applicable)
    {
        var accumulated = new TableStyleCellProperties();

        foreach (var format in applicable)
        {
            if (format.Cell is not { } from) continue;

            accumulated.ShadingFill = from.ShadingFill ?? accumulated.ShadingFill;
            accumulated.ShadingPattern = from.ShadingPattern ?? accumulated.ShadingPattern;
            accumulated.ShadingPatternColor = from.ShadingPatternColor ?? accumulated.ShadingPatternColor;
            accumulated.VerticalAlignment = from.VerticalAlignment ?? accumulated.VerticalAlignment;
            accumulated.MarginLeftTwips = from.MarginLeftTwips ?? accumulated.MarginLeftTwips;
            accumulated.MarginRightTwips = from.MarginRightTwips ?? accumulated.MarginRightTwips;
            accumulated.MarginTopTwips = from.MarginTopTwips ?? accumulated.MarginTopTwips;
            accumulated.MarginBottomTwips = from.MarginBottomTwips ?? accumulated.MarginBottomTwips;

            OverwriteBorders(from.Borders, accumulated.Borders);
        }

        // The cell's own formatting wins over all of it.
        cell.ShadingFill ??= accumulated.ShadingFill;
        cell.ShadingPattern ??= accumulated.ShadingPattern;
        cell.ShadingPatternColor ??= accumulated.ShadingPatternColor;
        cell.VerticalAlignment ??= accumulated.VerticalAlignment;
        cell.MarginLeftTwips ??= accumulated.MarginLeftTwips;
        cell.MarginRightTwips ??= accumulated.MarginRightTwips;
        cell.MarginTopTwips ??= accumulated.MarginTopTwips;
        cell.MarginBottomTwips ??= accumulated.MarginBottomTwips;

        cell.Borders.Top ??= accumulated.Borders.Top;
        cell.Borders.Left ??= accumulated.Borders.Left;
        cell.Borders.Bottom ??= accumulated.Borders.Bottom;
        cell.Borders.Right ??= accumulated.Borders.Right;
        cell.Borders.InsideHorizontal ??= accumulated.Borders.InsideHorizontal;
        cell.Borders.InsideVertical ??= accumulated.Borders.InsideVertical;
    }

    /// <summary>
    /// Hangs the style's text formatting on every paragraph directly inside a cell. A table nested
    /// in the cell is left alone: it wears its own style, and answers for its own cells.
    /// </summary>
    private static void HangText(TableCell cell, List<TableStyleFormat> applicable)
    {
        var text = new TableStyleText(
            [.. applicable.Select(format => format.Paragraph).OfType<ParagraphProperties>()],
            [.. applicable.Select(format => format.Run).OfType<RunProperties>()]);

        if (text.Paragraph.Count == 0 && text.Run.Count == 0) return;

        foreach (var paragraph in cell.Content.OfType<Paragraph>())
            paragraph.Properties.FromTableStyle = text;
    }
}