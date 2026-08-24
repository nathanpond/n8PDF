using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The three-dimensional pie — sectors, rim, turns, explosion — held ink to ink against Word's
/// raster of <c>chart-3d-pie-probe</c>, by colour family.
/// </summary>
/// <remarks>
/// Families rather than exact colours, deliberately: Word paints the rim as a cylindrical
/// gradient running the sector's colour from about 0.35 of itself to full, and it is drawn here
/// flat at the middle of that range — so the geometry is what these compare, not the light
/// model. The put-back-wrong cases live in the same masks: a rim drawn on the far arcs, a
/// circle drawn in place of the ellipse, or a turn ignored each move whole sectors of a family
/// to where another family stands, which the two-way coverage catches at ten times these bars'
/// slack — measured by trying each while the laws were being fitted.
///
/// The ellipse and rim laws are exact at perspective nought and fitted families under
/// perspective — see <see cref="n8PDF.Layout.Chart3DComposer"/>'s remarks. The absent-scene
/// page — what a real document's 3-D pie almost always is — holds tightest; the stated-scene
/// pages carry looser bars because every one of them states an <c>hPercent</c>, and a stated
/// height changes the pie's thickness split in a way not yet modelled (the silhouette holds
/// still while the rim fattens at the top face's expense — the follow-up issue holds the
/// measurements). The explosion page's rescaling is likewise the follow-up's, as is the
/// perspective's non-uniform sector mapping: a small slice at the pie's back reads a third
/// smaller in Word's raster than its share of the affine ellipse, so the bars here bound the
/// geometry rather than certify it — the follow-up's acceptance is tightening them.
/// </remarks>
public class Chart3DPieTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-pie-probe";

    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(0, "absent scene", 0.64)]
    [InlineData(1, "rotX 10", 0.6)]
    [InlineData(2, "rotX 25", 0.63)]
    [InlineData(3, "rotX 40", 0.69)]
    [InlineData(4, "turn 45", 0.59)]
    [InlineData(5, "turn 135", 0.59)]
    [InlineData(6, "turn 225", 0.59)]
    [InlineData(7, "turn 315", 0.59)]
    [InlineData(8, "explosion 25", 0.5)]
    [InlineData(9, "hPercent 150", 0.59)]
    [InlineData(10, "perspective 60", 0.47)]
    [InlineData(11, "parallel rotX 10", 0.69)]
    [InlineData(12, "parallel rotX 15", 0.7)]
    [InlineData(13, "parallel rotX 25", 0.72)]
    [InlineData(14, "parallel rotX 40", 0.74)]
    [InlineData(15, "p15 rotX 15", 0.65)]
    [InlineData(16, "p15 rotX 25", 0.68)]
    [InlineData(17, "p45 rotX 15", 0.54)]
    [InlineData(18, "p60 rotX 40", 0.63)]
    public void The_sectors_and_rim_land_where_words_do(int page, string what, double bar)
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

        var families = new (string Name, Func<(byte R, byte G, byte B), bool> Belongs)[]
        {
            ("red", p => p.R > 85 && p.R - p.G > 50 && p.R - p.B > 50),
            ("blue", p => p.B > 85 && p.B - p.R > 50 && p.B - p.G > 50),
            ("green", p => p.G > 70 && p.G - p.R > 40 && p.G - p.B > 40),
        };

        foreach (var (name, belongs) in families)
        {
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

            // The small back slice bears the whole of the perspective's sector pinch, so its
            // floor sits deeper below the page's bar than the two big slices'.
            var floor = name == "green" ? Math.Max(0.30, bar - 0.17) : bar;

            _output.WriteLine($"p{page} {what} {name}: word covered {wordCovered:0.0000}, " +
                              $"ours covered {oursCovered:0.0000} ({wordInk}/{oursInk} px)");
            Assert.True(wordInk > 500, $"p{page} {name}: Word left almost no ink");
            Assert.True(wordCovered > floor,
                $"p{page} {what} {name}: only {wordCovered:0.0000} of Word's ink is covered, under {floor}");
            Assert.True(oursCovered > floor,
                $"p{page} {what} {name}: only {oursCovered:0.0000} of our ink is near Word's, under {floor}");
        }
    }
}
