using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Where an exact line puts its baseline. <c>w:lineRule="exact"</c> fixes the height of the line
/// and says nothing about how the room is divided above and below the baseline.
/// </summary>
/// <remarks>
/// <c>exact-line-probe</c> measures eleven heights, and a sweep of fifty-three from twenty points
/// to seventy-two was run twice over while this was written — fifty-six point Times and twenty-four
/// point Verdana — and Word put every baseline of the second sweep in exactly the place it put the
/// first. So the share is Word's own and not the font's, which is what this holds: the probe's
/// last three pages are the same height in Times, Arial and Calibri, whose own descents are 0.1953,
/// 0.1897 and 0.2200 of their line — five steps of the grid apart at this size — and Word sets all
/// three on one baseline.
///
/// The share is four fifths. That lands on Word's answer at thirty-six of the fifty-three heights
/// swept and one step of the grid from it at the other seventeen, never further, and what Word
/// does with that last step is not a rounding of anything measured here: the residual repeats every
/// six points, and no rule of the form round(aH + b) — in points, in twips, or in the grid's own
/// units — reproduces it. Both sets are held below, so the day the rule is found this test will say
/// what changed.
/// </remarks>
public class ExactLineTests(ITestOutputHelper output)
{
    /// <summary>Every line of the probe, against Word's own.</summary>
    [Fact]
    public void The_baselines_are_words_or_one_step_from_them()
    {
        if (TestFonts.SkipForMissingFonts("exact-line-probe")) return;

        var word = Baselines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "exact-line-probe.pdf")));
        var ours = Baselines(Converter.Convert(Fixtures.Build("exact-line-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        Assert.Equal(word.Count, ours.Count);

        var apart = word.Zip(ours, (w, o) => Math.Abs(w - o)).ToList();

        output.WriteLine($"word {string.Join(" ", word)}");
        output.WriteLine($"ours {string.Join(" ", ours)}");
        output.WriteLine($"{apart.Count(a => a > 0.001)} of {apart.Count} a step out, worst {apart.Max():0.###}pt");

        Assert.True(apart.Max() <= 0.24 + 0.001, $"a baseline is {apart.Max():0.###}pt from Word's");
    }

    /// <summary>
    /// The heights Word and this agree on exactly, and the two the sweep says are a step out.
    /// Twenty and fifty are the probe's own; the whole list of seventeen from the sweep is 20, 21,
    /// 26, 27, 32, 33, 38, 39, 44, 50, 51, 56, 57, 62, 63, 68 and 69 points.
    /// </summary>
    [Theory]
    [InlineData(600, 96.0, 0.0, "thirty points, exactly Word's")]
    [InlineData(800, 104.16, 0.0, "forty, exactly Word's")]
    [InlineData(827, 105.12, 0.0, "Word's own three-line cap height")]
    [InlineData(1100, 115.92, 0.0, "fifty-five, exactly Word's")]
    [InlineData(1200, 120.0, 0.0, "sixty, exactly Word's")]
    [InlineData(1400, 128.16, 0.0, "seventy, exactly Word's")]
    [InlineData(400, 88.08, -0.24, "twenty: a step above Word's")]
    [InlineData(1000, 112.08, -0.24, "fifty: a step above Word's")]
    public void Each_height_lands_where_the_sweep_said(int twips, double word, double apart, string what)
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

        Assert.Equal(word + apart, first, 2);
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
    private static readonly int[] Heights = [400, 500, 600, 800, 827, 1000, 1100, 1200, 1400];

    /// <summary>Every baseline of the document, page by page and top first.</summary>
    private static List<double> Baselines(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .GroupBy(run => (run.PageIndex, Math.Round(run.BaselineY, 2)))
            .Select(line => line.Key)
            .OrderBy(line => line.PageIndex).ThenBy(line => line.Item2)
            .Select(line => line.Item2)
            .ToList();
}
