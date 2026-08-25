using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The perspective camera for <c>rAngAx="0"</c>, checked corner by corner against Word.
/// </summary>
/// <remarks>
/// <see cref="Chart3DProjection"/> holds the laws; this holds them to Word's own output. Every
/// page here is a red bar filling its box (gaps nought, value 60 of 100) in a plot rectangle
/// stated at x 0.2, y 0.1, w 0.6, h 0.55 of a 360x216 chart at (72,72), so the bar's silhouette
/// is the projected box and #106's finder reads its six corners to better than a tenth of a
/// point. The model's corners are compared **in page coordinates** — no fitting, no scale taken
/// from the page, nothing free.
///
/// The pages marked held back were never used while the laws were being found; they combine
/// angles, depths and heights the fitted pages do not.
/// </remarks>
public class Chart3DCameraTests(ITestOutputHelper output)
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

    private static Chart3DProjection Projection(
        double rotX, double rotY, double perspective, double depthPercent, double? hPercent,
        int categories, int series) =>
        new(rotX, rotY, perspective, depthPercent, hPercent, categories, series,
            rectLeft: 144, rectTop: 93.6, rectWidth: 216, rectHeight: 118.8);

    /// <summary>The hull of the bar the probes draw: the whole box up to value 60 of 100.</summary>
    private static List<(double X, double Y)> ModelCorners(Chart3DProjection projection)
    {
        var points = new List<(double X, double Y)>();
        foreach (var x in new[] { 0.0, 1.0 })
        foreach (var y in new[] { 0.0, 0.6 })
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

    /// <summary>
    /// The camera puts every corner of the box within a quarter point of Word's, in page
    /// coordinates, across rotations, perspectives, depths, heights and counts.
    /// </summary>
    /// <remarks>
    /// Nothing here is taken from the page — not a scale, not an offset. The two held-back rows
    /// were never consulted while the laws were measured.
    /// </remarks>
    [Theory]
    [InlineData("chart-3d-perspective-probe", 0, 15, 20, 0, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-perspective-probe", 8, 40, 45, 0, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-perspective-probe", 2, 15, 20, 10, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-perspective-probe", 3, 15, 20, 20, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-perspective-probe", 4, 15, 20, 30, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-perspective-probe", 9, 40, 45, 30, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-camera-probe", 10, 60, 15, 50, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 0, 15, 20, 30, 75, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 1, 15, 20, 30, 150, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 2, 30, 20, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 3, 30, 20, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 5, 45, 20, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 8, 40, 45, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 9, 40, 45, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 14, 15, 20, 30, 100, 0, 1, 2, false)]
    [InlineData("chart-3d-eye-probe", 21, 15, 20, 30, 100, 150, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 23, 33, 27, 66, 130, 0, 1, 1, true)]
    [InlineData("chart-3d-eye2-probe", 0, 30, 20, 30, 100, 100, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 1, 30, 20, 30, 100, 150, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 2, 40, 45, 30, 100, 100, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 3, 40, 45, 30, 100, 150, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 5, 40, 45, 30, 100, 0, 2, 1, false)]
    [InlineData("chart-3d-eye2-probe", 10, 15, 35, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 11, 15, 35, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 18, 37, 23, 30, 160, 90, 1, 1, true)]
    public void Every_corner_lands_within_a_quarter_point(
        string fixture, int page, int rotX, int rotY, int perspective, int depthPercent,
        int hPercent, int categories, int series, bool heldBack)
    {
        AssertWithin(fixture, page, rotX, rotY, perspective, depthPercent, hPercent,
            categories, series, heldBack, bar: 0.24);
    }

    /// <summary>
    /// The wider sweep stays under a point — still no fitting, still page coordinates, over
    /// twelve more angle pairs, both count axes and the recorded pages of the second run.
    /// </summary>
    /// <remarks>
    /// What the extra fraction is, so nobody mistakes it for slack: the recorded pages carry
    /// corners found by an older vintage of #106's instrument, a tenth or two adrift of
    /// today's; the constants of the placement identities (the 0.9703 fill, the frustum scale)
    /// hold to a third of a percent page by page, which is one to two tenths at this scene size
    /// and looks like Word's own page-grid snapping; the width-bound fill constant is settled
    /// (#141 — the single- and two-category width-bound pages agree to ±0.13%, a three-category
    /// box wanting ~0.7% less), so the fraction of a point it carries here is the wide 3cat page,
    /// not doubt about the constant; and near the edge of the verified domain the eye offsets
    /// begin their migration toward the deep-perspective regime the follow-up issue owns. Three
    /// of these rows are held back.
    /// </remarks>
    [Theory]
    [InlineData("chart-3d-perspective-probe", 1, 15, 20, 5, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-camera-probe", 11, 15, 20, 30, 100, 0, 2, 1, false)]
    [InlineData("chart-3d-eye-probe", 4, 45, 20, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 6, 15, 50, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 11, 60, 15, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-eye-probe", 12, 15, 20, 30, 100, 0, 3, 1, false)]
    [InlineData("chart-3d-eye-probe", 24, 18, 62, 45, 80, 130, 1, 1, true)]
    [InlineData("chart-3d-eye2-probe", 4, 30, 20, 30, 100, 0, 2, 1, false)]
    [InlineData("chart-3d-eye2-probe", 7, 60, 15, 30, 150, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 8, 10, 70, 30, 75, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 9, 10, 70, 30, 150, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 12, 15, 65, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 13, 15, 65, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-eye2-probe", 19, 12, 48, 30, 60, 0, 1, 1, true)]
    [InlineData("chart-3d-branch-probe", 19, 22, 41, 70, 100, 0, 2, 1, true)]
    [InlineData("chart-3d-geometry-probe-recorded", 1, 30, 20, 30, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 2, 45, 20, 30, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 3, 15, 35, 30, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 4, 15, 50, 30, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 5, 15, 20, 15, 100, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 6, 15, 20, 30, 50, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 7, 15, 20, 30, 200, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 8, 25, 40, 45, 150, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 9, 35, 30, 20, 75, 0, 1, 1, false)]
    [InlineData("chart-3d-geometry-probe-recorded", 10, 20, 55, 50, 120, 0, 1, 1, false)]
    public void The_wider_sweep_stays_under_a_point(
        string fixture, int page, int rotX, int rotY, int perspective, int depthPercent,
        int hPercent, int categories, int series, bool heldBack)
    {
        AssertWithin(fixture, page, rotX, rotY, perspective, depthPercent, hPercent,
            categories, series, heldBack, bar: 0.85);
    }

    private void AssertWithin(
        string fixture, int page, int rotX, int rotY, int perspective, int depthPercent,
        int hPercent, int categories, int series, bool heldBack, double bar)
    {
        var word = fixture == "chart-3d-geometry-probe-recorded"
            ? Recorded(page)
            : WordCorners(fixture, page);
        if (word is null)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var projection = Projection(rotX, rotY, perspective, depthPercent,
            hPercent == 0 ? null : hPercent, categories, series);
        var worst = Astray(ModelCorners(projection), word);

        _output.WriteLine($"{rotX}° by {rotY}°, perspective {perspective}, depth {depthPercent}" +
                          $"{(hPercent > 0 ? $", hPercent {hPercent}" : "")}" +
                          $"{(categories > 1 ? $", {categories} categories" : "")}" +
                          $"{(series > 1 ? $", {series} series" : "")}" +
                          $"{(heldBack ? " (held back)" : "")}: worst corner {worst:0.000}pt");

        Assert.True(worst < bar, $"the worst corner is {worst:0.000}pt from Word's, above the {bar}pt bar");
    }

    /// <summary>
    /// The second run's corner lists, measured by #106's instrument against Word's own raster
    /// and recorded on #98 — pages the raster round does not have to be repeated for.
    /// </summary>
    private static IReadOnlyList<(double X, double Y)> Recorded(int page)
    {
        double[][] pages =
        [
            [],
            [187.8690,158.2001, 230.1229,113.9323, 321.0245,128.2072, 318.7038,155.9609, 293.2685,210.4752, 190.3613,188.2614],
            [196.5539,166.0085, 231.5162,109.5816, 311.6718,128.4452, 309.1471,148.6552, 284.8630,210.6323, 199.1009,186.2023],
            [161.9698,139.4346, 241.7272,118.9043, 343.2287,132.3257, 341.0414,171.7944, 275.6134,210.1753, 164.1845,181.4574],
            [160.4461,133.9585, 257.1513,118.7692, 342.9638,137.5018, 340.6949,178.6491, 240.0630,210.1514, 162.5958,173.8688],
            [166.9060,151.6883, 219.0341,121.7569, 338.8306,131.8071, 337.7985,172.5032, 299.8814,210.4069, 167.9225,195.5014],
            [165.4265,140.3690, 201.0755,125.3290, 339.4067,135.7971, 337.0117,183.4747, 321.9484,210.2841, 167.9835,190.0225],
            [171.6408,154.7533, 256.2018,113.3236, 340.5185,118.6491, 338.9994,147.1301, 293.5381,210.0806, 173.5221,193.9675],
            [173.8534,145.2097, 265.3903,111.0540, 333.9919,127.6576, 330.8945,153.4548, 249.8390,210.2138, 177.2476,173.9278],
            [187.4335,149.8844, 228.9981,113.7406, 317.3184,141.0096, 315.5478,168.4165, 279.8995,210.5739, 189.3068,177.5881],
            [163.6357,129.8547, 267.6490,114.2380, 339.2685,133.8013, 335.2357,168.7733, 217.3699,209.9708, 167.5374,163.3485]
        ];
        var raw = pages[page];
        var list = new List<(double X, double Y)>();
        for (var i = 0; i < raw.Length; i += 2) list.Add((raw[i], raw[i + 1]));
        return list;
    }

    /// <summary>
    /// Deep perspective on a mild scene: the eye offsets now follow the measured laws, and this
    /// says how close that leaves the corners.
    /// </summary>
    /// <remarks>
    /// The near-floor constraint sets the eye distance here, and #141 implemented the deep-regime
    /// offsets it measured off Word (rotX and rotY both inside 45°), so the picture is no longer
    /// clamped. What remains is the eye distance: with the floor law's intercept
    /// foreshortened by cosA (#141) it is within a tenth of a percent at rotX 15 and 22 — the 80
    /// page is down to ~0.3pt — but its slope still biases by rotX 30, and at perspective 240 (past
    /// Word's UI cap of 100) the ex law is slightly concave. This test pins the residual so closing
    /// it is visible and widening it fails.
    /// </remarks>
    [Theory]
    [InlineData("chart-3d-perspective-probe", 6, 15, 20, 80, 0.5)]
    [InlineData("chart-3d-camera-probe", 2, 15, 20, 240, 8.0)]
    public void Deep_perspective_is_close_but_not_yet_within_the_bar(
        string fixture, int page, int rotX, int rotY, int perspective, double ceiling)
    {
        if (WordCorners(fixture, page) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var projection = Projection(rotX, rotY, perspective, 100, null, 1, 1);
        var worst = Astray(ModelCorners(projection), word);

        _output.WriteLine($"{rotX}° by {rotY}°, perspective {perspective}: worst corner {worst:0.000}pt");

        Assert.True(worst < ceiling,
            $"the deep-perspective gap has widened: {worst:0.000}pt against the recorded {ceiling}pt");
        Assert.True(worst > 0.24,
            $"the deep-perspective page is now within the bar at {worst:0.000}pt — the gap has " +
            "closed, so promote this page into the quarter-point test and retire this one");
    }

    /// <summary>
    /// Put back wrong, the camera misses by an order of magnitude: <c>perspective</c> read as
    /// whole degrees, the eye put back on the axis, or the fill taken as the whole frustum.
    /// </summary>
    [Theory]
    [InlineData("degrees")]
    [InlineData("axis")]
    [InlineData("fill")]
    public void Put_back_wrong_it_misses_by_an_order_of_magnitude(string wrong)
    {
        if (WordCorners("chart-3d-eye-probe", 23) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var right = Astray(ModelCorners(Projection(33, 27, 66, 130, null, 1, 1)), word);
        var astray = Astray(WrongCorners(wrong), word);

        _output.WriteLine($"{wrong}: {astray:0.0}pt against the right camera's {right:0.000}pt");

        Assert.True(astray > 10 * right,
            $"the wrong camera ({wrong}) is within {astray:0.00}pt of Word against the right " +
            $"one's {right:0.000}pt — this page no longer tells right from wrong");
    }

    /// <summary>The camera at 33° by 27°, perspective 66, depth 130, one thing put back wrong.</summary>
    private static List<(double X, double Y)> WrongCorners(string wrong)
    {
        const double rotX = 33 * Math.PI / 180, rotY = 27 * Math.PI / 180;
        const double hx = 0.5, hy = 0.275, hz = 0.65;
        var (cosA, sinA, cosB, sinB) = (Math.Cos(rotX), Math.Sin(rotX), Math.Cos(rotY), Math.Sin(rotY));

        var tan = Math.Tan(wrong == "degrees" ? 66.0 / 2 * Math.PI / 180 : 66.0 / 4 * Math.PI / 180);
        var fill = wrong == "fill" ? 1.0 : 0.9702;
        const double aspect = 216 / 118.8;

        var extentY = hx * Math.Abs(sinA * sinB) + hy * cosA + hz * Math.Abs(sinA * cosB);
        var extentX = hx * cosB + hz * sinB;
        var distance = Math.Max(extentY / (fill * tan), extentX / (fill * 0.9862 * aspect * tan));

        var ex = wrong == "axis" ? 0 : tan * aspect * cosA * (hx * sinB - hz * cosB);
        var ey = wrong == "axis"
            ? 0
            : -tan * 1.0306 * (hx * sinB * cosA + 0.9702 * hz * cosB * cosA - hy * sinA);

        var scale = 118.8 / 2 / (distance * tan);
        var points = new List<(double X, double Y)>();
        foreach (var x in new[] { -hx, hx })
        foreach (var y in new[] { -hy, -hy + 0.6 * 2 * hy })
        foreach (var z in new[] { -hz, hz })
        {
            var (rx, rz) = (x * cosB + z * sinB, -x * sinB + z * cosB);
            var (ry, rz2) = (y * cosA + rz * sinA, -y * sinA + rz * cosA);
            var towards = distance / (rz2 + distance);
            var qx = ex + (rx - ex) * towards;
            var qy = ey + (ry - ey) * towards;
            points.Add((252 + scale * (qx - ex), 153 - scale * (qy - ey)));
        }
        return Hull(points);
    }
}
