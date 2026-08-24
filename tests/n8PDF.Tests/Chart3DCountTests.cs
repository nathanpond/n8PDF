using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What the number of categories and series does to the three-dimensional box itself.
/// </summary>
/// <remarks>
/// **A category is a unit of width and a series is a unit of depth.** The box is as many units wide
/// as there are categories and as many deep as there are series, and the whole of it is then scaled
/// uniformly to fit the plot rectangle, as #108 and #109 established. Nothing about the rectangle
/// changes those proportions.
///
/// Five earlier runs failed to land this, and for one reason: saying what the counts do to the box
/// appears to need a projection, and there is no projection — that is #98, and it is unfinished. So
/// each run fitted a box **and** a projection at once, which is why solving for a depth "drives the
/// search to its bounds".
///
/// Two things get past that. The first is ratios: the silhouette's three edges carry the same fit
/// scale, so <c>across / depth</c> divides it out and reads <c>W / D</c> directly. The second is
/// <c>c:rAngAx</c>. With right-angled axes there is **no perspective**, the projection is affine, and
/// an edge ratio is exactly a scene ratio. With perspective on, the same measurement scatters by
/// several per cent and appears to depend on the plot rectangle — which is what
/// <see cref="Perspective_hides_the_rule"/> shows, because that false dependence is very likely what
/// earlier runs were chasing.
///
/// The probe carries both arms so the rule is found where it is clean and checked where it is not.
/// </remarks>
public class Chart3DCountTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-count-probe";

    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    private static double Length((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>The box's three edge lengths on a page: across, in depth, and upright.</summary>
    /// <remarks>
    /// The lowest corner of the hexagon is the one nearest the reader and the two edges meeting there
    /// are the across one, running left, and the depth one, running right. The uprights are the two
    /// edges the projection leaves exactly vertical — with right-angled axes a vertical of the scene
    /// stays a vertical of the page, so they are found by having no run at all rather than by being
    /// steeper than some threshold.
    /// </remarks>
    private (double Across, double Depth, double Upright)? Box(byte[] pdf, int page)
    {
        const double scale = 6;

        if (PdfRasterizer.Render(pdf, page, scale) is not { } rendered) return null;

        var shape = BoxSilhouette.Find(rendered, scale, Reddish, (73, 73, 431, 287));

        if (!shape.Found)
        {
            _output.WriteLine($"page {page}: {shape.Refused}");
            return null;
        }

        var points = shape.Points;

        var low = 0;
        for (var i = 1; i < points.Count; i++)
            if (points[i].Y > points[low].Y) low = i;

        var one = points[(low - 1 + points.Count) % points.Count];
        var other = points[(low + 1) % points.Count];
        var (left, right) = one.X < other.X ? (one, other) : (other, one);

        var upright = 0.0;

        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];

            if (Math.Abs(a.X - b.X) < 0.5 && Math.Abs(a.Y - b.Y) > 2)
                upright = Math.Max(upright, Length(a, b));
        }

        return (Length(points[low], left), Length(points[low], right), upright);
    }

    private byte[]? Reference()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var path = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(path), $"No Word reference PDF at {path}");

        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// A category is a unit of width: the box is as many units wide as there are categories.
    /// </summary>
    /// <remarks>
    /// Read as <c>across / depth</c>, which divides out the fit. The depth is held at one series
    /// throughout, so the ratio should go exactly as the number of categories, and it does:
    ///
    /// | categories | 1 | 2 | 3 | 4 | 6 |
    /// |---|---|---|---|---|---|
    /// | `across / depth` | 2.333 | 4.646 | 6.995 | 9.229 | 14.024 |
    /// | against the first | 1.000 | **1.991** | **2.998** | **3.956** | **6.011** |
    ///
    /// Within about one per cent at every count, on the right-angled-axes arm where the projection is
    /// affine and the ratio is exact.
    /// </remarks>
    [Theory]
    [InlineData(14, 2)]
    [InlineData(15, 3)]
    [InlineData(16, 4)]
    [InlineData(17, 6)]
    public void The_box_is_a_unit_wide_for_every_category(int page, int categories)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 13) is not { } one || Box(pdf, page) is not { } many)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var measured = many.Across / many.Depth / (one.Across / one.Depth);

        _output.WriteLine($"{categories} categories: the box is {measured:0.000} times as wide " +
                          $"for its depth as at one");

        Assert.InRange(measured / categories, 0.98, 1.02);
    }

    /// <summary>
    /// A series is a unit of depth: the box is as many units deep as there are series.
    /// </summary>
    /// <remarks>
    /// The same measurement with the sweeps exchanged, so <c>across / depth</c> falls as the count
    /// rises:
    ///
    /// | series | 1 | 2 | 3 | 4 | 6 |
    /// |---|---|---|---|---|---|
    /// | first over this | 1.000 | **1.991** | **2.995** | **3.995** | **6.076** |
    ///
    /// <c>standard</c> grouping throughout — it is what puts series in depth at all. Under
    /// <c>clustered</c> they stand side by side across instead, and an earlier run of #114 measured a
    /// third of the width believing it was measuring depth.
    /// </remarks>
    [Theory]
    [InlineData(18, 2)]
    [InlineData(19, 3)]
    [InlineData(20, 4)]
    [InlineData(21, 6)]
    public void The_box_is_a_unit_deep_for_every_series(int page, int series)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 13) is not { } one || Box(pdf, page) is not { } many)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var measured = one.Across / one.Depth / (many.Across / many.Depth);

        _output.WriteLine($"{series} series: the box is {measured:0.000} times as deep " +
                          $"for its width as at one");

        Assert.InRange(measured / series, 0.98, 1.02);
    }

    /// <summary>
    /// The two act independently, which the held-back pages say and were not used to decide.
    /// </summary>
    /// <remarks>
    /// Two categories with three series, and three with two. Neither was used in arriving at the
    /// rule. If a category is a unit of width and a series a unit of depth then
    /// <c>across / depth</c> must go as <c>categories / series</c> whatever the pair, and it does —
    /// 1.561 against a predicted 1.555, and 3.498 against 3.500.
    /// </remarks>
    [Theory]
    [InlineData(22, 2, 3)]
    [InlineData(23, 3, 2)]
    public void Categories_and_series_compose(int page, int categories, int series)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 13) is not { } one || Box(pdf, page) is not { } both)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var measured = both.Across / both.Depth;
        var predicted = one.Across / one.Depth * categories / series;

        _output.WriteLine($"{categories} categories by {series} series: {measured:0.000}, " +
                          $"the rule predicts {predicted:0.000}");

        Assert.InRange(measured / predicted, 0.98, 1.02);
    }

    /// <summary>
    /// The plot rectangle does not change the box's proportions, only its size.
    /// </summary>
    /// <remarks>
    /// The re-check #116's criteria ask for, at a count other than one. A rectangle a third wider
    /// leaves <c>across / depth</c> at 7.007 against 6.995 — a sixth of a per cent — so #108's
    /// uniform fit carries over to three categories unchanged, and what the rectangle binds is the
    /// scale rather than the shape.
    /// </remarks>
    [Fact]
    public void The_plot_rectangle_changes_the_size_and_not_the_shape()
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 15) is not { } ordinary || Box(pdf, 24) is not { } wide)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var a = ordinary.Across / ordinary.Depth;
        var b = wide.Across / wide.Depth;

        _output.WriteLine($"three categories: {a:0.000} in the ordinary rectangle, {b:0.000} in one " +
                          $"a third wider — and the box is {wide.Across / ordinary.Across:0.000} times as big");

        // The shape holds.
        Assert.InRange(b / a, 0.99, 1.01);

        // The size does not, or the two pages would not be telling us anything.
        Assert.True(wide.Across / ordinary.Across > 1.2,
            "the wider rectangle drew the same size of box, so it is not the page it claims to be");
    }

    /// <summary>
    /// With perspective on, the same measurement scatters and appears to depend on the rectangle.
    /// </summary>
    /// <remarks>
    /// Why the right-angled arm exists, and worth keeping because it is the likeliest thing five
    /// earlier runs were chasing.
    ///
    /// <c>across / depth</c> is a scene ratio only when the projection is affine. Under perspective a
    /// projected edge length depends on how far that edge lies from the reader, so the ratio picks up
    /// the box's own size and position. The rule is still visible through it — the counts still move
    /// it roughly the right way — but it misses by several per cent, and the wider plot rectangle
    /// shifts it by seven to nine per cent, which looks exactly like the rectangle changing the box
    /// and is not.
    /// </remarks>
    [Fact]
    public void Perspective_hides_the_rule()
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 0) is not { } one || Box(pdf, 2) is not { } three ||
            Box(pdf, 11) is not { } threeWide)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var measured = three.Across / three.Depth / (one.Across / one.Depth);
        var byRectangle = (threeWide.Across / threeWide.Depth) / (three.Across / three.Depth);

        _output.WriteLine($"with perspective, three categories reads {measured:0.000} where the rule " +
                          $"says 3.000, and the wider rectangle moves it by {(byRectangle - 1) * 100:0.0}%");

        // Off by more than the affine arm's one per cent...
        Assert.True(Math.Abs(measured - 3) > 0.03,
            $"the perspective arm now reads {measured:0.000}, close enough to 3 that the affine arm " +
            "is no longer earning its place — worth understanding before removing it");

        // ...and the rectangle appears to matter, where affinely it does not.
        Assert.True(Math.Abs(byRectangle - 1) > 0.02,
            "the plot rectangle no longer appears to move the perspective reading, so the trap this " +
            "test records has gone");
    }

    /// <summary>
    /// The box's height does **not** follow the uniform fit, and this is not yet explained.
    /// </summary>
    /// <remarks>
    /// Recorded rather than solved, because it is reproducible and it bears on #109.
    ///
    /// The fit is uniform and can be shown so: taking the scale from <c>across / categories</c> and
    /// from the depth edge separately gives 1, 0.632, 0.442, 0.340, 0.233 and 1, 0.634, 0.442, 0.344,
    /// 0.232 across the category sweep — the same numbers, which is what a single scale means.
    ///
    /// The upright edge does not follow it. Divided by that scale it comes out as a multiplier of
    /// **1, 1, 2, 2, 3** at counts of 1, 2, 3, 4 and 6, and the same 1, 1, 2, 2, 3 appears again in
    /// the series sweep. So the box is taller at three categories than at two, in a picture where
    /// everything else got smaller.
    ///
    /// What that costs is a rule of #109's that has to be re-opened: the box's height over its width
    /// was measured at one category, and at one category it is the plot rectangle's aspect exactly —
    /// 0.545 measured against 0.55 stated, once the bar's 60 of 100 is divided out. It is not that at
    /// any other count.
    ///
    /// No rule is claimed here. `ceil(n/2)` fits all ten pages and is too strange a function to
    /// believe on ten pages, and the counts tried skip 5, 7 and 8 — exactly where it would be
    /// falsified. Filed rather than guessed at. What **is** asserted is the fact: the height departs
    /// from the uniform fit by a factor of two or more, so nothing downstream should assume it
    /// follows.
    /// </remarks>
    [Fact]
    public void The_height_does_not_follow_the_uniform_fit()
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 13) is not { } one || Box(pdf, 14) is not { } two || Box(pdf, 15) is not { } three)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        // The scale, read off the depth edge, which holds one series throughout.
        var toTwo = two.Depth / one.Depth;
        var toThree = three.Depth / one.Depth;

        _output.WriteLine($"scale from the depth edge: {toTwo:0.000} at two categories, {toThree:0.000} at three");
        _output.WriteLine($"upright edge: {one.Upright:0.00}, {two.Upright:0.00}, {three.Upright:0.00}");
        _output.WriteLine($"against a uniform fit: {two.Upright / (one.Upright * toTwo):0.000} and " +
                          $"{three.Upright / (one.Upright * toThree):0.000}");

        // Two categories does follow the fit.
        Assert.InRange(two.Upright / (one.Upright * toTwo), 0.97, 1.03);

        // Three does not, and by a factor rather than a margin.
        Assert.True(three.Upright / (one.Upright * toThree) > 1.5,
            "the height now follows the uniform fit at three categories, which would settle the " +
            "anomaly this test records rather than break the test — check it before deleting this");
    }
}
