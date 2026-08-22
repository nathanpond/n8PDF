using n8PDF;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the curves a trendline follows.
/// </summary>
/// <remarks>
/// The one part of a chart with no Word in it. Everywhere else here a rule is measured against
/// Word's own export, because Word's arithmetic is the thing being reproduced; a least-squares
/// line is not — it is determined by the points, and any two implementations that disagree about
/// it have one of them wrong.
///
/// So the expected values below are worked out independently rather than taken from a run of this
/// code: exactly where the arithmetic is short enough to do by hand, and against the closed form
/// otherwise. A test that asserted whatever the implementation happened to produce would be
/// checking that it does not change, which is a different and much weaker claim.
/// </remarks>
public class ChartTrendlineTests
{
    private static ChartTrendline Line(
        ChartTrendlineKind kind = ChartTrendlineKind.Linear,
        int order = 2, int period = 2, double forward = 0, double backward = 0,
        double? intercept = null) =>
        new(kind, order, period, forward, backward, intercept, null, 2.25);

    private static ChartTrendlineKind Kind(string name) => name switch
    {
        "poly" => ChartTrendlineKind.Polynomial,
        "exp" => ChartTrendlineKind.Exponential,
        "log" => ChartTrendlineKind.Logarithmic,
        "power" => ChartTrendlineKind.Power,
        "movingAvg" => ChartTrendlineKind.MovingAverage,
        _ => ChartTrendlineKind.Linear
    };

    /// <summary>
    /// Equal to within a relative tolerance, rather than to a count of decimal places.
    /// </summary>
    /// <remarks>
    /// xunit's decimal-place overload rounds both sides before comparing, so a value landing
    /// exactly on a rounding boundary fails however close it is: a power fit gave
    /// 24.796874999999993 against 24.796875 and was reported as differing, on an error of seven
    /// parts in a quadrillion. What these tests mean to assert is that the fit is exact to the
    /// precision a double can carry, which is what this says.
    /// </remarks>
    private static void Close(double expected, double actual) =>
        Assert.True(
            Math.Abs(expected - actual) <= 1e-9 * Math.Max(1, Math.Abs(expected)),
            $"expected {expected:R}, got {actual:R}");

    private static List<(double X, double Y)> Pairs(params (double X, double Y)[] points) => [.. points];

    /// <summary>
    /// Points that already lie on a line give that line back exactly.
    /// </summary>
    /// <remarks>
    /// The degenerate case of least squares, and the one worth asserting first: where the residual
    /// can be nought it must be. y = 3 + 2x through four points, so the drawn ends are the ends of
    /// the data.
    /// </remarks>
    [Fact]
    public void A_straight_line_through_straight_points_is_those_points()
    {
        var fitted = ChartTrendlineFit.Fit(Line(), Pairs((0, 3), (1, 5), (2, 7), (3, 9)));

        Assert.Equal(2, fitted.Count);
        Assert.Equal(0, fitted[0].X, 9);
        Assert.Equal(3, fitted[0].Y, 9);
        Assert.Equal(3, fitted[^1].X, 9);
        Assert.Equal(9, fitted[^1].Y, 9);
    }

    /// <summary>
    /// A least-squares line through points that are not collinear.
    /// </summary>
    /// <remarks>
    /// Worked by hand. For x = 0,1,2,3 and y = 1,3,2,5: mean x = 1.5, mean y = 2.75.
    /// Σ(x−x̄)(y−ȳ) = (−1.5)(−1.75) + (−0.5)(0.25) + (0.5)(−0.75) + (1.5)(2.25) = 5.5.
    /// Σ(x−x̄)² = 2.25 + 0.25 + 0.25 + 2.25 = 5. So the slope is 1.1 and the intercept
    /// 2.75 − 1.1×1.5 = 1.1.
    /// </remarks>
    [Fact]
    public void A_least_squares_line_has_the_slope_the_arithmetic_gives()
    {
        var fitted = ChartTrendlineFit.Fit(Line(), Pairs((0, 1), (1, 3), (2, 2), (3, 5)));

        Assert.Equal(2, fitted.Count);
        Assert.Equal(1.1, fitted[0].Y, 9);           // at x = 0, the intercept
        Assert.Equal(1.1 + 1.1 * 3, fitted[^1].Y, 9); // at x = 3
    }

