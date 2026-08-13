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

            glyphs.Add(new ShapedGlyph(glyph, font.GetAdvanceWidth(glyph), 0, 0, cluster));
        }

        if (applyKerning) Kern(font, glyphs);

        Attach(font, glyphs);

        return new ShapedText(text, [.. glyphs]);
    }

    /// <summary>
    /// Puts each mark where the face says it goes on what it is drawn on.
    /// </summary>
    /// <remarks>
    /// An accent, a Hebrew vowel point, an Arabic dot: none has a place of its own, and none can
    /// be drawn by advancing the pen. The face gives the mark an anchor and the letter an anchor
    /// and the two are brought together, which is a movement of the mark alone — the pen does not
    /// know it happened, and the letter after is set as though the mark were not there.
    ///
    /// A mark may be drawn on a mark, which is how a letter carries two: the second is placed
    /// against the first rather than against the letter. What each is placed against is therefore
    /// the nearest thing before it that is not a mark, or the mark before it, and the movement is
    /// measured from where the pen stood when that glyph began — so everything between is
    /// subtracted.
    /// </remarks>
    private static void Attach(TrueTypeFont font, List<ShapedGlyph> glyphs)
    {
        for (var i = 1; i < glyphs.Count; i++)
        {
            if (!font.IsMark(glyphs[i].Glyph)) continue;

            // What it is drawn on: the mark before it where there is one, and otherwise the
            // nearest letter.
            var onMark = font.IsMark(glyphs[i - 1].Glyph);
            var at = i - 1;

            if (!onMark)
            {
                while (at > 0 && font.IsMark(glyphs[at].Glyph)) at--;
            }

            if (font.GetMarkOffset(glyphs[i].Glyph, glyphs[at].Glyph, onMark) is not { } offset) continue;

            // The pen has moved on since that glyph began.
            var between = 0;
            for (var j = at; j < i; j++) between += glyphs[j].Advance;

            glyphs[i] = glyphs[i] with
            {
                XOffset = offset.X - between + glyphs[i].XOffset,
                YOffset = offset.Y + glyphs[i].YOffset
            };
        }
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
