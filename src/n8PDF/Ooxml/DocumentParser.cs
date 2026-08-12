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

        CollectParagraphContent(element, paragraph);
        return paragraph;
    }

    /// <summary>
    /// Walks a paragraph's children, folding complex fields into single field runs.
    /// </summary>
    /// <remarks>
    /// A complex field is not an element but a sequence: a run carrying "begin", runs holding the
    /// instruction, a "separate", the runs Word last rendered, then "end". Reading them as
    /// ordinary runs would show the cached value but lose the instruction, so the field could
    /// never be recomputed — which is exactly what a page number needs.
    /// </remarks>
    private static void CollectParagraphContent(XElement element, Paragraph paragraph)
    {
        var instruction = new System.Text.StringBuilder();
        var result = new System.Text.StringBuilder();

        // 0 outside a field, 1 collecting the instruction, 2 collecting the cached result.
        var state = 0;
        RunProperties? fieldProperties = null;

        foreach (var child in element.Elements())
        {
            var fieldChar = child.Name == W.Main + "r"
                ? child.Element(W.Main + "fldChar")?.Attr("fldCharType")
                : null;

            switch (fieldChar)
            {
                case "begin":
                    state = 1;
                    instruction.Clear();
                    result.Clear();
                    fieldProperties = child.Element(W.Main + "rPr") is { } begin
                        ? ParseRunProperties(begin)
                        : null;
                    continue;

                case "separate":
                    state = 2;
                    continue;

                case "end":
                {
                    var run = new Run();
                    if (fieldProperties is not null) run.Properties = fieldProperties;
                    run.Content.Add(new FieldInline(instruction.ToString(), result.ToString()));
                    paragraph.Runs.Add(run);

                    state = 0;
                    continue;
                }
            }

            if (state == 1)
            {
                instruction.Append(string.Concat(child.Descendants(W.Main + "instrText").Select(t => t.Value)));
                continue;
            }

            if (state == 2)
            {
                // The result's own formatting is taken from the first run that carries any.
                if (fieldProperties is null && child.Element(W.Main + "rPr") is { } resultProperties)
                    fieldProperties = ParseRunProperties(resultProperties);

                result.Append(string.Concat(child.Descendants(W.Main + "t").Select(t => t.Value)));
                continue;
            }

            CollectRuns(child, paragraph);
        }
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

        // A simple field holds its instruction in an attribute and the value Word last computed
        // in the runs inside it. Skipping the element would drop that cached value entirely.
        if (element.Name == W.Main + "fldSimple")
        {
            var instruction = element.Attr("instr") ?? string.Empty;
            var cached = string.Concat(element.Descendants(W.Main + "t").Select(t => t.Value));

            var run = new Run();
            var firstRun = element.Element(W.Main + "r");
            if (firstRun?.Element(W.Main + "rPr") is { } rPr) run.Properties = ParseRunProperties(rPr);

            run.Content.Add(new FieldInline(instruction, cached));
            paragraph.Runs.Add(run);
            return;
        }

        // A bookmark marks a place an internal link can reach. It has no content of its own, so
        // it is recorded as a zero-width marker on a run of its own.
        if (element.Name == W.Main + "bookmarkStart")
        {
            var name = element.Attr("name");

            // Word brackets every document with a bookmark named _GoBack that means nothing here.
            if (name is not null && name != "_GoBack")
            {
                var marker = new Run();
                marker.Content.Add(new BookmarkInline(name));
                paragraph.Runs.Add(marker);
            }

            return;
        }

        if (element.Name == W.Main + "hyperlink")
        {
            var target = new HyperlinkTarget(
                element.Attribute(W.Relationships + "id")?.Value,
                element.Attr("anchor"));

            var first = paragraph.Runs.Count;
            foreach (var child in element.Elements())
                CollectRuns(child, paragraph);

            // Everything the element contained belongs to the link.
            for (var i = first; i < paragraph.Runs.Count; i++)
                paragraph.Runs[i].Hyperlink = target;

            return;
        }

        if (element.Name == W.Main + "ins" ||
            element.Name == W.Main + "smartTag" ||
            element.Name == W.Main + "sdtContent")
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
                if (ParseDrawing(child) is { } drawing) run.Content.Add(drawing);
            }
            else if (child.Name == W.Main + "footnoteReference" || child.Name == W.Main + "endnoteReference")
            {
                var kind = child.Name == W.Main + "footnoteReference" ? NoteKind.Footnote : NoteKind.Endnote;
                if (int.TryParse(child.Attr("id"), out var id))
                    run.Content.Add(new NoteReferenceInline(id, kind));
            }
            else if (child.Name == W.Main + "footnoteRef")
            {
                run.Content.Add(new NoteMarkInline(NoteKind.Footnote));
            }
            else if (child.Name == W.Main + "endnoteRef")
            {
                run.Content.Add(new NoteMarkInline(NoteKind.Endnote));
            }
            else if (child.Name == W.Main + "separator" || child.Name == W.Main + "continuationSeparator")
            {
                run.Content.Add(new SeparatorInline());
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
    /// Reads a notes part — footnotes or endnotes — into notes keyed by id.
    /// </summary>
    /// <remarks>
    /// The separators come through as notes too, because that is how the format stores them: the
    /// rule Word draws above the notes is a note whose body holds a <c>w:separator</c>. Keeping
    /// them means the space they occupy is measured from the document rather than assumed.
    /// </remarks>
    public static Dictionary<int, Note> ParseNotes(XDocument xml, NoteKind kind)
    {
        var name = W.Main + (kind == NoteKind.Footnote ? "footnote" : "endnote");
        var result = new Dictionary<int, Note>();

        foreach (var element in xml.Root?.Elements(name) ?? [])
        {
            if (!int.TryParse(element.Attr("id"), out var id)) continue;

            var note = new Note(id, element.Attr("type") ?? "normal");

            foreach (var child in element.Elements())
            {
                if (child.Name == W.Main + "p") note.Body.Add(ParseParagraph(child));
                else if (child.Name == W.Main + "tbl") note.Body.Add(ParseTable(child));
            }

            result[id] = note;
        }

        return result;
    }

    /// <summary>
    /// Reads the number format from a <c>w:footnotePr</c> or <c>w:endnotePr</c>, wherever it
    /// appears: a section carries one, and so does the document's settings part.
    /// </summary>
    public static NumberFormat? ReadNoteNumberFormat(XElement? container, NoteKind kind)
    {
        var name = W.Main + (kind == NoteKind.Footnote ? "footnotePr" : "endnotePr");
        var value = container?.Element(name)?.Element(W.Main + "numFmt")?.Attr("val");

        return value is null ? null : NumberingParser.ParseNumberFormat(value);
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

    /// <summary>
    /// Reads a <c>w:drawing</c>, which holds either an inline picture or an anchored one.
    /// </summary>
    private static InlineElement? ParseDrawing(XElement element)
    {
        var anchor = element.Element(W.WordDrawing + "anchor");
        if (anchor is not null) return ParseAnchoredDrawing(anchor);

        var inline = element.Element(W.WordDrawing + "inline") ?? element;

        var (width, height) = ReadExtent(inline);
        if (width <= 0 || height <= 0) return null;

        return new DrawingInline(width, height, ReadEmbeddedRelationship(inline));
    }

    private static AnchoredDrawing? ParseAnchoredDrawing(XElement anchor)
    {
        var (width, height) = ReadExtent(anchor);
        if (width <= 0 || height <= 0) return null;

        var positionH = anchor.Element(W.WordDrawing + "positionH");
        var positionV = anchor.Element(W.WordDrawing + "positionV");

        // Exactly one of wrapNone, wrapSquare, wrapTight, wrapThrough and wrapTopAndBottom is
        // present. Tight and through follow a polygon; both are approximated by the bounding box,
        // which is what wrapSquare does.
        var wrap = TextWrapMode.Square;
        if (anchor.Element(W.WordDrawing + "wrapNone") is not null) wrap = TextWrapMode.None;
        else if (anchor.Element(W.WordDrawing + "wrapTopAndBottom") is not null) wrap = TextWrapMode.TopAndBottom;

        return new AnchoredDrawing
        {
            WidthEmu = width,
            HeightEmu = height,
            RelationshipId = ReadEmbeddedRelationship(anchor),
            Wrap = wrap,
            BehindText = anchor.Attribute("behindDoc")?.Value is "1" or "true",

            HorizontalFrom = positionH?.Attribute("relativeFrom")?.Value switch
            {
                "margin" => HorizontalAnchor.Margin,
                "page" => HorizontalAnchor.Page,
                "character" => HorizontalAnchor.Character,
                "leftMargin" => HorizontalAnchor.LeftMargin,
                "rightMargin" => HorizontalAnchor.RightMargin,
                _ => HorizontalAnchor.Column
            },
            HorizontalOffsetEmu = ReadOffset(positionH),
            HorizontalAlign = positionH?.Element(W.WordDrawing + "align")?.Value.Trim(),

            VerticalFrom = positionV?.Attribute("relativeFrom")?.Value switch
            {
                "line" => VerticalAnchor.Line,
                "margin" => VerticalAnchor.Margin,
                "page" => VerticalAnchor.Page,
                "topMargin" => VerticalAnchor.TopMargin,
                "bottomMargin" => VerticalAnchor.BottomMargin,
                _ => VerticalAnchor.Paragraph
            },
            VerticalOffsetEmu = ReadOffset(positionV),
            VerticalAlign = positionV?.Element(W.WordDrawing + "align")?.Value.Trim(),

            DistanceLeftEmu = ReadDistance(anchor, "distL"),
            DistanceRightEmu = ReadDistance(anchor, "distR"),
            DistanceTopEmu = ReadDistance(anchor, "distT"),
            DistanceBottomEmu = ReadDistance(anchor, "distB")
        };
    }

    private static (long Width, long Height) ReadExtent(XElement container)
    {
        var extent = container.Element(W.WordDrawing + "extent")
                     ?? container.Descendants(W.WordDrawing + "extent").FirstOrDefault()
                     ?? container.Descendants(W.Drawing + "ext").FirstOrDefault();
        if (extent is null) return (0, 0);

        long.TryParse(extent.Attribute("cx")?.Value, out var width);
        long.TryParse(extent.Attribute("cy")?.Value, out var height);
        return (width, height);
    }

    private static string? ReadEmbeddedRelationship(XElement container) =>
        container.Descendants(W.Drawing + "blip").FirstOrDefault()
            ?.Attribute(W.Relationships + "embed")?.Value;

    private static long? ReadOffset(XElement? position)
    {
        var text = position?.Element(W.WordDrawing + "posOffset")?.Value;
        return long.TryParse(text, out var value) ? value : null;
    }

    private static long ReadDistance(XElement anchor, string name) =>
        long.TryParse(anchor.Attribute(name)?.Value, out var value) ? value : 0;

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
                case "kern":
                    properties.KerningMinimumHalfPoints = element.IntVal();
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

        section.BreakType = sectPr.Element(W.Main + "type")?.Attr("val") switch
        {
            "continuous" => SectionBreakType.Continuous,
            "evenPage" => SectionBreakType.EvenPage,
            "oddPage" => SectionBreakType.OddPage,
            "nextColumn" => SectionBreakType.NextColumn,
            _ => SectionBreakType.NextPage
        };

        section.FootnoteNumberFormat = ReadNoteNumberFormat(sectPr, NoteKind.Footnote);
        section.EndnoteNumberFormat = ReadNoteNumberFormat(sectPr, NoteKind.Endnote);

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

        foreach (var reference in sectPr.Elements(W.Main + "headerReference"))
        {
            var id = reference.Attribute(W.Relationships + "id")?.Value;
            if (id is not null) section.HeaderReferences[reference.Attr("type") ?? "default"] = id;
        }

        foreach (var reference in sectPr.Elements(W.Main + "footerReference"))
        {
            var id = reference.Attribute(W.Relationships + "id")?.Value;
            if (id is not null) section.FooterReferences[reference.Attr("type") ?? "default"] = id;
        }

        section.TitlePage = sectPr.Element(W.Main + "titlePg")?.OnOff() ?? false;

        var cols = sectPr.Element(W.Main + "cols");
        if (cols is not null)
        {
            section.ColumnCount = Math.Max(1, cols.IntAttr("num") ?? 1);
            section.ColumnSpaceTwips = cols.IntAttr("space") ?? section.ColumnSpaceTwips;
            section.ColumnSeparator = ReadOnOff(cols.Attr("sep"));

            // Stated widths only count when the document turns even division off, which is how
            // Word writes unequal columns; a stray w:col otherwise is not what the layout uses.
            if (!ReadOnOff(cols.Attr("equalWidth"), defaultValue: true))
            {
                foreach (var col in cols.Elements(W.Main + "col"))
                {
                    section.ColumnWidths.Add(
                        (col.IntAttr("w") ?? 0, col.IntAttr("space") ?? section.ColumnSpaceTwips));
                }
            }
        }

        // Word writes top and bottom margins as negative values when they mean "at least this
        // far", which would otherwise produce a text area taller than the page.
        section.MarginTopTwips = Math.Abs(section.MarginTopTwips);
        section.MarginBottomTwips = Math.Abs(section.MarginBottomTwips);

        return section;
    }

    public static Table ParseTable(XElement element)
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

    /// <summary>
    /// Reads an ST_OnOff attribute. Its absence means the default, and "0", "false" and "off" all
    /// mean off — an attribute present with any other value, or with none, means on.
    /// </summary>
    private static bool ReadOnOff(string? value, bool defaultValue = false) => value switch
    {
        null => defaultValue,
        "0" or "false" or "off" => false,
        _ => true
    };

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
        // Word draws a heavy leader with the same underscore glyph as a plain one; its export
        // shows the two producing identical runs.
        "underscore" or "heavy" => TabLeader.Underscore,
        "middleDot" => TabLeader.MiddleDot,
        _ => TabLeader.None
    };
}
