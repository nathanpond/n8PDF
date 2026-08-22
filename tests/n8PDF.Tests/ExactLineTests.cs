using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where an exact line puts its baseline. <c>w:lineRule="exact"</c> fixes the height of the line
/// and says nothing about how the room is divided above and below the baseline.
/// </summary>
/// <remarks>
/// The share is four fifths, and it is Word's own rather than the font's: the probe's last three
/// pages are the same height in Times, Arial and Calibri, whose own descents are 0.1953, 0.1897 and
/// 0.2200 of their line — five steps of the grid apart at this size — and Word sets all three on
/// one baseline. A sweep of fifty-three heights run twice over, in fifty-six point Times and in
/// twenty-four point Verdana, put every baseline of the second in exactly the place it put the
/// first.
///
/// Four fifths alone lands one step of the grid out on about a fifth of the heights, and the last
/// step was measured by sweeping every height a twip at a time rather than a point at a time: 865
/// heights from fifteen points to a hundred and fifty, in four exports. Two rules come out of it,
/// and <see cref="LayoutEngine"/> writes both up where they are applied:
///
///   * the height behaves as though it were a twip larger or smaller before the four fifths is
///     taken, by how many whole steps of the ascent leave over four — larger at one, smaller at
///     two and three, the height itself at nought. That is 779 of the 865;
///   * where the height and its fifth both land half way between two steps — every odd multiple of
///     three points — Word takes a further step, at all but one such height in five and then one
///     of those in five again. A base-five pattern, measured and not derived, which is why it was
///     checked against a second sweep of sixty-three heights the first never reached: it predicted
///     every one of them.
///
/// Together they account for all 865. What the probe holds is the nineteen heights that pin them,
/// so the rule cannot drift without a test saying so.
/// </remarks>
public class ExactLineTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every line of the probe, against Word's own: the first line of each page exactly, and the
    /// lines under it within a step of the grid.
    /// </summary>
    /// <remarks>
    /// The rule above says where the baseline of an exact line falls below the top of its own box,
    /// and the top of the first box on a page is the margin. Where the next line's box begins is a
    /// second question, settled by exact-line-advance-probe: Word advances by the height itself
    /// and rounds each baseline where it lands, which this does too. How that rounding goes is a
    /// third: it is not to the nearest step but from five twelfths of one above, which is measured
    /// in Grid.ExactBaseline. What is left after that is a last step no rule of the height
    /// reproduces — a line under the first is a step from Word's about one time in ten, and never
    /// further.
    /// </remarks>
    [Fact]
    public void The_first_baseline_of_every_page_is_words()
    {
        if (TestFonts.SkipForMissingFonts("exact-line-probe")) return;

        var word = Baselines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "exact-line-probe.pdf")));
        var ours = Baselines(Converter.Convert(Fixtures.Build("exact-line-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        Assert.Equal(word.Count, ours.Count);

        var apart = word.Zip(ours, (w, o) => Math.Abs(w - o)).ToList();

        output.WriteLine($"word {string.Join(" ", word)}");
        output.WriteLine($"ours {string.Join(" ", ours)}");
        output.WriteLine($"{apart.Count(a => a > 0.001)} of {apart.Count} out at all, worst {apart.Max():0.###}pt");

        // Three lines to a page: the exact-spaced pair and the ordinary line beneath them.
        for (var i = 0; i < apart.Count; i += 3)
        {
            Assert.True(apart[i] < 0.001,
                $"the first baseline of page {i / 3 + 1} is {apart[i]:0.###}pt from Word's");
        }

        Assert.True(apart.Max() <= 0.24 + 0.001, $"a baseline is {apart.Max():0.###}pt from Word's");
    }

    /// <summary>
    /// Every height the probe holds, at the baseline Word's own export puts it on. The last ten are
    /// the ones the whole-point sweep could not reach: three where four fifths lands exactly half
    /// way between two steps and the number of whole steps under it decides which way it goes, two
    /// where it lands a third of a step over and Word takes the step anyway, and five that say what
    /// happens where the height and its fifth both land half way.
    /// </summary>
    [Theory]
    [InlineData(600, 96.0, "thirty points")]
    [InlineData(800, 104.16, "forty")]
    [InlineData(827, 105.12, "Word's own three-line cap height")]
    [InlineData(1100, 115.92, "fifty-five")]
    [InlineData(1200, 120.0, "sixty")]
    [InlineData(1400, 128.16, "seventy")]
    [InlineData(400, 88.08, "twenty")]
    [InlineData(1000, 112.08, "fifty")]
    [InlineData(405, 88.08, "half way, three whole steps over four: down")]
    [InlineData(411, 88.56, "half way, none over: up")]
    [InlineData(423, 88.8, "half way, two over: down")]
    [InlineData(416, 88.8, "a third of a step over, one whole step over four: up")]
    [InlineData(440, 89.76, "the same again")]
    [InlineData(420, 89.04, "twenty-one points: the height and its fifth both half way")]
    [InlineData(540, 93.84, "twenty-seven, the same")]
    [InlineData(300, 84.0, "fifteen: the same, and Word does not take the step")]
    [InlineData(900, 108.0, "forty-five, the same")]
    [InlineData(444, 89.76, "the ordinary half-way height")]
    public void Each_height_lands_where_word_puts_it(int twips, double word, string what)
    {
        if (TestFonts.SkipForMissingFonts("exact-line-probe")) return;

        output.WriteLine(what);

        var pdf = Converter.Convert(Fixtures.Build("exact-line-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        // The probe's pages are in the order the heights were written, and every page opens with
        // the exact-spaced line.
        var page = Array.IndexOf(Heights, twips);
        var first = PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page)
            .Min(run => run.BaselineY);

        Assert.Equal(word, first, 2);
    }

    /// <summary>
    /// Three faces, one height, one baseline: the share of an exact line is Word's and not the
    /// font's. Times, Arial and Calibri keep 0.1953, 0.1897 and 0.2200 of their own lines below
    /// the baseline, which at fifty points is a spread of five steps of the grid.
    /// </summary>
    [Fact]
    public void The_share_is_words_rather_than_the_faces()
    {
        if (TestFonts.SkipForMissingFonts("exact-line-probe")) return;

        var pdf = Converter.Convert(Fixtures.Build("exact-line-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });
        var runs = PdfTextExtractor.Extract(pdf);

        var times = First(runs, Array.IndexOf(Heights, 1000));
        var arial = First(runs, Heights.Length);
        var calibri = First(runs, Heights.Length + 1);

        output.WriteLine($"times {times} arial {arial} calibri {calibri}");

        Assert.Equal(times, arial, 2);
        Assert.Equal(times, calibri, 2);
    }

    private static double First(List<ExtractedTextRun> runs, int page) =>
        runs.Where(run => run.PageIndex == page).Min(run => run.BaselineY);

    /// <summary>The exact heights the probe is written in, in twips, in the order of its pages.</summary>
    private static readonly int[] Heights =
    [
        400, 500, 600, 800, 827, 1000, 1100, 1200, 1400,
        405, 411, 423, 416, 440, 420, 540, 300, 900, 444
    ];

    /// <summary>Every baseline of the document, page by page and top first.</summary>
    /// <summary>
    /// How an exact-spaced paragraph gets from one line to the next: by the height itself, not by
    /// a whole number of steps of the grid.
    /// </summary>
    /// <remarks>
    /// The two are told apart by the gaps between Word's own baselines. An advance of a whole
    /// number of steps would put every gap of a page at the same number; Word's take two — 83 and
    /// 84 steps at 20.05 points, where the height is 83⅓ — which is what rounding each baseline of
    /// an unrounded run of positions gives. Nothing accumulates either way: over twenty lines the
    /// last baseline of five of the probe's six pages is exactly Word's, and the sixth is one step
    /// from it. A rounded advance would have drifted by up to three points over the same twenty.
    /// </remarks>
    [Theory]
    [InlineData(0, 401, 367, 1954, 13)]
    [InlineData(1, 405, 367, 1970, 18)]
    [InlineData(2, 411, 369, 1996, 15)]
    [InlineData(3, 420, 371, 2033, 20)]
    [InlineData(4, 423, 370, 2044, 20)]
    [InlineData(5, 500, 383, 2362, 17)]
    public void A_paragraph_advances_by_the_height_itself(
        int page, int twips, int first, int last, int exact)
    {
        if (TestFonts.SkipForMissingFonts("exact-line-advance-probe")) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, "exact-line-advance-probe.pdf");

        var word = Steps(File.ReadAllBytes(reference), page);
        var ours = Steps(Converter.Convert(Fixtures.Build("exact-line-advance-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"{twips} twips: word {string.Join(" ", word)}");
        output.WriteLine($"{twips} twips: ours {string.Join(" ", ours)}");

        Assert.Equal(20, word.Count);
        Assert.Equal(word.Count, ours.Count);

        // Word's own gaps are not all the same, which is what says the advance is the height.
        var gaps = word.Zip(word.Skip(1), (a, b) => b - a).Distinct().OrderBy(gap => gap).ToList();

        output.WriteLine($"word's gaps: {string.Join(", ", gaps)}");
        Assert.Equal(2, gaps.Count);
        Assert.Equal(1, gaps[1] - gaps[0]);

        // The first baseline is Word's exactly, and nothing drifts away from it: every line is
        // within a step, the last one included.
        Assert.Equal(first, word[0]);
        Assert.Equal(last, word[^1]);
        Assert.Equal(word[0], ours[0]);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.True(Math.Abs(ours[i] - word[i]) <= 1,
                $"line {i + 1} of the {twips} twip page is {ours[i] - word[i]} steps from Word's");
        }

        Assert.Equal(exact, word.Zip(ours, (w, o) => w == o).Count(same => same));
    }

    /// <summary>
    /// How much of the probe lands exactly where Word puts it, held to the number so that a rule
    /// better than five twelfths says so by failing this.
    /// </summary>
    [Fact]
    public void The_rounding_of_the_lines_between_is_as_close_as_it_is()
    {
        if (TestFonts.SkipForMissingFonts("exact-line-advance-probe")) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, "exact-line-advance-probe.pdf");
        var pdf = Converter.Convert(Fixtures.Build("exact-line-advance-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var (same, all) = (0, 0);

        for (var page = 0; page < 6; page++)
        {
            var word = Steps(File.ReadAllBytes(reference), page);
            var ours = Steps(pdf, page);

            same += word.Zip(ours, (w, o) => w == o).Count(match => match);
            all += word.Count;
        }

        output.WriteLine($"{same} of {all} baselines are exactly Word's");

        // 103 of 120 at the time of writing; rounding to the nearest step gives 96.
        Assert.Equal(120, all);
        Assert.True(same >= 103,
            $"only {same} of {all} baselines land where Word puts them, where 103 did");
    }

    /// <summary>Every baseline of a page, in whole steps of the grid, top first.</summary>
    private static List<int> Steps(byte[] pdf, int page) =>
        [.. PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page)
            .Select(run => (int)Math.Round(run.BaselineY / 0.24))
            .Distinct()
            .OrderBy(step => step)];

    private static List<double> Baselines(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .GroupBy(run => (run.PageIndex, Math.Round(run.BaselineY, 2)))
            .Select(line => line.Key)
            .OrderBy(line => line.PageIndex).ThenBy(line => line.Item2)
            .Select(line => line.Item2)
            .ToList();
}
