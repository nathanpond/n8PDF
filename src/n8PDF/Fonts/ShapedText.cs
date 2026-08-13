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
public readonly record struct ShapedGlyph(ushort Glyph, int Advance, int XOffset, int YOffset, int Cluster);

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
    /// The code point a glyph stands for, for the map a PDF carries so that its text can be
    /// searched and copied. A glyph standing for several characters is named by the first of
    /// them, which is what a reader needs to find the word again.
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
}
