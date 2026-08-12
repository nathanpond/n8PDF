using System.Globalization;
using n8PDF.Fonts;

namespace n8PDF.Pdf;

/// <summary>A page under construction: its geometry plus the content stream being written to it.</summary>
public sealed class PdfPage
{
    internal PdfPage(PdfDictionary dictionary, double width, double height)
    {
        Dictionary = dictionary;
        Width = width;
        Height = height;
    }

    internal PdfDictionary Dictionary { get; }

    /// <summary>Page width in points.</summary>
    public double Width { get; }

    /// <summary>Page height in points.</summary>
    public double Height { get; }

    public ContentStreamBuilder Content { get; } = new();
}

/// <summary>
/// Assembles a multi-page PDF, owning the font registry and the shared resource dictionary so
/// that callers deal in fonts and pages rather than in object graphs.
/// </summary>
public sealed class PdfBuilder
{
    private readonly PdfDocument _document = new();
    private readonly Dictionary<TrueTypeFont, PdfFont> _fonts = [];
    private readonly Dictionary<Images.ImageData, PdfImage> _images = [];
    private readonly List<PdfPage> _pages = [];
    private readonly PdfDictionary _fontResources = new();
    private readonly PdfDictionary _xObjectResources = new();
    private readonly PdfDictionary _sharedResources = new();
    private readonly PdfReference _sharedResourcesRef;

    public PdfBuilder()
    {
        _sharedResources.Set("Font", _fontResources);
        _sharedResources.Set("XObject", _xObjectResources);
        _sharedResourcesRef = _document.Add(_sharedResources);
    }

    public PdfDocument Document => _document;

    public IReadOnlyList<PdfPage> Pages => _pages;

    public string? Title
    {
        get => _document.Title;
        set => _document.Title = value;
    }

    public PdfPage AddPage(double widthPoints, double heightPoints)
    {
        var dictionary = _document.AddPage(widthPoints, heightPoints, out _);

        // Every page shares one resource dictionary. Fonts are document-wide, and duplicating
        // the dictionary per page would bloat the file for no benefit.
        dictionary.Set("Resources", _sharedResourcesRef);

        var page = new PdfPage(dictionary, widthPoints, heightPoints);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// Returns the PDF font for a face, registering it on first use. Resource names are assigned
    /// in first-use order, which keeps output stable for a given document.
    /// </summary>
    public PdfFont UseFont(TrueTypeFont font)
    {
        if (_fonts.TryGetValue(font, out var existing))
            return existing;

        var name = "F" + (_fonts.Count + 1).ToString(CultureInfo.InvariantCulture);
        var pdfFont = new PdfFont(font, name);
        _fonts[font] = pdfFont;
        return pdfFont;
    }

    /// <summary>
    /// Returns the PDF image for some decoded image data, registering it on first use. The same
    /// picture used twice is embedded once.
    /// </summary>
    public PdfImage UseImage(Images.ImageData image)
    {
        if (_images.TryGetValue(image, out var existing)) return existing;

        var name = "Im" + (_images.Count + 1).ToString(CultureInfo.InvariantCulture);
        var pdfImage = new PdfImage(image, name);
        _images[image] = pdfImage;
        return pdfImage;
    }

    public void Save(Stream stream)
    {
        // Font objects are built last: the width array and ToUnicode map depend on which glyphs
        // the content streams actually used.
        foreach (var font in _fonts.Values)
            _fontResources.Set(font.ResourceName, font.Build(_document));

        foreach (var image in _images.Values)
            _xObjectResources.Set(image.ResourceName, image.Build(_document));

        foreach (var page in _pages)
        {
            var content = new PdfStream(page.Content.ToArray());
            page.Dictionary.Set("Contents", _document.Add(content));
        }

        _document.Save(stream);
    }

    public byte[] ToArray()
    {
        using var buffer = new MemoryStream();
        Save(buffer);
        return buffer.ToArray();
    }
}
