using n8PDF.Images;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The 3-D pie's rim is a cylinder wall lit from the left, not a flat band (#166): the left of the
/// front reads bright, the right an ambient floor. Measured against Word's raster of
/// <c>chart-3d-pie-probe</c>'s tall-rim pages, where the rim is deep enough to sample.
/// </summary>
public class Chart3DPieRimTests(ITestOutputHelper output)
{
    private const string FixtureName = "chart-3d-pie-probe";

    private static bool Red((byte R, byte G, byte B) p) => p.R > 40 && p.G < 45 && p.B < 45;
    private static bool Blue((byte R, byte G, byte B) p) => p.B > 40 && p.R < 45 && p.G < 45;

    [Theory]
    [InlineData(14, "parallel rotX 40")]
    [InlineData(3, "rotX 40 persp 30")]
    public void The_rim_is_bright_on_the_left_and_an_ambient_floor_on_the_right(int page, string what)
    {
        if (TestFonts.SkipForMissingFonts(FixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, FixtureName + ".pdf");
        var ours = Converter.Convert(Fixtures.Build(FixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        const double scale = 6;
        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(File.ReadAllBytes(reference), page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        // Blue is the left-front sector, red the right-front; each rim reads its own lit shade.
        var (wordRedRim, wordBlueRim) = (RimShade(word.Pixels, Red), RimShade(word.Pixels, Blue));
        var (oursRedRim, oursBlueRim) = (RimShade(mine.Pixels, Red), RimShade(mine.Pixels, Blue));

        output.WriteLine($"p{page} {what}: Word red-rim {wordRedRim:F3} blue-rim {wordBlueRim:F3}; " +
                         $"ours red-rim {oursRedRim:F3} blue-rim {oursBlueRim:F3}");

        // Word lights the rim from the left: the left (blue) rim is clearly brighter than the
        // right (red) one, and it does so by a wide margin.
        Assert.True(wordBlueRim - wordRedRim > 0.15, $"p{page}: Word's rim should be graded, got " +
                    $"blue {wordBlueRim:F3} vs red {wordRedRim:F3}");

        // We reproduce that: our rim is graded the same way, not flat.
        Assert.True(oursBlueRim - oursRedRim > 0.15, $"p{page}: our rim should be graded left-bright, got " +
                    $"blue {oursBlueRim:F3} vs red {oursRedRim:F3}");

        // And it lands near Word's shade on each side — the ambient floor tighter than the lit side,
        // where the affine arc angle and Word's true cylinder normal diverge a little.
        Assert.True(Math.Abs(oursRedRim - wordRedRim) < 0.10,
            $"p{page}: our right-rim {oursRedRim:F3} is off Word's {wordRedRim:F3}");
        Assert.True(Math.Abs(oursBlueRim - wordBlueRim) < 0.15,
            $"p{page}: our left-rim {oursBlueRim:F3} is off Word's {wordBlueRim:F3}");
    }

    // The mean shade (family channel / 255) of a family's rim: the band below the pie's horizontal
    // axis, sampled three pixels up from each column's lowest pixel of that family.
    private static double RimShade(ImageData px, Func<(byte, byte, byte), bool> fam)
    {
        bool At(int x, int y)
        {
            var i = (y * px.Width + x) * 3;
            return fam((px.Data[i], px.Data[i + 1], px.Data[i + 2]));
        }

        // The pie's axis row: the widest run of pie ink (red or blue), immune to the legend swatches.
        var (bestW, axisY) = (0, 0);
        for (var y = 0; y < px.Height; y++)
        {
            int start = -1, bestL = 0;
            for (var x = 0; x < px.Width; x++)
            {
                var ink = Red((px.Data[(y * px.Width + x) * 3], px.Data[(y * px.Width + x) * 3 + 1],
                    px.Data[(y * px.Width + x) * 3 + 2])) || Blue((px.Data[(y * px.Width + x) * 3],
                    px.Data[(y * px.Width + x) * 3 + 1], px.Data[(y * px.Width + x) * 3 + 2]));
                if (ink) { if (start < 0) start = x; bestL = Math.Max(bestL, x - start + 1); }
                else start = -1;
            }
            if (bestL > bestW) { bestW = bestL; axisY = y; }
        }

        var chan = fam((255, 0, 0)) ? 0 : 2; // red -> R channel, blue -> B channel
        var shades = new List<double>();
        for (var x = 0; x < px.Width; x++)
        {
            int lo = -1;
            for (var y = px.Height - 1; y > axisY; y--) if (At(x, y)) { lo = y; break; }
            if (lo < 0 || lo - axisY < 8) continue; // a real rim band below the axis
            var sy = lo - 3;
            if (!At(x, sy)) continue;
            shades.Add(px.Data[(sy * px.Width + x) * 3 + chan] / 255.0);
        }

        return shades.Count > 0 ? shades.Average() : 0;
    }
}
