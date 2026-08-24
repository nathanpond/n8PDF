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

    private const double DefaultLineWidth = 0.5;

    /// <summary>The grey of the floor's outline and the depth axis's ticks, sampled from
    /// Word's raster — (137,137,137) at its core on every probe page.</summary>
    private static readonly DrawingColor FloorOutline = new(137, 137, 137);

    private const double FloorOutlineWidth = 0.33;

    /// <summary>How far a depth-axis tick reaches, matching the flat axes' ticks.</summary>
    private const double TickLength = 3.1733;

    /// <summary>The projection the scene asks for, mirrored where <c>rotY</c> passes 180.</summary>
    public static IChart3DProjection Projection(
        ChartScene scene, double categories, double series,
        double rectLeft, double rectTop, double rectWidth, double rectHeight,
        double? heightUnits = null)
    {
        var rotY = ((scene.RotationY % 360) + 360) % 360;
        var mirrored = rotY > 180;
        if (mirrored) rotY = 360 - rotY;

        IChart3DProjection projection = scene.RightAngleAxes
            ? new Chart3DObliqueProjection(scene.RotationX, rotY, scene.DepthPercent,
                scene.HeightPercent, categories, series, rectLeft, rectTop, rectWidth, rectHeight,
                heightUnits)
            : new Chart3DProjection(scene.RotationX, rotY, scene.Perspective, scene.DepthPercent,
                scene.HeightPercent, categories, series, rectLeft, rectTop, rectWidth, rectHeight,
                heightUnits);

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
        var categories = Math.Max(1, chart.Categories.Count);
        var arrangement = Chart3DArrangement.For(chart);

        var projection = Projection(scene, arrangement.WidthUnits, arrangement.DepthUnits,
            plan.Left, plan.Top, plan.Width, plan.Height, arrangement.HeightUnits);

        // The floor's outline is drawn whether or not the floor is filled — every page of every
        // probe shows it, a hairline in a grey nothing else uses.
        yield return new PathOperation([
            new PathStep(PathStepKind.Move, [projection.Project(0, 0, 0)]),
            new PathStep(PathStepKind.Line, [projection.Project(1, 0, 0)]),
            new PathStep(PathStepKind.Line, [projection.Project(1, 0, 1)]),
            new PathStep(PathStepKind.Line, [projection.Project(0, 0, 1)]),
            new PathStep(PathStepKind.Close, [])
        ], null, FloorOutline, FloorOutlineWidth, EvenOdd: false);

        // The depth axis's tick marks, at each row boundary along the box's right depth edge,
        // reaching outward the way a flat axis's "out" ticks do. Its labels are text and are
        // placed with the rest of the chart's text — see ChartComposer.DepthAxisLabels.
        if (chart.DepthAxis is { Deleted: false, MajorTickMark: not "none" })
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
        if (Resolve(chart.FloorFill, theme) is { } floor)
            yield return Quad(projection,
                (0, 0, 0), (1, 0, 0), (1, 0, 1), (0, 0, 1), Shade(floor, FloorShade));

        if (Resolve(chart.BackWallFill, theme) is { } back)
            yield return Quad(projection,
                (0, 0, 1), (1, 0, 1), (1, 1, 1), (0, 1, 1), back);

        if (Resolve(chart.SideWallFill, theme) is { } side)
            yield return Quad(projection,
                (0, 0, 0), (0, 1, 0), (0, 1, 1), (0, 0, 1), Shade(side, SideShade));

        // The lines are painted value first, then depth, then category, which is the order
        // the junctions give away: where the category boundary at nought runs along the side
        // wall's base it covers the value line at nought, and comes back green in Word's raster
        // where the reverse order would leave it red.
        // The value axis rules the side wall and the back wall at each mark, ends included.
        if (chart.ValueAxis is { } value)
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
        if (chart.DepthAxis is { MajorGridlines: true } depth)
        {
            for (var k = 0; k <= arrangement.Rows; k++)
            {
                var t = (double)k / arrangement.Rows;

                yield return Polyline(projection, [(0, 1, t), (0, 0, t), (1, 0, t)],
                    Style(depth.MajorGridlineColor, depth.MajorGridlineWidth, theme));
            }
        }

        // The category axis rules the back wall and the floor at each slot boundary.
        if (chart.CategoryAxis is { MajorGridlines: true } category)
        {
            for (var k = 0; k <= categories; k++)
            {
                var t = (double)k / categories;

                yield return Polyline(projection, [(t, 1, 1), (t, 0, 1), (t, 0, 0)],
                    Style(category.MajorGridlineColor, category.MajorGridlineWidth, theme));
            }
        }

    }

    /// <summary>The marks of a scale, counted rather than added up so they do not drift.</summary>
    private static IEnumerable<double> Marked(double minimum, double maximum, double unit)
    {
        if (unit <= 0) yield break;

        var steps = (int)Math.Floor((maximum - minimum) / unit + 0.000001);
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
