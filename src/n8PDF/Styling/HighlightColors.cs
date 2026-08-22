namespace n8PDF.Styling;

/// <summary>
/// The sixteen colours a <c>w:highlight</c> can name.
/// </summary>
/// <remarks>
/// A highlight names a colour rather than stating one, and the sixteen names are the sixteen
/// colours of an old display adapter: each channel off, half on at 128, or full. highlight-probe
/// puts all sixteen on a page and reads them back out of Word's own export, which is where these
/// come from rather than from any table.
/// </remarks>
internal static class HighlightColors
{
    /// <summary>The colour a name stands for, or null for none and for a name nobody knows.</summary>
    public static (double Red, double Green, double Blue)? Resolve(string? name) => name switch
    {
        "black" => (0, 0, 0),
        "blue" => (0, 0, 1),
        "cyan" => (0, 1, 1),
        "green" => (0, 1, 0),
        "magenta" => (1, 0, 1),
        "red" => (1, 0, 0),
        "yellow" => (1, 1, 0),
        "white" => (1, 1, 1),
        "darkBlue" => (0, 0, Half),
        "darkCyan" => (0, Half, Half),
        "darkGreen" => (0, Half, 0),
        "darkMagenta" => (Half, 0, Half),
        "darkRed" => (Half, 0, 0),
        "darkYellow" => (Half, Half, 0),
        "darkGray" => (Half, Half, Half),
        "lightGray" => (Light, Light, Light),
        _ => null
    };

    /// <summary>128 of 255, which is what Word writes for every dark name.</summary>
    private const double Half = 128 / 255.0;

    /// <summary>And 192, for the light grey.</summary>
    private const double Light = 192 / 255.0;
}
