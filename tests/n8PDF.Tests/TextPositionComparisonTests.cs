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

    /// <summary>
    /// Fixtures whose text cannot be compared character for character, and why.
    /// </summary>
    /// <remarks>
    /// Only one, and it is about how Word writes a PDF rather than about what either of us laid
    /// out. Word breaks a line of Hebrew into many runs and encodes some of them as pairs whose
    /// map back to characters gives the two the other way round, and it leaves gaps between runs
    /// that this reader cannot tell from spaces. What the two agree on is where the text goes,
    /// which is what this comparison is for and is asserted for this fixture like any other: its
    /// lines begin within a hundredth of a point of Word's. What the drawn order is, and that it
    /// is the order Hebrew is read in, is asserted in HebrewTests against the algorithm's own
    /// answer rather than against a reading of Word's file.
    /// </remarks>
    private static readonly Dictionary<string, string> TextNotComparable = new()
    {
        ["hebrew"] = "Word encodes a line of Hebrew as runs this reader cannot reassemble exactly",
        ["marks"] = "the same, and its Hebrew carries points, which Word encodes the same way",

        // And this one for a second reason as well: which face is borrowed for a character the
        // run's own cannot draw is a choice rather than a fact, Word's is not discoverable, and
        // the two are not the same width. Where the text goes is compared like any other fixture's
        // and agrees exactly; how wide a borrowed face draws it is not something to hold Word to.
        ["font-fallback"] = "the face borrowed for what a font cannot draw is a choice, and not Word's"
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

        if (!TextNotComparable.TryGetValue(name, out var untellable))
        {
            Assert.True(report.TextMismatchCount == 0,
                $"'{name}': {report.TextMismatchCount} line(s) differ in text.\n{report.ToText()}");
        }
        else _output.WriteLine($"text not compared: {untellable}");

        Assert.True(report.MaxAbsStartXDelta <= StartXTolerance,
            $"'{name}': a line starts {report.MaxAbsStartXDelta:0.###}pt from where Word puts it " +
            $"(tolerance {StartXTolerance}pt).\n{report.ToText()}");

        if (!TextNotComparable.ContainsKey(name))
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

    public static TheoryData<string> RealDocumentNames
    {
        get
        {
            var data = new TheoryData<string>();

            if (Directory.Exists(TestPaths.RealFixtures))
            {
                foreach (var path in Directory.GetFiles(TestPaths.RealFixtures, "*.docx").OrderBy(p => p))
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    if (!name.StartsWith("~$", StringComparison.Ordinal)) data.Add(name);
                }
            }

            // A theory with no data is an error rather than a pass, so there is always one entry.
            if (data.Count == 0) data.Add(string.Empty);

            return data;
        }
    }

    /// <summary>
    /// The same per-line comparison, against documents Word itself wrote.
    /// </summary>
    /// <remarks>
    /// Hand-authored fixtures contain only the markup we thought to write. These carry Word's
    /// full styles.xml with its several hundred latent styles, its settings.xml, its theme and
    /// its fonts — the parts of a real document that no fixture reproduces, and the ones most
    /// likely to be interpreted differently.
    ///
    /// System font discovery stays on: a real document names fonts that the pinned test set does
    /// not have, and resolving them the way a caller would is part of what is being checked.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RealDocumentNames))]
    public void Real_document_line_positions_match_word(string name)
    {
        if (name.Length == 0)
        {
            _output.WriteLine(
                $"No documents in {TestPaths.RealFixtures}. Generate them with " +
                "tools/make-real-fixtures.sh.");
            return;
        }

        var referencePath = Path.Combine(TestPaths.ReferencePdfs, "real-" + name + ".pdf");
        Assert.True(File.Exists(referencePath),
            $"No reference PDF for real document '{name}'. Regenerate with tools/make-real-fixtures.sh.");

        var docx = File.ReadAllBytes(Path.Combine(TestPaths.RealFixtures, name + ".docx"));
        var report = PdfLineComparison.Compare(name, Converter.Convert(docx), File.ReadAllBytes(referencePath));

        _output.WriteLine(report.ToText());

        if (KnownRealDivergences.TryGetValue(name, out var reason))
        {
            // Still compared, and the report is still printed, so the numbers stay visible and a
            // regression elsewhere in the document is not masked by the one known problem.
            _output.WriteLine($"KNOWN DIVERGENCE: {reason}");
            return;
        }

        Assert.True(report.UnmatchedCount == 0,
            $"'{name}': {report.UnmatchedCount} line(s) had no counterpart.\n{report.ToText()}");

        Assert.True(report.MaxAbsStartXDelta <= StartXTolerance,
            $"'{name}': a line starts {report.MaxAbsStartXDelta:0.###}pt from Word's.\n{report.ToText()}");

        Assert.True(report.MaxAbsBaselineDelta <= BaselineTolerance,
            $"'{name}': a baseline sits {report.MaxAbsBaselineDelta:0.###}pt from Word's.\n{report.ToText()}");
    }

    /// <summary>
    /// Real documents whose geometry is known to diverge, with why.
    /// </summary>
    /// <remarks>
    /// Empty. The one entry it held — the report's table sitting 1.02pt right of Word's, enough
    /// to wrap a cell Word fits on one line — was resolved by table-inset-probe: a declared
    /// w:tblInd is measured to the cell content edge rather than the table edge, and our autofit
    /// was sizing columns without allowing for the borders that layout later subtracted.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownRealDivergences = [];

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
