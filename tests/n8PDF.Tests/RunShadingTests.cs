using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a run's own background — a <c>w:shd</c> inside a <c>w:rPr</c> — paints.
/// </summary>
/// <remarks>
/// The same rectangle a highlight paints, which is what run-shading-probe is for: its pages mirror
/// highlight-probe's, and Word draws the two the same way to the last thousandth of a point. As
/// wide as the run and as tall as the line, both edges on the grid; a space inside the line
/// covered and one dropped at a break not; a plain space between two shaded words leaving two
/// boxes; a shaded paragraph mark painting nothing.
///
/// Two things are a run background's alone, and both are measured here:
///
///   * it is drawn **over** the paragraph's own background and does not take the paragraph's
///     fiftieth of an inch of reach — it stops where the run stops;
///   * a run asking for both a background and a highlight gets **the highlight only**. Word's page
///     has one rectangle for such a run, in the highlight's colour, not two.
/// </remarks>
public class RunShadingTests(ITestOutputHelper output)
{
    /// <summary>Every box of the probe, against the box Word drew.</summary>
    [Fact]
    public void Every_box_is_the_box_word_draws()
    {
        if (TestFonts.SkipForMissingFonts("run-shading-probe")) return;

        var word = Boxes(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "run-shading-probe.pdf")));
        var ours = Boxes(Ours());

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
    /// A run with both a background and a highlight is painted once, in the highlight's colour.
    /// </summary>
    [Fact]
    public void A_highlight_is_painted_instead_of_a_background()
    {
        if (TestFonts.SkipForMissingFonts("run-shading-probe")) return;

        var ours = Boxes(Ours()).Where(box => box.PageIndex == 4).ToList();

        // The paragraph's own blue, the orange of the run inside it, and one box for the run that
        // asks for orange and yellow at once.
        Assert.Equal(["DEEBF7", "FCE4D6", "FFFF00"], ours.Select(box => box.ColorHex));

        // The orange is inside the blue rather than beside it, and drawn after it.
        Assert.True(ours[1].Left > ours[0].Left && ours[1].Right < ours[0].Right,
            "the run's background should sit within the paragraph's");
    }

    /// <summary>
    /// A background and a highlight are one rule: the same six runs, shaded in one probe and
    /// highlighted in the other, come out as the same six rectangles.
    /// </summary>
    /// <remarks>
    /// The pages are written to match — "ab lit cd" at four sizes in Times and two in Arial — so
    /// this compares the two rules against each other rather than each against Word separately,
    /// and would catch either drifting from the other.
    /// </remarks>
    [Fact]
    public void A_background_covers_what_a_highlight_covers()
    {
        if (TestFonts.SkipForMissingFonts("run-shading-probe") ||
            TestFonts.SkipForMissingFonts("highlight-probe"))
        {
            return;
        }

        var shaded = Boxes(Ours()).Where(box => box.PageIndex == 0).ToList();

        var lit = Boxes(Converter.Convert(Fixtures.Build("highlight-probe"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }))
            .Where(box => box.PageIndex == 1).ToList();

        Assert.Equal(6, shaded.Count);
        Assert.Equal(lit.Count, shaded.Count);

        for (var i = 0; i < lit.Count; i++)
        {
            output.WriteLine($"shaded {shaded[i]}   highlighted {lit[i]}");

            Assert.Equal(lit[i].Left, shaded[i].Left, 0.001);
            Assert.Equal(lit[i].Top, shaded[i].Top, 0.001);
            Assert.Equal(lit[i].Width, shaded[i].Width, 0.001);
            Assert.Equal(lit[i].Height, shaded[i].Height, 0.001);
        }
    }

    private static byte[] Ours() => Converter.Convert(Fixtures.Build("run-shading-probe"),
        new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    private static List<ExtractedRectangle> Boxes(byte[] pdf) =>
        PdfPathExtractor.Extract(pdf)
            .OrderBy(box => box.PageIndex).ThenBy(box => box.Top).ThenBy(box => box.Left)
            .ToList();
}
