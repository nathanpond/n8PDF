using n8PDF.Fonts;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Packaging;
using n8PDF.Pdf;
using n8PDF.Styling;

namespace n8PDF;

/// <summary>Settings for a conversion.</summary>
public sealed class ConversionOptions
{
    /// <summary>
    /// Fonts available to the conversion. Leave null to get a library that discovers the
    /// platform's installed fonts. Supply one with fonts registered explicitly to make output
    /// reproducible regardless of what is installed.
    /// </summary>
    /// <remarks>
    /// Leaving it null costs nothing to repeat: what the platform's font directories hold is read
    /// once for the process and shared, and a face is read from its file only when a document
    /// actually asks for it. A library of one's own is worth keeping and reusing all the same,
    /// since anything registered into it is registered once.
    /// </remarks>
    public FontLibrary? Fonts { get; set; }

    public LayoutOptions Layout { get; set; } = new();

    /// <summary>
    /// How much the document is allowed to decompress to. A <c>.docx</c> is a ZIP, and a hostile
    /// one can be a few hundred bytes that become gigabytes; these bound that. Raise them for a
    /// document that is genuinely enormous.
    /// </summary>
    public PackageLimits Limits { get; set; } = new();

    /// <summary>
    /// Fill in properties a document's styles leave unstated from Word's built-in style
    /// definitions, as Word itself does. On by default, because matching Word is the point.
    /// </summary>
    /// <remarks>
    /// Turn this off to render strictly what the document says. The two differ only for documents
    /// with a sparse <c>styles.xml</c>; anything Word saved states its own values and is
    /// unaffected either way. See <see cref="Styling.WordBuiltInStyles"/>.
    /// </remarks>
    public bool ApplyWordBuiltInStyleDefaults { get; set; } = true;

    /// <summary>
    /// Leave the hinting out of the fonts the PDF embeds.
    /// </summary>
    /// <remarks>
    /// Off, because Word keeps it: its own exports carry <c>cvt</c>, <c>fpgm</c> and <c>prep</c>
    /// in every subset they embed, and matching Word is the point of this converter.
    ///
    /// Turning it on takes out those three tables and the instructions inside each glyph, which
    /// is about a quarter of what an embedded face weighs. Nothing about the shapes changes —
    /// hinting nudges points onto the pixel grid at small sizes on a low-resolution screen, and
    /// most PDF readers do their own rasterising and ignore it. It cannot be subset rather than
    /// dropped: control values are reached by index and function numbers are worked out as the
    /// instructions run, so which of them a glyph needs is not a question that can be answered
    /// without running them.
    /// </remarks>
    public bool DropFontHinting { get; set; }

    /// <summary>Title recorded in the PDF's document information dictionary.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// The name of the file being converted, which is what a FILENAME field shows. Set for you by
    /// <see cref="Converter.ConvertFile"/>; a conversion from a stream has no name to go on unless
    /// one is given here.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// The instant the DATE and TIME fields report, for a conversion that has to come out the same
    /// twice. Defaults to the creation date if one is given, and to now otherwise.
    /// </summary>
    public DateTimeOffset? FieldsAsOf { get; set; }

    /// <summary>
    /// The record to fill a document's merge fields from. A document written for a mail merge
    /// names a data source this cannot reach, so converting one with no record given shows each
    /// field's own name in guillemets — which is what Word shows for the same document.
    /// </summary>
    public MailMergeRecord? MergeRecord { get; set; }

    /// <summary>
    /// Creation timestamp for the PDF. Left null so that converting the same document twice
    /// produces identical bytes, which is what makes golden comparison possible.
    /// </summary>
    public DateTimeOffset? CreationDate { get; set; }
}

/// <summary>
/// Converts WordprocessingML documents to PDF.
/// </summary>
/// <remarks>
/// Everything happens in this assembly: the DOCX container is read, its markup parsed, styles
/// resolved, text measured against the real font files, lines broken and pages composed, and the
/// PDF written — with no third-party library, external process or service involved.
/// </remarks>
public static class Converter
{
    /// <summary>Converts a DOCX stream to a PDF stream.</summary>
    public static void Convert(Stream docx, Stream pdf, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();

        var laidOut = LayoutDocument(docx, options);

        var builder = new PdfBuilder { Title = options.Title, DropHinting = options.DropFontHinting };
        builder.Document.CreationDate = options.CreationDate;

        // The font library goes with it: a drawing can hold text that layout never measured,
        // and drawing it needs a font of its own.
        PdfRenderer.Render(laidOut, builder, options.Fonts);
        builder.Save(pdf);
    }

