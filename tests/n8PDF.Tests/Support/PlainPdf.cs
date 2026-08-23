using System.Globalization;
using System.Text;

namespace n8PDF.Tests.Support;

/// <summary>
/// A one-page PDF of filled polygons, written by hand.
/// </summary>
/// <remarks>
/// Deliberately not built with the library. This exists to give <see cref="BoxSilhouette"/> a page
/// whose corners are known exactly, and an instrument checked against the code it will be used to
/// check is not checked at all. Nothing here reads a n8PDF type, so a fault in the PDF writer can
/// neither hide nor cause a fault in the instrument.
///
/// Written to the letter of what a rasteriser needs and no more: no fonts, no compression, no
/// object streams, and a cross-reference table counted out by hand.
/// </remarks>
internal static class PlainPdf
{
    public const double Width = 612;
    public const double Height = 792;

    /// <summary>A page of filled polygons, each given in points from the page's top-left corner.</summary>
    public static byte[] Of(IEnumerable<(IReadOnlyList<(double X, double Y)> Points, (byte R, byte G, byte B) Fill)> shapes)
    {
        var content = new StringBuilder();

        foreach (var (points, fill) in shapes)
        {
            content.Append(CultureInfo.InvariantCulture,
                $"{fill.R / 255.0:0.####} {fill.G / 255.0:0.####} {fill.B / 255.0:0.####} rg\n");

            for (var i = 0; i < points.Count; i++)
                content.Append(CultureInfo.InvariantCulture,
                    // PDF measures up from the foot of the page and everything else here measures
                    // down from its head, so the flip happens once, here.
                    $"{points[i].X:0.####} {Height - points[i].Y:0.####} {(i == 0 ? "m" : "l")}\n");

            content.Append("h f\n");
        }

        var stream = content.ToString();

        var objects = new List<string>
        {
            "<</Type/Catalog/Pages 2 0 R>>",
            "<</Type/Pages/Kids[3 0 R]/Count 1>>",
            $"<</Type/Page/Parent 2 0 R/MediaBox[0 0 {Width:0} {Height:0}]/Contents 4 0 R/Resources<<>>>>",
            $"<</Length {Encoding.ASCII.GetByteCount(stream)}>>\nstream\n{stream}endstream"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();

        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var startxref = Encoding.ASCII.GetByteCount(pdf.ToString());

        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) pdf.Append(CultureInfo.InvariantCulture, $"{offset:0000000000} 00000 n \n");

        pdf.Append(CultureInfo.InvariantCulture,
            $"trailer\n<</Size {objects.Count + 1}/Root 1 0 R>>\nstartxref\n{startxref}\n%%EOF\n");

        return Encoding.ASCII.GetBytes(pdf.ToString());
    }
}
