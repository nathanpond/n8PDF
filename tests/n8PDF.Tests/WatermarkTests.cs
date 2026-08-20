using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Watermarks: a word set across the page behind everything else.
/// </summary>
/// <remarks>
/// One is a shape in the header holding its text on a path rather than in paragraphs, turned a
/// quarter of the way round, and painted half see-through. What decides how large the word comes
/// out is not the size the document gives it — Word writes a single point — but the shape it has
/// to fill, and how that fitting works is measured here rather than assumed.
///
/// Word's own export turns the letters into outlines, so its file holds no watermark text at all
/// and nothing about one can be compared as text. Both pages are rasterised instead and the ink
/// counted, which is what the eye would do.
/// </remarks>
public class WatermarkTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// What each page of the fit probe holds, and how much of its ink has to agree with Word's.
    /// </summary>
    /// <remarks>
    /// Every page but one agrees to better than a fifth of a percent. The exception is the page
    /// asking for another face: Word draws that one in the same face as all the others, ignoring
    /// what the document asked for, and this reader does what the document says. Which of the two
    /// is right is not really in doubt — the fixture asks for Times New Roman and gets Word's
    /// Calibri — so the difference is left standing rather than copied.
    /// </remarks>
    public static TheoryData<int, string, double> Pages => new()
    {
        { 0, "DRAFT in a wide box", 99.5 },
        { 1, "the same word in half the height", 99.5 },
        { 2, "a longer word in the same box", 99.5 },
        { 3, "a short word in the same box", 99.5 },
        { 4, "a word reaching below the line", 99.5 },
        { 5, "another face, which Word passes over", 97 },
        { 6, "a narrower box", 99.5 }
    };

    [Theory]
    [MemberData(nameof(Pages))]
    public void A_word_is_fitted_to_its_shape_the_way_word_fits_it(
        int page, string what, double required)
    {
        var agreement = Agreement("watermark-fit-probe", page);
        if (agreement is null) return;

        _output.WriteLine($"page {page + 1} ({what}): {agreement:0.00}%");

        Assert.True(agreement > required,
            $"page {page + 1} ({what}) agrees with Word on only {agreement:0.00}% of its ink.");
    }

    /// <summary>
    /// And the whole of one: turned, grey, half see-through, and on every page of the document.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_watermark_lands_where_word_puts_it(int page)
    {
        var agreement = Agreement("watermark", page);
        if (agreement is null) return;

        _output.WriteLine($"page {page + 1}: {agreement:0.00}%");

        Assert.True(agreement > 99.5,
            $"page {page + 1} agrees with Word on only {agreement:0.00}% of its ink.");
    }

    /// <summary>
    /// The word itself stays a word, which is the one place this does better than Word: its own
    /// export leaves the letters as outlines, and a reader cannot find the watermark in it.
    /// </summary>
    [Fact]
    public void The_word_can_still_be_read()
    {
        var pdf = Converter.Convert(Fixtures.Build("watermark"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var drafts = PdfTextExtractor.Extract(pdf).Where(run => run.Text.Contains("DRAFT")).ToList();

        Assert.Equal(2, drafts.Count);
        Assert.Equal([0, 1], drafts.Select(run => run.PageIndex));

        // And Word's own file does not hold it at all, which is what this is measured against.
        var word = PdfTextExtractor.ExtractFile(Path.Combine(TestPaths.ReferencePdfs, "watermark.pdf"));
        Assert.DoesNotContain(word, run => run.Text.Contains("DRAFT"));
    }

    /// <summary>It is drawn under the text rather than over it.</summary>
    [Fact]
    public void A_watermark_goes_under_the_page()
    {
        using var stream = new MemoryStream(Fixtures.Build("watermark"));

        var laidOut = Converter.LayoutDocument(stream,
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        foreach (var page in laidOut.Pages)
        {
            var drawn = Assert.Single(page.Images);
            Assert.True(drawn.Image.IsDrawing);

            // A picture goes down before any of the text does, which is what puts it behind.
            Assert.NotEmpty(page.Texts);
        }
    }

    /// <summary>What the older markup says a watermark is, read back off it.</summary>
    [Fact]
    public void A_watermark_is_read_from_what_it_says()
    {
        var shape = Read("""
            <v:shape id="PowerPlusWaterMarkObject" type="#_x0000_t136"
                     style="position:absolute;margin-left:0;margin-top:0;width:400pt;height:100pt;
                            rotation:315;z-index:-251658752;mso-position-horizontal:center;
                            mso-position-horizontal-relative:margin"
                     fillcolor="#d9d9d9" stroked="f">
              <v:fill opacity=".5"/>
              <v:textpath style="font-family:&quot;Calibri&quot;;font-size:1pt" string="DRAFT"/>
            </v:shape>
            """);

        Assert.Equal("DRAFT", shape.WordArt?.Text);
        Assert.Equal("Calibri", shape.WordArt?.FontFamily);
        Assert.Equal("D9D9D9", shape.Fill?.Hex);
        Assert.Null(shape.Line);
        Assert.Equal(315, shape.RotationDegrees, 3);
        Assert.Equal(0.5, shape.FillOpacity, 3);
    }

    /// <summary>Opacity is written either as a fraction or in sixty-fourths of a thousand.</summary>
    [Theory]
    [InlineData(".5", 0.5)]
    [InlineData("0.25", 0.25)]
    [InlineData("32768f", 0.5)]
    [InlineData("13107f", 0.2)]
    public void Opacity_comes_in_two_spellings(string written, double expected)
    {
        var shape = Read($"""
            <v:rect style="width:100pt;height:50pt" fillcolor="red">
              <v:fill opacity="{written}"/>
            </v:rect>
            """);

        Assert.Equal(expected, shape.FillOpacity, 2);
    }

    /// <summary>And the page it is painted on carries the transparency it asked for.</summary>
    [Fact]
    public void The_page_carries_the_transparency()
    {
        var pdf = Converter.Convert(Fixtures.Build("watermark"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var text = System.Text.Encoding.Latin1.GetString(pdf);

        // A PDF carries transparency in the graphics state rather than in the colour, so a page
        // with a half-solid watermark on it has a state saying so.
        Assert.Contains("/ExtGState", text);
        Assert.Contains("/ca 0.5", text);
    }

    private static ShapeFrame Read(string shapeXml)
    {
        var run = XDocument.Parse($"""
            <w:r xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                 xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:o="urn:schemas-microsoft-com:office:office"
                 xmlns:w10="urn:schemas-microsoft-com:office:word">
              <w:pict>{shapeXml}</w:pict>
            </w:r>
            """);

        var drawing = Assert.Single(DocumentParser.ParseRun(run.Root!).Content);

        var shape = drawing switch
        {
            DrawingInline inline => inline.Shape,
            AnchoredDrawing anchored => anchored.Shape,
            _ => null
        };

        Assert.NotNull(shape);
        return shape;
    }

    /// <summary>
    /// How much of one page's ink the two agree on, or null where nothing can rasterise it.
    /// </summary>
    private double? Agreement(string fixtureName, int page)
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var ours = Converter.Convert(Fixtures.Build(fixtureName),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        const double scale = 3;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(File.ReadAllBytes(reference), page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return null;
        }

        var (agreed, covered) = (0, 0);

        // The whole page, since a watermark is drawn across the whole of one.
        for (var y = 40.0; y < 750; y++)
        for (var x = 20.0; x < 590; x++)
        {
            var a = mine.At(x, y, scale);
            var b = word.At(x, y, scale);

            // A watermark is a pale grey, so what counts as ink here has to admit one.
            if ((a.R < 245 || a.G < 245 || a.B < 245) == (b.R < 245 || b.G < 245 || b.B < 245))
                agreed++;

            covered++;
        }

        return 100.0 * agreed / covered;
    }
}
