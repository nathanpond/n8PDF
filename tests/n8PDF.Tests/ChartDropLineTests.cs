using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the lines a chart hangs from its points: down to the axis, and across a category's spread.
/// </summary>
/// <remarks>
/// Red pixels, for the reason <see cref="ChartTrendlineInkTests"/> gives at length: a whole-plot
/// ink comparison was shown there to score *higher* with the thing under test removed altogether,
/// because a line a point wide is too little of a chart for it to notice. The probe paints both
/// kinds of line <c>C00000</c> and nothing else on its pages is red.
///
/// These are also what settle the questions the format leaves open — where a drop line stops when
/// the scale runs below nought, and which point it hangs from when a category holds more than one
/// series. Neither can be read; both move the line by tens of points on the page.
/// </remarks>
public class ChartDropLineTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static bool IsHanging((byte R, byte G, byte B) pixel) =>
        pixel.R > 100 && pixel.G < 90 && pixel.B < 90;

    [Theory]
    [InlineData(0, "drop lines, one series")]
    [InlineData(1, "drop lines, the scale below nought")]
    [InlineData(2, "drop lines, two series")]
    [InlineData(3, "high-low lines on a line chart")]
    [InlineData(4, "both together")]
    public void A_hanging_line_is_drawn_where_word_draws_it(int page, string what)
    {
        const string fixtureName = "chart-drop-line-probe";

        if (TestFonts.SkipForMissingFonts(fixtureName)) return;

        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(fixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var theirs = File.ReadAllBytes(reference);

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        bool Red(RenderedPage page, double x, double y) => IsHanging(page.At(x, y, scale));

        var (ourRed, theirRed, exact, near) = (0, 0, 0, 0);

        for (var y = 74.0; y < 286; y++)
        for (var x = 74.0; x < 430; x++)
        {
            if (Red(mine, x, y)) ourRed++;
            if (!Red(word, x, y)) continue;

            theirRed++;

            if (Red(mine, x, y))
            {
                exact++;
                near++;
                continue;
            }

            // Not on the pixel, but next to one of ours: the difference between a line drawn
            // somewhere else and a line whose edge the two rasterisers rounded differently.
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (!Red(mine, x + dx, y + dy)) continue;

                near++;
                goto next;
            }

            next: ;
        }

        _output.WriteLine(
            $"{what}: {ourRed} red here, {theirRed} in Word's; " +
            $"{100.0 * exact / theirRed:0.0}% on the pixel, {100.0 * near / theirRed:0.0}% within one");

        Assert.True(theirRed > 50, $"Word drew no hanging lines to compare against on {what}");
        Assert.True(ourRed > 50, $"{what} drew no hanging lines");

        // The claim that matters, and it is nearly exact: every pixel Word inks red, we ink or
        // touch. A line stopping at the plot's floor rather than the axis, or hanging from the
        // wrong point of a category, misses this by tens of percent rather than by ones.
        Assert.True(near >= 0.99 * theirRed,
            $"{what} leaves {100.0 * (theirRed - near) / theirRed:0.0}% of Word's lines untouched");

        // Pixel for pixel it is 96.5% at worst, the rest being edges the two rasterisers round
        // differently — measured, not allowed for in advance.
        Assert.True(exact >= 0.95 * theirRed,
            $"{what} covers only {100.0 * exact / theirRed:0.0}% of Word's lines on the pixel");

        Assert.InRange((double)ourRed / theirRed, 0.95, 1.15);
    }
}
