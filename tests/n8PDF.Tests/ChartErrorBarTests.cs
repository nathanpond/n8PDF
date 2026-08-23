using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests how far an error bar reaches.
/// </summary>
/// <remarks>
/// The arithmetic half, like <see cref="ChartTrendlineTests"/> beside it: a standard deviation is
/// determined by the numbers rather than by Word, so the figures below are worked out
/// independently rather than read back from a run of this code.
///
/// The one thing here that is *not* arithmetic is which standard deviation — over the whole series
/// or as a sample of something larger. The format does not say. That was measured, and the test
/// that settles it is in <see cref="ChartErrorBarInkTests"/>, where Word's own drawing decides.
/// </remarks>
public class ChartErrorBarTests
{
    /// <summary>The probe's values, whose two deviations are far enough apart to tell apart.</summary>
    private static readonly double[] Sample = [30, 45, 20, 55];

    private static ChartErrorBars Bars(
        ChartErrorAmount amount, double value = 10,
        ChartErrorSides sides = ChartErrorSides.Both,
        IReadOnlyList<double?>? plus = null, IReadOnlyList<double?>? minus = null) =>
        new(ChartErrorDirection.Value, sides, amount, value, plus ?? [], minus ?? [], true, null, 1);

    private static void Close(double expected, double actual) =>
        Assert.True(
            Math.Abs(expected - actual) <= 1e-9 * Math.Max(1, Math.Abs(expected)),
            $"expected {expected:R}, got {actual:R}");

    /// <summary>
    /// The deviation of the probe's values, worked by hand, and taken as a sample.
    /// </summary>
    /// <remarks>
    /// Mean of 30, 45, 20, 55 is 37.5. The squared deviations are 56.25, 56.25, 306.25 and 306.25,
    /// summing to 725. Over three that is 241.667, whose root is 15.5456; over four it would be
    /// 181.25, whose root is 13.4629 — 15% apart, which is what made the two tellable.
    ///
    /// Word draws 15.55, measured off the reference PDF's path geometry. So it is the sample one,
    /// and the assertion below that it is *not* the population one is the whole point of this
    /// test: both are defensible readings of a format that does not say, and this is the evidence.
    /// </remarks>
    [Fact]
    public void The_deviation_is_of_a_sample_rather_than_of_the_whole_series()
    {
        Close(Math.Sqrt(241.66666666666666), ChartErrorAmounts.Deviation(Sample));

        // And it is not the population one, which is the alternative reading Word rules out.
        Assert.NotEqual(Math.Sqrt(181.25), ChartErrorAmounts.Deviation(Sample), 6);
    }

    /// <summary>The standard error is that deviation over the root of the count.</summary>
    [Fact]
    public void The_standard_error_is_the_deviation_over_the_root_of_the_count()
    {
        Close(Math.Sqrt(241.66666666666666) / 2, ChartErrorAmounts.Error(Sample));
    }

