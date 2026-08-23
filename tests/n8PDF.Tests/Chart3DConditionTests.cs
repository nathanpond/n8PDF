using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Why the floor's convergence is worth about 1.8% and what does not fix it.
/// </summary>
/// <remarks>
/// <see cref="ChartGridLineTests.What_the_convergence_costs_in_slope_error"/> showed the reading
/// multiplies a slope's error some twentyfold, because once the lines concur it reduces exactly to
/// <c>(s1 - s0) / (s4 - s3)</c> — a ratio of differences far smaller than the numbers they are taken
/// between. That is a property of the measure, so no amount of better line fitting removes it, and
/// two ways of changing the **scene** were the candidates for beating it.
///
/// Both were tried and both fail, which is what this file records. The 1.8% stands as the floor.
/// </remarks>
public class Chart3DConditionTests(ITestOutputHelper output)
{
    private const string Deep = "chart-3d-condition-probe";
    private const string Flat = "chart-3d-size-probe";

    private readonly ITestOutputHelper _output = output;

    private static double Bluish((byte R, byte G, byte B) pixel) =>
        Math.Clamp((Math.Min(pixel.B - pixel.R, pixel.B - pixel.G) - 6) / 60.0, 0, 1);

    /// <summary>The floor's lines on a page, the region taken from the ink so size cannot matter.</summary>
    private static IReadOnlyList<GridLines.Line>? Floor(RenderedPage page, double scale, int expect)
    {
        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;

        for (var y = 0.0; y < 780; y += 1 / scale)
        for (var x = 0.0; x < 610; x += 1 / scale)
        {
            if (Bluish(page.At(x, y, scale)) <= 0.02) continue;

            left = Math.Min(left, x);
            right = Math.Max(right, x);
            top = Math.Min(top, y);
            bottom = Math.Max(bottom, y);
        }

        if (right <= left) return null;

        return GridLines.Find(page, scale, Bluish, (left - 2, top - 2, right + 2, bottom + 2),
            (left + right) / 2, expect: expect, concur: true);
    }

    /// <summary>How much the reading multiplies a relative error in one slope.</summary>
    private static double Amplification(IReadOnlyList<GridLines.Line> floor)
    {
        var s = floor.Select(line => line.Slope).ToArray();
        var typical = s.Average();

        return Math.Abs(typical / (s[1] - s[0])) + Math.Abs(typical / (s[^1] - s[^2]));
    }

