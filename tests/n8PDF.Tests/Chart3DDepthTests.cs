using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What <c>c:depthPercent</c> does to the box, and what it pointedly does not do.
/// </summary>
/// <remarks>
/// #116 established that a series is a unit of depth, measured throughout at <c>depthPercent</c> 100.
/// Two things follow that had to be measured rather than assumed, and #98 stopped on them rather than
/// build a projection over them — an unmeasured depth would have been absorbed by the viewing
/// distance, where nothing would ever have shown it.
///
/// **The depth is `series × depthPercent/100` units.** The obvious reading, and it holds across a
/// twenty-five-fold sweep to better than a per cent.
///
/// **The height is not.** It is `floor((categories + series)/2)` units at every `depthPercent`, so
/// the rule counts **series** and not units of depth. That mattered: at `depthPercent` 50 a single
/// series is half a unit deep, and a height rule reading units would give `floor((1 + 0.5)/2) = 0`.
/// It gives one, as it does everywhere else.
/// </remarks>
public class Chart3DDepthTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-depth-probe";

    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    private static double Length((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The box's three edges, the height read twice so a misreading shows itself.</summary>
    private (double Across, double Depth, double Upright, double Otherwise)? Box(byte[] pdf, int page)
    {
        const double scale = 6;

        if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered) return null;

        var shape = BoxSilhouette.Find(rendered, scale, Reddish, (73, 73, 431, 287));

        if (!shape.Found) return null;

        var points = shape.Points;

        var low = 0;
        for (var i = 1; i < points.Count; i++)
            if (points[i].Y > points[low].Y) low = i;

        var one = points[(low - 1 + points.Count) % points.Count];
        var other = points[(low + 1) % points.Count];
        var (across, depth) = Math.Abs(one.Y - points[low].Y) < 0.5 ? (one, other) : (other, one);

        var upright = 0.0;

        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];

            if (Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) > 2)
                upright = Math.Max(upright, Length(a, b));
        }

        var span = points.Max(p => p.Y) - points.Min(p => p.Y);

        return (Length(points[low], across), Length(points[low], depth),
                upright, span - Math.Abs(depth.Y - points[low].Y));
    }

    private byte[]? Reference()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// The box is <c>series × depthPercent/100</c> units deep.
    /// </summary>
    /// <remarks>
    /// Read as <c>across / depth</c>, which divides out the fit. The width is one unit throughout, so
    /// the ratio should go as <c>100 / depthPercent</c> — and over a sweep from 20 to 500 it does:
    ///
    /// | `depthPercent` | 20 | 50 | 100 | 150 | 200 | 300 | 500 |
    /// |---|---|---|---|---|---|---|---|
    /// | `across / depth` | 11.578 | 4.701 | 2.333 | 1.561 | 1.171 | 0.776 | 0.466 |
    /// | `100/depthPercent`, scaled | 11.665 | 4.666 | 2.333 | 1.555 | 1.167 | 0.778 | 0.467 |
    ///
    /// Within three quarters of a per cent everywhere.
    /// </remarks>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 50)]
    [InlineData(3, 150)]
    [InlineData(4, 200)]
    [InlineData(5, 300)]
    [InlineData(6, 500)]
    public void The_depth_is_the_series_count_times_the_stated_percentage(int page, int depthPercent)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 2) is not { } hundred || Box(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var ratio = measured.Across / measured.Depth;
        var predicted = hundred.Across / hundred.Depth * 100 / depthPercent;

        _output.WriteLine($"depthPercent {depthPercent}: across over depth is {ratio:0.000}, " +
                          $"and one unit of width over {depthPercent / 100.0:0.00} of depth is {predicted:0.000}");

        Assert.InRange(ratio / predicted, 0.99, 1.01);
    }

    /// <summary>
    /// It multiplies the series count rather than replacing it.
    /// </summary>
    /// <remarks>
    /// The pages where both move at once, two of them **held back** and used for nothing in arriving
    /// at the rule above. Three series at half depth is a box one and a half units deep, and three
    /// categories at double depth is three units wide by two deep — each predicted from the rule and
    /// each landing on it.
    /// </remarks>
    [Theory]
    [InlineData(7, 1, 3, 50, "three series at half depth")]
    [InlineData(9, 3, 1, 200, "held back: three categories at double depth")]
    [InlineData(10, 2, 2, 50, "held back: two by two at half depth")]
    public void The_percentage_multiplies_the_series_count(
        int page, int categories, int series, int depthPercent, string what)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 2) is not { } one || Box(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var ratio = measured.Across / measured.Depth;
        var predicted = one.Across / one.Depth * categories / (series * depthPercent / 100.0);

        _output.WriteLine($"{what}: across over depth is {ratio:0.000}, the rule predicts {predicted:0.000}");

        Assert.InRange(ratio / predicted, 0.99, 1.01);
    }

    /// <summary>
    /// The height counts series, and takes no notice of <c>depthPercent</c> at all.
    /// </summary>
    /// <remarks>
    /// The question this probe was really built for. `floor((categories + series)/2)` was measured
    /// entirely at `depthPercent` 100, where a series is exactly one unit deep — so whether the rule
    /// counts **series** or **units of depth** was undetermined, and the two part company the moment
    /// the percentage is anything else.
    ///
    /// They part company badly. At `depthPercent` 50 a single series is half a unit deep, so a rule
    /// reading units would give `floor((1 + 0.5)/2)`, which is **nought**.
    ///
    /// Measured, the height is **one unit at every `depthPercent` from 20 to 500** — 0.998, 1.011,
    /// 1.000, 1.007, 1.002, 1.003, 1.003 — so it counts series and the box does not flatten as it
    /// deepens.
    /// </remarks>
    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 50)]
    [InlineData(2, 100)]
    [InlineData(3, 150)]
    [InlineData(4, 200)]
    [InlineData(5, 300)]
    [InlineData(6, 500)]
    public void The_height_takes_no_notice_of_the_depth_percentage(int page, int depthPercent)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 2) is not { } hundred || Box(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        Assert.InRange(measured.Upright - measured.Otherwise, -0.4, 0.4);

        // Height over depth times the depth in units is the height in units, normalised so that
        // depthPercent 100 reads one.
        var units = measured.Upright / measured.Depth * (depthPercent / 100.0)
                    / (hundred.Upright / hundred.Depth);

        _output.WriteLine($"depthPercent {depthPercent}: the box is {units:0.000} units tall");

        Assert.InRange(units, 0.97, 1.03);
    }

    /// <summary>
    /// And with the counts raised, the height is still <c>floor((c + s)/2)</c> whatever the depth.
    /// </summary>
    /// <remarks>
    /// The two rules together, on the pages where both the counts and the percentage differ from the
    /// pages either was measured on. Three series at half depth gives two units, and so does three
    /// categories at double depth, and so does two by two at half — <c>floor(4/2)</c> every time,
    /// with the percentage playing no part.
    /// </remarks>
    [Theory]
    [InlineData(7, 1, 3, 50, 2)]
    [InlineData(9, 3, 1, 200, 2)]
    [InlineData(10, 2, 2, 50, 2)]
    public void The_height_is_still_half_the_counts_together(
        int page, int categories, int series, int depthPercent, int units)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 2) is not { } one || Box(pdf, page) is not { } measured)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        Assert.InRange(measured.Upright - measured.Otherwise, -0.4, 0.4);

        var deep = series * depthPercent / 100.0;
        var tall = measured.Upright / measured.Depth * deep / (one.Upright / one.Depth);

        _output.WriteLine($"{categories} by {series} at depthPercent {depthPercent}: " +
                          $"{tall:0.000} units tall, floor((c + s)/2) is {units}");

        Assert.InRange(tall / units, 0.97, 1.03);
    }

    /// <summary>
    /// A page the instrument cannot read says so, rather than answering.
    /// </summary>
    /// <remarks>
    /// Three series at double depth is a box six units deep against one across, seen so nearly
    /// edge-on that its outline no longer reduces to a box's. Its two readings of the height come
    /// back 29.26 and 68.74 — not a margin apart but a factor — so the page is refused.
    ///
    /// Kept as a test because a single reading would have returned 29.26 with nothing to mark it, and
    /// the depth rule would then have looked wrong by a factor of four on exactly one page, which is
    /// the sort of thing that gets a correct rule abandoned.
    /// </remarks>
    [Fact]
    public void A_box_seen_too_nearly_edge_on_is_refused()
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 8) is not { } edgeOn)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        _output.WriteLine($"three series at double depth: the height reads {edgeOn.Upright:0.00} " +
                          $"one way and {edgeOn.Otherwise:0.00} the other");

        Assert.True(Math.Abs(edgeOn.Upright - edgeOn.Otherwise) > 1,
            "this page now reads consistently, so it could join the sweep rather than be excluded " +
            "from it — worth checking why before deleting this test");
    }
}
