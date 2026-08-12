using System.Text;
using System.Text.RegularExpressions;
using n8PDF.Pdf;

namespace n8PDF.Tests.Support;

/// <summary>Page geometry read back out of a PDF.</summary>
/// <param name="PageCount">Number of page objects found.</param>
/// <param name="MediaBoxes">Each page's media box as (width, height) in points.</param>
public sealed record PdfGeometry(int PageCount, IReadOnlyList<(double Width, double Height)> MediaBoxes);

/// <summary>
/// Reads structural facts back out of a PDF, including ones we did not write.
/// </summary>
/// <remarks>
/// This is a heuristic scanner, not a parser: it searches the raw bytes and the contents of every
/// inflatable stream for page objects and media boxes. That second part matters because Word
/// writes cross-reference and object streams, so a PDF it produced has its page dictionaries
/// hidden inside compressed streams where a plain text search finds nothing.
///
/// Good enough to compare pagination and page size against a reference. Comparing per-run text
/// positions needs a real content-stream parser, which is the next step in this harness.
/// </remarks>
public static class PdfInspector
{
    // Word writes "/Type/Page" with no space; we write "/Type /Page". The negative lookahead
    // keeps "/Pages" — the page-tree node — from being counted as a page.
    private static readonly Regex PageObject = new(@"/Type\s*/Page(?![a-zA-Z])", RegexOptions.Compiled);

    private static readonly Regex MediaBox = new(
        @"/MediaBox\s*\[\s*(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)\s*\]",
        RegexOptions.Compiled);

    public static PdfGeometry Inspect(byte[] pdf)
    {
        var haystacks = new List<string> { Encoding.Latin1.GetString(pdf) };
        haystacks.AddRange(InflateStreams(pdf));

        var pageCount = 0;
        var boxes = new List<(double Width, double Height)>();

        foreach (var haystack in haystacks)
        {
            pageCount += PageObject.Matches(haystack).Count;

            foreach (Match match in MediaBox.Matches(haystack))
            {
                var x0 = double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                var y0 = double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                var x1 = double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);
                var y1 = double.Parse(match.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture);

                boxes.Add((Math.Abs(x1 - x0), Math.Abs(y1 - y0)));
            }
        }

        return new PdfGeometry(pageCount, boxes);
    }

    public static PdfGeometry InspectFile(string path) => Inspect(File.ReadAllBytes(path));

    /// <summary>Inflates every Flate-compressed stream in the file, ignoring those that fail.</summary>
    private static List<string> InflateStreams(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var results = new List<string>();
        var index = 0;

        while (true)
        {
            var keyword = text.IndexOf("stream", index, StringComparison.Ordinal);
            if (keyword < 0) break;

            // Skip "endstream" matches, and step over the end-of-line that follows the keyword.
            if (keyword >= 3 && text.Substring(keyword - 3, 3) == "end")
            {
                index = keyword + 6;
                continue;
            }

            var dataStart = keyword + "stream".Length;
            if (dataStart < text.Length && text[dataStart] == '\r') dataStart++;
            if (dataStart < text.Length && text[dataStart] == '\n') dataStart++;

            var end = text.IndexOf("endstream", dataStart, StringComparison.Ordinal);
            if (end < 0) break;

            var length = end - dataStart;
            if (length > 0)
            {
                var data = new byte[length];
                Array.Copy(pdf, dataStart, data, 0, length);

                try
                {
                    results.Add(Encoding.Latin1.GetString(PdfFilters.FlateDecode(data)));
                }
                catch (InvalidDataException)
                {
                    // Not Flate, or trailing bytes confused the decoder. Font programs and JPEGs
                    // land here and hold nothing we are looking for.
                }
            }

            index = end + "endstream".Length;
        }

        return results;
    }
}
