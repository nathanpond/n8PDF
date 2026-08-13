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
                Advance = font.GetAdvanceWidth(glyph),
                IsMark = IsMark(font, glyph, codePoint)
            });
        }

        var plan = ShapingPlan.For(text);

        if (plan.DecomposesMarks) Decompose(font, buffer);

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
                item.Component, item.Merged, item.Standing);
        }

        return new ShapedText(text, glyphs);
    }

    /// <summary>
    /// Writes a mark that stands for two as the two it stands for.
    /// </summary>
    /// <remarks>
    /// Several of these scripts have a vowel written on both sides of its consonant at once, and
    /// store it as one character. It cannot be drawn as one: one half goes to the left of the
    /// letter and the other to the right, and everything that follows — which half is moved, what
    /// the font is asked for — is about the halves. The database says what the halves are.
    ///
    /// Only marks are taken apart, and only where the font can draw the pieces. A letter with an
    /// accent is left alone: it is one character in the text and one glyph on the page, and taking
    /// it apart would draw the accent twice as far from its letter as the face intends.
    /// </remarks>
    private static void Decompose(TrueTypeFont font, List<ShapeItem> buffer)
    {
        for (var i = 0; i < buffer.Count; i++)
        {
            var item = buffer[i];

            // Asked of the character rather than of the glyph: whether a font happens to file a
            // vowel sign among its letters says nothing about whether the vowel is written on two
            // sides of its consonant.
            if (item.CodePoint == 0 || !IsMark(item.CodePoint)) continue;

            var pieces = char.ConvertFromUtf32(item.CodePoint)
                .Normalize(System.Text.NormalizationForm.FormD);

            if (pieces.Length < 2) continue;

            var glyphs = new List<ushort>();

            foreach (var piece in pieces)
            {
                var glyph = font.GetGlyphIndex(piece);
                if (glyph == 0) break;

                glyphs.Add(glyph);
            }

            // A font that cannot draw the halves is asking for the whole, and gets it.
            if (glyphs.Count != pieces.Length) continue;

            buffer[i] = new ShapeItem(glyphs[0], item.Cluster, item.Mask, pieces[0])
            {
                Advance = font.GetAdvanceWidth(glyphs[0]),
                IsMark = IsMark(font, glyphs[0], pieces[0]),
                Standing = pieces[0].ToString()
            };

            for (var piece = 1; piece < glyphs.Count; piece++)
            {
                // Each half stands for the half it is, which is what Word writes into its own
                // files: one character taken apart is read back as the two it was made of.
                buffer.Insert(i + piece, new ShapeItem(glyphs[piece], item.Cluster, item.Mask,
                    pieces[piece])
                {
                    Advance = font.GetAdvanceWidth(glyphs[piece]),
                    IsMark = IsMark(font, glyphs[piece], pieces[piece]),
                    Standing = pieces[piece].ToString()
                });
            }

            i += glyphs.Count - 1;
        }
    }

    /// <summary>
    /// Whether a glyph is a mark: what the font says, or what the character is where the font says
    /// nothing.
    /// </summary>
    private static bool IsMark(TrueTypeFont font, ushort glyph, int codePoint)
    {
        if (font.Classes is { Classifies: true } classes) return classes.IsMark(glyph);

        return IsMark(codePoint);
    }

    /// <summary>Whether the character is one Unicode calls a mark.</summary>
    private static bool IsMark(int codePoint) =>
        System.Globalization.CharUnicodeInfo.GetUnicodeCategory(codePoint)
            is System.Globalization.UnicodeCategory.NonSpacingMark
            or System.Globalization.UnicodeCategory.SpacingCombiningMark
            or System.Globalization.UnicodeCategory.EnclosingMark;
}