    public static byte[] Convert(byte[] docx, ConversionOptions? options = null)
    {
        using var input = new MemoryStream(docx);
        using var output = new MemoryStream();
        Convert(input, output, options);
        return output.ToArray();
    }

    public static void ConvertFile(string docxPath, string pdfPath, ConversionOptions? options = null)
    {
        // The name is what a FILENAME field shows, and this is the one entry point that knows it.
        options ??= new ConversionOptions();
        options.FileName ??= docxPath;

        using var input = File.OpenRead(docxPath);
        using var output = File.Create(pdfPath);
        Convert(input, output, options);
    }

    /// <summary>
    /// Runs everything up to but not including PDF generation, returning the positioned pages.
    /// </summary>
    /// <remarks>
    /// Not part of what the package promises. Layout is what the fidelity work inspects and
    /// asserts against — the PDF is only its serialisation — and the tests reach it as an
    /// internal. Making it public would freeze the whole layout model along with it: every page,
    /// line, run, resolved format and font the model reaches, none of which is settled enough to
    /// promise. If a caller ever needs something of it, the thing to add is that something.
    /// </remarks>
    internal static LaidOutDocument LayoutDocument(Stream docx, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();

        using var package = OpcPackage.Open(docx, limits: options.Limits);
        var mainPartName = package.GetMainDocumentPartName();

        var document = DocumentParser.Parse(package.ReadPartAsXml(mainPartName));
        LoadImages(package, mainPartName, document);
        LoadHeadersAndFooters(package, mainPartName, document);
        LoadNotes(package, mainPartName, document, NoteKind.Footnote);
        LoadNotes(package, mainPartName, document, NoteKind.Endnote);
        LoadHyperlinks(package, mainPartName, mainPartName, document.Body, document);
        LoadDiagrams(package, mainPartName, document.Body);
        LoadCharts(package, mainPartName, document.Body);

        var settingsPart = package.GetRelatedPartName(mainPartName, OpcPackage.SettingsRelationship);
        if (settingsPart is not null)
        {
            var settings = package.ReadPartAsXml(settingsPart).Root;
            document.EvenAndOddHeaders = settings?.Element(W.Main + "evenAndOddHeaders") is not null;

            if (settings?.Element(W.Main + "decimalSymbol")?.Attr("val") is { Length: > 0 } symbol)
                document.DecimalSymbol = symbol;

            // Whether words are broken at the ends of lines. The zone is how much white a line may
            // be left with before one is: a quarter of an inch where the document says nothing,
            // which is Word's own default.
            document.Hyphenation = new Hyphenation(
                settings?.Element(W.Main + "autoHyphenation")?.OnOff() ?? false,
                Units.TwipsToPoints(settings?.Element(W.Main + "hyphenationZone")?.IntVal() ?? 360),
                Math.Max(0, settings?.Element(W.Main + "consecutiveHyphenLimit")?.IntVal() ?? 0),
                settings?.Element(W.Main + "doNotHyphenateCaps")?.OnOff() ?? false);

            // How notes are numbered is stated on the section if anywhere, and in the settings
            // otherwise; a document that says nothing gets Word's defaults.
            if (DocumentParser.ReadNoteNumberFormat(settings, NoteKind.Footnote) is { } footnoteFormat)
                document.FootnoteNumberFormat = footnoteFormat;

            if (DocumentParser.ReadNoteNumberFormat(settings, NoteKind.Endnote) is { } endnoteFormat)
                document.EndnoteNumberFormat = endnoteFormat;

            // Where the endnotes are gathered is the one thing about a note Word takes from here
            // rather than from the section.
            if (DocumentParser.ReadEndnotePosition(settings) is { } endnotePosition)
                document.EndnotePosition = endnotePosition;
        }

        if (document.Section.FootnoteNumberFormat is { } sectionFootnotes)
            document.FootnoteNumberFormat = sectionFootnotes;

        if (document.Section.EndnoteNumberFormat is { } sectionEndnotes)
            document.EndnoteNumberFormat = sectionEndnotes;

        var stylesPart = package.GetRelatedPartName(mainPartName, OpcPackage.StylesRelationship);
        var styles = StylesParser.Parse(stylesPart is null ? null : package.ReadPartAsXml(stylesPart));

        // What a table takes from its style is filled in before anything is laid out, since a
        // cell cannot say whether it is ruled until its style has been asked.
        Styling.TableStyles.Apply(document, styles);

        var themePart = package.GetRelatedPartName(mainPartName, OpcPackage.ThemeRelationship);
        var theme = StylesParser.ParseTheme(themePart is null ? null : package.ReadPartAsXml(themePart));

        var numberingPart = package.GetRelatedPartName(mainPartName, OpcPackage.NumberingRelationship);
        var numbering = NumberingParser.Parse(
            numberingPart is null ? null : package.ReadPartAsXml(numberingPart));

        var resolver = new StyleResolver(
            styles, theme, options.ApplyWordBuiltInStyleDefaults, numbering);

        var fonts = options.Fonts ?? new FontLibrary();

        var environment = new FieldEnvironment
        {
            Properties = DocumentProperties.Parse(
                package.TryReadPartAsXml("docProps/core.xml"),
                package.TryReadPartAsXml("docProps/custom.xml")),
            FileName = options.FileName,
            Now = options.FieldsAsOf ?? options.CreationDate ?? DateTimeOffset.Now,
            Merge = options.MergeRecord
        };

        var bookmarks = BookmarkText.Collect(document);
        environment.TextOfBookmark = name => bookmarks.GetValueOrDefault(name);

        var engine = new LayoutEngine(fonts, resolver, options.Layout, options.Limits) { Fields = environment };
        var laidOut = engine.Layout(document);

        // A page number cannot be known while the page it is on is still being filled, so a
        // document holding one is laid out again knowing where everything fell the first time.
        if (!engine.NeedsPagination) return laidOut;

        var pagination = engine.CollectPagination(laidOut);

        // A reference to a page shows the number that page is printed as, which is not where it
        // stands in the document once a section has begun its numbering again.
        environment.PageOfBookmark = name => laidOut.Bookmarks.TryGetValue(name, out var found)
            ? laidOut.Pages[found.PageIndex].PageNumber
            : 0;

        var second = new LayoutEngine(fonts, resolver, options.Layout, options.Limits)
        {
            Fields = environment,
            Pagination = pagination
        };

        return second.Layout(document);
    }