    /// <summary>A fixed amount is the same distance at every point, both ways.</summary>
    [Fact]
    public void A_fixed_amount_reaches_the_same_distance_everywhere()
    {
        var reach = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.Fixed), Sample);

        Assert.Equal(4, reach.Count);
        Assert.All(reach, r => Assert.Equal((10.0, 10.0), r));
    }

    /// <summary>
    /// A percentage follows the point, so a bar grows with the number it belongs to.
    /// </summary>
    /// <remarks>
    /// Twenty percent of 30, 45, 20 and 55 is 6, 9, 4 and 11 — which is the whole of the rule, and
    /// the reason a percentage bar is not the same length twice.
    /// </remarks>
    [Fact]
    public void A_percentage_follows_the_point_it_belongs_to()
    {
        var reach = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.Percentage, 20), Sample);

        Assert.Equal([(6.0, 6.0), (9.0, 9.0), (4.0, 4.0), (11.0, 11.0)], reach);
    }

    /// <summary>A spread is one distance for the whole series, not one per point.</summary>
    [Theory]
    [InlineData("stdDev")]
    [InlineData("stdErr")]
    public void A_spread_is_one_distance_for_the_whole_series(string which)
    {
        var amount = which == "stdDev"
            ? ChartErrorAmount.StandardDeviation
            : ChartErrorAmount.StandardError;

        var reach = ChartErrorAmounts.Reach(Bars(amount, 1), Sample);

        Assert.Equal(4, reach.Count);
        Assert.All(reach, r => Close(reach[0].Up, r.Up));
    }

    /// <summary>
    /// A multiple applies to a deviation, and nothing applies to a standard error.
    /// </summary>
    /// <remarks>
    /// The format lets the element carry a value in both cases. For a deviation it is the multiple
    /// — two of them is twice as far. For a standard error there is nowhere for it to go, and Word
    /// ignores it; asserting that keeps someone from "fixing" it into a multiplier later.
    /// </remarks>
    [Fact]
    public void A_multiple_counts_for_a_deviation_and_not_for_a_standard_error()
    {
        var one = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.StandardDeviation, 1), Sample);
        var two = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.StandardDeviation, 2), Sample);

        Close(one[0].Up * 2, two[0].Up);

        var plain = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.StandardError, 1), Sample);
        var multiplied = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.StandardError, 5), Sample);

        Close(plain[0].Up, multiplied[0].Up);
    }

    /// <summary>Stated distances are read per point, and each side separately.</summary>
    [Fact]
    public void Stated_distances_are_read_for_each_side_of_each_point()
    {
        var reach = ChartErrorAmounts.Reach(
            Bars(ChartErrorAmount.Custom, plus: [5, 10, 15, 20], minus: [20, 15, 10, 5]), Sample);

        Assert.Equal([(5.0, 20.0), (10.0, 15.0), (15.0, 10.0), (20.0, 5.0)], reach);
    }

    /// <summary>A bar reaching one way has no length the other.</summary>
    [Theory]
    [InlineData("plus")]
    [InlineData("minus")]
    public void A_bar_reaching_one_way_has_no_length_the_other(string side)
    {
        var sides = side == "plus" ? ChartErrorSides.Plus : ChartErrorSides.Minus;
        var reach = ChartErrorAmounts.Reach(Bars(ChartErrorAmount.Fixed, 10, sides), Sample);

        Assert.All(reach, r => Assert.Equal(side == "plus" ? 0 : 10, r.Down));
        Assert.All(reach, r => Assert.Equal(side == "plus" ? 10 : 0, r.Up));
    }

    // ----- what cannot be drawn is refused rather than drawn wrong -----

    /// <summary>A shorter list of stated distances leaves the rest of the points without bars.</summary>
    /// <remarks>
    /// Rather than losing the chart. Two stated against four points draws two bars, which is what
    /// a reader sees in Word.
    /// </remarks>
    [Fact]
    public void Fewer_stated_distances_than_points_leaves_the_rest_without()
    {
        var reach = ChartErrorAmounts.Reach(
            Bars(ChartErrorAmount.Custom, plus: [5, 10], minus: [20]), Sample);

        Assert.Equal([(5.0, 20.0), (10.0, 0.0), (0.0, 0.0), (0.0, 0.0)], reach);
    }

    /// <summary>A negative distance is not a bar reaching the other way.</summary>
    [Fact]
    public void A_negative_distance_draws_nothing_rather_than_reaching_backwards()
    {
        var reach = ChartErrorAmounts.Reach(
            Bars(ChartErrorAmount.Custom, plus: [-5, 10, -1, 0], minus: [-20, -1, 4, 0]), Sample);

        Assert.Equal([(0.0, 0.0), (10.0, 0.0), (0.0, 4.0), (0.0, 0.0)], reach);
    }

    /// <summary>A single point has no spread, so a spread-based bar has no length.</summary>
    [Fact]
    public void One_point_has_no_spread()
    {
        Assert.Equal(0, ChartErrorAmounts.Deviation([42]));
        Assert.Equal(0, ChartErrorAmounts.Error([42]));
        Assert.Equal(0, ChartErrorAmounts.Deviation([]));
    }

    /// <summary>Nothing a reach produces is ever a number that cannot be drawn.</summary>
    [Theory]
    [InlineData("fixedVal")]
    [InlineData("percentage")]
    [InlineData("stdDev")]
    [InlineData("stdErr")]
    [InlineData("cust")]
    public void No_reach_ever_produces_a_number_that_cannot_be_drawn(string name)
    {
        var amount = name switch
        {
            "percentage" => ChartErrorAmount.Percentage,
            "stdDev" => ChartErrorAmount.StandardDeviation,
            "stdErr" => ChartErrorAmount.StandardError,
            "cust" => ChartErrorAmount.Custom,
            _ => ChartErrorAmount.Fixed
        };

        double[] awkward = [0, -1, 1e300, double.MaxValue];

        var reach = ChartErrorAmounts.Reach(
            Bars(amount, double.NaN, plus: [double.NaN, 1e300, null, -0.0],
                minus: [double.PositiveInfinity, null, 3, 0]),
            awkward);

        foreach (var (up, down) in reach)
        {
            Assert.True(double.IsFinite(up), $"{name} produced up = {up}");
            Assert.True(double.IsFinite(down), $"{name} produced down = {down}");
            Assert.True(up >= 0 && down >= 0, $"{name} produced a negative reach");
        }
    }
}

