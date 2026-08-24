using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// How tall the three-dimensional box is, which is not what the uniform fit would make it.
/// </summary>
/// <remarks>
/// #116 settled that a category is a unit of width and a series a unit of depth, and that the box is
/// then scaled uniformly to fit. Two of the three dimensions follow that scale. The third does not.
///
/// **The box is `ceil(n/2)` units tall**, where `n` is whichever count is being varied — so it grows
/// by a whole unit at every *odd* count and stands still at every even one. Measured across all eight
/// counts of the series sweep, with the scale divided out:
///
/// | series | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 |
/// |---|---|---|---|---|---|---|---|---|
/// | height, against one series | 1.000 | 1.004 | 2.005 | 2.013 | **3.021** | 3.028 | **4.018** | 4.015 |
/// | `ceil(n/2)` | 1 | 1 | 2 | 2 | 3 | 3 | 4 | 4 |
///
/// Counts **5 and 7 were held back** and used for nothing until the rule was already written down,
/// because they are where the plausible alternatives part company: they are the counts that separate
/// `ceil(n/2)` from anything smooth.
///
/// **What this is not.** It is not the value axis. The bar measured is 60 in an axis stated 0..100,
/// so a height that moved with the count could as easily have been a maximum that moved — and
/// <see cref="The_value_axis_is_not_what_moves"/> rules that out: the bar's height goes as the value
/// while the footprint does not stir.
///
/// **What is not claimed.** Why. `ceil(n/2)` is a strange thing for a chart to do to a box, and
/// nothing here explains it; what is recorded is that it is what Word does, at eight counts, in two
/// sweeps, with the axis excluded and the height read two independent ways. Whoever implements the
/// projection should reproduce it and not tidy it.
/// </remarks>
public class Chart3DBoxHeightTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-height-count-probe";

    private readonly ITestOutputHelper _output = output;

    private static bool Reddish((byte R, byte G, byte B) pixel) =>
        pixel.R > 120 && pixel.G < 90 && pixel.B < 90;

    private static double Length((double X, double Y) a, (double X, double Y) b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// The box's edges on a page, with its height read two ways so a bad reading shows itself.
    /// </summary>
    /// <remarks>
    /// The upright edges are the ones the projection leaves exactly vertical. The second reading is
    /// the silhouette's whole vertical extent less the rise of its depth edge, which is the same
    /// quantity arrived at without touching the same hull edge. They agree to a fifth of a point
    /// everywhere but one page, and that page is a misreading rather than a finding — which is the
    /// point of taking it twice.
    /// </remarks>
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
    /// The box is <c>ceil(n/2)</c> units tall: it grows at every odd count and stands still at evens.
    /// </summary>
    /// <remarks>
    /// Read off the series sweep, where the depth is <c>n</c> units and carries the same fit scale as
    /// the height, so <c>upright / depth</c> divides the scale out and <c>× n</c> recovers the height
    /// in units. Nothing about the projection is solved for.
    ///
    /// Five and seven are the held-back counts. They are also the two that make the rule worth
    /// stating rather than guessing — at 1, 2, 3, 4, 6 alone, which is what #116 had, several
    /// functions agree.
    /// </remarks>
    [Theory]
    [InlineData(8, 1, 1, false)]
    [InlineData(9, 2, 1, false)]
    [InlineData(10, 3, 2, false)]
    [InlineData(11, 4, 2, false)]
    [InlineData(12, 5, 3, true)]
    [InlineData(13, 6, 3, false)]
    [InlineData(14, 7, 4, true)]
    [InlineData(15, 8, 4, false)]
    public void The_box_is_half_the_count_rounded_up_in_units_tall(
        int page, int series, int units, bool heldBack)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 8) is not { } one || Box(pdf, page) is not { } many)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // The two readings of the height must agree, or the page is not being read properly.
        Assert.InRange(many.Upright - many.Otherwise, -0.4, 0.4);

        // Depth is n units and shares the scale, so this is the height in units.
        var measured = many.Upright / many.Depth * series / (one.Upright / one.Depth);

        _output.WriteLine($"{series} series{(heldBack ? " (held back)" : "")}: the box is " +
                          $"{measured:0.000} units tall, and ceil(n/2) is {units}");

        Assert.InRange(measured / units, 0.97, 1.03);
    }

    /// <summary>
    /// The same in the category sweep, where the width is what carries the count.
    /// </summary>
    /// <remarks>
    /// A second sweep rather than a second reading of the first: here the depth is held at one unit
    /// and the width grows, so the scale is taken from the depth edge instead. It gives the same
    /// 1, 1, 2, 2, 3, 4, 4.
    ///
    /// Five categories is missing on purpose. Its two height readings disagree — 38.83 against 10.17 —
    /// so the page is refused rather than reported, and <see cref="A_misread_page_shows_itself"/>
    /// pins that. A count that cannot be read is not a count that agrees.
    /// </remarks>
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 4, 2)]
    [InlineData(5, 6, 3)]
    [InlineData(6, 7, 4)]
    [InlineData(7, 8, 4)]
    public void The_categories_say_the_same(int page, int categories, int units)
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 0) is not { } one || Box(pdf, page) is not { } many)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        Assert.InRange(many.Upright - many.Otherwise, -0.4, 0.4);

        // Depth is one unit throughout here, so it is the scale directly.
        var measured = many.Upright / many.Depth / (one.Upright / one.Depth);

        _output.WriteLine($"{categories} categories: the box is {measured:0.000} units tall, " +
                          $"and ceil(n/2) is {units}");

        Assert.InRange(measured / units, 0.96, 1.04);
    }

    /// <summary>
    /// It is the box that moves, not the value axis.
    /// </summary>
    /// <remarks>
    /// The alternative worth killing, because it would have accounted for everything above without
    /// the box changing at all: the upright measured is a bar of 60 in an axis **stated** 0..100, so
    /// if Word were choosing its own maximum the bar would be a different fraction of an unchanged
    /// box.
    ///
    /// It is not. At three categories the bar's height goes 13.51, 27.41, 41.32, 55.23 for values of
    /// 20, 40, 60 and 80 — proportional to the value, so the maximum is the stated 100 throughout —
    /// while the footprint does not stir: `across` is 189.26 and `depth` 27.06 on every one of them.
    /// </remarks>
    [Fact]
    public void The_value_axis_is_not_what_moves()
    {
        if (Reference() is not { } pdf) return;

        var heights = new List<(int Value, double Height)>();
        var footprints = new List<(double Across, double Depth)>();

        foreach (var (page, value) in new[] { (16, 20), (17, 40), (2, 60), (18, 80) })
        {
            if (Box(pdf, page) is not { } box)
            {
                Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
                return;
            }

            heights.Add((value, box.Upright));
            footprints.Add((box.Across, box.Depth));
        }

        _output.WriteLine("three categories at values 20, 40, 60, 80: heights " +
                          string.Join(", ", heights.Select(h => h.Height.ToString("0.00"))));
        _output.WriteLine("footprints: " +
                          string.Join(", ", footprints.Select(f => $"{f.Across:0.00}x{f.Depth:0.00}")));

        // The bar's height goes as the value, so the axis maximum is the stated one.
        foreach (var (value, height) in heights)
            Assert.InRange(height / value / (heights[^1].Height / heights[^1].Value), 0.95, 1.05);

        // And the footprint is untouched by the value, so nothing else moved with it.
        Assert.InRange(footprints.Max(f => f.Across) - footprints.Min(f => f.Across), 0, 0.5);
        Assert.InRange(footprints.Max(f => f.Depth) - footprints.Min(f => f.Depth), 0, 0.5);
    }

    /// <summary>
    /// A page whose height cannot be read says so, rather than answering wrongly.
    /// </summary>
    /// <remarks>
    /// Five categories is the one page in the probe where the two readings of the height disagree,
    /// and by a factor rather than a margin — 38.83 against 10.17. The hull edge taken for an upright
    /// is not one, on a box flat enough that the reduction to six corners put its vertices elsewhere.
    ///
    /// Recorded because a single reading would have reported 38.83 with no sign that anything was
    /// wrong, and 38.83 is not far from a plausible answer. Taking the height twice is what turns a
    /// wrong number into a refusal.
    /// </remarks>
    [Fact]
    public void A_misread_page_shows_itself()
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 4) is not { } five)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        _output.WriteLine($"five categories: upright reads {five.Upright:0.00}, " +
                          $"the other way reads {five.Otherwise:0.00}");

        Assert.True(Math.Abs(five.Upright - five.Otherwise) > 1,
            "the five-category page now reads consistently, which would be worth understanding — the " +
            "count could then be added to the sweep above rather than left out of it");
    }

    /// <summary>
    /// #109's height over width holds at one category and at no other.
    /// </summary>
    /// <remarks>
    /// The re-check that #116's criteria asked for and that this issue exists to finish. #109
    /// established that the box's height over its width is the plot rectangle's aspect — measured, as
    /// everything in that story was, at **one category**.
    ///
    /// At one category it is right: 0.564 against the 0.55 the rectangle states. At two it is 0.282,
    /// which is half. The rule as written holds only where it was measured, and the general form is
    /// the one above — <c>H = aspect × ceil(n/2)</c> units, against a width of <c>n</c> units, so the
    /// two agree only when <c>ceil(n/2) = n</c>, which is to say at <c>n = 1</c>.
    /// </remarks>
    [Fact]
    public void The_height_over_width_rule_holds_only_at_one_category()
    {
        if (Reference() is not { } pdf) return;

        if (Box(pdf, 0) is not { } one || Box(pdf, 1) is not { } two)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        // The bar is 60 of 100, and a vertical foreshortens by cos(rotX) at 15 degrees.
        const double toBox = 0.6 * 0.9659;

        var atOne = one.Upright / one.Across / toBox;
        var atTwo = two.Upright / two.Across / toBox;

        _output.WriteLine($"height over width: {atOne:0.000} at one category, {atTwo:0.000} at two, " +
                          $"against the rectangle's 0.55");

        // #109's rule, where #109 measured it.
        Assert.InRange(atOne, 0.52, 0.60);

        // And gone by two, at half.
        Assert.InRange(atTwo / atOne, 0.45, 0.55);
    }
}
