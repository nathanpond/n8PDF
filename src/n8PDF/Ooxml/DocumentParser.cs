using System.Globalization;
using System.Xml.Linq;

namespace n8PDF.Ooxml;

/// <summary>
/// Turns <c>word/document.xml</c> into the document model. Property elements are read verbatim:
/// resolving what they mean against the style hierarchy is the Styling layer's job.
/// </summary>
internal static class DocumentParser
{
    public static WordDocument Parse(XDocument xml)
    {
        var document = new WordDocument();
        var body = xml.Root?.Element(W.Main + "body");
        if (body is null) return document;

        // Fields run from paragraph to paragraph, so what is open carries down the body.
        var scope = new FieldScope();

        foreach (var element in Blocks(body))
        {
            if (element.Name == W.Main + "p")
                document.Body.Add(ParseParagraph(element, scope));
            else if (element.Name == W.Main + "tbl")
                document.Body.Add(ParseTable(element));
            else if (element.Name == W.Main + "sectPr")
                document.Section = ParseSection(element);
        }

        FoldAdjacentTables(document.Body);
        return document;
    }

    /// <summary>Reads <c>w:ruby</c>: a phonetic guide and the word it stands over.</summary>
    private static RubyInline ParseRuby(XElement element)
    {
        var ruby = new RubyInline();
        var properties = element.Element(W.Main + "rubyPr");

        ruby.Alignment = properties?.Element(W.Main + "rubyAlign")?.Val() switch
        {
            "left" => RubyAlignment.Left,
            "right" => RubyAlignment.Right,
            "distributeLetter" => RubyAlignment.DistributeLetter,
            "distributeSpace" => RubyAlignment.DistributeSpace,
            _ => RubyAlignment.Center
        };

        ruby.GuideHalfPoints = properties?.Element(W.Main + "hps")?.IntVal();
        ruby.RaiseHalfPoints = properties?.Element(W.Main + "hpsRaise")?.IntVal();

        foreach (var run in element.Element(W.Main + "rt")?.Elements(W.Main + "r") ?? [])
            ruby.Guide.Add(ParseRun(run));

        foreach (var run in element.Element(W.Main + "rubyBase")?.Elements(W.Main + "r") ?? [])
            ruby.Base.Add(ParseRun(run));

        return ruby;
    }

    /// <summary>
    /// Folds tables written one after the other into one, which is how Word reads them.
    /// </summary>
    /// <remarks>
    /// Two <c>w:tbl</c> elements with nothing between them are one table to Word, and a document
    /// that means two must put a paragraph between them. adjacent-tables-probe shows what that
    /// costs: two touching tables come out with one line round the pair rather than two, no thick
    /// edge where they meet, and no space between them either.
    ///
    /// What the second table said about itself is not thrown away with it. Its rows keep the
    /// columns and the indent they were written with — the probe's second table names its columns
    /// the other way round and Word keeps them that way, and a second table asking to be indented
    /// is indented while the first is not.
    /// </remarks>
    private static void FoldAdjacentTables(List<BlockElement> blocks)
    {
        for (var i = blocks.Count - 1; i > 0; i--)
        {
            if (blocks[i] is not Table second || blocks[i - 1] is not Table first) continue;

            var grid = second.Grid.SequenceEqual(first.Grid) ? null : second.Grid;

            // Every row of a merged table carries the indent of the table it was written in, the
            // first table's rows included: Word draws the merged table at the first table's own
            // indent and then indents each row again by whatever its own table asked for, so a
            // first table asking for half an inch has its rows an inch in. Measured on the eighth
            // page of merged-indent-probe, which is the one page where the first table is the
            // indented one.
            foreach (var row in first.Rows) row.IndentTwips ??= first.Properties.IndentTwips ?? 0;

            foreach (var row in second.Rows)
            {
                row.Grid ??= grid;
                row.IndentTwips ??= second.Properties.IndentTwips ?? 0;
                first.Rows.Add(row);
            }

            blocks.RemoveAt(i);
        }
    }

