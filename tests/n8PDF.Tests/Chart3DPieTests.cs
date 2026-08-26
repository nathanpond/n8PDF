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
/// The ellipse and rim under perspective are one projected camera now, not two fitted families —
/// the tilted disc seen through Word's perspective divide — see
/// <see cref="n8PDF.Layout.Chart3DComposer"/>'s remarks; only the rise off centre stays fitted.
/// The absent-scene
/// page — what a real document's 3-D pie almost always is — holds tightest. <c>hPercent</c> sets
/// the cylinder's depth and nothing else: the parallel <c>hPercent</c> pages (50–200) match Word
/// to a point, the rim growing dead-linearly while the top ellipse and the width hold. Under
/// perspective that depth still interacts with the flatten in a way the camera does not fully
/// catch at a steep tilt and a thick pie together (the follow-up owns it), so those pages are not
/// asserted. The explosion pages are now derived rather than fitted: the pie shrinks so the
/// exploded arrangement fits, the disc's centre holds, and the front slices land within a point
/// of Word across the sweep — but the same non-uniform sector mapping the follow-up owns caps
/// what a single per-page bar can reach, because a small slice at the pie's back reads a third
/// smaller in Word's raster than its share of the affine ellipse, and an explosion shrinks that
/// back slice further. So the explosion bars, like the perspective ones, bound the back-slice
/// geometry rather than certify it — the follow-up's acceptance is tightening those.
/// </remarks>
public class Chart3DPieTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-pie-probe";

    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(0, "absent scene", 0.9)]
    [InlineData(1, "rotX 10", 0.6)]
    [InlineData(2, "rotX 25", 0.66)]
    [InlineData(3, "rotX 40", 0.7)]
    [InlineData(4, "turn 45", 0.62)]
    [InlineData(5, "turn 135", 0.62)]
    [InlineData(6, "turn 225", 0.62)]
    [InlineData(7, "turn 315", 0.62)]
    [InlineData(8, "explosion 25", 0.53)]
    [InlineData(9, "hPercent 150", 0.62)]
    [InlineData(10, "perspective 60", 0.49)]
    [InlineData(11, "parallel rotX 10", 0.7)]
    [InlineData(12, "parallel rotX 15", 0.72)]
    [InlineData(13, "parallel rotX 25", 0.74)]
    [InlineData(14, "parallel rotX 40", 0.76)]
    [InlineData(15, "p15 rotX 15", 0.68)]
    [InlineData(16, "p15 rotX 25", 0.71)]
    [InlineData(17, "p45 rotX 15", 0.57)]
    [InlineData(18, "p60 rotX 40", 0.57)]
    [InlineData(19, "explosion 10", 0.67)]
    [InlineData(20, "explosion 25 blue", 0.67)]
    [InlineData(21, "p0 hPercent 50", 0.74)]
    [InlineData(22, "p0 hPercent 100", 0.71)]
    [InlineData(23, "p0 hPercent 150", 0.7)]
    [InlineData(24, "p0 hPercent 200", 0.68)]
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
                if (w)
                {
                    wordInk++;
                    if (o || Near(mine, x, y)) wordNear++;
                }

                if (o)
                {
                    oursInk++;
                    if (w || Near(word, x, y)) oursNear++;
                }
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