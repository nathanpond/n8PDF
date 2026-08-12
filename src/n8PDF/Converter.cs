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
    public FontLibrary? Fonts { get; set; }

    public LayoutOptions Layout { get; set; } = new();

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

    /// <summary>Title recorded in the PDF's document information dictionary.</summary>
    public string? Title { get; set; }

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

        var builder = new PdfBuilder { Title = options.Title };
        builder.Document.CreationDate = options.CreationDate;

        PdfRenderer.Render(laidOut, builder);
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
        using var input = File.OpenRead(docxPath);
        using var output = File.Create(pdfPath);
        Convert(input, output, options);
    }

    /// <summary>
    /// Runs everything up to but not including PDF generation, returning the positioned pages.
    /// Exposed because layout is what fidelity work inspects and asserts against; the PDF is
    /// just its serialisation.
    /// </summary>
    public static LaidOutDocument LayoutDocument(Stream docx, ConversionOptions? options = null)
    {
        options ??= new ConversionOptions();

        using var package = OpcPackage.Open(docx);
        var mainPartName = package.GetMainDocumentPartName();

        var document = DocumentParser.Parse(package.ReadPartAsXml(mainPartName));
        LoadImages(package, mainPartName, document);
        LoadHeadersAndFooters(package, mainPartName, document);
        LoadHyperlinks(package, mainPartName, mainPartName, document.Body, document);

        var settingsPart = package.GetRelatedPartName(mainPartName, OpcPackage.SettingsRelationship);
        if (settingsPart is not null)
        {
            document.EvenAndOddHeaders = package.ReadPartAsXml(settingsPart)
                .Root?.Element(W.Main + "evenAndOddHeaders") is not null;
        }

        var stylesPart = package.GetRelatedPartName(mainPartName, OpcPackage.StylesRelationship);
        var styles = StylesParser.Parse(stylesPart is null ? null : package.ReadPartAsXml(stylesPart));

        var themePart = package.GetRelatedPartName(mainPartName, OpcPackage.ThemeRelationship);
        var theme = StylesParser.ParseTheme(themePart is null ? null : package.ReadPartAsXml(themePart));

        var numberingPart = package.GetRelatedPartName(mainPartName, OpcPackage.NumberingRelationship);
        var numbering = NumberingParser.Parse(
            numberingPart is null ? null : package.ReadPartAsXml(numberingPart));

        var resolver = new StyleResolver(
            styles, theme, options.ApplyWordBuiltInStyleDefaults, numbering);

        var fonts = options.Fonts ?? new FontLibrary();
        var engine = new LayoutEngine(fonts, resolver, options.Layout);

        return engine.Layout(document);
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
                foreach (var element in root.Elements())
                {
                    if (element.Name == W.Main + "p") content.Body.Add(DocumentParser.ParseParagraph(element));
                    else if (element.Name == W.Main + "tbl") content.Body.Add(DocumentParser.ParseTable(element));
                }

                document.HeadersAndFooters[relationship.Id] = content;
                LoadHyperlinks(package, partName, partName, content.Body, document);
            }
            catch (Exception e) when (e is IOException or InvalidDataException or FileNotFoundException)
            {
            }
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
