using n8PDF.Images;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Where a scene point of a three-dimensional chart lands on the page — one contract over the two
/// projections Word uses, picked by <c>rAngAx</c>.
/// </summary>
internal interface IChart3DProjection
{
    /// <summary>
    /// Where a scene point lands, in points from the page's top-left. Coordinates run 0..1 across
    /// the box: <paramref name="x"/> left face to right, <paramref name="y"/> floor to top,
    /// <paramref name="z"/> front face to back.
    /// </summary>
    (double X, double Y) Project(double x, double y, double z);
}

/// <summary>
/// Draws the room a three-dimensional plot stands in: the stated walls and floor, and the
/// gridlines projected onto them.
/// </summary>
/// <remarks>
/// Everything here is measured against Word's raster of <c>chart-3d-wall-probe</c> and the
/// committed gridline probes — see <c>Chart3DWallTests</c>.
///
/// <para><b>The surfaces.</b> The back wall, side wall and floor are the box's own faces — the
/// floor is the projected box base to the half point, which took a probe of its own to see
/// because a bar of any height casts an occlusion shadow across it. They are drawn only where
/// the document states a fill (#110: unstated, nothing is drawn), shaded by the one rule the
/// bars will share: a face keeps its stated colour on the back wall as on a bar's front, takes
/// three quarters of it on the floor as on a bar's top, and five eighths on the side wall as on
/// a bar's side — per channel, multiplicatively.</para>
///
/// <para><b>Which side the side wall stands.</b> At <c>rotY</c> up to 180 the room opens to the
/// right and the side wall stands at the box's left; past 180 the whole picture is the mirror of
/// <c>360 − rotY</c> about the plot rectangle's centreline, measured exact on both arms — so the
/// side wall swaps sides by mirroring rather than by a rule of its own.</para>
///
/// <para><b>The gridlines.</b> An axis's line at a mark is drawn on the two surfaces the mark's
/// own plane crosses, whether or not the axis itself is deleted — the probes delete every axis
/// and Word draws the lines regardless. The value axis rules the side wall and the back wall at
/// each scale mark, ends included; the depth axis rules the side wall and the floor at each row
/// boundary; the category axis rules the back wall and the floor at each slot boundary. Minor
/// gridlines take the minor unit where stated; where not, a fifth of the major unit — the one
/// number here that is assumed rather than measured, since the probe states its minor unit.</para>
/// </remarks>
internal static class Chart3DComposer
{
    /// <summary>The floor's share of its stated colour: a top-facing surface, [0.750, 0.766].</summary>
    private const double FloorShade = 0.758;

    /// <summary>The side wall's: a side-facing surface, [0.625, 0.646].</summary>
    private const double SideShade = 0.6355;

    /// <summary>
    /// A left-facing side's share: past rotY 180 the mirror shows the bars' other flank, and
    /// Word lights it darker — sampled at exactly 102/255 on the box probe's 340 page.
    /// </summary>
    private const double FarSideShade = 0.400;

    private const double DefaultLineWidth = 0.5;

    /// <summary>The grey of the floor's outline and the depth axis's ticks, sampled from
    /// Word's raster — (137,137,137) at its core on every probe page.</summary>
    private static readonly DrawingColor FloorOutline = new(137, 137, 137);

    private const double FloorOutlineWidth = 0.33;

    /// <summary>How far a depth-axis tick reaches, matching the flat axes' ticks.</summary>
    private const double TickLength = 3.1733;

    /// <summary>Whether a turn mirrors the picture — past 180, the scene is drawn as its
    /// reflection (#99), but the data axes re-anchor: categories still run left to right.</summary>
    public static bool Mirrors(ChartScene scene) => (scene.RotationY % 360 + 360) % 360 > 180;

    /// <summary>The projection the scene asks for, mirrored where <c>rotY</c> passes 180.</summary>
    public static IChart3DProjection Projection(
        ChartScene scene, double categories, double series,
        double rectLeft, double rectTop, double rectWidth, double rectHeight,
        double? heightUnits = null, double? marginUnits = null, bool statedHeight = true)
    {
        var rotY = ((scene.RotationY % 360) + 360) % 360;
        var mirrored = rotY > 180;
        if (mirrored) rotY = 360 - rotY;

        IChart3DProjection projection = scene.RightAngleAxes
            ? new Chart3DObliqueProjection(scene.RotationX, rotY, scene.DepthPercent,
                statedHeight ? scene.HeightPercent : null, categories, series,
                rectLeft, rectTop, rectWidth, rectHeight, heightUnits, marginUnits)
            : new Chart3DProjection(scene.RotationX, rotY, scene.Perspective, scene.DepthPercent,
                statedHeight ? scene.HeightPercent : null, categories, series,
                rectLeft, rectTop, rectWidth, rectHeight, heightUnits, marginUnits);

        return mirrored
            ? new MirroredProjection(projection, rectLeft + rectWidth / 2)
            : projection;
    }

    /// <summary>
    /// The scene at <c>360 − rotY</c>, flipped about the plot rectangle's centreline — which is
    /// exactly Word's picture for a turn past 180, measured on both arms at rotY 340.
    /// </summary>
    private sealed class MirroredProjection(IChart3DProjection inner, double middle) : IChart3DProjection
    {
        public (double X, double Y) Project(double x, double y, double z)
        {
            var (px, py) = inner.Project(x, y, z);

            return (2 * middle - px, py);
        }
    }

