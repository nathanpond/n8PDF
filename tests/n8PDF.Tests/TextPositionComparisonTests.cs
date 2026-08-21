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
    /// Vertical tolerance for a baseline. Word writes every baseline on a grid of 1/300 inch —
    /// 0.24pt — and so does this: twenty-two fixtures now agree with Word's page exactly, and most
    /// of the rest differ by a single step of that grid where a rounding falls the other way. This
    /// admits three of them, which is what the two line box probes need — math-structure-probe and
    /// east-asian-line-box-probe are the worst at 0.72pt — and nothing more.
    /// </summary>
    private const double BaselineTolerance = 0.8;

    /// <summary>
    /// Fixtures whose vertical geometry is allowed to diverge from Word, with the tolerance each
    /// needs and why.
    /// </summary>
    /// <remarks>
    /// Two entries, and worth keeping it to two. Every other fixture agrees with Word vertically
    /// to within 0.73pt and horizontally to the last decimal place, so a third entry here would
    /// record a regression rather than a known gap.
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
    /// <summary>
    /// And the one whose horizontal geometry diverges too, for the same reason as its vertical.
    /// </summary>
    private static readonly Dictionary<string, double> KnownHorizontalDivergences = new()
    {
        ["chart-title-legend-label"] = 1.0,

        // A script hangs off the plain advance of what it is on, which is where Word hangs one at
        // twelve point to the last decimal place — and 1.09 points further along when the letter
        // under it is twenty. The face states a kern for that: MathKernInfo gives every glyph a
        // staircase of them by height, so how far a script is pushed off depends on how high it
        // sits over the letter. It is not read, and this is the one fixture whose scripts sit high
        // enough over their letters for the difference to show.
        ["math-structure-probe"] = 1.2
    };

    private static readonly Dictionary<string, (double Tolerance, string Reason)> KnownVerticalDivergences =
        new()
        {
            ["chart-title-legend-label"] = (2.1,
                """
                A share written on a slice of a pie is placed by a fitting of Word's own: its four
                slices come out at 0.684, 0.687, 0.690 and 0.711 of the radius, and at up to a
                degree and a half off the middle of their own slice. Everything here puts one at
                seven tenths of the radius along the middle of its slice, which is two points out
                on the narrowest of the four and within a fifth of a point on the rest.
                """),

            ["superscript-shift-probe"] = (2.5,
                """
                What a raised or lowered run is a share of, which Word will not say. This fixture
                puts the question five sizes wide and three faces deep, and the shift comes back
                depending on both — so a single share of the type size, which is what this uses,
                cannot follow it everywhere.

                Where it does not is at the sizes nothing is ever superscripted at: the two and a
                half points are Calibri at ninety-six point, where Word raises 0.3325 of the size
                and Times New Roman gets 0.355. Below forty-eight point every face here is within
                three steps of the grid, and Times, which superscript-probe is written in, is
                within one at every size measured.

                ResolvedRunFormat.BaselineShiftPoints has the whole measurement, including the
                eleven faces that showed the shift is not a share of anything a face declares.
                """),

            ["equations"] = (1.2,
                """
                How tall a line holding an equation is, which math-line-box-probe measures and
                which is implemented from that measurement: the ink of what is in the equation
                with the face's own math leading over it, and never less than a line of the face
                at the size the equation is set at. Twenty-five equations there come out within
                three quarters of a point of Word's, and most within a quarter.

                What is left here is those quarter points accumulating down seventeen lines, each
                of them a line whose height Word rounds to the three hundredth of an inch and this
                does not.
                """),

            ["math-nary-probe"] = (2.8,
                """
                The rails between the operators, as in the other probes: a two point line of
                Word's is 2.16 points where the same line here is 2.2998, and there are forty of
                them. Where every limit goes is asserted in MathNaryTests and what each line comes
                to in MathLineBoxTests, both against Word's own page.
                """),

            ["math-bracket-probe"] = (1.7,
                """
                The rails between the brackets, as in math-kern-probe: a two point line of Word's
                is 2.16 points where the same line here is 2.2998, and there are fifty of them
                down the two pages. What the brackets themselves come to is asserted probe by
                probe in MathBracketTests, and the shape Word picks for each of the seventeen is
                the shape picked here.
                """),

            ["math-kern-probe"] = (2.2,
                """
                Not the equations: every one of the fifteen scripts on this page sits within four
                hundredths of a point of where Word puts it, which is what MathKernTests asserts.
                It is the rails between them — a two point line each, thirty of them — that drift.
                Word rounds how tall a line is to the three hundredth of an inch it rounds every
                other position to, which takes a two point line from 2.2998 points to 2.16, and
                thirty of those come to two points down the page. Nothing here rounds a line's
                height; the fixtures that show it are the ones whose lines are small enough for a
                fourteenth of a point to matter.
                """),

            ["math-line-box-probe"] = (1.0,
                """
                The rails between the equations: a two point line of Word's is 2.16 points where
                the same line here is 2.2998, since nothing here rounds a line's height to the
                three hundredth of an inch Word rounds it to. Fifty of them down two pages come to
                just under a point of drift. What each equation asks of its own line is asserted
                probe by probe in MathLineBoxTests.
                """),
        };

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

        // Word writes Arabic as the presentation forms, one glyph to a run, and the name of God as
        // the single glyph the font holds for it — which comes back out of Word's own file as the
        // letter J. Where the text goes is compared like any other fixture's, and its lines begin
        // where Word begins them; what the runs say is two spellings of the same line.
        ["arabic"] = "Word encodes Arabic as the presentation forms, which is not what was typed",

        // And these for the plainest reason of all: a shaped Indic syllable is drawn from glyphs
        // that stand for no character in particular — a conjunct of three consonants is one shape
        // — and Word's file maps them back to whatever code the glyph happens to sit at. A line of
        // Devanagari comes out of it as "नम#$". Where the text goes is compared like any other
        // fixture's, and agrees to the last decimal place.
        ["indic"] = "Word maps a shaped syllable back to nothing in particular",
        ["southeast-asian"] = "the same, for the scripts that stack and reorder",
        ["universal"] = "the same again, for the scripts shaped by no script's rules",
        ["apple"] = "and again, for the faces whose shaping is written in Apple's tables",

        // And this one for a second reason as well: which face is borrowed for a character the
        // run's own cannot draw is a choice rather than a fact, Word's is not discoverable, and
        // the two are not the same width. Where the text goes is compared like any other fixture's
        // and agrees exactly; how wide a borrowed face draws it is not something to hold Word to.
        ["font-fallback"] = "the face borrowed for what a font cannot draw is a choice, and not Word's",

        // An equation for the same reason once removed: Word draws the letters of one from the
        // block Unicode keeps for mathematics, and gives its subset of them no map back to
        // characters at all. What comes out of Word's own file is the codes of its subset. Ours
        // are the mathematical letters themselves, so an x copied out of our page is the 𝑥 that
        // was set — where the letters go is compared like any other fixture's.
        ["equations"] = "Word gives the letters of an equation no map back to what they say",
        ["math-line-box-probe"] = "the same: they are equations and nothing else",
        ["math-kern-probe"] = "the same again",
        ["math-bracket-probe"] = "and again",
        ["math-nary-probe"] = "and again",
        ["math-structure-probe"] = "the same again"
    };

    /// <summary>
    /// Fixtures holding text that Word's own file does not hold as text at all, and what it is.
    /// </summary>
    /// <remarks>
    /// A watermark is a word drawn along a path, and Word's export turns it into outlines: its
    /// PDF has the shape of the letters and no letters, so the word cannot be found in it, let
    /// alone compared line for line. This reader keeps it as text, which is the better of the two
    /// — the word stays searchable — and means our page has a line Word's has not.
    ///
    /// A watermark set across the page is turned, and turned text is left out of the comparison
    /// altogether, so only the ones set along it are counted here.
    ///
    /// What holds these to Word is <c>WatermarkTests</c>, which rasterises both and compares the
    /// ink: the two agree on better than 99% of every page. Everything else on these pages is
    /// compared here like any other fixture's.
    /// </remarks>
    private static readonly Dictionary<string, (int Lines, string Reason)> DrawnAsOutlines = new()
    {
        ["watermark"] = (0, "a watermark is outlines in Word's file and text in ours, and turned"),
        ["watermark-fit-probe"] = (7, "the same, seven boxes over")
    };

    /// <summary>
    /// Fixtures where Word's file holds lines of text that ours does not, and why.
    /// </summary>
    /// <remarks>
    /// One, and it is about what a reader copies rather than about what is drawn. A bracket too
    /// tall for any shape the face keeps is built out of three — a head, a middle and a foot — and
    /// Word writes each of the three as a character of its own, so its page holds three lines of
    /// text where ours holds one. All three shapes are drawn here, in the same places to within a
    /// quarter of a point; what carries the text is the first of them, so that a reader dragging
    /// across the equation copies one bracket rather than three pieces of one.
    /// </remarks>
    private static readonly Dictionary<string, (int Lines, string Reason)> WordWritesMorePieces = new()
    {
        ["math-bracket-probe"] = (2, "a built-up bracket is three characters in Word's file and one here")
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
        if (TestFonts.SkipForMissingFonts(name)) return;

        var referencePath = Path.Combine(TestPaths.ReferencePdfs, name + ".pdf");
        if (!File.Exists(referencePath)) return; // reported by Fixture_has_a_reference_pdf

        var report = Compare(name, referencePath);
        _output.WriteLine(report.ToText());


        if (DrawnAsOutlines.TryGetValue(name, out var outlined))
        {
            // Exactly the watermarks may be unmatched, and every one of them must be a line of
            // ours rather than one of Word's: a line of Word's with no counterpart here would be
            // something dropped, which is what this test is for.
            var ours = report.Deltas.Count(delta => delta.Theirs is null);

            Assert.True(report.UnmatchedCount == outlined.Lines && ours == outlined.Lines,
                $"'{name}': {report.UnmatchedCount} line(s) had no counterpart, {ours} of them " +
                $"ours; {outlined.Lines} of ours were expected — {outlined.Reason}.\n{report.ToText()}");
        }
        else if (WordWritesMorePieces.TryGetValue(name, out var pieces))
        {
            var theirs = report.Deltas.Count(delta => delta.Ours is null);

            Assert.True(report.UnmatchedCount == pieces.Lines && theirs == pieces.Lines,
                $"'{name}': {report.UnmatchedCount} line(s) had no counterpart, {theirs} of them " +
                $"Word's; {pieces.Lines} of Word's were expected — {pieces.Reason}.\n{report.ToText()}");
        }
        else
        {
            Assert.True(report.UnmatchedCount == 0,
                $"'{name}': {report.UnmatchedCount} line(s) had no counterpart.\n{report.ToText()}");
        }

        if (!TextNotComparable.TryGetValue(name, out var untellable))
        {
            Assert.True(report.TextMismatchCount == 0,
                $"'{name}': {report.TextMismatchCount} line(s) differ in text.\n{report.ToText()}");
        }
        else _output.WriteLine($"text not compared: {untellable}");

        var startTolerance = KnownHorizontalDivergences.TryGetValue(name, out var sideways)
            ? sideways
            : StartXTolerance;

        Assert.True(report.MaxAbsStartXDelta <= startTolerance,
            $"'{name}': a line starts {report.MaxAbsStartXDelta:0.###}pt from where Word puts it " +
            $"(tolerance {startTolerance}pt).\n{report.ToText()}");

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
        if (name.Length == 0 || TestFonts.SkipForMissingFonts(name)) return;

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

        // A known divergence buys one number a wider allowance and nothing else: everything a
        // document gets right stays held to the ordinary tolerance, so a regression beside a known
        // problem still fails.
        var (baselineTolerance, reason) = KnownRealDivergences.TryGetValue(name, out var known)
            ? known
            : (BaselineTolerance, string.Empty);

        if (reason.Length > 0) _output.WriteLine($"KNOWN DIVERGENCE: {reason}");

        Assert.True(report.UnmatchedCount == 0,
            $"'{name}': {report.UnmatchedCount} line(s) had no counterpart.\n{report.ToText()}");

        Assert.True(report.MaxAbsStartXDelta <= StartXTolerance,
            $"'{name}': a line starts {report.MaxAbsStartXDelta:0.###}pt from Word's.\n{report.ToText()}");

        Assert.True(report.MaxAbsBaselineDelta <= baselineTolerance,
            $"'{name}': a baseline sits {report.MaxAbsBaselineDelta:0.###}pt from Word's" +
            $"{(reason.Length > 0 ? $" (allowed {baselineTolerance}pt: {reason})" : "")}.\n{report.ToText()}");
    }

    /// <summary>
    /// Real documents whose geometry is known to diverge, with why.
    /// </summary>
    /// <remarks>
    /// One entry. An earlier one — the report's table sitting 1.02pt right of Word's, enough to
    /// wrap a cell Word fits on one line — was resolved by table-inset-probe: a declared
    /// w:tblInd is measured to the cell content edge rather than the table edge, and our autofit
    /// was sizing columns without allowing for the borders that layout later subtracted.
    /// </remarks>
    private static readonly Dictionary<string, (double Tolerance, string Reason)> KnownRealDivergences =
        new()
        {
        };

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
