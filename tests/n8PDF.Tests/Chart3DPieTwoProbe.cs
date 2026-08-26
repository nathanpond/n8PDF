using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The instrument for #166's back-slice sector pinch: a two-slice pie (red then blue, one clean
/// boundary between them, no third colour to bleed) rendered by Word at a sweep of boundary angles
/// and perspectives. Reading where Word puts the single red/blue boundary against its data angle
/// isolates the sector-angle mapping from the outline — which the three-colour probe could not do.
/// </summary>
/// <remarks>
/// Skipped: it measures Word's raster to characterise a mapping the converter does not yet
/// reproduce, so there is nothing of ours to assert against. This is the dense grid — 35 pages,
/// a boundary sweep (data 18–162°) at rotX 25 and perspective 15/30/60, plus rotX 15/40 rows.
/// What it settled:
/// <list type="bullet">
/// <item>The outline stays a near-symmetric affine ellipse (Word's 90° boundary lands at screen
/// sinθ ≈ 1.0 at every perspective), so only the sector angles are redistributed, not the shape.</item>
/// <item>The boundary's screen sinθ over its affine value falls smoothly and monotonically with the
/// boundary angle — about 1.8 at data 18° (the back is pushed out), through 1 near 80°, down to 0.5
/// at 144° (the front is pulled in). But it is not a single perspective divide, nor a Möbius warp
/// <c>sinB/(1+k cosB)</c>, nor a tan-half warp, nor the projected-direction-on-the-affine-ellipse
/// model — every closed form tried leaves RMS ≥ 0.07 of the radius, because the shape of the fall
/// itself changes with perspective (the back's push weakens and the front's pull eases as the
/// perspective deepens), not merely its amplitude.</item>
/// <item>So the redistribution is a genuine two-variable surface with no clean law in reach here;
/// shipping any of the imperfect fits would mis-place sectors much as the raw projection did (which
/// also had the depth sign backwards), so the converter keeps the affine angles and this records
/// the target. A faithful fix wants either a cleaner-than-raster boundary read or a model this grid
/// does not suggest.</item>
/// </list>
/// </remarks>
public class Chart3DPieTwoProbe(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-pie-two-probe";

    private static bool Red((byte R, byte G, byte B) p) => p.R > 60 && p.G < 60 && p.B < 60;
    private static bool Blue((byte R, byte G, byte B) p) => p.B > 60 && p.R < 60 && p.G < 60;

    [Fact(Skip = "instrument for #166 — records Word's sector-boundary mapping")]
    public void Measure()
    {
        const double scale = 6;
        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        // (raster page, rotX, perspective, redPercent -> data boundary B = redPercent·3.6°)
        var pages = new[]
        {
            (0, 25, 15, 5),
            (1, 25, 15, 10),
            (2, 25, 15, 15),
            (3, 25, 15, 20),
            (4, 25, 15, 25),
            (5, 25, 15, 30),
            (6, 25, 15, 35),
            (7, 25, 15, 40),
            (8, 25, 15, 45),
            (9, 25, 30, 5),
            (10, 25, 30, 10),
            (11, 25, 30, 15),
            (12, 25, 30, 20),
            (13, 25, 30, 25),
            (14, 25, 30, 30),
            (15, 25, 30, 35),
            (16, 25, 30, 40),
            (17, 25, 30, 45),
            (18, 25, 60, 5),
            (19, 25, 60, 10),
            (20, 25, 60, 15),
            (21, 25, 60, 20),
            (22, 25, 60, 25),
            (23, 25, 60, 30),
            (24, 25, 60, 35),
            (25, 25, 60, 40),
            (26, 25, 60, 45),
            (27, 15, 30, 10),
            (28, 15, 30, 20),
            (29, 15, 30, 30),
            (30, 15, 30, 40),
            (31, 40, 30, 10),
            (32, 40, 30, 20),
            (33, 40, 30, 30),
            (34, 40, 30, 40),
        };

        output.WriteLine("rotX persp redP |  B°  | Word sinθ | affine sinB | ratio");
        foreach (var (page, rotX, persp, redP) in pages)
        {
            if (PdfRasterizer.Render(File.ReadAllBytes(reference), page, scale) is not { } r)
            {
                output.WriteLine("rasterizer unavailable");
                return;
            }

            var px = r.Pixels;

            bool Is(int x, int y, Func<(byte, byte, byte), bool> f)
            {
                if (x < 0 || y < 0 || x >= px.Width || y >= px.Height) return false;
                var i = (y * px.Width + x) * 3;
                return f((px.Data[i], px.Data[i + 1], px.Data[i + 2]));
            }

            var (bestW, axisY) = (0, 0);
            for (var y = 0; y < px.Height; y++)
            {
                int start = -1, bestL = 0;
                for (var x = 0; x < px.Width; x++)
                {
                    if (Is(x, y, Red) || Is(x, y, Blue))
                    {
                        if (start < 0) start = x;
                        bestL = Math.Max(bestL, x - start + 1);
                    }
                    else start = -1;
                }

                if (bestL > bestW)
                {
                    bestW = bestL;
                    axisY = y;
                }
            }

            int lo = px.Width, hi = -1;
            for (var x = 0; x < px.Width; x++)
                if (Is(x, axisY, Red) || Is(x, axisY, Blue))
                {
                    lo = Math.Min(lo, x);
                    hi = Math.Max(hi, x);
                }

            double cx = (lo + hi) / 2.0, cy = axisY, rx = (hi - lo) / 2.0;

            // The far end of the red/blue boundary line on the right (x>cx): the red pixel touching
            // blue that is furthest from the centre — the rim point at data angle B.
            double bestD = -1, bx = 0;
            for (var y = 0; y < px.Height; y++)
            for (var x = (int)cx; x < px.Width; x++)
            {
                if (!Is(x, y, Red)) continue;
                if (!(Is(x + 1, y, Blue) || Is(x, y + 1, Blue) || Is(x - 1, y, Blue) ||
                      Is(x, y - 1, Blue) || Is(x + 1, y + 1, Blue) || Is(x - 1, y - 1, Blue))) continue;
                var dd = (x - cx) * (x - cx) + (y - cy) * (y - cy);
                if (dd > bestD)
                {
                    bestD = dd;
                    bx = x;
                }
            }

            var wordSin = (bx - cx) / rx;
            var affine = Math.Sin(redP * 3.6 * Math.PI / 180);
            output.WriteLine($"{rotX,4} {persp,5} {redP,5} | {redP * 3.6,4:F0} | {wordSin,9:F3} | " +
                             $"{affine,11:F3} | {(Math.Abs(affine) > 0.01 ? wordSin / affine : 0),5:F3}");
        }
    }
}