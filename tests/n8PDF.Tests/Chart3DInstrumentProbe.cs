using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// #248 diagnostic (Skipped — run by hand): measures the corner finder against the deep-camera box
/// shapes, which the #106 validation (<see cref="Chart3DSilhouetteTests"/>) does not cover.
/// </summary>
/// <remarks>
/// It draws each deep-perspective bar with corners projected exactly by <see cref="Chart3DProjection"/>
/// — so, as with #106, the truth is known to the last decimal and nothing in the library stands
/// between the projected corners and the raster. What it found: on the clean synthetic raster the
/// finder recovers every reachable deep box to 0.03–0.13pt, all under 0.15pt — its fitting is sound
/// on this geometry. Snapping the corners to Word's 1/300-inch grid first (as Word's own geometry is)
/// adds ~0.1pt. And #141's free (F,ex,ey) fit against Word's real raster floors at 0.13–0.22pt. So the
/// deep pages' ~0.2pt residual is the finder reading Word's grid-snapped, shaded raster — a Word-side
/// floor near the 0.24pt grid — not the fitting, which is already fine. The remaining #248 capability
/// is the corner-on band (rotY ≥ 45) the finder refuses outright, needed by #247.
/// </remarks>
public class Chart3DInstrumentProbe(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private const double RectLeft = 144, RectTop = 93.6, RectWidth = 216, RectHeight = 118.8;
    private const double Scale = 6;
    private static readonly (double Left, double Top, double Right, double Bottom) Region = (40, 40, 572, 500);

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 60 && p.R > p.G + 40 && p.R > p.B + 40;

    // The box index order z-outer, y-mid, x-inner, matching Chart3DSilhouetteTests.Faces.
    private static readonly int[][] Faces =
    [
        [0, 1, 3, 2], [4, 5, 7, 6], [0, 1, 5, 4], [2, 3, 7, 6], [0, 2, 6, 4], [1, 3, 7, 5]
    ];

    private record struct Pg(double Rx, double Ry, double Q, double Dp);

    private static readonly Pg[] Pages =
    [
        new(15, 20, 80, 100), new(15, 20, 110, 100), new(15, 20, 160, 100), new(15, 20, 200, 100),
        new(15, 20, 240, 100),
        new(22, 20, 100, 100), new(22, 20, 160, 100),
        new(30, 20, 140, 100), new(30, 20, 160, 100), new(30, 20, 220, 100),
        new(38, 20, 160, 100),
    ];

    [Fact(Skip = "#248 diagnostic — run by hand; rasterises deep-camera box shapes")]
    public void Report()
    {
        _output.WriteLine("geom          shallowest  worstRecovery   (of a pixel@300dpi)  status");
        foreach (var p in Pages)
        {
            var proj = new Chart3DProjection(p.Rx, p.Ry, p.Q, p.Dp, null, 1, 1,
                RectLeft, RectTop, RectWidth, RectHeight);

            // The eight bar corners (the box up to value 0.6), projected exactly.
            var corners = new List<(double X, double Y)>();
            var depths = new List<double>();
            var a = p.Rx * Math.PI / 180;
            var b = p.Ry * Math.PI / 180;
            var (cosA, sinA, cosB, sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));
            var (hx, hy, hz) = (0.5, RectHeight / RectWidth / 2, p.Dp / 100 / 2);
            foreach (var z in new[] { 0.0, 1.0 })
            foreach (var y in new[] { 0.0, 0.6 })
            foreach (var x in new[] { 0.0, 1.0 })
            {
                corners.Add(proj.Project(x, y, z));
                double sx = (x - 0.5) * 2 * hx, sy = (y - 0.5) * 2 * hy, sz = (z - 0.5) * 2 * hz;
                var sz1 = -sx * sinB + sz * cosB;
                depths.Add(-sy * sinA + sz1 * cosA);
            }

            var truth = Hull(corners);
            if (truth.Count != 6)
            {
                _output.WriteLine($"{p.Rx}/{p.Ry} q{p.Q}: truth is {truth.Count}-gon, skip");
                continue;
            }

            var shallowest = ShallowestCorner(truth);

            var page = PdfRasterizer.Render(Paint(corners, depths), 0, Scale);
            if (page is null)
            {
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            var shape = BoxSilhouette.Find(page, Scale, Reddish, Region);
            var clean = shape.Found ? Worst(shape.Points, truth) : double.NaN;

            // Simulate Word: snap each corner to the 1/300-inch grid (0.24pt) before drawing.
            const double grid = 72.0 / 300;
            var snapped = corners.Select(c => (Math.Round(c.X / grid) * grid, Math.Round(c.Y / grid) * grid)).ToList();
            var snapPage = PdfRasterizer.Render(Paint(snapped, depths), 0, Scale);
            if (snapPage is null)
            {
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            var snapShape = BoxSilhouette.Find(snapPage, Scale, Reddish, Region);
            var snapWorst = snapShape.Found ? Worst(snapShape.Points, truth) : double.NaN;

            _output.WriteLine(
                $"{p.Rx}/{p.Ry} q{p.Q,3}   {shallowest,6:F1}°     clean={clean,7:F4}   snapped={snapWorst,7:F4}   " +
                $"(snap adds {snapWorst - clean,6:F4})");
        }
    }

    private static byte[] Paint(List<(double X, double Y)> corners, List<double> depths)
    {
        var shades = new (byte R, byte G, byte B)[] { (200, 30, 30), (255, 70, 70), (150, 20, 20) };
        var order = Enumerable.Range(0, Faces.Length).OrderByDescending(f => Faces[f].Average(c => depths[c])).ToList();
        return PlainPdf.Of(order.Select((face, i) =>
            ((IReadOnlyList<(double X, double Y)>)[.. Faces[face].Select(c => corners[c])],
                shades[i % shades.Length])));
    }

    private static double Worst(IReadOnlyList<(double X, double Y)> found, IReadOnlyList<(double X, double Y)> truth)
    {
        var worst = 0.0;
        foreach (var t in truth)
            worst = Math.Max(worst, found.Min(f => Math.Sqrt((f.X - t.X) * (f.X - t.X) + (f.Y - t.Y) * (f.Y - t.Y))));
        return worst;
    }

    private static double ShallowestCorner(IReadOnlyList<(double X, double Y)> poly)
    {
        var n = poly.Count;
        var shallowest = 180.0;
        for (var i = 0; i < n; i++)
        {
            var prev = poly[(i - 1 + n) % n];
            var cur = poly[i];
            var next = poly[(i + 1) % n];
            double ax = prev.X - cur.X, ay = prev.Y - cur.Y, bx = next.X - cur.X, by = next.Y - cur.Y;
            var dot = ax * bx + ay * by;
            var mag = Math.Sqrt(ax * ax + ay * ay) * Math.Sqrt(bx * bx + by * by);
            var angle = Math.Acos(Math.Clamp(dot / mag, -1, 1)) * 180 / Math.PI;
            // The turn away from straight is 180 − interior angle; shallow means the outline barely bends.
            shallowest = Math.Min(shallowest, 180 - angle);
        }

        return shallowest;
    }

    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], p) <= 1e-9) lower.RemoveAt(lower.Count - 1);
            lower.Add(p);
        }

        var upper = new List<(double X, double Y)>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], p) <= 1e-9) upper.RemoveAt(upper.Count - 1);
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        var first = 0;
        for (var i = 1; i < lower.Count; i++)
            if (lower[i].X < lower[first].X)
                first = i;
        return [.. Enumerable.Range(0, lower.Count).Select(i => lower[(first + i) % lower.Count])];
    }
}