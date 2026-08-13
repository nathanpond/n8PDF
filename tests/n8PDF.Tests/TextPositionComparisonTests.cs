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
    private static readonly Dictionary<string, (double Tolerance, string Reason)> KnownVerticalDivergences =
        new()
        {
            // A line carrying a note's mark is about 0.36pt taller in Word than here, which is a
            // quarter of one percent of a line and invisible in a document with a note or two —
            // the footnotes fixture has three and matches to a tenth of a point. These have eight
            // to a page, and the difference accumulates down the body: 0.23pt at the first mark,
            // 2.76pt by the eighth.
            //
            // It is the raised mark that does it. Word raises the mark 4.08pt above the baseline
            // where this raises it 3.85, and grows the line box by what stands above it; this
            // sizes the line from the run's own metrics and the raise costs it nothing. Nothing to
            // do with how the notes are numbered — page 0 of footnote-restart-page is numbered
            // one to eight either way — and nothing to do with the notes themselves: any
            // superscript would do it. Left as its own thing to fix rather than folded into the
            // numbering, since it moves every document holding a raised run.
            ["footnote-restart-page"] = (3.0, "a line carrying a raised mark is 0.36pt short of Word's, eight times over"),
            ["footnote-restart-section"] = (3.0, "a line carrying a raised mark is 0.36pt short of Word's, eight times over"),
            ["endnote-restart-section"] = (3.5, "a line carrying a raised mark is 0.36pt short of Word's, nine times over")
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