/// <summary>
/// Tests that error bars land where Word puts them.
/// </summary>
/// <remarks>
/// Red pixels, for the reason <see cref="ChartTrendlineInkTests"/> gives: a whole-plot ink
/// comparison cannot see something this thin, and was shown there to score *higher* with the thing
/// under test removed altogether. The probe paints its bars <c>C00000</c> and nothing else on the
/// page is red.
///
/// This is also what settles the two things the format leaves open — whether a standard deviation
/// divides by n or by n−1, and how wide an end cap is drawn. Neither can be read; both change the
/// red count by far more than the measurement floor.
/// </remarks>
public class ChartErrorBarInkTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static bool IsBar((byte R, byte G, byte B) pixel) =>
        pixel.R > 100 && pixel.G < 90 && pixel.B < 90;

    [Theory]
    [InlineData(0, "a fixed ten, capped")]
    [InlineData(1, "the same, uncapped")]
    [InlineData(2, "a fixed ten, plus only")]
    [InlineData(3, "twenty percent of a point")]
    [InlineData(4, "one standard deviation")]
    [InlineData(5, "the standard error")]
    [InlineData(6, "stated per point")]
    [InlineData(7, "a narrower plot and larger type")]
    public void An_error_bar_is_drawn_where_word_draws_it(int page, string what)
    {
        const string fixtureName = "chart-error-bar-probe";

        if (TestFonts.SkipForMissingFonts(fixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(fixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var theirs = File.ReadAllBytes(reference);

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (ourRed, theirRed, both) = (0, 0, 0);

        for (var y = 74.0; y < 286; y++)
        for (var x = 74.0; x < 430; x++)
        {
            var a = IsBar(mine.At(x, y, scale));
            var b = IsBar(word.At(x, y, scale));

            if (a) ourRed++;
            if (b) theirRed++;
            if (a && b) both++;
        }

        _output.WriteLine($"{what}: {ourRed} red here, {theirRed} in Word's, {both} shared");

        // Fifty rather than a hundred: a bar reaching one way is half the ink of one reaching
        // both, and the page that does draws 84. What this guards against is nought, not a
        // shortfall — the coverage assertion below is what measures the drawing.
        Assert.True(theirRed > 50, $"Word drew no error bars to compare against on {what}");
        Assert.True(ourRed > 50, $"{what} drew no error bars");

        // The exact claim, and the one that matters: every pixel Word inks red, we ink too. A bar
        // of the wrong length, in the wrong place, or about the wrong value fails this at once —
        // it was 53% while the deviation bars were still drawn about their own points.
        Assert.True(both >= 0.97 * theirRed,
            $"{what} covers only {100.0 * both / theirRed:0.0}% of Word's bars");

        // And not much more of it. The bound is looser than the trendline's because the same
        // rasterising difference counts for more here: a page of short capped strokes has several
        // times the edge of one long line, and the measured excess across the eight pages is 2–8%.
        Assert.InRange((double)ourRed / theirRed, 0.95, 1.15);
    }
}
