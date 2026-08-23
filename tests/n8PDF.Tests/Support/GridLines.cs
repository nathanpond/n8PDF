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
    /// How much of a colour is part of a line, from nought to one. A line a point wide is never
    /// drawn saturated — Word's raster turns a stated <c>FF0000</c> into <c>FFBFBF</c> — and where
    /// the edge of one lies is a matter of degree rather than of yes and no. Returning the degree
    /// rather than a verdict is what keeps the answer still when a threshold moves: a pixel that is
    /// half the hue votes half as loudly instead of switching sides.
    /// </param>
    /// <param name="within">The region to look in, in points.</param>
    /// <param name="referenceX">The column each line's position is reported at.</param>
    /// <param name="mostSlope">
    /// How far from horizontal a line may lean. Keeping this tight is what makes the search small,
    /// and a chart's gridlines are never far off.
    /// </param>
    /// <param name="leastPixels">
    /// How many pixels a line needs before it is believed, where <paramref name="expect"/> is not
    /// given. Set from how long a line is expected to be.
    /// </param>
    /// <param name="expect">
    /// How many lines there are, where that is known — and for a chart's gridlines it is, from the
    /// axis that draws them. Given it, the strongest that many are taken and no threshold enters
    /// the answer at all, which is what makes the result the same whatever the caller asks for.
    /// Leave it null where the count is not known.
    /// </param>
    public static IReadOnlyList<Line> Find(
        RenderedPage page, double scale,
        Func<(byte R, byte G, byte B), double> belongs,
        (double Left, double Top, double Right, double Bottom) within,
        double referenceX, double mostSlope = 0.6, int leastPixels = 200, int? expect = null)
    {
        var lit = new List<(double X, double Y, double Weight)>();

        for (var y = within.Top; y < within.Bottom; y += 1 / scale)
        for (var x = within.Left; x < within.Right; x += 1 / scale)
        {
            var much = belongs(page.At(x, y, scale));

            if (much > 0.02) lit.Add((x, y, much));
        }

        var lot = lit.Sum(p => p.Weight);

        if (lot < leastPixels && expect is null) return [];

        // The vote. A pixel at (x,y) lies on the line of slope m that crosses the reference column
        // at y - m(x - referenceX), so each pixel adds one vote to that place for every slope tried.
        const int slopes = 121;
        var step = 0.25 / scale;                       // a quarter pixel of position
        var lowest = within.Top - mostSlope * (within.Right - within.Left);
        var places = (int)((within.Bottom - lowest + mostSlope * (within.Right - within.Left)) / step) + 2;

        var votes = new double[slopes, places];

        for (var s = 0; s < slopes; s++)
        {
            var m = -mostSlope + 2 * mostSlope * s / (slopes - 1.0);

            foreach (var (x, y, weight) in lit)
            {
                var at = (int)((y - m * (x - referenceX) - lowest) / step);

                if (at >= 0 && at < places) votes[s, at] += weight;
            }
        }

        // Peaks: a place that beats everything near it, in slope as well as in position. The window
        // has to be wide enough that one line does not answer twice at neighbouring slopes.
        var peaks = new List<(int S, int At, double Votes)>();

        for (var s = 0; s < slopes; s++)
        for (var a = 0; a < places; a++)
        {
            var v = votes[s, a];

            if (expect is null && v < leastPixels / 3.0) continue;
            if (v < 12) continue;

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
            var near = lit.Where(p => Math.Abs(p.Y - (at + m * (p.X - referenceX))) < 1.5).ToList();

            if (expect is null && near.Sum(p => p.Weight) < leastPixels) continue;
            if (near.Count < 12) continue;

            var mine = near.Select(p => (p.X, p.Y)).ToList();
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

        // Where the count is known, the strongest that many are the answer and nothing is thrown
        // away by a threshold — which is what keeps this still when the caller's settings move.
        if (expect is { } many)
            kept = [.. kept.OrderByDescending(l => l.Pixels).Take(many)];

        return [.. kept.OrderBy(l => l.At)];
    }
}
