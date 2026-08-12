namespace n8PDF.Tests.Support.PdfReading;

/// <summary>A run of text as positioned by a PDF's content stream.</summary>
/// <param name="PageIndex">Zero-based page.</param>
/// <param name="X">Device-space X of the run's origin, in points from the left edge.</param>
/// <param name="BaselineY">
/// Distance from the <em>top</em> of the page down to the baseline. PDF user space measures up
/// from the bottom; this is flipped on extraction so that both sides of a comparison are in the
/// same frame as n8PDF's layout model.
/// </param>
public sealed record ExtractedTextRun(
    int PageIndex,
    double X,
    double BaselineY,
    string Text,
    string FontFamily,
    double FontSize,
    double Width);

/// <summary>
/// Interprets page content streams and reports where the text actually landed.
/// </summary>
/// <remarks>
/// This is what makes fidelity measurable rather than asserted: it reads Word's own PDF and ours
/// through the same code path, so a difference in the output is a difference in the documents
/// rather than in how they were inspected.
///
/// A 2x3 affine matrix is enough — PDF's matrices are always [a b c d e f] with the third column
/// fixed at [0 0 1].
/// </remarks>
public static class PdfTextExtractor
{
    public static List<ExtractedTextRun> Extract(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        var runs = new List<ExtractedTextRun>();

        foreach (var page in reader.GetPages())
        {
            var fonts = LoadFonts(reader, page);
            ExtractPage(reader, page, fonts, runs);
        }

        return runs;
    }

    public static List<ExtractedTextRun> ExtractFile(string path) => Extract(File.ReadAllBytes(path));

    private static Dictionary<string, PdfFontInfo> LoadFonts(PdfFileReader reader, PdfPageInfo page)
    {
        var fonts = new Dictionary<string, PdfFontInfo>(StringComparer.Ordinal);

        if (reader.GetEntry(page.Resources, "Font") is not PdfDictValue fontResources) return fonts;

        foreach (var (name, value) in fontResources.Entries)
        {
            if (reader.Resolve(value) is PdfDictValue font)
                fonts[name] = PdfFontInfo.Load(reader, name, font);
        }

        return fonts;
    }

    private static void ExtractPage(
        PdfFileReader reader, PdfPageInfo page, Dictionary<string, PdfFontInfo> fonts, List<ExtractedTextRun> runs)
    {
        var content = reader.GetPageContent(page);
        if (content.Length == 0) return;

        var parser = new PdfParser(content);
        var operands = new List<PdfValue>();

        var graphicsStack = new Stack<Matrix>();
        var ctm = Matrix.Identity;

        var textMatrix = Matrix.Identity;
        var lineMatrix = Matrix.Identity;

        PdfFontInfo? font = null;
        var fontSize = 0.0;
        var charSpacing = 0.0;
        var wordSpacing = 0.0;
        var horizontalScale = 1.0;
        var leading = 0.0;
        var rise = 0.0;

        while (parser.ReadValue() is { } value)
        {
            if (value is not PdfOperatorValue op)
            {
                operands.Add(value);

                // A malformed stream could otherwise accumulate operands without bound.
                if (operands.Count > 64) operands.RemoveAt(0);
                continue;
            }

            switch (op.Operator)
            {
                case "q":
                    graphicsStack.Push(ctm);
                    break;

                case "Q":
                    if (graphicsStack.Count > 0) ctm = graphicsStack.Pop();
                    break;

                case "cm":
                    if (operands.Count >= 6) ctm = MatrixFrom(operands, operands.Count - 6).Multiply(ctm);
                    break;

                case "BT":
                    textMatrix = Matrix.Identity;
                    lineMatrix = Matrix.Identity;
                    break;

                case "ET":
                    break;

                case "Tf":
                    if (operands.Count >= 2)
                    {
                        if (operands[^2] is PdfNameValue fontName) fonts.TryGetValue(fontName.Name, out font);
                        fontSize = Number(operands[^1]);
                    }

                    break;

                case "Tc":
                    if (operands.Count >= 1) charSpacing = Number(operands[^1]);
                    break;

                case "Tw":
                    if (operands.Count >= 1) wordSpacing = Number(operands[^1]);
                    break;

                case "Tz":
                    if (operands.Count >= 1) horizontalScale = Number(operands[^1]) / 100.0;
                    break;

                case "TL":
                    if (operands.Count >= 1) leading = Number(operands[^1]);
                    break;

                case "Ts":
                    if (operands.Count >= 1) rise = Number(operands[^1]);
                    break;

                case "Tm":
                    if (operands.Count >= 6)
                    {
                        lineMatrix = MatrixFrom(operands, operands.Count - 6);
                        textMatrix = lineMatrix;
                    }

                    break;

                case "Td":
                    if (operands.Count >= 2)
                    {
                        lineMatrix = Matrix.Translation(Number(operands[^2]), Number(operands[^1])).Multiply(lineMatrix);
                        textMatrix = lineMatrix;
                    }

                    break;

                case "TD":
                    if (operands.Count >= 2)
                    {
                        // TD also sets the leading to the negated vertical displacement.
                        leading = -Number(operands[^1]);
                        lineMatrix = Matrix.Translation(Number(operands[^2]), Number(operands[^1])).Multiply(lineMatrix);
                        textMatrix = lineMatrix;
                    }

                    break;

                case "T*":
                    lineMatrix = Matrix.Translation(0, -leading).Multiply(lineMatrix);
                    textMatrix = lineMatrix;
                    break;

                case "Tj":
                case "'":
                case "\"":
                    if (op.Operator != "Tj")
                    {
                        // Both move to the next line first; the quote form also sets spacing.
                        if (op.Operator == "\"" && operands.Count >= 3)
                        {
                            wordSpacing = Number(operands[^3]);
                            charSpacing = Number(operands[^2]);
                        }

                        lineMatrix = Matrix.Translation(0, -leading).Multiply(lineMatrix);
                        textMatrix = lineMatrix;
                    }

                    if (operands.Count >= 1 && operands[^1] is PdfStringValue show)
                    {
                        ShowText(show.Bytes, ref textMatrix, ctm, page, font, fontSize, charSpacing,
                            wordSpacing, horizontalScale, rise, runs);
                    }

                    break;

                case "TJ":
                    if (operands.Count >= 1 && operands[^1] is PdfArrayValue array)
                    {
                        foreach (var item in array.Items)
                        {
                            switch (item)
                            {
                                case PdfStringValue segment:
                                    ShowText(segment.Bytes, ref textMatrix, ctm, page, font, fontSize,
                                        charSpacing, wordSpacing, horizontalScale, rise, runs);
                                    break;

                                case PdfNumberValue adjustment:
                                    // Adjustments are thousandths of an em, subtracted from the
                                    // pen position, and scale with the font size and Tz.
                                    var shift = -adjustment.Value / 1000.0 * fontSize * horizontalScale;
                                    textMatrix = Matrix.Translation(shift, 0).Multiply(textMatrix);
                                    break;
                            }
                        }
                    }

                    break;
            }

            operands.Clear();
        }
    }

