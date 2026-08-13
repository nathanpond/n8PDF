namespace n8PDF.Fonts.OpenType;

/// <summary>
/// One glyph while it is being shaped: what it is at this moment, where in the text it came from,
/// and what may still be done to it.
/// </summary>
/// <remarks>
/// Held as an object rather than a value because shaping is a sequence of changes to a run and not
/// a calculation over it — a glyph is substituted, moved, attached to another, and each step has
/// to see what the last one did.
/// </remarks>
internal sealed class ShapeItem
{
    public ShapeItem(ushort glyph, int cluster, uint mask, int codePoint = 0)
    {
        Glyph = glyph;
        Cluster = cluster;
        Mask = mask;
        CodePoint = codePoint;
    }

    public ushort Glyph { get; set; }

    /// <summary>Where in the text this glyph came from, as an index into it.</summary>
    public int Cluster { get; set; }

    /// <summary>
    /// The character it was made from. Kept because the shapers of the scripts that reorder ask
    /// what a character is rather than what glyph it became — and the two part company as soon as
    /// the first substitution runs.
    /// </summary>
    public int CodePoint { get; }

    /// <summary>
    /// Where the other characters this glyph stands for came from, where it stands for several.
    /// </summary>
    public int[]? Merged { get; set; }

    /// <summary>
    /// Which features may still apply here. A script whose rules differ from letter to letter
    /// within one syllable — as the Indic ones do — says so by giving each glyph a different set.
    /// </summary>
    public uint Mask { get; set; }

    /// <summary>Which letter of a ligature this glyph belongs to, where it is a mark on one.</summary>
    public int Component { get; set; }

    /// <summary>What this glyph is to the shaper of its script, and where in a syllable it sits.</summary>
    public byte Category { get; set; }

    public byte Position { get; set; }

    /// <summary>The syllable this glyph belongs to, numbered from the start of the run.</summary>
    public int Syllable { get; set; }

    /// <summary>
    /// Whether this glyph is what a lookup made of several, and whether it is one of several a
    /// lookup made of one.
    /// </summary>
    /// <remarks>
    /// The Indic rules are written partly in terms of what the font managed to do: a consonant is
    /// moved before the base only if the feature that asks for it actually made a shape, and a
    /// repha is moved only if the two letters it was made of really did become one. So what
    /// happened to each glyph has to be remembered, not merely done.
    /// </remarks>
    public bool Ligated { get; set; }

    public bool Multiplied { get; set; }

    public bool Substituted { get; set; }

    /// <summary>What it was made of, where a lookup made it of several.</summary>
    public bool LigatedAndDidNotMultiply => Ligated && !Multiplied;

    // ----- what positioning has done to it -----

    public int Advance { get; set; }

    public int XOffset { get; set; }

    public int YOffset { get; set; }

    /// <summary>
    /// The glyph this one is attached to, as a distance in buffer positions, or nought where it is
    /// attached to nothing. Kept as a distance so that it survives the buffer being turned round.
    /// </summary>
    public int AttachedTo { get; set; }

    /// <summary>Whether what it is attached to is a mark, which is attached to something in turn.</summary>
    public bool AttachedToMark { get; set; }

    public override string ToString() => $"{Glyph}/{Cluster}";
}