    /// <summary>
    /// Reads the image parts the main document references, keyed by relationship id.
    /// </summary>
    /// <summary>
    /// Reads the header and footer parts the section refers to.
    /// </summary>
    /// <remarks>
    /// They are separate parts rather than part of the body, and are parsed with the same reader:
    /// a header holds paragraphs and tables like anything else.
    /// </remarks>
    private static void LoadHeadersAndFooters(OpcPackage package, string mainPartName, WordDocument document)
    {
        foreach (var relationship in package.GetRelationships(mainPartName))
        {
            if (relationship.IsExternal) continue;
            if (relationship.Type != OpcPackage.HeaderRelationship &&
                relationship.Type != OpcPackage.FooterRelationship)
            {
                continue;
            }

            try
            {
                var partName = package.ResolveTarget(mainPartName, relationship.Target);
                if (!package.HasPart(partName)) continue;

                var root = package.ReadPartAsXml(partName).Root;
                if (root is null) continue;

                var content = new HeaderFooter();
                foreach (var element in DocumentParser.Blocks(root))
                {
                    if (element.Name == W.Main + "p") content.Body.Add(DocumentParser.ParseParagraph(element));
                    else if (element.Name == W.Main + "tbl") content.Body.Add(DocumentParser.ParseTable(element));
                }

                document.HeadersAndFooters[relationship.Id] = content;
                LoadHyperlinks(package, partName, partName, content.Body, document);
                LoadPartImages(package, partName, content.Body, document);
                LoadDiagrams(package, partName, content.Body);
                LoadCharts(package, partName, content.Body);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException)
            {
            }
        }
    }