    /// <summary>The room: surfaces first, then the gridlines on them.</summary>
    public static IEnumerable<DrawingOperation> Draw(
        ChartDefinition chart, ChartComposer.Plan plan, DocumentTheme theme)
    {
        var scene = chart.Scene!;

        if (chart.Round)
        {
            foreach (var operation in Pie(chart, plan, theme))
                yield return operation;
            yield break;
        }

        var categories = Math.Max(1, chart.Categories.Count);
        var arrangement = Chart3DArrangement.For(chart);

        // Lying, the scene transposes: the categories stand one unit each on a vertical plane,
        // the values run across the page, and the box's length takes the standing height rule
        // with the aspect the other way up — measured on the box probe's lying pages. The lean
        // stays exactly where it stands: sin rotY rightward, sin rotX upward, in page space.
        var projection = chart.Lying
            ? Projection(scene,
                (scene.HeightPercent is { } stated ? categories * stated / 100 : arrangement.HeightUnits)
                    * (plan.Width / plan.Height),
                arrangement.DepthUnits, plan.Left, plan.Top, plan.Width, plan.Height,
                heightUnits: categories * (plan.Width / plan.Height),
                marginUnits: categories, statedHeight: false)
            : Projection(scene, arrangement.WidthUnits, arrangement.DepthUnits,
                plan.Left, plan.Top, plan.Width, plan.Height, arrangement.HeightUnits);

        // The value-origin plane's outline is drawn whether or not it is filled — every page of
        // every probe shows it, a hairline in a grey nothing else uses. Standing that plane is
        // the floor; lying it is the wall the bars grow from.
        var outline = chart.Lying
            ? new[] { (0.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 1.0, 1.0), (0.0, 0.0, 1.0) }
            : new[] { (0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (1.0, 0.0, 1.0), (0.0, 0.0, 1.0) };

        yield return new PathOperation([
            new PathStep(PathStepKind.Move, [projection.Project(outline[0].Item1, outline[0].Item2, outline[0].Item3)]),
            new PathStep(PathStepKind.Line, [projection.Project(outline[1].Item1, outline[1].Item2, outline[1].Item3)]),
            new PathStep(PathStepKind.Line, [projection.Project(outline[2].Item1, outline[2].Item2, outline[2].Item3)]),
            new PathStep(PathStepKind.Line, [projection.Project(outline[3].Item1, outline[3].Item2, outline[3].Item3)]),
            new PathStep(PathStepKind.Close, [])
        ], null, FloorOutline, FloorOutlineWidth, EvenOdd: false);

        // The depth axis's tick marks, at each row boundary along the box's right depth edge,
        // reaching outward the way a flat axis's "out" ticks do. Its labels are text and are
        // placed with the rest of the chart's text — see ChartComposer.DepthAxisLabels.
        if (!chart.Lying && chart.DepthAxis is { Deleted: false, MajorTickMark: not "none" })
        {
            for (var k = 0; k <= arrangement.Rows; k++)
            {
                var (px, py) = projection.Project(1, 0, (double)k / arrangement.Rows);

                yield return new PathOperation([
                    new PathStep(PathStepKind.Move, [(px, py)]),
                    new PathStep(PathStepKind.Line, [(px + TickLength, py)])
                ], null, FloorOutline, FloorOutlineWidth, EvenOdd: false);
            }
        }

        // The surfaces, only where stated. The side wall is always drawn at x nought: past
        // rotY 180 the projection itself is mirrored, which is what moves the wall across.
        // Lying, the room beyond the outline is unmeasured and nothing more is drawn of it.
        if (!chart.Lying && Resolve(chart.FloorFill, theme) is { } floor)
            yield return Quad(projection,
                (0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1), Shade(floor, FloorShade));

        if (!chart.Lying && Resolve(chart.BackWallFill, theme) is { } back)
            yield return Quad(projection,
                (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1), back);

        if (!chart.Lying && Resolve(chart.SideWallFill, theme) is { } side)
            yield return Quad(projection,
                (0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1), Shade(side, SideShade));

        // The lines are painted value first, then depth, then category, which is the order
        // the junctions give away: where the category boundary at nought runs along the side
        // wall's base it covers the value line at nought, and comes back green in Word's raster
        // where the reverse order would leave it red.
        // The value axis rules the side wall and the back wall at each mark, ends included.
        if (!chart.Lying && chart.ValueAxis is { } value)
        {
            var span = plan.Maximum - plan.Minimum;

            if (value.MinorGridlines && span > 0)
            {
                // A fifth of the major unit is what Word's automatic minor comes to; the stated
                // unit is what the probe measures.
                var unit = value.MinorUnit ?? plan.MajorUnit / 5;
                var style = Style(value.MinorGridlineColor, value.MinorGridlineWidth, theme);

                foreach (var mark in Marked(plan.Minimum, plan.Maximum, unit))
                {
                    // Not where a major line already rules.
                    var major = (mark - plan.Minimum) / plan.MajorUnit;
                    if (Math.Abs(major - Math.Round(major)) < 0.0001) continue;

                    var t = (mark - plan.Minimum) / span;

                    yield return Polyline(projection, [(0, t, 0), (0, t, 1), (1, t, 1)], style);
                }
            }

            if (value.MajorGridlines && span > 0)
            {
                var style = Style(value.MajorGridlineColor, value.MajorGridlineWidth, theme);

                foreach (var mark in ChartComposer.Marks(plan))
                {
                    var t = (mark - plan.Minimum) / span;

                    yield return Polyline(projection, [(0, t, 0), (0, t, 1), (1, t, 1)], style);
                }
            }
        }
        // The depth axis rules the side wall and the floor at each row boundary.
        if (!chart.Lying && chart.DepthAxis is { MajorGridlines: true } depth)
        {
            for (var k = 0; k <= arrangement.Rows; k++)
            {
                var t = (double)k / arrangement.Rows;

                yield return Polyline(projection, [(0, 1, t), (0, 0, t), (1, 0, t)],
                    Style(depth.MajorGridlineColor, depth.MajorGridlineWidth, theme));
            }
        }

        // The category axis rules the back wall and the floor at each slot boundary.
        if (!chart.Lying && chart.CategoryAxis is { MajorGridlines: true } category)
        {
            for (var k = 0; k <= categories; k++)
            {
                var t = (double)k / categories;

                yield return Polyline(projection, [(t, 1, 1), (t, 0, 1), (t, 0, 0)],
                    Style(category.MajorGridlineColor, category.MajorGridlineWidth, theme));
            }
        }


        // A surface chart stops here, by decision (#104): its mesh is a different plot from
        // anything below, and the flat kind it parses to would draw it as a 3-D line — a wrong
        // picture presented as the right one. The reader keeps the room and gets no mesh, the
        // same honest answer the whole family gave before its own stories landed.
        if (chart.Surface) yield break;

        if (chart.Kind is ChartKind.Line or ChartKind.Area)
        {
            foreach (var operation in Ribbons(chart, plan, arrangement, projection, theme))
                yield return operation;
            yield break;
        }

        foreach (var operation in Bars(chart, plan, arrangement, projection, theme))
            yield return operation;
    }

    /// <summary>
    /// The ribbons a 3-D line or area chart runs in depth: a sloped roof following the values,
    /// an unshaded front, and side-shaded ends.
    /// </summary>
    /// <remarks>
    /// Measured from <c>chart-3d-ribbon-probe</c>. The points span the plot edge to edge —
    /// point <c>i</c> of <c>n</c> stands at <c>i/(n−1)</c> of the box — the way the flat area
    /// chart spans. The rows and their depth fills are the bars' own arrangement. A roof
    /// segment's shade follows its slope: 0.827 of the colour rising, 0.639 falling, the flat
    /// top's 0.758 level — Word lights the tilted quads and those are the three readings its
    /// raster shows. An area drops to nought (or its stack) and closes with a front wall; a
    /// line is the roof alone over a thin front strip, 0.045 of the box tall, read off the
    /// ribbon's end.
    /// </remarks>
    private static IEnumerable<DrawingOperation> Ribbons(
        ChartDefinition chart, ChartComposer.Plan plan, Chart3DArrangement arrangement,
        IChart3DProjection projection, DocumentTheme theme)
    {
        var span = plan.Maximum - plan.Minimum;
        if (span <= 0) yield break;

        var stacked = chart.Grouping is ChartGrouping.Stacked or ChartGrouping.PercentStacked;
        var area = chart.Kind == ChartKind.Area || stacked;

        double At(double value) => Math.Clamp((value - plan.Minimum) / span, 0, 1);

        PathOperation Quad3(
            (double, double, double) a, (double, double, double) b,
            (double, double, double) c, (double, double, double) d, DrawingColor fill)
        {
            var q = new[] { a, b, c, d }
                .Select(v => projection.Project(v.Item1, v.Item2, v.Item3)).ToArray();
            return new PathOperation([
                new PathStep(PathStepKind.Move, [q[0]]),
                new PathStep(PathStepKind.Line, [q[1]]),
                new PathStep(PathStepKind.Line, [q[2]]),
                new PathStep(PathStepKind.Line, [q[3]]),
                new PathStep(PathStepKind.Close, [])
            ], fill, null, DefaultLineWidth, EvenOdd: false);
        }

        for (var row = arrangement.Rows - 1; row >= 0; row--)
        {
            var running = new double[chart.Categories.Count == 0 ? 1 : chart.Categories.Count];

            for (var index = 0; index < chart.Series.Count; index++)
            {
                if (arrangement.Rows > 1 && index != row) continue;

                var series = chart.Series[index];
                var count = series.Values.Count;
                if (count < 2) continue;

                var colour = ResolveSeries(series, 0, theme);

                // A line's ribbon and a stacked pile run deeper than the bars' slots: 0.6 of
                // the row against the 0.4 the stated gapDepth gives a bar — measured at
                // gapDepth 150; whether they follow the gap at all, no page yet separates.
                var (z0, z1) = arrangement.Depth(chart, index);
                if (!area)
                {
                    var mid = (z0 + z1) / 2;
                    var slot = 1.0 / arrangement.Rows;
                    (z0, z1) = (mid - 0.3 * slot, mid + 0.3 * slot);
                }

                // An area spans the plot edge to edge; a line stands its points at the
                // category centres, exactly as the flat charts divide the foot.
                double X(int i) => area || stacked
                    ? (double)i / (count - 1)
                    : (i + 0.5) / count;

                var tops = new double[count];
                var bases = new double[count];
                for (var i = 0; i < count; i++)
                {
                    var value = Math.Max(0, series.Values[i] ?? 0);
                    if (stacked)
                    {
                        bases[i] = At(Math.Max(plan.Minimum, 0) + running[i]);
                        running[i] += value;
                        tops[i] = At(Math.Max(plan.Minimum, 0) + running[i]);
                    }
                    else
                    {
                        tops[i] = At(value);
                        bases[i] = area ? At(Math.Max(plan.Minimum, 0)) : tops[i];
                    }
                }

                // The left end, under everything of this series. A line's ribbon has no
                // ends to show — its thickness on Word's page is the roof's own edge.
                if (area || stacked)
                    yield return Quad3((X(0), bases[0], z0), (X(0), tops[0], z0), (X(0), tops[0], z1),
                        (X(0), bases[0], z1), Shade(colour, SideShade));

                // The roofs, left to right, each shaded by its slope.
                for (var i = 0; i < count - 1; i++)
                {
                    var factor = tops[i + 1] > tops[i] + 0.0001 ? 0.827
                        : tops[i + 1] < tops[i] - 0.0001 ? 0.639
                        : FloorShade;

                    yield return Quad3((X(i), tops[i], z0), (X(i + 1), tops[i + 1], z0),
                        (X(i + 1), tops[i + 1], z1), (X(i), tops[i], z1), Shade(colour, factor));
                }

                // The front: a wall to the base for an area or a stack. A line shows no
                // front strip — its thickness is the sliver its ends and folds show.
                if (!area && !stacked) continue;

                var steps = new List<PathStep>
                {
                    new(PathStepKind.Move, [projection.Project(0, bases[0], z0)])
                };
                for (var i = 0; i < count; i++)
                    steps.Add(new PathStep(PathStepKind.Line, [projection.Project(X(i), tops[i], z0)]));
                for (var i = count - 1; i >= 0; i--)
                    steps.Add(new PathStep(PathStepKind.Line, [projection.Project(X(i), bases[i], z0)]));
                steps.Add(new PathStep(PathStepKind.Close, []));
                yield return new PathOperation(steps, colour, null, DefaultLineWidth, EvenOdd: false);

                // The right end, over the roofs' and front's corner.
                yield return Quad3((X(count - 1), bases[^1], z0), (X(count - 1), tops[^1], z0),
                    (X(count - 1), tops[^1], z1), (X(count - 1), bases[^1], z1),
                    Shade(colour, SideShade));
            }
        }
    }

    /// <summary>
    /// The boxes themselves: three visible faces each, painted far row to near, and within a
    /// row in ascending slot order so a later bar covers the lean of the one before it.
    /// </summary>
    /// <remarks>
    /// The shades are the walls' (#110): the front keeps the series colour, the top takes three
    /// quarters of it, the side five eighths. The top stops the measured 0.0061 of the box short
    /// of its value. A negative value is drawn the way the flat chart draws one — white, and
    /// outlined in black even though the series asks for no outline — hanging from nought;
    /// #113's "no ink of the series' colour" was exactly right, because the ink is white.
    /// Stacked and percent-stacked segments pile bottom to top, each from where the last ended.
    /// </remarks>
    private static IEnumerable<DrawingOperation> Bars(
        ChartDefinition chart, ChartComposer.Plan plan, Chart3DArrangement arrangement,
        IChart3DProjection projection, DocumentTheme theme)
    {
        var categories = Math.Max(1, chart.Categories.Count);
        var span = plan.Maximum - plan.Minimum;
        if (span <= 0) yield break;

        var stacked = chart.Grouping is ChartGrouping.Stacked or ChartGrouping.PercentStacked;

        // Past rotY 180 the room is drawn mirrored, but the data axes re-anchor rather than
        // riding the mirror — measured on the box probe's 340 page, whose unequal bars a full
        // mirror puts on the wrong sides and whose front row Word tucks behind: categories
        // still run left to right on the page, and the first series moves to the far row. So
        // the spans flip inside the mirrored scene, and the painting runs in page order.
        var mirrored = Mirrors(chart.Scene!) && !chart.Lying;

        double At(double value) => Math.Clamp((value - plan.Minimum) / span, 0, 1);

        // Far rows first. Within a row, ascending page position; a clustered row ascends by
        // series inside each category, and a stack ascends from its floor.
        for (var walkRow = 0; walkRow < arrangement.Rows; walkRow++)
        for (var step = 0; step < categories; step++)
        {
            // Far row under, near row over. Past 180 Word's own painting is stranger than any
            // whole-bar order — the probe's 340 page shows a far bar and a near bar cutting
            // into each other, which only a face-level depth sort produces — and the follow-up
            // issue holds those measurements; the ordinary order is kept here, which the ink
            // comparison bounds at about a tenth of the overlapped bars' ink.
            var row = arrangement.Rows - 1 - walkRow;
            var category = mirrored ? categories - 1 - step : step;
            var running = 0.0;
            var total = stacked
                ? chart.Series.Sum(entry =>
                    category < entry.Values.Count ? Math.Max(0, entry.Values[category] ?? 0) : 0)
                : 0;

            for (var walk = 0; walk < chart.Series.Count; walk++)
            {
                var index = mirrored && arrangement.Rows == 1 && !stacked
                    ? chart.Series.Count - 1 - walk
                    : walk;
                if (arrangement.Rows > 1 && index != row) continue;

                var series = chart.Series[index];
                if (category >= series.Values.Count || series.Values[category] is not { } value)
                    continue;

                // Under the mirror a clustered row re-anchors too, the first series keeping
                // the left of its cluster — consistent with the categories, though no probe
                // page pins it.
                var across = mirrored && arrangement.Rows == 1 && !stacked
                    ? chart.Series.Count - 1 - index
                    : index;
                var (x0, x1) = arrangement.Across(chart, category, across);
                if (mirrored) (x0, x1) = (1 - x1, 1 - x0);
                var (z0, z1) = arrangement.Depth(chart, index);

                double from, to;
                var inverted = false;

                if (stacked)
                {
                    if (value <= 0) continue;
                    var share = chart.Grouping == ChartGrouping.PercentStacked && total > 0
                        ? value / total * (plan.Maximum - Math.Max(plan.Minimum, 0)) + 0.0
                        : value;
                    from = At(Math.Max(plan.Minimum, 0) + running);
                    running += share;
                    to = At(Math.Max(plan.Minimum, 0) + running);
                }
                else if (value >= 0)
                {
                    from = At(Math.Max(plan.Minimum, 0));
                    to = At(value);
                }
                else
                {
                    inverted = true;
                    to = At(Math.Min(plan.Maximum, 0));
                    from = At(value);
                }

                if (to - from <= 0) continue;

                // The measured shortfall comes off the value end.
                if (inverted) from += Chart3DObliqueProjection.BarTopShortfall;
                else to -= Chart3DObliqueProjection.BarTopShortfall;
                if (to - from <= 0) continue;

                var colour = inverted
                    ? new DrawingColor(255, 255, 255)
                    : ResolveSeries(series, category, theme);
                var stroke = inverted ? new DrawingColor(0, 0, 0) : (DrawingColor?)null;
                var width = inverted ? 0.75 : DefaultLineWidth;

                // The faces, painted far to near on the box itself: top first standing (it is
                // never in front of the front face), then side, then front.
                foreach (var face in Faces(chart.Lying, mirrored, x0, x1, from, to, z0, z1))
                {
                    var fill = Shade(colour, face.Factor);
                    var corners = face.Corners
                        .Select(q => projection.Project(q.Item1, q.Item2, q.Item3)).ToArray();

                    yield return new PathOperation([
                        new PathStep(PathStepKind.Move, [corners[0]]),
                        new PathStep(PathStepKind.Line, [corners[1]]),
                        new PathStep(PathStepKind.Line, [corners[2]]),
                        new PathStep(PathStepKind.Line, [corners[3]]),
                        new PathStep(PathStepKind.Close, [])
                    ], fill, stroke, width, EvenOdd: false);
                }
            }
        }
    }

    /// <summary>
    /// A bar's three visible faces, each with its orientation's shade: standing, the value runs
    /// up and the top face takes three quarters; lying, the value runs across, the categories'
    /// upper face is the top and the value end is the side.
    /// </summary>
    private static IEnumerable<((double, double, double)[] Corners, double Factor)> Faces(
        bool lying, bool mirrored, double x0, double x1, double from, double to, double z0, double z1)
    {
        // The mirror shows the bars' other flank, and a left-facing side is lit darker.
        var side = mirrored ? FarSideShade : SideShade;

        if (lying)
        {
            // Scene coordinates: x is the value, y the category (0 at the page's foot).
            yield return ([(from, x1, z0), (from, x1, z1), (to, x1, z1), (to, x1, z0)], FloorShade);
            yield return ([(to, x0, z0), (to, x1, z0), (to, x1, z1), (to, x0, z1)], side);
            yield return ([(from, x0, z0), (to, x0, z0), (to, x1, z0), (from, x1, z0)], 1);
        }
        else
        {
            yield return ([(x0, to, z0), (x1, to, z0), (x1, to, z1), (x0, to, z1)], FloorShade);
            yield return ([(x1, from, z0), (x1, to, z0), (x1, to, z1), (x1, from, z1)], side);
            yield return ([(x0, from, z0), (x1, from, z0), (x1, to, z0), (x0, to, z0)], 1);
        }
    }

    /// <summary>A bar's stated colour: the point's own fill where one is stated, else the series'.</summary>
    private static DrawingColor ResolveSeries(ChartSeries series, int category, DocumentTheme theme)
    {
        var stated = series.PointFills.TryGetValue(category, out var point) && point is not null
            ? point
            : series.Fill;

        return Resolve(stated, theme) ?? new DrawingColor(0x44, 0x72, 0xC4);
    }


    /// <summary>
    /// The three-dimensional pie: an elliptical top cut into sectors, and a rim below the arcs
    /// that face the reader.
    /// </summary>
    /// <remarks>
    /// A pie ignores <c>rotY</c>, <c>rAngAx</c> and — measured here — <c>hPercent</c>; only the
    /// tilt and the perspective shape it (#96). The laws, from <c>chart-3d-pie-probe</c>:
    ///
    /// <list type="bullet">
    /// <item>The silhouette fills 0.9702 of the plot rectangle on whichever side binds and is
    /// centred across; at perspective nought it is centred down as well.</item>
    /// <item>At perspective nought the top is the exact tilted disc: <c>ry = rx·sin rotX</c>,
    /// and the rim stands <c>0.24·rx·cos rotX</c> tall — the cylinder is 0.24 of its own
    /// radius thick, measured [0.238, 0.242].</item>
    /// <item>Perspective flattens the top and deepens the rim, and both come from one camera
    /// rather than two fitted families: the tilted disc, a cylinder 0.24 of its radius deep, seen
    /// through the same perspective divide the boxes use, from an eye that drops as the
    /// perspective deepens (<see cref="PieSilhouette"/>). Projecting its rims reproduces the
    /// probe's grid — the top's semi-axis and the whole height together — to about a hundredth of
    /// the radius across rotX 10–40 and perspective 0–60, the projective form the way #98 derived
    /// the box camera. A naive fixed-disc projection cannot do this: it magnifies the near edge
    /// and so <em>grows</em> the silhouette, where Word's shrinks — only the dropped eye flattens
    /// it (#166). One residual stays fitted: Word lifts the pie a little further off the plot
    /// centre than the symmetric projection accounts for, largest at a gentle tilt and steep
    /// perspective — the rise, riding on <c>sinA − ryUnit</c>, the height the flattening took.</item>
    /// <item>Word paints the rim as a cylindrical gradient. It is drawn here flat at 0.65 of
    /// the sector's colour, the middle of the gradient's measured [0.35, 1.0] range; the ink
    /// comparisons read colour families for exactly this reason.</item>
    /// <item>An exploded sector moves along its bisector by its stated share of the radius, on
    /// the ellipse's own axes, and the whole pie shrinks so the furthest tip still lands on the
    /// fill boundary while the disc's centre holds — the radius is the disc-fill radius over
    /// <c>1 + reach</c> on the binding axis. Derived, not fitted: measured to under a point on the
    /// front slices across an explosion sweep (0/10/25/40) and an off-axis slice (#166).</item>
    /// </list>
    /// </remarks>
    private static IEnumerable<DrawingOperation> Pie(
        ChartDefinition chart, ChartComposer.Plan plan, DocumentTheme theme)
    {
        var scene = chart.Scene!;
        var a = scene.RotationX * Math.PI / 180;
        var theta = scene.Perspective / 4 * Math.PI / 180;
        var (sinA, cosA) = (Math.Sin(a), Math.Cos(a));

        // The vertical make-up of the silhouette, per unit of rx: the tilted disc under Word's
        // own perspective camera, projected rather than fitted (#166). One camera gives the top
        // ellipse's flattening and the rim's growth together, in place of the tan^1.5/sin²
        // families they were; only the rise off centre (below) stays a residual the projection
        // does not account for.
        var (ryUnit, rimUnit) = PieSilhouette(sinA, cosA, Math.Tan(theta));
        var height = 2 * ryUnit + rimUnit;

        var series = chart.Series.FirstOrDefault();
        if (series is null) yield break;

        var values = series.Values.Select(value => Math.Max(0, value ?? 0)).ToList();
        var total = values.Sum();
        if (total <= 0) yield break;

        // Sector geometry first — a sweep depends only on the values, not on the radius, so how
        // far each explosion reaches is known before the radius is chosen.
        // A three-dimensional pie cannot be turned: CT_Pie3DChart carries no firstSliceAng, and
        // Word ignores one written anyway — four probe pages stating four different angles render
        // identically. The first slice always starts at the top.
        var sectors = new List<(double From, double Sweep, DrawingColor Colour, double Mid, double Reach)>();
        double maxHReach = 0, maxVReach = 0;
        var walk = 0.0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0) continue;
            var sweep = values[i] / total * 2 * Math.PI;
            var fill = series.PointFills.TryGetValue(i, out var point) && point is not null
                ? point
                : series.Fill;
            var colour = Resolve(fill, theme) ?? new DrawingColor(0x44, 0x72, 0xC4);

            var mid = walk + sweep / 2;
            var reach = series.PointExplosions.TryGetValue(i, out var stated) ? stated / 100.0 : 0;
            if (reach > 0)
            {
                // The sector slides out along its bisector, so its arc reaches reach·rx further
                // in x and reach·ry further in y, scaled by the bisector's direction cosines.
                maxHReach = Math.Max(maxHReach, reach * Math.Abs(Math.Sin(mid)));
                maxVReach = Math.Max(maxVReach, reach * Math.Abs(Math.Cos(mid)));
            }

            sectors.Add((walk, sweep, colour, mid, reach));
            walk += sweep;
        }

