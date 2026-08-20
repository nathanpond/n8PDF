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

            ["equations"] = (2.0,
                """
                How tall a line holding an equation is, which math-line-box-probe measures and
                which is implemented from that measurement: the ink of what is in the equation
                with the face's own math leading over it, and never less than a line of the face
                at the size the equation is set at. Twenty-six equations there come out within
                nine tenths of a point of Word's, and most within a quarter.

                What is left here is that ninth tenth accumulating: the sum of the equations
                fixture asks its line for nine tenths of a point more than Word's does, because
                its limits sit where the integral's rule puts them rather than where Word puts a
                sum's — see the note on Nary. Down seventeen lines that comes to under two points.
                """),

            ["math-line-box-probe"] = (2.4,
                """
                One of the twenty-five: a bracket round a fraction whose parts are twice the size
                the equation is set at. Word reaches two shapes further up the face's series of
                brackets than this does, so its bracket is 0.86 points wider and taller, and the
                line holding it is taller with it. How far a bracket has to reach before Word
                takes the next shape was measured from two brackets at twelve point — nine tenths
                of what it holds, which is TeX's own factor — and this says that the rule does not
                carry to a bracket round something twice its own size.

                Every other line of the fixture is within 0.6 of a point, and what each equation
                asks of its line is asserted probe by probe in MathLineBoxTests.
                """),

            ["vml-stroke-probe"] = (5.5,
                """
                An old-style shape with an outline thicker than a point makes its line taller in
                Word than the shape's own height, and the line under it sits lower by:

                    1½pt outline  0.96pt      3pt  1.92pt      6pt  5.04pt
                      2pt outline  0.96pt    4½pt  4.08pt

                which follows neither the weight nor the offset the same shape is drawn at — 2pt
                and 3pt are drawn at the same offset and grow the line by different amounts. The
                offset itself is implemented, and is exact at every weight; this is the part that
                was measured and not explained, and the probe is here so it can be read again.
                Nothing in an ordinary document reaches it: Word's own text boxes are outlined at
                three quarters of a point, and everything at a point or less grows nothing.
                """)
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
            ["smartart"] = (3.5,
                """
                Every line of the diagram is where Word puts it across the page and every line
                sits the right distance from the one above it — the two agree on the line spacing
                to 0.3pt and on the space between paragraphs to 0.5pt — but each box's text as a
                whole sits 3.1pt above Word's.

                The block is centred in the box, so a constant offset means the two disagree by
                6.2pt about how tall the block is, or by 3.1pt about where the first baseline sits
                inside it. Those two cannot be told apart here, and the fixture cannot be made to
                tell them apart: Word writes the diagram's cache itself, so its type size, its
                line spacing and its anchoring are Word's to choose and not the document's. Both
                readings fit every line of it.

                What is measured is that the block is centred whether or not it fits — the tallest
                box's three lines overrun their box at both ends in Word's own drawing, and did
                not here until they were let to.
                """)
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
