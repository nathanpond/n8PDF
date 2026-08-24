namespace n8PDF.Tests.Support;

/// <summary>
/// Finds the corners of a projected three-dimensional box in a rendered page.
/// </summary>
/// <remarks>
/// Word rasterises a three-dimensional plot — the whole thing arrives as one 300 dpi bitmap — so
/// there is no path geometry to read the way every other chart feature here is measured. What can
/// be read instead is the shape of a box painted a colour nothing else uses, and that is what this
/// is for.
///
/// **A pixel is 0.24pt at Word's resolution, and the projection has to be pinned finer than that.**
/// The way past it is not to look harder at any one pixel but to stop looking at pixels
/// individually: an edge of the silhouette is tens of pixels long, and a line fitted through all of
/// them is far better placed than the best single pixel on it. Two fitted lines then intersect at a
/// corner whose accuracy is a fraction of a pixel. <see cref="Chart3DSilhouetteTests"/> measures
/// what that fraction actually is against shapes whose corners are known exactly.
///
/// The other half of it is convexity. A box is convex and projecting one keeps it convex, so the
/// silhouette is a convex hexagon — which means the boundary can be replaced by its convex hull
/// before anything is fitted, and every stray pixel a rasteriser leaves in a concavity goes with it.
/// </remarks>
internal static class BoxSilhouette
{
    /// <summary>
    /// How sharply two edges must turn, in degrees, for the corner between them to be worth
    /// reporting.
    /// </summary>
    /// <remarks>
    /// Two nearly-collinear lines cross at a place that a very small error in either one's angle
    /// moves a very long way, so a box turned almost square-on to the viewer has corners that cannot
    /// be found however carefully the edges are fitted. That is a property of the geometry and not
    /// of the fitting, so the answer is to refuse rather than to try harder.
    ///
    /// Measured against shapes whose corners are known exactly: a box whose edges turn by 2.58° is
    /// 0.224pt out, and one turning by 7.79° is 0.093pt — inside the tenth of a point this is
    /// required to hold to, as is every sharper case measured, up to 0.035pt at 31°. So the limit
    /// lies in (2.58, 7.79) and the middle of that is used, the way the cell margin and the legend's
    /// wrapping inset are.
    /// </remarks>
    public const double SharpestUsefulCorner = 5.0;

    /// <summary>How many corners a projected box shows.</summary>
    /// <remarks>
    /// Six, always, unless the box is turned to face the viewer square on — at which point two of
    /// them coincide with two others and the silhouette is a rectangle. That case is refused rather
    /// than guessed at, since a rectangle says nothing about depth.
    /// </remarks>
    public const int Corners = 6;

    /// <summary>What was found, or why nothing was.</summary>
    /// <param name="Points">
    /// The corners, going round the shape, or empty where <paramref name="Refused"/> says why not.
    /// The first is the leftmost, and they run clockwise as the page is looked at.
    /// </param>
    /// <param name="Refused">Null where the shape was found.</param>
    internal sealed record Shape(IReadOnlyList<(double X, double Y)> Points, string? Refused)
    {
        public bool Found => Refused is null;
    }

