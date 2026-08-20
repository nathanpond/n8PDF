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
    /// <param name="AcrossUnit">
    /// How far apart the marks along the foot go, where the foot is a scale of its own rather than
    /// a run of categories. Nought for every chart but a scatter.
    /// </param>
    public sealed record Plan(
        double Left, double Top, double Width, double Height,
        double Minimum, double Maximum, double MajorUnit, bool Lying = false,
        double AcrossMinimum = 0, double AcrossMaximum = 0, double AcrossUnit = 0)
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

        /// <summary>
        /// True where the foot is a scale of its own: a chart of pairs, which has to be positioned
        /// both ways rather than one way and by category the other.
        /// </summary>
        public bool Paired => AcrossUnit > 0;

        /// <summary>Where a value sits along the foot, which only a chart of pairs has.</summary>
        public double AcrossOf(double value) =>
            Left + (value - AcrossMinimum) / (AcrossMaximum - AcrossMinimum) * Width;

        /// <summary>Where the axis up the side stands, which is where the foot reads nought.</summary>
        public double AcrossCrossing => AcrossOf(Math.Clamp(0, AcrossMinimum, AcrossMaximum));

        /// <summary>
        /// Where a category's point falls along the plot: at the middle of its own share where the
        /// axes cross between the categories, and at the mark itself where they cross at one — so
        /// that the first and last points touch the ends of the plot.
        /// </summary>
        public double PointAt(int index, int count, bool spanning) =>
            spanning
                ? Left + (count <= 1 ? Width / 2 : Width * index / (count - 1))
                : Left + Width * (index + 0.5) / Math.Max(1, count);

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
    /// The room a title takes over and above the text itself, and how far its own box sits from
    /// the edge of the frame.
    /// </summary>
    /// <remarks>
    /// Measured from chart-title-legend-label: a title of ten point takes 20.076pt off the top of
    /// the plot, one of thirty takes 42.226, and the face those two are set in makes a line of
    /// 1.1074 ems — so the room is nine points and a line, whatever the line comes to. The two
    /// pads are measured once each, at eighteen point: a chart's own title begins 7.35pt below the
    /// top of the frame, and an axis title ends 12.5pt inside the edge it belongs to, whichever
    /// edge that is.
    /// </remarks>
    private const double TitleGap = 9.0;

    public const double TitleTop = 7.43;

    public const double AxisTitleEdge = 12.5;

    /// <summary>
    /// How much of the frame a title wraps into. Word breaks the seventeenth page's title after
    /// "every", which puts the width it wrapped to somewhere between 243pt and 293pt of a 360pt
    /// frame; four fifths is the middle of that and is what is used.
    /// </summary>
    public const double TitleWidth = 0.75;

    /// <summary>
    /// What a legend takes, and how its entries are set out. All measured from the same fixture,
    /// at ten point and twenty: a legend along the top or foot takes 11.8pt and a line, and one up
    /// a side takes 15.118pt and the widest entry it holds. A key is 0.5492 of the type size
    /// square, and the words begin 0.7863 of it from the key's own left edge.
    /// </summary>
    private const double LegendGap = 11.8;

    private const double LegendSide = 15.118;

    private const double LegendSwatch = 0.5492;

    private const double LegendSwatchGap = 0.8239;

    private const double LegendSwatchGapFixed = -0.376;

    /// <summary>
    /// How far apart the entries of a legend along the foot are set, over and above their own
    /// widths — 0.784 of the type size, except where an entry is long enough that a seventh of it
    /// is more, which is what the four-series page shows and what nothing here explains.
    /// </summary>
    private const double LegendEntryGap = 0.784;

    private const double LegendLongEntry = 0.1464;

    /// <summary>
    /// Where a legend along the foot sits: its baseline this far above the foot of the frame, and
    /// the whole of it this far to the right of the middle. Both measured at two sizes.
    /// </summary>
    private const double LegendBaseline = 8.64;

    private const double LegendBaselineGrowth = 0.36;

    private const double LegendShift = 0.137;

    private const double LegendShiftFixed = 0.57;

    /// <summary>
    /// How far below the top of the frame a legend along the top begins, over and above its own
    /// ascent. Measured once, at ten point.
    /// </summary>
    private const double LegendTop = 8.24;

    /// <summary>
    /// How far a key sits below the baseline of the words beside it, and how far a legend up the
    /// right of a chart ends inside the frame.
    /// </summary>
    private const double LegendKeyDrop = 0.2446;

    private const double LegendKeyDropFixed = 0.356;

    private const double LegendEdge = 10.12;

    /// <summary>How far apart the entries of a legend up a side are, as a share of the type.</summary>
    private const double LegendPitch = 1.8083;

    /// <summary>
    /// How far clear of the end of a bar a number written at it sits, and how far to the right of
    /// a point on a line. The first is measured at ten point and twenty — 7.15pt and 10.04pt clear
    /// of the bar, which is the label's own descender and four and a half points besides.
    /// </summary>
    private const double LabelGap = 4.5;

    private const double LabelSide = 8.5;

    /// <summary>How far inside the frame a number that would overrun it is set instead.</summary>
    private const double LabelClamp = 1.36;

    /// <summary>
    /// How far out from the middle of a pie a share written on it sits, as part of the radius.
    /// Fitted to the four slices of the twelfth page, which give 0.684 to 0.712.
    /// </summary>
    private const double PieLabelRadius = 0.69;

    /// <summary>
    /// Where the things round the plotting go: what each takes from the plot, and where each is
    /// then drawn.
    /// </summary>
    /// <param name="Title">The room the chart's own title takes off the top.</param>
    public readonly record struct Dressing(
        double Title, double AxisTitleLeft, double AxisTitleBottom,
        double LegendLeft, double LegendRight, double LegendTop, double LegendBottom)
    {
        public double Left => AxisTitleLeft + LegendLeft;

        public double Right => LegendRight;

        public double Top => Title + LegendTop;

        public double Bottom => AxisTitleBottom + LegendBottom;
    }

    /// <summary>
    /// Works out how much room the title, the axis titles and the legend take from the plotting.
    /// </summary>
    public static Dressing Room(
        ChartDefinition chart, double width, double height,
        Func<string, double, double> measure,
        Func<double, (double Ascent, double Descent)> labelHeight,
        Func<IReadOnlyList<BlockElement>, double, (double Width, double Height)> text)
    {
        var title = chart.Title is { Overlay: false } head
            ? TitleGap + text(head.Paragraphs, width * TitleWidth).Height
            : 0;

        var acrossTitle = chart.CategoryAxis?.Title is { Overlay: false } across
            ? TitleGap + text(across.Paragraphs, width * TitleWidth).Height
            : 0;

        var upTitle = chart.ValueAxis?.Title is { Overlay: false } up
            ? TitleGap + text(up.Paragraphs, height * TitleWidth).Height
            : 0;

        var (left, right, top, bottom) = (0.0, 0.0, 0.0, 0.0);

        if (chart.Legend is { Overlay: false } legend)
        {
            var size = legend.LabelSizePoints;
            var (ascent, descent) = labelHeight(size);

            var room = legend.Position switch
            {
                "l" or "r" => LegendSide + Entries(chart, legend, measure).Max(entry => entry.Width),
                _ => LegendGap + ascent + descent
            };

            switch (legend.Position)
            {
                case "l": left = room; break;
                case "t": top = room; break;
                case "b": bottom = room; break;
                default: right = room; break;
            }
        }

        // A chart lying down keeps its categories up the side, so its axis titles swap over too.
        return chart.Lying
            ? new Dressing(title, acrossTitle, upTitle, left, right, top, bottom)
            : new Dressing(title, upTitle, acrossTitle, left, right, top, bottom);
    }

    /// <summary>A legend's keys, which are the only part of it that is not words.</summary>
    private static IEnumerable<DrawingOperation> Keys(
        ChartDefinition chart, double width, double height,
        Func<string, double, double> measure,
        Func<double, (double Ascent, double Descent)> labelHeight,
        double titleRoom, DocumentTheme theme)
    {
        foreach (var entry in Legend(chart, width, height, measure, labelHeight, titleRoom))
        {
            yield return new PathOperation(
                Rectangle(entry.SwatchX, entry.SwatchY, entry.Swatch, entry.Swatch),
                Resolve(entry.Fill, theme), null, LineWidth, EvenOdd: false);
        }
    }

    /// <summary>One entry of a legend: a key in the series' colour, and the series' name.</summary>
    public readonly record struct LegendEntry(
        string Text, double Width, DrawingColorReference? Fill);

    /// <summary>What a legend holds, in the order the chart lists its series.</summary>
    /// <remarks>
    /// A pie names its slices rather than its series, since a pie is one series divided between
    /// its categories and naming it once would say nothing.
    /// </remarks>
    public static IReadOnlyList<LegendEntry> Entries(
        ChartDefinition chart, ChartLegend legend, Func<string, double, double> measure)
    {
        var size = legend.LabelSizePoints;
        var head = size * LegendSwatchGap + LegendSwatchGapFixed;

        if (chart.Kind == ChartKind.Pie && chart.Series.Count > 0)
        {
            var pie = chart.Series[0];

            return
            [
                .. chart.Categories.Select((name, i) => new LegendEntry(
                    name, head + measure(name, size),
                    pie.PointFills.TryGetValue(i, out var point) ? point : pie.Fill))
            ];
        }

        return
        [
            .. chart.Series.Select(series => new LegendEntry(
                series.Name, head + measure(series.Name, size),
                series.Fill ?? series.Line))
        ];
    }

    /// <summary>Where each entry of a legend is drawn: its key, and where its words begin.</summary>
    public readonly record struct PlacedEntry(
        string Text, double SwatchX, double SwatchY, double Swatch, double TextX, double Baseline,
        DrawingColorReference? Fill);

    public static IReadOnlyList<PlacedEntry> Legend(
        ChartDefinition chart, double width, double height,
        Func<string, double, double> measure,
        Func<double, (double Ascent, double Descent)> labelHeight,
        double titleRoom = 0)
    {
        if (chart.Legend is not { Overlay: false } legend) return [];

        var entries = Entries(chart, legend, measure);
        if (entries.Count == 0) return [];

        var size = legend.LabelSizePoints;
        var swatch = size * LegendSwatch;
        var head = size * LegendSwatchGap + LegendSwatchGapFixed;

        var placed = new List<PlacedEntry>();

        if (legend.Position is "l" or "r")
        {
            // Up a side: one entry to a line, the whole block centred on the middle of the frame,
            // and the words ending a bare margin inside the edge they are set against.
            var widest = entries.Max(entry => entry.Width);
            var pitch = size * LegendPitch;

            // Up the left it begins a bare margin inside the frame; up the right it ends 10.12pt
            // inside it, which is what the fifth page measures.
            var left = legend.Position == "l"
                ? BareMargin
                : width - LegendEdge - widest;

            var middle = height / 2;
            var top = middle - pitch * entries.Count / 2;

            for (var i = 0; i < entries.Count; i++)
            {
                var centre = top + pitch * i + pitch / 2;

                placed.Add(new PlacedEntry(entries[i].Text,
                    left, centre - swatch / 2, swatch, left + head,
                    centre + size * LegendKeyDrop + LegendKeyDropFixed, entries[i].Fill));
            }

            return placed;
        }

        // Along the foot or the top: side by side, the block centred a little right of the middle.
        var widestEntry = entries.Max(entry => entry.Width);
        var gap = Math.Max(size * LegendEntryGap, widestEntry * LegendLongEntry);

        var content = entries.Sum(entry => entry.Width) + gap * (entries.Count - 1);
        var start = width / 2 - content / 2 + size * LegendShift + LegendShiftFixed;

        // Along the foot it is set by how far its baseline sits above the foot of the frame, and
        // along the top by how far below the top — under whatever title is up there already.
        var baseline = legend.Position == "t"
            ? titleRoom + LegendTop + labelHeight(size).Ascent
            : height - LegendBaseline - size * LegendBaselineGrowth;

        var x = start;

        foreach (var entry in entries)
        {
            placed.Add(new PlacedEntry(entry.Text,
                x, baseline - size * LegendKeyDrop - LegendKeyDropFixed - swatch / 2, swatch,
                x + head, baseline, entry.Fill));

            x += entry.Width + gap;
        }

        return placed;
    }

    /// <summary>One number written at a point, and where it goes.</summary>
    /// <param name="Centred">
    /// True where the point is the middle of the words, false where it is where they begin.
    /// </param>
    public readonly record struct PlacedLabel(string Text, double X, double Baseline, bool Centred);

    /// <summary>
    /// What is written at each point of a chart that asks for it, and where each goes.
    /// </summary>
    /// <remarks>
    /// Measured from chart-title-legend-label. A number written past the end of a bar clears it by
    /// four and a half points and its own descender, and one written inside the end clears it the
    /// same way with its ascender instead — 7.15pt and 14.09pt at ten point, 10.04pt at twenty,
    /// which is the same four and a half both times. One written at a point on a line goes to its
    /// right, its words beginning 8.5pt past the point and set about it. One written on a slice of
    /// a pie goes out along the middle of the slice, about seven tenths of the way to the rim.
    /// </remarks>
    public static IEnumerable<PlacedLabel> DataLabels(
        ChartDefinition chart, Plan plan,
        Func<double, (double Ascent, double Descent)> labelHeight)
    {
        var categories = Math.Max(1, chart.Categories.Count);

        if (chart.Kind == ChartKind.Pie)
        {
            var series = chart.Series.FirstOrDefault();
            var labels = series?.Labels ?? chart.Labels;

            if (series is null || labels is not { Any: true }) yield break;

            var values = series.Values.Select(value => Math.Max(0, value ?? 0)).ToList();
            var total = values.Sum();
            if (total <= 0) yield break;

            var centre = (X: plan.Left + plan.Width / 2, Y: plan.Top + plan.Height / 2);
            var radius = Math.Min(plan.Width, plan.Height) / 2 * PieLabelRadius;

            var (ascent, descent) = labelHeight(labels.SizePoints);
            var angle = chart.FirstSliceAngle * Math.PI / 180;

            for (var i = 0; i < values.Count; i++)
            {
                var sweep = values[i] / total * 2 * Math.PI;
                var middle = angle + sweep / 2;

                angle += sweep;
                if (values[i] <= 0) continue;

                yield return new PlacedLabel(
                    Written(labels, values[i], values[i] / total),
                    centre.X + radius * Math.Sin(middle),
                    centre.Y - radius * Math.Cos(middle) + (ascent - descent) / 2,
                    Centred: true);
            }

            yield break;
        }

        if (chart.Kind is ChartKind.Column or ChartKind.Bar)
        {
            foreach (var bar in Bars(chart, plan))
            {
                if (bar.Labels is not { Any: true } labels) continue;

                var (ascent, descent) = labelHeight(labels.SizePoints);
                var inside = labels.Position is "inEnd" or "ctr" or "inBase";

                if (plan.Lying)
                {
                    var end = bar.Value >= 0 ? bar.X + bar.Width : bar.X;

                    yield return new PlacedLabel(
                        Written(labels, bar.Value, 0),
                        inside ? bar.X + bar.Width - LabelGap : end + LabelGap,
                        bar.Y + bar.Height / 2 + (ascent - descent) / 2,
                        Centred: false);
                }
                else
                {
                    var end = bar.Value >= 0 ? bar.Y : bar.Y + bar.Height;

                    var baseline = inside
                        ? end + LabelGap + ascent
                        : end - LabelGap - descent;

                    // What would overrun the top of the chart is set against it instead, a shade
                    // inside: measured from the tallest bar of the page labelled at twenty point,
                    // which Word sets 1.36pt further down than the frame alone would ask.
                    yield return new PlacedLabel(
                        Written(labels, bar.Value, 0),
                        bar.X + bar.Width / 2,
                        Math.Max(baseline, ascent + LabelClamp),
                        Centred: true);
                }
            }

            yield break;
        }

        // A line or a chart of pairs writes its numbers beside their own points.
        foreach (var series in chart.Series)
        {
            if ((series.Labels ?? chart.Labels) is not { Any: true } labels) continue;

            var (ascent, descent) = labelHeight(labels.SizePoints);
            var points = Points(chart, series, plan);

            var shown = series.Values.Where(value => value.HasValue).Select(value => value!.Value)
                .ToList();

            for (var i = 0; i < points.Count && i < shown.Count; i++)
            {
                yield return new PlacedLabel(
                    Written(labels, shown[i], 0),
                    points[i].X + LabelSide,
                    points[i].Y + (ascent - descent) / 2,
                    Centred: false);
            }
        }
    }

    /// <summary>What a label says: the number, its share, or both with the category beside it.</summary>
    private static string Written(ChartLabels labels, double value, double share)
    {
        var parts = new List<string>();

        if (labels.Value) parts.Add(Format(value, labels.NumberFormat));
        if (labels.Percent) parts.Add(Format(share, labels.NumberFormat is "General" or null
            ? "0%"
            : labels.NumberFormat));

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Works out where the plotting goes and what the value axis runs between.
    /// </summary>    /// <summary>
    /// Works out where the plotting goes and what the value axis runs between.
    /// </summary>    /// <summary>
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
        Func<double, (double Ascent, double Descent)> labelHeight,
        Func<string, double, double, (double Width, int Lines)>? wrap = null,
        Func<IReadOnlyList<BlockElement>, double, (double Width, double Height)>? block = null)
    {
        wrap ??= (text, size, _) => (measure(text, size), 1);
        block ??= (_, _) => (0, 0);

        var room = Room(chart, width, height, measure, labelHeight, block);

        if (chart.PlotArea is { } stated)
        {
            return Complete(chart,
                (stated.X * width, stated.Y * height, stated.Width * width, stated.Height * height));
        }

        // Nothing is known about the axes yet, so the plot is first taken as large as it could
        // possibly be, and then measured again against the room the labels that gave it leave.
        var box = (Left: BareMargin + room.Left, Top: BareMargin + room.Top,
            Width: Math.Max(1, width - 2 * BareMargin - room.Left - room.Right),
            Height: Math.Max(1, height - 2 * BareMargin - room.Top - room.Bottom));

        for (var round = 0; round < 4; round++)
        {
            var next = Place(chart, width, height, box, measure, labelHeight, wrap, room);

            var settled = Math.Abs(next.Width - box.Width) < 0.001 &&
                          Math.Abs(next.Height - box.Height) < 0.001;

            box = next;
            if (settled) break;
        }

        return Complete(chart, box);
    }

    /// <summary>The plot area with the scales its own size gives it.</summary>
    private static Plan Complete(
        ChartDefinition chart, (double Left, double Top, double Width, double Height) box)
    {
        var lying = chart.Lying;

        var (minimum, maximum, unit) = Scale(chart,
            lying ? box.Width : box.Height, chart.ValueAxis?.LabelSizePoints ?? 10);

        var across = chart.Paired
            ? Across(chart, box.Width, chart.CategoryAxis?.LabelSizePoints ?? 10)
            : (0, 0, 0);

        return new Plan(box.Left, box.Top, box.Width, box.Height, minimum, maximum, unit, lying,
            across.Item1, across.Item2, across.Item3);
    }

    /// <summary>Puts the plot area inside the room its labels leave it.</summary>
    /// <remarks>
    /// Which labels those are depends on which way the chart runs, but what each side does with
    /// them does not. A side carrying labels ranged against it makes room for the widest of them
    /// and the gap they keep; the foot makes room for a line 1.584 type sizes below the axis, and
    /// for every further line a label wraps onto; and wherever the outermost label along the foot
    /// is centred on the plot's own corner — which is what a chart of pairs does, what a chart
    /// crossing at the middle of a category does, and what a chart lying down does — the side it
    /// hangs over makes room for half of it.
    /// </remarks>
    private static (double Left, double Top, double Width, double Height) Place(
        ChartDefinition chart, double width, double height,
        (double Left, double Top, double Width, double Height) box,
        Func<string, double, double> measure,
        Func<double, (double Ascent, double Descent)> labelHeight,
        Func<string, double, double, (double Width, int Lines)> wrap,
        Dressing room)
    {
        var plan = Complete(chart, box);

        var left = BareMargin;
        var top = BareMargin;
        var right = BareMargin;
        var bottom = BareMargin;

        // What is written up the side, ranged against the axis it belongs to.
        if (Ranged(chart, plan) is { Count: > 0 } ranged)
        {
            var size = RangedSize(chart);

            left = Math.Max(left,
                LabelMargin + ranged.Max(label => measure(label, size)) + size * ValueLabelGap);
        }

        // A number at the top of an upright scale is set about its mark, so half of it reaches
        // above the plot; the categories of a chart lying down never do.
        if (!chart.Lying && chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" })
        {
            var size = chart.ValueAxis.LabelSizePoints;
            var (ascent, descent) = labelHeight(size);

            top = Math.Max(top, TopMargin + (ascent + descent) / 2);
        }

        // And what is written under the foot.
        var (foot, cornered, footSize) = UnderFoot(chart, plan);

        if (foot.Count > 0)
        {
            var (_, descent) = labelHeight(footSize);

            // Only words wrap: a number written under an axis takes whatever room it takes, and
            // a category takes its own share of the plot and wraps inside it.
            var slot = chart.Paired || chart.Lying
                ? double.MaxValue / 4
                : box.Width / Math.Max(1, foot.Count);

            var lines = foot.Select(label => wrap(label, footSize, slot)).ToList();

            bottom = Math.Max(bottom,
                LabelMargin + footSize * CategoryLabelBaseline + descent +
                (lines.Max(line => line.Lines) - 1) * footSize * LabelLine);

            if (cornered)
            {
                left = Math.Max(left, BareMargin + lines[0].Width / 2);
                right = Math.Max(right, BareMargin + lines[^1].Width / 2);
            }
        }

        // What goes round the plotting takes its room from the same sides the labels do, and one
        // simply follows the other: the fourteenth page of chart-title-legend-label carries a
        // title, both axis titles, a legend and a number over every bar, and its foot comes to the
        // labels, the axis title and the legend added together to the hundredth of a point.
        return (left + room.Left, top + room.Top,
            Math.Max(1, width - left - right - room.Left - room.Right),
            Math.Max(1, height - top - bottom - room.Top - room.Bottom));
    }

    /// <summary>
    /// How far apart the lines of a label that wraps sit, as a share of its type size: the face as
    /// Windows reads it, which for Calibri is 1.2207 ems, and what Word leaves between the two
    /// lines of the long category on chart-area-scatter's nineteenth page.
    /// </summary>
    private const double LabelLine = 1.2207;

    /// <summary>What is written up the side of a chart, ranged against its axis.</summary>
    private static IReadOnlyList<string> Ranged(ChartDefinition chart, Plan plan)
    {
        if (chart.Lying)
        {
            return chart.CategoryAxis is { Deleted: false, TickLabelPosition: not "none" }
                ? chart.Categories
                : [];
        }

        return chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" } axis
            ? [.. Marks(plan).Select(value => Format(value, axis.NumberFormat))]
            : [];
    }

    private static double RangedSize(ChartDefinition chart) =>
        (chart.Lying ? chart.CategoryAxis?.LabelSizePoints : chart.ValueAxis?.LabelSizePoints) ?? 10;

    /// <summary>
    /// What is written under the foot, whether its outermost labels are centred on the corners of
    /// the plot, and what size they are set in.
    /// </summary>
    private static (IReadOnlyList<string> Labels, bool Cornered, double Size) UnderFoot(
        ChartDefinition chart, Plan plan)
    {
        if (chart.Lying)
        {
            return chart.ValueAxis is { Deleted: false, TickLabelPosition: not "none" } value
                ? ([.. Marks(plan).Select(mark => Format(mark, value.NumberFormat))], true,
                    value.LabelSizePoints)
                : ([], false, 10);
        }

        if (chart.CategoryAxis is not { Deleted: false, TickLabelPosition: not "none" } axis)
            return ([], false, 10);

        return chart.Paired
            ? ([.. MarksAcross(plan).Select(mark => Format(mark, axis.NumberFormat))], true,
                axis.LabelSizePoints)
            : (chart.Categories, Spanning(chart), axis.LabelSizePoints);
    }

    /// <summary>
    /// True where the points of a line or an area sit at the marks rather than between them, which
    /// is what an area chart asks for and what Word writes on every one it makes.
    /// </summary>
    public static bool Spanning(ChartDefinition chart) =>
        chart.ValueAxis?.CrossBetween == "midCat";

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
    /// between — and never more than ten, however long the axis is.
    /// </summary>
    /// <remarks>
    /// The ten is measured from chart-area-scatter, whose fifteenth page holds an axis long enough
    /// for eleven labels and a chart of exactly one, which would run to 1.1 in tenths if it could.
    /// Word runs it to 1.2 in fifths instead. Every other axis measured either runs out of room
    /// first or lands on ten exactly, which is why the twenty-six charts of the scale probes never
    /// showed it.
    /// </remarks>
    private const int MostIntervals = 10;

    private static int Intervals(double axisLength, bool lying, double labelSize)
    {
        var room = Math.Max(1, labelSize) * (lying ? LyingLabelRoom : UprightLabelRoom);

        return Math.Clamp((int)Math.Floor(axisLength / room) - 1, 1, MostIntervals);
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

        return Fit(chart.ValueAxis, lowest, highest,
            Intervals(axisLength, chart.Lying, labelSize), percent);
    }

    /// <summary>
    /// And what the foot of a chart of pairs runs between, which is the same question asked of the
    /// numbers along it — and answered with a lying axis's room, since that is where they are
    /// written.
    /// </summary>
    private static (double Minimum, double Maximum, double Unit) Across(
        ChartDefinition chart, double axisLength, double labelSize)
    {
        var values = chart.Series
            .SelectMany(series => series.XValues)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

        return Fit(chart.CategoryAxis,
            values.Count > 0 ? Math.Min(0, values.Min()) : 0,
            values.Count > 0 ? Math.Max(0, values.Max()) : 0,
            Intervals(axisLength, lying: true, labelSize), percent: false);
    }

    /// <summary>The smallest step that leaves an axis no more marks than it has room for.</summary>
    private static (double Minimum, double Maximum, double Unit) Fit(
        ChartAxis? axis, double lowest, double highest, int most, bool percent)
    {
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
    /// <param name="measure">
    /// How wide a string is, which the keys of a legend need as much as its words do: where each
    /// key goes depends on how wide the name beside it is.
    /// </param>
    public static VectorDrawing Draw(
        ChartDefinition chart, Plan plan, double width, double height, DocumentTheme theme,
        Func<string, double, double>? measure = null,
        Func<double, (double Ascent, double Descent)>? labelHeight = null,
        double titleRoom = 0)
    {
        var Measured = measure ?? ((text, size) => text.Length * size * 0.5);
        var Boxed = labelHeight ?? (size => (size * 0.75, size * 0.25));

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

        // A pie has no axes to draw, and nothing behind it but the frame — but it has a legend
        // like any other chart, and one naming its slices rather than its series.
        if (chart.Kind == ChartKind.Pie)
        {
            operations.AddRange(Slices(chart, plan, theme));
            operations.AddRange(Keys(chart, width, height, Measured, Boxed, titleRoom, theme));

            return new VectorDrawing(width, height, operations);
        }

        var crossing = plan.Crossing;

        // Where the axis up the side stands: at the left of the plot for a chart of categories,
        // and at its own nought for a chart of pairs, whose foot is a scale like any other.
        var standing = plan.Paired ? plan.AcrossCrossing : plan.Left;

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

        // A chart of pairs can be ruled the other way as well, since its foot is a scale too.
        if (plan.Paired && chart.CategoryAxis is { Deleted: false, MajorGridlines: true })
        {
            foreach (var value in MarksAcross(plan))
            {
                var at = plan.AcrossOf(value);
                if (Math.Abs(at - standing) < 0.001) continue;

                operations.Add(Stroke([(at, plan.Top), (at, plan.Bottom)]));
            }
        }

        if (chart.Kind == ChartKind.Area) operations.AddRange(Areas(chart, plan, theme));

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

        // The value axis runs along the edge of the plot the numbers are written against, whatever
        // the categories do: up the left of an upright chart, along the foot of one lying down.
        if (chart.ValueAxis is { Deleted: false } valueAxis)
        {
            operations.Add(plan.Lying
                ? Stroke([(plan.Left, plan.Bottom), (plan.Right, plan.Bottom)])
                : Stroke([(standing, plan.Top), (standing, plan.Bottom)]));

            if (valueAxis.MajorTickMark is not "none")
            {
                foreach (var value in Marks(plan))
                {
                    var at = plan.PositionOf(value);

                    operations.Add(plan.Lying
                        ? Stroke([(at, plan.Bottom), (at, plan.Bottom + TickLength)])
                        : Stroke([(standing - TickLength, at), (standing, at)]));
                }
            }
        }

        // The category axis crosses the value one at its nought, and moves with it.
        if (chart.CategoryAxis is { Deleted: false } categoryAxis)
        {
            operations.Add(plan.Lying
                ? Stroke([(crossing, plan.Top), (crossing, plan.Bottom)])
                : Stroke([(plan.Left, crossing), (plan.Right, crossing)]));

            if (categoryAxis.MajorTickMark is not "none" && plan.Paired)
            {
                // A foot that is a scale of its own is marked where its own numbers fall.
                foreach (var value in MarksAcross(plan))
                {
                    var at = plan.AcrossOf(value);
                    operations.Add(Stroke([(at, crossing), (at, crossing + TickLength)]));
                }
            }
            else if (categoryAxis.MajorTickMark is not "none")
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

        operations.AddRange(Keys(chart, width, height, Measured, Boxed, titleRoom, theme));

        // The lines and what stands at their points go over the axes rather than under them,
        // which is the order Word writes them in.
        if (chart.Kind is ChartKind.Line or ChartKind.Scatter)
        {
            operations.AddRange(Lines(chart, plan, theme));
            operations.AddRange(Markers(chart, plan, theme));
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
            DrawingColorReference? Fill, bool Inverted, double Value, ChartLabels? Labels)>
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
                var labels = chart.Series[index].Labels ?? chart.Labels;

                yield return plan.Lying
                    ? (near, place, span, barWidth, chart.Series[index].Fill, inverted, value, labels)
                    : (place, near, barWidth, span, chart.Series[index].Fill, inverted, value, labels);
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
    /// Where a series' points fall, in the chart's own coordinates.
    /// </summary>
    /// <remarks>
    /// A chart of categories puts them at the middles of the categories, as the bars of a bar
    /// chart stand — or at the marks themselves where the axes cross at the middle of a category
    /// rather than between them, which is what an area chart asks for. Measured from Word's export
    /// of chart-line-pie: the four points of its line land at 175.5, 238.5, 301.5 and 364.5 across
    /// a plot running 144 to 396, which is the middle of each quarter of it; and from the first
    /// page of chart-area-scatter, whose four corners land at 162, 240, 318 and 396 across a plot
    /// running 162 to 396, which is the ends and the thirds.
    ///
    /// A chart of pairs has no categories at all and puts each point where its own two numbers
    /// say, one along each axis.
    /// </remarks>
    public static List<(double X, double Y)> Points(
        ChartDefinition chart, ChartSeries series, Plan plan)
    {
        var points = new List<(double X, double Y)>();

        if (chart.Paired)
        {
            for (var i = 0; i < series.Values.Count; i++)
            {
                if (series.Values[i] is not { } value) continue;
                if (i >= series.XValues.Count || series.XValues[i] is not { } x) continue;

                points.Add((plan.AcrossOf(x), plan.PositionOf(value)));
            }

            return points;
        }

        var categories = Math.Max(1, chart.Categories.Count);
        var spanning = Spanning(chart);

        for (var i = 0; i < series.Values.Count && i < categories; i++)
        {
            if (series.Values[i] is not { } value) continue;

            points.Add((plan.PointAt(i, categories, spanning), plan.PositionOf(value)));
        }

        return points;
    }

    /// <summary>
    /// A line through each series' points, curving through them unless the series says not to,
    /// which is the format's own default.
    /// </summary>
    private static IEnumerable<DrawingOperation> Lines(
        ChartDefinition chart, Plan plan, DocumentTheme theme)
    {
        foreach (var series in chart.Series)
        {
            if (!Drawn(chart, series)) continue;

            var points = Points(chart, series, plan);
            if (points.Count < 2) continue;

            var smooth = chart.Paired
                ? chart.ScatterStyle is "smooth" or "smoothMarker" || series.Smooth
                : series.Smooth;

            yield return new PathOperation(
                smooth ? Curve(points) : Straight(points),
                null, Resolve(series.Line, theme), series.LineWidthPoints, EvenOdd: false);
        }
    }

    /// <summary>
    /// Whether a series joins its points with a line. A chart of pairs draws one only where its
    /// style asks for one and the series has not said outright that it wants none; a line chart
    /// always does.
    /// </summary>
    private static bool Drawn(ChartDefinition chart, ChartSeries series) =>
        !series.NoLine &&
        (!chart.Paired || chart.ScatterStyle is not ("none" or "marker"));

    /// <summary>
    /// Each series filled down to the axis, which is what an area chart is: a line chart with the
    /// space under it coloured in.
    /// </summary>
    /// <remarks>
    /// Measured from chart-area-scatter. The shape runs along the points and back along the axis,
    /// and where the areas are stacked it runs back along the series below it instead, so that
    /// each is a band rather than a shape hiding the ones behind. Unstacked, they are drawn one
    /// over another in the order the chart lists them, opaquely — Word writes no transparency of
    /// its own, so a taller area behind a shorter one is simply hidden by it.
    /// </remarks>
    private static IEnumerable<DrawingOperation> Areas(
        ChartDefinition chart, Plan plan, DocumentTheme theme)
    {
        var categories = Math.Max(1, chart.Categories.Count);
        var spanning = Spanning(chart);

        var stacked = chart.Grouping is ChartGrouping.Stacked or ChartGrouping.PercentStacked;
        var percent = chart.Grouping == ChartGrouping.PercentStacked;

        var below = new double[categories];
        var floor = plan.PositionOf(Math.Clamp(0, plan.Minimum, plan.Maximum));

        foreach (var series in chart.Series)
        {
            var top = new List<(double X, double Y)>();
            var bottom = new List<(double X, double Y)>();

            for (var i = 0; i < categories; i++)
            {
                if (i >= series.Values.Count || series.Values[i] is not { } value) continue;

                var whole = percent ? Whole(chart, i) : 1;
                if (whole <= 0) continue;

                var height = percent ? value / whole : value;

                var x = plan.PointAt(i, categories, spanning);
                var under = stacked ? below[i] : 0;

                top.Add((x, Along(plan, under + height)));
                bottom.Add((x, stacked ? Along(plan, under) : floor));

                if (stacked) below[i] = under + height;
            }

            if (top.Count < 2) continue;

            var steps = new List<PathStep> { new(PathStepKind.Move, [top[0]]) };

            for (var i = 1; i < top.Count; i++) steps.Add(new PathStep(PathStepKind.Line, [top[i]]));
            for (var i = bottom.Count - 1; i >= 0; i--)
                steps.Add(new PathStep(PathStepKind.Line, [bottom[i]]));

            steps.Add(new PathStep(PathStepKind.Close, []));

            yield return new PathOperation(
                steps, Resolve(series.Fill, theme), null, LineWidth, EvenOdd: false);
        }
    }

    /// <summary>
    /// What Word rounds every edge of a marker to: a three-hundredth of an inch.
    /// </summary>
    private const double Quantum = 0.24;

    /// <summary>
    /// The shapes Word runs through for a series that says nothing about its markers, in the order
    /// it runs through them.
    /// </summary>
    /// <remarks>
    /// The first four are measured, from a chart-area-scatter page holding four series and saying
    /// nothing about any of them: a diamond, a square, a triangle and a cross. What comes after is
    /// the order Excel has always used and is not measured here, since a document with five
    /// unstated series to measure it with is not one Word writes.
    /// </remarks>
    private static readonly string[] AutomaticSymbols =
        ["diamond", "square", "triangle", "x", "star", "dot", "dash", "plus", "circle"];

    /// <summary>
    /// A mark at each of a series' points, where the series draws them.
    /// </summary>
    /// <remarks>
    /// Everything about where one goes is measured from chart-area-scatter, which holds markers of
    /// three, five, seven and nine points and two shapes of each. A marker of size s is drawn in a
    /// box of s rounded to the three-hundredth of an inch, whose corner is the point less half
    /// that box rounded <em>down</em> to the same, and the shape itself is drawn half a
    /// three-hundredth inside the box on every side. That accounts for all four sizes exactly: a
    /// marker of seven comes out 6.72 across and up to a third of a point left of and above the
    /// point it belongs to, which is Word's rounding and not a mistake.
    ///
    /// A series saying nothing gets a marker anyway, in the series' own colour and outlined in it
    /// at half a point: seven points across where the series draws a line, and six where it does
    /// not — measured either way, and the only rule here that has no reason behind it.
    /// </remarks>
    private static IEnumerable<DrawingOperation> Markers(
        ChartDefinition chart, Plan plan, DocumentTheme theme)
    {
        // Only a chart of pairs marks its points where nothing asks it to. A line chart that says
        // nothing draws none, which is what Word's own line charts say outright.
        var drawn = chart.Paired && chart.ScatterStyle is not ("none" or "line" or "smooth");

        for (var index = 0; index < chart.Series.Count; index++)
        {
            var series = chart.Series[index];

            var symbol = series.Marker?.Symbol ?? (drawn ? "auto" : "none");
            if (symbol is "none" or "picture") continue;

            if (symbol == "auto")
            {
                if (!drawn) continue;
                symbol = AutomaticSymbols[index % AutomaticSymbols.Length];
            }

            var automatic = series.Marker is null;

            var size = automatic
                ? Drawn(chart, series) ? AutomaticMarker : AutomaticMarker - 1
                : series.Marker!.SizePoints;

            // Word's own charts take a marker's colours from the series where the marker says
            // nothing of its own, which is what a marker left to itself looks like.
            var fill = Resolve(series.Marker?.Fill ?? series.Line ?? series.Fill, theme);
            var stroke = Resolve(series.Marker?.Line ?? series.Line ?? series.Fill, theme);
            var width = automatic ? LineWidth : series.Marker!.LineWidthPoints;

            foreach (var (x, y) in Points(chart, series, plan))
            {
                var box = Math.Round(size / Quantum, MidpointRounding.AwayFromZero) * Quantum;

                var left = Math.Floor((x - box / 2) / Quantum) * Quantum + Quantum / 2;
                var top = Math.Floor((y - box / 2) / Quantum) * Quantum + Quantum / 2;
                var side = box - Quantum;

                yield return Marker(symbol, left, top, side, fill, stroke, width);
            }
        }
    }

    /// <summary>How large a marker is where the series does not say.</summary>
    private const double AutomaticMarker = 7;

    /// <summary>One marker, in the box measured out for it.</summary>
    private static PathOperation Marker(
        string symbol, double left, double top, double side,
        DrawingColor? fill, DrawingColor? stroke, double width)
    {
        var (x, y) = (left + side / 2, top + side / 2);
        var half = side / 2;

        switch (symbol)
        {
            case "square":
                return new PathOperation(Rectangle(left, top, side, side),
                    fill, stroke, width, EvenOdd: false);

            case "circle":
                return new PathOperation(Ellipse(x, y, half), fill, stroke, width, EvenOdd: false);

            // Half the size, which is what a dot is: a marker with nothing but its middle.
            case "dot":
                return new PathOperation(Ellipse(x, y, half / 2), fill, stroke, width,
                    EvenOdd: false);

            case "triangle":
                return new PathOperation(
                [
                    new PathStep(PathStepKind.Move, [(x, top)]),
                    new PathStep(PathStepKind.Line, [(left + side, top + side)]),
                    new PathStep(PathStepKind.Line, [(left, top + side)]),
                    new PathStep(PathStepKind.Close, [])
                ], fill, stroke, width, EvenOdd: false);

            // The crossing kinds are lines rather than shapes, so they are stroked and not filled.
            case "x":
                return new PathOperation(
                [
                    new PathStep(PathStepKind.Move, [(left, top)]),
                    new PathStep(PathStepKind.Line, [(left + side, top + side)]),
                    new PathStep(PathStepKind.Move, [(left + side, top)]),
                    new PathStep(PathStepKind.Line, [(left, top + side)])
                ], null, stroke, width, EvenOdd: false);

            case "plus":
                return new PathOperation(
                [
                    new PathStep(PathStepKind.Move, [(x, top)]),
                    new PathStep(PathStepKind.Line, [(x, top + side)]),
                    new PathStep(PathStepKind.Move, [(left, y)]),
                    new PathStep(PathStepKind.Line, [(left + side, y)])
                ], null, stroke, width, EvenOdd: false);

            case "star":
                return new PathOperation(
                [
                    new PathStep(PathStepKind.Move, [(x, top)]),
                    new PathStep(PathStepKind.Line, [(x, top + side)]),
                    new PathStep(PathStepKind.Move, [(left, top)]),
                    new PathStep(PathStepKind.Line, [(left + side, top + side)]),
                    new PathStep(PathStepKind.Move, [(left + side, top)]),
                    new PathStep(PathStepKind.Line, [(left, top + side)])
                ], null, stroke, width, EvenOdd: false);

            case "dash":
                return new PathOperation(
                [
                    new PathStep(PathStepKind.Move, [(left, y)]),
                    new PathStep(PathStepKind.Line, [(left + side, y)])
                ], null, stroke, width, EvenOdd: false);

            default:
                return new PathOperation(
                [
                    new PathStep(PathStepKind.Move, [(x, top)]),
                    new PathStep(PathStepKind.Line, [(left + side, y)]),
                    new PathStep(PathStepKind.Line, [(x, top + side)]),
                    new PathStep(PathStepKind.Line, [(left, y)]),
                    new PathStep(PathStepKind.Close, [])
                ], fill, stroke, width, EvenOdd: false);
        }
    }

    /// <summary>A circle, as four Béziers.</summary>
    private static IReadOnlyList<PathStep> Ellipse(double x, double y, double radius)
    {
        const double arc = 0.5523;
        var control = radius * arc;

        return
        [
            new PathStep(PathStepKind.Move, [(x + radius, y)]),
            new PathStep(PathStepKind.Curve,
                [(x + radius, y + control), (x + control, y + radius), (x, y + radius)]),
            new PathStep(PathStepKind.Curve,
                [(x - control, y + radius), (x - radius, y + control), (x - radius, y)]),
            new PathStep(PathStepKind.Curve,
                [(x - radius, y - control), (x - control, y - radius), (x, y - radius)]),
            new PathStep(PathStepKind.Curve,
                [(x + control, y - radius), (x + radius, y - control), (x + radius, y)]),
            new PathStep(PathStepKind.Close, [])
        ];
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
    public static IEnumerable<double> Marks(Plan plan) =>
        Marked(plan.Minimum, plan.Maximum, plan.MajorUnit);

    /// <summary>And the values the foot marks, where the foot is a scale of its own.</summary>
    public static IEnumerable<double> MarksAcross(Plan plan) =>
        Marked(plan.AcrossMinimum, plan.AcrossMaximum, plan.AcrossUnit);

    private static IEnumerable<double> Marked(double minimum, double maximum, double unit)
    {
        if (unit <= 0) yield break;

        // Counted rather than added up, so that a hundred marks do not drift.
        var steps = (int)Math.Floor((maximum - minimum) / unit + 0.000001);

        for (var i = 0; i <= steps; i++) yield return minimum + i * unit;
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
