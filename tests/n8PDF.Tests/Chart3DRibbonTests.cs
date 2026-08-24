using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The ribbons of a 3-D line or area chart, held ink to ink against Word's raster of
/// <c>chart-3d-ribbon-probe</c> by colour family.
/// </summary>
/// <remarks>
/// The probe's values zigzag so the folds show: a fold put back mitred flat, a ribbon drawn to
/// its full row depth, or the roofs shaded level all repaint whole segments, which the two-way
/// family masks catch. The rows and depths are the bars' arrangement, already corner-verified;
/// what these pages add is the ribbon geometry itself — the edge-to-edge span, the sloped
/// roofs at their measured shades (0.827 rising, 0.639 falling), the thin line ribbon, and the
/// stacked pile.
/// </remarks>
public class Chart3DRibbonTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-ribbon-probe";

    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(0, "r", "area zigzag")]
    [InlineData(1, "r", "line zigzag")]
    [InlineData(2, "rb", "area two series")]
    [InlineData(3, "rb", "line two series")]
    [InlineData(4, "rb", "area stacked")]
    [InlineData(5, "r", "line camera")]
    public void The_ribbons_land_where_words_do(int page, string colours, string what)
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return;

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
            return;
        }

        foreach (var c in colours)
        {
            Func<(byte R, byte G, byte B), bool> belongs = c switch
            {
                'r' => p => p.R > 85 && p.R - p.G > 50 && p.R - p.B > 50,
                _ => p => p.B > 85 && p.B - p.R > 50 && p.B - p.G > 50,
            };

            var (wordInk, wordNear, oursInk, oursNear) = (0, 0, 0, 0);

            bool Near(RenderedPage page2, double x, double y)
            {
                for (var dy = -3; dy <= 3; dy++)
                for (var dx = -3; dx <= 3; dx++)
                    if (belongs(page2.At(x + dx / scale, y + dy / scale, scale)))
                        return true;
                return false;
            }

            for (var y = 74.0; y < 287; y += 1 / scale)
            for (var x = 74.0; x < 431; x += 1 / scale)
            {
                var w = belongs(word.At(x, y, scale));
                var o = belongs(mine.At(x, y, scale));
                if (w) { wordInk++; if (o || Near(mine, x, y)) wordNear++; }
                if (o) { oursInk++; if (w || Near(word, x, y)) oursNear++; }
            }

            var wordCovered = wordInk == 0 ? 1 : (double)wordNear / wordInk;
            var oursCovered = oursInk == 0 ? 1 : (double)oursNear / oursInk;

            _output.WriteLine($"p{page} {what} '{c}': word covered {wordCovered:0.0000}, " +
                              $"ours covered {oursCovered:0.0000} ({wordInk}/{oursInk} px)");
            Assert.True(wordInk > 500, $"p{page} '{c}': Word left almost no ink");
            Assert.True(wordCovered > 0.85,
                $"p{page} {what} '{c}': only {wordCovered:0.0000} of Word's ink is covered");
            Assert.True(oursCovered > 0.85,
                $"p{page} {what} '{c}': only {oursCovered:0.0000} of our ink is near Word's");
        }
    }
}