    /// <summary>A quadratic through three points is the one quadratic through them.</summary>
    /// <remarks>
    /// Three points determine a parabola exactly, so least squares has no freedom left and must
    /// return it. y = x² through (0,0), (1,1), (2,4) — so the fitted curve must pass through
    /// x = 1.5 at 2.25, which no straight line through those points does.
    /// </remarks>
    [Fact]
    public void A_quadratic_through_three_points_passes_through_them()
    {
        var fitted = ChartTrendlineFit.Fit(
            Line(ChartTrendlineKind.Polynomial, order: 2), Pairs((0, 0), (1, 1), (2, 4)));

        Assert.True(fitted.Count > 2, "a curve is sampled rather than drawn end to end");

        foreach (var (x, y) in fitted) Close(x * x, y);
    }

    /// <summary>
    /// An exponential fit is a straight line through the logarithms.
    /// </summary>
    /// <remarks>
    /// y = 2·e^(x·ln 3) = 2·3^x gives 2, 6, 18, 54 — exactly exponential, so the fit is exact and
    /// every drawn point must lie on it.
    /// </remarks>
    [Fact]
    public void An_exponential_fit_recovers_an_exponential()
    {
        var fitted = ChartTrendlineFit.Fit(
            Line(ChartTrendlineKind.Exponential), Pairs((0, 2), (1, 6), (2, 18), (3, 54)));

        Assert.True(fitted.Count > 2);

        foreach (var (x, y) in fitted) Close(2 * Math.Pow(3, x), y);
    }

    /// <summary>A power fit recovers a power law: y = 3x², giving 3, 12, 27, 48.</summary>
    [Fact]
    public void A_power_fit_recovers_a_power_law()
    {
        var fitted = ChartTrendlineFit.Fit(
            Line(ChartTrendlineKind.Power), Pairs((1, 3), (2, 12), (3, 27), (4, 48)));

        Assert.True(fitted.Count > 2);

        foreach (var (x, y) in fitted) Close(3 * x * x, y);
    }

    /// <summary>A logarithmic fit recovers y = 2·ln x + 1.</summary>
    [Fact]
    public void A_logarithmic_fit_recovers_a_logarithm()
    {
        var points = Pairs(
            (1, 1), (2, 2 * Math.Log(2) + 1), (3, 2 * Math.Log(3) + 1), (4, 2 * Math.Log(4) + 1));

        var fitted = ChartTrendlineFit.Fit(Line(ChartTrendlineKind.Logarithmic), points);

        Assert.True(fitted.Count > 2);

        foreach (var (x, y) in fitted) Close(2 * Math.Log(x) + 1, y);
    }

    /// <summary>
    /// A moving average is the trailing mean, drawn at the last point of each run.
    /// </summary>
    /// <remarks>
    /// Which end it is drawn at is the whole of what distinguishes it from a smoothing. Values
    /// 1,2,3,4 at period 2 give means 1.5, 2.5, 3.5 — three points, not four, because the first
    /// has no run behind it — and the first of them belongs at x = 1, not at x = 0.5.
    /// </remarks>
    [Fact]
    public void A_moving_average_is_the_trailing_mean()
    {
        var fitted = ChartTrendlineFit.Fit(
            Line(ChartTrendlineKind.MovingAverage, period: 2), Pairs((0, 1), (1, 2), (2, 3), (3, 4)));

        Assert.Equal(3, fitted.Count);
        Assert.Equal((1, 1.5), (fitted[0].X, fitted[0].Y));
        Assert.Equal((2, 2.5), (fitted[1].X, fitted[1].Y));
        Assert.Equal((3, 3.5), (fitted[2].X, fitted[2].Y));
    }

    /// <summary>A period longer than the data draws nothing rather than one averaged point.</summary>
    [Fact]
    public void A_moving_average_longer_than_its_data_draws_nothing()
    {
        Assert.Empty(ChartTrendlineFit.Fit(
            Line(ChartTrendlineKind.MovingAverage, period: 5), Pairs((0, 1), (1, 2), (2, 3))));
    }