    /// <summary>
    /// The corners of the shape painted in a given colour, within a given region of the page.
    /// </summary>
    /// <param name="page">The rendered page.</param>
    /// <param name="scale">What it was rendered at, in pixels to the point.</param>
    /// <param name="belongs">
    /// Whether a colour is part of the shape. A box shows three faces of the same hue lightened and
    /// darkened, so this has to admit all three rather than one exact colour.
    /// </param>
    /// <param name="within">
    /// The region to look in, in points. A shape touching its edge is refused as clipped: the plot
    /// area cuts a bar that reaches the top of its scale, and what comes back is the corner of the
    /// plot rather than the corner of the box. That is not a hypothetical — it is what happened when
    /// this was first attempted by hand.
    /// </param>
    public static Shape Find(
        RenderedPage page, double scale,
        Func<(byte R, byte G, byte B), bool> belongs,
        (double Left, double Top, double Right, double Bottom) within,
        int corners = Corners)
    {
        var left = (int)(within.Left * scale);
        var top = (int)(within.Top * scale);
        var right = Math.Min((int)(within.Right * scale), page.Pixels.Width - 1);
        var bottom = Math.Min((int)(within.Bottom * scale), page.Pixels.Height - 1);

        if (right <= left || bottom <= top) return new Shape([], "the region is empty");

        var mask = new bool[right - left + 1, bottom - top + 1];
        var found = 0;

        for (var py = top; py <= bottom; py++)
        for (var px = left; px <= right; px++)
        {
            var at = (py * page.Pixels.Width + px) * 3;

            if (!belongs((page.Pixels.Data[at], page.Pixels.Data[at + 1], page.Pixels.Data[at + 2])))
                continue;

            mask[px - left, py - top] = true;
            found++;
        }

        // Enough of a shape to be one. Six edges want fitting and a handful of pixels cannot do it.
        if (found < 200) return new Shape([], $"only {found} pixels of that colour");

        var wide = mask.GetLength(0);
        var tall = mask.GetLength(1);

        // Touching the edge of the region means the shape runs outside it and what is being measured
        // is the region's corner rather than the shape's.
        for (var x = 0; x < wide; x++)
            if (mask[x, 0] || mask[x, tall - 1])
                return new Shape([], "the shape reaches the top or bottom of the region and is cut by it");

        for (var y = 0; y < tall; y++)
            if (mask[0, y] || mask[wide - 1, y])
                return new Shape([], "the shape reaches the side of the region and is cut by it");

        // The boundary: in the shape, with something outside it next door. Taken at pixel centres,
        // which is where the colour was actually sampled.
        var edge = new List<(double X, double Y)>();

        for (var y = 0; y < tall; y++)
        for (var x = 0; x < wide; x++)
        {
            if (!mask[x, y]) continue;

            if (mask[x - 1, y] && mask[x + 1, y] && mask[x, y - 1] && mask[x, y + 1]) continue;

            edge.Add(((left + x + 0.5) / scale, (top + y + 0.5) / scale));
        }

        var hull = Hull(edge);

        if (hull.Count < corners)
            return new Shape([], $"the outline has only {hull.Count} corners, fewer than the {corners} asked for");

        // Down to six, dropping whichever corner costs least to lose, then each of the six edges
        // refitted from the pixels rather than kept from the hull.
        var reduced = Reduce(hull, corners);
        var lines = new List<((double X, double Y) On, (double X, double Y) Along)>(corners);

        for (var i = 0; i < corners; i++)
        {
            var a = reduced[i];
            var b = reduced[(i + 1) % corners];

            // The pixels this edge owns: nearer to it than to any other, and not round a corner.
            var mine = edge.Where(p => Nearest(p, reduced) == i).ToList();

            if (mine.Count < 8)
                return new Shape([], $"an edge of the outline has only {mine.Count} pixels, too few to fit a line through");

            lines.Add(Fit(mine, (b.X - a.X, b.Y - a.Y)));
        }

        var met = new List<(double X, double Y)>(corners);

        for (var i = 0; i < corners; i++)
        {
            var previous = lines[(i + corners - 1) % corners];

            if (Meet(previous, lines[i]) is not { } corner)
                return new Shape([], "two edges of the outline are parallel and do not meet at a corner");

            // How sharply the outline turns here. Too little and the crossing is not worth having,
            // whatever the fit — see SharpestUsefulCorner.
            var turn = Math.Abs(Math.Atan2(
                previous.Along.X * lines[i].Along.Y - previous.Along.Y * lines[i].Along.X,
                previous.Along.X * lines[i].Along.X + previous.Along.Y * lines[i].Along.Y)) * 180 / Math.PI;

            if (turn < SharpestUsefulCorner)
                return new Shape([],
                    $"the outline turns by only {turn:0.0}° at one corner, so the box is too nearly " +
                    "square-on for its corners to be placed");

            met.Add(corner);
        }

        // Round to start at the leftmost corner, so a caller can name them without knowing which
        // pixel the walk happened to begin at.
        var first = 0;
        for (var i = 1; i < met.Count; i++)
            if (met[i].X < met[first].X) first = i;

        return new Shape([.. Enumerable.Range(0, corners).Select(i => met[(first + i) % corners])], null);
    }

    /// <summary>
    /// The corners taken straight off the pixels, which is what fitting is measured against.
    /// </summary>
    /// <remarks>
    /// Exposed for <see cref="Chart3DSilhouetteTests"/> and used nowhere else. This is the obvious
    /// way to do it and the way it was first done by hand — hull the mask, cut it down to six — and
    /// its answers can only ever sit on a pixel, which is the ceiling fitting exists to get past.
    /// </remarks>
    public static IReadOnlyList<(double X, double Y)> CornersFromPixels(List<(double X, double Y)> pixels)
    {
        var hull = Hull(pixels);

        return hull.Count < Corners ? hull : Reduce(hull, Corners);
    }

    /// <summary>Which edge of a polygon a point lies nearest.</summary>
    private static int Nearest((double X, double Y) point, IReadOnlyList<(double X, double Y)> polygon)
    {
        var (best, at) = (double.MaxValue, 0);

        for (var i = 0; i < polygon.Count; i++)
        {
            var distance = ToSegment(point, polygon[i], polygon[(i + 1) % polygon.Count]);

            if (distance >= best) continue;

            best = distance;
            at = i;
        }

        return at;
    }