    private static void ShowText(
        byte[] bytes, ref Matrix textMatrix, Matrix ctm, PdfPageInfo page, PdfFontInfo? font, double fontSize,
        double charSpacing, double wordSpacing, double horizontalScale, double rise, List<ExtractedTextRun> runs)
    {
        if (font is null || bytes.Length == 0) return;

        // The text-space to device-space transform, without the font parameters.
        var toDevice = textMatrix.Multiply(ctm);

        // The parameter matrix is a single construction, not a composition: horizontal scaling
        // and size on the diagonal, rise in the f slot (ISO 32000-1 §9.4.4).
        var parameters = new Matrix(fontSize * horizontalScale, 0, 0, fontSize, 0, rise);
        var render = parameters.Multiply(toDevice);

        var originX = render.E;
        var originY = render.F;

        var text = new System.Text.StringBuilder();
        var advance = 0.0;

        foreach (var code in font.DecodeCodes(bytes))
        {
            text.Append(font.GetText(code));

            var glyphWidth = font.GetWidth(code) / 1000.0 * fontSize;
            var extra = charSpacing;

            // Word spacing applies to single-byte code 32 only — never inside a two-byte code,
            // which is why n8PDF justifies through TJ adjustments instead.
            if (code == 32 && font.BytesPerCode == 1) extra += wordSpacing;

            advance += (glyphWidth + extra) * horizontalScale;
        }

        textMatrix = Matrix.Translation(advance, 0).Multiply(textMatrix);

        var body = text.ToString();
        if (body.Length == 0) return;

        // The advance accumulated above is in text space, which is the right unit for moving the
        // text matrix but not for reporting. Word writes "Tf /TT2 1" and puts the real size in
        // the text matrix, so its text space is scaled by 12 for 12pt text; reporting the raw
        // advance would understate every width by that factor.
        var horizontalScaleToDevice = Math.Sqrt(toDevice.A * toDevice.A + toDevice.B * toDevice.B);
        var deviceAdvance = advance * (double.IsFinite(horizontalScaleToDevice) && horizontalScaleToDevice > 0
            ? horizontalScaleToDevice
            : 1);

        // The size text renders at is the nominal size scaled by the vertical magnitude of the
        // same transform. For an unscaled page that is just the nominal size.
        var verticalScale = Math.Sqrt(toDevice.C * toDevice.C + toDevice.D * toDevice.D);
        var effectiveSize = fontSize * (double.IsFinite(verticalScale) && verticalScale > 0 ? verticalScale : 1);

        runs.Add(new ExtractedTextRun(
            page.Index,
            Math.Round(originX, 4),
            // Flip into a top-left origin so both sides of a comparison share n8PDF's frame.
            Math.Round(page.Height - originY, 4),
            body,
            font.FamilyName,
            Math.Round(effectiveSize, 4),
            Math.Round(deviceAdvance, 4)));
    }

    private static double Number(PdfValue value) => value is PdfNumberValue n ? n.Value : 0;

    private static Matrix MatrixFrom(List<PdfValue> operands, int start) => new(
        Number(operands[start]), Number(operands[start + 1]), Number(operands[start + 2]),
        Number(operands[start + 3]), Number(operands[start + 4]), Number(operands[start + 5]));

    /// <summary>A 2x3 affine transform, the only shape PDF matrices take.</summary>
    public readonly record struct Matrix(double A, double B, double C, double D, double E, double F)
    {
        public static readonly Matrix Identity = new(1, 0, 0, 1, 0, 0);

        public static Matrix Translation(double x, double y) => new(1, 0, 0, 1, x, y);

        public static Matrix Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);

        /// <summary>Returns this matrix concatenated with <paramref name="other"/> (this × other).</summary>
        public Matrix Multiply(Matrix other) => new(
            A * other.A + B * other.C,
            A * other.B + B * other.D,
            C * other.A + D * other.C,
            C * other.B + D * other.D,
            E * other.A + F * other.C + other.E,
            E * other.B + F * other.D + other.F);
    }
}
