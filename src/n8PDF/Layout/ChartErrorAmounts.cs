using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// How far an error bar reaches from its point.
/// </summary>
/// <remarks>
/// Arithmetic rather than Word's opinion, like the trendline fitting beside it, so it lives apart
/// from the composer and is tested against figures worked out independently.
///
/// Two of the five amounts are a property of the whole series rather than of a point: a standard
/// deviation and a standard error describe the spread of the numbers, so every bar in the series
/// is the same length and moving a point changes all of them. The other three are per point.
///
/// The standard deviation is the **sample** one, dividing by n−1 rather than by n. The format says
/// nothing about which, and the two are 15% apart over the probe's four points, so it was measured
/// rather than chosen: Word's own drawing reaches 15.55 of the value axis where the population
/// deviation would reach 13.46. Read out of the reference PDF's path geometry rather than off the
/// picture, since 15% of a short bar is a couple of points on the page. See
/// <c>chart-error-bar-probe</c>, whose fifth page is the one that settles it.
/// </remarks>
internal static class ChartErrorAmounts
{
    /// <summary>
    /// How far the bars reach above and below each of a series' points.
    /// </summary>
    /// <param name="values">The series' values, in order, with its gaps left out.</param>
    /// <returns>
    /// One pair for each value: how far up, and how far down. Either may be nought, which is what
    /// a bar reaching only one way asks for.
    /// </returns>
    public static IReadOnlyList<(double Up, double Down)> Reach(
        ChartErrorBars bars, IReadOnlyList<double> values)
    {
        var reach = new List<(double Up, double Down)>(values.Count);

        // A spread is a property of the series, so it is worked out once rather than per point.
        var shared = bars.Amount switch
        {
            ChartErrorAmount.StandardDeviation => Deviation(values) * bars.Value,
            ChartErrorAmount.StandardError => Error(values),
            _ => 0.0
        };

        for (var i = 0; i < values.Count; i++)
        {
            var (up, down) = bars.Amount switch
            {
                ChartErrorAmount.Fixed => (bars.Value, bars.Value),

                // Of the point's own size, so a bar at nought has no length whichever way it runs,
                // and one below nought reaches as far as one above it would.
                ChartErrorAmount.Percentage =>
                    (Math.Abs(values[i]) * bars.Value / 100, Math.Abs(values[i]) * bars.Value / 100),

                ChartErrorAmount.StandardDeviation or ChartErrorAmount.StandardError =>
                    (shared, shared),

                ChartErrorAmount.Custom => (At(bars.Plus, i), At(bars.Minus, i)),

                _ => (0.0, 0.0)
            };

            // A distance is a distance: a negative one is not a bar reaching the other way, it is
            // a document saying something that cannot be drawn.
            up = double.IsFinite(up) ? Math.Max(0, up) : 0;
            down = double.IsFinite(down) ? Math.Max(0, down) : 0;

            reach.Add((
                bars.Sides == ChartErrorSides.Minus ? 0 : up,
                bars.Sides == ChartErrorSides.Plus ? 0 : down));
        }

        return reach;
    }

    /// <summary>
    /// The one value every bar of a series is drawn about, where they share one, and null where
    /// each is drawn about its own point.
    /// </summary>
    /// <remarks>
    /// Only a standard deviation does this, and it is the strangest thing the probe turned up. A
    /// deviation bar is not drawn about the point it belongs to at all: Word draws every one of
    /// them about the series' **mean**, so a series of four points gets four identical bars at
    /// four different places along the foot, saying where the middle of the data is and how far it
    /// scatters rather than anything about the point they stand on.
    ///
    /// Measured, and it is not a small effect — the probe's fifth page draws all four bars over
    /// the same 144..203 of the page where drawing them about their points spreads them across
    /// 111..236. The mean of 30, 45, 20 and 55 is 37.5, and the middle of Word's bars reads 37.7.
    ///
    /// A standard error does **not** do it: its bars sit on their own points, which the sixth page
    /// confirms to the pixel. The two are inconsistent with each other and this reproduces the
    /// inconsistency, because reproducing Word is the point.
    /// </remarks>
    public static double? Centre(ChartErrorBars bars, IReadOnlyList<double> values)
    {
        if (bars.Amount != ChartErrorAmount.StandardDeviation || values.Count == 0) return null;

        var mean = 0.0;
        foreach (var value in values) mean += value;

        return mean / values.Count;
    }

    /// <summary>
    /// One of a stated pair of distances, or nothing where the document stated fewer than it has
    /// points.
    /// </summary>
    /// <remarks>
    /// A short array is not an error worth losing the chart over — the points it covers get their
    /// bars and the rest get none, which is what a reader sees in Word.
    /// </remarks>
    private static double At(IReadOnlyList<double?> stated, int index) =>
        index < stated.Count && stated[index] is { } value ? value : 0;

    /// <summary>
    /// The standard deviation of the series, taken as a sample rather than as the whole of
    /// something.
    /// </summary>
    /// <remarks>
    /// Dividing by n−1, not by n. Measured rather than read: see the remarks on the class.
    /// </remarks>
    public static double Deviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2) return 0;

        var mean = 0.0;
        foreach (var value in values) mean += value;
        mean /= values.Count;

        var square = 0.0;
        foreach (var value in values) square += (value - mean) * (value - mean);

        return Math.Sqrt(square / (values.Count - 1));
    }

    /// <summary>
    /// The standard error of the series: its spread divided by the root of how many there are.
    /// </summary>
    /// <remarks>
    /// The stated value has no part in this one. The format allows the element to carry one
    /// anyway, and Word ignores it — which is why it is not multiplied in here.
    /// </remarks>
    public static double Error(IReadOnlyList<double> values) =>
        values.Count < 2 ? 0 : Deviation(values) / Math.Sqrt(values.Count);
}
