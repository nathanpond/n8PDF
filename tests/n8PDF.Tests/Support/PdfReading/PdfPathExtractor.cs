namespace n8PDF.Tests.Support.PdfReading;

/// <summary>A filled, axis-aligned rectangle found in a content stream.</summary>
/// <param name="Top">
/// Distance from the top of the page down to the rectangle's upper edge. PDF measures up from the
/// bottom; this is flipped on extraction so both sides of a comparison share n8PDF's frame.
/// </param>
public sealed record ExtractedRectangle(int PageIndex, double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public override string ToString() =>
        $"p{PageIndex} ({Left:0.##}, {Top:0.##}) {Width:0.##}x{Height:0.##}";
}

/// <summary>
/// Reports the filled rectangles a PDF draws: rules, borders and cell shading.
/// </summary>
/// <remarks>
/// Text extraction cannot see any of these, which leaves a whole class of output — the footnote
/// separator among it — outside the comparison against Word unless they are read too.
///
/// Both ways of writing a rectangle are handled, because the two documents under comparison use
/// different ones: n8PDF emits <c>re</c>, while Word builds the same shape out of a move and three
/// lines. A path that is not an axis-aligned rectangle is ignored rather than approximated.
///
/// A stroked straight line counts too, reported as the rectangle its width covers. Word draws some
/// of its rules that way rather than by filling — the bar tab stop's, for one — and a reader that
/// only looked at fills would report the page as having no rule on it at all.
/// </remarks>
public static class PdfPathExtractor
{
    public static List<ExtractedRectangle> Extract(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        var result = new List<ExtractedRectangle>();

        foreach (var page in reader.GetPages())
            ExtractPage(reader, page, result);

        return result;
    }

    public static List<ExtractedRectangle> ExtractFile(string path) => Extract(File.ReadAllBytes(path));

    private static void ExtractPage(PdfFileReader reader, PdfPageInfo page, List<ExtractedRectangle> result)
    {
        var content = reader.GetPageContent(page);
        if (content.Length == 0) return;

        var parser = new PdfParser(content);
        var operands = new List<PdfValue>();

        var stack = new Stack<(PdfTextExtractor.Matrix Ctm, double LineWidth)>();
        var ctm = PdfTextExtractor.Matrix.Identity;
        var lineWidth = 1.0;

        // Points of the subpath being built, and the rectangles the whole path has produced.
        var points = new List<(double X, double Y)>();
        var pending = new List<ExtractedRectangle>();

        while (parser.ReadValue() is { } value)
        {
            if (value is not PdfOperatorValue op)
            {
                operands.Add(value);
                if (operands.Count > 64) operands.RemoveAt(0);
                continue;
            }

            switch (op.Operator)
            {
                case "q":
                    stack.Push((ctm, lineWidth));
                    break;

                case "Q":
                    if (stack.Count > 0) (ctm, lineWidth) = stack.Pop();
                    break;

                case "w" when operands.Count >= 1:
                    lineWidth = Number(operands[^1]);
                    break;

                case "cm":
                    if (operands.Count >= 6)
                        ctm = MatrixFrom(operands).Multiply(ctm);
                    break;

                case "re" when operands.Count >= 4:
                {
                    var x = Number(operands[^4]);
                    var y = Number(operands[^3]);
                    var width = Number(operands[^2]);
                    var height = Number(operands[^1]);

                    FlushSubpath(points, pending, ctm, page);
                    AddRectangle(pending, page, ctm,
                        [(x, y), (x + width, y), (x + width, y + height), (x, y + height)]);
                    break;
                }

                case "m" when operands.Count >= 2:
                    FlushSubpath(points, pending, ctm, page);
                    points.Add((Number(operands[^2]), Number(operands[^1])));
                    break;

                case "l" when operands.Count >= 2:
                    points.Add((Number(operands[^2]), Number(operands[^1])));
                    break;

                case "c" or "v" or "y":
                    // A curve cannot be part of a rectangle, so the subpath is abandoned.
                    points.Clear();
                    break;

                case "h":
                    FlushSubpath(points, pending, ctm, page);
                    break;

                case "f" or "F" or "f*" or "b" or "b*" or "B" or "B*":
                    FlushSubpath(points, pending, ctm, page);
                    result.AddRange(pending);
                    pending.Clear();
                    break;

                case "S" or "s":
                    // A straight stroke covers a rectangle as wide as the pen that drew it.
                    if (points.Count == 2) AddStroke(result, page, ctm, points, lineWidth);

                    points.Clear();
                    pending.Clear();
                    break;

                case "n":
                    // Discarded, most often after being used as a clipping path.
                    points.Clear();
                    pending.Clear();
                    break;
            }

            if (value is PdfOperatorValue) operands.Clear();
        }
    }