    /// <summary>How far a point is from a segment.</summary>
    private static double ToSegment((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = dx * dx + dy * dy;

        if (length <= 0) return Math.Sqrt((p.X - a.X) * (p.X - a.X) + (p.Y - a.Y) * (p.Y - a.Y));

        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        var (qx, qy) = (a.X + t * dx, a.Y + t * dy);

        return Math.Sqrt((p.X - qx) * (p.X - qx) + (p.Y - qy) * (p.Y - qy));
    }

    /// <summary>
    /// A line through a set of points, fitted by its principal axis.
    /// </summary>
    /// <remarks>
    /// Exposed for <see cref="GridLines"/>, which wants a gridline placed the same way a
    /// silhouette's edge is and for the same reason: a line a point wide on a 300 dpi raster is
    /// several pixels of nothing very definite, and where it lies is far better recovered from all
    /// of them at once than from any one of them.
    /// </remarks>
    public static ((double X, double Y) On, (double X, double Y) Along) FitLine(
        IReadOnlyList<(double X, double Y)> points, (double X, double Y) roughly) => Fit(points, roughly);

    /// <summary>
    /// A line through a set of points, fitted by its principal axis rather than as y against x.
    /// </summary>
    /// <remarks>
    /// Total least squares, and the reason for it is that a projected box always has near-vertical
    /// edges. Fitting y = mx + c to one of those divides by very nearly nothing and the answer is
    /// worthless; the principal axis has no orientation it cannot handle, since it minimises the
    /// perpendicular distance rather than the vertical one.
    /// </remarks>
    private static ((double X, double Y) On, (double X, double Y) Along) Fit(
        IReadOnlyList<(double X, double Y)> points, (double X, double Y) roughly)
    {
        var (mx, my) = (0.0, 0.0);
        foreach (var (x, y) in points) { mx += x; my += y; }
        mx /= points.Count; my /= points.Count;

        var (xx, xy, yy) = (0.0, 0.0, 0.0);

        foreach (var (x, y) in points)
        {
            var (dx, dy) = (x - mx, y - my);
            xx += dx * dx; xy += dx * dy; yy += dy * dy;
        }

        // The larger eigenvector of the covariance, which is the direction the points run in.
        var middle = (xx + yy) / 2;
        var spread = Math.Sqrt(Math.Max(0, (xx - yy) * (xx - yy) / 4 + xy * xy));
        var largest = middle + spread;

        var (ax, ay) = Math.Abs(xy) > 1e-12
            ? (largest - yy, xy)
            : xx >= yy ? (1.0, 0.0) : (0.0, 1.0);

        var length = Math.Sqrt(ax * ax + ay * ay);
        if (length <= 0) return ((mx, my), roughly);

        (ax, ay) = (ax / length, ay / length);

        // Pointed the way the hull ran, so that consecutive edges stay in order.
        if (ax * roughly.X + ay * roughly.Y < 0) (ax, ay) = (-ax, -ay);

        return ((mx, my), (ax, ay));
    }

    /// <summary>Where two lines cross, or null where they do not.</summary>
    private static (double X, double Y)? Meet(
        ((double X, double Y) On, (double X, double Y) Along) a,
        ((double X, double Y) On, (double X, double Y) Along) b)
    {
        var determinant = a.Along.X * -b.Along.Y - -b.Along.X * a.Along.Y;

        // Parallel to within a hundredth of a degree, which no two edges of a box in view ever are.
        if (Math.Abs(determinant) < 1e-7) return null;

        var (dx, dy) = (b.On.X - a.On.X, b.On.Y - a.On.Y);
        var t = (dx * -b.Along.Y - -b.Along.X * dy) / determinant;

        return (a.On.X + t * a.Along.X, a.On.Y + t * a.Along.Y);
    }

    /// <summary>The convex hull of a set of points, clockwise as the page is looked at.</summary>
    /// <remarks>Andrew's monotone chain: sort, then sweep the lower and upper sides.</remarks>
    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        if (sorted.Count < 3) return sorted;

        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var built = new List<(double X, double Y)>();

        foreach (var p in sorted)
        {
            while (built.Count >= 2 && Cross(built[^2], built[^1], p) <= 0) built.RemoveAt(built.Count - 1);
            built.Add(p);
        }

        var lower = built.Count + 1;
        built.RemoveAt(built.Count - 1);

        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (built.Count >= lower && Cross(built[^2], built[^1], p) <= 0) built.RemoveAt(built.Count - 1);
            built.Add(p);
        }

        built.RemoveAt(built.Count - 1);

        return built;
    }

    /// <summary>
    /// A polygon cut down to a given number of corners, losing the cheapest at each step.
    /// </summary>
    /// <remarks>
    /// What a corner costs to lose is how far it stands from the line its two neighbours would make
    /// without it — so a corner sitting almost on that line is nearly free, which is exactly what a
    /// hull vertex introduced by pixel quantisation is. The six that survive are the six real ones,
    /// and they are then thrown away in favour of lines refitted from the pixels: this decides
    /// *which* edges there are, not where they lie.
    /// </remarks>
    private static List<(double X, double Y)> Reduce(List<(double X, double Y)> polygon, int corners)
    {
        var left = new List<(double X, double Y)>(polygon);

        while (left.Count > corners)
        {
            var (cheapest, at) = (double.MaxValue, 0);

            for (var i = 0; i < left.Count; i++)
            {
                var cost = ToSegment(left[i], left[(i + left.Count - 1) % left.Count], left[(i + 1) % left.Count]);

                if (cost >= cheapest) continue;

                cheapest = cost;
                at = i;
            }

            left.RemoveAt(at);
        }

        return left;
    }
}
