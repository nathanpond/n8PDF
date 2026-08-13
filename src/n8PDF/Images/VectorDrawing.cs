namespace n8PDF.Images;

/// <summary>A colour, as three bytes.</summary>
public readonly record struct DrawingColor(byte Red, byte Green, byte Blue);

/// <summary>One step of a path, in the drawing's own coordinates.</summary>
/// <param name="Kind">What the step does: move, line, curve or close.</param>
/// <param name="Points">
/// Where it goes. A move or a line takes one point, a curve takes three — two controls and an
/// end — and a close takes none.
/// </param>
public readonly record struct PathStep(PathStepKind Kind, IReadOnlyList<(double X, double Y)> Points);

public enum PathStepKind
{
    Move,
    Line,
    Curve,
    Close
}

/// <summary>Something drawn: a path, a piece of text, or a picture.</summary>
public abstract record DrawingOperation;

/// <summary>
/// A path, filled, stroked or both.
/// </summary>
/// <param name="EvenOdd">
/// Which of the two rules decides what is inside a path that crosses itself: even-odd, or the
/// winding it was drawn with.
/// </param>
public sealed record PathOperation(
    IReadOnlyList<PathStep> Steps,
    DrawingColor? Fill,
    DrawingColor? Stroke,
    double StrokeWidth,
    bool EvenOdd) : DrawingOperation;

/// <summary>
/// A piece of text, which is left as text rather than turned into outlines: the reader that ends
/// up with it can then select it, search it and print it at whatever resolution it likes.
/// </summary>
/// <param name="X">Where the text begins, at its baseline.</param>
/// <param name="Angle">How far it is turned, anticlockwise, in degrees.</param>
public sealed record TextOperation(
    string Text,
    double X,
    double Y,
    string FontFamily,
    double SizePoints,
    bool Bold,
    bool Italic,
    DrawingColor Color,
    double Angle = 0) : DrawingOperation;

/// <summary>A picture drawn into a rectangle of the drawing.</summary>
public sealed record ImageOperation(
    ImageData Image,
    double X,
    double Y,
    double Width,
    double Height) : DrawingOperation;

/// <summary>
/// A drawing: something recorded as the things to draw rather than as the pixels they came to.
/// </summary>
/// <remarks>
/// A metafile is a list of drawing commands, so it is kept as one all the way to the PDF, where
/// the commands are written out again as PDF's own. Turning it into pixels on the way would fix it
/// at one resolution, and the whole point of a drawing is that it has none.
/// </remarks>
/// <param name="Width">The drawing's own width, in points.</param>
/// <param name="Height">The drawing's own height, in points.</param>
public sealed record VectorDrawing(
    double Width,
    double Height,
    IReadOnlyList<DrawingOperation> Operations);
