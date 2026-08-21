namespace n8PDF.Layout;

/// <summary>
/// The grid Word writes on: one three-hundredth of an inch, in both directions that matter here.
/// </summary>
/// <remarks>
/// Word does not put text where the arithmetic says. It works a line's height out exactly — from
/// the face's own ascender, descender and line gap at the size the run states — and then writes
/// the baseline on a grid of 0.24 points, which is a three-hundredth of an inch. The same grid
/// rounds the size it draws at: two point text is drawn at 1.92, five at 5.04, eleven at 11.04.
///
/// What is rounded is where a line lands, not how tall it is. line-grid-probe settles that: nine
/// pages of forty single-spaced lines. Were the height rounded, every gap on a page would be the
/// same whole number of steps; instead they mix 2.16 with 2.4 and 22.8 with 23.04, and the
/// distance from a page's first baseline to its fortieth comes within a quarter of a point of
/// thirty-nine exact heights — 89.52 against 89.69 at two point, 493.2 against 493.31 at eleven.
///
/// The same probe says the height is worked out at the size the run <em>states</em>, not the size
/// Word draws at, and the two straddle the truth: over thirty-nine gaps a two point page would
/// span 86.1 points if the drawn size decided it and 89.69 if the stated size did, and Word spans
/// 89.52. Eleven point, where the rounding goes the other way, agrees: 493.2 measured against
/// 493.31 stated and 495.1 drawn.
///
/// This is why a page of Word's drifts from a page of exact arithmetic and then catches up again:
/// what is rounded is the running position, so nothing accumulates beyond half a step.
/// </remarks>
internal static class Grid
{
    /// <summary>The step: a three hundredth of an inch, in points.</summary>
    public const double Step = 0.24;

    /// <summary>The nearest place on the grid.</summary>
    public static double Snap(double points) =>
        Math.Round(points / Step, MidpointRounding.AwayFromZero) * Step;

    /// <summary>Where the baseline of a line goes, given the top of its line box.</summary>
    /// <remarks>
    /// Inside a line box it is the descent that is rounded to the grid — the room below the
    /// baseline — and the ascent takes what the exact height leaves above it. Rounding the ascent
    /// instead does not fit, and neither does rounding the height. line-ascent-probe reads the
    /// ascent straight off the page, from pages whose first paragraph is a single letter, so that
    /// the baseline is the top margin plus that ascent and nothing else: over seventy-four of them
    /// — four faces, and Times New Roman and Arial at every half point from six to twenty —
    /// rounding the descent accounts for seventy-one, and rounding the ascent for sixty-two. No
    /// rounding of a height or a descent through any intermediate unit does better. The three it
    /// misses are one step each, and each is a descent that lands just above a half step.
    ///
    /// Then the baseline itself is rounded where it lands. The line below starts from the exact
    /// height regardless, so neither rounding accumulates: six of line-grid-probe's nine pages
    /// come out as Word's line for line, forty lines at a time.
    /// </remarks>
    public static double Baseline(double top, double ascent, double height) =>
        Snap(top + height - Snap(height - ascent));
}
