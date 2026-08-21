using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Phonetic guides set over East Asian text, from <c>w:ruby</c>.
/// </summary>
/// <remarks>
/// <c>ruby-probe</c> puts every alignment the markup has to Word, over the same word — 振仮名 read
/// ふりがな, four letters of guide over three of word — and each line comes out where Word puts it:
///
///   centre            the guide in the middle of the word, which is what Word writes by default
///   left, right       against one end of it
///   distributeLetter  spread so the guide's ends meet the word's, the space between the letters
///   distributeSpace   spread the same, with half a gap outside each end as well
///   a wide guide      eight letters over one: the pair takes the guide's room and the word is
///                     centred under it
///   a narrow guide    two letters over three: the pair takes the word's room
///
/// The guide is set at the size <c>w:hps</c> names and raised off the word's baseline by
/// <c>w:hpsRaise</c>, and the line grows to hold it: the probe's lines stand 20.4 points apart
/// where a line of the same twelve point Mincho alone would take 15.6.
/// </remarks>
public class RubyTests(ITestOutputHelper output)
{
    /// <summary>Every guide and every word of the probe, against Word's own.</summary>
    [Fact]
    public void The_guides_stand_where_words_stand()
    {
        if (TestFonts.SkipForMissingFonts("ruby-probe")) return;

        var word = Japanese(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "ruby-probe.pdf")));
        var ours = Japanese(Ours());

        foreach (var piece in ours) output.WriteLine($"   {piece}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.Equal(word[i].Text, ours[i].Text);
            Assert.Equal(word[i].Left, ours[i].Left, 0.1);
            Assert.Equal(word[i].Width, ours[i].Width, 0.1);
            Assert.Equal(word[i].Size, ours[i].Size, 0.1);

            // Within a step of the grid: where the lines fall is the line box's business, and the
            // guide follows the word it stands over.
            Assert.Equal(word[i].Baseline, ours[i].Baseline, 0.3);
        }
    }

    /// <summary>
    /// Where the guide sits over its word, alignment by alignment. The word is 36 points of
    /// Mincho and the guide 24, so what changes is where the 12 points of slack go.
    /// </summary>
    [Theory]
    [InlineData(0, 6.0, 24.0, "centred: six points either side")]
    [InlineData(1, 0.0, 24.0, "left: flush with the word")]
    [InlineData(2, 12.0, 24.0, "right: flush with its end")]
    [InlineData(3, 0.0, 36.0, "spread between the letters: the ends meet the word's")]
    [InlineData(4, 1.5, 33.0, "spread outside them too: half a gap at each end")]
    public void The_guide_is_set_over_its_word_as_the_markup_asks(
        int line, double inset, double width, string what)
    {
        if (TestFonts.SkipForMissingFonts("ruby-probe")) return;

        output.WriteLine(what);

        var pieces = Japanese(Ours());
        var guide = pieces[line * 2];
        var over = pieces[line * 2 + 1];

        output.WriteLine($"guide {guide.Left:0.##}..{guide.Left + guide.Width:0.##}, " +
                         $"word {over.Left:0.##}..{over.Left + over.Width:0.##}");

        Assert.Equal(over.Left + inset, guide.Left, 0.1);
        Assert.Equal(width, guide.Width, 0.1);
    }

    /// <summary>
    /// A guide too long for its word takes the room instead, and the word is centred under it.
    /// The probe's sixth line has eight letters of guide over one of word.
    /// </summary>
    [Fact]
    public void A_guide_wider_than_its_word_takes_the_room()
    {
        if (TestFonts.SkipForMissingFonts("ruby-probe")) return;

        var pieces = Japanese(Ours());
        var guide = pieces[10];
        var over = pieces[11];

        output.WriteLine($"guide {guide.Width:0.##} wide, word {over.Width:0.##}");

        Assert.Equal(48, guide.Width, 0.1);
        Assert.Equal(12, over.Width, 0.1);

        // The word in the middle of the guide.
        Assert.Equal(guide.Left + (guide.Width - over.Width) / 2, over.Left, 0.1);
    }

    /// <summary>
    /// The guide is raised off the word's baseline by what <c>w:hpsRaise</c> says — eleven points
    /// here, which lands on the grid at 11.04 — and the line grows to hold it.
    /// </summary>
    [Fact]
    public void The_guide_is_raised_and_the_line_grows_to_hold_it()
    {
        if (TestFonts.SkipForMissingFonts("ruby-probe")) return;

        var pieces = Japanese(Ours());

        for (var i = 0; i < pieces.Count; i += 2)
        {
            var raised = pieces[i + 1].Baseline - pieces[i].Baseline;
            Assert.Equal(11.04, raised, 0.3);
        }

        // And the lines are further apart than a line of the word alone would be: twelve point
        // Mincho takes 15.6 points, and these take a little over twenty.
        var baselines = pieces.Where((_, i) => i % 2 == 1).Select(piece => piece.Baseline).ToList();
        var steps = baselines.Zip(baselines.Skip(1), (a, b) => b - a).Take(5).ToList();

        output.WriteLine($"the lines step by {string.Join(", ", steps.Select(step => $"{step:0.##}"))}");
        Assert.All(steps, step => Assert.InRange(step, 20.0, 21.0));
    }

    private static byte[] Ours() =>
        Converter.Convert(Fixtures.Build("ruby-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>
    /// The Japanese of the document, guide and word alternating down the page. A guide spread
    /// between its letters is written a letter at a time, so the pieces of one are gathered back
    /// together by the baseline they share.
    /// </summary>
    private static List<(double Left, double Width, double Baseline, double Size, string Text)> Japanese(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .Where(run => run.Text.Any(c => c > 0x3000))
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(line => line.Key)
            .Select(line =>
            {
                var runs = line.OrderBy(run => run.X).ToList();

                return (runs[0].X,
                    runs[^1].X + runs[^1].Width - runs[0].X,
                    line.Key,
                    runs[0].FontSize,
                    string.Concat(runs.Select(run => run.Text)));
            })
            .ToList();
}
