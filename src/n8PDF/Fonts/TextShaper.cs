namespace n8PDF.Fonts;

/// <summary>
/// Turns text into the glyphs that draw it.
/// </summary>
/// <remarks>
/// What it does at present is what the writing it has met needs and no more: each character takes
/// the glyph the font's character map gives it, and a pair of them is drawn closer together where
/// the face says so. That is the whole of shaping for the Latin, Greek and Cyrillic scripts, and
/// none of it for the ones that join their letters or reorder them.
///
/// What matters is that it is one place rather than several. Measuring and drawing were working
/// the same thing out separately — the width of a line came from one walk over its characters and
/// the kerning written into the page from another — and two walks that must agree are two walks
/// that can disagree. They are now one, and what flows from here to the page is glyphs.
/// </remarks>
public static class TextShaper
{
    /// <summary>
    /// Shapes a run of text set in one face.
    /// </summary>
    /// <param name="applyKerning">
    /// Whether to draw a pair of glyphs closer together where the face says they should be. Word
    /// does not unless the document asks, so neither does this.
    /// </param>
    public static ShapedText Shape(TrueTypeFont font, string text, bool applyKerning = false)
    {
        if (string.IsNullOrEmpty(text)) return ShapedText.Empty;

        var glyphs = new List<ShapedGlyph>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var cluster = i;
            int codePoint = text[i];

            // A character outside the basic multilingual plane is written as two, and is one
            // glyph rather than two broken ones.
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            var glyph = font.GetGlyphIndex(codePoint);

            glyphs.Add(new ShapedGlyph(glyph, font.GetAdvanceWidth(glyph), cluster));
        }

        if (applyKerning) Kern(font, glyphs);

        return new ShapedText(text, [.. glyphs]);
    }

    /// <summary>
    /// Draws each pair the face names closer together, by shortening the advance of the glyph on
    /// the left of it. That is where kerning belongs: it is not a gap between two glyphs but a
    /// property of the first of them in that company, which is why it survives being carried
    /// through a pipeline of glyphs and would not survive one of characters.
    /// </summary>
    private static void Kern(TrueTypeFont font, List<ShapedGlyph> glyphs)
    {
        for (var i = 0; i + 1 < glyphs.Count; i++)
        {
            var kerning = font.GetKerning(glyphs[i].Glyph, glyphs[i + 1].Glyph);
            if (kerning == 0) continue;

            glyphs[i] = glyphs[i] with { Advance = glyphs[i].Advance + kerning };
        }
    }
}