        // The disc stays centred on the plot rectangle; an exploded sector slides past its edge,
        // and Word shrinks the whole pie so the furthest tip still lands on the fill boundary
        // rather than spilling over it. So the radius is the disc-fill radius divided by how far
        // the exploded arrangement reaches on the binding axis: the pie shrinks, the centre holds.
        // Measured against Word to under a point across an explosion sweep (0/10/25/40 on the
        // horizontal slice) and an off-axis slice (#166); the vertical divisor is the same reach
        // argument on the other axis, which no Word-reachable pie is tall enough to bind.
        const double Fill = 0.9702;
        var rx = Math.Min(
            Fill * plan.Width / 2 / (1 + maxHReach),
            Fill * plan.Height / (height <= 0 ? 1 : height) / (1 + maxVReach));
        var ry = rx * ryUnit;
        var rim = rx * rimUnit;

        // The projected disc sits symmetrically about its axis, but Word lifts the whole pie a
        // little further off the plot centre than that — a residual the camera does not explain,
        // largest at a gentle tilt and steep perspective, where the flattening takes the most
        // from the top. It rides on the height so lost: sinA − ryUnit is what the flattening
        // removed. Still fitted (#166).
        var rise = rx * (sinA - ryUnit) * Math.Pow(cosA, 4);
        var cx = plan.Left + plan.Width / 2;
        var cy = plan.Top + plan.Height / 2 - rise - (2 * ry + rim) / 2 + ry;

