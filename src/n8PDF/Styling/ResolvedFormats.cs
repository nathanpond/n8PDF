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
    /// The type size, in half-points, at or above which the font's kerning applies. Zero means
    /// never, which is what a document that says nothing gets.
    /// </summary>
    public int KerningMinimumHalfPoints { get; init; }

    /// <summary>
    /// Whether this run's text is kerned: the document has to ask for it, and the type has to be
    /// at least as large as the size it named.
    /// </summary>
    public bool Kerned =>
        KerningMinimumHalfPoints > 0 && EffectiveFontSizePoints * 2 >= KerningMinimumHalfPoints;

    /// <summary>
    /// The size actually drawn at. Word renders superscripts and subscripts at a reduced size
    /// rather than merely shifting the baseline.
    /// </summary>
    public double EffectiveFontSizePoints =>
        VerticalAlignment == VerticalTextAlignment.Baseline ? FontSizePoints : FontSizePoints * 0.65;

    /// <summary>
    /// The size the line box is measured from, which is the size the run declares rather than the
    /// size it is drawn at.
    /// </summary>
    /// <remarks>
    /// A raised or lowered run keeps the line box of the size it was given: Word draws the glyphs
    /// smaller and moves them within that box, and the line is neither taller nor shorter for it.
    /// Measured from its export of <c>superscript-probe</c>, where a twenty point superscript in a
    /// twelve point line makes the line as tall as a twenty point one — above the baseline and
    /// below it, the descent being a twenty point run's rather than the drawn size's — while a
    /// twelve point superscript in a twelve point line changes nothing at all.
    /// </remarks>
    public double LineBoxFontSizePoints => FontSizePoints;

    /// <summary>
    /// Baseline shift in points; positive raises the text.
    /// </summary>
    /// <remarks>
    /// Both were measured from Word's export of <c>superscript-probe</c>, which carries each at
    /// twelve, twenty and forty point so that neither is a ratio fitted to one number. A lowered
    /// run drops about a twelfth of its size — 0.96pt at twelve point and 3.12 at forty — which is
    /// far less than it looks like it should, and was nearly twice that here until it was
    /// measured. A raised one rises about a third: Word's own is 4.08, 6.96 and 14.40 against the
    /// 4.20, 6.97 and 13.94 this gives, which is inside its own rounding of 1/300 inch at the two
    /// sizes a document is actually set in.
    /// </remarks>
    public double BaselineShiftPoints => VerticalAlignment switch
    {
        VerticalTextAlignment.Superscript => FontSizePoints * 0.35,
        VerticalTextAlignment.Subscript => FontSizePoints * -0.08,
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

    /// <summary>
    /// Whether the paragraph runs right to left. What that decides is which way the paragraph
    /// goes as a whole — where its lines begin, and which way round its runs are set when they
    /// have no direction of their own. Which way each character runs is the character's business.
    /// </summary>
    public bool RightToLeft { get; init; }

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

    /// <summary>
    /// The heading level this paragraph stands at, or null for body text. A table of contents
    /// gathers the paragraphs that have one.
    /// </summary>
    public int? OutlineLevel { get; init; }

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
