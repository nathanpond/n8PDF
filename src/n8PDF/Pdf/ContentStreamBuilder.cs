using System.Globalization;
using System.Text;

namespace n8PDF.Pdf;

/// <summary>
/// Builds a page content stream (ISO 32000-1 §8-9). Coordinates are in PDF user space:
/// points, origin at the bottom-left of the page. Callers are expected to have already
/// flipped from Word's top-left origin.
/// </summary>
internal sealed class ContentStreamBuilder
{
    private readonly MemoryStream _buffer = new();
    private bool _inText;

    public byte[] ToArray() => _buffer.ToArray();

    // ----- graphics state -----

    /// <summary>Pushes the graphics state (<c>q</c>).</summary>
    public ContentStreamBuilder Save() => Op("q");

    /// <summary>Pops the graphics state (<c>Q</c>).</summary>
    public ContentStreamBuilder Restore() => Op("Q");

    /// <summary>Sets the non-stroking colour from components in the 0..1 range.</summary>
    public ContentStreamBuilder SetFillColor(double red, double green, double blue) =>
        Op($"{N(red)} {N(green)} {N(blue)} rg");

    public ContentStreamBuilder SetStrokeColor(double red, double green, double blue) =>
        Op($"{N(red)} {N(green)} {N(blue)} RG");

    public ContentStreamBuilder SetLineWidth(double width) => Op($"{N(width)} w");

    /// <summary>How a stroke ends: 0 square at the point, 1 rounded off past it.</summary>
    public ContentStreamBuilder SetLineCap(int cap) => Op($"{cap} J");

    // ----- paths -----

    public ContentStreamBuilder MoveTo(double x, double y) => Op($"{N(x)} {N(y)} m");

    public ContentStreamBuilder LineTo(double x, double y) => Op($"{N(x)} {N(y)} l");

    public ContentStreamBuilder Rectangle(double x, double y, double width, double height) =>
        Op($"{N(x)} {N(y)} {N(width)} {N(height)} re");

    /// <summary>Fills the current path (<c>f</c>).</summary>
    public ContentStreamBuilder CurveTo(
        double x1, double y1, double x2, double y2, double x3, double y3) =>
        Op($"{N(x1)} {N(y1)} {N(x2)} {N(y2)} {N(x3)} {N(y3)} c");

    public ContentStreamBuilder ClosePath() => Op("h");

    /// <summary>
    /// Keeps what follows inside the path just drawn, and draws nothing itself.
    /// </summary>
    public ContentStreamBuilder Clip() => Op("W n");

    public ContentStreamBuilder ClipEvenOdd() => Op("W* n");

    public ContentStreamBuilder PaintShading(string name) => Op($"/{name} sh");

    public ContentStreamBuilder Fill() => Op("f");

    /// <summary>Fills by the even-odd rule rather than by the winding the path was drawn with.</summary>
    public ContentStreamBuilder FillEvenOdd() => Op("f*");

    public ContentStreamBuilder FillAndStroke(bool evenOdd = false) => Op(evenOdd ? "B*" : "B");

    /// <summary>Strokes the current path (<c>S</c>).</summary>
    public ContentStreamBuilder Stroke() => Op("S");

    // ----- text -----

    public ContentStreamBuilder BeginText()
    {
        _inText = true;
        return Op("BT");
    }

    public ContentStreamBuilder EndText()
    {
        _inText = false;
        return Op("ET");
    }

    /// <summary>Selects a font by its page-resource name and size in points.</summary>
    public ContentStreamBuilder SetFont(string resourceName, double sizePoints)
    {
        EnsureInText();
        WriteRaw("/");
        WriteRaw(resourceName);
        return Op($" {N(sizePoints)} Tf");
    }

    /// <summary>
    /// Sets the text matrix to place the next glyph run's baseline origin at (x, y).
    /// Absolute positioning per run keeps layout errors from accumulating down the page.
    /// </summary>
    public ContentStreamBuilder SetTextPosition(double x, double y)
    {
        EnsureInText();
        return Op($"1 0 0 1 {N(x)} {N(y)} Tm");
    }

    /// <summary>
    /// Sets a text matrix with a horizontal shear, used to synthesise italics for fonts that
    /// have no real oblique face.
    /// </summary>
    public ContentStreamBuilder SetTextPositionSkewed(double x, double y, double skew)
    {
        EnsureInText();
        return Op($"1 0 {N(skew)} 1 {N(x)} {N(y)} Tm");
    }

