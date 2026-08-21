using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Numbers set in the margin against the lines they belong to: which lines take one, what it
/// counts, and where it stands.
/// </summary>
/// <remarks>
/// <c>line-number-probe</c> is three sections, so that one export answers all of it:
///
///   every line          countBy="1", six lines numbered one to six
///   an empty line       counted like any other, which is why "Counting again 1." is eight
///   passed over         two lines with w:suppressLineNumbers, neither numbered nor counted
///   the next page       restart="newPage" begins again at one
///   every fifth         countBy="5" prints only the multiples, restart="continuous" carries on
///   start               ignored where the count is continuous, which is Word's own reading
///   distance            720 twips puts the number half an inch out; nothing at all puts it 18pt
///
/// Word's own PDF is the arbiter throughout, and the numbers are compared against the lines they
/// stand beside rather than in a list of their own: a number in the right place beside the wrong
/// line would pass the one and fail the other.
///
/// The one thing the export cannot settle is whether Word lays out the empty paragraph a section
/// break is written on, since it falls at the foot of a page where a line nobody can see and a
/// line that is not there look alike. What the numbers do prove is that it is not counted: the
/// count runs six, seven across the break rather than six, eight.
/// </remarks>
public class LineNumberTests(ITestOutputHelper output)
{
    /// <summary>Every number Word set, beside the line it set it against, page by page.</summary>
    [Theory]
    [InlineData(0, "one to eleven, with an empty line counted and two passed over")]
    [InlineData(1, "begun again at one on a new page")]
    [InlineData(2, "every fifth line only, carrying on from the section before")]
    [InlineData(3, "one again, half an inch out")]
    public void The_numbers_stand_where_words_stand(int page, string what)
    {
        if (TestFonts.SkipForMissingFonts("line-number-probe")) return;

        output.WriteLine(what);

        var word = Numbers(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "line-number-probe.pdf")), page);
        var ours = Numbers(Converter.Convert(Fixtures.Build("line-number-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"word {string.Join(" | ", word)}");
        output.WriteLine($"ours {string.Join(" | ", ours)}");

        Assert.Equal(word, ours);
    }

    /// <summary>
    /// A line the paragraph passed over takes no number, and does not take its turn either: the
    /// lines after the two suppressed ones carry on from the empty line before them.
    /// </summary>
    [Fact]
    public void A_line_passed_over_does_not_take_its_turn()
    {
        if (TestFonts.SkipForMissingFonts("line-number-probe")) return;

        var pdf = Converter.Convert(Fixtures.Build("line-number-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });
        var beside = Numbers(pdf, 0);

        Assert.DoesNotContain(beside, line => line.Contains("Passed over"));

        // Six lines of text, then the empty seventh, then two nobody counted.
        Assert.Contains("6@48.48 Everyline6.", beside);
        Assert.Contains("7@48.48 ", beside);
        Assert.Contains("8@48.48 Countingagain1.", beside);
    }

    /// <summary>
    /// The distance is measured from the text to the end of the number, so a section that names
    /// half an inch sets its numbers eighteen points further out than one that names nothing and
    /// takes Word's own eighteen.
    /// </summary>
    [Fact]
    public void The_distance_is_measured_to_the_end_of_the_number()
    {
        if (TestFonts.SkipForMissingFonts("line-number-probe")) return;

        var pdf = Converter.Convert(Fixtures.Build("line-number-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var said = Single(pdf, 3);
        var unsaid = Single(pdf, 0);

        output.WriteLine($"nothing said {unsaid}, half an inch out {said}");
        Assert.Equal(18, unsaid - said, 3);
    }

    /// <summary>Where the one-figure numbers on a page begin.</summary>
    private static double Single(byte[] pdf, int page) =>
        PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && IsNumber(run) && run.Text.Length == 1)
            .Select(run => run.X)
            .Distinct()
            .Single();

    /// <summary>
    /// The line numbers on a page, top first, each with the line it stands against: "8@48.48
    /// Counting again 1.".
    /// </summary>
    private static List<string> Numbers(byte[] pdf, int page)
    {
        var runs = PdfTextExtractor.Extract(pdf).Where(run => run.PageIndex == page).ToList();

        return runs
            .Where(IsNumber)
            .OrderByDescending(number => number.BaselineY)
            .Select(number =>
            {
                var line = runs
                    .Where(run => !IsNumber(run) && Math.Abs(run.BaselineY - number.BaselineY) < 0.01)
                    .OrderBy(run => run.X)
                    .Select(run => run.Text);

                // The spaces are dropped rather than kept: Word breaks "Every line 6." into
                // pieces where this sets it in one, and where the spaces fall between the pieces
                // is a question for the comparison of the text, not for this.
                return $"{number.Text}@{number.X:0.##} {Bare(string.Concat(line))}";
            })
            .ToList();
    }

    /// <summary>A string with its spaces taken out.</summary>
    private static string Bare(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>
    /// A run is a line number if it is nothing but figures and stands left of the text. The
    /// margin is an inch, and no line of this probe is indented out into it.
    /// </summary>
    private static bool IsNumber(ExtractedTextRun run) =>
        run.X < 72 && run.Text.Length > 0 && run.Text.All(char.IsAsciiDigit);
}
