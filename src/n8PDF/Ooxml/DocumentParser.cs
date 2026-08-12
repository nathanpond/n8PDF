using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>
/// Turns <c>word/document.xml</c> into the document model. Property elements are read verbatim:
/// resolving what they mean against the style hierarchy is the Styling layer's job.
/// </summary>
public static class DocumentParser
{
    public static WordDocument Parse(XDocument xml)
    {
        var document = new WordDocument();
        var body = xml.Root?.Element(W.Main + "body");
        if (body is null) return document;

        foreach (var element in body.Elements())
        {
            if (element.Name == W.Main + "p")
                document.Body.Add(ParseParagraph(element));
            else if (element.Name == W.Main + "tbl")
                document.Body.Add(ParseTable(element));
            else if (element.Name == W.Main + "sectPr")
                document.Section = ParseSection(element);
        }

        return document;
    }

    public static Paragraph ParseParagraph(XElement element)
    {
        var paragraph = new Paragraph();

        var pPr = element.Element(W.Main + "pPr");
        if (pPr is not null)
        {
            paragraph.Properties = ParseParagraphProperties(pPr);

            var sectPr = pPr.Element(W.Main + "sectPr");
            if (sectPr is not null) paragraph.SectionBreak = ParseSection(sectPr);
        }

        foreach (var child in element.Elements())
            CollectRuns(child, paragraph);

        return paragraph;
    }

    /// <summary>
    /// Collects runs from a paragraph child. Hyperlinks and revision-tracking containers wrap
    /// runs rather than replacing them, so their contents are pulled up rather than skipped —
    /// otherwise the text inside a tracked insertion would silently vanish.
    /// </summary>
    private static void CollectRuns(XElement element, Paragraph paragraph)
    {
        if (element.Name == W.Main + "r")
        {
            paragraph.Runs.Add(ParseRun(element));
            return;
        }

        if (element.Name == W.Main + "hyperlink" ||
            element.Name == W.Main + "ins" ||
            element.Name == W.Main + "smartTag" ||
            element.Name == W.Main + "sdtContent" ||
            element.Name == W.Main + "bookmarkStart")
        {
            foreach (var child in element.Elements())
                CollectRuns(child, paragraph);
            return;
        }

        // Structured document tags wrap their content one level deeper.
        if (element.Name == W.Main + "sdt")
        {
            var content = element.Element(W.Main + "sdtContent");
            if (content is not null)
            {
                foreach (var child in content.Elements())
                    CollectRuns(child, paragraph);
            }
        }

        // w:del holds deleted text, which must not be rendered, so it is deliberately dropped.
    }

    public static Run ParseRun(XElement element)
    {
        var run = new Run();

        var rPr = element.Element(W.Main + "rPr");
        if (rPr is not null) run.Properties = ParseRunProperties(rPr);

        foreach (var child in element.Elements())
        {
            if (child.Name == W.Main + "t")
            {
                run.Content.Add(new TextInline(ReadText(child)));
            }
            else if (child.Name == W.Main + "tab")
            {
                run.Content.Add(new TabInline());
            }
            else if (child.Name == W.Main + "br")
            {
                var type = child.Attr("type");
                var kind = type switch
                {
                    "page" => BreakKind.Page,
                    "column" => BreakKind.Column,
                    _ => BreakKind.Line
                };
                run.Content.Add(new BreakInline(kind));
            }
            else if (child.Name == W.Main + "drawing")
            {
                var drawing = ParseDrawing(child);
                if (drawing is not null) run.Content.Add(drawing);
            }
            else if (child.Name == W.Main + "noBreakHyphen")
            {
                run.Content.Add(new TextInline("‑"));
            }
            else if (child.Name == W.Main + "softHyphen")
            {
                run.Content.Add(new TextInline("­"));
            }
        }

        return run;
    }