    /// <summary>
    /// Running forward extends the line past the data by that many categories.
    /// </summary>
    /// <remarks>
    /// One category is one step of x, which for these four points is 1. So a forward of 2 on
    /// y = 3 + 2x reaches x = 5, where the line reads 13.
    /// </remarks>
    [Fact]
    public void Running_forward_extends_the_line_past_the_data()
    {
        var fitted = ChartTrendlineFit.Fit(
            Line(forward: 2, backward: 1), Pairs((0, 3), (1, 5), (2, 7), (3, 9)));

        Assert.Equal(-1, fitted[0].X, 9);
        Assert.Equal(1, fitted[0].Y, 9);
        Assert.Equal(5, fitted[^1].X, 9);
        Assert.Equal(13, fitted[^1].Y, 9);
    }

    /// <summary>
    /// A forced intercept is met exactly, and the slope fitted around it.
    /// </summary>
    /// <remarks>
    /// Forcing the line through nought is the usual reason to ask. With the constant held at 0 the
    /// remaining fit minimises Σ(y − bx)², whose solution is b = Σxy ÷ Σx². For x = 1,2,3 and
    /// y = 2,4,7: Σxy = 2 + 8 + 21 = 31, Σx² = 1 + 4 + 9 = 14, so b = 31/14.
    /// </remarks>
    [Fact]
    public void A_forced_intercept_is_met_exactly()
    {
        var fitted = ChartTrendlineFit.Fit(Line(intercept: 0), Pairs((1, 2), (2, 4), (3, 7)));

        Assert.Equal(2, fitted.Count);

        // The drawn ends are the data's ends, so read the slope off them rather than assuming
        // one of them sits at x = 0.
        var slope = (fitted[^1].Y - fitted[0].Y) / (fitted[^1].X - fitted[0].X);
        Assert.Equal(31.0 / 14.0, slope, 9);

        // And extrapolating back to nought arrives at the intercept it was given.
        Assert.Equal(0, fitted[0].Y - slope * fitted[0].X, 9);
    }

    // ----- what cannot be fitted is refused rather than drawn wrong -----

    /// <summary>
    /// A log-space fit needs positive numbers, and says so by drawing nothing.
    /// </summary>
    /// <remarks>
    /// The alternative is a NaN reaching the content stream, which is the failure #26 was filed
    /// for elsewhere in this codebase. Word leaves such a trendline out too.
    /// </remarks>
    [Theory]
    [InlineData("exp")]
    [InlineData("power")]
    public void A_fit_through_logarithms_refuses_a_value_at_or_below_nought(string name)
    {
        var kind = Kind(name);

        Assert.Empty(ChartTrendlineFit.Fit(Line(kind), Pairs((1, 1), (2, 0), (3, 4))));
        Assert.Empty(ChartTrendlineFit.Fit(Line(kind), Pairs((1, 1), (2, -3), (3, 4))));
    }

    /// <summary>A logarithmic fit needs a positive argument, not a positive value.</summary>
    [Fact]
    public void A_logarithmic_fit_refuses_an_argument_at_or_below_nought()
    {
        Assert.Empty(ChartTrendlineFit.Fit(
            Line(ChartTrendlineKind.Logarithmic), Pairs((0, 1), (1, 2), (2, 3))));
    }

    /// <summary>Fewer than two points determine no line at all.</summary>
    [Fact]
    public void A_single_point_determines_nothing()
    {
        Assert.Empty(ChartTrendlineFit.Fit(Line(), Pairs((1, 1))));
        Assert.Empty(ChartTrendlineFit.Fit(Line(), []));
    }

    /// <summary>
    /// Points stacked above one x determine no line either, and must not divide by nothing.
    /// </summary>
    [Fact]
    public void Points_all_at_one_x_determine_nothing()
    {
        Assert.Empty(ChartTrendlineFit.Fit(Line(), Pairs((2, 1), (2, 5), (2, 9))));
    }

