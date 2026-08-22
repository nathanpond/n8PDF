using n8PDF.Ooxml;

namespace n8PDF.Styling;

/// <summary>
/// Character formatting after the full cascade has been applied: every value is concrete, with
/// no "inherit" left anywhere. Layout and rendering consume only this, never the raw XML.
/// </summary>
internal sealed record ResolvedRunFormat
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

    /// <summary>
    /// The box round this run, from <c>w:bdr</c>. It makes the line it is on taller: run-border-probe
    /// measures a step of the grid and the line's own weight above the text, and the weight below.
    /// </summary>
    public ParagraphBorderEdge? Border { get; init; }

    /// <summary>The mark drawn over each character of this run, from <c>w:em</c>.</summary>
    public EmphasisMark Emphasis { get; init; }

    /// <summary>
    /// The background behind this run, from a <c>w:shd</c> of its own. A highlight is drawn
    /// instead of it where the run has both: see RunShadingTests.
    /// </summary>
    public Shading Shading { get; init; }

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
    /// A share of the type size, and a fitted one: <c>superscript-shift-probe</c> puts the
    /// question to Word across eleven faces and five sizes, and what comes back is a rule this
    /// cannot reproduce.
    ///
    /// It is not a share of the size. For every face the sizes disagree: Times New Roman wants at
    /// least 0.375 of the size at eight point and at most 0.350 at twelve, and the two cannot both
    /// be had. Nor is it a share of anything the face declares — not its ascent, its descent, its
    /// cap height, its x-height, nor the superscript offset in its own <c>OS/2</c> table. Calibri
    /// and Candara have the same ascent, descent, cap height and x-height to four decimal places,
    /// and Word raises a superscript 0.3325 of the size in one and 0.4525 in the other. No linear
    /// combination of those metrics comes within twenty times the precision of the measurement.
    ///
    /// So a share of the size it stays, fitted to what was measured rather than guessed:
    ///
    ///   Times New Roman, eight point to ninety-six    every one within a step of the grid
    ///   Arial, the same                               within three steps, and within one below 48pt
    ///   Calibri, the same                             within ten, and within three below 48pt
    ///
    /// which is as close as one number comes to a rule with a face in it: 0.358 is what Times New
    /// Roman wants across every size measured, Arial wants 0.35, and Calibri 0.333. The number
    /// here follows Times, which is what superscript-probe is written in and what the comparison
    /// against Word therefore holds. LineBoxTests states the gap case by case.
    ///
    /// The lowered run is fitted the same way, and 0.083 is the share that keeps all three faces
    /// within two steps at the sizes a document uses.
    /// </remarks>
    public double BaselineShiftPoints => PositionPoints + VerticalAlignment switch
    {
        VerticalTextAlignment.Superscript => FontSizePoints * 0.358,
        VerticalTextAlignment.Subscript => FontSizePoints * -0.083,
        _ => 0
    };

    /// <summary>
    /// How far <c>w:position</c> raises the run off its baseline, negative to lower it. Unlike
    /// the shift a superscript takes, this one is stated rather than worked out — a dropped
    /// capital is written with the drop Word measured when it made it.
    /// </summary>
    public double PositionPoints { get; init; }

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
internal sealed record ResolvedParagraphFormat
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

    /// <summary>
    /// Whether the margin's numbering passes this paragraph over. Such a paragraph is not counted
    /// either, so the line after it carries the number it would have had.
    /// </summary>
    public bool SuppressLineNumbers { get; init; }

    /// <summary>The box round this paragraph, from <c>w:pBdr</c>, or null where it has none.</summary>
    public ParagraphBorders? Borders { get; init; }

    /// <summary>The background behind this paragraph, from <c>w:shd</c>.</summary>
    public Shading Shading { get; init; }

    /// <summary>The frame round this paragraph, or null where it has none.</summary>
    public FrameProperties? Frame { get; init; }

    /// <summary>Whether this paragraph's words are left whole at the ends of its lines.</summary>
    public bool SuppressAutoHyphens { get; init; }

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
    public double LineSpacingMultiple =>
        LineRule is LineSpacingRule.Auto or LineSpacingRule.Scaled ? Line / 240.0 : 1.0;

    /// <summary>Line spacing in points; only meaningful for Exact and AtLeast.</summary>
    public double LineSpacingPoints => Units.TwipsToPoints(Line);
}
