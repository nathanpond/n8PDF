using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The corner-on band of the deep 3-D camera, verified through its rotY=45 symmetry (#247) rather
/// than through the corner finder, which refuses a box turned nearly square-on.
/// </summary>
/// <remarks>
/// A square-base box — one category, default depth, so its width and depth half-extents are equal —
/// has a geometric symmetry: turned by 45+d it is the mirror image of turned by 45−d. So the
/// corner-on band (rotY &gt; 45) is the sub-45 band #141 solved, reflected. That is checked here
/// against Word's own raster, with no corners fitted: the red bar's pixels are compared by overlap
/// (intersection over union).
///
/// Two things follow from the symmetry and both hold to better than half a percent — the rest is
/// the anti-aliasing along the box's edges:
/// <list type="bullet">
/// <item>a rotY=45 page is its own mirror about the plot centre (the eye sits on the axis there,
/// ex(45)=0);</item>
/// <item>a rotY=B page is the mirror of its rotY=90−B partner — and markedly not its unmirrored
/// partner, which is what says the two are genuinely reflections and not merely alike.</item>
/// </list>
/// The deep laws bear this out for the eye distance and the vertical offset — <c>hx·sinB + hz·cosB</c>
/// is unchanged by <c>B → 90−B</c> when hx=hz — but not for ex, whose <c>sinB</c> factor does not
/// vanish at 45 as the symmetry demands: ex's turn-dependence past rotY 20 is the piece #141 round 2
/// flagged as unresolved, and this pins that it must be odd about 45.
/// </remarks>
public class Chart3DMirrorTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;
    private const double Scale = 6;

    // The plot rectangle's centre in points (chart at 72,72; plot x 0.2 w 0.6 of a 360-wide chart).
    private const double CentreX = 144 + 108;

    private static bool Reddish((byte R, byte G, byte B) p) => p.R > 120 && p.G < 90 && p.B < 90;

    private HashSet<(int, int)>? RedMask(int rasterPage)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "chart-3d-mirror-probe.pdf");
        if (!File.Exists(path)) return null;
        if (PdfRasterizer.Render(File.ReadAllBytes(path), rasterPage, Scale) is not { } page) return null;

        var mask = new HashSet<(int, int)>();
        var w = page.Pixels.Width;
        for (var py = 0; py < page.Pixels.Height; py++)
        for (var px = 0; px < w; px++)
        {
            var at = (py * w + px) * 3;
            if (Reddish((page.Pixels.Data[at], page.Pixels.Data[at + 1], page.Pixels.Data[at + 2])))
                mask.Add((px, py));
        }

        return mask;
    }

    private static HashSet<(int, int)> Mirror(HashSet<(int, int)> mask)
    {
        var centre = (int)Math.Round(CentreX * Scale);
        return [.. mask.Select(p => (2 * centre - p.Item1, p.Item2))];
    }

    private static double Iou(HashSet<(int, int)> a, HashSet<(int, int)> b)
    {
        var inter = a.Count(b.Contains);
        var union = a.Count + b.Count - inter;
        return union == 0 ? 0 : (double)inter / union;
    }

    /// <summary>A box turned to rotY=45 renders as its own mirror: the eye sits on the axis, ex(45)=0.</summary>
    [Theory]
    [InlineData(0, "15° by 45°, perspective 80")]
    [InlineData(1, "30° by 45°, perspective 80")]
    [InlineData(8, "15° by 45°, perspective 30")]
    public void A_forty_five_degree_turn_renders_symmetric(int page, string what)
    {
        if (RedMask(page) is not { } mask)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var iou = Iou(mask, Mirror(mask));
        _output.WriteLine($"{what}: self-vs-mirror overlap {iou:0.0000}");
        Assert.True(iou > 0.99, $"{what}: a rotY=45 box should be its own mirror, overlap was {iou:0.000}");
    }

    /// <summary>
    /// A box turned to rotY=B renders as the mirror of the one turned to rotY=90−B — and not as its
    /// unmirrored self, which is what makes this a symmetry rather than a coincidence.
    /// </summary>
    [Theory]
    [InlineData(2, 3, "15/40 against 15/50")]
    [InlineData(4, 5, "15/35 against 15/55")]
    [InlineData(6, 7, "15/30 against 15/60")]
    public void A_turn_past_forty_five_mirrors_its_partner_below(int below, int above, string what)
    {
        if (RedMask(below) is not { } low || RedMask(above) is not { } high)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var mirrored = Iou(low, Mirror(high));
        var raw = Iou(low, high);
        _output.WriteLine($"{what}: mirrored overlap {mirrored:0.0000}, unmirrored {raw:0.0000}");

        Assert.True(mirrored > 0.99, $"{what}: the two should be mirror images, overlap was {mirrored:0.000}");
        Assert.True(raw < 0.92,
            $"{what}: put back wrong — without the mirror the overlap is {raw:0.000}, so this is a real " +
            "reflection and not two pages that merely look alike");
    }
}