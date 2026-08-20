using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Styling;

namespace n8PDF.Layout;

/// <summary>
/// A run of text placed on a page.
/// </summary>
/// <remarks>
/// Coordinates use Word's convention: the origin is the top-left corner of the page and Y grows
/// downward. The flip into PDF's bottom-left origin happens once, in the renderer.
/// </remarks>
internal sealed class PositionedText
{
    public required double X { get; init; }

    /// <summary>Distance from the top of the page down to the text baseline.</summary>
    public required double BaselineY { get; init; }

    public required string Text { get; init; }

    public required ResolvedRunFormat Format { get; init; }

    public required FontSelection Font { get; init; }

    /// <summary>Advance width of this run in points, as measured during layout.</summary>
    public required double Width { get; init; }

    /// <summary>
    /// Extra width distributed across this run's spaces by justification, in points. Rendered
    /// through the word-spacing operator rather than by moving each word.
    /// </summary>
    public double WordSpacing { get; init; }

    /// <summary>
    /// Where this run links to, if anywhere. Carried to the writer, which turns it into a link
    /// annotation over the run's box.
    /// </summary>
    public ResolvedHyperlink? Link { get; init; }

    /// <summary>
    /// Whether this run's width was measured with the font's kerning applied. Carried to the
    /// writer so that what is drawn is spaced the way it was measured.
    /// </summary>
    public bool Kerned { get; init; }

    /// <summary>
    /// Whether this run is drawn from the right. The text is kept in the order it is read, which
    /// is the only order it can be shaped in — which letter joins to which is a fact about the
    /// text — and the writer turns the glyphs round.
    /// </summary>
    public bool RightToLeft { get; init; }

    public double FontSizePoints => Format.EffectiveFontSizePoints;

    /// <summary>
    /// A copy of this run moved by the given offset.
    /// </summary>
    /// <remarks>
    /// Layout composes some content — table cells, and lines displaced by a float — in one place
    /// and moves it to another. Copying field by field at each of those sites made it easy to add
    /// a property here and silently lose it in transit, so the copy lives with the type instead.
    /// </remarks>
    public PositionedText Translate(double dx, double dy) => new()
    {
        X = X + dx,
        BaselineY = BaselineY + dy,
        Text = Text,
        Format = Format,
        Font = Font,
        Width = Width,
        WordSpacing = WordSpacing,
        Link = Link,
        Kerned = Kerned,
        RightToLeft = RightToLeft
    };

    public override string ToString() => $"({X:0.##}, {BaselineY:0.##}) \"{Text}\"";
}

/// <summary>A hyperlink with its target already resolved.</summary>
/// <param name="Url">An external address, or null for an internal link.</param>
/// <param name="Anchor">A bookmark name within the document, or null for an external link.</param>
internal sealed record ResolvedHyperlink(string? Url, string? Anchor);

/// <summary>A place in the document an internal link can point at.</summary>
internal sealed record BookmarkDestination(int PageIndex, double X, double Y);

/// <summary>A horizontal rule drawn for an underline or strikethrough.</summary>
internal sealed class PositionedRule
{
    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Width { get; init; }

    public required double Thickness { get; init; }

    public required (double Red, double Green, double Blue) Color { get; init; }
}

/// <summary>One composed line, retained so that diagnostics can describe layout by line.</summary>
internal sealed class LaidOutLine
{
    public required double BaselineY { get; init; }

    public required double Height { get; init; }

    public required double Ascent { get; init; }

    public List<PositionedText> Texts { get; } = [];

    /// <summary>Index of the paragraph this line came from, for diagnostics.</summary>
    public int ParagraphIndex { get; init; }
}

/// <summary>
/// A filled rectangle: cell shading, and the border lines of a table.
/// </summary>
/// <remarks>
/// Borders are filled rectangles rather than stroked lines because a stroke straddles its path,
/// which puts half of every border outside the cell it belongs to and makes adjacent cells
/// overlap by half a line width.
/// </remarks>
internal sealed class PositionedRectangle
{
    public required double X { get; init; }

    /// <summary>Distance from the top of the page down to the rectangle's top edge.</summary>
    public required double Y { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public required (double Red, double Green, double Blue) Color { get; init; }
}

/// <summary>An image placed on a page.</summary>
internal sealed class PositionedImage
{
    public required double X { get; init; }

    /// <summary>Distance from the top of the page down to the image's top edge.</summary>
    public required double Y { get; init; }

    /// <summary>Display width in points, which is the size the document asked for, not the pixel width.</summary>
    public required double Width { get; init; }

    public required double Height { get; init; }

    public required Images.ImageData Image { get; init; }
}

internal sealed class LaidOutPage
{
    public required double WidthPoints { get; init; }

    public required double HeightPoints { get; init; }

    /// <summary>
    /// The section this page belongs to, which is what decided its size and margins and which
    /// running heads it takes. A document with section breaks has pages of more than one section,
    /// so this cannot be asked of the document as a whole.
    /// </summary>
    public SectionProperties Section { get; init; } = new();

    /// <summary>
    /// Which page this is within its own section, counted from zero. A section's own first page is
    /// what a title page means, not the document's.
    /// </summary>
    public int IndexInSection { get; init; }

    /// <summary>
    /// The number this page is printed as, which is what a page number field shows. The same as
    /// its place in the document unless a section began its numbering again.
    /// </summary>
    public int PageNumber { get; init; }

    public List<LaidOutLine> Lines { get; } = [];

    /// <summary>Images, drawn after the shading and borders but before the text.</summary>
    public List<PositionedImage> Images { get; } = [];

    public List<PositionedRule> Rules { get; } = [];

    /// <summary>
    /// Shading and borders, in paint order: everything here is drawn before any text, and fills
    /// are added before the borders that sit on top of them.
    /// </summary>
    public List<PositionedRectangle> Rectangles { get; } = [];

    public IEnumerable<PositionedText> Texts => Lines.SelectMany(line => line.Texts);
}

/// <summary>The fully laid-out document, ready to render.</summary>
internal sealed class LaidOutDocument
{
    public List<LaidOutPage> Pages { get; } = [];

    /// <summary>Bookmark positions by name, for resolving internal links.</summary>
    public Dictionary<string, BookmarkDestination> Bookmarks { get; } = [];

    /// <summary>
    /// The document's final section. Page geometry comes from <see cref="LaidOutPage.Section"/>
    /// instead, since a document with section breaks has pages belonging to several.
    /// </summary>
    public required SectionProperties Section { get; init; }
}
