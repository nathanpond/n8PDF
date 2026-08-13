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

        // A script that joins its letters is drawn from different glyphs depending on what stands
        // beside each one, which has to be settled before anything is measured.
        var forms = Text.ArabicJoining.Joins(text) ? Text.ArabicJoining.Forms(text) : null;

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

            // The shape a joining letter takes: the same character, a different glyph.
            if (forms is not null && font.Substitution is { } substitution)
            {
                glyph = substitution.Substitute(forms[cluster] switch
                {
                    Text.JoiningForm.Initial => "init",
                    Text.JoiningForm.Medial => "medi",
                    Text.JoiningForm.Final => "fina",
                    _ => "isol"
                }, glyph);
            }

            glyphs.Add(new ShapedGlyph(glyph, font.GetAdvanceWidth(glyph), 0, 0, cluster));
        }

        // Some pairs may not be written as two. Composing a shadda and the vowel beside it into
        // the one mark the font holds for the pair is asked of every script and is done for all of
        // them; writing lam and alef as one shape belongs to the scripts that join, and is not
        // asked of a font drawing Latin, where it would bring in the ligatures Word leaves off.
        Join(font, glyphs, forms is not null ? ["ccmp", "rlig", "liga"] : ["ccmp"]);

        if (applyKerning) Kern(font, glyphs);

        // Turned round before the marks are placed rather than after. A mark is moved from where
        // the pen stands when it is drawn, and turning the order round moves the pen: a mark
        // placed while it followed its letter, and then drawn before it, lands a letter's width
        // to the left of where it belongs.
        if (rightToLeft) glyphs.Reverse();

        Attach(font, glyphs, rightToLeft);

        return new ShapedText(text, [.. glyphs]);
    }

    /// <summary>
    /// Writes as one glyph what the font says may not be written as two.
    /// </summary>
    /// <remarks>
    /// A ligature is not decoration here. Lam followed by alef is written as a single shape in
    /// Arabic and drawing the two apart is a spelling mistake rather than a plain one. The
    /// glyphs that make it are replaced by the one it is, and the cluster of the first is kept, so
    /// that what the ligature stands for can still be found in the text it came from.
    /// </remarks>
    private static void Join(TrueTypeFont font, List<ShapedGlyph> glyphs, string[] features)
    {
        if (font.Substitution is not { } substitution) return;

        var sequence = new List<ushort>();

        // In the order the features are meant to run: the marks are combined first, so that what
        // the letters reach across afterwards is the mark they have become.
        foreach (var feature in features)
        {
            for (var i = 0; i < glyphs.Count; i++)
            {
                sequence.Clear();
                foreach (var glyph in glyphs) sequence.Add(glyph.Glyph);

                if (substitution.Ligature(feature, sequence, i, font.IsMark) is not { } made) continue;

                // Where in the text the letters it was made of came from, so that what is drawn
                // as one shape can still be read back as the several characters it stands for.
                var merged = new List<int>();

                foreach (var component in made.Taken)
                {
                    if (glyphs[component].Merged is { } already) merged.AddRange(already);
                    else merged.Add(glyphs[component].Cluster);
                }

                glyphs[i] = glyphs[i] with
                {
                    Glyph = made.Glyph,
                    Advance = font.GetAdvanceWidth(made.Glyph),
                    Merged = [.. merged]
                };

                // A mark the match reached across was written over one of the letters the shape
                // now stands for, and the font offers a place for each of them. Which letter is
                // how many components stand before the mark; a mark past the last of them was
                // written over the last.
                for (var j = made.Taken[0] + 1; j < glyphs.Count; j++)
                {
                    // Over the letters the shape is made of and the marks they carry, and no
                    // further: the first glyph that is neither is not part of this shape.
                    if (Array.IndexOf(made.Taken, j) >= 0) continue;
                    if (!font.IsMark(glyphs[j].Glyph)) break;

                    var component = 0;
                    while (component + 1 < made.Taken.Length && made.Taken[component + 1] < j) component++;

                    glyphs[j] = glyphs[j] with { Component = component };
                }

                // What it was made of goes; anything the match reached across stays where it was.
                for (var k = made.Taken.Length - 1; k >= 1; k--) glyphs.RemoveAt(made.Taken[k]);
            }
        }
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
    /// against the first rather than against the letter. Which of the two any mark is placed
    /// against is not settled by which stands nearer — it is settled by the font, whose tables are
    /// tried in the order it lists them — and the movement is measured from where the pen stood
    /// when that glyph began, so everything between counts: one way where what it is drawn on has
    /// already been drawn, and the other way where it has not.
    /// </remarks>
    /// <param name="rightToLeft">
    /// Whether the glyphs are in the order they are drawn from the right, in which a mark is
    /// reached before the letter it belongs to rather than after it.
    /// </param>
    private static void Attach(TrueTypeFont font, List<ShapedGlyph> glyphs, bool rightToLeft)
    {
        var step = rightToLeft ? 1 : -1;

        for (var i = 0; i < glyphs.Count; i++)
        {
            if (!font.IsMark(glyphs[i].Glyph)) continue;

            var next = i + step;
            if (next < 0 || next >= glyphs.Count) continue;

            // The mark beside it, where the one beside it is a mark, and the nearest letter
            // whichever it is. The font decides which of the two this mark is drawn on.
            var neighbour = font.IsMark(glyphs[next].Glyph) ? glyphs[next].Glyph : (ushort?)null;

            var at = next;
            while (at >= 0 && at < glyphs.Count && font.IsMark(glyphs[at].Glyph)) at += step;

            var letter = at >= 0 && at < glyphs.Count ? glyphs[at].Glyph : (ushort?)null;

            if (font.GetMarkOffset(glyphs[i].Glyph, letter, neighbour, glyphs[i].Component)
                is not { } offset) continue;

            // How far apart the two pens stand. Where what it is drawn on came first the pen has
            // moved on since, and the mark must come back by that much; where the mark comes
            // first that pen is still ahead of it, and the mark must reach forward instead.
            var reference = offset.OnMark ? next : at;
            var between = 0;

            for (var j = Math.Min(i, reference); j < Math.Max(i, reference); j++)
                between += glyphs[j].Advance;

            glyphs[i] = glyphs[i] with
            {
                XOffset = (rightToLeft ? between : -between) + offset.X + glyphs[i].XOffset,
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
