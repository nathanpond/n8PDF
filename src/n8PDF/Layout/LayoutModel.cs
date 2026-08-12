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
public sealed class PositionedText
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

    public double FontSizePoints => Format.EffectiveFontSizePoints;

    public override string ToString() => $"({X:0.##}, {BaselineY:0.##}) \"{Text}\"";
}

/// <summary>A horizontal rule drawn for an underline or strikethrough.</summary>
public sealed class PositionedRule
{
    public required double X { get; init; }

    public required double Y { get; init; }

    public required double Width { get; init; }

    public required double Thickness { get; init; }

    public required (double Red, double Green, double Blue) Color { get; init; }
}

/// <summary>One composed line, retained so that diagnostics can describe layout by line.</summary>
public sealed class LaidOutLine
{
    public required double BaselineY { get; init; }

    public required double Height { get; init; }

    public required double Ascent { get; init; }

    public List<PositionedText> Texts { get; } = [];

    /// <summary>Index of the paragraph this line came from, for diagnostics.</summary>
    public int ParagraphIndex { get; init; }
}

public sealed class LaidOutPage
{
    public required double WidthPoints { get; init; }

    public required double HeightPoints { get; init; }

    public List<LaidOutLine> Lines { get; } = [];

    public List<PositionedRule> Rules { get; } = [];

    public IEnumerable<PositionedText> Texts => Lines.SelectMany(line => line.Texts);
}

/// <summary>The fully laid-out document, ready to render.</summary>
public sealed class LaidOutDocument
{
    public List<LaidOutPage> Pages { get; } = [];

    public required SectionProperties Section { get; init; }
}