    /// <summary>
    /// The blocks a container holds, with whatever wraps them unwrapped.
    /// </summary>
    /// <remarks>
    /// A body, a cell, a running head and a note all hold paragraphs and tables — and all of them
    /// may hold those inside something else again. A content control wraps the cover page, the
    /// table of contents and every placeholder a template leaves to be filled in; a compatibility
    /// alternative wraps content offered twice over; the custom XML element wraps whatever an old
    /// document tagged. None of the three is a paragraph or a table, and a walk that looks only
    /// for those two loses everything inside them without saying so.
    ///
    /// What Word does with each is measured in content-controls, which puts one of every kind on a
    /// page and names the line inside it. Word draws all of them, in place, with no more room
    /// between the lines than any other paragraph gets — and where an alternative offers two
    /// branches it draws the choice rather than the fallback, which is the one thing here that
    /// could have gone either way.
    ///
    /// Anything else is passed through untouched, so that a body's <c>w:sectPr</c> still reaches
    /// the reader that wants it.
    /// </remarks>
    public static IEnumerable<XElement> Blocks(XElement container, int depth = 0)
    {
        // A wrapper may hold a wrapper, and a document written to be awkward may hold thousands.
        // Sixteen deep is far past anything Word writes and stops a walk that would not end.
        if (depth > 16)
        {
            yield break;
        }

        foreach (var child in container.Elements())
        {
            var inner = Unwrapped(child);

            if (inner is null)
            {
                yield return child;
                continue;
            }

            foreach (var block in Blocks(inner, depth + 1)) yield return block;
        }
    }

    /// <summary>
    /// What a wrapper holds, or null where the element is not one.
    /// </summary>
    private static XElement? Unwrapped(XElement element)
    {
        if (element.Name == W.Main + "sdt") return element.Element(W.Main + "sdtContent");

        if (element.Name == W.Main + "customXml") return element;

        // The choice rather than the fallback: Word draws the choice of the pair on the seventh
        // page of content-controls, and what the two hold there is different words so as to say
        // which. A run-level alternative is chosen differently — see Preferred — because there the
        // choice may be a drawing this cannot read, where here it is paragraphs either way.
        if (element.Name == W.Compatibility + "AlternateContent")
        {
            return element.Element(W.Compatibility + "Choice")
                   ?? element.Element(W.Compatibility + "Fallback");
        }

        return null;
    }

