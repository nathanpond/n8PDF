using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a cell makes of a shading pattern, and what "no shading" means where a cell says it.
/// </summary>
/// <remarks>
/// A cell blends its two colours exactly as a paragraph and a run do — cell-shading-probe asks for
/// six shares of red over yellow and Word's cells come out the same six colours as the paragraph's
/// — and differs from them in one thing only: an **automatic fill is a white surface** in a cell.
/// A cell asking for a clear pattern over an automatic fill is painted white; a paragraph asking
/// for exactly the same thing is not painted at all.
///
/// Three more answers the probe gives, none of them guessable from the format:
///
///   * <c>nil</c> paints nothing, in a cell as anywhere else;
///   * a **texture** — <c>horzStripe</c> and its kind — is a real hatch in Word, which its export
///     writes as a tiling pattern. A flat rectangle of the fill is what is drawn here instead, and
///     that is an approximation rather than a match;
///   * a <c>w:shd</c> on the **table** reaches no cell at all. Word's export has nothing behind
///     the cell that says nothing of its own, so neither has this.
/// </remarks>
public class CellShadingTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every colour of the probe, in the order it stands on the page, against Word's own.
    /// </summary>
    /// <remarks>
    /// The colours are compared rather than the rectangles: Word paints each cell twice over, once
    /// to its edge and once inset by half a point inside its border, where this paints the ground
    /// once. Both cover the cell; only one of them is a number worth holding to.
    /// </remarks>
    [Fact]
    public void Every_cell_is_the_colour_word_paints_it()
    {
        if (TestFonts.SkipForMissingFonts("cell-shading-probe")) return;

        var word = Fills(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "cell-shading-probe.pdf")));
        var ours = Fills(Converter.Convert(Fixtures.Build("cell-shading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        output.WriteLine($"word {string.Join(" ", word.Select(fill => $"{fill.Left:0}:{fill.ColorHex}"))}");
        output.WriteLine($"ours {string.Join(" ", ours.Select(fill => $"{fill.Left:0}:{fill.ColorHex}"))}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.Equal(Math.Round(word[i].Left, 2), Math.Round(ours[i].Left, 2));

            // The striped cell excepted: Word paints a hatch there and this paints the fill under
            // it, which a reader of flat fills reports as Word's pattern having no colour at all.
            if (ours[i].ColorHex == "FFFF00" && word[i].ColorHex == "000000") continue;

            Assert.Equal(word[i].ColorHex, ours[i].ColorHex);
        }
    }

    /// <summary>
    /// The six shares, written out: a pattern is a blend, and <c>pct12</c> is an eighth rather
    /// than the twelfth its name suggests.
    /// </summary>
    [Fact]
    public void A_pattern_is_a_share_of_its_colour_over_its_fill()
    {
        if (TestFonts.SkipForMissingFonts("cell-shading-probe")) return;

        // Red over yellow at a twentieth, a tenth, an eighth, a quarter, a half and three
        // quarters. The eighth is pct12, whose name states the whole part of twelve and a half.
        string[] expected = ["FFF200", "FFE500", "FFDF00", "FFBF00", "FF7F00", "FF4000"];

        var word = Row(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "cell-shading-probe.pdf")), 0);
        var ours = Row(Converter.Convert(Fixtures.Build("cell-shading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), 0);

        Assert.Equal(expected, word);
        Assert.Equal(expected, ours);
    }

    /// <summary>
    /// The second row: what the words for "none" come to, and what a texture and an automatic
    /// fill come to.
    /// </summary>
    /// <remarks>
    /// The striped cell is the one place this does not match Word and says so. Word draws a real
    /// hatch — its export writes a tiling pattern, which a reader of flat fills cannot see a
    /// colour in at all — and what is drawn here is the fill the hatch was to be laid over.
    /// </remarks>
    [Fact]
    public void An_automatic_fill_is_white_in_a_cell()
    {
        if (TestFonts.SkipForMissingFonts("cell-shading-probe")) return;

        // solid, clear, nil, auto, half red over auto, and then the stripe. The nil cell paints
        // nothing and so has no colour in either list.
        string[] expected = ["FF0000", "FFFF00", "FFFFFF", "FF7F7F"];

        var word = Row(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "cell-shading-probe.pdf")), 1);
        var ours = Row(Converter.Convert(Fixtures.Build("cell-shading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), 1);

        output.WriteLine($"word {string.Join(" ", word)}");
        output.WriteLine($"ours {string.Join(" ", ours)}");

        Assert.Equal(expected, word[..4]);
        Assert.Equal(expected, ours[..4]);

        // And the stripe, which is where the two part company.
        Assert.Equal(5, word.Count);
        Assert.Equal("FFFF00", ours[4]);
    }

    /// <summary>A shading on the table itself reaches no cell of it, in Word or here.</summary>
    [Fact]
    public void A_shading_on_the_table_reaches_no_cell()
    {
        if (TestFonts.SkipForMissingFonts("cell-shading-probe")) return;

        // The four cells are: nothing of its own, a pattern of its own, an automatic fill, and a
        // solid green. The first is unpainted in both, which is what says the table's own pct25
        // never reached it.
        string[] expected = ["FF7F00", "FFFFFF", "00B050"];

        var word = Row(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "cell-shading-probe.pdf")), 4);
        var ours = Row(Converter.Convert(Fixtures.Build("cell-shading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), 4);

        Assert.Equal(expected, word);
        Assert.Equal(expected, ours);
    }

    /// <summary>The colours of one band of fills across the page, left to right.</summary>
    /// <remarks>
    /// Banded by their top edge, since the two documents put the rows a fraction apart: what is
    /// being compared here is which colour each cell came out, not where its box is.
    /// </remarks>
    private static List<string> Row(byte[] pdf, int band)
    {
        var fills = Fills(pdf);
        var bands = new List<List<ExtractedRectangle>>();

        foreach (var fill in fills)
        {
            if (bands.Count > 0 && Math.Abs(bands[^1][0].Top - fill.Top) < 2) bands[^1].Add(fill);
            else bands.Add([fill]);
        }

        return band >= bands.Count
            ? []
            : [.. bands[band].OrderBy(fill => fill.Left).Select(fill => fill.ColorHex)];
    }

    /// <summary>
    /// The fills of the document, one to a cell: Word's second, inset pass over each cell is
    /// dropped so that the two documents are counted the same way.
    /// </summary>
    private static List<ExtractedRectangle> Fills(byte[] pdf)
    {
        var kept = new List<ExtractedRectangle>();

        foreach (var fill in PdfPathExtractor.Extract(pdf)
                     .OrderByDescending(fill => fill.Width)
                     .ThenBy(fill => fill.Left))
        {
            if (kept.Any(seen =>
                    seen.PageIndex == fill.PageIndex &&
                    Math.Abs(seen.Top - fill.Top) < 1 &&
                    Math.Abs(seen.Left - fill.Left) < 1))
            {
                continue;
            }

            kept.Add(fill);
        }

        return [.. kept.OrderBy(fill => fill.PageIndex).ThenBy(fill => fill.Top).ThenBy(fill => fill.Left)];
    }
}
