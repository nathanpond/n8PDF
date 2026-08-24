using n8PDF.Images;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// EMF clipping regions (#69): ink drawn under a clip stays inside it, exactly where Word keeps it.
/// </summary>
/// <remarks>
/// The probe draws three colours under three kinds of clip — a red ellipse under an intersect
/// rectangle, a blue bar under an exclude, green under a region of two rectangles — and the test
/// reads the ink of our page and of Word's export the same way: each colour's bounding box is the
/// clip's, not the shape's, and the cut-out places hold no ink. A clip that fails paints where
/// the document said not to, which is worse than something missing.
/// </remarks>
public class EmfClipTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static bool Red((byte R, byte G, byte B) p) => p.R > 150 && p.G < 100 && p.B < 100;

    private static bool Blue((byte R, byte G, byte B) p) => p.B > 150 && p.R < 100 && p.G < 110;

    private static bool Green((byte R, byte G, byte B) p) => p.G > 120 && p.R < 100 && p.B < 110;

    private sealed record Box(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;

        public double Height => Bottom - Top;
    }

    /// <summary>The bounding box of a colour's ink, in page points.</summary>
    private static Box? Find(RenderedPage page, Func<(byte R, byte G, byte B), bool> colour)
    {
        const double scale = 3;
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        var any = false;

        for (var y = 60.0; y < 280; y += 1 / scale)
        for (var x = 60.0; x < 320; x += 1 / scale)
        {
            if (!colour(page.At(x, y, scale))) continue;

            any = true;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }

        return any ? new Box(left, top, right, bottom) : null;
    }

    private void AssertClipped(byte[] pdf, string who)
    {
        Assert.True(PdfRasterizer.Render(pdf, 0, 3) is { } page || !PdfRasterizer.IsRequired,
            PdfRasterizer.UnavailableMessage);
        if (PdfRasterizer.Render(pdf, 0, 3) is not { } rendered) return;

        var red = Find(rendered, Red);
        var blue = Find(rendered, Blue);
        var green = Find(rendered, Green);

        Assert.True(red is not null && blue is not null && green is not null,
            $"{who}: a colour is missing entirely — red {red}, blue {blue}, green {green}");

        _output.WriteLine($"{who}: red {red!.Width:0.0}x{red.Height:0.0} " +
                          $"blue {blue!.Width:0.0}x{blue.Height:0.0} green {green!.Width:0.0}x{green.Height:0.0}");

        // The red ellipse is 180x70 of shape; the intersect window is 80x40, and the ink is the
        // window, not the shape.
        Assert.InRange(red.Width, 76, 84);
        Assert.InRange(red.Height, 36, 44);

        // The blue bar keeps its full 160x26 — the exclude cuts a hole, not the edge —
        // and the hole (50 wide, centred at 105,87 in drawing space) holds no blue.
        Assert.InRange(blue.Width, 156, 164);
        var holeX = blue.Left + 105 - 20;
        var holeY = blue.Top + 87 - 74;
        Assert.False(Blue(rendered.At(holeX, holeY, 3)),
            $"{who}: blue ink inside the excluded rectangle at ({holeX:0.0}, {holeY:0.0})");
        Assert.True(Blue(rendered.At(blue.Left + 30, holeY, 3)),
            $"{who}: no blue beside the hole, so the bar itself is wrong");

        // Green spans its two islands and nothing between them.
        Assert.InRange(green.Width, 156, 164);
        var betweenX = green.Left + 100 - 20;
        var greenY = green.Top + 7;
        Assert.False(Green(rendered.At(betweenX, greenY, 3)),
            $"{who}: green ink between the region's two rectangles");
        Assert.True(Green(rendered.At(green.Left + 20, greenY, 3)) &&
                    Green(rendered.At(green.Right - 20, greenY, 3)),
            $"{who}: an island is missing its green");
    }

    /// <summary>Our page keeps every colour inside its clip.</summary>
    [Fact]
    public void The_ink_stays_inside_the_clips()
    {
        var pdf = n8PDF.Converter.Convert(Fixtures.Build("images-metafile-clip"),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        AssertClipped(pdf, "ours");
    }

    /// <summary>
    /// Word's export agrees where its player works, and where it does not the divergence is
    /// pinned — measured, not assumed.
    /// </summary>
    /// <remarks>
    /// Word for Mac's own metafile player was measured with one clip per file: the intersect is
    /// honoured to the point (its red window sits exactly where ours does), and SAVEDC/RESTOREDC
    /// restore correctly — but an EXCLUDECLIPRECT empties the clip, so everything drawn under it
    /// vanishes, and a region of several rectangles clips to its <b>last</b> rectangle alone.
    /// Those are degradations of the player, not meanings of the records: a file from Windows
    /// that excludes a hole means "everything but the hole", and drawing nothing loses content.
    /// So ours keeps the semantic clip — the precedent the watermark set: diverge where Word's
    /// export is the lesser rendering, and pin the divergence. If this test ever fails with
    /// Word's blue present or both islands green, its player has been fixed — promote the fixture
    /// into full agreement then.
    /// </remarks>
    [Fact]
    public void Word_honours_the_intersect_and_degrades_the_rest_as_measured()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "images-metafile-clip.pdf");
        if (!File.Exists(path)) return; // reported by Fixture_has_a_reference_pdf

        if (PdfRasterizer.Render(File.ReadAllBytes(path), 0, 3) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        var pdf = n8PDF.Converter.Convert(Fixtures.Build("images-metafile-clip"),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        if (PdfRasterizer.Render(pdf, 0, 3) is not { } ours) return;

        var wordRed = Find(word, Red);
        var ourRed = Find(ours, Red);

        Assert.True(wordRed is not null && ourRed is not null, "the intersect window lost its red");
        _output.WriteLine($"red: ours ({ourRed!.Left:0.0},{ourRed.Top:0.0}) {ourRed.Width:0.0}x{ourRed.Height:0.0}, " +
                          $"word ({wordRed!.Left:0.0},{wordRed.Top:0.0}) {wordRed.Width:0.0}x{wordRed.Height:0.0}");

        Assert.True(Math.Abs(ourRed.Left - wordRed.Left) < 1.5 && Math.Abs(ourRed.Top - wordRed.Top) < 1.5 &&
                    Math.Abs(ourRed.Width - wordRed.Width) < 2 && Math.Abs(ourRed.Height - wordRed.Height) < 2,
            "Word's intersect window no longer sits where ours does");

        // The measured degradations, held so a fixed player announces itself.
        Assert.Null(Find(word, Blue));

        var wordGreen = Find(word, Green);
        Assert.True(wordGreen is not null && wordGreen.Width < 60,
            "Word drew more than the last rectangle of the region — its player has been fixed, " +
            "so promote this fixture into full agreement");
    }

    /// <summary>The decoder itself: each operation carries the clips in force when it drew.</summary>
    [Fact]
    public void The_operations_carry_their_clips()
    {
        var writer = new EmfWriter(100, 100);
        var brush = writer.CreateBrush(200, 0, 0);

        writer.SaveDc().IntersectClipRect(10, 10, 60, 60);
        writer.Select(brush).Rectangle(0, 0, 100, 100);
        writer.RestoreDc();
        writer.Rectangle(0, 0, 50, 50);

        var drawing = ImageReader.Read(writer.Build()).Drawing!;
        var paths = drawing.Operations.OfType<PathOperation>().ToList();

        Assert.Equal(2, paths.Count);
        Assert.NotNull(paths[0].Clips);
        Assert.Single(paths[0].Clips!);
        Assert.False(paths[0].Clips![0].EvenOdd);
        Assert.Null(paths[1].Clips);

        // Put back wrong — an exclude taken as an intersect — the rule flips.
        var exclude = new EmfWriter(100, 100);
        var red = exclude.CreateBrush(200, 0, 0);
        exclude.ExcludeClipRect(10, 10, 60, 60);
        exclude.Select(red).Rectangle(0, 0, 100, 100);

        var excluded = ImageReader.Read(exclude.Build()).Drawing!
            .Operations.OfType<PathOperation>().Single();

        Assert.True(excluded.Clips![0].EvenOdd, "an exclude must cut, not keep");
        Assert.Equal(10, excluded.Clips[0].Steps.Count);
    }
}
