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

    /// <summary>
    /// The width of something drawn, on the grid: rounded down rather than to the nearest.
    /// </summary>
    /// <remarks>
    /// A three point line is the case that says so, being exactly half a step from either
    /// answer: Word draws the border round a page at 2.88 where three points is 12½ steps, so the
    /// half goes down. Every other weight a border takes is a quarter point or coarser, where
    /// rounding down and rounding to the nearest agree.
    /// </remarks>
    public static double Width(double points) => Math.Floor(points / Step + 1e-9) * Step;

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

    /// <summary>
    /// Where the baseline of a line of an exact height goes: its own place, rounded down to the
    /// grid from five twelfths of a step above it.
    /// </summary>
    /// <remarks>
    /// A line whose height is fixed keeps no room below the baseline to be rounded — the share
    /// above it is settled by <see cref="LayoutEngine"/>'s own rule and is already a whole number
    /// of steps — so what is left is where the *place* lands, and Word does not round it to the
    /// nearest. Measured from the exact-spaced paragraphs of the sweeps behind that rule, 121
    /// heights of up to thirty-two lines each: rounding to the nearest agrees with Word on 84% of
    /// the lines under the first, and rounding down from five twelfths of a step above agrees on
    /// 89%. The five twelfths was then checked at sixty-one heights the fitting never saw, where
    /// it agrees on 92% against the nearest's 84%.
    ///
    /// It is a fitted constant and nothing here derives it. What is left over is the last step of
    /// a rounding that no rule of the height reproduces — see ExactLineTests — and it is never
    /// more than one step of the grid.
    /// </remarks>
    public static double ExactBaseline(double top, double ascent) =>
        Math.Floor((top + ascent) / Step + 5.0 / 12) * Step;
}
