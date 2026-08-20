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
    }

    /// <summary>
    /// Works out where the plotting goes and what the value axis runs between.
    /// </summary>
    /// <remarks>
    /// A chart that places its plot area by hand is followed exactly: Word's export of the fixture
    /// puts it at the fractions the chart gives, to the last decimal place. One that does not is
    /// given the same proportions Word's own automatic placing came to on that fixture, which is
    /// where this is weakest — see the note in the README.
    /// </remarks>
    public static Plan Arrange(ChartDefinition chart, double width, double height)
    {
        var layout = chart.PlotArea ?? new ChartLayout(0.2, 0.1, 0.7, 0.7);

        var (minimum, maximum, unit) = Scale(chart);

        return new Plan(
            layout.X * width, layout.Y * height, layout.Width * width, layout.Height * height,
            minimum, maximum, unit);
    }

    /// <summary>
    /// What the value axis runs between and how far apart its marks are, where the chart does not
    /// say.
    /// </summary>
    /// <remarks>
    /// The rule here is the plainest one that fits: from nought to a round number above the
    /// largest value, in steps that give between four and six marks. Word's own choice is not this
    /// simple, and a chart that leaves its scale to be worked out is where this is furthest from
    /// Word — which is why the fixture states its own.
    /// </remarks>
    private static (double Minimum, double Maximum, double Unit) Scale(ChartDefinition chart)
    {
        var values = chart.Series
            .SelectMany(series => series.Values)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        var highest = values.Count > 0 ? values.Max() : 0;
        var lowest = Math.Min(0, values.Count > 0 ? values.Min() : 0);

        var minimum = chart.ValueAxis?.Minimum ?? lowest;
        var maximum = chart.ValueAxis?.Maximum ?? RoundUp(highest);

        if (maximum <= minimum) maximum = minimum + 1;

        var unit = chart.ValueAxis?.MajorUnit ?? RoundUp((maximum - minimum) / 5);

        return (minimum, maximum, unit <= 0 ? maximum - minimum : unit);
    }

    /// <summary>The next round number at or above a value: one, two or five times a power of ten.</summary>
    private static double RoundUp(double value)
    {
        if (value <= 0) return 1;

        var power = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var share = value / power;

        return power * (share <= 1 ? 1 : share <= 2 ? 2 : share <= 5 ? 5 : 10);
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

        // The two axis lines, along the left and the foot of the plot.
        if (chart.ValueAxis is { Deleted: false })
            operations.Add(Stroke([(plan.Left, plan.Top), (plan.Left, plan.Bottom)]));

        if (chart.CategoryAxis is { Deleted: false })
        {
            operations.Add(Stroke([(plan.Left, plan.Bottom), (plan.Right, plan.Bottom)]));

            if (chart.CategoryAxis.MajorTickMark is not "none")
            {
                // A category axis marks the boundaries between its categories rather than their
                // middles, so a chart of four categories carries five marks.
                var categories = Math.Max(1, chart.Categories.Count);

                for (var i = 0; i <= categories; i++)
                {
                    var x = plan.Left + plan.Width * i / categories;
                    operations.Add(Stroke([(x, plan.Bottom), (x, plan.Bottom + TickLength)]));
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
