using n8PDF.Fonts;
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

    /// <summary>
    /// The reference size the string is measured at. Nothing turns on it: the letters are scaled
    /// to the shape either way, and a hundred keeps the arithmetic away from both ends of what a
    /// double can say.
    /// </summary>
    private const double ReferenceSize = 100;

    public static VectorDrawing Draw(
        ShapeFrame shape, double width, double height, DocumentTheme theme,
        FontLibrary? fonts = null)
    {
        // A word on a path is drawn as the word: what the shape says it is filled with paints the
        // letters, and the shape itself is not drawn at all.
        if (shape.WordArt is { } word && fonts is not null)
        {
            if (WordArt(shape, word, width, height, theme, fonts) is { } drawn)
                return new VectorDrawing(width, height, [drawn]);
        }

        var fill = Resolve(shape.Fill, theme);
        var stroke = Resolve(shape.Line, theme);

        var steps = Path(shape.Geometry, width, height);

        if (shape.RotationDegrees != 0)
            steps = Turn(steps, shape.RotationDegrees, width / 2, height / 2);

        // An old-style shape is drawn a little way down and to the right of its own box, which is
        // carried here as an offset on every point of the path rather than as a move of the box:
        // the box is what the text around it was laid out against, and only the drawing shifts.
        if (shape.DrawnOffsetPoints != 0) steps = Shift(steps, shape.DrawnOffsetPoints);

        return new VectorDrawing(width, height, [
            new PathOperation(steps, fill, stroke, shape.LineWidthPoints, EvenOdd: false)
        ]);
    }

    /// <summary>
    /// A word stretched to fill the shape, turned with it, or null where the face cannot say how
    /// large its letters are.
    /// </summary>
    /// <remarks>
    /// What is fitted is the ink and not the type: measured from watermark-fit-probe, where the
    /// same box comes out holding the same rectangle of ink whether it is asked for DRAFT, for
    /// CONFIDENTIAL, for a word with a tail below the line, or for the same word in another face.
    /// The rectangle is the shape less its own insets, the tenth of an inch at the sides and half
    /// of that above and below that every text box has.
    /// </remarks>
    private static DrawingOperation? WordArt(
        ShapeFrame shape, ShapeWordArt word, double width, double height,
        DocumentTheme theme, FontLibrary fonts)
    {
        if (!fonts.TryResolve(word.FontFamily, word.Bold, word.Italic, out var selection))
            return null;

        if (Ink(selection.Font, word.Text) is not { } ink) return null;

        var areaLeft = shape.InsetLeftPoints;
        var areaTop = shape.InsetTopPoints;
        var areaWidth = width - shape.InsetLeftPoints - shape.InsetRightPoints;
        var areaHeight = height - shape.InsetTopPoints - shape.InsetBottomPoints;

        if (areaWidth <= 0 || areaHeight <= 0 || ink.Width <= 0 || ink.Height <= 0) return null;

        var scaleX = areaWidth / ink.Width;
        var scaleY = areaHeight / ink.Height;

        // Where the letters begin, so that their ink lands on the area rather than beside it.
        var x = areaLeft - ink.Left * scaleX;
        var y = areaTop + ink.Top * scaleY;

        if (shape.RotationDegrees != 0)
            (x, y) = Turn(x, y, shape.RotationDegrees, width / 2, height / 2);

        return new WordArtOperation(
            word.Text, x, y, word.FontFamily, ReferenceSize,
            word.Bold || selection.SyntheticBold, word.Italic || selection.SyntheticItalic,
            Resolve(shape.Fill, theme) ?? new DrawingColor(0, 0, 0),
            scaleX, scaleY, shape.RotationDegrees, shape.FillOpacity);
    }

    /// <summary>
    /// The box a string's ink fills, in points at <see cref="ReferenceSize"/>, measured from the
    /// point the first letter begins at and from its baseline.
    /// </summary>
    /// <remarks>
    /// A face that keeps no box for its glyphs — a PostScript-outlined one — is answered from its
    /// own overall box instead, which is the largest any of its letters could be and so sets the
    /// word a little small rather than not at all.
    /// </remarks>
    private static (double Left, double Top, double Width, double Height)? Ink(
        TrueTypeFont font, string text)
    {
        var perUnit = ReferenceSize / font.UnitsPerEm;

        double pen = 0, left = double.MaxValue, right = double.MinValue;
        double top = double.MinValue, bottom = double.MaxValue;

        foreach (var rune in text.EnumerateRunes())
        {
            var glyph = font.GetGlyphIndex(rune.Value);
            var advance = font.GetAdvanceWidth(glyph);

            if (font.GetGlyphBounds(glyph) is { } bounds)
            {
                left = Math.Min(left, pen + bounds.MinX);
                right = Math.Max(right, pen + bounds.MaxX);
                top = Math.Max(top, bounds.MaxY);
                bottom = Math.Min(bottom, bounds.MinY);
            }

            pen += advance;
        }

        if (left > right)
        {
            // Nothing said where its ink is, so the face's own box stands for every letter of it.
            left = 0;
            right = pen;
            top = font.Metrics.BBoxMaxY;
            bottom = font.Metrics.BBoxMinY;

            if (right <= 0 || top <= bottom) return null;
        }

        return (left * perUnit, top * perUnit, (right - left) * perUnit, (top - bottom) * perUnit);
    }

    /// <summary>Turns a path clockwise about a point.</summary>
    private static IReadOnlyList<PathStep> Turn(
        IReadOnlyList<PathStep> steps, double degrees, double centreX, double centreY) =>
        [.. steps.Select(step => step with
        {
            Points = [.. step.Points.Select(point =>
                Turn(point.X, point.Y, degrees, centreX, centreY))]
        })];

    /// <summary>
    /// Turns one point clockwise about another. Clockwise is what the page sees, the axes here
    /// running down rather than up.
    /// </summary>
    private static (double X, double Y) Turn(
        double x, double y, double degrees, double centreX, double centreY)
    {
        var radians = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        var (dx, dy) = (x - centreX, y - centreY);

        return (centreX + dx * cos - dy * sin, centreY + dx * sin + dy * cos);
    }

    private static IReadOnlyList<PathStep> Shift(IReadOnlyList<PathStep> steps, double by) =>
        [.. steps.Select(step => step with
        {
            Points = [.. step.Points.Select(point => (point.X + by, point.Y + by))]
        })];

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
