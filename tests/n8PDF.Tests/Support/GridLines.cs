namespace n8PDF.Tests.Support;

/// <summary>
/// Finds the gridlines of a chart in a rendered page.
/// </summary>
/// <remarks>
/// <see cref="BoxSilhouette"/> places an edge once its pixels are in hand; this decides which lines
/// there are and which pixels are whose. They are different problems and the second is the harder
/// one here, because a chart's gridlines **touch things** — every one of them runs into the plot's
/// own outline at both ends, and in a three-dimensional chart they converge as well.
///
/// Two obvious groupings were tried against Word's output and both failed on that fact. Walking the
/// pixels and joining what is close **fragments** each line into dozens of pieces, and fitting a
/// fragment gives a near-vertical line whose position extrapolates thousands of points off the page.
/// Connected components, which is normally the right tool, goes the other way and **merges** them:
/// every line joins its neighbours through the outline they all touch, leaving one blob.
///
/// So this does not group pixels at all. **Every pixel votes for the lines it could lie on**, and a
/// line is where the votes pile up. A pixel shared between two lines, or between a line and the
/// outline, adds a vote to each rather than confusing either — which is exactly the case that
/// defeated both attempts. What makes it cheap rather than a general search is that the direction is
/// roughly known: these lines are near enough horizontal, so the vote is over a modest range of
/// slope and a position at one reference column.
/// </remarks>
internal static class GridLines
{
    /// <summary>A line found, given as where it crosses the reference column and how it leans.</summary>
    /// <param name="At">Its y where x is the reference column.</param>
    /// <param name="Slope">How much y changes for each point of x.</param>
    /// <param name="Pixels">How many pixels were fitted to place it.</param>
    internal readonly record struct Line(double At, double Slope, int Pixels);

    /// <summary>
    /// The lines of a given colour within a region.
    /// </summary>
    /// <param name="page">The rendered page.</param>
    /// <param name="scale">What it was rendered at, in pixels to the point.</param>
    /// <param name="belongs">
    /// Whether a colour is part of a line. A line a point wide is never drawn saturated — Word's
    /// raster turns a stated <c>FF0000</c> into <c>FFBFBF</c> — so this has to test the hue rather
    /// than match a colour.
    /// </param>
    /// <param name="within">The region to look in, in points.</param>
    /// <param name="referenceX">The column each line's position is reported at.</param>
    /// <param name="mostSlope">
    /// How far from horizontal a line may lean. Keeping this tight is what makes the search small,
    /// and a chart's gridlines are never far off.
    /// </param>
    /// <param name="leastPixels">
    /// How many pixels a line needs before it is believed. Set from how long a line is expected to
    /// be, since the point of this is to tell a line from a stray.
    /// </param>
    public static IReadOnlyList<Line> Find(
        RenderedPage page, double scale,
        Func<(byte R, byte G, byte B), bool> belongs,
        (double Left, double Top, double Right, double Bottom) within,
        double referenceX, double mostSlope = 0.6, int leastPixels = 200)
    {
        var lit = new List<(double X, double Y)>();

        for (var y = within.Top; y < within.Bottom; y += 1 / scale)
        for (var x = within.Left; x < within.Right; x += 1 / scale)
            if (belongs(page.At(x, y, scale)))
                lit.Add((x, y));

        if (lit.Count < leastPixels) return [];

        // The vote. A pixel at (x,y) lies on the line of slope m that crosses the reference column
        // at y - m(x - referenceX), so each pixel adds one vote to that place for every slope tried.
        const int slopes = 121;
        var step = 0.25 / scale;                       // a quarter pixel of position
        var lowest = within.Top - mostSlope * (within.Right - within.Left);
        var places = (int)((within.Bottom - lowest + mostSlope * (within.Right - within.Left)) / step) + 2;

        var votes = new int[slopes, places];

        for (var s = 0; s < slopes; s++)
        {
            var m = -mostSlope + 2 * mostSlope * s / (slopes - 1.0);

            foreach (var (x, y) in lit)
            {
                var at = (int)((y - m * (x - referenceX) - lowest) / step);

                if (at >= 0 && at < places) votes[s, at]++;
            }
        }

        // Peaks: a place that beats everything near it, in slope as well as in position. The window
        // has to be wide enough that one line does not answer twice at neighbouring slopes.
        var peaks = new List<(int S, int At, int Votes)>();

        for (var s = 0; s < slopes; s++)
        for (var a = 0; a < places; a++)
        {
            var v = votes[s, a];

            if (v < leastPixels / 3) continue;

            var best = true;

            for (var ds = -6; ds <= 6 && best; ds++)
            for (var da = -12; da <= 12; da++)
            {
                var (ns, na) = (s + ds, a + da);

                if (ns < 0 || na < 0 || ns >= slopes || na >= places) continue;
                if (votes[ns, na] <= v) continue;

                best = false;
                break;
            }

            if (best) peaks.Add((s, a, v));
        }

        // Strongest first, and a peak too near one already taken is the same line answering twice.
        var found = new List<Line>();

        foreach (var (s, a, _) in peaks.OrderByDescending(p => p.Votes))
        {
            var m = -mostSlope + 2 * mostSlope * s / (slopes - 1.0);
            var at = lowest + a * step;

            // The pixels near this line, fitted the way an edge is.
            var mine = lit.Where(p => Math.Abs(p.Y - (at + m * (p.X - referenceX))) < 1.5).ToList();

            if (mine.Count < leastPixels) continue;

            var (on, along) = BoxSilhouette.FitLine(mine, (1.0, m));

            if (Math.Abs(along.X) < 1e-9) continue;

            var slope = along.Y / along.X;

            // How well those pixels actually lie on it. A peak can form where two converging lines
            // happen to line up at some slope between them, and the fit through such a set is a
            // line nothing is really on — it scatters where a real one does not. A gridline is
            // about a point wide, so half a point of scatter is generous and a false one fails it.
            var scatter = Math.Sqrt(mine.Sum(p =>
            {
                var off = (p.X - on.X) * along.Y - (p.Y - on.Y) * along.X;
                return off * off;
            }) / mine.Count);

            if (scatter > 0.5) continue;

            found.Add(new Line(on.Y + (referenceX - on.X) * slope, slope, mine.Count));
        }

        // One line can win at more than one slope, and the refit moves it, so near-duplicates are
        // collapsed **after** placing rather than before — the strongest of each cluster survives.
        var kept = new List<Line>();

        foreach (var line in found.OrderByDescending(l => l.Pixels))
            if (!kept.Any(k => Math.Abs(k.At - line.At) < 4))
                kept.Add(line);

        return [.. kept.OrderBy(l => l.At)];
    }
}