    public static Paragraph ParseParagraph(XElement element, FieldScope? scope = null)
    {
        var paragraph = new Paragraph();

        var pPr = element.Element(W.Main + "pPr");
        if (pPr is not null)
        {
            paragraph.Properties = ParseParagraphProperties(pPr);

            var sectPr = pPr.Element(W.Main + "sectPr");
            if (sectPr is not null) paragraph.SectionBreak = ParseSection(sectPr);
        }

        CollectParagraphContent(element, paragraph, scope);

        // An equation on a line of its own is centred, and says so in its own properties rather
        // than the paragraph's: a paragraph holding nothing but an m:oMathPara has no w:jc at all
        // and Word still centres it. What the equation says wins where it says anything.
        if (element.Element(OfficeMath.Main + "oMathPara") is { } display)
        {
            paragraph.Properties.Justification =
                display.Element(OfficeMath.Main + "oMathParaPr")?
                    .Element(OfficeMath.Main + "jc")?.Attribute(OfficeMath.Main + "val")?.Value switch
                    {
                        "left" => Justification.Left,
                        "right" => Justification.Right,
                        _ => Justification.Center
                    };
        }

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
    ///
    /// Fields nest, and they do not have to end in the paragraph they begin in. A table of
    /// contents is both at once: it runs from the first of its entries to the last, and each of
    /// those entries holds a page reference of its own. One that runs past the end of its
    /// paragraph keeps the runs it has collected as ordinary content — that is the result Word
    /// last computed, and it is what a reader sees where nothing here can work the field out —
    /// and the paragraphs that carry on with it are marked as belonging to it.
    /// </remarks>
    private static void CollectParagraphContent(XElement element, Paragraph paragraph, FieldScope? scope)
    {
        // Fields begun in this paragraph and not yet ended, innermost last.
        var open = new Stack<FieldBuilder>();

        // A field begun in an earlier paragraph is still open here, which makes this paragraph
        // part of what it produced rather than content of its own.
        if (scope is { IsOpen: true }) paragraph.InsideField = true;

        foreach (var child in element.Elements())
        {
            var fieldChar = child.Name == W.Main + "r"
                ? child.Element(W.Main + "fldChar")?.Attr("fldCharType")
                : null;

            switch (fieldChar)
            {
                case "begin":
                    open.Push(new FieldBuilder
                    {
                        Properties = child.Element(W.Main + "rPr") is { } begin
                            ? ParseRunProperties(begin)
                            : null,
                        CheckBox = ReadCheckBox(child.Element(W.Main + "fldChar"))
                    });
                    continue;

                case "separate":
                    if (open.Count > 0) open.Peek().InResult = true;
                    continue;

                case "end":
                    if (open.Count > 0) Close(open, paragraph);
                    else scope?.Close();

                    continue;
            }

            if (open.Count > 0 && !open.Peek().InResult)
            {
                open.Peek().Instruction.Append(
                    string.Concat(child.Descendants(W.Main + "instrText").Select(t => t.Value)));

                continue;
            }

            CollectRuns(child, open.Count > 0 ? open.Peek().Result : paragraph.Runs);
        }

        // What is still open runs on past this paragraph. Its instruction is known, so it is
        // recorded where it began, and what it has gathered so far follows as ordinary runs.
        foreach (var unclosed in open.Reverse())
        {
            var run = new Run();
            if (unclosed.Properties is not null) run.Properties = unclosed.Properties;

            var field = new FieldInline(unclosed.Instruction.ToString(), string.Empty);
            run.Content.Add(field);

            paragraph.Runs.Add(run);
            paragraph.Runs.AddRange(unclosed.Result);

            paragraph.OpensField ??= field;
            scope?.Open();
        }
    }

    /// <summary>
    /// Ends the innermost open field, folding it into a single run of the context around it —
    /// which is the paragraph, or the field that field is nested inside.
    /// </summary>
    private static void Close(Stack<FieldBuilder> open, Paragraph paragraph)
    {
        var finished = open.Pop();

        var run = new Run();
        var properties = finished.Properties ??
                         finished.Result.FirstOrDefault(r => r.Properties is not null)?.Properties;

        if (properties is not null) run.Properties = properties;

        run.Content.Add(new FieldInline(
            finished.Instruction.ToString(),
            string.Concat(finished.Result.Select(r => r.GetText())))
        {
            CheckBox = finished.CheckBox
        });

        (open.Count > 0 ? open.Peek().Result : paragraph.Runs).Add(run);
    }

    /// <summary>
    /// Reads the box a form field draws, from the field data carried on its opening character.
    /// </summary>
    /// <remarks>
    /// Which state it is in is <c>w:checked</c> where the field says, and <c>w:default</c> where
    /// it does not: a box nobody has touched stands at whatever it was made with. Both are on-off
    /// values, so a bare element means ticked.
    /// </remarks>
    private static CheckBox? ReadCheckBox(XElement? fieldChar)
    {
        if (fieldChar?.Element(W.Main + "ffData")?.Element(W.Main + "checkBox") is not { } box)
            return null;

        var ticked = box.Element(W.Main + "checked")?.OnOff()
                     ?? box.Element(W.Main + "default")?.OnOff()
                     ?? false;

        return new CheckBox(ticked, box.Element(W.Main + "size")?.IntVal());
    }

    /// <summary>A field being read: its instruction, and the runs of the result after it.</summary>
    private sealed class FieldBuilder
    {
        public System.Text.StringBuilder Instruction { get; } = new();

        public List<Run> Result { get; } = [];

        public RunProperties? Properties { get; init; }

        /// <summary>The box this field draws, where it is a checkbox rather than a field of text.</summary>
        public CheckBox? CheckBox { get; init; }

        /// <summary>True once the "separate" has been passed and the result has begun.</summary>
        public bool InResult { get; set; }
    }

    /// <summary>
    /// Collects runs from a paragraph child. Hyperlinks and revision-tracking containers wrap
    /// runs rather than replacing them, so their contents are pulled up rather than skipped —
    /// otherwise the text inside a tracked insertion would silently vanish.
    /// </summary>
    private static void CollectRuns(XElement element, List<Run> runs)
    {
        if (element.Name == W.Main + "r")
        {
            runs.Add(ParseRun(element));
            return;
        }

        // An equation, which is markup of its own in a namespace of its own. It is read into a
        // tree here and set later; what matters at this point is that it is not passed over.
        if (element.Name == OfficeMath.Main + "oMath" ||
            element.Name == OfficeMath.Main + "oMathPara")
        {
            var display = element.Name == OfficeMath.Main + "oMathPara";

            foreach (var math in display
                         ? element.Elements(OfficeMath.Main + "oMath")
                         : [element])
            {
                var run = new Run();
                run.Content.Add(new MathInline(OfficeMath.Parse(math), display));
                runs.Add(run);
            }

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
            runs.Add(run);
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
                marker.Content.Add(new BookmarkInline(name, element.IntAttr("id") ?? 0));
                runs.Add(marker);
            }

            return;
        }

        // The end of a bookmark, which names no bookmark of its own: it is matched to its start
        // by number. What lies between the two is the text a REF field shows.
        if (element.Name == W.Main + "bookmarkEnd")
        {
            var marker = new Run();
            marker.Content.Add(new BookmarkEndInline(element.IntAttr("id") ?? 0));
            runs.Add(marker);
            return;
        }


        if (element.Name == W.Main + "hyperlink")
        {
            var target = new HyperlinkTarget(
                element.Attribute(W.Relationships + "id")?.Value,
                element.Attr("anchor"));

            var first = runs.Count;
            foreach (var child in element.Elements())
                CollectRuns(child, runs);

            // Everything the element contained belongs to the link.
            for (var i = first; i < runs.Count; i++)
                runs[i].Hyperlink = target;

            return;
        }

        if (element.Name == W.Main + "ins" ||
            element.Name == W.Main + "smartTag" ||
            element.Name == W.Main + "sdtContent")
        {
            foreach (var child in element.Elements())
                CollectRuns(child, runs);
            return;
        }

        // Structured document tags wrap their content one level deeper.
        if (element.Name == W.Main + "sdt")
        {
            var content = element.Element(W.Main + "sdtContent");
            if (content is not null)
            {
                foreach (var child in content.Elements())
                    CollectRuns(child, runs);
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
            else if (child.Name == W.Main + "ruby")
            {
                run.Content.Add(ParseRuby(child));
            }
            else if (child.Name == W.Main + "drawing")
            {
                if (ParseDrawing(child) is { } drawing) run.Content.Add(drawing);
            }
            else if (child.Name == W.Main + "pict")
            {
                // The older spelling of a shape, which is what Word wrote before 2007 and still
                // writes for a watermark.
                if (Vml.ParsePicture(child) is { } picture) run.Content.Add(picture);
            }
            else if (child.Name == W.Compatibility + "AlternateContent")
            {
                // The same drawing written twice over, for readers of different ages. The first
                // choice is the one meant for a reader that understands it, and the fallback the
                // older spelling of the same thing — so taking the choice and ignoring the
                // fallback draws it once rather than twice.
                foreach (var alternative in Preferred(child).Elements())
                {
                    if (alternative.Name == W.Main + "drawing" && ParseDrawing(alternative) is { } drawing)
                        run.Content.Add(drawing);
                    else if (alternative.Name == W.Main + "pict" && Vml.ParsePicture(alternative) is { } picture)
                        run.Content.Add(picture);
                }
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
                run.Content.Add(new SeparatorInline(child.Name == W.Main + "continuationSeparator"));
            }
            else if (child.Name == W.Main + "noBreakHyphen")
            {
                run.Content.Add(new TextInline("‑"));
            }
            else if (child.Name == W.Main + "softHyphen")
            {
                run.Content.Add(new TextInline("­"));
            }
            else if (child.Name == W.Main + "sym")
            {
                ReadSymbol(run, child);
            }
        }

        return run;
    }

    /// <summary>
    /// Reads a <c>w:sym</c>: a character named by its code, in a face named beside it.
    /// </summary>
    /// <remarks>
    /// The code is written in the private-use block the symbol faces keep their glyphs in — the
    /// tick of Wingdings is F0FC — and Word's own export strips the block back off again, writing
    /// the character as 00FC. The <c>symbols</c> fixture shows it does the same with a code that
    /// never had the block on it: F0FC and 00FC come out as the same character in the same face,
    /// so both are read the same way here.
    ///
    /// A face is not required. Where the element names none, the run's own is meant.
    /// </remarks>
    private static void ReadSymbol(Run run, XElement element)
    {
        var code = element.Attribute(W.Main + "char")?.Value;

        if (code is not { Length: > 0 } ||
            !int.TryParse(code, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return;
        }

        var character = (char)((value & 0xff00) == 0xf000 ? value & 0xff : value);
        var font = element.Attribute(W.Main + "font")?.Value;

        run.Content.Add(new SymbolInline(character.ToString(), string.IsNullOrEmpty(font) ? null : font));
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

            foreach (var child in Blocks(element))
            {
                if (child.Name == W.Main + "p") note.Body.Add(ParseParagraph(child));
                else if (child.Name == W.Main + "tbl") note.Body.Add(ParseTable(child));
            }

            FoldAdjacentTables(note.Body);
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

    /// <summary>Reads where the endnotes are gathered, from <c>w:pos</c>.</summary>
    public static EndnotePosition? ReadEndnotePosition(XElement? container)
    {
        return container?.Element(W.Main + "endnotePr")?.Element(W.Main + "pos")?.Attr("val") switch
        {
            "sectEnd" => EndnotePosition.SectionEnd,
            "docEnd" => EndnotePosition.DocumentEnd,
            _ => null
        };
    }

    /// <summary>Reads where a page's footnotes are set, from <c>w:pos</c>.</summary>
    public static NotePosition? ReadNotePosition(XElement? container)
    {
        return container?.Element(W.Main + "footnotePr")?.Element(W.Main + "pos")?.Attr("val") switch
        {
            "beneathText" => NotePosition.BeneathText,
            "pageBottom" => NotePosition.PageBottom,
            _ => null
        };
    }

    /// <summary>
    /// Reads whether the notes are numbered again from the beginning on every page or in every
    /// section, from <c>w:numRestart</c>.
    /// </summary>
    public static NoteNumberRestart? ReadNoteNumberRestart(XElement? container, NoteKind kind)
    {
        var name = W.Main + (kind == NoteKind.Footnote ? "footnotePr" : "endnotePr");

        return container?.Element(name)?.Element(W.Main + "numRestart")?.Attr("val") switch
        {
            "eachPage" => NoteNumberRestart.EachPage,
            "eachSect" => NoteNumberRestart.EachSection,
            "continuous" => NoteNumberRestart.Continuous,
            _ => null
        };
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
    /// The branch of an <c>mc:AlternateContent</c> to read: the first choice where there is one,
    /// and the fallback otherwise.
    /// </summary>
    /// <remarks>
    /// Which choices a reader can take is stated as the namespaces it must understand, and the
    /// only one Word writes for a shape is its own <c>wps</c>, which is the one read here. A
    /// document offering something else falls back, which is what the fallback is for.
    /// </remarks>
    private static XElement Preferred(XElement alternateContent)
    {
        foreach (var choice in alternateContent.Elements(W.Compatibility + "Choice"))
        {
            var requires = choice.Attribute("Requires")?.Value ?? string.Empty;

            if (requires.Split(' ').All(prefix => prefix is "wps" or "wpg" or "wp14" or ""))
                return choice;
        }

        return alternateContent.Element(W.Compatibility + "Fallback") ?? alternateContent;
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

        return new DrawingInline(width, height, ReadEmbeddedRelationship(inline))
        {
            Shape = ReadShape(inline),
            DiagramRelationshipId = ReadDiagram(inline),
            ChartRelationshipId = ReadChart(inline)
        };
    }

    /// <summary>
    /// Reads the shape a drawing frame holds, or null where it holds a picture instead.
    /// </summary>
    /// <remarks>
    /// A shape and a picture arrive the same way — both are a <c>a:graphicData</c> inside the same
    /// frame — and what tells them apart is what is inside it. A shape's own size is taken from
    /// the frame rather than from its <c>a:xfrm</c>: the two agree in everything Word writes, and
    /// the frame is what the text around it was laid out against.
    /// </remarks>
    private static ShapeFrame? ReadShape(XElement container)
    {
        var wsp = container.Descendants(W.Shape + "wsp").FirstOrDefault();
        if (wsp is null) return null;

        var shape = new ShapeFrame();

        if (wsp.Descendants(W.Shape + "spPr").FirstOrDefault() is { } spPr)
        {
            shape.Geometry = spPr.Element(W.Drawing + "prstGeom")?.Attribute("prst")?.Value ?? "rect";
            shape.Fill = ReadDrawingFill(spPr);

            if (spPr.Element(W.Drawing + "ln") is { } line)
            {
                shape.Line = ReadDrawingFill(line);

                if (line.Attribute("w")?.Value is { } width &&
                    long.TryParse(width, out var emu) && emu > 0)
                {
                    shape.LineWidthPoints = Units.EmuToPoints(emu);
                }
            }
        }

        if (wsp.Descendants(W.Shape + "bodyPr").FirstOrDefault() is { } bodyPr)
        {
            shape.InsetLeftPoints = Inset(bodyPr, "lIns", shape.InsetLeftPoints);
            shape.InsetTopPoints = Inset(bodyPr, "tIns", shape.InsetTopPoints);
            shape.InsetRightPoints = Inset(bodyPr, "rIns", shape.InsetRightPoints);
            shape.InsetBottomPoints = Inset(bodyPr, "bIns", shape.InsetBottomPoints);

            shape.Anchor = bodyPr.Attribute("anchor")?.Value switch
            {
                "ctr" => ShapeTextAnchor.Center,
                "b" => ShapeTextAnchor.Bottom,
                _ => ShapeTextAnchor.Top
            };
        }

        var content = wsp.Descendants(W.Main + "txbxContent").FirstOrDefault();
        if (content is not null)
        {
            foreach (var child in Blocks(content))
            {
                if (child.Name == W.Main + "p") shape.Content.Add(ParseParagraph(child));
                else if (child.Name == W.Main + "tbl") shape.Content.Add(ParseTable(child));
            }
        }

        FoldAdjacentTables(shape.Content);
        return shape;

        static double Inset(XElement bodyPr, string name, double fallback) =>
            bodyPr.Attribute(name)?.Value is { } value && long.TryParse(value, out var emu)
                ? Units.EmuToPoints(emu)
                : fallback;
    }

    /// <summary>
    /// The relationship a diagram's data is reached by, where the frame holds a diagram.
    /// </summary>
    /// <remarks>
    /// Four parts describe a diagram and the frame names all four; this is the one that matters,
    /// since the arrangement to be drawn is a part beside it rather than one the frame names.
    /// </remarks>
    private static string? ReadDiagram(XElement container) =>
        container.Descendants(W.Diagram + "relIds").FirstOrDefault()
            ?.Attribute(W.Relationships + "dm")?.Value;

    /// <summary>The relationship a chart's own part is reached by, where the frame holds one.</summary>
    private static string? ReadChart(XElement container) =>
        container.Descendants(ChartReader.Main + "chart").FirstOrDefault()
            ?.Attribute(W.Relationships + "id")?.Value;

    /// <summary>
    /// The colour something is painted in: a literal one, a theme slot, or nothing at all where
    /// it declares <c>a:noFill</c>.
    /// </summary>
    /// <remarks>
    /// An element saying nothing about its fill is not the same as one saying it has none — the
    /// first inherits from the theme's format scheme, which is not read here, and the second is
    /// unpainted. Both come back as null, so a shape that leaves its fill unstated is drawn
    /// unfilled rather than in a colour guessed for it.
    /// </remarks>
    private static DrawingColorReference? ReadDrawingFill(XElement container)
    {
        var solid = container.Element(W.Drawing + "solidFill");
        if (solid is null) return null;

        if (solid.Element(W.Drawing + "srgbClr")?.Attribute("val")?.Value is { } hex)
            return new DrawingColorReference(hex, null);

        if (solid.Element(W.Drawing + "schemeClr")?.Attribute("val")?.Value is { } slot)
            return new DrawingColorReference(null, slot);

        return null;
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
            Shape = ReadShape(anchor),
            DiagramRelationshipId = ReadDiagram(anchor),
            ChartRelationshipId = ReadChart(anchor),
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
                case "position":
                    properties.PositionHalfPoints = element.IntVal();
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
                case "bidi":
                    properties.RightToLeft = element.OnOff();
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

                case "framePr":
                    properties.Frame = ReadFrame(element);
                    break;

                case "suppressAutoHyphens":
                    properties.SuppressAutoHyphens = element.OnOff();
                    break;

                case "suppressLineNumbers":
                    properties.SuppressLineNumbers = element.OnOff();
                    break;

                case "pageBreakBefore":
                    properties.PageBreakBefore = element.OnOff();
                    break;
                case "widowControl":
                    properties.WidowControl = element.OnOff();
                    break;
                case "outlineLvl":
                    properties.OutlineLevel = element.IntVal();
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
        section.FootnotePosition = ReadNotePosition(sectPr);
        section.PageNumberStart = sectPr.Element(W.Main + "pgNumType")?.IntAttr("start");
        section.FootnoteNumberRestart = ReadNoteNumberRestart(sectPr, NoteKind.Footnote);
        section.EndnoteNumberRestart = ReadNoteNumberRestart(sectPr, NoteKind.Endnote);

        section.PageBorders = ReadPageBorders(sectPr.Element(W.Main + "pgBorders"));
        section.LineNumbers = ReadLineNumbering(sectPr.Element(W.Main + "lnNumType"));

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

        section.VerticalAlignment = sectPr.Element(W.Main + "vAlign")?.Attr("val") switch
        {
            "center" => VerticalPageAlignment.Center,
            "bottom" => VerticalPageAlignment.Bottom,
            "both" => VerticalPageAlignment.Both,
            _ => VerticalPageAlignment.Top
        };

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

        properties.StyleId = tblPr.Element(W.Main + "tblStyle")?.Val();
        properties.RowBandSize = Math.Max(1, tblPr.Element(W.Main + "tblStyleRowBandSize")?.IntVal() ?? 1);
        properties.ColumnBandSize = Math.Max(1, tblPr.Element(W.Main + "tblStyleColBandSize")?.IntVal() ?? 1);

        if (tblPr.Element(W.Main + "tblLook") is { } look) properties.Look = ReadTableLook(look);

        properties.IndentTwips = tblPr.Element(W.Main + "tblInd")?.IntAttr("w");
        properties.Mirrored = tblPr.Element(W.Main + "bidiVisual")?.OnOff() ?? properties.Mirrored;
        properties.Position = ReadTablePosition(tblPr.Element(W.Main + "tblpPr"));

        if (tblPr.Element(W.Main + "tblLayout")?.Attr("type") is { } layout)
            properties.FixedLayout = layout == "fixed";

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

    /// <summary>
    /// Reads a <c>w:tblLook</c>, in either of the two spellings Word writes it in.
    /// </summary>
    /// <remarks>
    /// The attributes are read where they are there and the hexadecimal <c>w:val</c> where they
    /// are not, since a document written before Word 2007 has only the second. The bits of it
    /// say the same six things: 0x0020 a first row, 0x0040 a last row, 0x0080 a first column,
    /// 0x0100 a last column, and 0x0200 and 0x0400 turning the two kinds of banding off.
    /// </remarks>
    private static TableLook ReadTableLook(XElement element)
    {
        var packed = 0;
        if (element.Attr("val") is { } value)
            int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed);

        bool Read(string name, int bit, bool inverted = false)
        {
            if (element.Attr(name) is { } attribute) return ReadOnOff(attribute) != inverted;

            return ((packed & bit) != 0) != inverted;
        }

        return new TableLook
        {
            FirstRow = Read("firstRow", 0x0020),
            LastRow = Read("lastRow", 0x0040),
            FirstColumn = Read("firstColumn", 0x0080),
            LastColumn = Read("lastColumn", 0x0100),
            HorizontalBanding = Read("noHBand", 0x0200, inverted: true),
            VerticalBanding = Read("noVBand", 0x0400, inverted: true)
        };
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

            row.CantSplit = trPr.Element(W.Main + "cantSplit")?.OnOff();
            row.IsHeader = trPr.Element(W.Main + "tblHeader")?.OnOff();
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

            // "auto" is kept rather than dropped: a cell declaring it is turning off shading its
            // style would otherwise give it, which is not the same as saying nothing.
            cell.ShadingFill = tcPr.Element(W.Main + "shd")?.Attr("fill");

            cell.VerticalAlignment = ReadVerticalAlignment(tcPr.Element(W.Main + "vAlign"));

            // The two upright directions are the ones Word writes and the ones a document uses;
            // the vertical-script variants say what to do with East Asian text set in columns,
            // which is another matter and not read here.
            cell.TextDirection = tcPr.Element(W.Main + "textDirection")?.Val() switch
            {
                "btLr" or "tbLrV" => CellTextDirection.BottomToTop,
                "tbRl" or "tbRlV" => CellTextDirection.TopToBottom,
                _ => CellTextDirection.LeftToRight
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

        foreach (var child in Blocks(cellElement))
        {
            if (child.Name == W.Main + "p")
                cell.Content.Add(ParseParagraph(child));
            else if (child.Name == W.Main + "tbl")
                cell.Content.Add(ParseTable(child));
        }

        FoldAdjacentTables(cell.Content);

        // A cell must contain at least one paragraph; an empty one still occupies a row.
        if (cell.Content.Count == 0) cell.Content.Add(new Paragraph());

        return cell;
    }

    /// <summary>Reads the <c>w:trPr</c> of a table style, which says less than a row's own can.</summary>
    public static TableStyleRowProperties ParseTableStyleRowProperties(XElement trPr)
    {
        var properties = new TableStyleRowProperties
        {
            CantSplit = trPr.Element(W.Main + "cantSplit")?.OnOff(),
            IsHeader = trPr.Element(W.Main + "tblHeader")?.OnOff()
        };

        if (trPr.Element(W.Main + "trHeight") is { } height)
        {
            properties.HeightTwips = height.IntAttr("val");
            properties.HeightRule = height.Attr("hRule") switch
            {
                "exact" => RowHeightRule.Exact,
                "atLeast" => RowHeightRule.AtLeast,
                _ => properties.HeightTwips is not null ? RowHeightRule.AtLeast : null
            };
        }

        return properties;
    }

    /// <summary>Reads the <c>w:tcPr</c> of a table style.</summary>
    public static TableStyleCellProperties ParseTableStyleCellProperties(XElement tcPr)
    {
        var properties = new TableStyleCellProperties
        {
            ShadingFill = tcPr.Element(W.Main + "shd")?.Attr("fill"),
            VerticalAlignment = ReadVerticalAlignment(tcPr.Element(W.Main + "vAlign"))
        };

        if (tcPr.Element(W.Main + "tcBorders") is { } borders) ReadBorders(borders, properties.Borders);

        if (tcPr.Element(W.Main + "tcMar") is { } margins)
        {
            properties.MarginLeftTwips = margins.Element(W.Main + "left")?.IntAttr("w");
            properties.MarginRightTwips = margins.Element(W.Main + "right")?.IntAttr("w");
            properties.MarginTopTwips = margins.Element(W.Main + "top")?.IntAttr("w");
            properties.MarginBottomTwips = margins.Element(W.Main + "bottom")?.IntAttr("w");
        }

        return properties;
    }

    /// <summary>Reads a <c>w:vAlign</c>, or null where a cell declares none.</summary>
    internal static VerticalCellAlignment? ReadVerticalAlignment(XElement? element) => element?.Val() switch
    {
        null => null,
        "center" => VerticalCellAlignment.Center,
        "bottom" => VerticalCellAlignment.Bottom,
        _ => VerticalCellAlignment.Top
    };

    internal static void ReadBorders(XElement container, BorderSet target)
    {
        target.Top = ReadBorderEdge(container.Element(W.Main + "top"));
        target.Left = ReadBorderEdge(container.Element(W.Main + "left") ?? container.Element(W.Main + "start"));
        target.Bottom = ReadBorderEdge(container.Element(W.Main + "bottom"));
        target.Right = ReadBorderEdge(container.Element(W.Main + "right") ?? container.Element(W.Main + "end"));
        target.InsideHorizontal = ReadBorderEdge(container.Element(W.Main + "insideH"));
        target.InsideVertical = ReadBorderEdge(container.Element(W.Main + "insideV"));
    }

    /// <summary>Reads <c>w:tblpPr</c>: where a floating table stands and what it is measured from.</summary>
    private static TablePosition? ReadTablePosition(XElement? element)
    {
        if (element is null) return null;

        static double Distance(XElement element, string name) =>
            Units.TwipsToPoints(element.IntAttr(name) ?? 0);

        static TableAnchor Anchor(string? value) => value switch
        {
            "page" => TableAnchor.Page,
            "margin" => TableAnchor.Margin,
            _ => TableAnchor.Text
        };

        static TableAlignSpec Spec(string? value) => value switch
        {
            "left" => TableAlignSpec.Left,
            "center" => TableAlignSpec.Center,
            "right" => TableAlignSpec.Right,
            "inside" => TableAlignSpec.Inside,
            "outside" => TableAlignSpec.Outside,
            "top" => TableAlignSpec.Top,
            "bottom" => TableAlignSpec.Bottom,
            "inline" => TableAlignSpec.Inline,
            _ => TableAlignSpec.None
        };

        return new TablePosition(
            Distance(element, "leftFromText"),
            Distance(element, "rightFromText"),
            Distance(element, "topFromText"),
            Distance(element, "bottomFromText"),
            Anchor(element.Attr("horzAnchor")),
            Anchor(element.Attr("vertAnchor")),
            Distance(element, "tblpX"),
            Spec(element.Attr("tblpXSpec")),
            Distance(element, "tblpY"),
            Spec(element.Attr("tblpYSpec")));
    }

    /// <summary>
    /// Reads <c>w:framePr</c>, of which only the dropped capital is honoured.
    /// </summary>
    /// <remarks>
    /// <c>w:lines</c> is read and kept, but nothing is drawn from it: Word writes the size it
    /// worked out onto the run and the height it worked out onto the paragraph, and those are what
    /// the letter is drawn by. drop-cap-probe puts a frame of three lines round a letter of
    /// ordinary size and Word shortens one line, not three.
    /// </remarks>
    private static FrameProperties? ReadFrame(XElement element)
    {
        var kind = element.Attr("dropCap") switch
        {
            "drop" => DropCapKind.Drop,
            "margin" => DropCapKind.Margin,
            _ => DropCapKind.None
        };

        if (kind == DropCapKind.None) return null;

        return new FrameProperties(
            kind,
            Math.Max(1, element.IntAttr("lines") ?? 1),
            Units.TwipsToPoints(element.IntAttr("hSpace") ?? 0));
    }

    /// <summary>
    /// Reads <c>w:lnNumType</c>: the numbering down the margin.
    /// </summary>
    /// <remarks>
    /// A section that states no distance gets eighteen points, which is what line-number-probe
    /// measures: its first section says nothing about one and Word writes the numbers against a
    /// place eighteen points in from the text.
    /// </remarks>
    private static LineNumbering? ReadLineNumbering(XElement? element)
    {
        if (element is null) return null;

        return new LineNumbering(
            Math.Max(1, element.IntAttr("countBy") ?? 1),
            element.IntAttr("start") ?? 1,
            element.Attr("restart") switch
            {
                "newSection" => LineNumberRestart.NewSection,
                "continuous" => LineNumberRestart.Continuous,
                _ => LineNumberRestart.NewPage
            },
            element.IntAttr("distance") is { } distance
                ? Units.TwipsToPoints(distance)
                : 18);
    }

    /// <summary>
    /// Reads <c>w:pgBorders</c>: the border round the page, each edge with its own line and its
    /// own distance from whatever it is measured from.
    /// </summary>
    private static PageBorders? ReadPageBorders(XElement? element)
    {
        if (element is null) return null;

        static PageBorderEdge? Edge(XElement? side)
        {
            if (ReadBorderEdge(side) is not { IsVisible: true } line) return null;

            // The space is in points, and a border that states none stands where the thing it is
            // measured from stands.
            return new PageBorderEdge(line, side?.IntAttr("space") ?? 0);
        }

        var borders = new PageBorders
        {
            Top = Edge(element.Element(W.Main + "top")),
            Left = Edge(element.Element(W.Main + "left")),
            Bottom = Edge(element.Element(W.Main + "bottom")),
            Right = Edge(element.Element(W.Main + "right")),
            FromText = element.Attr("offsetFrom") == "text",
            Display = element.Attr("display") switch
            {
                "firstPage" => PageBorderDisplay.FirstPage,
                "notFirstPage" => PageBorderDisplay.NotFirstPage,
                _ => PageBorderDisplay.AllPages
            }
        };

        return borders.IsEmpty ? null : borders;
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