    /// <summary>
    /// Reads a <c>w:t</c>. Leading and trailing whitespace is only meaningful when the element
    /// carries <c>xml:space="preserve"</c>; without it, XML whitespace rules apply and Word
    /// expects the text trimmed.
    /// </summary>
    private static string ReadText(XElement element)
    {
        var text = element.Value;
        var space = element.Attribute(XNamespace.Xml + "space")?.Value;
        return space == "preserve" ? text : text.Trim();
    }

    private static DrawingInline? ParseDrawing(XElement element)
    {
        // Both inline and anchored drawings carry an a:ext with the display size.
        var extent = element.Descendants(W.WordDrawing + "extent").FirstOrDefault()
                     ?? element.Descendants(W.Drawing + "ext").FirstOrDefault();
        if (extent is null) return null;

        var cx = extent.Attribute("cx")?.Value;
        var cy = extent.Attribute("cy")?.Value;
        if (!long.TryParse(cx, out var width) || !long.TryParse(cy, out var height))
            return null;

        var blip = element.Descendants(W.Drawing + "blip").FirstOrDefault();
        var relationshipId = blip?.Attribute(W.Relationships + "embed")?.Value;

        return new DrawingInline(width, height, relationshipId);
    }

    public static RunProperties ParseRunProperties(XElement rPr)
    {
        var properties = new RunProperties();

        foreach (var element in rPr.Elements())
        {
            var name = element.Name.LocalName;
            switch (name)
            {
                case "rStyle":
                    properties.StyleId = element.Val();
                    break;
                case "rFonts":
                    properties.AsciiFont = element.Attr("ascii");
                    properties.HighAnsiFont = element.Attr("hAnsi");
                    properties.EastAsiaFont = element.Attr("eastAsia");
                    properties.ComplexScriptFont = element.Attr("cs");
                    properties.AsciiTheme = element.Attr("asciiTheme");
                    properties.HighAnsiTheme = element.Attr("hAnsiTheme");
                    break;
                case "sz":
                    properties.SizeHalfPoints = element.IntVal();
                    break;
                case "b":
                    properties.Bold = element.OnOff();
                    break;
                case "i":
                    properties.Italic = element.OnOff();
                    break;
                case "caps":
                    properties.Caps = element.OnOff();
                    break;
                case "smallCaps":
                    properties.SmallCaps = element.OnOff();
                    break;
                case "strike":
                    properties.Strike = element.OnOff();
                    break;
                case "vanish":
                    properties.Vanish = element.OnOff();
                    break;
                case "u":
                    properties.Underline = ParseUnderline(element.Val());
                    break;
                case "color":
                    var color = element.Val();
                    // "auto" means the consumer picks a contrasting colour; black in practice.
                    properties.Color = color is null or "auto" ? null : color;
                    break;
                case "highlight":
                    properties.Highlight = element.Val();
                    break;
                case "vertAlign":
                    properties.VerticalAlignment = element.Val() switch
                    {
                        "superscript" => VerticalTextAlignment.Superscript,
                        "subscript" => VerticalTextAlignment.Subscript,
                        _ => VerticalTextAlignment.Baseline
                    };
                    break;
                case "spacing":
                    properties.CharacterSpacingTwips = element.IntVal();
                    break;
                case "w":
                    properties.ScalePercent = element.IntVal();
                    break;
            }
        }

        return properties;
    }

