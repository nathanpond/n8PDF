using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a <c>w:highlight</c> paints.
/// </summary>
/// <remarks>
/// A highlight is a filled rectangle behind a run and nothing else, so everything about it is a
/// question about ink. highlight-probe answers four of them against Word's own export:
///
///   the colours    the sixteen names are the sixteen colours of an old display adapter, each
///                  channel off, half on at 128, or full — read off the page, not off a table
///   the box        as tall as the line and as wide as the run, both edges put on the grid
///   the line       the line's height, not the run's: a twelve point run beside a thirty-six
///                  point one is highlighted the full forty-one points they share
///   the ends       a space inside the line is covered, one dropped at a break is not, and a
///                  plain space between two highlighted words leaves two boxes rather than one
///
/// A highlighted paragraph mark paints nothing at all, which the probe also shows: the marked
/// paragraph's line has no fill behind it anywhere.
/// </remarks>
public class HighlightTests(ITestOutputHelper output)
{
    /// <summary>Every box of the probe, against the box Word drew.</summary>
    [Fact]
    public void Every_box_is_the_box_word_draws()
    {
        if (TestFonts.SkipForMissingFonts("highlight-probe")) return;

        var word = Boxes(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "highlight-probe.pdf")));
        var ours = Boxes(Converter.Convert(Fixtures.Build("highlight-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        foreach (var box in word) output.WriteLine($"word {box}");
        foreach (var box in ours) output.WriteLine($"ours {box}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.Equal(word[i].PageIndex, ours[i].PageIndex);
            Assert.Equal(word[i].ColorHex, ours[i].ColorHex);
            Assert.Equal(word[i].Left, ours[i].Left, 0.001);
            Assert.Equal(word[i].Top, ours[i].Top, 0.001);
            Assert.Equal(word[i].Width, ours[i].Width, 0.001);
            Assert.Equal(word[i].Height, ours[i].Height, 0.001);
        }
    }

    /// <summary>
    /// The sixteen names, in the order the probe writes them, with the colour Word paints each in.
    /// </summary>
    /// <remarks>
    /// Written out here rather than only compared, because the mapping is the whole of what a name
    /// means: nothing in the document says what "darkCyan" is, and a wrong entry would still be a
    /// rectangle in the right place.
    /// </remarks>
    [Fact]
    public void The_sixteen_names_are_the_colours_word_paints()
    {
        if (TestFonts.SkipForMissingFonts("highlight-probe")) return;

        string[] expected =
        [
            "FFFF00", "00FF00", "00FFFF", "FF00FF", "0000FF", "FF0000", "000080", "008080",
            "008000", "800080", "800000", "808000", "808080", "C0C0C0", "000000", "FFFFFF"
        ];

        var word = Boxes(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "highlight-probe.pdf")))
            .Where(box => box.PageIndex == 0).Select(box => box.ColorHex).ToList();

        var ours = Boxes(Converter.Convert(Fixtures.Build("highlight-probe"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }))
            .Where(box => box.PageIndex == 0).Select(box => box.ColorHex).ToList();

        output.WriteLine(string.Join(" ", ours));

        Assert.Equal(expected, word);
        Assert.Equal(expected, ours);
    }

    /// <summary>
    /// The box is the line's height and the run's width, and it is the line the two-size page
    /// settles: the highlighted run there is a third of the height of the line it is on.
    /// </summary>
    [Fact]
    public void The_box_is_as_tall_as_the_line_and_not_as_tall_as_the_run()
    {
        if (TestFonts.SkipForMissingFonts("highlight-probe")) return;

        var ours = Boxes(Converter.Convert(Fixtures.Build("highlight-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        var pair = ours.Where(box => box.PageIndex == 3).ToList();
        Assert.Equal(2, pair.Count);

        // Twelve point Times on a line made forty-one points tall by the thirty-six point run
        // beside it, and then the thirty-six point run on a line of its own making.
        Assert.True(pair[0].Height > 41 && pair[0].Height < 42,
            $"the small run's box is {pair[0].Height:0.###}pt tall, not the line's forty-one");

        Assert.True(Math.Abs(pair[0].Height - pair[1].Height) <= 0.24 + 0.001,
            "the two boxes are on lines of the same height and should agree to a grid step");
    }

    private static List<ExtractedRectangle> Boxes(byte[] pdf) =>
        PdfPathExtractor.Extract(pdf)
            .OrderBy(box => box.PageIndex).ThenBy(box => box.Top).ThenBy(box => box.Left)
            .ToList();
}
