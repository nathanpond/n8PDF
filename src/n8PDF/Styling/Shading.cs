namespace n8PDF.Styling;

/// <summary>
/// A <c>w:shd</c>: a background colour, a pattern colour, and how much of the second is laid over
/// the first.
/// </summary>
/// <remarks>
/// The pattern is the interesting part. <c>clear</c> is the fill alone and <c>solid</c> the pattern
/// colour alone; every <c>pctN</c> between them is a straight blend of the two, which
/// paragraph-shading-probe measures: red on yellow at 10, 25, 50 and 75 per cent comes out of Word
/// as #FFE500, #FFBF00, #FF7F00 and #FF4000 — the green channel at 0.9, 0.75, 0.5 and 0.25 of its
/// own value, to the nearest 255th with a half going down.
///
/// The named textures — <c>horzStripe</c>, <c>thinDiagCross</c> and the rest — are hatchings rather
/// than blends, and are not measured here. They take the fill alone, which is the nearest thing to
/// them a solid rectangle can be.
/// </remarks>
internal readonly record struct Shading(string? Fill, string? Pattern, string? Color)
{
    /// <summary>Whether this says nothing at all, which is what most paragraphs say.</summary>
    public bool IsEmpty => Fill is null && Pattern is null && Color is null;

    /// <summary>
    /// The colour to paint, or null where the shading amounts to nothing: no fill, an automatic
    /// one, or a pattern of none.
    /// </summary>
    public (double Red, double Green, double Blue)? Resolve()
    {
        var pattern = Pattern ?? "clear";
        if (pattern is "nil") return null;

        var share = PatternShare(pattern);

        // With no pattern colour there is nothing to lay over the fill, whatever share was asked
        // for; with no fill there is nothing under the pattern, so the pattern colour stands alone.
        var fill = Parse(Fill);
        var color = Parse(Color);

        if (share <= 0 || color is null) return fill;
        if (fill is null) return share >= 1 ? color : null;

        return (Mix(fill.Value.Red, color.Value.Red, share),
            Mix(fill.Value.Green, color.Value.Green, share),
            Mix(fill.Value.Blue, color.Value.Blue, share));
    }

    /// <summary>
    /// Word works the blend in whole 255ths and puts a half down: yellow's green channel at a
    /// tenth of red is 229.5 and comes out 229, where at three quarters it is 63.75 and comes out
    /// 64.
    /// </summary>
    private static double Mix(double from, double to, double share) =>
        Math.Ceiling((from + (to - from) * share) * 255 - 0.5) / 255;

    /// <summary>How much of the pattern colour a <c>w:val</c> asks for, from none to all of it.</summary>
    private static double PatternShare(string pattern)
    {
        if (pattern is "solid") return 1;
        if (!pattern.StartsWith("pct", StringComparison.Ordinal)) return 0;

        if (!int.TryParse(pattern.AsSpan(3), out var percent)) return 0;

        // The enumeration names four shares with a half in them by their whole part: pct12 is an
        // eighth, pct37 three, pct62 five and pct87 seven.
        var half = percent is 12 or 37 or 62 or 87 ? 0.5 : 0;

        return Math.Clamp((percent + half) / 100.0, 0, 1);
    }

    /// <summary>An RRGGBB value, or null where there is none to speak of.</summary>
    private static (double Red, double Green, double Blue)? Parse(string? hex)
    {
        if (hex is null || hex.Length != 6 || hex.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null;

        for (var i = 0; i < 6; i++)
        {
            if (!Uri.IsHexDigit(hex[i])) return null;
        }

        return (Convert.ToInt32(hex[..2], 16) / 255.0,
            Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0,
            Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0);
    }
}
