using System.Globalization;
using n8PDF.Fonts;

namespace n8PDF.Pdf;

/// <summary>A page under construction: its geometry plus the content stream being written to it.</summary>
internal sealed class PdfPage
{
    internal PdfPage(PdfDictionary dictionary, double width, double height)
    {
        Dictionary = dictionary;
        Width = width;
        Height = height;
    }

    internal PdfDictionary Dictionary { get; }

    /// <summary>
    /// Link annotations for this page. Held separately from the content stream because an
    /// annotation is not drawn: it is an interactive region layered over what was drawn.
    /// </summary>
    internal PdfArray Annotations { get; } = new();

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
internal sealed class PdfBuilder
{
    private readonly PdfDocument _document = new();
    private readonly Dictionary<TrueTypeFont, PdfFont> _fonts = [];
    private readonly Dictionary<Images.ImageData, PdfImage> _images = [];
    private readonly List<PdfPage> _pages = [];
    private readonly PdfDictionary _fontResources = new();
    private readonly PdfDictionary _xObjectResources = new();
    private readonly PdfDictionary _alphaResources = new();
    private readonly Dictionary<double, string> _alphaNames = [];
    private readonly PdfDictionary _shadingResources = new();
    private int _shadings;
    private readonly PdfDictionary _sharedResources = new();
    private readonly PdfReference _sharedResourcesRef;

    public PdfBuilder()
    {
        _sharedResources.Set("Font", _fontResources);
        _sharedResources.Set("XObject", _xObjectResources);
        _sharedResources.Set("ExtGState", _alphaResources);
        _sharedResources.Set("Shading", _shadingResources);
        _sharedResourcesRef = _document.Add(_sharedResources);
    }

    public PdfDocument Document => _document;

    public IReadOnlyList<PdfPage> Pages => _pages;

    public string? Title
    {
        get => _document.Title;
        set => _document.Title = value;
    }

    /// <summary>Leave the hinting out of the fonts this embeds. See <c>ConversionOptions</c>.</summary>
    public bool DropHinting { get; set; }

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
    /// <summary>
    /// The graphics state that paints at the given opacity, named so a content stream can ask for
    /// it. A PDF carries transparency in the state rather than in the colour, so anything drawn
    /// see-through has to name one of these first.
    /// </summary>
    public string UseAlpha(double opacity)
    {
        var rounded = Math.Round(Math.Clamp(opacity, 0, 1), 3);

        if (_alphaNames.TryGetValue(rounded, out var existing)) return existing;

        var name = $"Ga{_alphaNames.Count + 1}";
        var state = new PdfDictionary();
        state.Set("ca", new PdfNumber(rounded));
        state.Set("CA", new PdfNumber(rounded));

        _alphaResources.Set(name, state);
        _alphaNames[rounded] = name;

        return name;
    }

    /// <summary>
    /// An axial shading between the given page coordinates, named so a content stream can paint
    /// it with <c>sh</c> (#64). Two stops make one exponential function; more make a stitching
    /// function over the segments between them.
    /// </summary>
    public string UseShading(
        IReadOnlyList<(double Position, (double R, double G, double B) Color)> stops,
        double x0, double y0, double x1, double y1)
    {
        static PdfDictionary Segment((double R, double G, double B) from, (double R, double G, double B) to)
        {
            var function = new PdfDictionary();
            function.Set("FunctionType", new PdfNumber(2));
            function.Set("Domain", new PdfArray().Add(0).Add(1));
            function.Set("C0", new PdfArray().Add(from.R).Add(from.G).Add(from.B));
            function.Set("C1", new PdfArray().Add(to.R).Add(to.G).Add(to.B));
            function.Set("N", new PdfNumber(1));
            return function;
        }

        PdfDictionary function;

        if (stops.Count == 2)
        {
            function = Segment(stops[0].Color, stops[1].Color);
        }
        else
        {
            var functions = new PdfArray();
            var bounds = new PdfArray();
            var encode = new PdfArray();

            for (var i = 0; i + 1 < stops.Count; i++)
            {
                functions.Add(Segment(stops[i].Color, stops[i + 1].Color));
                if (i > 0) bounds.Add(stops[i].Position);
                encode.Add(0).Add(1);
            }

            function = new PdfDictionary();
            function.Set("FunctionType", new PdfNumber(3));
            function.Set("Domain", new PdfArray().Add(0).Add(1));
            function.Set("Functions", functions);
            function.Set("Bounds", bounds);
            function.Set("Encode", encode);
        }

        // The axis runs from the first stop's position to the last's along the given line, so
        // stops that begin late or end early still land where they were asked to.
        var shading = new PdfDictionary();
        shading.Set("ShadingType", new PdfNumber(2));
        shading.Set("ColorSpace", "DeviceRGB");
        shading.Set("Coords", new PdfArray().Add(x0).Add(y0).Add(x1).Add(y1));
        shading.Set("Function", function);

        var name = "Sh" + (++_shadings).ToString(CultureInfo.InvariantCulture);
        _shadingResources.Set(name, _document.Add(shading));
        return name;
    }

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
            _fontResources.Set(font.ResourceName, font.Build(_document, DropHinting));

        foreach (var image in _images.Values)
            _xObjectResources.Set(image.ResourceName, image.Build(_document));

        foreach (var page in _pages)
        {
            var content = new PdfStream(page.Content.ToArray());
            page.Dictionary.Set("Contents", _document.Add(content));

            if (page.Annotations.Count > 0) page.Dictionary.Set("Annots", page.Annotations);
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
