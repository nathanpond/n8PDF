using System.Text;
using n8PDF;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tier 3, per-line: compares where n8PDF puts text against where Word puts it.
/// </summary>
/// <remarks>
/// This is the measurement the whole harness exists for. Pagination agreeing says almost nothing
/// on a one-page document; this says whether the lines are in the same places, in points.
///
/// Tolerances here are deliberately explicit constants rather than something generous enough to
/// always pass. When one is raised it should be because the difference was understood and
/// accepted, not because the test was in the way.
/// </remarks>
public class TextPositionComparisonTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Horizontal tolerance for a line's starting position. Measured difference is currently
    /// exactly zero on every fixture, so this is tight on purpose.
    /// </summary>
    private const double StartXTolerance = 0.5;

    /// <summary>Tolerance for a line's rendered width. Worst measured is about 2pt.</summary>
    private const double WidthTolerance = 2.5;

    /// <summary>
    /// Vertical tolerance for a baseline. There is a systematic offset of roughly half a point
    /// against Word on every fixture — the first baseline sits slightly low — so this admits that
    /// but nothing more.
    /// </summary>
    private const double BaselineTolerance = 1.0;

    /// <summary>
    /// Fixtures whose vertical geometry is known to diverge, with the tolerance each currently
    /// needs and why.
    /// </summary>
    /// <remarks>
    /// Listed individually rather than folded into one permissive global tolerance. A single
    /// number large enough to cover the worst case here would be 14pt, which is more than a whole
    /// line at 12pt — it would let any vertical regression through unnoticed. Each entry is a
    /// known bug to be driven to zero, and shrinking one of these numbers is the measure of
    /// progress.
    /// </remarks>
    private static readonly Dictionary<string, (double Tolerance, string Reason)> KnownVerticalDivergences = new()
    {
        // Retired, each after the underlying bug was found and fixed:
        //   paragraph-spacing (13pt) and paragraph-spacing-asymmetric (25pt) — adjacent paragraph
        //     spacing collapses to the maximum rather than summing.
        //   line-spacing (14pt) and line-spacing-multiples (14pt) — a multiple's extra leading
        //     goes below the baseline, and the line gap sits above the ascent.
        //   font-sizes (1.3pt) — fixed by the same line-gap change.

        // Everything remaining traces to one cause, and it is not a layout rule of ours.
        //
        // Word merges its own built-in style definitions into a document's. Our probe styles
        // declare Normal with no pPr at all, and Word supplies its template's values for what is
        // missing — w:line=259 (a 1.079 multiple) and w:after=160 (8pt). Both then apply to every
        // paragraph that inherits from Normal without saying otherwise.
        //
        // The evidence is a clean contrast between two probes measuring the same geometry. A 20pt
        // paragraph followed by a 12pt one, nothing between them:
        //     line-box-probe, which declares w:line=240 explicitly -> Word 15.36, ours 15.53
        //     space-after-interaction-probe, which declares no line -> Word 18.96, ours 15.53
        // The only difference is whether the paragraph states its own line spacing. Where it does,
        // we agree with Word; where it does not, Word substitutes its template's value and we use
        // the document's stated default.
        //
        // Replicating this means shipping Word's built-in style table — its template defaults
        // rather than the document's content — which is a decision about what n8PDF should be,
        // not a bug to fix quietly. Left as-is deliberately.
        //
        // Ruled out along the way, each now covered by a permanent fixture:
        //   Line box geometry — line-box-probe agrees to 0.28pt across three size pairings.
        //   Built-in style names — heading-spacing-probe shows "heading 2" behaving like a custom
        //     style, so Word is not keying off the name.
        //   Space-before at the top of a page — page-break-spacing-probe agrees to 0.048pt.

        ["styles"] = (6.0,
            "Body text inheriting Normal, which our styles.xml leaves empty and Word fills in " +
            "from its template. See above."),

        ["heading-spacing-probe"] = (8.0,
            "Same cause. Its headings match to 0.05pt; the divergence is in the body paragraphs."),

        ["space-after-interaction-probe"] = (3.6,
            "Same cause, and the probe that isolated it. Its four headings match to 0.05pt after " +
            "the page-break collapsing fix; only the body paragraphs still differ.")
    };

    public static TheoryData<string> FixtureNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Fixtures.All.Keys) data.Add(name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Line_positions_are_within_tolerance_of_word(string name)
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, name + ".pdf");
        if (!File.Exists(referencePath)) return; // reported by Fixture_has_a_reference_pdf

        var report = Compare(name, referencePath);
        _output.WriteLine(report.ToText());

        Assert.True(report.UnmatchedCount == 0,
            $"'{name}': {report.UnmatchedCount} line(s) had no counterpart.\n{report.ToText()}");

        Assert.True(report.TextMismatchCount == 0,
            $"'{name}': {report.TextMismatchCount} line(s) differ in text.\n{report.ToText()}");

        Assert.True(report.MaxAbsStartXDelta <= StartXTolerance,
            $"'{name}': a line starts {report.MaxAbsStartXDelta:0.###}pt from where Word puts it " +
            $"(tolerance {StartXTolerance}pt).\n{report.ToText()}");

        Assert.True(report.MaxAbsWidthDelta <= WidthTolerance,
            $"'{name}': a line's width differs from Word's by {report.MaxAbsWidthDelta:0.###}pt " +
            $"(tolerance {WidthTolerance}pt).\n{report.ToText()}");

        var (baselineTolerance, reason) = KnownVerticalDivergences.TryGetValue(name, out var known)
            ? known
            : (BaselineTolerance, string.Empty);

        Assert.True(report.MaxAbsBaselineDelta <= baselineTolerance,
            $"'{name}': a baseline sits {report.MaxAbsBaselineDelta:0.###}pt from Word's " +
            $"(tolerance {baselineTolerance}pt).{(reason.Length > 0 ? "\nKnown divergence: " + reason : "")}\n" +
            report.ToText());

        // A known divergence that has shrunk well inside its allowance means the underlying bug
        // was fixed and the entry should be tightened, or the tolerance stops measuring anything.
        if (reason.Length > 0 && report.MaxAbsBaselineDelta < baselineTolerance / 2)
        {
            Assert.Fail(
                $"'{name}' is listed as a known vertical divergence with a {baselineTolerance}pt " +
                $"tolerance, but now differs by only {report.MaxAbsBaselineDelta:0.###}pt. " +
                "Tighten or remove the entry in KnownVerticalDivergences.");
        }
    }

    /// <summary>
    /// Writes the full per-fixture comparison to the artifacts directory and prints the summary.
    /// This is the fidelity scoreboard: the numbers to drive down.
    /// </summary>
    [Fact]
    public void Fidelity_report()
    {
        var summary = new StringBuilder();
        var full = new StringBuilder();

        summary.Append($"{"fixture",-24} {"lines",5} {"max|dx|",8} {"max|dy|",8} {"mean dy",8} {"max|dw|",8}\n");
        summary.Append(new string('-', 66)).Append('\n');

        double worstX = 0, worstY = 0, worstW = 0;

        foreach (var name in Fixtures.All.Keys.OrderBy(n => n))
        {
            var referencePath = Path.Combine(TestPaths.ReferencePdfs, name + ".pdf");
            if (!File.Exists(referencePath)) continue;

            var report = Compare(name, referencePath);
            full.Append(report.ToText()).Append('\n');

            summary.Append(
                $"{name,-24} {report.LineCount,5} {report.MaxAbsStartXDelta,8:0.###} " +
                $"{report.MaxAbsBaselineDelta,8:0.###} {report.MeanBaselineDelta,8:+0.###;-0.###;0} " +
                $"{report.MaxAbsWidthDelta,8:0.###}\n");

            worstX = Math.Max(worstX, report.MaxAbsStartXDelta);
            worstY = Math.Max(worstY, report.MaxAbsBaselineDelta);
            worstW = Math.Max(worstW, report.MaxAbsWidthDelta);
        }

        summary.Append(new string('-', 66)).Append('\n');
        summary.Append($"{"worst",-24} {"",5} {worstX,8:0.###} {worstY,8:0.###} {"",8} {worstW,8:0.###}\n");

        var path = TestPaths.WriteArtifact("fidelity-report.txt",
            Encoding.UTF8.GetBytes(summary.ToString() + "\n\n" + full));

        _output.WriteLine(summary.ToString());
        _output.WriteLine($"Full per-line report: {path}");
    }

    private static ComparisonReport Compare(string name, string referencePath)
    {
        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };
        var ours = Converter.Convert(Fixtures.Build(name), options);

        return PdfLineComparison.Compare(name, ours, File.ReadAllBytes(referencePath));
    }
}
