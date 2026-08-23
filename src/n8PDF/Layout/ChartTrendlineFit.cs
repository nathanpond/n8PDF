using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Fits a trendline to the points a series holds.
/// </summary>
/// <remarks>
/// This is the one part of a chart with no Word in it. A least-squares line through given points
/// is not a matter of anybody's opinion, so nothing here is measured or fitted against Word's
/// export the way the rest of the chart code is — it is arithmetic, and it is tested against
/// coefficients worked out independently.
///
/// The three curved fits are all the straight one wearing a disguise. Taking logs turns
/// <c>y = a·e^(bx)</c> into <c>ln y = ln a + bx</c>, <c>y = a·x^b</c> into
/// <c>ln y = ln a + b·ln x</c>, and <c>y = a·ln x + b</c> is already straight in <c>ln x</c>. So
/// each transforms its points, fits a line, and transforms the answer back. That is what Excel
/// does and it is why these fits minimise the error in the *log* rather than in the value — a
/// point of a tenth counts as heavily as one of a thousand.
///
/// A transform that cannot be applied refuses rather than producing a NaN that would be drawn:
/// a logarithm needs its argument above nought, so a series holding a nought or a negative gets
/// no exponential, logarithmic or power line. Word leaves such a trendline out too.
/// </remarks>
internal static class ChartTrendlineFit
{
    /// <summary>How many points a curve is drawn with, which is enough to look smooth.</summary>
    private const int CurveSteps = 64;

    /// <summary>
    /// The points a trendline is drawn through, in the data's own coordinates, or an empty list
    /// where the data cannot carry the fit asked for.
    /// </summary>
    /// <param name="points">The series' points, x paired with y.</param>
    public static IReadOnlyList<(double X, double Y)> Fit(
        ChartTrendline trendline, IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count < 2) return [];

