using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The border round a page: where its line falls, which pages carry one, and how far each side
/// runs.
/// </summary>
/// <remarks>
/// <c>page-border-probe</c> is four sections, so that one export answers all of it. What it shows,
/// and what this holds:
///
///   offset from the page   the space is to the outside of the line — 24 points in draws from 24
///   offset from the text   the space is to the inside of it — none at all puts the line on the margin
///   display                firstPage means the section's first page and no other
///   a missing side         the sides that meet it run on to the edge of the paper
///
/// The ink is compared rather than the rectangles: Word draws each side as a bar between the
/// corners and then fills the corners in, where this draws one bar corner to corner, and the two
/// cover exactly the same ground.
/// </remarks>
public class PageBorderTests(ITestOutputHelper output)
{
    private const double Scale = 3;

    /// <summary>Every page of the probe, ink for ink, in the margins where only the border is.</summary>
    [Theory]
    [InlineData(0, "from the page, 24 points in")]
    [InlineData(1, "the same section's second page")]
    [InlineData(2, "from the text, against it")]
    [InlineData(3, "three points thick, first page only")]
    [InlineData(4, "the page after it, which asked for none")]
    [InlineData(5, "a top and a left edge only")]
    public void The_border_covers_what_words_covers(int page, string what)
    {
        if (TestFonts.SkipForMissingFonts("page-border-probe")) return;

        var reference = File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "page-border-probe.pdf"));
        var ours = Converter.Convert(Fixtures.Build("page-border-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        if (PdfRasterizer.Render(ours, page, Scale) is not { } mine ||
            PdfRasterizer.Render(reference, page, Scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var apart = 0;

        for (var y = 0.0; y < 792; y += 1.0 / Scale)
        for (var x = 0.0; x < 612; x += 1.0 / Scale)
        {
            // The margins only: the text inside is compared line by line elsewhere, and a
            // baseline a grid step from Word's would count here as ink out of place.
            if (x > 72 && x < 540 && y > 72 && y < 720) continue;

            var (r1, g1, b1) = mine.At(x, y, Scale);
            var (r2, g2, b2) = word.At(x, y, Scale);

            if (r1 + g1 + b1 < 700 != (r2 + g2 + b2 < 700)) apart++;
        }

        output.WriteLine($"page {page + 1} ({what}): {apart} points apart");

        Assert.Equal(0, apart);
    }

    /// <summary>
    /// And the geometry outright, so that a change says which rule it broke: the border of the
    /// first section stands 24 points from the paper on every side, a point thick.
    /// </summary>
    [Fact]
    public void A_border_offset_from_the_page_stands_where_it_says()
    {
        if (TestFonts.SkipForMissingFonts("page-border-probe")) return;

        var rects = PdfPathExtractor.Extract(Converter.Convert(Fixtures.Build("page-border-probe"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }))
            .Where(r => r.PageIndex == 0)
            .ToList();

        Assert.Equal(4, rects.Count);

        // A point of line, on the grid Word draws a width on: 0.96 rather than 1.
        Assert.All(rects, r => Assert.Equal(0.96, Math.Min(r.Width, r.Height), 2));

        Assert.Equal(24, rects.Min(r => r.Left), 2);
        Assert.Equal(24, rects.Min(r => r.Top), 2);
        Assert.Equal(612 - 24, rects.Max(r => r.Right), 2);
        Assert.Equal(792 - 24, rects.Max(r => r.Bottom), 2);
    }
}
