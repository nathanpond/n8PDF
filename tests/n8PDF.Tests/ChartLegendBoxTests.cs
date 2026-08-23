using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests how a legend given a size as well as a corner arranges more than one entry inside it.
/// </summary>
/// <remarks>
/// The generic position comparison already holds this fixture to half a point, so what these add
/// is the *structure*: which arrangement Word chose, not merely that the words landed. The two
/// arrangements are a row and a column, and the interesting property is that a rule fitting one
/// of them says nothing about the other — which is how this stayed unsettled through two earlier
/// attempts. Asserting the count of rows separately from the positions is what makes a change
/// that quietly stacks everything, or quietly rows everything, fail here rather than drift.
///
/// The probe sweeps one dimension at a time so the two are not confounded: four pages take a box
/// 180 wide from 21.6 to 172.8 tall, and four more take a box 129.6 tall from 54 to 306 wide. The
/// three entries come to 144.27 across, so the width sweep straddles the threshold and the height
/// sweep does not — and the height sweep therefore shows that height has no part in the choice,
/// which is the claim the first four pages are really there for.
/// </remarks>
public class ChartLegendBoxTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string FixtureName = "chart-legend-box-probe";

    private static readonly string[] Names = ["Aa", "Middling", "Longer", "name", "here"];

    /// <summary>The legend's own baselines on a page, in order, with the axis numbers left out.</summary>
    /// <remarks>
    /// By the words rather than by position: the legend sits over the plot on every page of this
    /// probe, so there is no region of the page that holds it and nothing else.
    /// </remarks>
    private static List<double> LegendBaselines(byte[] pdf, int page) =>
        PdfLineComparison.GroupIntoLines(PdfTextExtractor.Extract(pdf), tolerance: 1)
            .Where(line => line.PageIndex == page &&
                           Names.Any(name => line.Text.Contains(name, StringComparison.Ordinal)))
            .Select(line => line.BaselineY)
            .Distinct()
            .Order()
            .ToList();

    [Theory]
    // The height sweep, at a width the row fits across. Height does not enter into the choice.
    [InlineData(0, 1, "180 x 21.6")]
    [InlineData(1, 1, "180 x 54")]
    [InlineData(2, 1, "180 x 97.2")]
    [InlineData(3, 1, "180 x 172.8")]
    // The width sweep. 54 and 108 are under the row's 144.27 and stack; 198 and 306 are over it
    // and do not. The third entry wraps to three lines at 54 and to one at 108, which is why the
    // narrowest page has five baselines to the next one's three.
    [InlineData(4, 5, "54 x 129.6")]
    [InlineData(5, 3, "108 x 129.6")]
    [InlineData(6, 1, "198 x 129.6")]
    [InlineData(7, 1, "306 x 129.6")]
    public void A_boxed_legend_makes_one_row_of_its_entries_only_where_the_row_fits(
        int page, int rows, string box)
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(FixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var theirs = File.ReadAllBytes(reference);

        var mine = LegendBaselines(ours, page);
        var word = LegendBaselines(theirs, page);

        _output.WriteLine($"{box}: {mine.Count} row(s) here, {word.Count} in Word's — " +
                          $"[{string.Join(", ", mine.Select(y => y.ToString("0.00")))}] against " +
                          $"[{string.Join(", ", word.Select(y => y.ToString("0.00")))}]");

        // Word arranged them the way the table says, or the reference is not of the document it
        // claims to be and the rest of this proves nothing.
        Assert.Equal(rows, word.Count);

        // And so did we, on the same baselines. The count is what says the arrangement was
        // chosen right; the positions are what say it was then laid out right.
        Assert.Equal(rows, mine.Count);

        for (var i = 0; i < rows; i++)
            Assert.InRange(mine[i] - word[i], -0.5, 0.5);
    }

    /// <summary>
    /// The gap along a row grows with the box, and the left edge down a column does not move with
    /// the entry.
    /// </summary>
    /// <remarks>
    /// Two claims that the baseline test above cannot see, because both are horizontal.
    ///
    /// The first is that a row's spacing is *shared out* rather than fixed: the same three entries
    /// sit 14.20 apart in a box 198 wide and 41.20 apart in one 306 wide. A reader using a
    /// constant gap passes the narrow page and misses the wide one by twenty-seven points.
    ///
    /// The second is that a column has one left edge for all of its entries, set by the widest of
    /// them rather than by each — so "Aa" begins where "Middling" does, tens of points from where
    /// centring it on its own would put it.
    /// </remarks>
    [Fact]
    public void A_row_shares_out_its_spacing_and_a_column_shares_one_left_edge()
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(FixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var theirs = File.ReadAllBytes(reference);

        // Where each name begins on a page, in order across or down.
        List<double> Starts(byte[] pdf, int page) =>
            PdfLineComparison.GroupIntoLines(PdfTextExtractor.Extract(pdf), tolerance: 1)
                .Where(line => line.PageIndex == page &&
                               Names.Any(name => line.Text.Contains(name, StringComparison.Ordinal)))
                .OrderBy(line => line.BaselineY).ThenBy(line => line.StartX)
                .Select(line => line.StartX)
                .ToList();

        // Where each entry begins along a row. GroupIntoLines merges the row into one line, so
        // the entries are recovered from the runs: within an entry the runs are adjacent or a
        // space apart, and between entries there is a gap and the next key's head, which is never
        // less than ten points. The band is the legend's own baseline: a value-axis number sits three
        // and a half points above it and would otherwise read as a fourth entry. The same splitting
        // is applied to both, so it cannot favour either.
        List<double> Entries(byte[] pdf, int page)
        {
            var runs = PdfTextExtractor.Extract(pdf)
                .Where(run => run.PageIndex == page && run.BaselineY is > 148 and < 153)
                .OrderBy(run => run.X)
                .ToList();

            var starts = new List<double>();
            var end = double.NegativeInfinity;

            foreach (var run in runs)
            {
                if (run.X - end > 10) starts.Add(run.X);
                end = Math.Max(end, run.X + run.Width);
            }

            return starts;
        }

        // A row of three, in a box 198 wide and then in one 306 wide.
        var spacing = new List<double>();

        foreach (var page in new[] { 6, 7 })
        {
            var mine = Entries(ours, page);
            var word = Entries(theirs, page);

            _output.WriteLine($"page {page}: entries at [{string.Join(", ", mine.Select(x => x.ToString("0.00")))}] " +
                              $"against [{string.Join(", ", word.Select(x => x.ToString("0.00")))}]");

            Assert.Equal(3, word.Count);
            Assert.Equal(3, mine.Count);

            for (var i = 0; i < 3; i++)
                Assert.InRange(mine[i] - word[i], -0.5, 0.5);

            // How far apart Word set them, less the first entry's own words. Only the difference
            // between the two pages is used, so the entry's width falls out of it.
            spacing.Add(word[1] - word[0]);
        }

        // Shared out rather than fixed: the same three entries are twenty-seven points further
        // apart in the wider box. A reader with a constant gap cannot pass both pages above, and
        // this is what says so out loud.
        _output.WriteLine($"Word's entry pitch: {spacing[0]:0.00} at 198 wide, {spacing[1]:0.00} at 306");
        Assert.InRange(spacing[1] - spacing[0], 25, 29);

        // A column of three, in boxes 54 and 108 wide. Every entry begins at the same x, and it is
        // Word's x — the second claim needs both, since a reader that centred each entry on its
        // own would also be self-consistent.
        foreach (var page in new[] { 4, 5 })
        {
            var mine = Starts(ours, page);
            var word = Starts(theirs, page);

            _output.WriteLine($"page {page}: starts [{string.Join(", ", mine.Select(x => x.ToString("0.00")))}] " +
                              $"against [{string.Join(", ", word.Select(x => x.ToString("0.00")))}]");

            Assert.Equal(word.Count, mine.Count);
            Assert.All(mine, x => Assert.Equal(mine[0], x, 1));

            for (var i = 0; i < word.Count; i++)
                Assert.InRange(mine[i] - word[i], -0.5, 0.5);
        }
    }

    /// <summary>
    /// A box too short for its entries drops them from the end rather than overflowing.
    /// </summary>
    /// <remarks>
    /// #90, and the four pages the probe carries for it. The box is 54 wide throughout, which
    /// gives the third entry three lines, and only the height moves: 108, 86.4, 64.8 and 36.72,
    /// for shares of 36, 28.8, 21.6 and 12.24 if all three were drawn. Word draws three, two, two
    /// and one — so it is not simply what fits, since the box that keeps only two at 86.4 is taller
    /// than the one that keeps three at... it is not, and that is the point: the count is asked of
    /// the share, and dropping an entry makes the shares larger, so the question is asked again.
    ///
    /// The second page is the one that pins it. Three entries there would get 28.8 each and Word
    /// draws two, which get 43.2 — and the third page's two get 32.4, which Word keeps. So the
    /// need is above 28.8 and at most 32.4, and the entry is three lines: see
    /// <c>ChartComposer.BoxedLegendCrowding</c> for where in that interval it sits and why.
    ///
    /// What is asserted is the count in Word's own output beside ours, because a rule read off one
    /// page and applied to the others is exactly the failure this issue was stopped for twice.
    /// </remarks>
    [Theory]
    [InlineData(8, 3, "54 x 108")]
    [InlineData(9, 2, "54 x 86.4")]
    [InlineData(10, 2, "54 x 64.8")]
    [InlineData(11, 1, "54 x 36.72")]
    public void A_box_too_short_for_its_entries_drops_them_from_the_end(int page, int drawn, string box)
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(FixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var theirs = File.ReadAllBytes(reference);

        // Each entry begins with a word none of the others uses, so counting those counts entries
        // rather than lines — the third takes three lines when it is drawn at all.
        string[] first = ["Aa", "Middling", "Longer"];

        int Drawn(byte[] pdf) =>
            first.Count(name => PdfLineComparison
                .GroupIntoLines(PdfTextExtractor.Extract(pdf), tolerance: 1)
                .Any(line => line.PageIndex == page &&
                             line.Text.Contains(name, StringComparison.Ordinal)));

        var mine = Drawn(ours);
        var word = Drawn(theirs);

        _output.WriteLine($"{box}: {mine} entr(ies) here, {word} in Word's, expecting {drawn}");

        Assert.Equal(drawn, word);
        Assert.Equal(drawn, mine);
    }
}