        (double X, double Y) At(double angle, double reach, double ox, double oy) =>
            (cx + ox + reach * rx * Math.Sin(angle), cy + oy - reach * ry * Math.Cos(angle));

        var starts = new List<(double From, double Sweep, DrawingColor Colour, double Ox, double Oy)>();
        foreach (var (from, sweep, colour, mid, reach) in sectors)
        {
            var (ox, oy) = reach > 0
                ? (reach * rx * Math.Sin(mid), -reach * ry * Math.Cos(mid))
                : (0.0, 0.0);
            starts.Add((from, sweep, colour, ox, oy));
        }

        // The rim first, only under the arcs that face the reader — the lower half of the
        // ellipse — then the tops over it.
        foreach (var (from, sweep, colour, ox, oy) in starts)
        {
            foreach (var (f, t) in FrontArcs(from, sweep))
            {
                var steps = new List<PathStep> { new(PathStepKind.Move, [At(f, 1, ox, oy)]) };
                const int pieces = 24;
                for (var i = 1; i <= pieces; i++)
                    steps.Add(new PathStep(PathStepKind.Line, [At(f + (t - f) * i / pieces, 1, ox, oy)]));
                for (var i = pieces; i >= 0; i--)
                {
                    var (px, py) = At(f + (t - f) * i / pieces, 1, ox, oy);
                    steps.Add(new PathStep(PathStepKind.Line, [(px, py + rim)]));
                }

                steps.Add(new PathStep(PathStepKind.Close, []));

                yield return new PathOperation(steps, Shade(colour, 0.65), null, DefaultLineWidth,
                    EvenOdd: false);
            }
        }

