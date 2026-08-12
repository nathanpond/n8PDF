using n8PDF.Ooxml;

namespace n8PDF.Styling;

/// <summary>
/// Character formatting after the full cascade has been applied: every value is concrete, with
/// no "inherit" left anywhere. Layout and rendering consume only this, never the raw XML.
/// </summary>
public sealed record ResolvedRunFormat
{
    /// <summary>Font family name, already resolved through the theme if a slot was referenced.</summary>
    public string FontFamily { get; init; } = "Times New Roman";

    public double FontSizePoints { get; init; } = 10;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public bool Caps { get; init; }

    public bool SmallCaps { get; init; }

    public bool Strike { get; init; }

    /// <summary>Hidden text, which is parsed but not rendered.</summary>
    public bool Hidden { get; init; }

    public UnderlineStyle Underline { get; init; } = UnderlineStyle.None;

    /// <summary>Text colour as RRGGBB. Null means the default, which renders black.</summary>
    public string? ColorHex { get; init; }

    public string? HighlightColor { get; init; }

    public VerticalTextAlignment VerticalAlignment { get; init; } = VerticalTextAlignment.Baseline;

    /// <summary>Extra spacing added after every character, in points.</summary>
    public double CharacterSpacingPoints { get; init; }

    /// <summary>Horizontal scaling as a fraction; 1.0 is unscaled.</summary>
    public double ScaleFactor { get; init; } = 1.0;

    /// <summary>
    /// The size actually drawn at. Word renders superscripts and subscripts at a reduced size
    /// rather than merely shifting the baseline.
    /// </summary>
    public double EffectiveFontSizePoints =>
        VerticalAlignment == VerticalTextAlignment.Baseline ? FontSizePoints : FontSizePoints * 0.65;

    /// <summary>Baseline shift in points; positive raises the text.</summary>
    public double BaselineShiftPoints => VerticalAlignment switch
    {
        VerticalTextAlignment.Superscript => FontSizePoints * 0.35,
        VerticalTextAlignment.Subscript => FontSizePoints * -0.14,
        _ => 0
    };

    /// <summary>Splits the colour into PDF's 0..1 components, defaulting to black.</summary>
    public (double Red, double Green, double Blue) GetColor()
    {
        if (ColorHex is null || ColorHex.Length != 6) return (0, 0, 0);

        try
        {
            var red = Convert.ToInt32(ColorHex[..2], 16) / 255.0;
            var green = Convert.ToInt32(ColorHex.Substring(2, 2), 16) / 255.0;
            var blue = Convert.ToInt32(ColorHex.Substring(4, 2), 16) / 255.0;
            return (red, green, blue);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return (0, 0, 0);
        }
    }
}

/// <summary>Paragraph formatting after the full cascade, with measurements converted to points.</summary>
public sealed record ResolvedParagraphFormat
{
    public string? StyleId { get; init; }

    public Justification Justification { get; init; } = Justification.Left;

    public double IndentLeftPoints { get; init; }

    public double IndentRightPoints { get; init; }

    /// <summary>
    /// First-line offset in points relative to the left indent. Positive is a first-line indent,
    /// negative is a hanging indent.
    /// </summary>
    public double IndentFirstLinePoints { get; init; }

    public double SpaceBeforePoints { get; init; }

    public double SpaceAfterPoints { get; init; }

    /// <summary>
    /// The raw line-spacing value, interpreted according to <see cref="LineRule"/>: 240ths of a
    /// line for Auto, twips for Exact and AtLeast.
    /// </summary>
    public int Line { get; init; } = 240;

    public LineSpacingRule LineRule { get; init; } = LineSpacingRule.Auto;

    public bool ContextualSpacing { get; init; }

    public bool KeepNext { get; init; }

    public bool KeepLines { get; init; }

    public bool PageBreakBefore { get; init; }

    public bool WidowControl { get; init; } = true;

    public IReadOnlyList<TabStop> TabStops { get; init; } = [];

    /// <summary>The list this paragraph belongs to, or null when it is not a list item.</summary>
    public int? NumberingId { get; init; }

    /// <summary>Depth within the list, zero being the outermost level.</summary>
    public int NumberingLevel { get; init; }

    /// <summary>Formatting of the paragraph mark, which sets the height of an empty paragraph.</summary>
    public ResolvedRunFormat MarkFormat { get; init; } = new();

    /// <summary>Line spacing as a multiple of single spacing; only meaningful for Auto.</summary>
    public double LineSpacingMultiple => LineRule == LineSpacingRule.Auto ? Line / 240.0 : 1.0;

    /// <summary>Line spacing in points; only meaningful for Exact and AtLeast.</summary>
    public double LineSpacingPoints => Units.TwipsToPoints(Line);
}
