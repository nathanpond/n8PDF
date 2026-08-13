namespace n8PDF.Text;

/// <summary>
/// What a run of text looks like once it is drawn rather than stored.
/// </summary>
internal static class BidiText
{
    /// <summary>
    /// A run of one direction, in the order it is drawn. A run that goes right to left comes out
    /// back to front, with the marks kept on the letters they belong to and the brackets facing
    /// the way the reader is going.
    /// </summary>
    public static string Drawn(string text, byte level)
    {
        if ((level & 1) == 0 || text.Length == 0) return text;

        var levels = new byte[text.Length];
        Array.Fill(levels, level);

        var order = Bidi.Reorder(levels, text);
        var drawn = new char[text.Length];

        for (var i = 0; i < order.Length; i++) drawn[i] = Bidi.Mirror(text[order[i]]);

        return new string(drawn);
    }
}