    /// <summary>
    /// Nothing a fit produces is ever a NaN or an infinity.
    /// </summary>
    /// <remarks>
    /// The catch-all for the above, run over every kind against data chosen to be awkward for each
    /// of them — a nought, a negative, a repeat and a very large number together. Whatever any of
    /// them makes of it, what comes back is drawable or it is empty.
    /// </remarks>
    [Theory]
    [InlineData("linear")]
    [InlineData("poly")]
    [InlineData("exp")]
    [InlineData("log")]
    [InlineData("power")]
    [InlineData("movingAvg")]
    public void No_fit_ever_produces_a_number_that_cannot_be_drawn(string name)
    {
        var kind = Kind(name);
        var awkward = Pairs((0, 0), (0, -1), (1, 1e300), (2, 2), (2, 2));

        foreach (var (x, y) in ChartTrendlineFit.Fit(Line(kind, order: 6, forward: 3), awkward))
        {
            Assert.True(double.IsFinite(x), $"{kind} produced x = {x}");
            Assert.True(double.IsFinite(y), $"{kind} produced y = {y}");
        }
    }
}

/// <summary>
/// Tests that a trendline lands where Word puts it.
/// </summary>
/// <remarks>
/// The arithmetic beside this says the fit is right; this says the line made of it is drawn in
/// the same place as Word's. It has to be ink, because a trendline writes no text — there is no
/// baseline to compare, only a stroke across the plot.
///
/// **Only the red is counted, and that is the point.** The obvious test — the share of all the
/// page's ink the two agree about, which is how <c>ChartKindTests</c> compares the shapes a chart
/// is made of — was written first and thrown away, because it does not work here: removing the
/// trendline from the drawing altogether *raised* the agreement from 99.54% to 99.16%, both
/// comfortably above any threshold worth setting. A line a point wide across a plot is too little
/// of a chart's ink for a whole-plot comparison to notice, so such a test would have passed
/// whatever this code did.
///
/// The probe therefore paints its trendlines <c>C00000</c> and nothing else on the page is red,
/// which turns "is the trendline where Word put it" into a question about a few hundred pixels
/// that belong to nothing else. A missing trendline scores nought.
/// </remarks>
public class ChartTrendlineInkTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>Whether a rendered pixel is the red the probe paints its trendlines.</summary>
    private static bool IsTrendline((byte R, byte G, byte B) pixel) =>
        pixel.R > 100 && pixel.G < 90 && pixel.B < 90;

    [Theory]
    [InlineData(0, "linear")]
    [InlineData(1, "a polynomial of the second order")]
    [InlineData(2, "a moving average over two points")]
    [InlineData(3, "linear, running two categories on")]
    [InlineData(4, "exponential")]
    [InlineData(5, "linear through a forced intercept")]
    [InlineData(6, "linear, running one category back")]
    public void A_trendline_is_drawn_where_word_draws_it(int page, string what)
    {
        const string fixtureName = "chart-trendline-probe";

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

        // Inside the frame rather than over it, for the reason ChartKindTests gives: Word clips a
        // chart to its own frame and nothing here does.
        for (var y = 74.0; y < 286; y++)
        for (var x = 74.0; x < 430; x++)
        {
            var a = IsTrendline(mine.At(x, y, scale));
            var b = IsTrendline(word.At(x, y, scale));

            if (a) ourRed++;
            if (b) theirRed++;
            if (a && b) both++;
        }

        _output.WriteLine(
            $"{what}: {ourRed} red here, {theirRed} in Word's, {both} shared");

        Assert.True(theirRed > 100, $"Word drew no trendline to compare against on {what}");
        Assert.True(ourRed > 100, $"{what} drew no trendline");

        // Within a twentieth of Word's, which a line of the same weight along the same path is;
        // one fitted differently is not, and one absent scores nought.
        Assert.InRange((double)ourRed / theirRed, 0.95, 1.05);

        // And in the same place rather than merely of the same size: a line drawn elsewhere on the
        // plot would satisfy the count and share almost none of it.
        Assert.True(both > 0.8 * Math.Min(ourRed, theirRed),
            $"{what} overlaps Word's trendline on only {100.0 * both / Math.Min(ourRed, theirRed):0.0}%");
    }
}
