using n8PDF.Fonts.OpenType;

namespace n8PDF.Fonts;

/// <summary>
/// Turns text into the glyphs that draw it.
/// </summary>
/// <remarks>
/// What the shaper does is decided by the writing in front of it. A character takes the glyph the
/// font's character map gives it; a letter of a script that joins takes the shape its neighbours
/// call for; a syllable of an Indic script is put into the order it is written in, which is not the
/// order it is stored in; letters the font says may not be written apart are written as one; a pair
/// is drawn closer together where the face says so; and a mark is moved onto what it belongs to.
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
    /// <param name="rightToLeft">
    /// Whether the run is drawn from the right. Shaping happens in the order the text is read
    /// whichever way it is drawn — which letter joins to which, and which mark belongs to which
    /// letter, are questions about the text and not about the page — and the glyphs are turned
    /// round afterwards. Each keeps its own advance and its own offset, both of which are measured
    /// from where the glyph itself begins, so turning the order round moves everything to the
    /// right place and nothing else.
    /// </param>
    public static ShapedText Shape(
        TrueTypeFont font, string text, bool applyKerning = false, bool rightToLeft = false)
    {
        if (string.IsNullOrEmpty(text)) return ShapedText.Empty;

        var buffer = new List<ShapeItem>(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var cluster = i;
            int codePoint = text[i];

            // A character outside the basic multilingual plane is written as two, and is one glyph
            // rather than two broken ones.
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(text[i], text[i + 1]);
                i++;
            }

            var glyph = font.GetGlyphIndex(codePoint);

            buffer.Add(new ShapeItem(glyph, cluster, ShapingPlan.Everywhere, codePoint)
            {
                Advance = font.GetAdvanceWidth(glyph)
            });
        }

        var plan = ShapingPlan.For(text);

        plan.Substitute(font, text, buffer);

        // Whatever the glyphs have become, they advance the pen by their own widths until
        // positioning says otherwise.
        foreach (var item in buffer) item.Advance = font.GetAdvanceWidth(item.Glyph);

        plan.Position(font, buffer, applyKerning);

        Positioner.Resolve(buffer, rightToLeft);

        if (rightToLeft) buffer.Reverse();

        var glyphs = new ShapedGlyph[buffer.Count];

        for (var i = 0; i < buffer.Count; i++)
        {
            var item = buffer[i];

            glyphs[i] = new ShapedGlyph(
                item.Glyph, item.Advance, item.XOffset, item.YOffset, item.Cluster,
                item.Component, item.Merged);
        }

        return new ShapedText(text, glyphs);
    }
}
