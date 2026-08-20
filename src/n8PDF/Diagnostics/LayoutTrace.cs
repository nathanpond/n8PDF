using System.Globalization;
using System.Text;
using n8PDF.Layout;

namespace n8PDF.Diagnostics;

/// <summary>
/// Serialises a laid-out document to JSON: every positioned run with its coordinates, font and
/// size.
/// </summary>
/// <remarks>
/// This is the spine of the test suite. Comparing two PDFs tells you only that they differ;
/// comparing traces names the run and the coordinate that moved, which is the difference between
/// a regression you can fix and one you can only observe.
/// </remarks>
internal static class LayoutTrace
{
    /// <summary>Decimal places retained. A thousandth of a point is far below visible.</summary>
    private const int Precision = 3;

    public static string Write(LaidOutDocument document)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"pageCount\": ").Append(document.Pages.Count).Append(",\n");
        sb.Append("  \"pages\": [\n");

        for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            var page = document.Pages[pageIndex];

            sb.Append("    {\n");
            sb.Append("      \"width\": ").Append(Number(page.WidthPoints)).Append(",\n");
            sb.Append("      \"height\": ").Append(Number(page.HeightPoints)).Append(",\n");
            sb.Append("      \"lines\": [\n");

            for (var lineIndex = 0; lineIndex < page.Lines.Count; lineIndex++)
            {
                var line = page.Lines[lineIndex];

                sb.Append("        {\n");
                sb.Append("          \"paragraph\": ").Append(line.ParagraphIndex).Append(",\n");
                sb.Append("          \"baselineY\": ").Append(Number(line.BaselineY)).Append(",\n");
                sb.Append("          \"height\": ").Append(Number(line.Height)).Append(",\n");
                sb.Append("          \"runs\": [\n");

                for (var runIndex = 0; runIndex < line.Texts.Count; runIndex++)
                {
                    var run = line.Texts[runIndex];

                    sb.Append("            {");
                    sb.Append("\"x\": ").Append(Number(run.X));
                    sb.Append(", \"width\": ").Append(Number(run.Width));
                    sb.Append(", \"font\": ").Append(String(run.Font.Font.FamilyName));
                    sb.Append(", \"size\": ").Append(Number(run.FontSizePoints));

                    if (run.Format.Bold) sb.Append(", \"bold\": true");
                    if (run.Format.Italic) sb.Append(", \"italic\": true");
                    if (run.Font.SyntheticBold) sb.Append(", \"syntheticBold\": true");
                    if (run.Font.SyntheticItalic) sb.Append(", \"syntheticItalic\": true");
                    if (run.Format.ColorHex is not null)
                        sb.Append(", \"color\": ").Append(String(run.Format.ColorHex));

                    sb.Append(", \"text\": ").Append(String(run.Text));
                    sb.Append('}');
                    sb.Append(runIndex < line.Texts.Count - 1 ? ",\n" : "\n");
                }

                sb.Append("          ]\n");
                sb.Append("        }");
                sb.Append(lineIndex < page.Lines.Count - 1 ? ",\n" : "\n");
            }

            sb.Append("      ]\n");
            sb.Append("    }");
            sb.Append(pageIndex < document.Pages.Count - 1 ? ",\n" : "\n");
        }

        sb.Append("  ]\n");
        sb.Append("}\n");

        return sb.ToString();
    }

    /// <summary>A compact one-line-per-run rendering, for eyeballing a diff in a terminal.</summary>
    public static string WriteSummary(LaidOutDocument document)
    {
        var sb = new StringBuilder();

        for (var pageIndex = 0; pageIndex < document.Pages.Count; pageIndex++)
        {
            var page = document.Pages[pageIndex];
            sb.Append(CultureInfo.InvariantCulture, $"page {pageIndex + 1} ({Number(page.WidthPoints)} x {Number(page.HeightPoints)})\n");

            foreach (var line in page.Lines)
            {
                foreach (var run in line.Texts)
                {
                    sb.Append(CultureInfo.InvariantCulture,
                        $"  x={Number(run.X),-9} y={Number(run.BaselineY),-9} {run.Font.Font.FamilyName} {Number(run.FontSizePoints)}pt  \"{run.Text}\"\n");
                }
            }
        }

        return sb.ToString();
    }

    private static string Number(double value)
    {
        var rounded = Math.Round(value, Precision);

        // Avoid "-0", which would make an otherwise identical trace compare unequal.
        if (rounded == 0) rounded = 0;

        return rounded.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string String(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20)
                        sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(ch);
                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
