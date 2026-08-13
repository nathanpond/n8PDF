namespace n8PDF.Text;

/// <summary>
/// What a run of text is drawn as, where that differs from what it is stored as.
/// </summary>
internal static class BidiText
{
    /// <summary>
    /// A run of one direction, ready to be shaped. It keeps the order it is read in — which letter
    /// joins to which is a question about the text, and a shaper handed a word backwards answers
    /// it backwards — and what changes is only the characters that are drawn as something else
    /// where the line runs the other way: a bracket faces the way the reader is going.
    /// </summary>
    /// <remarks>
    /// Turning the run round is left to the shaper, which does it to the glyphs. It is the same
    /// movement either way, and doing it to the glyphs is the only place it can be done once the
    /// text has been shaped as text rather than as a row of letters.
    /// </remarks>
    public static string Mirrored(string text, byte level)
    {
        if ((level & 1) == 0 || text.Length == 0) return text;

        var drawn = new char[text.Length];

        for (var i = 0; i < text.Length; i++) drawn[i] = Bidi.Mirror(text[i]);

        return new string(drawn);
    }
}