    public static ParagraphProperties ParseParagraphProperties(XElement pPr)
    {
        var properties = new ParagraphProperties();

        foreach (var element in pPr.Elements())
        {
            var name = element.Name.LocalName;
            switch (name)
            {
                case "pStyle":
                    properties.StyleId = element.Val();
                    break;
                case "jc":
                    properties.Justification = ParseJustification(element.Val());
                    break;
                case "ind":
                    properties.IndentLeftTwips = element.IntAttr("left") ?? element.IntAttr("start");
                    properties.IndentRightTwips = element.IntAttr("right") ?? element.IntAttr("end");
                    properties.IndentFirstLineTwips = element.IntAttr("firstLine");
                    properties.IndentHangingTwips = element.IntAttr("hanging");
                    break;
                case "spacing":
                    properties.SpacingBeforeTwips = element.IntAttr("before");
                    properties.SpacingAfterTwips = element.IntAttr("after");
                    properties.Line = element.IntAttr("line");
                    properties.LineRule = element.Attr("lineRule") switch
                    {
                        "exact" => LineSpacingRule.Exact,
                        "atLeast" => LineSpacingRule.AtLeast,
                        "auto" => LineSpacingRule.Auto,
                        _ => properties.Line is not null ? LineSpacingRule.Auto : null
                    };
                    break;
                case "contextualSpacing":
                    properties.ContextualSpacing = element.OnOff();
                    break;
                case "keepNext":
                    properties.KeepNext = element.OnOff();
                    break;
                case "keepLines":
                    properties.KeepLines = element.OnOff();
                    break;
                case "pageBreakBefore":
                    properties.PageBreakBefore = element.OnOff();
                    break;
                case "widowControl":
                    properties.WidowControl = element.OnOff();
                    break;
                case "numPr":
                    properties.NumberingId = element.Element(W.Main + "numId")?.IntVal();
                    properties.NumberingLevel = element.Element(W.Main + "ilvl")?.IntVal();
                    break;
                case "tabs":
                    foreach (var tab in element.Elements(W.Main + "tab"))
                    {
                        var position = tab.IntAttr("pos");
                        if (position is null) continue;

                        properties.TabStops.Add(new TabStop(
                            position.Value,
                            ParseTabAlignment(tab.Attr("val")),
                            ParseTabLeader(tab.Attr("leader"))));
                    }

                    break;
                case "rPr":
                    properties.MarkRunProperties = ParseRunProperties(element);
                    break;
            }
        }

        return properties;
    }

    public static SectionProperties ParseSection(XElement sectPr)
    {
        var section = new SectionProperties();

        var pgSz = sectPr.Element(W.Main + "pgSz");
        if (pgSz is not null)
        {
            section.PageWidthTwips = pgSz.IntAttr("w") ?? section.PageWidthTwips;
            section.PageHeightTwips = pgSz.IntAttr("h") ?? section.PageHeightTwips;
            section.Landscape = string.Equals(pgSz.Attr("orient"), "landscape", StringComparison.OrdinalIgnoreCase);
        }

        var pgMar = sectPr.Element(W.Main + "pgMar");
        if (pgMar is not null)
        {
            section.MarginTopTwips = pgMar.IntAttr("top") ?? section.MarginTopTwips;
            section.MarginRightTwips = pgMar.IntAttr("right") ?? section.MarginRightTwips;
            section.MarginBottomTwips = pgMar.IntAttr("bottom") ?? section.MarginBottomTwips;
            section.MarginLeftTwips = pgMar.IntAttr("left") ?? section.MarginLeftTwips;
            section.HeaderDistanceTwips = pgMar.IntAttr("header") ?? section.HeaderDistanceTwips;
            section.FooterDistanceTwips = pgMar.IntAttr("footer") ?? section.FooterDistanceTwips;
            section.GutterTwips = pgMar.IntAttr("gutter") ?? section.GutterTwips;
        }

        var cols = sectPr.Element(W.Main + "cols");
        if (cols is not null)
            section.ColumnCount = Math.Max(1, cols.IntAttr("num") ?? 1);

        // Word writes top and bottom margins as negative values when they mean "at least this
        // far", which would otherwise produce a text area taller than the page.
        section.MarginTopTwips = Math.Abs(section.MarginTopTwips);
        section.MarginBottomTwips = Math.Abs(section.MarginBottomTwips);

        return section;
    }

