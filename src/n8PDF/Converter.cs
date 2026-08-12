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

        var stylesPart = package.GetRelatedPartName(mainPartName, OpcPackage.StylesRelationship);
        var styles = StylesParser.Parse(stylesPart is null ? null : package.ReadPartAsXml(stylesPart));

        var themePart = package.GetRelatedPartName(mainPartName, OpcPackage.ThemeRelationship);
        var theme = StylesParser.ParseTheme(themePart is null ? null : package.ReadPartAsXml(themePart));

        var resolver = new StyleResolver(styles, theme, options.ApplyWordBuiltInStyleDefaults);

        var fonts = options.Fonts ?? new FontLibrary();
        var engine = new LayoutEngine(fonts, resolver, options.Layout);

        return engine.Layout(document);
    }

    /// <summary>
    /// Reads the image parts the main document references, keyed by relationship id.
    /// </summary>
    /// <remarks>
    /// Done here rather than during layout because the package is closed by the time layout runs,
    /// and a drawing carries only the relationship id, not the picture. A part that is missing or
    /// unreadable is skipped: a broken image should cost its own placement, not the conversion.
    /// </remarks>
    private static void LoadImages(OpcPackage package, string mainPartName, WordDocument document)
    {
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
