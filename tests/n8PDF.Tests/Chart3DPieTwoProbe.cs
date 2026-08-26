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
/// reproduce, so there is nothing of ours to assert against. What it found, at rotX 25:
/// <list type="bullet">
/// <item>The outline is still widest at data 90° (Word's B=90 boundary lands at screen sinθ 0.997),
/// i.e. Word keeps a near-symmetric affine ellipse for the silhouette.</item>
/// <item>But the sector boundaries are redistributed: at perspective 30 the data→screen angle map
/// is 29→40, 61→64, 90→90, 119→130, 151→169 — the top/back sectors widen, the front-centre ones
/// narrow, non-monotonically. A single perspective divide does not fit it (RMS 0.11), and the raw
/// disc projection had the depth sign backwards, which is why it over-corrected.</item>
/// <item>The map moves with perspective: the same 61° boundary reads screen sinθ 0.959 / 0.898 /
/// 0.835 at perspective 15 / 30 / 60.</item>
/// </list>
/// Pinning the redistribution law wants a denser grid (more boundary angles × perspectives × rotX)
/// on top of what this fixture already renders.
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
            (0, 25, 30, 8), (1, 25, 30, 17), (2, 25, 30, 25), (3, 25, 30, 33), (4, 25, 30, 42),
            (5, 25, 15, 17), (6, 25, 60, 17),
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