    private static Table ParseTable(XElement element)
    {
        var table = new Table();

        var tblPr = element.Element(W.Main + "tblPr");
        if (tblPr is not null) table.Properties = ParseTableProperties(tblPr);

        foreach (var gridCol in element.Element(W.Main + "tblGrid")?.Elements(W.Main + "gridCol") ?? [])
        {
            var width = gridCol.IntAttr("w");
            if (width is not null) table.Grid.Add(width.Value);
        }

        foreach (var rowElement in element.Elements(W.Main + "tr"))
            table.Rows.Add(ParseTableRow(rowElement));

        return table;
    }

    public static TableProperties ParseTableProperties(XElement tblPr)
    {
        var properties = new TableProperties();

        var tblW = tblPr.Element(W.Main + "tblW");
        if (tblW is not null)
        {
            var width = tblW.IntAttr("w");
            switch (tblW.Attr("type"))
            {
                case "dxa":
                    properties.WidthTwips = width;
                    break;
                case "pct":
                    // Percentages here are in fiftieths of a percent.
                    if (width is not null)
                        properties.WidthFraction = Units.FiftiethsOfPercentToFraction(width.Value);
                    break;
            }
        }

        properties.IndentTwips = tblPr.Element(W.Main + "tblInd")?.IntAttr("w");
        properties.FixedLayout = tblPr.Element(W.Main + "tblLayout")?.Attr("type") == "fixed";
        properties.Justification = tblPr.Element(W.Main + "jc") is { } jc
            ? ParseJustification(jc.Val())
            : null;

        var borders = tblPr.Element(W.Main + "tblBorders");
        if (borders is not null) ReadBorders(borders, properties.Borders);

        var cellMargins = tblPr.Element(W.Main + "tblCellMar");
        if (cellMargins is not null)
        {
            properties.CellMarginLeftTwips =
                cellMargins.Element(W.Main + "left")?.IntAttr("w") ?? properties.CellMarginLeftTwips;
            properties.CellMarginRightTwips =
                cellMargins.Element(W.Main + "right")?.IntAttr("w") ?? properties.CellMarginRightTwips;
            properties.CellMarginTopTwips =
                cellMargins.Element(W.Main + "top")?.IntAttr("w") ?? properties.CellMarginTopTwips;
            properties.CellMarginBottomTwips =
                cellMargins.Element(W.Main + "bottom")?.IntAttr("w") ?? properties.CellMarginBottomTwips;
        }

        return properties;
    }

    private static TableRow ParseTableRow(XElement rowElement)
    {
        var row = new TableRow();

        var trPr = rowElement.Element(W.Main + "trPr");
        if (trPr is not null)
        {
            var height = trPr.Element(W.Main + "trHeight");
            if (height is not null)
            {
                row.HeightTwips = height.IntAttr("val");
                row.HeightRule = height.Attr("hRule") switch
                {
                    "exact" => RowHeightRule.Exact,
                    "atLeast" => RowHeightRule.AtLeast,
                    // Word omits hRule when it means "at least", which is its usual intent.
                    _ => row.HeightTwips is not null ? RowHeightRule.AtLeast : RowHeightRule.Auto
                };
            }

            row.CantSplit = trPr.Element(W.Main + "cantSplit")?.OnOff() ?? false;
            row.IsHeader = trPr.Element(W.Main + "tblHeader")?.OnOff() ?? false;
        }

        foreach (var cellElement in rowElement.Elements(W.Main + "tc"))
            row.Cells.Add(ParseTableCell(cellElement));

        return row;
    }

