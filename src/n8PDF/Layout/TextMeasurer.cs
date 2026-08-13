using n8PDF.Fonts;
using n8PDF.Styling;

namespace n8PDF.Layout;

/// <summary>
/// Measures text using real font metrics. Every horizontal position on a page traces back to
/// this, so its unit handling is deliberately explicit.
/// </summary>
public static class TextMeasurer
{
    /// <summary>
    /// Measures a string in points.
    /// </summary>
    /// <param name="font">The face to measure with.</param>
    /// <param name="text">The text, already transformed for caps if applicable.</param>
    /// <param name="fontSizePoints">Size to measure at.</param>
    /// <param name="characterSpacingPoints">Extra spacing added after each character.</param>
    /// <param name="applyKerning">
    /// Whether to apply pair kerning. Off by default because Word does not kern unless a
    /// document explicitly enables it with <c>w:kern</c>; kerning by default would make every
    /// line marginally narrower than Word's.
    /// </param>
    public static double Measure(
        TrueTypeFont font,
        string text,
        double fontSizePoints,
        double characterSpacingPoints = 0,
        bool applyKerning = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        return Measure(TextShaper.Shape(font, text, applyKerning), font, fontSizePoints, characterSpacingPoints);
    }

    /// <summary>
    /// Measures text already shaped, which is how a width and the glyphs written for it come to
    /// be the same walk over the same text rather than two walks that have to agree.
    /// </summary>
    public static double Measure(
        ShapedText shaped, TrueTypeFont font, double fontSizePoints, double characterSpacingPoints = 0) =>
        font.Metrics.ToPoints(shaped.AdvanceUnits, fontSizePoints) + characterSpacingPoints * shaped.Count;

    /// <summary>
    /// The natural height of one line of text in this font at this size: the distance Word
    /// leaves between consecutive baselines at single spacing.
    /// </summary>
    public static double GetNaturalLineHeight(TrueTypeFont font, double fontSizePoints) =>
        font.Metrics.ToPoints(font.Metrics.DefaultLineHeight, fontSizePoints);

    /// <summary>Distance from the top of the line box down to the baseline.</summary>
    public static double GetAscent(TrueTypeFont font, double fontSizePoints) =>
        font.Metrics.ToPoints(font.Metrics.DefaultAscent, fontSizePoints);

    /// <summary>
    /// Applies the character-level text transforms that change what is actually drawn.
    /// </summary>
    /// <remarks>
    /// Small caps is approximated by upper-casing without the size reduction real small caps
    /// need; drawing it properly means splitting a run at every case change and rendering the
    /// originally-lowercase parts smaller.
    /// </remarks>
    public static string ApplyTextTransform(string text, ResolvedRunFormat format)
    {
        if (format.Caps || format.SmallCaps)
            return text.ToUpperInvariant();

        return text;
    }
}
