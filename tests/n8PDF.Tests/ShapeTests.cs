using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Shapes, and the text boxes that are shapes with text in them.
/// </summary>
/// <remarks>
/// A text box is a document inside a document: its own paragraphs, laid out into a box that is
/// not the page's, drawn over a frame of its own. Three things have to be right for one to come
/// out — the frame, where the text sits inside it, and where the box sits on the page — and all
/// three are asked of Word here rather than of the specification.
/// </remarks>
public class ShapeTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Where a shape sets its text, against Word, on all five pages of the probe.
    /// </summary>
    /// <remarks>
    /// The pages vary the one thing each: Word's own insets against none, a fine outline against
    /// a thick one, and the three ways a box can hold its text in a height greater than it needs.
    /// A quarter of a point of tolerance is Word's own vertical quantum of 1/300 inch.
    /// </remarks>
    [Fact]
    public void A_shape_sets_its_text_where_word_sets_it()
    {
        var (ours, theirs) = BothWays("shape-inset-probe");

        var mine = Lines(ours);
        var word = Lines(theirs);

        Assert.Equal(word.Count, mine.Count);
        Assert.Equal(5, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine(
                $"page {i + 1}: {mine[i].Text} at ({mine[i].StartX:0.###}, {mine[i].BaselineY:0.###}), " +
                $"Word at ({word[i].StartX:0.###}, {word[i].BaselineY:0.###})");

            Assert.Equal(word[i].Text.Trim(), mine[i].Text.Trim());

            Assert.True(Math.Abs(mine[i].StartX - word[i].StartX) < 0.25,
                $"page {i + 1}: the text begins {mine[i].StartX - word[i].StartX:+0.###;-0.###}pt " +
                "from where Word begins it.");

            Assert.True(Math.Abs(mine[i].BaselineY - word[i].BaselineY) < 0.25,
                $"page {i + 1}: the text sits {mine[i].BaselineY - word[i].BaselineY:+0.###;-0.###}pt " +
                "from where Word sits it.");
        }
    }

    /// <summary>
    /// And where the shapes themselves are drawn, in the colours they are drawn in — including
    /// the one that names its colours by theme slot rather than outright.
    /// </summary>
    [Fact]
    public void A_shape_is_drawn_where_word_draws_it()
    {
        var (ours, theirs) = BothWays("shapes");

        var mine = PdfPathExtractor.Extract(ours);
        var word = PdfPathExtractor.Extract(theirs);

        // The rectangles: the two boxes, the plain shape, and the one in the theme's colours. The
        // rounded rectangle and the ellipse are curves, which this reader passes over.
        Assert.Equal(4, word.Count);
        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine($"{mine[i]} against Word's {word[i]}");

            Assert.Equal(word[i].PageIndex, mine[i].PageIndex);
            Assert.Equal(word[i].ColorHex, mine[i].ColorHex);

            Assert.True(Math.Abs(mine[i].Left - word[i].Left) < 0.25 &&
                        Math.Abs(mine[i].Top - word[i].Top) < 0.25 &&
                        Math.Abs(mine[i].Width - word[i].Width) < 0.25 &&
                        Math.Abs(mine[i].Height - word[i].Height) < 0.25,
                $"a shape is drawn at {mine[i]} where Word draws it at {word[i]}.");
        }
    }

    /// <summary>
    /// The shapes that are not rectangles, compared as ink on the page rather than as coordinates.
    /// </summary>
    /// <remarks>
    /// A rounded rectangle and an ellipse are curves, and a curve cannot be compared operator for
    /// operator: Word writes its own arcs and this writes Béziers of its own, and the two agree in
    /// what they draw rather than in how they say it. So both pages are rasterised and the ink is
    /// counted, which is what the eye would do.
    /// </remarks>
    [Fact]
    public void The_curved_shapes_cover_what_word_covers()
    {
        var (ours, theirs) = BothWays("shapes");

        const double scale = 3;
        const int page = 2;

        if (PdfRasterizer.Render(ours, page, scale) is not { } mine ||
            PdfRasterizer.Render(theirs, page, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (covered, agreed, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        // The corner of the page the shapes are drawn in, with room around them.
        for (var y = 80; y < 220; y++)
        for (var x = 66; x < 300; x++)
        {
            var a = mine.At(x, y, scale);
            var b = word.At(x, y, scale);

            var ink = a.R < 200 || a.G < 200 || a.B < 200;
            var theirInk = b.R < 200 || b.G < 200 || b.B < 200;

            if (ink) inkOfMine++;
            if (theirInk) inkOfTheirs++;
            if (ink == theirInk) agreed++;

            covered++;
        }

        var agreement = 100.0 * agreed / covered;

        _output.WriteLine(
            $"ink: {inkOfMine} here, {inkOfTheirs} in Word's; the two agree on {agreement:0.00}% of the page");

        Assert.True(agreement > 97, $"the two pages agree on only {agreement:0.0}% of the shapes");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    /// <summary>
    /// A shape written twice over, for readers of different ages, is drawn once — and it is the
    /// newer of the two that is read.
    /// </summary>
    /// <remarks>
    /// This is how every shape in a document Word saved arrives: wrapped in an
    /// <c>mc:AlternateContent</c> whose choice is the shape and whose fallback is the same thing
    /// in the older VML spelling. A reader that ignored the wrapper would find no drawing at all
    /// and drop the shape in silence, and one that read both branches would draw it twice.
    /// </remarks>
    [Fact]
    public void A_shape_offered_twice_is_read_once()
    {
        var run = XDocument.Parse("""
            <w:r xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                 xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                 xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                 xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                 xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"
                 xmlns:v="urn:schemas-microsoft-com:vml">
              <mc:AlternateContent>
                <mc:Choice Requires="wps">
                  <w:drawing>
                    <wp:inline>
                      <wp:extent cx="914400" cy="457200"/>
                      <a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
                        <wps:wsp>
                          <wps:spPr>
                            <a:prstGeom prst="roundRect"/>
                            <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                          </wps:spPr>
                          <wps:txbx><w:txbxContent>
                            <w:p><w:r><w:t>The newer one.</w:t></w:r></w:p>
                          </w:txbxContent></wps:txbx>
                        </wps:wsp>
                      </a:graphicData></a:graphic>
                    </wp:inline>
                  </w:drawing>
                </mc:Choice>
                <mc:Fallback>
                  <w:pict>
                    <v:shape><v:textbox><w:txbxContent>
                      <w:p><w:r><w:t>The older one.</w:t></w:r></w:p>
                    </w:txbxContent></v:textbox></v:shape>
                  </w:pict>
                </mc:Fallback>
              </mc:AlternateContent>
            </w:r>
            """);

        var shapes = DocumentParser.ParseRun(run.Root!).Content.OfType<DrawingInline>().ToList();

        var shape = Assert.Single(shapes).Shape;
        Assert.NotNull(shape);
        Assert.Equal("roundRect", shape.Geometry);
        Assert.Equal("FF0000", shape.Fill?.Hex);
        Assert.Equal("The newer one.", ((Paragraph)shape.Content[0]).GetText());
    }

    /// <summary>What a shape says about itself, and what it takes from Word when it says nothing.</summary>
    [Fact]
    public void A_shape_that_says_nothing_takes_words_own_insets()
    {
        var shape = Parse("""
            <wps:wsp>
              <wps:spPr><a:prstGeom prst="ellipse"/></wps:spPr>
              <wps:bodyPr/>
            </wps:wsp>
            """);

        // A tenth of an inch at the sides and half that above and below.
        Assert.Equal(7.2, shape.InsetLeftPoints, 3);
        Assert.Equal(7.2, shape.InsetRightPoints, 3);
        Assert.Equal(3.6, shape.InsetTopPoints, 3);
        Assert.Equal(3.6, shape.InsetBottomPoints, 3);
        Assert.Equal(ShapeTextAnchor.Top, shape.Anchor);

        // Saying nothing about a fill is not saying it has none, but both come out unpainted:
        // what it would otherwise take is the theme's format scheme, which is not read.
        Assert.Null(shape.Fill);
        Assert.Null(shape.Line);
    }

    [Fact]
    public void A_shape_reads_what_it_does_say()
    {
        var shape = Parse("""
            <wps:wsp>
              <wps:spPr>
                <a:prstGeom prst="triangle"/>
                <a:solidFill><a:schemeClr val="accent3"/></a:solidFill>
                <a:ln w="38100"><a:solidFill><a:srgbClr val="123456"/></a:solidFill></a:ln>
              </wps:spPr>
              <wps:bodyPr lIns="0" tIns="182880" anchor="b"/>
            </wps:wsp>
            """);

        Assert.Equal("triangle", shape.Geometry);
        Assert.Equal("accent3", shape.Fill?.ThemeSlot);
        Assert.Equal("123456", shape.Line?.Hex);
        Assert.Equal(3, shape.LineWidthPoints, 3);
        Assert.Equal(0, shape.InsetLeftPoints, 3);
        Assert.Equal(14.4, shape.InsetTopPoints, 3);
        Assert.Equal(ShapeTextAnchor.Bottom, shape.Anchor);
    }

    /// <summary>
    /// A colour named by theme slot is the colour the theme gives it, under either of its names.
    /// </summary>
    [Fact]
    public void The_theme_answers_for_the_colours_named_by_slot()
    {
        var theme = StylesParser.ParseTheme(XDocument.Parse("""
            <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:themeElements>
                <a:clrScheme name="Office">
                  <a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1>
                  <a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1>
                  <a:accent1><a:srgbClr val="4472C4"/></a:accent1>
                </a:clrScheme>
              </a:themeElements>
            </a:theme>
            """));

        Assert.Equal("4472C4", theme.ResolveColor("accent1"));

        // The four slots named for what they are for are the light and dark ones over again.
        Assert.Equal("000000", theme.ResolveColor("tx1"));
        Assert.Equal("FFFFFF", theme.ResolveColor("bg1"));
        Assert.Null(theme.ResolveColor("accent6"));
    }

    private static ShapeFrame Parse(string shapeXml)
    {
        var drawing = XDocument.Parse($"""
            <w:drawing xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                       xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                       xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                       xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape">
              <wp:inline>
                <wp:extent cx="914400" cy="457200"/>
                <a:graphic><a:graphicData>{shapeXml}</a:graphicData></a:graphic>
              </wp:inline>
            </w:drawing>
            """);

        var run = new XElement(W.Main + "r", drawing.Root);
        var inline = DocumentParser.ParseRun(run).Content.OfType<DrawingInline>().Single();

        Assert.NotNull(inline.Shape);
        return inline.Shape!;
    }

    private static List<TextLine> Lines(byte[] pdf) =>
        PdfLineComparison.GroupIntoLines(PdfTextExtractor.Extract(pdf));

    private static (byte[] Ours, byte[] Theirs) BothWays(string fixtureName)
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        return (Converter.Convert(Fixtures.Build(fixtureName),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));
    }
}
