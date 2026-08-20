using n8PDF.Images;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Draws a chart: the plot area, the gridlines, the axes and the bars, in the chart's own
/// coordinates.
/// </summary>
/// <remarks>
/// Everything here was measured against Word's export of the chart fixtures rather than taken from
/// the specification, which says what a chart holds and almost nothing about how it looks. The
/// numbers each rule was measured from are named where the rule is.
///
/// What comes back is paths only. The labels are text, and text is laid out by the engine that
/// lays out everything else — <see cref="LayoutEngine"/> asks for the labels separately and puts
/// them on the page as ordinary lines, so a chart's axis can be read and copied like any other
/// words on the page.
/// </remarks>
internal static class ChartComposer
{
    /// <summary>
    /// The corner a chart's own frame is rounded by, and the grey it is outlined in. Both are
    /// Word's defaults for a chart that says nothing about its own border, measured from its
    /// export of chart-column: a ten point corner and #898989 at half a point.
    /// </summary>
    private const double FrameCorner = 10;

    private static readonly DrawingColor FrameLine = new(0x89, 0x89, 0x89);

    /// <summary>What a chart draws its axes, gridlines and marks with when it says nothing.</summary>
    private const double LineWidth = 0.5;

    /// <summary>How far outside the axis a tick mark reaches, measured from the same export.</summary>
    private const double TickLength = 6.35;

    /// <summary>
    /// Where everything in a chart goes: the plot area, and what has to be drawn around it.
    /// </summary>
    public sealed record Plan(
        double Left, double Top, double Width, double Height,
        double Minimum, double Maximum, double MajorUnit)
    {
        public double Right => Left + Width;

        public double Bottom => Top + Height;

        /// <summary>Where a value sits, measured down from the top of the chart.</summary>
        public double PositionOf(double value) =>
            Bottom - (value - Minimum) / (Maximum - Minimum) * Height;

        /// <summary>
        /// Where the axis of categories runs, which is where the value axis reads nought rather
        /// than the foot of the plot.
        /// </summary>
        /// <remarks>
        /// The two are the same thing wherever nothing is negative, since the scale then begins at
        /// nought. Where something is, they part: Word's drawing of the probe's chart of −20 and 60
        /// puts the words under the bars a line below the nought, three quarters of the way down
        /// the plot, and not at the bottom of it. What hangs below nought hangs past them.
        /// </remarks>
        public double CrossingY => PositionOf(Math.Clamp(0, Minimum, Maximum));
    }

    /// <summary>
    /// The margin a chart keeps on a side that carries nothing, and the one it keeps outside the
    /// labels on a side that does.
    /// </summary>
    /// <remarks>
    /// Both measured from chart-layout-probe. A chart whose labels are all turned off puts its
    /// plotting eleven points inside its frame on every side; one that carries them begins the
    /// labels 6.5pt inside the frame, at every type size and however wide the numbers are — the
    /// widest label starts at exactly 6.5pt from the edge on all five pages that have one.
    /// </remarks>
    private const double BareMargin = 11;

    private const double LabelMargin = 6.5;

    /// <summary>
    /// How far above the plot the topmost label reaches, over and above five points: half the
    /// height of the label as Windows reads the face, which for Calibri is 0.611 of the type size.
    /// Measured at ten point and at twenty, where Word leaves 11.10pt and 17.21pt.
    /// </summary>
    private const double TopMargin = 5;

