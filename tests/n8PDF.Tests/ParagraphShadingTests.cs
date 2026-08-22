using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a paragraph's <c>w:shd</c> paints.
/// </summary>
/// <remarks>
/// paragraph-shading-probe measures four things against Word's own export:
///
///   the reach      a fiftieth of an inch past the paragraph's own edges, on both sides: text
///                  from 72 to 540 is shaded from 70.56 to 541.44
///   the edges      the paragraph's indents move it, the first line's indent does not, and
///                  centring the text does not — it is the paragraph that is shaded, not the line
///   the boxes      one rectangle per line, each covering its line box exactly, so that the lines
///                  of a paragraph and two paragraphs in a row all tile without a seam
///   the pattern    a straight blend: pct25 of red on yellow is #FFBF00, and solid is the pattern
///                  colour alone
/// </remarks>
public class ParagraphShadingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every fill of the probe, against Word's.
    /// </summary>
    /// <remarks>
    /// Fifteen of the sixteen are Word's to the thousandth of a point. The sixteenth is the
    /// paragraph with twelve points of space before and after it, which is the one paragraph in
    /// the probe whose line spacing is left to the document's 1.08 default, and its fill is a
    /// single grid step shorter than Word's — the residual of the line-height rounding, which
    /// ImageLineTests measures in its own right, and not of anything this rule does.
    /// </remarks>
    [Fact]
    public void Every_fill_is_words()
    {
        if (TestFonts.SkipForMissingFonts("paragraph-shading-probe")) return;

        var word = Fills(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "paragraph-shading-probe.pdf")));
        var ours = Fills(Converter.Convert(Fixtures.Build("paragraph-shading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        Assert.Equal(word.Count, ours.Count);

        var exact = 0;

        for (var i = 0; i < word.Count; i++)
        {
            output.WriteLine($"word {word[i]}\nours {ours[i]}");

            Assert.Equal(word[i].PageIndex, ours[i].PageIndex);
            Assert.Equal(word[i].ColorHex, ours[i].ColorHex);
            Assert.Equal(word[i].Left, ours[i].Left, 0.001);
            Assert.Equal(word[i].Width, ours[i].Width, 0.001);
            Assert.Equal(word[i].Top, ours[i].Top, 0.001);

            var apart = Math.Abs(word[i].Height - ours[i].Height);
            Assert.True(apart <= Step + 0.001,
                $"fill {i} is {ours[i].Height:0.###}pt tall where Word's is {word[i].Height:0.###}");

            if (apart <= 0.001) exact++;
        }

        output.WriteLine($"{exact} of {word.Count} exact");
        Assert.True(exact >= 15, $"only {exact} of {word.Count} fills are exactly Word's, where 15 were");
    }

    /// <summary>
    /// The fills of a paragraph and of its neighbour meet: no seam, no overlap.
    /// </summary>
    /// <remarks>
    /// The probe's third page is two shaded paragraphs of the same colour followed by a third of
    /// another, so what is being checked is that the boxes tile whether or not the paragraph
    /// changes — which is what makes a run of shaded paragraphs look like one block, as it does in
    /// Word.
    /// </remarks>
    [Fact]
    public void The_fills_of_neighbouring_lines_meet()
    {
        if (TestFonts.SkipForMissingFonts("paragraph-shading-probe")) return;

        var ours = Fills(Converter.Convert(Fixtures.Build("paragraph-shading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        var run = ours.Where(fill => fill.PageIndex == 2).ToList();
        Assert.Equal(3, run.Count);

        for (var i = 1; i < run.Count; i++)
        {
            Assert.Equal(run[i - 1].Bottom, run[i].Top, 0.001);
        }
    }

    /// <summary>
    /// A pattern is a share of its colour laid over its fill, and Word's shares are exact.
    /// </summary>
    [Fact]
    public void A_pattern_is_a_blend_of_the_two_colours()
    {
        if (TestFonts.SkipForMissingFonts("paragraph-shading-probe")) return;

        // Red over yellow at a tenth, a quarter, a half, three quarters, and all of it.
        string[] expected = ["FFE500", "FFBF00", "FF7F00", "FF4000", "FF0000"];

        var word = Fills(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "paragraph-shading-probe.pdf")))
            .Where(fill => fill.PageIndex == 3).Select(fill => fill.ColorHex).ToList();

        var ours = Fills(Converter.Convert(Fixtures.Build("paragraph-shading-probe"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }))
            .Where(fill => fill.PageIndex == 3).Select(fill => fill.ColorHex).ToList();

        output.WriteLine(string.Join(" ", ours));

        Assert.Equal(expected, word);
        Assert.Equal(expected, ours);
    }

    /// <summary>
    /// A shaded paragraph broken across a page leaves nothing behind on the page it left.
    /// </summary>
    /// <remarks>
    /// A background is painted where a line is placed, and a line placed at the foot of a page can
    /// be taken off again and laid on the next — widow control alone moves two of them. What the
    /// line painted has to go with it, or the page it left keeps a fill under empty space. The
    /// same bookkeeping carries a highlight, a bar tab's rule and a form field's box, none of
    /// which were taken off either before this.
    /// </remarks>
    [Fact]
    public void A_paragraph_broken_across_a_page_leaves_no_fill_behind()
    {
        var builder = new DocxBuilder();

        // Enough to fill the first page and leave the shaded paragraph straddling the break.
        for (var i = 1; i <= 42; i++)
            builder.AddParagraph($"Filler paragraph {i}.", Zero, Times);

        builder.AddParagraph(
            "A shaded paragraph long enough to take several lines, written where the page is "
            + "nearly full so that the break falls inside it and some of its lines are laid on "
            + "the page after the one they were first put on, which is the whole point of it: "
            + "what a line painted has to come off the page with the line, and be painted again "
            + "wherever the line ends up, so that neither page is left with a fill under empty "
            + "space or without one behind its text.",
            "<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFF2CC\"/>" + Zero, Times);

        var pdf = Converter.Convert(builder.Build(),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var fills = Fills(pdf).Where(fill => fill.ColorHex == "FFF2CC").ToList();
        var runs = PdfTextExtractor.Extract(pdf);

        foreach (var fill in fills) output.WriteLine(fill.ToString());

        Assert.True(fills.Count > 3, $"the paragraph took {fills.Count} lines, which is too few to break");
        Assert.Contains(fills, fill => fill.PageIndex == 0);
        Assert.Contains(fills, fill => fill.PageIndex == 1);

        // Nothing left behind: every fill has text of its own standing on it. A line taken off
        // the page whose fill stayed would show up here as a fill over empty paper.
        foreach (var fill in fills)
        {
            Assert.Contains(runs, run =>
                run.PageIndex == fill.PageIndex &&
                run.BaselineY > fill.Top && run.BaselineY < fill.Bottom);
        }

        // And nothing painted twice: a line laid again would put a second fill over the first.
        for (var i = 1; i < fills.Count; i++)
        {
            if (fills[i].PageIndex != fills[i - 1].PageIndex) continue;

            Assert.True(fills[i].Top >= fills[i - 1].Bottom - 0.001,
                $"{fills[i]} overlaps {fills[i - 1]}");
        }
    }

    private const string Zero =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private static readonly string Times =
        DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 24);

    private const double Step = 0.24;

    private static List<ExtractedRectangle> Fills(byte[] pdf) =>
        PdfPathExtractor.Extract(pdf)
            .OrderBy(fill => fill.PageIndex).ThenBy(fill => fill.Top).ThenBy(fill => fill.Left)
            .ToList();
}
