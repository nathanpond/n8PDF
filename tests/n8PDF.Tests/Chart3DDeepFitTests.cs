using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The #141 fitter — a diagnostic, not an assertion (both facts are <c>Skip</c>ped; run by hand).
/// It recovers Word's own (F, ex, ey) per deep-perspective page by fitting the pinned #98 placement
/// to the bar silhouette, and is the harness the derivation of the deep-regime offset laws works
/// from.
/// </summary>
/// <remarks>
/// Validated two ways: on the passing frustum rows it reproduces Word to ≤0.21pt, and on the deep
/// pages its per-page (F, ex, ey) reproduce the coefficient table recorded on #141 to the
/// thousandth. The page index is chartN − 1 (the rasteriser is 0-based; the deep-probe's chartN.xml
/// is its Nth chart) — an earlier off-by-one here invented a spurious fourth camera parameter.
/// </remarks>
public class Chart3DDeepFitTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const double RectLeft = 144, RectTop = 93.6, RectWidth = 216, RectHeight = 118.8;
    private const double Fill = 0.9702, FloorScale = 1.0306;

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 120 && p.G < 90 && p.B < 90;

    public readonly record struct Page(
        string Fixture,
        int Index,
        double RotX,
        double RotY,
        double Persp,
        double Depth,
        double? H);

    private static readonly Page[] Deep =
    [
        P(1, 15, 20, 110), P(2, 15, 20, 150), P(3, 15, 20, 190), P(4, 30, 20, 90), P(5, 30, 20, 140),
        P(6, 30, 20, 160), P(7, 30, 20, 180), P(8, 30, 20, 220), P(9, 30, 20, 240), P(10, 45, 20, 140),
        P(11, 45, 20, 180), P(12, 45, 20, 220), P(13, 45, 20, 200), P(14, 22, 20, 100), P(15, 22, 20, 140),
        P(16, 22, 20, 160), P(17, 22, 20, 180), P(18, 22, 20, 240), P(19, 38, 20, 100), P(20, 38, 20, 120),
        P(21, 38, 20, 160), P(22, 38, 20, 180), P(23, 38, 20, 200), P(24, 38, 20, 240), P(25, 30, 35, 100),
        P(26, 30, 35, 140), P(27, 30, 35, 160), P(28, 30, 35, 180), P(29, 30, 35, 240), P(30, 15, 50, 120),
        P(31, 15, 50, 140), P(32, 15, 50, 180), P(33, 15, 50, 240), P(34, 60, 15, 120), P(35, 60, 15, 160),
        P(36, 60, 15, 200), P(37, 60, 15, 240), P(38, 45, 45, 160), P(39, 45, 45, 200), P(40, 45, 45, 240),
        new("chart-3d-deep-probe", 41, 30, 20, 160, 50, null),
        new("chart-3d-deep-probe", 42, 30, 20, 160, 150, null),
        new("chart-3d-deep-probe", 43, 30, 20, 160, 200, null),
        new("chart-3d-deep-probe", 44, 15, 20, 160, 100, 50),
        new("chart-3d-deep-probe", 45, 15, 20, 160, 100, 100),
        new("chart-3d-deep-probe", 46, 15, 20, 160, 100, 150),
        // #141 round 9 probes: the ex t-curvature (15/20 filling p190→p240), two rotX slope arms
        // (25 and 33 at rotY 20, wide perspective), and a 15/20 depth arm to pair with the 30/20 one.
        new("chart-3d-deep2-probe", 1, 15, 20, 200, 100, null),
        new("chart-3d-deep2-probe", 2, 15, 20, 220, 100, null),
        new("chart-3d-deep2-probe", 3, 25, 20, 120, 100, null),
        new("chart-3d-deep2-probe", 4, 25, 20, 180, 100, null),
        new("chart-3d-deep2-probe", 5, 25, 20, 240, 100, null),
        new("chart-3d-deep2-probe", 6, 33, 20, 120, 100, null),
        new("chart-3d-deep2-probe", 7, 33, 20, 180, 100, null),
        new("chart-3d-deep2-probe", 8, 33, 20, 240, 100, null),
        new("chart-3d-deep2-probe", 9, 15, 20, 160, 50, null),
        new("chart-3d-deep2-probe", 10, 15, 20, 160, 100, null),
        new("chart-3d-deep2-probe", 11, 15, 20, 160, 200, null),
    ];

    private static Page P(int i, double rx, double ry, double q) => new("chart-3d-deep-probe", i, rx, ry, q, 100, null);

    private static (double Hx, double Hy, double Hz) Box(Page p)
    {
        var hy = p.H is { } stated ? stated / 100 / 2 : RectHeight / RectWidth / 2;
        return (0.5, hy, p.Depth / 100 / 2);
    }

    private static double GeometryTan(Page p) => Math.Tan(p.Persp / 4 * Math.PI / 180);

    private static (double X, double Y) Project(Page p, double f, double ex, double ey, double x, double y, double z)
    {
        var a = p.RotX * Math.PI / 180;
        var b = p.RotY * Math.PI / 180;
        var (cosA, sinA, cosB, sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));
        var td = GeometryTan(p);
        var (hx, hy, hz) = Box(p);
        var sx = (x - 0.5) * 2 * hx;
        var sy = (y - 0.5) * 2 * hy;
        var sz = (z - 0.5) * 2 * hz;
        (sx, sz) = (sx * cosB + sz * sinB, -sx * sinB + sz * cosB);
        (sy, sz) = (sy * cosA + sz * sinA, -sy * sinA + sz * cosA);
        var towards = f / (sz * td + f);
        var scale = RectHeight / 2 / f;
        return (RectLeft + RectWidth / 2 + scale * (sx - ex) * towards,
            RectTop + RectHeight / 2 - scale * (sy - ey) * towards);
    }

    private static List<(double X, double Y)> Hull3(Page p, double f, double ex, double ey)
    {
        var pts = new List<(double X, double Y)>();
        foreach (var x in new[] { 0.0, 1.0 })
        foreach (var y in new[] { 0.0, 0.6 })
        foreach (var z in new[] { 0.0, 1.0 })
            pts.Add(Project(p, f, ex, ey, x, y, z));
        return Hull(pts);
    }

    private static double ToBoundary((double X, double Y) q, IReadOnlyList<(double X, double Y)> poly)
    {
        var best = double.PositiveInfinity;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            var t = len2 > 0 ? Math.Clamp(((q.X - a.X) * dx + (q.Y - a.Y) * dy) / len2, 0, 1) : 0;
            double px = a.X + t * dx, py = a.Y + t * dy;
            best = Math.Min(best, Math.Sqrt((q.X - px) * (q.X - px) + (q.Y - py) * (q.Y - py)));
        }

        return best;
    }

    private static double Hausdorff(IReadOnlyList<(double X, double Y)> m, IReadOnlyList<(double X, double Y)> w)
    {
        double worst = 0;
        foreach (var q in w) worst = Math.Max(worst, ToBoundary(q, m));
        foreach (var q in m) worst = Math.Max(worst, ToBoundary(q, w));
        return worst;
    }

    private static double SumSq(Page p, double f, double ex, double ey, IReadOnlyList<(double X, double Y)> w)
    {
        var m = Hull3(p, f, ex, ey);
        double s = 0;
        foreach (var q in w)
        {
            var d = ToBoundary(q, m);
            s += d * d;
        }

        foreach (var q in m)
        {
            var d = ToBoundary(q, w);
            s += d * d;
        }

        return s;
    }

    private IReadOnlyList<(double X, double Y)>? WordCorners(Page p)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, p.Fixture + ".pdf");
        if (!File.Exists(path)) return null;
        const double scale = 6;
        // chartN.xml is the Nth chart; the rasteriser page index is 0-based, so index = N - 1.
        if (PdfRasterizer.Render(File.ReadAllBytes(path), p.Index - 1, scale) is not { } r) return null;
        var s = BoxSilhouette.Find(r, scale, Reddish, (73, 73, 431, 287));
        return s.Found ? s.Points : null;
    }

    private static (double F, double Ex, double Ey) Seed(Page p)
    {
        var a = p.RotX * Math.PI / 180;
        var b = p.RotY * Math.PI / 180;
        var (cosA, sinA, cosB, sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));
        var tan = GeometryTan(p);
        var (hx, hy, hz) = Box(p);
        var aspect = RectWidth / RectHeight;
        var extentY = hx * Math.Abs(sinA * sinB) + hy * cosA + hz * Math.Abs(sinA * cosB);
        var extentX = hx * Math.Abs(cosB) + hz * Math.Abs(sinB);
        var floorPart = FloorScale * (hx * Math.Abs(sinB * cosA) + hz * Math.Abs(cosB * cosA));
        var byHeight = extentY / Fill;
        var byWidth = extentX / (Fill * 0.9862 * aspect);
        var byFloor = floorPart * tan + hy;
        var f = Math.Max(byHeight, Math.Max(byWidth, byFloor));
        var eyeTan = byFloor > byHeight && byFloor > byWidth ? (Math.Max(byHeight, byWidth) - hy) / floorPart : tan;
        var ex = eyeTan * aspect * cosA * (hx * sinB - hz * cosB);
        var ey = -eyeTan * FloorScale * (hx * sinB * cosA + Fill * hz * cosB * cosA - hy * sinA);
        return (f, ex, ey);
    }

    private static double[] NelderMead(Func<double[], double> cost, double[] start, double step)
    {
        var n = start.Length;
        var s = new List<double[]> { (double[])start.Clone() };
        for (var k = 0; k < n; k++)
        {
            var v = (double[])start.Clone();
            v[k] += step;
            s.Add(v);
        }

        var c = s.Select(cost).ToList();
        for (var it = 0; it < 2000; it++)
        {
            var ord = Enumerable.Range(0, n + 1).OrderBy(i => c[i]).ToArray();
            s = ord.Select(i => s[i]).ToList();
            c = ord.Select(i => c[i]).ToList();
            if (c[n] - c[0] < 1e-12) break;
            var m = new double[n];
            for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
                m[j] += s[i][j] / n;

            double[] R(double t)
            {
                var v = new double[n];
                for (var j = 0; j < n; j++) v[j] = m[j] + t * (m[j] - s[n][j]);
                return v;
            }

            var r = R(1);
            var cr = cost(r);
            if (cr < c[0])
            {
                var e = R(2);
                var ce = cost(e);
                if (ce < cr)
                {
                    s[n] = e;
                    c[n] = ce;
                }
                else
                {
                    s[n] = r;
                    c[n] = cr;
                }
            }
            else if (cr < c[n - 1])
            {
                s[n] = r;
                c[n] = cr;
            }
            else
            {
                var k = R(-0.5);
                var ck = cost(k);
                if (ck < c[n])
                {
                    s[n] = k;
                    c[n] = ck;
                }
                else
                    for (var i = 1; i <= n; i++)
                    {
                        for (var j = 0; j < n; j++) s[i][j] = (s[i][j] + s[0][j]) / 2;
                        c[i] = cost(s[i]);
                    }
            }
        }

        return Enumerable.Range(0, n + 1).OrderBy(i => c[i]).Select(i => s[i]).First();
    }

    private static double[] Best(double[] start, Func<double[], double> cost)
    {
        double bc = double.MaxValue;
        var bv = start;
        foreach (var st in new[] { 0.06, 0.15, 0.3 })
        {
            var v = NelderMead(cost, start, st);
            var c = cost(v);
            if (c < bc)
            {
                bc = c;
                bv = v;
            }
        }

        return bv;
    }

    [Fact(Skip = "#141 diagnostic — run by hand; rasterises every deep page")]
    public void Report_the_deep_fit()
    {
        var fits = new List<(Page P, double T, double F, double Ex, double Ey, double Res)>();
        _output.WriteLine("page   geom     t      res     F       ex       ey");
        foreach (var p in Deep)
        {
            var word = WordCorners(p);
            if (word is null)
            {
                _output.WriteLine($"p{p.Index,2} {p.RotX}/{p.RotY} q{p.Persp}: refused");
                continue;
            }

            var t = GeometryTan(p);
            var (f0, ex0, ey0) = Seed(p);
            var v = Best([f0, ex0, ey0], a => SumSq(p, a[0], a[1], a[2], word));
            var res = Hausdorff(Hull3(p, v[0], v[1], v[2]), word);
            fits.Add((p, t, v[0], v[1], v[2], res));
            _output.WriteLine(
                $"p{p.Index,2} {p.RotX,2}/{p.RotY,-2} q{p.Persp,3} t={t:F3}  {res,6:F3}  {v[0]:F4}  {v[1],7:F4}  {v[2],7:F4}");
        }

        _output.WriteLine("\n=== per-geometry linear laws in t (pages under 0.3pt) ===");
        foreach (var g in fits.Where(f => f.Res < 0.3)
                     .GroupBy(f => (f.P.RotX, f.P.RotY, f.P.Depth, f.P.H))
                     .Where(g => g.Count() >= 2))
        {
            var pts = g.OrderBy(f => f.T).ToList();
            var f = Line(pts.Select(r => (r.T, r.F)));
            var x = Line(pts.Select(r => (r.T, r.Ex)));
            var y = Line(pts.Select(r => (r.T, r.Ey)));
            _output.WriteLine($"X{g.Key.RotX} Y{g.Key.RotY} d{g.Key.Depth} h{g.Key.H?.ToString() ?? "-"} ({g.Count()}):"
                              + $"  F={f.S:+0.0000;-0.0000},{f.I:+0.0000;-0.0000}"
                              + $"  ex={x.S:+0.000;-0.000},{x.I:+0.000;-0.000}"
                              + $"  ey={y.S:+0.0000;-0.0000},{y.I:+0.0000;-0.0000}");
        }
    }

    [Fact(Skip = "#141 diagnostic — validates the instrument against passing frustum rows")]
    public void Validate_instrument_on_frustum()
    {
        (string Fx, int Pg, int Rx, int Ry, int Q, int Dp)[] rows =
        [
            ("chart-3d-perspective-probe", 4, 15, 20, 30, 100),
            ("chart-3d-perspective-probe", 0, 15, 20, 0, 100),
            ("chart-3d-eye-probe", 3, 30, 20, 30, 200),
            ("chart-3d-camera-probe", 10, 60, 15, 50, 100),
        ];
        foreach (var r in rows)
        {
            var path = Path.Combine(TestPaths.ReferencePdfs, r.Fx + ".pdf");
            if (PdfRasterizer.Render(File.ReadAllBytes(path), r.Pg, 6) is not { } rendered) continue;
            var sil = BoxSilhouette.Find(rendered, 6, Reddish, (73, 73, 431, 287));
            if (!sil.Found)
            {
                _output.WriteLine($"{r.Fx} p{r.Pg}: no corners");
                continue;
            }

            var real = new Chart3DProjection(r.Rx, r.Ry, r.Q, r.Dp, null, 1, 1,
                rectLeft: RectLeft, rectTop: RectTop, rectWidth: RectWidth, rectHeight: RectHeight);
            var pts = new List<(double X, double Y)>();
            foreach (var x in new[] { 0.0, 1.0 })
            foreach (var y in new[] { 0.0, 0.6 })
            foreach (var z in new[] { 0.0, 1.0 })
                pts.Add(real.Project(x, y, z));
            _output.WriteLine($"{r.Fx} p{r.Pg}: real-vs-Word = {Hausdorff(Hull(pts), sil.Points):F3}pt");
        }
    }

    private static (double S, double I) Line(IEnumerable<(double X, double Y)> pts)
    {
        var l = pts.ToList();
        double n = l.Count,
            sx = l.Sum(p => p.X),
            sy = l.Sum(p => p.Y),
            sxx = l.Sum(p => p.X * p.X),
            sxy = l.Sum(p => p.X * p.Y);
        var slope = (n * sxy - sx * sy) / (n * sxx - sx * sx);
        return (slope, (sy - slope * sx) / n);
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
        return lower;
    }
}