    /// <summary>
    /// Works out where the plotting goes and what the value axis runs between.
    /// </summary>
    /// <remarks>
    /// A chart that places its plot area by hand is followed exactly: Word's export puts it at the
    /// fractions the chart gives, to the last decimal place. One that does not has its plotting
    /// worked out from what has to fit around it, which is what chart-layout-probe measures:
    ///
    ///   the left  makes room for the widest number up the value axis, and the gap it keeps from
    ///             the axis, beginning 6.5pt inside the frame
    ///   the foot  makes room for a category's own line, which sits 1.584 type sizes below the
    ///             axis and reaches its descender below that
    ///   the top   leaves half a label's height, so the topmost number does not overrun the frame
    ///   the right leaves the bare margin, since nothing is drawn there — a category label wider
    ///             than its bars is left to overrun, which is what Word does with one
    ///
    /// and every side falls back to the bare margin where the labels it would make room for are
    /// not drawn at all.
    /// </remarks>
    /// <param name="measure">
    /// How wide a string is in the face the labels are set in, at a given size. The labels have to
    /// be measured to be made room for, and only the font library can measure them.
    /// </param>
    /// <param name="labelHeight">
    /// How far a label reaches above and below its own baseline, as the face Windows reads it
    /// gives them.
    /// </param>
    public static Plan Arrange(
        ChartDefinition chart, double width, double height,
        Func<string, double, double> measure,
        Func<double, (double Ascent, double Descent)> labelHeight)
    {
        var (minimum, maximum, unit) = Scale(chart);

        if (chart.PlotArea is { } stated)
        {
            return new Plan(
                stated.X * width, stated.Y * height, stated.Width * width, stated.Height * height,
                minimum, maximum, unit);
        }

        var plan = new Plan(0, 0, width, height, minimum, maximum, unit);

        var left = BareMargin;
        var top = BareMargin;
        var bottom = BareMargin;

        if (chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" } valueAxis)
        {
            var size = valueAxis.LabelSizePoints;

            var widest = Marks(plan)
                .Select(value => measure(Format(value), size))
                .DefaultIfEmpty(0)
                .Max();

            left = Math.Max(left, LabelMargin + widest + size * ValueLabelGap);

            var (ascent, descent) = labelHeight(size);
            top = Math.Max(top, TopMargin + (ascent + descent) / 2);
        }

        if (chart.CategoryAxis is { Deleted: false, TickLabelPosition: not "none" } categoryAxis)
        {
            var size = categoryAxis.LabelSizePoints;
            var (_, descent) = labelHeight(size);

            bottom = Math.Max(bottom,
                LabelMargin + size * CategoryLabelBaseline + descent);
        }

        return new Plan(
            left, top, Math.Max(1, width - left - BareMargin), Math.Max(1, height - top - bottom),
            minimum, maximum, unit);
    }

    /// <summary>
    /// How far a number on the value axis ends short of its axis, as a share of its type size, and
    /// how far below the axis a category's baseline sits. Both measured at two sizes; see
    /// <see cref="LayoutEngine"/>, which places the labels these leave room for.
    /// </summary>
    public const double ValueLabelGap = 0.94;

    public const double CategoryLabelBaseline = 1.584;

    /// <summary>A number as an axis writes it: whole where it is whole.</summary>
    public static string Format(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// What the value axis runs between and how far apart its marks are, where the chart does not
    /// say.
    /// </summary>
    /// <remarks>
    /// Measured from chart-scale-probe, twelve charts differing only in the numbers they hold. Two
    /// rules account for every one of them:
    ///
    ///   the step is the largest of one, two or five times a power of ten that is no more than a
    ///   fifth of the span, and the top of the axis is the smallest multiple of that step lying
    ///   strictly above the largest value
    ///
    /// so a chart of 7 runs to 8 in ones, one of 9.5 runs to 10 in ones, one of 10 runs to 12 in
    /// twos, one of 47 runs to 50 in fives, one of 105 runs to 120 in twenties, and one of 0.4
    /// runs to 0.45 in twentieths. That the top is strictly above rather than at the largest value
    /// is what puts a chart of exactly 100 at 120 rather than leaving its tallest bar touching the
    /// frame.
    ///
    /// The foot is nought wherever nothing is negative, whatever the smallest value is — a chart
    /// of 30 and 55 still starts at nought. Where something is negative the foot steps below it the
    /// same way the top steps above: a chart of −20 and 60 runs from −30 to 70 in tens.
    /// </remarks>
    private static (double Minimum, double Maximum, double Unit) Scale(ChartDefinition chart)
    {
        var values = chart.Series
            .SelectMany(series => series.Values)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        var highest = values.Count > 0 ? Math.Max(0, values.Max()) : 0;
        var lowest = values.Count > 0 ? Math.Min(0, values.Min()) : 0;

        var unit = chart.ValueAxis?.MajorUnit ?? Step(highest - lowest);

        var maximum = chart.ValueAxis?.Maximum ?? Above(highest, unit);
        var minimum = chart.ValueAxis?.Minimum ?? (lowest < 0 ? -Above(-lowest, unit) : 0);

        if (maximum <= minimum) maximum = minimum + Math.Max(unit, 1);

        return (minimum, maximum, unit <= 0 ? maximum - minimum : unit);
    }

    /// <summary>
    /// How far apart the marks go: the largest of one, two or five times a power of ten that is no
    /// more than a fifth of what the axis has to span.
    /// </summary>
    private static double Step(double span)
    {
        if (span <= 0 || double.IsNaN(span) || double.IsInfinity(span)) return 1;

        var fifth = span / 5;
        var power = Math.Pow(10, Math.Floor(Math.Log10(fifth)));
        var share = fifth / power;

        return power * (share >= 5 ? 5 : share >= 2 ? 2 : 1);
    }

    /// <summary>The smallest multiple of a step lying strictly above a value.</summary>
    private static double Above(double value, double step)
    {
        if (step <= 0) return value;

        var steps = Math.Floor(value / step + 0.000000001) + 1;

        return steps * step;
    }

    /// <summary>Everything the chart draws that is not text.</summary>
    public static VectorDrawing Draw(
        ChartDefinition chart, Plan plan, double width, double height, DocumentTheme theme)
    {
        var operations = new List<DrawingOperation>
        {
            // The chart's own frame: white, cornered, and outlined in grey.
            new PathOperation(
                RoundedRectangle(0, 0, width, height, FrameCorner),
                new DrawingColor(255, 255, 255), FrameLine, LineWidth, EvenOdd: false)
        };

        // The plot area sits on top of it, filled but not outlined.
        operations.Add(new PathOperation(
            Rectangle(plan.Left, plan.Top, plan.Width, plan.Height),
            new DrawingColor(255, 255, 255), null, LineWidth, EvenOdd: false));

        // A pie has no axes to draw, and nothing behind it but the frame.
        if (chart.Kind == ChartKind.Pie)
        {
            operations.AddRange(Slices(chart, plan, theme));

            return new VectorDrawing(width, height, operations);
        }

        if (chart.ValueAxis is { Deleted: false, MajorGridlines: true })
        {
            foreach (var value in Marks(plan))
            {
                // The mark at the bottom of the scale is the axis itself and is drawn as one.
                if (Math.Abs(value - plan.Minimum) < plan.MajorUnit / 1000) continue;

                var y = plan.PositionOf(value);
                operations.Add(Stroke([(plan.Left, y), (plan.Right, y)]));
            }
        }

        foreach (var bar in Bars(chart, plan))
            operations.Add(new PathOperation(Rectangle(bar.X, bar.Y, bar.Width, bar.Height),
                Resolve(bar.Fill, theme), null, LineWidth, EvenOdd: false));

        if (chart.Kind == ChartKind.Line) operations.AddRange(Lines(chart, plan, theme));

        // The two axis lines, along the left and the foot of the plot.
        if (chart.ValueAxis is { Deleted: false })
            operations.Add(Stroke([(plan.Left, plan.Top), (plan.Left, plan.Bottom)]));

        if (chart.CategoryAxis is { Deleted: false })
        {
            var crossing = plan.CrossingY;

            operations.Add(Stroke([(plan.Left, crossing), (plan.Right, crossing)]));

            if (chart.CategoryAxis.MajorTickMark is not "none")
            {
                // A category axis marks the boundaries between its categories rather than their
                // middles, so a chart of four categories carries five marks.
                var categories = Math.Max(1, chart.Categories.Count);

                for (var i = 0; i <= categories; i++)
                {
                    var x = plan.Left + plan.Width * i / categories;
                    operations.Add(Stroke([(x, crossing), (x, crossing + TickLength)]));
                }
            }
        }

        return new VectorDrawing(width, height, operations);
    }

    /// <summary>Every bar the chart draws, in the chart's own coordinates.</summary>
    /// <remarks>
    /// How wide a bar is falls out of the gap between them: the gap is a percentage of one bar's
    /// width, so a category holding one series at a gap of 150 is two and a half bars wide and the
    /// bar is two fifths of it. Measured from Word's export: four categories across 252pt gives
    /// 63pt each and a bar of 25.2pt, which is 63 ÷ 2.5 exactly.
    /// </remarks>
    public static IEnumerable<(double X, double Y, double Width, double Height, DrawingColorReference? Fill)>
        Bars(ChartDefinition chart, Plan plan)
    {
        if (chart.Kind is not (ChartKind.Column or ChartKind.Bar)) yield break;

        var categories = Math.Max(1, chart.Categories.Count);
        var slot = plan.Width / categories;

        var series = Math.Max(1, chart.Series.Count);
        var overlap = chart.Overlap / 100.0;

        // The bars of one category sit side by side, less however far they overlap.
        var barWidth = slot / (series - (series - 1) * overlap + chart.GapWidth / 100.0);
        if (barWidth <= 0) yield break;

        var baseline = plan.PositionOf(Math.Max(plan.Minimum, 0));

        for (var category = 0; category < categories; category++)
        {
            var group = series * barWidth - (series - 1) * barWidth * overlap;
            var left = plan.Left + slot * category + (slot - group) / 2;

            for (var index = 0; index < chart.Series.Count; index++)
            {
                var values = chart.Series[index].Values;
                if (category >= values.Count || values[category] is not { } value) continue;

                var x = left + index * barWidth * (1 - overlap);
                var top = plan.PositionOf(value);

                yield return value >= 0
                    ? (x, top, barWidth, baseline - top, chart.Series[index].Fill)
                    : (x, baseline, barWidth, top - baseline, chart.Series[index].Fill);
            }
        }
    }

    /// <summary>
    /// A line through each series' points, one to a category.
    /// </summary>
    /// <remarks>
    /// The points sit at the middles of the categories, as the bars of a bar chart do, and the
    /// line runs from one to the next — curving through them unless the series says not to, which
    /// is the format's own default. Measured from Word's export of chart-line-pie: the four points
    /// of its line land at 175.5, 238.5, 301.5 and 364.5 across a plot running 144 to 396, which
    /// is the middle of each quarter of it.
    /// </remarks>
    private static IEnumerable<DrawingOperation> Lines(
        ChartDefinition chart, Plan plan, DocumentTheme theme)
    {
        var categories = Math.Max(1, chart.Categories.Count);
        var slot = plan.Width / categories;

        foreach (var series in chart.Series)
        {
            var points = new List<(double X, double Y)>();

            for (var i = 0; i < series.Values.Count && i < categories; i++)
            {
                if (series.Values[i] is not { } value) continue;

                points.Add((plan.Left + slot * (i + 0.5), plan.PositionOf(value)));
            }

            if (points.Count < 2) continue;

            yield return new PathOperation(
                series.Smooth ? Curve(points) : Straight(points),
                null, Resolve(series.Line, theme), series.LineWidthPoints, EvenOdd: false);
        }
    }

    private static IReadOnlyList<PathStep> Straight(IReadOnlyList<(double X, double Y)> points)
    {
        var steps = new List<PathStep> { new(PathStepKind.Move, [points[0]]) };

        for (var i = 1; i < points.Count; i++)
            steps.Add(new PathStep(PathStepKind.Line, [points[i]]));

        return steps;
    }

    /// <summary>
    /// A curve through every point, which is what a line chart draws unless told otherwise.
    /// </summary>
    /// <remarks>
    /// Each point is passed through at a slope of half the distance between its neighbours, and
    /// the ends at the slope of their own segment; the control points sit a third of the way along
    /// those slopes. That is a Catmull-Rom spline written as Béziers, and it is exactly what Word
    /// draws — every control point of the fixture's curve comes out of this to the EMU.
    /// </remarks>
    private static IReadOnlyList<PathStep> Curve(IReadOnlyList<(double X, double Y)> points)
    {
        var steps = new List<PathStep> { new(PathStepKind.Move, [points[0]]) };

        for (var i = 0; i < points.Count - 1; i++)
        {
            var start = points[i];
            var end = points[i + 1];

            var before = i > 0 ? points[i - 1] : start;
            var after = i + 2 < points.Count ? points[i + 2] : end;

            // The slope at a point is half of what its neighbours span; at an end there is only
            // the one segment, so the slope is its own.
            var startSlope = i > 0
                ? ((end.X - before.X) / 2, (end.Y - before.Y) / 2)
                : (end.X - start.X, end.Y - start.Y);

            var endSlope = i + 2 < points.Count
                ? ((after.X - start.X) / 2, (after.Y - start.Y) / 2)
                : (end.X - start.X, end.Y - start.Y);

            steps.Add(new PathStep(PathStepKind.Curve,
            [
                (start.X + startSlope.Item1 / 3, start.Y + startSlope.Item2 / 3),
                (end.X - endSlope.Item1 / 3, end.Y - endSlope.Item2 / 3),
                end
            ]));
        }

        return steps;
    }

    /// <summary>
    /// A pie: one slice for each value, filling the plot area.
    /// </summary>
    /// <remarks>
    /// The circle is centred in the plot area and reaches the nearer pair of its edges — Word's
    /// export puts the fixture's pie at the middle of a 216 by 172.8 plot with a radius of 86.4,
    /// which is half the shorter side. The first slice begins at the top and they run clockwise,
    /// each ending where the next begins.
    /// </remarks>
    private static IEnumerable<DrawingOperation> Slices(
        ChartDefinition chart, Plan plan, DocumentTheme theme)
    {
        var series = chart.Series.FirstOrDefault();
        if (series is null) yield break;

        var values = series.Values.Select(value => Math.Max(0, value ?? 0)).ToList();
        var total = values.Sum();
        if (total <= 0) yield break;

        var centre = (X: plan.Left + plan.Width / 2, Y: plan.Top + plan.Height / 2);
        var radius = Math.Min(plan.Width, plan.Height) / 2;

        var angle = chart.FirstSliceAngle * Math.PI / 180;

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0) continue;

            var sweep = values[i] / total * 2 * Math.PI;

            var fill = series.PointFills.TryGetValue(i, out var point) ? point : series.Fill;

            yield return new PathOperation(
                Wedge(centre, radius, angle, sweep),
                Resolve(fill, theme), new DrawingColor(255, 255, 255), 1.5, EvenOdd: false);

            angle += sweep;
        }
    }