    /// <summary>
    /// Reads the footnotes or endnotes part, if the document has one.
    /// </summary>
    private static void LoadNotes(
        OpcPackage package, string mainPartName, WordDocument document, NoteKind kind)
    {
        var relationship = kind == NoteKind.Footnote
            ? OpcPackage.FootnotesRelationship
            : OpcPackage.EndnotesRelationship;

        var partName = package.GetRelatedPartName(mainPartName, relationship);
        if (partName is null || !package.HasPart(partName)) return;

        var target = kind == NoteKind.Footnote ? document.Footnotes : document.Endnotes;

        try
        {
            foreach (var (id, note) in DocumentParser.ParseNotes(package.ReadPartAsXml(partName), kind))
            {
                target[id] = note;

                // A note is its own part as far as relationships go, so a link inside one is
                // resolved against the notes part rather than the body's.
                LoadHyperlinks(package, partName, partName, note.Body, document);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException)
        {
        }
    }

    /// <summary>
    /// Resolves the external addresses of the hyperlinks in one part.
    /// </summary>
    /// <remarks>
    /// Relationship ids are scoped to the part that declares them, so a header and the body can
    /// each own an <c>rId1</c> pointing somewhere different. Ids are rewritten to include the part
    /// name as they are collected, which lets the whole document share one address table without
    /// two parts' links standing on each other.
    ///
    /// A link whose relationship is missing keeps its rewritten id and finds nothing in the table,
    /// which is what layout treats as "not a link": the text still draws, it just isn't clickable.
    /// </remarks>
    private static void LoadHyperlinks(
        OpcPackage package, string partName, string scope, List<BlockElement> blocks, WordDocument document)
    {
        Dictionary<string, string>? targets = null;

        foreach (var run in EnumerateRuns(blocks))
        {
            if (run.Hyperlink is not { RelationshipId: { } id } link) continue;

            targets ??= package.GetRelationships(partName)
                .Where(r => r.Type == OpcPackage.HyperlinkRelationship)
                .GroupBy(r => r.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.Ordinal);

            var key = scope + "|" + id;
            if (targets.TryGetValue(id, out var target)) document.Hyperlinks[key] = target;

            run.Hyperlink = link with { RelationshipId = key };
        }
    }

    /// <summary>
    /// Reads the pictures a part of its own refers to, under keys naming the part as well as the
    /// relationship.
    /// </summary>
    /// <remarks>
    /// A header owns its relationships, so its "rId1" and the body's are different pictures
    /// entirely — a document with a watermark and a logo has both, and one list keyed by id alone
    /// would draw one of them in both places. The drawings are rewritten to the same scoped key,
    /// which is how a link in a header is already handled.
    /// </remarks>
    private static void LoadPartImages(
        OpcPackage package, string partName, List<BlockElement> blocks, WordDocument document)
    {
        Dictionary<string, string>? targets = null;

        foreach (var run in EnumerateRuns(blocks))
        foreach (var content in run.Content)
        {
            string? id = content switch
            {
                DrawingInline inline => inline.RelationshipId,
                AnchoredDrawing anchored => anchored.RelationshipId,
                _ => null
            };

            if (id is null) continue;

            targets ??= package.GetRelationships(partName)
                .Where(r => r.Type == OpcPackage.ImageRelationship && !r.IsExternal)
                .GroupBy(r => r.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Target, StringComparer.Ordinal);

            var key = partName + "|" + id;

            if (targets.TryGetValue(id, out var target))
            {
                try
                {
                    var image = package.ResolveTarget(partName, target);
                    if (package.HasPart(image)) document.Images[key] = package.ReadPart(image);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException)
                {
                }
            }

            switch (content)
            {
                case DrawingInline inline:
                    inline.RelationshipId = key;
                    break;

                case AnchoredDrawing anchored:
                    anchored.RelationshipId = key;
                    break;
            }
        }
    }

    /// <summary>
    /// Reads the arrangement of every diagram a run of blocks holds.
    /// </summary>
    /// <remarks>
    /// The frame names the diagram's data; the drawing to be drawn is a part beside that one,
    /// reached by a relationship of the data part's own. So it takes two hops, and the second is
    /// through a part the document itself never mentions.
    /// </remarks>
    private static void LoadDiagrams(OpcPackage package, string partName, List<BlockElement> blocks)
    {
        foreach (var run in EnumerateRuns(blocks))
        foreach (var content in run.Content)
        {
            string? id = content switch
            {
                DrawingInline inline => inline.DiagramRelationshipId,
                AnchoredDrawing anchored => anchored.DiagramRelationshipId,
                _ => null
            };

            if (id is null) continue;

            var shapes = ReadDiagram(package, partName, id);
            if (shapes.Count == 0) continue;

            switch (content)
            {
                case DrawingInline inline:
                    inline.Diagram = shapes;
                    break;

                case AnchoredDrawing anchored:
                    anchored.Diagram = shapes;
                    break;
            }
        }
    }

    /// <summary>Reads the chart part every chart in a run of blocks names.</summary>
    private static void LoadCharts(OpcPackage package, string partName, List<BlockElement> blocks)
    {
        foreach (var run in EnumerateRuns(blocks))
        foreach (var content in run.Content)
        {
            string? id = content switch
            {
                DrawingInline inline => inline.ChartRelationshipId,
                AnchoredDrawing anchored => anchored.ChartRelationshipId,
                _ => null
            };

            if (id is null) continue;

            ChartDefinition? chart = null;

            try
            {
                var reference = package.GetRelationships(partName)
                    .FirstOrDefault(r => r.Id == id && !r.IsExternal);

                if (reference is not null)
                {
                    var chartPart = package.ResolveTarget(partName, reference.Target);

                    if (package.HasPart(chartPart))
                        chart = ChartReader.Parse(package.ReadPartAsXml(chartPart));
                }
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException
                                         or System.Xml.XmlException)
            {
            }

            if (chart is null) continue;

            switch (content)
            {
                case DrawingInline inline:
                    inline.Chart = chart;
                    break;

                case AnchoredDrawing anchored:
                    anchored.Chart = chart;
                    break;
            }
        }
    }

    private static List<DiagramShape> ReadDiagram(
        OpcPackage package, string partName, string relationshipId)
    {
        try
        {
            var data = package.GetRelationships(partName)
                .FirstOrDefault(r => r.Id == relationshipId && !r.IsExternal);

            if (data is null) return [];

            var dataPart = package.ResolveTarget(partName, data.Target);
            if (!package.HasPart(dataPart)) return [];

            // Which drawing holds the arrangement is written inside the data, in an extension of
            // Word's own: a relationship id, which is resolved against the part that declared it.
            // Word declares it beside the diagram's own four, on the document rather than on the
            // data — so both are looked in, the data's own first.
            var named = package.ReadPartAsXml(dataPart).Root
                ?.Descendants(Diagram.Drawing + "dataModelExt").FirstOrDefault()
                ?.Attribute("relId")?.Value;

            foreach (var owner in new[] { dataPart, partName })
            {
                var drawing = package.GetRelationships(owner).FirstOrDefault(r =>
                    !r.IsExternal &&
                    (named is not null ? r.Id == named : r.Type == Diagram.DrawingRelationship));

                if (drawing is null || drawing.Type != Diagram.DrawingRelationship) continue;

                var drawingPart = package.ResolveTarget(owner, drawing.Target);

                if (package.HasPart(drawingPart))
                    return Diagram.Parse(package.ReadPartAsXml(drawingPart));
            }

            return [];
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException
                                     or System.Xml.XmlException)
        {
            return [];
        }
    }

    /// <summary>Every run in a block list, descending through table cells.</summary>
    private static IEnumerable<Run> EnumerateRuns(IEnumerable<BlockElement> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach (var run in paragraph.Runs) yield return run;
                    break;

                case Table table:
                    foreach (var row in table.Rows)
                    foreach (var cell in row.Cells)
                    foreach (var run in EnumerateRuns(cell.Content))
                        yield return run;
                    break;
            }
        }
    }

    private static void LoadImages(OpcPackage package, string mainPartName, WordDocument document)
    {
        // Done here rather than during layout because the package is closed by the time layout
        // runs, and a drawing carries only the relationship id, not the picture. A part that is
        // missing or unreadable is skipped: a broken image should cost its own placement, not the
        // conversion.
        foreach (var relationship in package.GetRelationships(mainPartName))
        {
            if (relationship.Type != OpcPackage.ImageRelationship || relationship.IsExternal) continue;

            try
            {
                var partName = package.ResolveTarget(mainPartName, relationship.Target);
                if (package.HasPart(partName))
                    document.Images[relationship.Id] = package.ReadPart(partName);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException)
            {
            }
        }
    }
}
