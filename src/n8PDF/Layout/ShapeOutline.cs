using n8PDF.Images;
using n8PDF.Ooxml;

namespace n8PDF.Layout;

/// <summary>
/// Turns a shape into the drawing it is drawn as: one path, filled, outlined, or both.
/// </summary>
/// <remarks>
/// A shape goes to the page as a drawing rather than as a picture, for the same reason a metafile
/// does — it is a set of commands, and a PDF has commands of its own to write them out as. That it
/// travels as a drawing also means it needs nothing new in the layout model or the renderer: both
/// already carry one.
///
/// The path runs along the shape's own edges. A PDF strokes a path down its middle, so half the
/// outline falls inside the shape and half outside it, which is where Word puts it too: its export
/// fills the whole extent and then strokes the same rectangle, rather than insetting either.
/// </remarks>
internal static class ShapeOutline
{
    /// <summary>
    /// How round a rounded rectangle's corners are, as a fraction of its shorter side. It is the
    /// adjustment value DrawingML gives <c>roundRect</c> when the shape names none of its own.
    /// </summary>
    private const double CornerFraction = 0.16667;

    /// <summary>
    /// How far along the tangent a Bézier control point goes to draw a quarter of a circle. There
    /// is no exact answer — a cubic cannot be an arc — and this is the value that minimises the
    /// error, which is under a part in ten thousand of the radius.
    /// </summary>
    private const double ArcControl = 0.5523;

    public static VectorDrawing Draw(
        ShapeFrame shape, double width, double height, DocumentTheme theme)
    {
        var fill = Resolve(shape.Fill, theme);
        var stroke = Resolve(shape.Line, theme);

        return new VectorDrawing(width, height, [
            new PathOperation(
                Path(shape.Geometry, width, height),
                fill,
                stroke,
                shape.LineWidthPoints,
                EvenOdd: false)
        ]);
    }

    /// <summary>The colour something is painted in, as the drawing named it.</summary>
    private static DrawingColor? Resolve(DrawingColorReference? color, DocumentTheme theme)
    {
        var hex = color?.Hex ?? theme.ResolveColor(color?.ThemeSlot);
        if (hex is null || hex.Length != 6) return null;

        try
        {
            return new DrawingColor(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex[4..], 16));
        }
        catch (Exception e) when (e is FormatException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>
    /// The outline of one of the preset geometries, in the shape's own coordinates, with the
    /// origin at its top left corner.
    /// </summary>
    /// <remarks>
    /// Four of the presets are drawn as themselves and every other one as the rectangle it is
    /// bounded by. DrawingML defines almost two hundred, each as a little program over its own
    /// adjustment values, and drawing them properly means implementing that language rather than
    /// this list — until then a shape whose geometry is not here is at least the right size, in
    /// the right place, and in the right colours.
    /// </remarks>
    private static IReadOnlyList<PathStep> Path(string geometry, double width, double height) =>
        geometry switch
        {
            "roundRect" => RoundedRectangle(width, height),
            "ellipse" => Ellipse(width, height),
            "triangle" => [
                Move(width / 2, 0), Line(width, height), Line(0, height), Close()
            ],
            _ => Rectangle(width, height)
        };

    private static IReadOnlyList<PathStep> Rectangle(double width, double height) =>
        [Move(0, 0), Line(width, 0), Line(width, height), Line(0, height), Close()];

    private static IReadOnlyList<PathStep> RoundedRectangle(double width, double height)
    {
        var radius = Math.Min(width, height) * CornerFraction;
        var control = radius * (1 - ArcControl);

        return
        [
            Move(radius, 0),
            Line(width - radius, 0),
            Curve((width - control, 0), (width, control), (width, radius)),
            Line(width, height - radius),
            Curve((width, height - control), (width - control, height), (width - radius, height)),
            Line(radius, height),
            Curve((control, height), (0, height - control), (0, height - radius)),
            Line(0, radius),
            Curve((0, control), (control, 0), (radius, 0)),
            Close()
        ];
    }

    private static IReadOnlyList<PathStep> Ellipse(double width, double height)
    {
        var (rx, ry) = (width / 2, height / 2);
        var (cx, cy) = (rx, ry);
        var (kx, ky) = (rx * ArcControl, ry * ArcControl);

        return
        [
            Move(cx, 0),
            Curve((cx + kx, 0), (width, cy - ky), (width, cy)),
            Curve((width, cy + ky), (cx + kx, height), (cx, height)),
            Curve((cx - kx, height), (0, cy + ky), (0, cy)),
            Curve((0, cy - ky), (cx - kx, 0), (cx, 0)),
            Close()
        ];
    }

    private static PathStep Move(double x, double y) => new(PathStepKind.Move, [(x, y)]);

    private static PathStep Line(double x, double y) => new(PathStepKind.Line, [(x, y)]);

    private static PathStep Curve(
        (double X, double Y) first, (double X, double Y) second, (double X, double Y) end) =>
        new(PathStepKind.Curve, [first, second, end]);

    private static PathStep Close() => new(PathStepKind.Close, []);
}