        return trendline.Kind == ChartTrendlineKind.MovingAverage
            ? MovingAverage(points, trendline.Period)
            : Curve(trendline, points);
    }

    /// <summary>
    /// A trailing mean: each point is the average of itself and the ones before it, and is drawn
    /// at the last of them rather than in their middle.
    /// </summary>
    /// <remarks>
    /// Which end it is drawn at is the whole of what distinguishes a moving average from a
    /// smoothing, and it is at the trailing end — the average of a run says where the run has got
    /// to, so it belongs at the point it has got to. A period longer than the data gives nothing
    /// to draw, rather than one point of everything averaged.
    /// </remarks>
    private static IReadOnlyList<(double X, double Y)> MovingAverage(
        IReadOnlyList<(double X, double Y)> points, int period)
    {
        if (period > points.Count) return [];

        var averaged = new List<(double X, double Y)>();

        for (var i = period - 1; i < points.Count; i++)
        {
            var total = 0.0;
            for (var j = i - period + 1; j <= i; j++) total += points[j].Y;

            averaged.Add((points[i].X, total / period));
        }

        return averaged;
    }

    /// <summary>The fitted curve, sampled across the range the trendline reaches.</summary>
    private static IReadOnlyList<(double X, double Y)> Curve(
        ChartTrendline trendline, IReadOnlyList<(double X, double Y)> points)
    {
        var from = points[0].X;
        var to = points[0].X;
        foreach (var point in points)
        {
            from = Math.Min(from, point.X);
            to = Math.Max(to, point.X);
        }

        // How far it runs past the data is stated in categories, which for a chart of pairs is
        // whatever one step of x happens to be.
        var step = points.Count > 1 ? (to - from) / (points.Count - 1) : 1;
        from -= trendline.Backward * step;
        to += trendline.Forward * step;

        if (!double.IsFinite(from) || !double.IsFinite(to) || to <= from) return [];

        if (Coefficients(trendline, points) is not { } fit) return [];

        // A straight line needs two points and nothing is gained by more; a curve is sampled.
        var steps = trendline.Kind is ChartTrendlineKind.Linear ? 1 : CurveSteps;
        var drawn = new List<(double X, double Y)>(steps + 1);

        for (var i = 0; i <= steps; i++)
        {
            var x = from + (to - from) * i / steps;
            var y = fit(x);

            // A power or logarithmic curve has nothing to say left of nought, so the run that
            // reaches there is simply shorter rather than drawn wrong.
            if (double.IsFinite(y)) drawn.Add((x, y));
        }

        return drawn.Count >= 2 ? drawn : [];
    }

    /// <summary>The fitted function itself, or null where this data cannot carry this fit.</summary>
    private static Func<double, double>? Coefficients(
        ChartTrendline trendline, IReadOnlyList<(double X, double Y)> points)
    {
        switch (trendline.Kind)
        {
            case ChartTrendlineKind.Linear:
            {
                var terms = Polynomial(points, 1, trendline.Intercept);
                return terms is null ? null : x => Evaluate(terms, x);
            }

            case ChartTrendlineKind.Polynomial:
            {
                var terms = Polynomial(points, trendline.Order, trendline.Intercept);
                return terms is null ? null : x => Evaluate(terms, x);
            }

            case ChartTrendlineKind.Exponential:
            {
                // ln y = ln a + bx, so the fit is straight once the values are logged.
                if (!AllPositive(points, values: true, arguments: false)) return null;

                var terms = Polynomial(Transform(points, logX: false, logY: true), 1, null);
                return terms is null ? null : x => Math.Exp(terms[0] + terms[1] * x);
            }

            case ChartTrendlineKind.Logarithmic:
            {
                // y = a·ln x + b: already straight in ln x, so only the argument is logged.
                if (!AllPositive(points, values: false, arguments: true)) return null;

                var terms = Polynomial(Transform(points, logX: true, logY: false), 1, null);
                return terms is null ? null : x => x > 0 ? terms[0] + terms[1] * Math.Log(x) : double.NaN;
            }

            case ChartTrendlineKind.Power:
            {
                // ln y = ln a + b·ln x, so both ends are logged.
                if (!AllPositive(points, values: true, arguments: true)) return null;

                var terms = Polynomial(Transform(points, logX: true, logY: true), 1, null);
                return terms is null
                    ? null
                    : x => x > 0 ? Math.Exp(terms[0] + terms[1] * Math.Log(x)) : double.NaN;
            }

            default:
                return null;
        }
    }

    private static bool AllPositive(
        IReadOnlyList<(double X, double Y)> points, bool values, bool arguments)
    {
        foreach (var point in points)
        {
            if (values && point.Y <= 0) return false;
            if (arguments && point.X <= 0) return false;
        }

        return true;
    }

    private static List<(double X, double Y)> Transform(
        IReadOnlyList<(double X, double Y)> points, bool logX, bool logY)
    {
        var transformed = new List<(double X, double Y)>(points.Count);
        foreach (var point in points)
            transformed.Add((logX ? Math.Log(point.X) : point.X, logY ? Math.Log(point.Y) : point.Y));

        return transformed;
    }

    /// <summary>
    /// Least squares for a polynomial of the given degree, lowest term first.
    /// </summary>
    /// <remarks>
    /// By the normal equations: the matrix of summed powers of x against the summed products with
    /// y, solved by elimination. That is the textbook method and it is ill-conditioned for a high
    /// degree — but the format bounds a polynomial to the sixth, and over the handful of points a
    /// chart holds it is exact to far more figures than a chart can draw.
    ///
    /// A forced intercept is honoured by fitting the *residual* — every y less the intercept —
    /// with the constant term held at nought, which is what forcing one means.
    /// </remarks>
    private static double[]? Polynomial(
        IReadOnlyList<(double X, double Y)> points, int degree, double? intercept)
    {
        var terms = degree + 1;
        if (points.Count < (intercept is null ? terms : terms - 1)) return null;

        var matrix = new double[terms, terms + 1];

        for (var row = 0; row < terms; row++)
        {
            for (var column = 0; column < terms; column++)
            {
                var total = 0.0;
                foreach (var point in points) total += Math.Pow(point.X, row + column);

                matrix[row, column] = total;
            }

            var product = 0.0;
            foreach (var point in points)
                product += (point.Y - (intercept ?? 0)) * Math.Pow(point.X, row);

            matrix[row, terms] = product;
        }

        // Forcing the intercept means the constant term is not free: it is the stated value, and
        // what is fitted is everything above it. Replacing the first equation with a0 = 0 says so.
        if (intercept is not null)
        {
            for (var column = 0; column <= terms; column++) matrix[0, column] = 0;
            matrix[0, 0] = 1;
        }

        var solved = Solve(matrix, terms);
        if (solved is null) return null;

        if (intercept is { } forced) solved[0] = forced;

        return solved;
    }

    /// <summary>Gaussian elimination with partial pivoting, or null where there is no solution.</summary>
    private static double[]? Solve(double[,] matrix, int size)
    {
        for (var pivot = 0; pivot < size; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < size; row++)
                if (Math.Abs(matrix[row, pivot]) > Math.Abs(matrix[best, pivot])) best = row;

            // A singular matrix means the points do not determine a curve of this degree — every
            // x the same, or fewer distinct x than the degree needs.
            if (Math.Abs(matrix[best, pivot]) < 1e-12) return null;

            if (best != pivot)
                for (var column = pivot; column <= size; column++)
                    (matrix[pivot, column], matrix[best, column]) = (matrix[best, column], matrix[pivot, column]);

            for (var row = pivot + 1; row < size; row++)
            {
                var factor = matrix[row, pivot] / matrix[pivot, pivot];
                for (var column = pivot; column <= size; column++)
                    matrix[row, column] -= factor * matrix[pivot, column];
            }
        }

        var answer = new double[size];
        for (var row = size - 1; row >= 0; row--)
        {
            var total = matrix[row, size];
            for (var column = row + 1; column < size; column++) total -= matrix[row, column] * answer[column];

            answer[row] = total / matrix[row, row];
        }

        foreach (var term in answer)
            if (!double.IsFinite(term))
                return null;

        return answer;
    }

    private static double Evaluate(double[] terms, double x)
    {
        var total = 0.0;
        for (var i = terms.Length - 1; i >= 0; i--) total = total * x + terms[i];

        return total;
    }
}
