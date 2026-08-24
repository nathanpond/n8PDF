namespace n8PDF.Fonts;

/// <summary>
/// A bound on how large shaping may grow the glyph buffer (#184).
/// </summary>
/// <remarks>
/// A GSUB Multiple substitution replaces one glyph with many, and an AAT insertion subtable adds
/// glyphs beside the ones matched; both run in passes that re-cover their own output, so without
/// a bound one input character expands to billions of glyphs and exhausts memory. What the stack
/// of passes may produce is capped here — a hundred thousand glyphs, far past any run a document
/// sets, and small enough that reaching it is a hostile font rather than a real one. At the cap
/// the expansion stops and the run is shaped no further, which loses nothing a real document had.
/// </remarks>
internal static class ShapingLimits
{
    public const int MaxGlyphs = 100_000;
}