    /// <summary>
    /// One slice, from its centre out to the rim, round, and back. The angles run clockwise from
    /// the top, which is how a pie is read.
    /// </summary>
    private static IReadOnlyList<PathStep> Wedge(
        (double X, double Y) centre, double radius, double from, double sweep)
    {
        var steps = new List<PathStep> { new(PathStepKind.Move, [At(from)]) };

        // A cubic cannot be an arc, so a slice is drawn in pieces no larger than a quarter turn.
        var pieces = Math.Max(1, (int)Math.Ceiling(sweep / (Math.PI / 2)));
        var step = sweep / pieces;
        var control = 4.0 / 3 * Math.Tan(step / 4) * radius;

        for (var i = 0; i < pieces; i++)
        {
            var start = from + step * i;
            var end = start + step;

            var (sx, sy) = At(start);
            var (ex, ey) = At(end);

            // The control points run along the tangent at each end, which for a circle is the
            // radius turned a quarter.
            steps.Add(new PathStep(PathStepKind.Curve,
            [
                (sx + control * Math.Cos(start), sy + control * Math.Sin(start)),
                (ex - control * Math.Cos(end), ey - control * Math.Sin(end)),
                (ex, ey)
            ]));
        }

        steps.Add(new PathStep(PathStepKind.Line, [centre]));
        steps.Add(new PathStep(PathStepKind.Close, []));

        return steps;

        (double X, double Y) At(double a) =>
            (centre.X + radius * Math.Sin(a), centre.Y - radius * Math.Cos(a));
    }

