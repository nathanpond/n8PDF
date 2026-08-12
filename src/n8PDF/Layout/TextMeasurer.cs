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

        var units = 0;
        ushort previous = 0;
        var count = 0;

        foreach (var (glyph, _) in EnumerateGlyphs(font, text))
        {
            units += font.GetAdvanceWidth(glyph);
            if (applyKerning && previous != 0) units += font.GetKerning(previous, glyph);

            previous = glyph;
            count++;
        }

        return font.Metrics.ToPoints(units, fontSizePoints) + characterSpacingPoints * count;
    }

    /// <summary>Measures a single character in points.</summary>
    public static double MeasureCharacter(TrueTypeFont font, int codePoint, double fontSizePoints) =>
        font.Metrics.ToPoints(font.GetAdvanceWidth(font.GetGlyphIndex(codePoint)), fontSizePoints);

    /// <summary>
    /// Walks a string as glyph/code-point pairs, combining surrogate pairs into single code
    /// points so that characters outside the BMP are measured once rather than twice.
    /// </summary>
    public static IEnumerable<(ushort Glyph, int CodePoint)> EnumerateGlyphs(TrueTypeFont font, string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            int codePoint = text[i];
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            yield return (font.GetGlyphIndex(codePoint), codePoint);
        }
    }

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