    private static TableCell ParseTableCell(XElement cellElement)
    {
        var cell = new TableCell();

        var tcPr = cellElement.Element(W.Main + "tcPr");
        if (tcPr is not null)
        {
            cell.WidthTwips = tcPr.Element(W.Main + "tcW")?.IntAttr("w");
            cell.GridSpan = Math.Max(1, tcPr.Element(W.Main + "gridSpan")?.IntVal() ?? 1);

            var borders = tcPr.Element(W.Main + "tcBorders");
            if (borders is not null) ReadBorders(borders, cell.Borders);

            var shading = tcPr.Element(W.Main + "shd");
            var fill = shading?.Attr("fill");
            cell.ShadingFill = fill is null or "auto" ? null : fill;

            cell.VerticalAlignment = tcPr.Element(W.Main + "vAlign")?.Val() switch
            {
                "center" => VerticalCellAlignment.Center,
                "bottom" => VerticalCellAlignment.Bottom,
                _ => VerticalCellAlignment.Top
            };

            // A vMerge with no value means "continue", which is the common spelling.
            var vMerge = tcPr.Element(W.Main + "vMerge");
            if (vMerge is not null) cell.VerticalMerge = vMerge.Val() ?? "continue";

            var margins = tcPr.Element(W.Main + "tcMar");
            if (margins is not null)
            {
                cell.MarginLeftTwips = margins.Element(W.Main + "left")?.IntAttr("w");
                cell.MarginRightTwips = margins.Element(W.Main + "right")?.IntAttr("w");
                cell.MarginTopTwips = margins.Element(W.Main + "top")?.IntAttr("w");
                cell.MarginBottomTwips = margins.Element(W.Main + "bottom")?.IntAttr("w");
            }
        }

        foreach (var child in cellElement.Elements())
        {
            if (child.Name == W.Main + "p")
                cell.Content.Add(ParseParagraph(child));
            else if (child.Name == W.Main + "tbl")
                cell.Content.Add(ParseTable(child));
        }

        // A cell must contain at least one paragraph; an empty one still occupies a row.
        if (cell.Content.Count == 0) cell.Content.Add(new Paragraph());

        return cell;
    }

    private static void ReadBorders(XElement container, BorderSet target)
    {
        target.Top = ReadBorderEdge(container.Element(W.Main + "top"));
        target.Left = ReadBorderEdge(container.Element(W.Main + "left") ?? container.Element(W.Main + "start"));
        target.Bottom = ReadBorderEdge(container.Element(W.Main + "bottom"));
        target.Right = ReadBorderEdge(container.Element(W.Main + "right") ?? container.Element(W.Main + "end"));
        target.InsideHorizontal = ReadBorderEdge(container.Element(W.Main + "insideH"));
        target.InsideVertical = ReadBorderEdge(container.Element(W.Main + "insideV"));
    }

    private static BorderEdge? ReadBorderEdge(XElement? element)
    {
        if (element is null) return null;

        var color = element.Attr("color");
        return new BorderEdge(
            element.Val() ?? "none",
            element.IntAttr("sz") ?? 0,
            color is null or "auto" ? null : color);
    }

    private static Justification ParseJustification(string? value) => value switch
    {
        "center" => Justification.Center,
        "right" or "end" => Justification.Right,
        "both" => Justification.Both,
        "distribute" => Justification.Distribute,
        _ => Justification.Left
    };

    private static UnderlineStyle ParseUnderline(string? value) => value switch
    {
        null or "none" => UnderlineStyle.None,
        "double" => UnderlineStyle.Double,
        "thick" => UnderlineStyle.Thick,
        "dotted" => UnderlineStyle.Dotted,
        "dash" or "dashed" => UnderlineStyle.Dashed,
        "wave" => UnderlineStyle.Wave,
        "words" => UnderlineStyle.Words,
        _ => UnderlineStyle.Single
    };

    private static TabAlignment ParseTabAlignment(string? value) => value switch
    {
        "center" => TabAlignment.Center,
        "right" or "end" => TabAlignment.Right,
        "decimal" => TabAlignment.Decimal,
        "bar" => TabAlignment.Bar,
        "clear" => TabAlignment.Clear,
        _ => TabAlignment.Left
    };

    private static TabLeader ParseTabLeader(string? value) => value switch
    {
        "dot" => TabLeader.Dot,
        "hyphen" => TabLeader.Hyphen,
        "underscore" => TabLeader.Underscore,
        "middleDot" => TabLeader.MiddleDot,
        _ => TabLeader.None
    };
}
