using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Autofit, measured before it was implemented (#64): a box told to size itself to its text
/// grows at render; one told to shrink its text draws full size where the text fits.
/// </summary>
/// <remarks>
/// The stored <c>fontScale</c> is Word's cache of its own last computation, not an instruction:
/// the probe's second box stores 50% and Word draws its text full size, because it fits. Where
/// the text does not fit, the stored value is applied — in a Word-authored document it is the
/// value Word itself computed, so the two agree there by construction; the overflow page is held
/// by our own extractor rather than by Word's ink, since a hand-authored mismatch between text
/// and stored scale is exactly the case Word recomputes.
/// </remarks>
public class ShapeAutofitTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] Ours() =>
        n8PDF.Converter.Convert(Fixtures.Build("shape-autofit-probe"),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    private sealed record Box(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;

        public double Height => Bottom - Top;
    }

    private static Box? Find(RenderedPage page, Func<(byte R, byte G, byte B), bool> colour)
    {
        double left = double.MaxValue, top = double.MaxValue, right = 0, bottom = 0;
        var any = false;

        for (var y = 30.0; y < 400; y += 1 / 3.0)
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

    /// <summary>The 30pt extent grows to its five lines of text, in ours and in Word's export.</summary>
    [Fact]
    public void A_box_grows_to_its_text()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "shape-autofit-probe.pdf");

        if (PdfRasterizer.Render(Ours(), 0, 3) is not { } ours ||
            !File.Exists(path) || PdfRasterizer.Render(File.ReadAllBytes(path), 0, 3) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        static bool PaleYellow((byte R, byte G, byte B) p) => p.R > 245 && p.G is > 230 and < 250 && p.B is > 180 and < 220;

        var mine = Find(ours, PaleYellow);
        var theirs = Find(word, PaleYellow);

        Assert.True(mine is not null && theirs is not null, "the growing box is missing");
        _output.WriteLine($"ours {mine!.Height:0.0}pt tall, word {theirs!.Height:0.0}pt");

        Assert.True(mine.Height > 60, $"the box stayed at its stated extent: {mine.Height:0.0}pt");
        Assert.True(Math.Abs(mine.Height - theirs.Height) < 4 && Math.Abs(mine.Top - theirs.Top) < 2,
            $"ours is {mine.Height:0.0}pt at {mine.Top:0.0}, Word's {theirs.Height:0.0}pt at {theirs.Top:0.0}");
    }

    /// <summary>Where the text fits, the stored half-size scale is ignored — full size, as Word draws it.</summary>
    [Fact]
    public void The_stored_scale_is_ignored_where_the_text_fits()
    {
        var runs = PdfTextExtractor.Extract(Ours());
        var line = runs.First(r => r.PageIndex == 1 && r.Text.StartsWith("Shrunk", StringComparison.Ordinal));

        _output.WriteLine($"drawn at {line.FontSize:0.0}pt");
        Assert.Equal(24, line.FontSize, 1);
    }

    /// <summary>
    /// And applied where it does not: an overflow box draws its 24pt text at the stored 12.
    /// </summary>
    /// <remarks>
    /// Not a fixture page: Word recomputes its own scale for a hand-authored mismatch between
    /// text and stored value, so there is no Word page to line this against — in a Word-authored
    /// document the stored value is Word's own computation, and the two agree by construction.
    /// </remarks>
    [Fact]
    public void The_stored_scale_applies_where_the_text_overflows()
    {
        var docx = new DocxBuilder()
            .AddRawParagraph(Fixtures.ShapeAnchor(931, 150, 40,
                "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
                "<a:solidFill><a:srgbClr val=\"E2EFDA\"/></a:solidFill>",
                offsetXPoints: 0, offsetYPoints: 20,
                txbx: "<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Times New Roman\" " +
                      "w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"48\"/></w:rPr>" +
                      "<w:t>Too many words to stand at full size in forty points of box.</w:t></w:r></w:p>",
                bodyPr: "<a:normAutofit fontScale=\"50000\" lnSpcReduction=\"20000\"/>"))
            .Build();

        var pdf = n8PDF.Converter.Convert(docx,
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var line = PdfTextExtractor.Extract(pdf)
            .First(r => r.Text.StartsWith("Too many", StringComparison.Ordinal));

        _output.WriteLine($"drawn at {line.FontSize:0.0}pt");
        Assert.Equal(12, line.FontSize, 1);
    }
}
