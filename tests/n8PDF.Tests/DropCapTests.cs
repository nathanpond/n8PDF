using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A dropped capital: where the letter stands, how much room the lines beside it give up, and
/// which lines those are.
/// </summary>
/// <remarks>
/// <c>drop-cap-probe</c> is eight pages written the way Word writes a dropped capital — Word's own
/// AppleScript was asked for one and this is the markup it produced, exact line spacing, keepNext,
/// baseline alignment and all. What the pages settle:
///
///   1  Word's own three-line cap: 56 point, dropped 5.5, on an exact line of 41.35
///   2  Word's own two-line cap, with a ninth of an inch of daylight beside it
///   3  the same in the margin, which Word anchors to the page rather than the text
///   4  a paragraph shorter than the frame, then another: the wrap reaches across both
///   5  a frame of three lines round a letter of ordinary size: the letter governs, not the count
///   6  a cap written by hand with no exact spacing, so the frame is the letter's own line
///   7  the same, not dropped at all
///   8  the same on a shorter line
///
/// Every one of them is compared against Word's own export, cap and text alike.
/// </remarks>
public class DropCapTests(ITestOutputHelper output)
{
    /// <summary>The letter itself: where it stands, how big it is, and on what baseline.</summary>
    [Theory]
    [InlineData(0, 72.0, 110.64, "three lines, inside the text")]
    [InlineData(1, 72.0, 97.2, "two lines, with a distance stated")]
    [InlineData(2, 37.68, 110.64, "in the margin, its own width out from the text")]
    [InlineData(5, 72.0, 120.48, "no exact spacing, so the letter's own line is the frame")]
    public void The_letter_stands_where_words_stands(int page, double x, double baseline, string what)
    {
        if (TestFonts.SkipForMissingFonts("drop-cap-probe")) return;

        output.WriteLine(what);

        var word = Cap(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "drop-cap-probe.pdf")), page);
        var ours = Cap(Ours(), page);

        output.WriteLine($"word x={word.X:0.##} y={word.BaselineY:0.##} sz={word.FontSize:0.##}");
        output.WriteLine($"ours x={ours.X:0.##} y={ours.BaselineY:0.##} sz={ours.FontSize:0.##}");

        Assert.Equal(x, ours.X, 2);
        Assert.Equal(baseline, ours.BaselineY, 2);
        Assert.Equal(word.X, ours.X, 2);
        Assert.Equal(word.BaselineY, ours.BaselineY, 2);
    }

    /// <summary>
    /// How far in the lines beside it begin, and how many of them do. The frame is the letter's
    /// advance at the size the run states plus <c>w:hSpace</c>, rounded to the grid — Word's own
    /// fifty-six point T measures 34.2167 and its lines begin 34.32 in.
    /// </summary>
    [Theory]
    [InlineData(0, 34.32, 3, "the letter's own width")]
    [InlineData(1, 30.48, 2, "a shorter letter and nine points of daylight")]
    [InlineData(3, 34.32, 3, "reaching across a short paragraph into the next")]
    [InlineData(5, 31.68, 5, "a frame as tall as a fifty-two point line covers five")]
    public void The_lines_beside_it_give_up_the_frame_s_width(
        int page, double indent, int shortened, string what)
    {
        if (TestFonts.SkipForMissingFonts("drop-cap-probe")) return;

        output.WriteLine(what);

        var word = Flow(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "drop-cap-probe.pdf")), page);
        var ours = Flow(Ours(), page);

        output.WriteLine($"word {string.Join(" ", word)}");
        output.WriteLine($"ours {string.Join(" ", ours)}");

        Assert.Equal(word, ours);
        Assert.Equal(shortened, ours.Count(x => Math.Abs(x - (72 + indent)) < 0.01));

        // And the measure comes back where the frame ends — on the page where the frame reaches
        // past the last line of the text there is nothing left to come back.
        if (ours.Count > shortened) Assert.Equal(72, ours[shortened], 2);
    }

    /// <summary>
    /// A cap in the margin takes nothing from the text: every line of the paragraph beside it
    /// runs the full measure, and the letter hangs its own width out to the left.
    /// </summary>
    [Fact]
    public void A_cap_in_the_margin_shortens_nothing()
    {
        if (TestFonts.SkipForMissingFonts("drop-cap-probe")) return;

        Assert.All(Flow(Ours(), 2), left => Assert.Equal(72, left, 2));
    }

    /// <summary>
    /// The count of lines the frame states has no say in it: a frame of three lines round a
    /// letter of ordinary size takes one line's room, which is the letter's own.
    /// </summary>
    [Fact]
    public void The_stated_count_of_lines_governs_nothing()
    {
        if (TestFonts.SkipForMissingFonts("drop-cap-probe")) return;

        var ours = Flow(Ours(), 4);
        var word = Flow(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "drop-cap-probe.pdf")), 4);

        output.WriteLine($"word {string.Join(" ", word)}");
        output.WriteLine($"ours {string.Join(" ", ours)}");

        // The letter is twelve point, so the frame is one line tall and the text runs beside it
        // on that line alone. Word's second line is back at the margin, and so is this one.
        Assert.Equal(word, ours);
        Assert.Equal(72, ours[1], 2);
    }

    private static byte[] Ours() =>
        Converter.Convert(Fixtures.Build("drop-cap-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>The dropped letter on a page: the one piece of text set larger than the rest.</summary>
    private static ExtractedTextRun Cap(byte[] pdf, int page) =>
        PdfTextExtractor.Extract(pdf).Single(run => run.PageIndex == page && run.FontSize > 20);

    /// <summary>Where each line of the flowing text begins, top first.</summary>
    private static List<double> Flow(byte[] pdf, int page) =>
        PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && run.FontSize is > 11.5f and < 20)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(line => line.Key)
            .Select(line => Math.Round(line.Min(run => run.X), 2))
            .ToList();
}
