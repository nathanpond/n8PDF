using n8PDF.Ooxml;

namespace n8PDF.Styling;

/// <summary>
/// Applies the WordprocessingML formatting cascade, turning raw property elements into concrete
/// <see cref="ResolvedParagraphFormat"/> and <see cref="ResolvedRunFormat"/> values.
/// </summary>
/// <remarks>
/// The order is: document defaults, then the style of the table the paragraph is in, then the
/// paragraph style's inheritance chain from the most general ancestor down, then the character
/// style's chain, then formatting applied directly to the element. Toggle properties do not
/// simply override — see <see cref="ApplyToggle"/>.
/// </remarks>
public sealed class StyleResolver(
    StyleDefinitions styles,
    DocumentTheme? theme = null,
    bool applyBuiltInStyleDefaults = true,
    NumberingDefinitions? numbering = null)
{
    /// <summary>
    /// Word's fallback when no style, default, or direct formatting names a font. Documents in
    /// the wild rarely reach this, but a hand-written one can.
    /// </summary>
    public const string FallbackFontFamily = "Times New Roman";

    /// <summary>The ECMA-376 default for <c>w:sz</c> when nothing specifies a size.</summary>
    public const double FallbackFontSizePoints = 10;

    private readonly StyleDefinitions _styles = styles;
    private readonly DocumentTheme _theme = theme ?? new DocumentTheme();
    private readonly bool _applyBuiltInStyleDefaults = applyBuiltInStyleDefaults;
    private readonly NumberingDefinitions _numbering = numbering ?? new NumberingDefinitions();

    /// <summary>The list definitions, needed to resolve a paragraph's numbering level.</summary>
    public NumberingDefinitions Numbering => _numbering;

    public StyleDefinitions Styles => _styles;

    public DocumentTheme Theme => _theme;

    /// <summary>Resolves paragraph formatting for a paragraph's own properties.</summary>
    public ResolvedParagraphFormat ResolveParagraph(ParagraphProperties? direct)
    {
        var accumulator = new ParagraphAccumulator();

        // 0. Word's built-in defaults, beneath everything the document says. A document that
        //    declares its Normal style as an empty element still gets Word's spacing for it.
        if (_applyBuiltInStyleDefaults)
            accumulator.Apply(WordBuiltInStyles.NormalParagraphProperties);

        // 1. Document defaults.
        accumulator.Apply(_styles.DefaultParagraphProperties);

        // 1a. The style of the table this paragraph sits in, if it sits in one. It goes here
        //     because a paragraph style beats it: measured from table-style-conditional-probe,
        //     whose last page sets one cell against the other in the same table.
        foreach (var fromTable in direct?.FromTableStyle?.Paragraph ?? [])
            accumulator.Apply(fromTable);

        // 2. The default paragraph style, which direct pStyle-less paragraphs inherit from.
        var styleId = direct?.StyleId ?? _styles.DefaultParagraphStyleId;
        foreach (var style in _styles.GetInheritanceChain(styleId))
        {
            if (style.ParagraphProperties is not null) accumulator.Apply(style.ParagraphProperties);
        }

        // 3. The numbering level, which is where a list item's indents come from. It sits below
        //    direct formatting but above the styles, so a document can still move one item.
        //    The list itself may be named by a style rather than by the paragraph, so which
        //    level applies is only known after the style chain has been walked.
        var numberingId = direct?.NumberingId ?? accumulator.NumberingId;
        var numberingLevel = direct?.NumberingLevel ?? accumulator.NumberingLevel ?? 0;

        if (numberingId is { } id && _numbering.GetLevel(id, numberingLevel)?.ParagraphProperties is { } levelProperties)
            accumulator.Apply(levelProperties);

        // 4. Direct formatting on the paragraph itself.
        if (direct is not null) accumulator.Apply(direct);

        var format = accumulator.Build(
            ResolveRun(direct, direct?.MarkRunProperties), direct?.StyleId ?? styleId);

        return format with { NumberingId = numberingId, NumberingLevel = numberingLevel };
    }

    /// <summary>
    /// Resolves character formatting for a run, given the paragraph it sits in. The paragraph is
    /// needed because a paragraph style contributes run properties to every run inside it.
    /// </summary>
    public ResolvedRunFormat ResolveRun(ParagraphProperties? paragraph, RunProperties? direct)
    {
        var accumulator = new RunAccumulator();

        // 1. Document defaults.
        accumulator.Apply(_styles.DefaultRunProperties, isDirect: false);

        // 1a. The table style, beneath the paragraph style for the same reason as above.
        foreach (var fromTable in paragraph?.FromTableStyle?.Run ?? [])
            accumulator.Apply(fromTable, isDirect: false);

        // 2. Run properties contributed by the paragraph style chain.
        var paragraphStyleId = paragraph?.StyleId ?? _styles.DefaultParagraphStyleId;
        foreach (var style in _styles.GetInheritanceChain(paragraphStyleId))
        {
            if (style.RunProperties is not null) accumulator.Apply(style.RunProperties, isDirect: false);
        }

        // 3. The character style chain referenced by the run.
        foreach (var style in _styles.GetInheritanceChain(direct?.StyleId))
        {
            if (style.RunProperties is not null) accumulator.Apply(style.RunProperties, isDirect: false);
        }

        // 4. Direct formatting on the run.
        if (direct is not null) accumulator.Apply(direct, isDirect: true);

        return accumulator.Build(_theme);
    }

    /// <summary>
    /// Combines a toggle property value into the accumulated value.
    /// </summary>
    /// <remarks>
    /// Toggle properties (bold, italic, caps, small caps, strike) do not behave like ordinary
    /// overrides inside the style hierarchy. Two styles that both turn bold on cancel out — a
    /// bold character style applied inside a bold heading produces non-bold text. An explicit
    /// "off" is not part of that dance: it forces the property off outright. Direct formatting
    /// on the run always sets the value absolutely.
    /// </remarks>
    internal static bool ApplyToggle(bool current, bool value, bool isDirect)
    {
        if (isDirect) return value;
        return value ? !current : false;
    }

    /// <summary>Accumulates run properties through the cascade.</summary>
    private sealed class RunAccumulator
    {
        private string? _fontName;
        private string? _fontTheme;
        private double? _sizePoints;
        private bool _bold;
        private bool _italic;
        private bool _caps;
        private bool _smallCaps;
        private bool _strike;
        private bool _hidden;
        private UnderlineStyle? _underline;
        private string? _color;
        private string? _colorTheme;
        private string? _highlight;
        private VerticalTextAlignment? _verticalAlignment;
        private double? _characterSpacingPoints;
        private int? _kerningMinimumHalfPoints;
        private double? _scale;

        public void Apply(RunProperties source, bool isDirect)
        {
            // A theme slot and a literal name are mutually exclusive per level: whichever this
            // level specifies replaces whatever an outer level said.
            if (source.AsciiTheme is not null || source.HighAnsiTheme is not null)
            {
                _fontTheme = source.AsciiTheme ?? source.HighAnsiTheme;
                _fontName = null;
            }
            else if (source.AsciiFont is not null || source.HighAnsiFont is not null)
            {
                _fontName = source.AsciiFont ?? source.HighAnsiFont;
                _fontTheme = null;
            }

            if (source.SizeHalfPoints is { } size) _sizePoints = Units.HalfPointsToPoints(size);

            if (source.Bold is { } bold) _bold = ApplyToggle(_bold, bold, isDirect);
            if (source.Italic is { } italic) _italic = ApplyToggle(_italic, italic, isDirect);
            if (source.Caps is { } caps) _caps = ApplyToggle(_caps, caps, isDirect);
            if (source.SmallCaps is { } smallCaps) _smallCaps = ApplyToggle(_smallCaps, smallCaps, isDirect);
            if (source.Strike is { } strike) _strike = ApplyToggle(_strike, strike, isDirect);
            if (source.Vanish is { } vanish) _hidden = ApplyToggle(_hidden, vanish, isDirect);

            if (source.Underline is { } underline) _underline = underline;
            if (source.Color is not null) _color = source.Color;
            if (source.ColorThemeSlot is not null) _colorTheme = source.ColorThemeSlot;
            if (source.Highlight is not null) _highlight = source.Highlight;
            if (source.VerticalAlignment is { } vertical) _verticalAlignment = vertical;
            if (source.CharacterSpacingTwips is { } spacing)
                _characterSpacingPoints = Units.TwipsToPoints(spacing);

            if (source.KerningMinimumHalfPoints is { } kerning)
                _kerningMinimumHalfPoints = kerning;

            if (source.ScalePercent is { } scale && scale > 0) _scale = scale / 100.0;
        }

        public ResolvedRunFormat Build(DocumentTheme theme)
        {
            // A theme slot wins over any literal name still in play, because it is how Word
            // records "whatever the document's body font is".
            var family = theme.Resolve(_fontTheme) ?? _fontName ?? FallbackFontFamily;

            return new ResolvedRunFormat
            {
                FontFamily = family,
                FontSizePoints = _sizePoints ?? FallbackFontSizePoints,
                Bold = _bold,
                Italic = _italic,
                Caps = _caps,
                SmallCaps = _smallCaps,
                Strike = _strike,
                Hidden = _hidden,
                Underline = _underline ?? UnderlineStyle.None,
                ColorHex = theme.ResolveColor(_colorTheme) ?? _color,
                HighlightColor = _highlight,
                VerticalAlignment = _verticalAlignment ?? VerticalTextAlignment.Baseline,
                CharacterSpacingPoints = _characterSpacingPoints ?? 0,
                KerningMinimumHalfPoints = _kerningMinimumHalfPoints ?? 0,
                ScaleFactor = _scale ?? 1.0
            };
        }
    }

    /// <summary>
    /// Accumulates paragraph properties. These are ordinary overrides — the toggle rule applies
    /// only to character formatting.
    /// </summary>
    private sealed class ParagraphAccumulator
    {
        private Justification? _justification;
        private bool? _rightToLeft;
        private int? _indentLeft;
        private int? _indentRight;
        private int? _firstLine;
        private int? _hanging;
        private int? _spaceBefore;
        private int? _spaceAfter;
        private int? _line;
        private LineSpacingRule? _lineRule;
        private bool? _contextualSpacing;
        private bool? _keepNext;
        private bool? _keepLines;
        private bool? _pageBreakBefore;
        private bool? _widowControl;
        private int? _outlineLevel;
        private List<TabStop>? _tabStops;

        public int? NumberingId { get; private set; }

        public int? NumberingLevel { get; private set; }

        public void Apply(ParagraphProperties source)
        {
            if (source.Justification is { } justification) _justification = justification;
            if (source.RightToLeft is { } rightToLeft) _rightToLeft = rightToLeft;
            if (source.IndentLeftTwips is { } left) _indentLeft = left;
            if (source.IndentRightTwips is { } right) _indentRight = right;

            // firstLine and hanging are alternative spellings of the same offset, so setting one
            // must clear the other or a style's hanging indent survives a direct first-line one.
            if (source.IndentFirstLineTwips is { } firstLine)
            {
                _firstLine = firstLine;
                _hanging = null;
            }

            if (source.IndentHangingTwips is { } hanging)
            {
                _hanging = hanging;
                _firstLine = null;
            }

            if (source.SpacingBeforeTwips is { } before) _spaceBefore = before;
            if (source.SpacingAfterTwips is { } after) _spaceAfter = after;
            if (source.Line is { } line) _line = line;
            if (source.LineRule is { } rule) _lineRule = rule;
            if (source.ContextualSpacing is { } contextual) _contextualSpacing = contextual;
            if (source.KeepNext is { } keepNext) _keepNext = keepNext;
            if (source.KeepLines is { } keepLines) _keepLines = keepLines;
            if (source.PageBreakBefore is { } pageBreak) _pageBreakBefore = pageBreak;
            if (source.WidowControl is { } widow) _widowControl = widow;
            if (source.OutlineLevel is { } outline) _outlineLevel = outline;
            if (source.NumberingId is { } numberingId) NumberingId = numberingId;
            if (source.NumberingLevel is { } numberingLevel) NumberingLevel = numberingLevel;

            // Tab stops replace rather than merge; a level that declares any declares them all.
            if (source.TabStops.Count > 0) _tabStops = [.. source.TabStops];
        }

        public ResolvedParagraphFormat Build(ResolvedRunFormat markFormat, string? styleId) => new()
        {
            StyleId = styleId,
            RightToLeft = _rightToLeft ?? false,

            // A paragraph that runs right to left begins at the right, which is what a document
            // means by asking for neither one edge nor the other.
            Justification = _justification ?? (_rightToLeft == true ? Justification.Right : Justification.Left),
            IndentLeftPoints = Units.TwipsToPoints(_indentLeft ?? 0),
            IndentRightPoints = Units.TwipsToPoints(_indentRight ?? 0),
            IndentFirstLinePoints = _hanging is { } hanging
                ? -Units.TwipsToPoints(hanging)
                : Units.TwipsToPoints(_firstLine ?? 0),
            SpaceBeforePoints = Units.TwipsToPoints(_spaceBefore ?? 0),
            SpaceAfterPoints = Units.TwipsToPoints(_spaceAfter ?? 0),
            Line = _line ?? 240,
            LineRule = _lineRule ?? LineSpacingRule.Auto,
            ContextualSpacing = _contextualSpacing ?? false,
            KeepNext = _keepNext ?? false,
            KeepLines = _keepLines ?? false,
            PageBreakBefore = _pageBreakBefore ?? false,
            WidowControl = _widowControl ?? true,
            OutlineLevel = _outlineLevel,
            TabStops = _tabStops ?? [],
            MarkFormat = markFormat
        };
    }
}