    private byte[]? Reference(string fixtureName)
    {
        if (TestFonts.SkipForMissingFonts(fixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// How badly the reading is conditioned depends on the depth, and shallow scenes are hopeless.
    /// </summary>
    /// <remarks>
    /// Measured across the gridline probe's depth pages at <c>rotX</c> 25:
    ///
    /// | <c>depthPercent</c> | 20 | 25 | 30 | 35 | 50 | 75 | 100 | 200 |
    /// |---|---|---|---|---|---|---|---|---|
    /// | <c>s / Δs</c> | 356 | 242 | 92 | 82 | 34 | 31 | 27 | 21 |
    ///
    /// The shallow end is the part worth keeping in view. At <c>depthPercent</c> 20 the five lines
    /// are so nearly parallel — slopes 0.1915 to 0.1959 across the whole floor — that a reading off
    /// them multiplies a slope's error by over three hundred.
    ///
    /// And those are precisely the pages that look **best** on a settings sweep: the shallowest two
    /// hold still to 0.10% and 0.15% while depth 100 wanders 1–2%. A sweep moves the rendering and
    /// not the scene, so it reports how repeatable a reading is, and a badly conditioned reading can
    /// be repeated very exactly indeed. That is the #126 distinction with a mechanism attached, and
    /// it is why this is asserted the way round it is: the *reproducible* pages are the *bad* ones.
    /// </remarks>
    [Theory]
    [InlineData(11, 20, 200, 500)]
    [InlineData(7, 25, 150, 350)]
    [InlineData(8, 50, 25, 45)]
    [InlineData(14, 75, 22, 40)]
    [InlineData(3, 100, 20, 36)]
    [InlineData(9, 200, 15, 28)]
    public void The_conditioning_of_the_reading_depends_on_the_depth(
        int page, int depthPercent, double least, double most)
    {
        if (Reference("chart-3d-gridline-probe") is not { } pdf) return;

        if (PdfRasterizer.Render(pdf, page, 8) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // The gridline probe's own region, the one every other measurement on it uses, rather than
        // one taken from the ink: at the shallow depths the lines crowd to six points apart and the
        // two regions do not always find the same five.
        var floor = GridLines.Find(rendered, 8, Bluish, (150, 84, 354, 256), 250, expect: 5, concur: true);

        Assert.Equal(5, floor.Count);

        var amplification = Amplification(floor);
        var slopes = floor.Select(line => line.Slope).ToArray();

        _output.WriteLine($"depthPercent {depthPercent}: slopes {slopes[0]:0.0000}..{slopes[^1]:0.0000}, " +
                          $"a slope's error multiplied by {amplification:0.0}");

        Assert.InRange(amplification, least, most);
    }

    /// <summary>
    /// Adding series does not add gridlines. Word draws fewer of them, not more.
    /// </summary>
    /// <remarks>
    /// This was the first of the two candidates: more lines on the floor would give the fit more to
    /// average over. There is no way to ask for them. A series-axis gridline is nominally one per
    /// series, but Word chooses its own tick interval to keep the spacing looking right, and
    /// <c>CT_SerAx</c> has no <c>majorUnit</c> to overrule it with.
    ///
    /// Nine series produce **three or four** lines rather than nine — fewer than the five series give.
    /// The same thinning is what makes the two smallest frames of <see cref="Chart3DSizeTests"/>
    /// carry four lines instead of five.
    ///
    /// So the lever does not exist, and that is the finding rather than a limitation of this probe.
    /// </remarks>
    [Theory]
    [InlineData(5, 360)]
    [InlineData(6, 420)]
    [InlineData(7, 480)]
    public void Nine_series_do_not_give_nine_gridlines(int page, int frame)
    {
        if (Reference(Deep) is not { } pdf) return;

        var counts = new List<int>();

        foreach (var scale in new[] { 6.0, 8.0, 10.0 })
        {
            if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                _output.WriteLine(PdfRasterizer.UnavailableMessage);
                return;
            }

            // Asked for nine, which is one per series.
            if (Floor(rendered, scale, 9) is { } floor) counts.Add(floor.Count);
        }

        _output.WriteLine($"{frame}pt frame, nine series: Word drew {string.Join(", ", counts)} gridlines");

        Assert.NotEmpty(counts);

        // Nowhere near nine, and fewer than five series get.
        Assert.All(counts, count => Assert.InRange(count, 2, 5));
    }

    /// <summary>
    /// A deeper scene is better conditioned and reads no better, so the second candidate fails too.
    /// </summary>
    /// <remarks>
    /// Depth was the more promising of the two, since the amplification falls from 30 to 21 between
    /// <c>depthPercent</c> 100 and 200. Measured the way #126 measures accuracy — one scene, five
    /// frame sizes, the spread between them — it comes out **worse**:
    ///
    /// | | amplification | spread across identical scenes |
    /// |---|---|---|
    /// | depth 100 | 30 | **1.79%** |
    /// | depth 200 | 21 | 2.92% |
    ///
    /// The conditioning improved and the answer did not, so something else got worse by more than the
    /// conditioning gained: a deeper box tilts its floor lines further from the horizontal and
    /// shortens them, and both cost slope precision. Most of the 2.92% is *within* a page rather than
    /// between pages — the five sizes' own means agree to 1.0% — so it is the fitting that suffered,
    /// not the scene that shifted.
    ///
    /// With both candidates gone, **1.8% is the floor** for a convergence read off five floor
    /// gridlines, and #98 should not chase a residual below it.
    /// </remarks>
    [Fact]
    public void A_deeper_scene_does_not_read_more_accurately()
    {
        if (Reference(Deep) is not { } deep || Reference(Flat) is not { } flat) return;

        (double Spread, double Amplification, int Count) Arm(byte[] pdf, int[] pages)
        {
            var readings = new List<double>();
            var amplifications = new List<double>();

            foreach (var page in pages)
            foreach (var scale in new[] { 6.0, 7.0, 8.0, 9.0, 10.0 })
            {
                if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered) continue;
                if (Floor(rendered, scale, 5) is not { Count: 5 } floor) continue;

                var at = floor.Select(line => line.At).ToArray();
                var gaps = Enumerable.Range(1, 4).Select(i => at[i] - at[i - 1]).ToArray();

                readings.Add(gaps[0] / gaps[^1]);
                amplifications.Add(Amplification(floor));
            }

            return readings.Count < 2
                ? (0, 0, readings.Count)
                : ((readings.Max() - readings.Min()) / readings.Average(),
                   amplifications.Average(), readings.Count);
        }

        var deeper = Arm(deep, [0, 1, 2, 3, 4]);
        var baseline = Arm(flat, [1, 2, 3, 4]);

        if (deeper.Count < 2 || baseline.Count < 2)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        _output.WriteLine($"depth 100: amplification {baseline.Amplification:0.0}, " +
                          $"spread {baseline.Spread * 100:0.00}% over {baseline.Count} readings");
        _output.WriteLine($"depth 200: amplification {deeper.Amplification:0.0}, " +
                          $"spread {deeper.Spread * 100:0.00}% over {deeper.Count} readings");

        // The deeper scene really is the better conditioned of the two.
        Assert.True(deeper.Amplification < baseline.Amplification,
            $"the deeper scene was supposed to be better conditioned: {deeper.Amplification:0.0} " +
            $"against {baseline.Amplification:0.0}");

        // And it does not read any better for it, which is the finding.
        Assert.True(deeper.Spread > 0.8 * baseline.Spread,
            $"depth 200 now reads materially better than depth 100 ({deeper.Spread * 100:0.00}% " +
            $"against {baseline.Spread * 100:0.00}%), so the conclusion recorded here has changed " +
            "and #98's floor should be revisited");

        // Both remain the wrong side of a per cent, which is the number #98 has to live with.
        Assert.InRange(baseline.Spread, 0.01, 0.03);
    }
}
