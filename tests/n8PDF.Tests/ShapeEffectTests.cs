using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Shape rotation, gradient and picture fills, and shadows (#64), held to Word by ink.
/// </summary>
/// <remarks>
/// The fill probe puts one shape to a page so each colour is alone with its assertions. Word's
/// export is read with the same instrument as ours, so the laws hold against the reference
/// renderer, not against our own expectations.
/// </remarks>
public class ShapeEffectTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private sealed record Box(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;

        public double Height => Bottom - Top;
    }

    private static byte[] OursPdf(string fixture) =>
        n8PDF.Converter.Convert(Fixtures.Build(fixture),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    private static byte[]? WordsPdf(string fixture)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static Box? Find(RenderedPage page, Func<(byte R, byte G, byte B), bool> colour)
    {
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        var any = false;

        for (var y = 30.0; y < 500; y += 1 / 3.0)
        for (var x = 60.0; x < 420; x += 1 / 3.0)
        {
            if (!colour(page.At(x, y, 3))) continue;

            any = true;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return any ? new Box(left, top, right, bottom) : null;
    }

    private void AssertAgrees(string what, Box? ours, Box? word, double tolerance = 1.5)
    {
        Assert.True(ours is not null && word is not null, $"{what}: missing — ours {ours}, word {word}");

        _output.WriteLine($"{what}: ours ({ours!.Left:0.0},{ours.Top:0.0}) {ours.Width:0.0}x{ours.Height:0.0}, " +
                          $"word ({word!.Left:0.0},{word.Top:0.0}) {word.Width:0.0}x{word.Height:0.0}");

        Assert.True(
            Math.Abs(ours.Left - word.Left) < tolerance && Math.Abs(ours.Top - word.Top) < tolerance &&
            Math.Abs(ours.Width - word.Width) < 2 * tolerance && Math.Abs(ours.Height - word.Height) < 2 * tolerance,
            $"{what}: ours ({ours.Left:0.0},{ours.Top:0.0}) {ours.Width:0.0}x{ours.Height:0.0} " +
            $"is not Word's ({word.Left:0.0},{word.Top:0.0}) {word.Width:0.0}x{word.Height:0.0}");
    }

    /// <summary>A 30° turn, a quarter turn, and a mirrored triangle land where Word lands them.</summary>
    [Fact]
    public void Turned_shapes_stand_where_words_do()
    {
        var ours = PdfRasterizer.Render(OursPdf("shape-rotation-probe"), 0, 3);
        var word = WordsPdf("shape-rotation-probe") is { } reference ? PdfRasterizer.Render(reference, 0, 3) : null;

        if (ours is null || word is null)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var red = Find(ours, p => p.R > 140 && p.G < 90 && p.B < 90);

        // A 90x40 box at 30° covers 98x80 of page; drawn square it would cover 90x40.
        Assert.True(red is not null && red.Width is > 94 and < 102 && red.Height is > 76 and < 84,
            $"the 30° box's ink is {red?.Width:0.0}x{red?.Height:0.0}, not the turned bounds");

        AssertAgrees("30° box", red, Find(word, p => p.R > 140 && p.G < 90 && p.B < 90));
        AssertAgrees("quarter turn", Find(ours, p => p.B > 140 && p.R < 90),
            Find(word, p => p.B > 140 && p.R < 90));
        AssertAgrees("triangle", Find(ours, p => p.G > 120 && p.R < 90 && p.B < 90),
            Find(word, p => p.G > 120 && p.R < 90 && p.B < 90));
    }

    /// <summary>Gradient ends and blends, the clipped picture, and the shadow — page by page, both readers.</summary>
    [Fact]
    public void Fills_and_shadow_read_like_words()
    {
        var oursPdf = OursPdf("shape-fill-probe");
        var wordPdf = WordsPdf("shape-fill-probe");

        if (wordPdf is null || PdfRasterizer.Render(oursPdf, 0, 3) is null)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        foreach (var (pdf, who) in new[] { (oursPdf, "ours"), (wordPdf, "word") })
        {
            // Page 1: the horizontal gradient — red end, blue end, a blend between.
            var page = PdfRasterizer.Render(pdf, 0, 3)!;
            var red = Find(page, p => p.R > 150 && p.B < 100 && p.G < 100);
            Assert.True(red is not null, $"{who}: no red end");

            var y = red!.Top + 8;
            var mid = page.At(red.Left + 88, y, 3);

            _output.WriteLine($"{who}: gradient mid ({mid.R},{mid.G},{mid.B})");
            Assert.True(mid.R is > 60 and < 200 && mid.B is > 60 and < 200,
                $"{who}: the middle is ({mid.R},{mid.G},{mid.B}), not a blend");
            Assert.True(page.At(red.Left + 172, y, 3).B > 150, $"{who}: no blue end");

            // Page 2: three stops run down — red top, green middle, blue foot.
            page = PdfRasterizer.Render(pdf, 1, 3)!;
            var top = Find(page, p => p.R > 150 && p.G < 100 && p.B < 100);
            Assert.True(top is not null, $"{who}: no red head on the vertical gradient");

            var x = top!.Left + 20;
            Assert.True(page.At(x, top.Top + 58, 3).G > 120, $"{who}: no green middle");
            Assert.True(page.At(x, top.Top + 112, 3).B > 150, $"{who}: no blue foot");

            // Page 3: the picture stays inside its ellipse — ink at centre, none at the corner.
            page = PdfRasterizer.Render(pdf, 2, 3)!;
            var ellipse = FindInk(page);
            Assert.True(ellipse is not null, $"{who}: the pictured ellipse is missing");

            var centre = page.At((ellipse!.Left + ellipse.Right) / 2, (ellipse.Top + ellipse.Bottom) / 2, 3);
            Assert.False(centre is { R: > 240, G: > 240, B: > 240 }, $"{who}: nothing at the ellipse's centre");

            var corner = page.At(ellipse.Left + 2, ellipse.Top + 2, 3);
            Assert.True(corner is { R: > 240, G: > 240, B: > 240 },
                $"{who}: picture ink outside the ellipse at its corner: ({corner.R},{corner.G},{corner.B})");

            // Page 4: the shadow falls south-east and nowhere else.
            page = PdfRasterizer.Render(pdf, 3, 3)!;
            var amber = Find(page, p => p.R is > 210 and < 240 && p.G is > 162 and < 192 && p.B < 25);
            Assert.True(amber is not null, $"{who}: the shadowed box is missing");

            var under = page.At(amber!.Left + amber.Width / 2 + 4, amber.Bottom + 2.5, 3);
            var above = page.At(amber.Left + amber.Width / 2 - 4, amber.Top - 2.5, 3);

            _output.WriteLine($"{who}: under ({under.R},{under.G},{under.B}) above ({above.R},{above.G},{above.B})");
            Assert.True(under.R < 220 && Math.Abs(under.R - under.G) < 40 && Math.Abs(under.G - under.B) < 40,
                $"{who}: no grey shadow under the box: ({under.R},{under.G},{under.B})");
            Assert.True(above is { R: > 240, G: > 240, B: > 240 },
                $"{who}: ink above the box where no shadow falls: ({above.R},{above.G},{above.B})");
        }
    }

    /// <summary>Any non-white, non-text ink on the page — the pictured ellipse, on its page alone.</summary>
    private static Box? FindInk(RenderedPage page)
    {
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        var any = false;

        for (var y = 30.0; y < 400; y += 0.5)
        for (var x = 60.0; x < 300; x += 0.5)
        {
            var p = page.At(x, y, 3);

            // Only saturated ink counts: the checkerboard is nothing but saturated colour, and
            // skipping the achromatic keeps the caption's antialiased greys out of the box.
            var maxima = Math.Max(p.R, Math.Max(p.G, p.B));
            var minima = Math.Min(p.R, Math.Min(p.G, p.B));
            if (maxima - minima < 60) continue;

            any = true;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return any ? new Box(left, top, right, bottom) : null;
    }
}
