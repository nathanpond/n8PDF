using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The boxes themselves — faces, shades, painter order, negatives and the lying direction —
/// held ink to ink against Word's raster of <c>chart-3d-box-probe</c>.
/// </summary>
/// <remarks>
/// Ink agreement is the order proof as well as the placement proof: on the cluster page the
/// middle bar covers its neighbour's side face, and on the depth pages the near row covers the
/// far one's foot, so an order put back wrong repaints whole faces in the wrong colour and the
/// per-colour masks tear apart. The bars carry the asymmetry the wall tests document — Word's
/// raster antialiases every edge and stands its bars against blended neighbours, so our crisp
/// ink always covers his and never quite the reverse.
///
/// The negative page holds three findings at once: a bar below nought is drawn — white, black
/// outlined even though the series asks for no outline, hanging from nought — which is exactly
/// why #113 found "no ink of the series' colour": the ink is white. Its top face takes the same
/// three-quarter shade white takes everywhere else.
/// </remarks>
public class Chart3DBoxTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-box-probe";

    private readonly ITestOutputHelper _output = output;

    private (RenderedPage Ours, RenderedPage Word, double Scale)? Pages(int page)
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return null;

        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(FixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        const double scale = 6;
        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(File.ReadAllBytes(reference), page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        return (mine, word, scale);
    }

    private (double WordCovered, double OursCovered, int WordInk, int OursInk) Agreement(
        RenderedPage ours, RenderedPage word, double scale,
        Func<(byte R, byte G, byte B), bool> belongs)
    {
        var (wordInk, wordNear, oursInk, oursNear) = (0, 0, 0, 0);

        bool Near(RenderedPage page, double x, double y)
        {
            for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
                if (belongs(page.At(x + dx / scale, y + dy / scale, scale)))
                    return true;
            return false;
        }

        for (var y = 74.0; y < 287; y += 1 / scale)
        for (var x = 74.0; x < 431; x += 1 / scale)
        {
            var w = belongs(word.At(x, y, scale));
            var o = belongs(ours.At(x, y, scale));

            if (w) { wordInk++; if (o || Near(ours, x, y)) wordNear++; }
            if (o) { oursInk++; if (w || Near(word, x, y)) oursNear++; }
        }

        return (wordInk == 0 ? 1 : (double)wordNear / wordInk,
                oursInk == 0 ? 1 : (double)oursNear / oursInk, wordInk, oursInk);
    }

    /// <summary>
    /// Every coloured face lands where Word lands it: rows far to front, clusters left to
    /// right, stacks bottom to top, on both arms, at a turn past 180, and lying down.
    /// </summary>
    [Theory]
    [InlineData(0, "rb", "standard 20, square")]
    [InlineData(1, "rb", "standard 340, mirrored")]
    [InlineData(2, "rb", "standard 110, second quadrant")]
    [InlineData(3, "rbg", "clustered, abutting")]
    [InlineData(4, "rbg", "stacked, piled")]
    [InlineData(6, "r", "lying")]
    [InlineData(7, "rb", "standard 20, camera")]
    [InlineData(8, "rb", "lying, two categories")]
    public void The_boxes_land_where_words_do(int page, string colours, string what)
    {
        if (Pages(page) is not { } pages) return;

        foreach (var c in colours)
        {
            Func<(byte R, byte G, byte B), bool> belongs = c switch
            {
                'r' => p => p.R > 110 && p.G < 90 && p.B < 90,
                'b' => p => p.B > 110 && p.R < 90 && p.G < 90,
                _ => p => p.G > 90 && p.R < 90 && p.B < 90,
            };

            var (wordCovered, oursCovered, wordInk, oursInk) =
                Agreement(pages.Ours, pages.Word, pages.Scale, belongs);

            _output.WriteLine($"p{page} {what} '{c}': word covered {wordCovered:0.0000}, " +
                              $"ours covered {oursCovered:0.0000} ({wordInk}/{oursInk} px)");
            Assert.True(wordInk > 1000, $"p{page} '{c}': Word left almost no ink");

            var (word, ours) = (0.97, 0.93);
            Assert.True(wordCovered > word,
                $"p{page} {what} '{c}': only {wordCovered:0.0000} of Word's ink is covered by ours");
            Assert.True(oursCovered > ours,
                $"p{page} {what} '{c}': only {oursCovered:0.0000} of our ink is near Word's");
        }
    }

    /// <summary>
    /// A bar below nought is drawn white with a black outline, hanging from nought — and no ink
    /// of the series' colour appears anywhere, which is what the first measurement saw.
    /// </summary>
    [Fact]
    public void A_negative_bar_is_white_outlined_and_hangs_from_nought()
    {
        if (Pages(5) is not { } pages) return;

        // The outline: near-black hairlines.
        var outline = Agreement(pages.Ours, pages.Word, pages.Scale,
            p => p is { R: < 90, G: < 90, B: < 90 });
        _output.WriteLine($"outline: word covered {outline.WordCovered:0.0000}, " +
                          $"ours covered {outline.OursCovered:0.0000} ({outline.WordInk}/{outline.OursInk} px)");
        Assert.True(outline.WordInk > 500, "Word drew no outline for the negative bar");
        Assert.True(outline.WordCovered > 0.90 && outline.OursCovered > 0.85,
            $"the outline disagrees: {outline.WordCovered:0.0000}/{outline.OursCovered:0.0000}");

        // The top face: the three-quarter shade of white.
        var top = Agreement(pages.Ours, pages.Word, pages.Scale,
            p => Math.Abs(p.R - p.G) < 8 && Math.Abs(p.G - p.B) < 8 && p.R is > 175 and < 210);
        _output.WriteLine($"grey top: word covered {top.WordCovered:0.0000}, " +
                          $"ours covered {top.OursCovered:0.0000} ({top.WordInk}/{top.OursInk} px)");
        Assert.True(top.WordInk > 500 && top.WordCovered > 0.90,
            "the shaded white top face is not where Word puts it");

        // And nothing red anywhere, on his page or ours.
        for (var y = 74.0; y < 287; y += 1)
        for (var x = 74.0; x < 431; x += 1)
        {
            Assert.False(pages.Word.At(x, y, pages.Scale) is { R: > 150, G: < 90, B: < 90 },
                $"Word drew series-coloured ink at ({x},{y})");
            Assert.False(pages.Ours.At(x, y, pages.Scale) is { R: > 150, G: < 90, B: < 90 },
                $"we drew series-coloured ink at ({x},{y})");
        }
    }
}
