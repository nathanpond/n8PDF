namespace n8PDF.Fonts;

/// <summary>
/// One glyph as a shaper places it: which glyph, how far the pen moves after it, and which
/// character of the text it came from.
/// </summary>
/// <param name="Glyph">The glyph's number in the font.</param>
/// <param name="Advance">
/// How far the pen moves after drawing it, in the font's design units. Usually the glyph's own
/// advance, and not always: kerning is a shortening of the advance of the glyph on the left.
/// </param>
/// <param name="XOffset">
/// How far the glyph is drawn from where the pen stands, across, without moving the pen. Nought
/// for everything that has a place of its own, and not for a mark: a mark is drawn on the letter
/// before it rather than after it.
/// </param>
/// <param name="YOffset">The same, up the page.</param>
/// <param name="Cluster">
/// Where in the text this glyph came from, as an index into it. Several glyphs may share a
/// cluster and several characters may share a glyph, which is why this is an index into the text
/// rather than a count of glyphs.
/// </param>
/// <param name="Component">
/// Which letter of a ligature a mark was written over, counted from the first. Nought for
/// everything else, and for a mark on a letter that is only itself. A shape standing for several
/// letters offers a place for a mark over each of them, and this is what says which.
/// </param>
/// <param name="Merged">
/// Where in the text the other letters this glyph stands for came from, where it stands for
/// several. A ligature is one glyph for several characters, and a map from the page back to the
/// text that named only the first of them would lose the rest: a reader searching for the word
/// would not find it, and a reader copying the line would copy something shorter than what is
/// drawn.
/// </param>
public readonly record struct ShapedGlyph(
    ushort Glyph, int Advance, int XOffset, int YOffset, int Cluster, int Component = 0,
    int[]? Merged = null);

/// <summary>
/// Text turned into the glyphs that draw it.
/// </summary>
/// <remarks>
/// Everything downstream of this measures and draws glyphs rather than characters, which is the
/// only arrangement that can ever be right. A character is not a glyph: one may need several, as
/// an accent written apart from its letter does; several may need one, as a ligature does; and
/// which glyph a character takes can depend on its neighbours, as it does in every script that
/// joins its letters. None of that is true of the writing this converter has met so far, where a
/// character maps to a glyph and to nothing else — but a pipeline that carries characters as far
/// as the page cannot be told the difference, and one that carries glyphs can.
///
/// Advances are kept in the font's own units rather than in points so that this is a property of
/// the text and the face alone, measurable afterwards at whatever size the run is set in.
/// </remarks>
public sealed class ShapedText
{
    public static readonly ShapedText Empty = new(string.Empty, []);

    public ShapedText(string source, ShapedGlyph[] glyphs)
    {
        Source = source;
        Glyphs = glyphs;

        var units = 0;
        foreach (var glyph in glyphs) units += glyph.Advance;

        AdvanceUnits = units;
    }

    /// <summary>The text this was shaped from.</summary>
    public string Source { get; }

    public IReadOnlyList<ShapedGlyph> Glyphs { get; }

    /// <summary>What the whole run advances the pen, in the font's design units.</summary>
    public int AdvanceUnits { get; }

    public int Count => Glyphs.Count;

    /// <summary>
    /// The code point a glyph stands for, for the places one is all that can be carried: the
    /// character map of an embedded font, which maps a character to a glyph and not the reverse.
    /// A glyph standing for several characters is named by the first of them.
    /// </summary>
    public int CodePointOf(int index)
    {
        var at = Glyphs[index].Cluster;
        if (at < 0 || at >= Source.Length) return 0;

        return char.IsHighSurrogate(Source[at]) && at + 1 < Source.Length &&
               char.IsLowSurrogate(Source[at + 1])
            ? char.ConvertToUtf32(Source[at], Source[at + 1])
            : Source[at];
    }

    /// <summary>
    /// What a glyph stands for, for the map a PDF carries so that its text can be searched and
    /// copied. Usually one character, and all of them where a ligature has written several as one
    /// shape — which is what lets a reader copy the word out of the page and get the word.
    /// </summary>
    public string TextOf(int index)
    {
        var glyph = Glyphs[index];

        if (glyph.Merged is not { Length: > 1 } merged) return At(glyph.Cluster);

        var text = new System.Text.StringBuilder();
        foreach (var cluster in merged) text.Append(At(cluster));

        return text.ToString();
    }

    private string At(int cluster)
    {
        if (cluster < 0 || cluster >= Source.Length) return string.Empty;

        return char.IsHighSurrogate(Source[cluster]) && cluster + 1 < Source.Length &&
               char.IsLowSurrogate(Source[cluster + 1])
            ? Source.Substring(cluster, 2)
            : Source[cluster].ToString();
    }
}
