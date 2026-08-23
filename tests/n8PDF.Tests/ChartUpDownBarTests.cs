using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests that a stock chart's up and down bars are painted in the colours the document states.
/// </summary>
/// <remarks>
/// This exists because a wrong-namespace lookup meant they were not, and nothing noticed. The
/// reason nothing noticed is worth keeping in front of whoever reads this next: the stock fixture
/// states white for a rising bar and black for a falling one, and those are exactly the colours
/// the composer fills in when the document says nothing. A reader that failed to read the
/// statement drew the same picture, so every existing comparison passed.
///
/// So the probe states green and red, which the fallback would never choose, and the two pages
/// exchange them. Counting each colour separately is what makes the pair meaningful: a reader that
/// ignored the document would draw white and black on both pages and fail both, and one that read
/// the colours but attached them to the wrong direction would pass the counts on each page taken
/// alone and fail the positions.
/// </remarks>
public class ChartUpDownBarTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static bool IsGreen((byte R, byte G, byte B) p) => p.G > 100 && p.R < 100 && p.B < 100;

    private static bool IsRed((byte R, byte G, byte B) p) => p.R > 100 && p.G < 90 && p.B < 90;

    [Theory]
    [InlineData(0, "rising green, falling red")]
    [InlineData(1, "the two exchanged")]
    public void The_bars_are_painted_in_the_colours_the_document_states(int page, string what)
    {
        const string fixtureName = "chart-updown-bar-probe";

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

        var (ourGreen, theirGreen, sharedGreen) = (0, 0, 0);
        var (ourRed, theirRed, sharedRed) = (0, 0, 0);

        for (var y = 74.0; y < 286; y++)
        for (var x = 74.0; x < 430; x++)
        {
            var a = mine.At(x, y, scale);
            var b = word.At(x, y, scale);

            if (IsGreen(a)) ourGreen++;
            if (IsGreen(b)) theirGreen++;
            if (IsGreen(a) && IsGreen(b)) sharedGreen++;

            if (IsRed(a)) ourRed++;
            if (IsRed(b)) theirRed++;
            if (IsRed(a) && IsRed(b)) sharedRed++;
        }

        _output.WriteLine(
            $"{what}: green {ourGreen} here / {theirGreen} in Word's / {sharedGreen} shared; " +
            $"red {ourRed} / {theirRed} / {sharedRed}");

        // Word painted both, or the reference is not of the document it claims to be.
        Assert.True(theirGreen > 50 && theirRed > 50,
            $"Word drew no coloured bars to compare against on {what}");

        // Each colour where Word has it. A reader that ignored the document draws white and black
        // and scores nought on both; one that swapped the two scores nought on the overlap while
        // keeping the counts, which is why the overlap is asserted and not only the count.
        Assert.True(sharedGreen >= 0.95 * theirGreen,
            $"{what}: the rising bars cover only {100.0 * sharedGreen / theirGreen:0.0}% of Word's");

        Assert.True(sharedRed >= 0.95 * theirRed,
            $"{what}: the falling bars cover only {100.0 * sharedRed / theirRed:0.0}% of Word's");

        Assert.InRange((double)ourGreen / theirGreen, 0.9, 1.1);
        Assert.InRange((double)ourRed / theirRed, 0.9, 1.1);
    }
}