    /// <summary>
    /// Sets the text matrix outright: the whole of where the text goes, how far it is stretched
    /// each way, and how far it is turned.
    /// </summary>
    public ContentStreamBuilder SetTextMatrix(
        double a, double b, double c, double d, double e, double f)
    {
        EnsureInText();
        return Op($"{N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)} Tm");
    }

    /// <summary>Names a graphics state to draw with, which is how a PDF carries transparency.</summary>
    public ContentStreamBuilder SetGraphicsState(string resourceName) => Op($"/{resourceName} gs");

    /// <summary>Sets the text rise, used for superscript and subscript.</summary>
    public ContentStreamBuilder SetTextRise(double rise)
    {
        EnsureInText();
        return Op($"{N(rise)} Ts");
    }

    /// <summary>Sets additional spacing inserted after each character (<c>Tc</c>).</summary>
    public ContentStreamBuilder SetCharacterSpacing(double spacing)
    {
        EnsureInText();
        return Op($"{N(spacing)} Tc");
    }

    /// <summary>
    /// Sets additional spacing inserted at each space character (<c>Tw</c>).
    /// </summary>
    /// <remarks>
    /// This has no effect on the composite Identity-H fonts n8PDF embeds: the operator applies
    /// only to the single-byte code 32, and a two-byte encoding has no such code. Justification
    /// therefore goes through explicit <c>TJ</c> adjustments instead.
    /// </remarks>
    public ContentStreamBuilder SetWordSpacing(double spacing)
    {
        EnsureInText();
        return Op($"{N(spacing)} Tw");
    }

    /// <summary>
    /// Sets the text rendering mode: 0 fill, 1 stroke, 2 fill then stroke. Mode 2 with a small
    /// line width is how weight is synthesised for a family with no real bold face.
    /// </summary>
    public ContentStreamBuilder SetTextRenderMode(int mode)
    {
        EnsureInText();
        return Op($"{mode} Tr");
    }

    /// <summary>Sets horizontal scaling as a percentage; 100 is unscaled.</summary>
    public ContentStreamBuilder SetHorizontalScaling(double percent)
    {
        EnsureInText();
        return Op($"{N(percent)} Tz");
    }

    /// <summary>
    /// Shows a run of glyphs. Bytes are the font's encoding — for the Identity-H composite
    /// fonts we embed, that is two bytes per glyph index, big-endian.
    /// </summary>
    public ContentStreamBuilder ShowGlyphs(ReadOnlySpan<byte> encoded)
    {
        EnsureInText();
        WriteRaw("<");
        foreach (var b in encoded)
            WriteRaw(b.ToString("X2", CultureInfo.InvariantCulture));

        return Op("> Tj");
    }

    /// <summary>
    /// Shows glyphs with per-position adjustments (<c>TJ</c>). Adjustments are in thousandths
    /// of an em and are <em>subtracted</em> from the pen position, so a positive value pulls
    /// the following glyph left — this is how kerning is expressed.
    /// </summary>
    public ContentStreamBuilder ShowGlyphsAdjusted(IReadOnlyList<(byte[] Encoded, double Adjustment)> segments)
    {
        EnsureInText();
        WriteRaw("[");
        foreach (var (encoded, adjustment) in segments)
        {
            if (encoded.Length > 0)
            {
                WriteRaw("<");
                foreach (var b in encoded)
                    WriteRaw(b.ToString("X2", CultureInfo.InvariantCulture));
                WriteRaw(">");
            }

            if (adjustment != 0) WriteRaw(N(adjustment));
        }

        return Op("] TJ");
    }

    // ----- external objects -----

    /// <summary>Draws an XObject (an image or form) by its page-resource name.</summary>
    public ContentStreamBuilder DrawXObject(string resourceName)
    {
        WriteRaw("/");
        WriteRaw(resourceName);
        return Op(" Do");
    }

    /// <summary>
    /// Concatenates a matrix onto the CTM. Images are drawn into a unit square, so placing one
    /// means scaling by its size and translating to its corner.
    /// </summary>
    public ContentStreamBuilder Transform(double a, double b, double c, double d, double e, double f) =>
        Op($"{N(a)} {N(b)} {N(c)} {N(d)} {N(e)} {N(f)} cm");

    // ----- plumbing -----

    private void EnsureInText()
    {
        if (!_inText)
            throw new InvalidOperationException("Text operators are only valid between BeginText and EndText.");
    }

    private ContentStreamBuilder Op(string op)
    {
        WriteRaw(op);
        _buffer.WriteByte((byte)'\n');
        return this;
    }

    private void WriteRaw(string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        _buffer.Write(bytes, 0, bytes.Length);
    }

    private static string N(double value) => PdfNumber.Format(value);
}