    /// <summary>Turns the subpath built so far into a rectangle, if that is what it is.</summary>
    private static void FlushSubpath(
        List<(double X, double Y)> points, List<ExtractedRectangle> pending,
        PdfTextExtractor.Matrix ctm, PdfPageInfo page)
    {
        if (points.Count is 4 or 5)
        {
            // A closed path repeats its first point; either form describes the same four corners.
            var corners = points.Count == 5 && Close(points[0], points[4])
                ? points.GetRange(0, 4)
                : points.Count == 4
                    ? points
                    : null;

            if (corners is not null) AddRectangle(pending, page, ctm, corners);
        }

        points.Clear();
    }

    private static void AddRectangle(
        List<ExtractedRectangle> pending, PdfPageInfo page, PdfTextExtractor.Matrix ctm,
        IReadOnlyList<(double X, double Y)> corners)
    {
        var transformed = corners.Select(p => Apply(ctm, p)).ToList();

        var left = transformed.Min(p => p.X);
        var right = transformed.Max(p => p.X);
        var bottom = transformed.Min(p => p.Y);
        var top = transformed.Max(p => p.Y);

        // Every corner must sit on one of the four edges, or the shape is not axis-aligned.
        foreach (var (x, y) in transformed)
        {
            if ((Math.Abs(x - left) > 0.01 && Math.Abs(x - right) > 0.01) ||
                (Math.Abs(y - bottom) > 0.01 && Math.Abs(y - top) > 0.01))
            {
                return;
            }
        }

        pending.Add(new ExtractedRectangle(
            page.Index, left, page.Height - top, right - left, top - bottom));
    }

    /// <summary>
    /// Reports an axis-aligned stroke as the rectangle its width covers. A stroke straddles its
    /// path, so the rectangle reaches half the pen's width to either side of it.
    /// </summary>
    private static void AddStroke(
        List<ExtractedRectangle> result, PdfPageInfo page, PdfTextExtractor.Matrix ctm,
        List<(double X, double Y)> points, double lineWidth)
    {
        var from = Apply(ctm, points[0]);
        var to = Apply(ctm, points[1]);

        // The pen is round in user space, so it scales with the matrix rather than with either axis.
        var scale = Math.Sqrt(Math.Abs(ctm.A * ctm.D - ctm.B * ctm.C));
        var width = Math.Max(lineWidth * scale, 0.01) / 2;

        if (Math.Abs(from.X - to.X) < 0.01)
        {
            var top = Math.Min(from.Y, to.Y);
            var bottom = Math.Max(from.Y, to.Y);

            result.Add(new ExtractedRectangle(
                page.Index, from.X - width, page.Height - bottom, width * 2, bottom - top));
        }
        else if (Math.Abs(from.Y - to.Y) < 0.01)
        {
            var left = Math.Min(from.X, to.X);
            var right = Math.Max(from.X, to.X);

            result.Add(new ExtractedRectangle(
                page.Index, left, page.Height - (from.Y + width), right - left, width * 2));
        }
    }

    private static bool Close((double X, double Y) a, (double X, double Y) b) =>
        Math.Abs(a.X - b.X) < 0.001 && Math.Abs(a.Y - b.Y) < 0.001;

    private static (double X, double Y) Apply(PdfTextExtractor.Matrix m, (double X, double Y) p) =>
        (m.A * p.X + m.C * p.Y + m.E, m.B * p.X + m.D * p.Y + m.F);

    private static PdfTextExtractor.Matrix MatrixFrom(List<PdfValue> operands)
    {
        var start = operands.Count - 6;
        return new PdfTextExtractor.Matrix(
            Number(operands[start]), Number(operands[start + 1]), Number(operands[start + 2]),
            Number(operands[start + 3]), Number(operands[start + 4]), Number(operands[start + 5]));
    }

    private static double Number(PdfValue value) => value is PdfNumberValue n ? n.Value : 0;
}
