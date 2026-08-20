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

    /// <summary>And what it outlines a bar turned the other way about with.</summary>
    private const double InvertedOutline = 0.75;

    /// <summary>
    /// How far outside the axis a tick mark reaches. Word writes it as 40301 EMU on both axes of
    /// chart-axis-probe and on the lying axis of chart-bar-stacked alike, which is this.
    /// </summary>
    private const double TickLength = 3.1733;

    /// <summary>
    /// Where everything in a chart goes: the plot area, and what has to be drawn around it.
    /// </summary>
    /// <param name="Lying">
    /// True where the value axis runs along the foot and the categories up the side, which is what
    /// a bar chart is and a column chart is not.
    /// </param>
    public sealed record Plan(
        double Left, double Top, double Width, double Height,
        double Minimum, double Maximum, double MajorUnit, bool Lying = false)
    {
        public double Right => Left + Width;

        public double Bottom => Top + Height;

        /// <summary>
        /// Where a value sits along the axis it is measured on: down from the top of the chart for
        /// an upright one, across from its left for one lying down.
        /// </summary>
        public double PositionOf(double value) =>
            Lying
                ? Left + (value - Minimum) / (Maximum - Minimum) * Width
                : Bottom - (value - Minimum) / (Maximum - Minimum) * Height;

        /// <summary>
        /// Where the axis of categories runs, which is where the value axis reads nought rather
        /// than the edge of the plot.
        /// </summary>
        /// <remarks>
        /// The two are the same thing wherever nothing is negative, since the scale then begins at
        /// nought. Where something is, they part: Word's drawing of the probe's chart of −20 and 60
        /// puts the words under the bars a line below the nought, seven tenths of the way down the
        /// plot, and not at the bottom of it. What hangs below nought hangs past them.
        /// </remarks>
        public double Crossing => PositionOf(Math.Clamp(0, Minimum, Maximum));

        /// <summary>How much room one category has along the axis they run on.</summary>
        public double Slot(int categories) =>
            (Lying ? Height : Width) / Math.Max(1, categories);

        /// <summary>
        /// Where a category's own share of the plot begins — its left edge upright, its top edge
        /// lying down, since a chart on its side puts the first category at the foot and works up.
        /// </summary>
        public double SlotAt(int index, int categories)
        {
            var slot = Slot(categories);

            return Lying ? Bottom - slot * (index + 1) : Left + slot * index;
        }
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
    ///   the side carrying the numbers   makes room for the widest of them and the gap they keep
    ///                                   from the axis, beginning 6.5pt inside the frame
    ///   the side carrying the categories  makes room for the same, where they are ranged against
    ///                                   the axis rather than set under it
    ///   the foot, where words go under it  makes room for a line 1.584 type sizes below the axis
    ///                                   and its descender below that
    ///   the top of an upright chart     leaves half a label's height, so the topmost number does
    ///                                   not overrun the frame
    ///   the right of one lying down     leaves half a label's width, for the same reason: the
    ///                                   last number along the foot is centred on the plot's corner
    ///
    /// and every side falls back to the bare margin where the labels it would make room for are
    /// not drawn at all. A category label wider than its bars is left to overrun, which is what
    /// Word does with one.
    ///
    /// Where the plot goes and what the axis runs between are not two questions but one, since the
    /// scale decides how wide the numbers are and the numbers decide how long the axis is. Word
    /// settles it by going round: the axis is first measured as if nothing had to fit beside it,
    /// and then again with the labels that gave. Two rounds settle every chart measured here —
    /// chart-bar-stacked's second page begins at fives, is placed, and comes back as tens.
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
        var lying = chart.Lying;
        var size = chart.ValueAxis?.LabelSizePoints ?? 10;

        if (chart.PlotArea is { } stated)
        {
            var box = (Left: stated.X * width, Top: stated.Y * height,
                Width: stated.Width * width, Height: stated.Height * height);

            var (minimum, maximum, unit) = Scale(chart, lying ? box.Width : box.Height, size);

            return new Plan(box.Left, box.Top, box.Width, box.Height, minimum, maximum, unit, lying);
        }

        // Nothing is known about the axis yet, so it is first measured as long as the plot could
        // possibly be, and then again against the room the labels it earns leave it.
        var length = Math.Max(1, (lying ? width : height) - 2 * BareMargin);
        var plan = new Plan(0, 0, width, height, 0, 1, 1, lying);

        for (var round = 0; round < 4; round++)
        {
            plan = Place(chart, width, height, Scale(chart, length, size), measure, labelHeight);

            var settled = lying ? plan.Width : plan.Height;
            if (Math.Abs(settled - length) < 0.001) break;

            length = settled;
        }

        return plan;
    }

    /// <summary>Puts the plot area inside the room its labels leave it.</summary>
    private static Plan Place(
        ChartDefinition chart, double width, double height,
        (double Minimum, double Maximum, double Unit) scale,
        Func<string, double, double> measure,
        Func<double, (double Ascent, double Descent)> labelHeight)
    {
        var lying = chart.Lying;
        var whole = new Plan(0, 0, width, height, scale.Minimum, scale.Maximum, scale.Unit, lying);

        var left = BareMargin;
        var top = BareMargin;
        var right = BareMargin;
        var bottom = BareMargin;

        if (chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" } valueAxis)
        {
            var size = valueAxis.LabelSizePoints;

            var widest = Marks(whole)
                .Select(value => measure(Format(value, valueAxis.NumberFormat), size))
                .DefaultIfEmpty(0)
                .Max();

            if (lying)
            {
                var (_, descent) = labelHeight(size);

                bottom = Math.Max(bottom, LabelMargin + size * CategoryLabelBaseline + descent);
                right = Math.Max(right, BareMargin + widest / 2);
            }
            else
            {
                var (ascent, descent) = labelHeight(size);

                left = Math.Max(left, LabelMargin + widest + size * ValueLabelGap);
                top = Math.Max(top, TopMargin + (ascent + descent) / 2);
            }
        }

        if (chart.CategoryAxis is { Deleted: false, TickLabelPosition: not "none" } categoryAxis)
        {
            var size = categoryAxis.LabelSizePoints;

            if (lying)
            {
                var widest = chart.Categories
                    .Select(category => measure(category, size))
                    .DefaultIfEmpty(0)
                    .Max();

                left = Math.Max(left, LabelMargin + widest + size * ValueLabelGap);
            }
            else
            {
                var (_, descent) = labelHeight(size);

                bottom = Math.Max(bottom, LabelMargin + size * CategoryLabelBaseline + descent);
            }
        }

        return new Plan(
            left, top, Math.Max(1, width - left - right), Math.Max(1, height - top - bottom),
            scale.Minimum, scale.Maximum, scale.Unit, lying);
    }

    /// <summary>
    /// How far a label ranged against its axis ends short of it, as a share of its type size, and
    /// how far below the axis one written underneath has its baseline. Both measured at two sizes;
    /// see <see cref="LayoutEngine"/>, which places the labels these leave room for.
    /// </summary>
    /// <remarks>
    /// The first is measured from where the widest label of each axis ends: 9.278pt short at ten
    /// point and 18.547pt at twenty, and the same for the numbers up an upright chart's side as for
    /// the words down a lying one's. The second from where the baseline lands under the axis:
    /// 15.84pt at ten point, and the same 1.584 of the type size at twenty.
    /// </remarks>
    public const double ValueLabelGap = 0.9277;

    public const double CategoryLabelBaseline = 1.584;

    /// <summary>
    /// A number as an axis writes it: whole where it is whole, and in the format the axis asks for
    /// where it asks for one.
    /// </summary>
    /// <remarks>
    /// A format code is a spreadsheet's, and a spreadsheet's format codes are a language of their
    /// own. What is read here is what a chart axis actually carries: how many decimal places to
    /// keep, whether to group the thousands, and whether the number is a percentage — which is the
    /// one that matters, since a chart stacked to the whole is written in hundredths and read in
    /// per cents. Anything else falls back to writing the number plainly.
    /// </remarks>
    public static string Format(double value, string? code = null)
    {
        if (code is null || code.Length == 0 || code == "General") return Plain(value);

        // Only the first section is used: an axis writes one kind of number, not one kind for the
        // positives and another for the negatives.
        var pattern = code.Split(';')[0];

        var percent = pattern.Contains('%');
        var grouped = pattern.Contains(",#") || pattern.Contains(",0");

        var point = pattern.IndexOf('.');
        var places = 0;
        for (var i = point + 1; point >= 0 && i < pattern.Length && pattern[i] is '0' or '#'; i++)
            places++;

        var number = percent ? value * 100 : value;

        var written = number.ToString(
            (grouped ? "#,##0" : "0") + (places > 0 ? "." + new string('0', places) : string.Empty),
            System.Globalization.CultureInfo.InvariantCulture);

        return percent ? written + "%" : written;
    }

    private static string Plain(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// How much room one label wants along the axis it is written against, as a share of the type
    /// it is set in: a tenth over its own size where the axis stands upright, and three times it
    /// where the axis lies down and the numbers are written end to end.
    /// </summary>
    /// <remarks>
    /// Both measured from chart-scale-probe and chart-bar-scale-probe together, twenty-six charts
    /// between them. The numbers only bound the two: an upright axis takes anything from 1.05 to
    /// 1.145 times the type size, a lying one anything from 2.88 to 3.15, and the values here are
    /// the middle of each. What the measurements do settle is that both grow with the type — the
    /// same numbers set in twenty point divide an axis into a third as many steps as in ten — and
    /// that neither has anything to do with how wide the numbers themselves are, since a chart of
    /// millions divides its foot exactly as a chart of tens does.
    /// </remarks>
    private const double UprightLabelRoom = 1.1;

    private const double LyingLabelRoom = 3;

    /// <summary>
    /// The most major intervals an axis of a given length will carry: as many as leaves room for
    /// one more label than there are intervals, since a mark is written at both ends as well as
    /// between.
    /// </summary>
    private static int Intervals(double axisLength, bool lying, double labelSize)
    {
        var room = Math.Max(1, labelSize) * (lying ? LyingLabelRoom : UprightLabelRoom);

        return Math.Max(1, (int)Math.Floor(axisLength / room) - 1);
    }

    /// <summary>
    /// What the value axis runs between and how far apart its marks are, where the chart does not
    /// say.
    /// </summary>
    /// <remarks>
    /// Measured from chart-scale-probe and chart-bar-scale-probe, twenty-six charts differing in
    /// the numbers they hold, how long the axis is, which way it runs and what size its labels are.
    /// One rule accounts for every one of them:
    ///
    ///   the step is the smallest of one, two or five times a power of ten for which the axis —
    ///   running from the largest step at or below the least value to the smallest step strictly
    ///   above the greatest — has no more intervals than the axis has room to label
    ///
    /// so a chart of 7 up a 126pt side runs to 8 in ones, one of 9.5 runs to 10 in ones, one of 10
    /// runs to 12 in twos, one of 47 runs to 50 in fives, one of 105 runs to 120 in twenties, and
    /// one of 0.4 runs to 0.45 in twentieths. That the top is strictly above rather than at the
    /// largest value is what puts a chart of exactly 100 at 120 rather than leaving its tallest bar
    /// touching the frame. The same 47 laid on its side across the same room runs to 60 in twenties
    /// instead, because a number written along an axis wants far more room than one written up it.
    ///
    /// The foot is nought wherever nothing is negative, whatever the smallest value is — a chart of
    /// 30 and 55 still starts at nought. Where something is negative the foot steps below it the
    /// same way the top steps above: a chart of −20 and 60 runs from −30 to 70 in tens.
    ///
    /// Two things are measured rather than the values themselves. A stacked chart is scaled by what
    /// its categories come to rather than by what any one bar holds, so its axis has to reach the
    /// tallest pile; and one stacked to the whole runs to exactly one, which is the single place
    /// the top is not a step above what it holds.
    /// </remarks>
    private static (double Minimum, double Maximum, double Unit) Scale(
        ChartDefinition chart, double axisLength, double labelSize)
    {
        var percent = chart.Grouping == ChartGrouping.PercentStacked;

        var values = Totals(chart);

        var highest = values.Count > 0 ? Math.Max(0, values.Max()) : 0;
        var lowest = values.Count > 0 ? Math.Min(0, values.Min()) : 0;

        if (percent)
        {
            highest = 1;
            lowest = lowest < 0 ? -1 : 0;
        }

        var axis = chart.ValueAxis;
        var most = Intervals(axisLength, chart.Lying, labelSize);

        var unit = axis?.MajorUnit ?? 0;
        var (minimum, maximum) = (0.0, 0.0);

        if (unit > 0)
        {
            (minimum, maximum) = Bounds(axis, lowest, highest, unit, percent);
        }
        else
        {
            foreach (var candidate in Steps(highest - lowest))
            {
                unit = candidate;
                (minimum, maximum) = Bounds(axis, lowest, highest, candidate, percent);

                if ((maximum - minimum) / candidate <= most + 0.000001) break;
            }
        }

        if (maximum <= minimum) maximum = minimum + Math.Max(unit, 1);

        return (minimum, maximum, unit <= 0 ? maximum - minimum : unit);
    }

    /// <summary>
    /// What a chart's axis has to reach: every value it holds, or — where the bars are stacked —
    /// what each category piles up to, above and below the axis kept apart.
    /// </summary>
    private static List<double> Totals(ChartDefinition chart)
    {
        if (chart.Grouping is not (ChartGrouping.Stacked or ChartGrouping.PercentStacked))
        {
            return
            [
                .. chart.Series
                    .SelectMany(series => series.Values)
                    .Where(value => value.HasValue)
                    .Select(value => value!.Value)
            ];
        }

        var totals = new List<double>();

        for (var category = 0; category < chart.Categories.Count; category++)
        {
            double above = 0, below = 0;

            foreach (var series in chart.Series)
            {
                if (category >= series.Values.Count || series.Values[category] is not { } value)
                    continue;

                if (value >= 0) above += value;
                else below += value;
            }

            totals.Add(above);
            totals.Add(below);
        }

        return totals;
    }

    /// <summary>Where the axis begins and ends, once the step is known.</summary>
    private static (double Minimum, double Maximum) Bounds(
        ChartAxis? axis, double lowest, double highest, double unit, bool percent)
    {
        var maximum = axis?.Maximum ?? (percent ? highest : Above(highest, unit));

        var minimum = axis?.Minimum
                      ?? (lowest < 0 ? percent ? lowest : -Above(-lowest, unit) : 0);

        return (minimum, maximum);
    }

    /// <summary>
    /// Every step an axis might take, from far too small to far too large: one, two and five times
    /// each power of ten in turn.
    /// </summary>
    private static IEnumerable<double> Steps(double span)
    {
        if (span <= 0 || double.IsNaN(span) || double.IsInfinity(span))
        {
            yield return 1;
            yield break;
        }

        var power = Math.Pow(10, Math.Floor(Math.Log10(span)) - 3);

        for (var decade = 0; decade < 8; decade++)
        {
            yield return power;
            yield return power * 2;
            yield return power * 5;

            power *= 10;
        }
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

        var crossing = plan.Crossing;

        if (chart.ValueAxis is { Deleted: false, MajorGridlines: true })
        {
            foreach (var value in Marks(plan))
            {
                var at = plan.PositionOf(value);

                // The mark the categories run along carries the axis itself and is drawn as one.
                // On a chart of nothing but positives that is the foot of the scale; on one that
                // dips below nought it is the nought, and the mark at the foot is drawn.
                if (Math.Abs(at - crossing) < 0.001) continue;

                operations.Add(plan.Lying
                    ? Stroke([(at, plan.Top), (at, plan.Bottom)])
                    : Stroke([(plan.Left, at), (plan.Right, at)]));
            }
        }

        foreach (var bar in Bars(chart, plan))
        {
            // A bar hanging below nought is drawn the other way about unless the series says not
            // to: white, and outlined instead of filled. Measured from chart-bar-stacked's last
            // page, where Word draws the one negative bar white with a black outline of 9525 EMU,
            // which is three quarters of a point — and draws it so even though the series it
            // belongs to asks for no outline at all.
            operations.Add(bar.Inverted
                ? new PathOperation(Rectangle(bar.X, bar.Y, bar.Width, bar.Height),
                    new DrawingColor(255, 255, 255), new DrawingColor(0, 0, 0), InvertedOutline,
                    EvenOdd: false)
                : new PathOperation(Rectangle(bar.X, bar.Y, bar.Width, bar.Height),
                    Resolve(bar.Fill, theme), null, LineWidth, EvenOdd: false));
        }

        if (chart.Kind == ChartKind.Line) operations.AddRange(Lines(chart, plan, theme));

        // The value axis runs along the edge of the plot the numbers are written against, whatever
        // the categories do: up the left of an upright chart, along the foot of one lying down.
        if (chart.ValueAxis is { Deleted: false } valueAxis)
        {
            operations.Add(plan.Lying
                ? Stroke([(plan.Left, plan.Bottom), (plan.Right, plan.Bottom)])
                : Stroke([(plan.Left, plan.Top), (plan.Left, plan.Bottom)]));

            if (valueAxis.MajorTickMark is not "none")
            {
                foreach (var value in Marks(plan))
                {
                    var at = plan.PositionOf(value);

                    operations.Add(plan.Lying
                        ? Stroke([(at, plan.Bottom), (at, plan.Bottom + TickLength)])
                        : Stroke([(plan.Left - TickLength, at), (plan.Left, at)]));
                }
            }
        }

        // The category axis crosses the value one at its nought, and moves with it.
        if (chart.CategoryAxis is { Deleted: false } categoryAxis)
        {
            operations.Add(plan.Lying
                ? Stroke([(crossing, plan.Top), (crossing, plan.Bottom)])
                : Stroke([(plan.Left, crossing), (plan.Right, crossing)]));

            if (categoryAxis.MajorTickMark is not "none")
            {
                // A category axis marks the boundaries between its categories rather than their
                // middles, so a chart of four categories carries five marks.
                var categories = Math.Max(1, chart.Categories.Count);

                for (var i = 0; i <= categories; i++)
                {
                    if (plan.Lying)
                    {
                        var y = plan.Top + plan.Height * i / categories;
                        operations.Add(Stroke([(crossing - TickLength, y), (crossing, y)]));
                    }
                    else
                    {
                        var x = plan.Left + plan.Width * i / categories;
                        operations.Add(Stroke([(x, crossing), (x, crossing + TickLength)]));
                    }
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
    /// 63pt each and a bar of 25.2pt, which is 63 ÷ 2.5 exactly. Bars that overlap take the same
    /// arithmetic — stacking is nothing but an overlap of a hundred, which is why a stacked chart
    /// of two series across 78pt gives a bar of 31.2pt, being 78 ÷ 2.5 again.
    ///
    /// Where they go differs between the two kinds of chart in a way the format never says. A
    /// chart lying down puts its first category at the foot and works upwards, and within a
    /// category puts its first series at the foot too — so both run backwards against an upright
    /// chart's left-to-right. Measured from chart-bar-stacked: the first category's bar is in the
    /// bottom third of every one of its lying pages, and on the page of two clustered series the
    /// second series is the upper of the pair.
    ///
    /// Stacked bars begin where the last one ended rather than at the axis, with what rises above
    /// nought and what hangs below it piled separately. Stacked to the whole, each is first taken
    /// as its share of what its own category comes to.
    /// </remarks>
    public static IEnumerable<(double X, double Y, double Width, double Height,
            DrawingColorReference? Fill, bool Inverted)>
        Bars(ChartDefinition chart, Plan plan)
    {
        if (chart.Kind is not (ChartKind.Column or ChartKind.Bar)) yield break;

        var categories = Math.Max(1, chart.Categories.Count);
        var slot = plan.Slot(categories);

        var series = Math.Max(1, chart.Series.Count);
        var overlap = chart.Overlap / 100.0;

        var stacked = chart.Grouping is ChartGrouping.Stacked or ChartGrouping.PercentStacked;
        var percent = chart.Grouping == ChartGrouping.PercentStacked;

        // The bars of one category sit side by side, less however far they overlap.
        var barWidth = slot / (series - (series - 1) * overlap + chart.GapWidth / 100.0);
        if (barWidth <= 0) yield break;

        for (var category = 0; category < categories; category++)
        {
            var group = series * barWidth - (series - 1) * barWidth * overlap;
            var start = plan.SlotAt(category, categories) + (slot - group) / 2;

            var whole = percent ? Whole(chart, category) : 1;
            if (whole <= 0) continue;

            // What the category has piled up to so far, on each side of the axis.
            double above = 0, below = 0;

            for (var index = 0; index < chart.Series.Count; index++)
            {
                var values = chart.Series[index].Values;
                if (category >= values.Count || values[category] is not { } value) continue;

                var height = percent ? value / whole : value;

                double from, to;

                if (stacked)
                {
                    if (height >= 0) (from, to, above) = (above, above + height, above + height);
                    else (from, to, below) = (below, below + height, below + height);
                }
                else
                {
                    (from, to) = (0, height);
                }

                var one = Along(plan, from);
                var other = Along(plan, to);

                var near = Math.Min(one, other);
                var span = Math.Abs(other - one);

                // Within a category the series run one way upright and the other lying down.
                var place = start + (plan.Lying ? series - 1 - index : index)
                    * barWidth * (1 - overlap);

                var inverted = height < 0 && chart.Series[index].InvertIfNegative;

                yield return plan.Lying
                    ? (near, place, span, barWidth, chart.Series[index].Fill, inverted)
                    : (place, near, barWidth, span, chart.Series[index].Fill, inverted);
            }
        }
    }

    /// <summary>Where a value falls along the axis, kept inside the plot.</summary>
    private static double Along(Plan plan, double value) =>
        plan.PositionOf(Math.Clamp(value, plan.Minimum, plan.Maximum));

    /// <summary>What one category of a chart stacked to the whole comes to.</summary>
    private static double Whole(ChartDefinition chart, int category)
    {
        double total = 0;

        foreach (var series in chart.Series)
        {
            if (category >= series.Values.Count || series.Values[category] is not { } value)
                continue;

            total += Math.Abs(value);
        }

        return total;
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
