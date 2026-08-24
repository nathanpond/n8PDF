namespace n8PDF.Images;

/// <summary>A colour, as three bytes.</summary>
internal readonly record struct DrawingColor(byte Red, byte Green, byte Blue);

/// <summary>One step of a path, in the drawing's own coordinates.</summary>
/// <param name="Kind">What the step does: move, line, curve or close.</param>
/// <param name="Points">
/// Where it goes. A move or a line takes one point, a curve takes three — two controls and an
/// end — and a close takes none.
/// </param>
internal readonly record struct PathStep(PathStepKind Kind, IReadOnlyList<(double X, double Y)> Points);

internal enum PathStepKind
{
    Move,
    Line,
    Curve,
    Close
}

/// <summary>Something drawn: a path, a piece of text, or a picture.</summary>
internal abstract record DrawingOperation;

/// <summary>
/// A path, filled, stroked or both.
/// </summary>
/// <param name="EvenOdd">
/// Which of the two rules decides what is inside a path that crosses itself: even-odd, or the
/// winding it was drawn with.
/// </param>
/// <param name="Clip">
/// A rectangle to keep the path inside, where it is not to be drawn whole. What a chart draws in
/// its plot area is kept there — a bubble larger than the plot is cut off at its edge rather than
/// drawn over the axis labels, which is how Word draws one.
/// </param>
/// <param name="RoundCap">
/// Whether the stroke is rounded off past each end rather than stopping square at it. It reaches
/// half its own width further either way when it is, which is visible on a short line and is what
/// Word does with the ones a chart hangs from its points.
/// </param>
internal sealed record PathOperation(
    IReadOnlyList<PathStep> Steps,
    DrawingColor? Fill,
    DrawingColor? Stroke,
    double StrokeWidth,
    bool EvenOdd,
    (double X, double Y, double Width, double Height)? Clip = null,
    bool RoundCap = false,
    IReadOnlyList<ClipShape>? Clips = null,
    double FillOpacity = 1,
    DrawingGradient? Gradient = null) : DrawingOperation;

/// <summary>
/// A gradient painted inside a path (#64): its resolved stops in order and the axis they run
/// along, clockwise from three o'clock in the drawing's own frame.
/// </summary>
internal sealed record DrawingGradient(
    IReadOnlyList<(double Position, DrawingColor Color)> Stops, double AngleDegrees);

/// <summary>
/// One clip a metafile had in force when it drew (#69): a path of its own and the rule deciding
/// its inside. A path drawn under several is kept inside all of them — the renderer writes each
/// as a clip in turn, which is how PDF composes the same intersection.
/// </summary>
internal sealed record ClipShape(IReadOnlyList<PathStep> Steps, bool EvenOdd);

/// <summary>
/// A piece of text, which is left as text rather than turned into outlines: the reader that ends
/// up with it can then select it, search it and print it at whatever resolution it likes.
/// </summary>
/// <param name="X">Where the text begins, at its baseline.</param>
/// <param name="Angle">How far it is turned, anticlockwise, in degrees.</param>
internal sealed record TextOperation(
    string Text,
    double X,
    double Y,
    string FontFamily,
    double SizePoints,
    bool Bold,
    bool Italic,
    DrawingColor Color,
    double Angle = 0) : DrawingOperation;

/// <summary>
/// One word, stretched and turned: what a watermark is drawn as.
/// </summary>
/// <remarks>
/// Unlike the text a metafile carries, this says exactly where its baseline goes and how far the
/// letters are stretched each way, because the whole point of it is that the ink fills a box
/// rather than that the type is set at a size. It stays text rather than becoming outlines, so a
/// reader can still find the word — Word's own export turns it into paths, and the word can no
/// longer be searched for.
/// </remarks>
/// <param name="X">Where the baseline of the first letter is, in the drawing's coordinates.</param>
/// <param name="ScaleX">How far the letters are stretched across, over the size given.</param>
/// <param name="ScaleY">And down.</param>
/// <param name="AngleDegrees">How far it is turned, clockwise.</param>
/// <param name="Opacity">How solid it is, from nought to one.</param>
internal sealed record WordArtOperation(
    string Text,
    double X,
    double Y,
    string FontFamily,
    double SizePoints,
    bool Bold,
    bool Italic,
    DrawingColor Color,
    double ScaleX = 1,
    double ScaleY = 1,
    double AngleDegrees = 0,
    double Opacity = 1) : DrawingOperation;

/// <summary>A picture drawn into a rectangle of the drawing.</summary>
internal sealed record ImageOperation(
    ImageData Image,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<ClipShape>? Clips = null) : DrawingOperation;

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
internal sealed record VectorDrawing(
    double Width,
    double Height,
    IReadOnlyList<DrawingOperation> Operations);