        foreach (var (from, sweep, colour, ox, oy) in starts)
        {
            var steps = new List<PathStep> { new(PathStepKind.Move, [(cx + ox, cy + oy)]) };
            const int pieces = 48;
            for (var i = 0; i <= pieces; i++)
                steps.Add(new PathStep(PathStepKind.Line, [At(from + sweep * i / pieces, 1, ox, oy)]));
            steps.Add(new PathStep(PathStepKind.Close, []));

            yield return new PathOperation(steps, colour, null, DefaultLineWidth, EvenOdd: false);
        }
    }

    /// <summary>
    /// The top ellipse's semi-axis and the rim's depth, per unit of the pie's radius, from Word's
    /// perspective camera (#166).
    /// </summary>
    /// <remarks>
    /// The pie is a flat cylinder — a disc of radius one, 0.24 deep — lying in
    /// the floor plane, tilted back by rotX and seen through the same perspective divide the boxes
    /// use. The eye sits at <c>(0, ey, −D)</c>; the frustum's half-height at the disc is
    /// <c>F = FrustumBase + FrustumSlope·tan θ</c> so that <c>D = F/tan θ</c> stays infinite at
    /// perspective nought (there the projection is the parallel one and the top is the exact
    /// tilted disc, <c>ry = sin rotX</c>, rim <c>0.24·cos rotX</c>). As the perspective deepens the
    /// eye drops by <c>ey = −EyeDrop·tan θ</c>, which flattens the top face and lets the rim grow —
    /// the two effects that were separate fitted families. Projecting the top and bottom rims and
    /// reading the silhouette off them reproduces Word's grid — the upper semi-axis and the total
    /// height together — to about a hundredth of the radius across rotX 10–40 and perspective
    /// 0–60. The three camera constants were measured from that grid the way #98 measured the
    /// box camera's; the top face comes out symmetric about its axis, so the drawn ellipse is too.
    /// </remarks>
    private static (double RyUnit, double RimUnit) PieSilhouette(double sinA, double cosA, double tan)
    {
        // The rim's depth in radii, from the parallel-perspective rim standing 0.24·cosA tall.
        const double Thickness = 0.24;
        // The frustum half-height F = FrustumBase + FrustumSlope·tan θ, and the eye's drop.
        const double FrustumBase = 0.2428, FrustumSlope = 1.0612, EyeDrop = 0.7581;

        var parallel = tan < 1e-9;
        var d = parallel ? 0 : (FrustumBase + FrustumSlope * tan) / tan;
        var ey = parallel ? 0 : -EyeDrop * tan;

        double topMin = double.MaxValue, topMax = double.MinValue, botMax = double.MinValue, rxUnit = 0;
        const int steps = 360;
        for (var i = 0; i < steps; i++)
        {
            var phi = 2 * Math.PI * i / steps;
            var (sx, sz) = (Math.Sin(phi), Math.Cos(phi));

            // The top face at y = +Thickness/2, the bottom at −Thickness/2; rotX tilts about the
            // horizontal, then the perspective divide from the eye.
            for (var top = 0; top < 2; top++)
            {
                var y = (top == 0 ? Thickness : -Thickness) / 2;
                var sy2 = y * cosA + sz * sinA;
                var sz2 = -y * sinA + sz * cosA;
                var towards = parallel ? 1 : d / (d + sz2);
                var screenY = -towards * (sy2 - ey);
                if (top == 0)
                {
                    if (screenY < topMin) topMin = screenY;
                    if (screenY > topMax) topMax = screenY;
                    var absX = Math.Abs(towards * sx);
                    if (absX > rxUnit) rxUnit = absX;
                }
                else if (screenY > botMax)
                {
                    botMax = screenY;
                }
            }
        }

        return ((topMax - topMin) / 2 / rxUnit, (botMax - topMax) / rxUnit);
    }

    /// <summary>
    /// The parts of a sector's arc that face the reader: pie angles run clockwise from the top,
    /// so the facing half is the half-turn centred on the bottom.
    /// </summary>
    private static IEnumerable<(double From, double To)> FrontArcs(double from, double sweep)
    {
        // Normalise to [0, 2pi) and intersect with (pi/2, 3pi/2) modulo full turns.
        var start = from % (2 * Math.PI);
        if (start < 0) start += 2 * Math.PI;
        var end = start + sweep;

        for (var baseTurn = 0.0; baseTurn < end; baseTurn += 2 * Math.PI)
        {
            var f = Math.Max(start, baseTurn + Math.PI / 2);
            var t = Math.Min(end, baseTurn + 3 * Math.PI / 2);
            if (t > f) yield return (f, t);
        }
    }

    // A real 3-D axis never approaches this; the cap bounds a hostile min/max/unit (#241).
    private const int MostMarks = 1000;

    /// <summary>The marks of a scale, counted rather than added up so they do not drift.</summary>
    private static IEnumerable<double> Marked(double minimum, double maximum, double unit)
    {
        if (unit <= 0) yield break;

        // Capped so a document-controlled min/max/unit cannot force an unbounded draw loop (#241);
        // a real axis is far below this, so the cap never truncates one Word would draw.
        var steps = Math.Min(MostMarks, (int)Math.Floor((maximum - minimum) / unit + 0.000001));
        for (var i = 0; i <= steps; i++) yield return minimum + i * unit;
    }

    private static PathOperation Quad(
        IChart3DProjection projection,
        (double X, double Y, double Z) a, (double X, double Y, double Z) b,
        (double X, double Y, double Z) c, (double X, double Y, double Z) d,
        DrawingColor fill)
    {
        var p = new[] { a, b, c, d }.Select(q => projection.Project(q.X, q.Y, q.Z)).ToArray();

        return new PathOperation([
            new PathStep(PathStepKind.Move, [p[0]]),
            new PathStep(PathStepKind.Line, [p[1]]),
            new PathStep(PathStepKind.Line, [p[2]]),
            new PathStep(PathStepKind.Line, [p[3]]),
            new PathStep(PathStepKind.Close, [])
        ], fill, null, DefaultLineWidth, EvenOdd: false);
    }

    private static PathOperation Polyline(
        IChart3DProjection projection, (double X, double Y, double Z)[] along,
        (DrawingColor Colour, double Width) style)
    {
        var steps = new List<PathStep>
        {
            new(PathStepKind.Move, [projection.Project(along[0].X, along[0].Y, along[0].Z)])
        };
        foreach (var q in along.Skip(1))
            steps.Add(new PathStep(PathStepKind.Line, [projection.Project(q.X, q.Y, q.Z)]));

        return new PathOperation(steps, null, style.Colour, style.Width, EvenOdd: false);
    }

    private static (DrawingColor Colour, double Width) Style(
        DrawingColorReference? colour, double? width, DocumentTheme theme) =>
        (Resolve(colour, theme) ?? new DrawingColor(0, 0, 0), width ?? DefaultLineWidth);

    /// <summary>A face's share of a colour, applied to each channel alike (#110).</summary>
    private static DrawingColor Shade(DrawingColor colour, double factor) => new(
        (byte)Math.Round(colour.Red * factor),
        (byte)Math.Round(colour.Green * factor),
        (byte)Math.Round(colour.Blue * factor));

    /// <summary>
    /// A stated colour resolved, or null where nothing is stated — which for a wall means
    /// nothing is drawn, unlike the series fallback the flat charts use.
    /// </summary>
    private static DrawingColor? Resolve(DrawingColorReference? colour, DocumentTheme theme)
    {
        var hex = colour?.Hex ?? (colour?.ThemeSlot is { } slot ? theme.ResolveColor(slot) : null);
        if (hex is null || hex.Length != 6) return null;

        try
        {
            return new DrawingColor(
                Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..], 16));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
