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
    /// <param name="concur">
    /// Whether the lines are known to meet at one point, as a plot's gridlines running away from
    /// the reader do. See <see cref="Concurring"/> for what it buys and why it does not prejudge
    /// what is being measured.
    /// </param>
    public static IReadOnlyList<Line> Find(
        RenderedPage page, double scale,
        Func<(byte R, byte G, byte B), double> belongs,
        (double Left, double Top, double Right, double Bottom) within,
        double referenceX, double mostSlope = 0.6, int leastPixels = 200, int? expect = null,
        bool concur = false)
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
        // The slope grid is fixed in **resolution**, not in count, so that widening the search
        // does not coarsen it. A fixed count made the answer depend on `mostSlope`: asking for a
        // wider range spread the same 121 slopes further apart, and a line whose slope fell between
        // two of them gathered a lop-sided set of pixels.
        const double leaning = 0.004;

        var slopes = 2 * (int)Math.Ceiling(mostSlope / leaning) + 1;
        var step = 0.25 / scale;                       // a quarter pixel of position
        var lowest = within.Top - mostSlope * (within.Right - within.Left);
        var places = (int)((within.Bottom - lowest + mostSlope * (within.Right - within.Left)) / step) + 2;

        var votes = new double[slopes, places];

        for (var s = 0; s < slopes; s++)
        {
            var m = (s - (slopes - 1) / 2) * leaning;

            foreach (var (x, y, weight) in lit)
            {
                var at = (int)((y - m * (x - referenceX) - lowest) / step);

                if (at >= 0 && at < places) votes[s, at] += weight;
            }
        }

        // Peaks: a place that beats everything near it, in slope as well as in position. The window
        // has to be wide enough that one line does not answer twice at neighbouring slopes.
        var peaks = new List<(int S, int At, double Votes)>();
        var window = (int)Math.Ceiling(0.024 / leaning);

        for (var s = 0; s < slopes; s++)
        for (var a = 0; a < places; a++)
        {
            var v = votes[s, a];

            if (expect is null && v < leastPixels / 3.0) continue;
            if (v < 12) continue;

            var best = true;

            for (var ds = -window; ds <= window && best; ds++)
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
            var m = (s - (slopes - 1) / 2) * leaning;
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
            if (!kept.Any(k => Math.Abs(k.At - line.At) < 2.5))
                kept.Add(line);

        // Where the count is known, the strongest that many are the answer and nothing is thrown
        // away by a threshold — which is what keeps this still when the caller's settings move.
        if (expect is { } many) kept = Strongest(kept, many);

        // The refit moves pixels between lines, so a stray that was merely the weakest of the five
        // can become one that nothing is really on. Weighed again on the far side of it.
        if (concur && kept.Count >= 3)
        {
            kept = Concurring(kept, lit, referenceX);

            if (expect is { } still) kept = Strongest(kept, still);
        }

        return [.. kept.OrderBy(l => l.At)];
    }

    /// <summary>
    /// The strongest so many lines, less any that the rest outnumber.
    /// </summary>
    /// <remarks>
    /// Where the count is known the strongest that many are the answer, and no threshold enters
    /// it — which is what keeps the result the same when a caller's settings move.
    ///
    /// What the count alone does not settle is whether there really were that many to find. A
    /// gridline drawn like its fellows is inked like them, so one placed on a fraction of their
    /// evidence is not a gridline measured badly: it is something else of the colour standing in
    /// for one that was missed. Left in, it puts the answer out by a factor rather than a percent —
    /// a stray at the end of a crowded plot turned a ratio of 0.91 into one of 22, and one two
    /// points from a real line turned 0.49 into 0.05.
    ///
    /// So a line the middle of the others outnumbers two to one is dropped, and fewer lines come
    /// back than were asked for. That is the honest answer: a caller who wanted five and got four
    /// can see that it failed, where one who wanted five and got five cannot.
    /// </remarks>
    private static List<Line> Strongest(List<Line> lines, int many)
    {
        var strongest = lines.OrderByDescending(l => l.Pixels).Take(many).ToList();
        var typical = strongest[strongest.Count / 2].Pixels;

        return [.. strongest.Where(l => 2 * l.Pixels >= typical)];
    }

    /// <summary>
    /// The same lines, refitted so that they meet at one point.
    /// </summary>
    /// <remarks>
    /// Lines that run away from the reader converge, and five of them are therefore not ten free
    /// numbers: they are a point they all pass through and an angle apiece. Fitting each on its own
    /// spends the three parameters that are not there on noise, and it shows — a line whose slope
    /// falls between the vote's slopes gathers a lop-sided set of pixels and its refit follows them,
    /// moving that one line by half a point while its neighbours hold to a tenth.
    ///
    /// What makes this worth doing rather than merely tidy is that the constraint falls entirely on
    /// the part that is **not** being measured. Each line keeps a free parameter and so keeps its
    /// own position at the reference column; what it loses is the freedom to lean independently of
    /// the others, which nothing here wants it to have. The spacings the caller goes on to compare
    /// are as free after this as before.
    ///
    /// Two steps alternate, each exact given the other: the meeting point that lies closest to all
    /// the lines, and then each line refitted through that point from the pixels nearest it. The
    /// reassignment matters as much as the constraint, since it is a slope taken from the vote's
    /// grid that gathered the wrong pixels to begin with.
    ///
    /// Lines that do not converge have their meeting point at infinity, which shows up as a
    /// singular system; that is left alone rather than forced, so a caller who asks for this on
    /// parallel lines gets the unconstrained answer rather than a wrong one.
    /// </remarks>
    private static List<Line> Concurring(
        List<Line> lines, List<(double X, double Y, double Weight)> lit, double referenceX)
    {
        var current = lines.ToList();

        for (var round = 0; round < 6; round++)
        {
            // Where the lines meet: the point whose summed squared distance to all of them is
            // least, each weighted by how much evidence placed it.
            double xx = 0, xy = 0, yy = 0, bx = 0, by = 0;

            foreach (var line in current)
            {
                var length = Math.Sqrt(1 + line.Slope * line.Slope);
                var (nx, ny) = (-line.Slope / length, 1 / length);
                var d = nx * referenceX + ny * line.At;
                var w = line.Pixels;

                xx += w * nx * nx;
                xy += w * nx * ny;
                yy += w * ny * ny;
                bx += w * nx * d;
                by += w * ny * d;
            }

            var determinant = xx * yy - xy * xy;

            // Parallel, or near enough that the meeting point is not a real thing to fit through.
            if (Math.Abs(determinant) < 1e-9 * (xx + yy) * (xx + yy)) return current;

            var (vx, vy) = ((yy * bx - xy * by) / determinant, (xx * by - xy * bx) / determinant);

            // Each pixel to the line it lies nearest, so a slope taken from the vote's grid stops
            // deciding which pixels are whose.
            var mine = current.Select(_ => new List<(double X, double Y)>()).ToList();

            foreach (var (x, y, _) in lit)
            {
                var (best, nearest) = (-1, 1.5);

                for (var i = 0; i < current.Count; i++)
                {
                    var off = Math.Abs(y - (current[i].At + current[i].Slope * (x - referenceX)))
                              / Math.Sqrt(1 + current[i].Slope * current[i].Slope);

                    if (off >= nearest) continue;

                    (best, nearest) = (i, off);
                }

                if (best >= 0) mine[best].Add((x, y));
            }

            var next = new List<Line>(current.Count);

            for (var i = 0; i < current.Count; i++)
            {
                if (mine[i].Count < 12)
                {
                    next.Add(current[i]);
                    continue;
                }

                // The direction through the meeting point that those pixels lie along: the leading
                // eigenvector of their scatter about it, which is the least-squares fit of a line
                // pinned at one end.
                double a = 0, b = 0, c = 0;

                foreach (var (x, y) in mine[i])
                {
                    var (dx, dy) = (x - vx, y - vy);

                    a += dx * dx;
                    b += dx * dy;
                    c += dy * dy;
                }

                var largest = (a + c + Math.Sqrt((a - c) * (a - c) + 4 * b * b)) / 2;

                var (ux, uy) = Math.Abs(largest - a) > Math.Abs(largest - c)
                    ? (b, largest - a)
                    : (largest - c, b);

                if (Math.Abs(ux) < 1e-9)
                {
                    next.Add(current[i]);
                    continue;
                }

                var slope = uy / ux;

                next.Add(new Line(vy + (referenceX - vx) * slope, slope, mine[i].Count));
            }

            current = next;
        }

        return current;
    }
}