    /// <summary>The values the value axis marks, from its bottom to its top.</summary>
    public static IEnumerable<double> Marks(Plan plan)
    {
        if (plan.MajorUnit <= 0) yield break;

        // Counted rather than added up, so that a hundred marks do not drift.
        var steps = (int)Math.Floor((plan.Maximum - plan.Minimum) / plan.MajorUnit + 0.000001);

        for (var i = 0; i <= steps; i++) yield return plan.Minimum + i * plan.MajorUnit;
    }

    private static PathOperation Stroke(IReadOnlyList<(double X, double Y)> points) =>
        new([
            new PathStep(PathStepKind.Move, [points[0]]),
            new PathStep(PathStepKind.Line, [points[1]])
        ], null, new DrawingColor(0, 0, 0), LineWidth, EvenOdd: false);

    private static DrawingColor? Resolve(DrawingColorReference? color, DocumentTheme theme)
    {
        var hex = color?.Hex ?? theme.ResolveColor(color?.ThemeSlot);
        if (hex is null || hex.Length != 6) return new DrawingColor(0x44, 0x72, 0xC4);

        try
        {
            return new DrawingColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex[4..], 16));
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return new DrawingColor(0x44, 0x72, 0xC4);
        }
    }

    private static IReadOnlyList<PathStep> Rectangle(double x, double y, double width, double height) =>
    [
        new(PathStepKind.Move, [(x, y)]),
        new(PathStepKind.Line, [(x + width, y)]),
        new(PathStepKind.Line, [(x + width, y + height)]),
        new(PathStepKind.Line, [(x, y + height)]),
        new(PathStepKind.Close, [])
    ];

    private static IReadOnlyList<PathStep> RoundedRectangle(
        double x, double y, double width, double height, double radius)
    {
        // The same quarter-circle approximation the shapes use.
        const double arc = 0.5523;
        var control = radius * (1 - arc);

        return
        [
            new(PathStepKind.Move, [(x + radius, y)]),
            new(PathStepKind.Line, [(x + width - radius, y)]),
            new(PathStepKind.Curve,
                [(x + width - control, y), (x + width, y + control), (x + width, y + radius)]),
            new(PathStepKind.Line, [(x + width, y + height - radius)]),
            new(PathStepKind.Curve,
                [(x + width, y + height - control), (x + width - control, y + height),
                    (x + width - radius, y + height)]),
            new(PathStepKind.Line, [(x + radius, y + height)]),
            new(PathStepKind.Curve,
                [(x + control, y + height), (x, y + height - control), (x, y + height - radius)]),
            new(PathStepKind.Line, [(x, y + radius)]),
            new(PathStepKind.Curve, [(x, y + control), (x + control, y), (x + radius, y)]),
            new(PathStepKind.Close, [])
        ];
    }
}
