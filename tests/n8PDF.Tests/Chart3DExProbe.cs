using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// #247 diagnostic (Skipped — run by hand): ex's turn (rotY) dependence at fixed rotX 15 /
/// perspective 80, across the mirror-probe's rotY 30–60 and the existing 15/20.
/// </summary>
/// <remarks>
/// What it pins: ex is <b>odd about rotY=45</b>, ex(45)=0, to a thousandth — ex(40)=−0.039 against
/// ex(50)=+0.040, ex(35)=−0.079 against ex(55)=+0.079, ex(30)=−0.091 against ex(60)=+0.091. The
/// shipped ex law's sinB turn factor is right only to about rotY 30 (it matches at 20 and 30) and
/// then keeps climbing where the true factor bends back to zero at 45. So the constraint the
/// rotY=45 symmetry gives — ex odd, ex(45)=0 — is confirmed, but the full turn law is still the
/// "ex resists" wall of #141 rounds 3–4: ex = cosA·f(B) + sinA·g(B), and f,g cannot be separated
/// from silhouette data at a single rotX. This adds the constraint; it does not crack f,g.
/// </remarks>
public class Chart3DExProbe(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private const double RectLeft = 144, RectTop = 93.6, RectWidth = 216, RectHeight = 118.8;
    private const double Fill = 0.9702, FloorScale = 1.0306;

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 120 && p.G < 90 && p.B < 90;

    private record struct Pg(string Fx, int Raster, double Rx, double Ry, double Q, double Dp);

    private static readonly Pg[] Pages =
    [
        new("chart-3d-perspective-probe", 6, 15, 20, 80, 100),
        new("chart-3d-mirror-probe", 6, 15, 30, 80, 100),
        new("chart-3d-mirror-probe", 4, 15, 35, 80, 100),
        new("chart-3d-mirror-probe", 2, 15, 40, 80, 100),
        new("chart-3d-mirror-probe", 0, 15, 45, 80, 100),
        // the mirror (rotY>45) side, to test oddness directly
        new("chart-3d-mirror-probe", 3, 15, 50, 80, 100),
        new("chart-3d-mirror-probe", 5, 15, 55, 80, 100),
        new("chart-3d-mirror-probe", 7, 15, 60, 80, 100),
    ];

    private static (double Hx, double Hy, double Hz) Box(Pg p) => (0.5, RectHeight / RectWidth / 2, p.Dp / 100 / 2);
    private static double Tan(Pg p) => Math.Tan(p.Q / 4 * Math.PI / 180);

    private static (double X, double Y) Project(Pg p, double f, double ex, double ey, double x, double y, double z)
    {
        var a = p.Rx * Math.PI / 180;
        var b = p.Ry * Math.PI / 180;
        var (cosA, sinA, cosB, sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));
        var td = Tan(p);
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

    private static List<(double X, double Y)> Hull3(Pg p, double f, double ex, double ey)
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

    private static double SumSq(Pg p, double f, double ex, double ey, IReadOnlyList<(double X, double Y)> w)
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

    private static double Hausdorff(IReadOnlyList<(double X, double Y)> m, IReadOnlyList<(double X, double Y)> w)
    {
        double worst = 0;
        foreach (var q in w) worst = Math.Max(worst, ToBoundary(q, m));
        foreach (var q in m) worst = Math.Max(worst, ToBoundary(q, w));
        return worst;
    }

    private IReadOnlyList<(double X, double Y)>? WordCorners(Pg p)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, p.Fx + ".pdf");
        if (!File.Exists(path)) return null;
        const double scale = 6;
        if (PdfRasterizer.Render(File.ReadAllBytes(path), p.Raster, scale) is not { } r) return null;
        var s = BoxSilhouette.Find(r, scale, Reddish, (73, 73, 431, 287));
        return s.Found ? s.Points : null;
    }

    private static double SeedF(Pg p)
    {
        var a = p.Rx * Math.PI / 180;
        var b = p.Ry * Math.PI / 180;
        var (cosA, sinA, cosB, sinB) = (Math.Cos(a), Math.Sin(a), Math.Cos(b), Math.Sin(b));
        var (hx, hy, hz) = Box(p);
        var aspect = RectWidth / RectHeight;
        var extentY = hx * Math.Abs(sinA * sinB) + hy * cosA + hz * Math.Abs(sinA * cosB);
        var extentX = hx * Math.Abs(cosB) + hz * Math.Abs(sinB);
        var floorPart = FloorScale * (hx * Math.Abs(sinB * cosA) + hz * Math.Abs(cosB * cosA));
        return Math.Max(Math.Max(extentY / Fill, extentX / (Fill * 0.9862 * aspect)),
            floorPart * Tan(p) + FloorScale * cosA * hy);
    }

    [Fact(Skip = "#247 diagnostic — run by hand; rasterises the mirror-probe rotY sweep")]
    public void Report()
    {
        _output.WriteLine("rotY   res     fitEx     bracket   rotYfac=ex/bracket   sinB   cosB-sinB   sin(90-2B)/... ");
        foreach (var p in Pages)
        {
            var word = WordCorners(p);
            if (word is null)
            {
                _output.WriteLine($"{p.Ry}: refused");
                continue;
            }

            var f0 = SeedF(p);
            var free = Best([f0, 0, -0.15], v => SumSq(p, v[0], v[1], v[2], word));
            var res = Hausdorff(Hull3(p, free[0], free[1], free[2]), word);
            var fitEx = free[1];
            var a = p.Rx * Math.PI / 180;
            var b = p.Ry * Math.PI / 180;
            var (cosA, cosB, sinB) = (Math.Cos(a), Math.Cos(b), Math.Sin(b));
            var (hx, _, hz) = Box(p);
            var t = Tan(p);
            var aspect = RectWidth / RectHeight;
            var bracket = hx * aspect * cosA * t - hz;
            var rotYfac = fitEx / bracket;
            _output.WriteLine(
                $"{p.Ry,3}  {res:F3}  {fitEx,7:F4}  {bracket,7:F4}   {rotYfac,8:F4}         " +
                $"{sinB:F4}  {cosB - sinB,7:F4}   {Math.Sin((90 - 2 * p.Ry) * Math.PI / 180),7:F4}");
        }
    }

    private static double[] Best(double[] start, Func<double[], double> cost)
    {
        var bc = double.MaxValue;
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
        for (var it = 0; it < 3000; it++)
        {
            var ord = Enumerable.Range(0, n + 1).OrderBy(i => c[i]).ToArray();
            s = ord.Select(i => s[i]).ToList();
            c = ord.Select(i => c[i]).ToList();
            if (c[n] - c[0] < 1e-14) break;
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

    private static List<(double X, double Y)> Hull(List<(double X, double Y)> points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();

        double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var lower = new List<(double X, double Y)>();
        foreach (var pt in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], pt) <= 1e-9) lower.RemoveAt(lower.Count - 1);
            lower.Add(pt);
        }

        var upper = new List<(double X, double Y)>();
        for (var i = sorted.Count - 1; i >= 0; i--)
        {
            var pt = sorted[i];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], pt) <= 1e-9) upper.RemoveAt(upper.Count - 1);
            upper.Add(pt);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }
}