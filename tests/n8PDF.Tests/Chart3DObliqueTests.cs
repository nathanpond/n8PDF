using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The oblique projection for <c>rAngAx="1"</c>, checked corner by corner against Word.
/// </summary>
/// <remarks>
/// <see cref="Chart3DObliqueProjection"/> holds the laws; this holds them to Word's own output,
/// the way <see cref="Chart3DCameraTests"/> does for the camera. Every page is a red bar in a
/// plot rectangle stated at x 0.2, y 0.1, w 0.6, h 0.55 of a 360x216 chart at (72,72), so the
/// bar's silhouette is the projected box and #106's finder reads its six corners. The model's
/// corners are compared **in page coordinates** — no fitting, no scale taken from the page,
/// nothing free.
///
/// Three probes feed it. <c>chart-3d-projection-probe</c> is this story's own: rotX swept 15..55,
/// rotY swept 15..45, depth swept 60..250, both counts, the bar's value swept 30..100, hPercent
/// stated at 50 and 150, and three held-back pages moving everything at once. The committed
/// <c>chart-3d-rotation-probe</c> (rotY 5..65, rotX 5..60) and <c>chart-3d-depth-probe</c>
/// (depth 20..500, counts to three) extend the sweeps.
///
/// **The bars.** The strict tier holds 0.35pt, the wider tier 0.85pt, and neither is slack: the
/// last tenth beyond the story's quarter-point ambition is not the law's. Word rasterises the
/// scene to its own 300 dpi grid, which moves a corner as much as 0.17pt before any law is asked
/// about it; three pages of one scene at three bar values reproduce their fitted placement to a
/// hundredth of a point, so the residual is Word's own, not noise in the reading. Refitting the
/// margin constants under three different shape families moves the worst page between 0.28 and
/// 0.31pt — the floor does not move with the model. The wider tier's extra fraction is the
/// camera's own: corners on the committed probes were read by an older vintage of the
/// instrument, and the 5°-tilt page adds a nearly-degenerate silhouette whose top corners turn
/// by very little.
/// </remarks>
public class Chart3DObliqueTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    private IReadOnlyList<(double X, double Y)>? WordCorners(string fixture, int page)
    {
        if (TestFonts.SkipForMissingFonts(fixture)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        const double scale = 6;
        if (PdfRasterizer.Render(File.ReadAllBytes(path), page, scale) is not { } rendered) return null;

        var shape = BoxSilhouette.Find(rendered, scale, Reddish, (73, 73, 431, 287));
        Assert.True(shape.Found, $"{fixture} page {page}: {shape.Refused}");
        return shape.Points;
    }

    private static Chart3DObliqueProjection Projection(
        double rotX, double rotY, double depthPercent, double? hPercent, int categories, int series) =>
        new(rotX, rotY, depthPercent, hPercent, categories, series,
            rectLeft: 144, rectTop: 93.6, rectWidth: 216, rectHeight: 118.8);

    /// <summary>The hull of the bar the probes draw: the box up to the bar's top.</summary>
    private static List<(double X, double Y)> ModelCorners(Chart3DObliqueProjection projection, double value)
    {
        var top = value - Chart3DObliqueProjection.BarTopShortfall;
        var points = new List<(double X, double Y)>();
        foreach (var x in new[] { 0.0, 1.0 })
        foreach (var y in new[] { 0.0, top })
        foreach (var z in new[] { 0.0, 1.0 })
            points.Add(projection.Project(x, y, z));
        return Hull(points);
    }

    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-12) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }
        var upper = new List<(double X, double Y)>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-12) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    /// <summary>Worst distance from Word's corners to the model's, best cyclic pairing.</summary>
    private static double Astray(List<(double X, double Y)> model, IReadOnlyList<(double X, double Y)> word)
    {
        if (model.Count != word.Count) return double.PositiveInfinity;
        var n = word.Count;
        var best = double.PositiveInfinity;
        for (var shift = 0; shift < n; shift++)
        foreach (var reverse in new[] { false, true })
        {
            double worst = 0;
            for (var i = 0; i < n; i++)
            {
                var idx = reverse ? (shift - i % n + 2 * n) % n : (shift + i) % n;
                var dx = model[idx].X - word[i].X;
                var dy = model[idx].Y - word[i].Y;
                worst = Math.Max(worst, Math.Sqrt(dx * dx + dy * dy));
            }
            best = Math.Min(best, worst);
        }
        return best;
    }

    private void AssertWithin(
        string fixture, int page, int rotX, int rotY, int depthPercent, int hPercent,
        int categories, int series, int value, bool heldBack, double bar)
    {
        if (WordCorners(fixture, page) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var projection = Projection(rotX, rotY, depthPercent,
            hPercent == 0 ? null : hPercent, categories, series);
        var worst = Astray(ModelCorners(projection, value / 100.0), word);

        _output.WriteLine($"{rotX}° by {rotY}°, depth {depthPercent}, value {value}" +
                          $"{(hPercent > 0 ? $", hPercent {hPercent}" : "")}" +
                          $"{(categories > 1 ? $", {categories} categories" : "")}" +
                          $"{(series > 1 ? $", {series} series" : "")}" +
                          $"{(heldBack ? " (held back)" : "")}: worst corner {worst:0.000}pt");

        Assert.True(worst < bar, $"the worst corner is {worst:0.000}pt from Word's, above the {bar}pt bar");
    }

    /// <summary>
    /// The projection puts every corner of the bar within a third of a point of Word's, in page
    /// coordinates, across tilts, turns, depths, counts and bar values.
    /// </summary>
    /// <remarks>
    /// The held-back rows were never consulted while the laws were being measured; they move both
    /// angles and the depth at once, which no fitted page does.
    /// </remarks>
    [Theory]
    [InlineData(0, 15, 20, 100, 1, 1, 60, false)]
    [InlineData(1, 25, 20, 100, 1, 1, 60, false)]
    [InlineData(2, 35, 20, 100, 1, 1, 60, false)]
    [InlineData(3, 55, 20, 100, 1, 1, 60, false)]
    [InlineData(4, 20, 15, 100, 1, 1, 60, false)]
    [InlineData(5, 20, 30, 100, 1, 1, 60, false)]
    [InlineData(6, 20, 45, 100, 1, 1, 60, false)]
    [InlineData(7, 15, 20, 60, 1, 1, 60, false)]
    [InlineData(8, 15, 20, 130, 1, 1, 60, false)]
    [InlineData(9, 15, 20, 250, 1, 1, 60, false)]
    [InlineData(10, 15, 20, 100, 2, 1, 60, false)]
    [InlineData(11, 15, 20, 100, 1, 2, 60, false)]
    [InlineData(12, 32, 48, 130, 1, 1, 60, true)]
    [InlineData(13, 22, 12, 70, 1, 1, 60, true)]
    [InlineData(14, 28, 40, 160, 1, 2, 60, true)]
    [InlineData(15, 15, 20, 100, 1, 1, 100, false)]
    [InlineData(16, 30, 20, 100, 1, 1, 100, false)]
    [InlineData(17, 15, 20, 100, 1, 1, 30, false)]
    [InlineData(18, 15, 20, 100, 1, 1, 90, false)]
    [InlineData(19, 20, 45, 100, 1, 1, 100, false)]
    public void Every_corner_lands_within_a_third_of_a_point(
        int page, int rotX, int rotY, int depthPercent, int categories, int series, int value,
        bool heldBack)
    {
        AssertWithin("chart-3d-projection-probe", page, rotX, rotY, depthPercent, 0,
            categories, series, value, heldBack, bar: 0.35);
    }

    /// <summary>
    /// A stated <c>c:hPercent</c> makes the box exactly that share of a category unit tall.
    /// </summary>
    /// <remarks>
    /// The rule is the camera's (#109) and it is verified on this arm too: a free fit of the box's
    /// height on the hPercent 150 page lands on the stated rule to a thousandth. The half-point
    /// bar rather than the third-point one is the placement's: a taller box takes a smaller scale,
    /// and the margin constants' intervals cost more of a point at the extremes of the height
    /// sweep than in its middle.
    /// </remarks>
    [Theory]
    [InlineData(20, 150)]
    [InlineData(21, 50)]
    public void A_stated_height_holds_to_a_half_point(int page, int hPercent)
    {
        AssertWithin("chart-3d-projection-probe", page, 15, 20, 100, hPercent, 1, 1, 60,
            heldBack: false, bar: 0.55);
    }

    /// <summary>
    /// The committed probes' wider sweeps stay under the camera's wider-tier bar — still no
    /// fitting, still page coordinates, over rotY 5..65, rotX 5..60 and depth 20..500.
    /// </summary>
    /// <remarks>
    /// What the extra fraction is, so nobody mistakes it for slack: these references were read by
    /// an older vintage of #106's instrument (the same caveat
    /// <see cref="Chart3DCameraTests.The_wider_sweep_stays_under_a_point"/> carries), and the
    /// 5°-tilt page's silhouette is nearly degenerate — its top corners turn by little more than
    /// the finder's refusal threshold, which is where the instrument is at its weakest.
    /// </remarks>
    [Theory]
    [InlineData("chart-3d-rotation-probe", 0, 20, 5, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 1, 20, 10, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 2, 20, 20, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 3, 20, 35, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 4, 20, 50, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 5, 20, 65, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 6, 5, 20, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 7, 10, 20, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 8, 30, 20, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 9, 45, 20, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 10, 60, 20, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 11, 40, 45, 100, 1, 1)]
    [InlineData("chart-3d-rotation-probe", 12, 25, 60, 100, 1, 1)]
    [InlineData("chart-3d-depth-probe", 0, 15, 20, 20, 1, 1)]
    [InlineData("chart-3d-depth-probe", 1, 15, 20, 50, 1, 1)]
    [InlineData("chart-3d-depth-probe", 2, 15, 20, 100, 1, 1)]
    [InlineData("chart-3d-depth-probe", 3, 15, 20, 150, 1, 1)]
    [InlineData("chart-3d-depth-probe", 4, 15, 20, 200, 1, 1)]
    [InlineData("chart-3d-depth-probe", 5, 15, 20, 300, 1, 1)]
    [InlineData("chart-3d-depth-probe", 6, 15, 20, 500, 1, 1)]
    [InlineData("chart-3d-depth-probe", 7, 15, 20, 50, 1, 3)]
    [InlineData("chart-3d-depth-probe", 8, 15, 20, 200, 1, 3)]
    [InlineData("chart-3d-depth-probe", 9, 15, 20, 200, 3, 1)]
    [InlineData("chart-3d-depth-probe", 10, 15, 20, 50, 2, 2)]
    public void The_committed_sweeps_stay_under_the_wider_bar(
        string fixture, int page, int rotX, int rotY, int depthPercent, int categories, int series)
    {
        AssertWithin(fixture, page, rotX, rotY, depthPercent, 0, categories, series, 60,
            heldBack: false, bar: 0.85);
    }

    /// <summary>
    /// A wrong model's corners, built by repeating the projection's arithmetic with one named
    /// thing put back wrong.
    /// </summary>
    private static List<(double X, double Y)> Wrong(
        string what, double rotX, double rotY, double depthPercent, int categories, int series,
        double value)
    {
        const double rectLeft = 144, rectTop = 93.6, rectWidth = 216, rectHeight = 118.8;
        var sinA = Math.Sin(rotX * Math.PI / 180);
        var sinB = Math.Sin(rotY * Math.PI / 180);
        var cosA = Math.Cos(rotX * Math.PI / 180);
        var cosB = Math.Cos(rotY * Math.PI / 180);
        var hx = categories / 2.0;
        var hz = series * depthPercent / 100 / 2;
        var hy = what == "unit height"
            ? Math.Floor((categories + series) / 2.0) / 2
            : Math.Floor((categories + series) / 2.0) * (rectHeight / rectWidth) / 2;

        if (what == "leans swapped") (sinA, sinB) = (sinB, sinA);

        (double X, double Y) Screen(double sx, double sy, double sz)
        {
            if (what == "a rotation")
            {
                // Turn about the vertical by rotY, then the horizontal by minus rotX, and drop z —
                // the model #98 originally asked for and #140 disproved.
                (sx, sz) = (sx * cosB + sz * sinB, -sx * sinB + sz * cosB);
                sy = sy * cosA + sz * sinA;
                return (sx, sy);
            }
            return (sx + sz * sinB, sy + sz * sinA);
        }

        var corners = new List<(double X, double Y)>();
        foreach (var x in new[] { -hx, hx })
        foreach (var y in new[] { -hy, -hy + 2 * hy * (value - Chart3DObliqueProjection.BarTopShortfall) })
        foreach (var z in new[] { -hz, hz })
            corners.Add(Screen(x, y, z));

        double aL = 0.0098, aR = 0.0099 + 0.0210 * sinB, aB = 0.0121, aT = 0.0056 + 0.0259 * sinA;
        if (what == "no lean share") (aL, aR, aB, aT) = (0.0103, 0.0103, 0.0103, 0.0103);

        var box = new List<(double X, double Y)>();
        foreach (var x in new[] { -hx, hx })
        foreach (var y in new[] { -hy, hy })
        foreach (var z in new[] { -hz, hz })
            box.Add(Screen(x, y, z));

        var xMin = box.Min(p => p.X) - 2 * hx * aL;
        var xMax = box.Max(p => p.X) + 2 * hx * aR;
        var yMin = box.Min(p => p.Y) - 2 * hx * aB;
        var yMax = box.Max(p => p.Y) + 2 * hx * aT;
        var s = Math.Min(rectWidth / (xMax - xMin), rectHeight / (yMax - yMin));
        var cx = rectLeft + rectWidth / 2 - s * (xMin + xMax) / 2;
        var cy = rectTop + rectHeight / 2 + s * (yMin + yMax) / 2;

        return Hull(corners.Select(p => (cx + s * p.X, cy - s * p.Y)).ToList());
    }

    /// <summary>
    /// Each element of the law, put back wrong, moves the corners past the bar — so every one of
    /// them is doing load-bearing work against Word's raster.
    /// </summary>
    /// <remarks>
    /// The page is 55° by 20°, where tilt and turn differ most among the fitted pages. "A
    /// rotation" is the model the projection is not (#140): turning the box tilts its width axis,
    /// which Word holds level. "Leans swapped" reads each angle into the other axis. "No lean
    /// share" keeps the margins' size but spreads them evenly, dropping the extra the leaning
    /// sides carry. "Unit height" makes the box half a unit tall instead of following the
    /// rectangle's aspect (#137).
    /// </remarks>
    [Theory]
    [InlineData("a rotation", 2.0)]
    [InlineData("leans swapped", 2.0)]
    [InlineData("no lean share", 0.55)]
    [InlineData("unit height", 5.0)]
    public void Put_back_wrong_it_fails(string what, double atLeast)
    {
        if (WordCorners("chart-3d-projection-probe", 3) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var wrong = Astray(Wrong(what, 55, 20, 100, 1, 1, 0.6), word);
        var right = Astray(ModelCorners(Projection(55, 20, 100, null, 1, 1), 0.6), word);

        _output.WriteLine($"{what}: {wrong:0.000}pt astray where the law is {right:0.000}pt");

        Assert.True(right < 0.35, $"the law itself is {right:0.000}pt astray on the injection page");
        Assert.True(wrong > atLeast,
            $"{what} lands {wrong:0.000}pt astray — no longer far enough past the law to tell them apart");
    }

    /// <summary>
    /// The bar's shortfall below the box top is real: without it, a bar at the axis maximum
    /// overshoots Word's by nearly half a point.
    /// </summary>
    /// <remarks>
    /// The value-100 page is where it shows undiluted: the whole silhouette is the box, and
    /// modelling the bar as reaching the box's top puts its three top corners past Word's.
    /// </remarks>
    [Fact]
    public void The_bar_stops_short_of_the_box_top()
    {
        if (WordCorners("chart-3d-projection-probe", 15) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var projection = Projection(15, 20, 100, null, 1, 1);

        var withIt = Astray(ModelCorners(projection, 1.0), word);

        var without = new List<(double X, double Y)>();
        foreach (var x in new[] { 0.0, 1.0 })
        foreach (var y in new[] { 0.0, 1.0 })
        foreach (var z in new[] { 0.0, 1.0 })
            without.Add(projection.Project(x, y, z));

        var astray = Astray(Hull(without), word);

        _output.WriteLine($"bar to the box top: {astray:0.000}pt astray; stopped short: {withIt:0.000}pt");

        Assert.True(withIt < 0.35);
        Assert.True(astray > withIt + 0.2,
            $"the full-height bar is only {astray:0.000}pt astray against {withIt:0.000}pt — the shortfall no longer shows");
    }
}
