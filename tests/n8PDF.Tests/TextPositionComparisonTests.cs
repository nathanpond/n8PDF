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
    /// Vertical tolerance for a baseline. The worst measured difference across every fixture is
    /// 0.282pt, which is close to Word's own vertical quantum of 1/300 inch (0.24pt) — so this
    /// admits roughly one quantum of disagreement and nothing more.
    /// </summary>
    private const double BaselineTolerance = 1.0;

    /// <summary>
    /// Fixtures whose vertical geometry is allowed to diverge from Word, with the tolerance each
    /// needs and why.
    /// </summary>
    /// <remarks>
    /// Currently empty, and worth keeping that way. Every fixture agrees with Word vertically to
    /// within 0.29pt and horizontally to the last decimal place, so an entry here would record a
    /// regression rather than a known gap.
    ///
    /// Entries were listed individually rather than folded into one permissive global tolerance,
    /// so that each stayed a specific bug to drive to zero. All have now been retired, each after
    /// the cause was measured rather than assumed:
    ///
    ///   paragraph-spacing, paragraph-spacing-asymmetric — adjacent paragraph spacing collapses
    ///     to the larger of the two values instead of summing them.
    ///   line-spacing, line-spacing-multiples, font-sizes — a line-spacing multiple's extra
    ///     leading goes below the baseline, and the font's line gap belongs above the ascent.
    ///   space-after-interaction-probe — across a page break the collapse still applies, but the
    ///     previous paragraph's space-after is absorbed by the page it ended on.
    ///   styles, heading-spacing-probe — Word fills in what a document's styles leave unstated
    ///     from its own built-in definitions. See WordBuiltInStyles.
    /// </remarks>
    private static readonly Dictionary<string, (double Tolerance, string Reason)> KnownVerticalDivergences = [];

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
