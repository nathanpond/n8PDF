using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Shapes in the older spelling: the <c>w:pict</c> Word wrote before 2007.
/// </summary>
/// <remarks>
/// It says in one attribute what the newer spelling says in a dozen elements — the size, the
/// position and what the shape is anchored to are all CSS in a string — and it names its geometry
/// by the element rather than by an attribute. What it produces is the same shape the newer
/// spelling does, so what these tests watch is the reading of it; the drawing and the laying out
/// underneath are the same code, and <see cref="ShapeTests"/> is what holds those to Word.
/// </remarks>
public class VmlShapeTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>Every shape of the older kind, drawn where Word draws it and in its colours.</summary>
    [Theory]
    [InlineData("vml-shapes")]
    [InlineData("vml-stroke-probe")]
    public void An_old_shape_is_drawn_where_word_draws_it(string name)
    {
        var (ours, theirs) = BothWays(name);

        var mine = PdfPathExtractor.Extract(ours);
        var word = PdfPathExtractor.Extract(theirs);

        Assert.NotEmpty(word);
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
    /// Where an old-style shape is drawn is not quite where its own size puts it, and how far off
    /// depends on how thick its outline is.
    /// </summary>
    /// <remarks>
    /// This is the measurement itself, written out: ten pages, the same rectangle on each, varying
    /// nothing but the weight of its outline. The offset steps in twos rather than growing with
    /// the weight, and it starts a whole point in — so everything an ordinary document draws, at a
    /// point or less, is not offset at all.
    /// </remarks>
    [Fact]
    public void A_thick_outline_moves_an_old_shape()
    {
        var (ours, _) = BothWays("vml-stroke-probe");

        var drawn = PdfPathExtractor.Extract(ours);

        // Ten pages of rectangles, then two pages whose shapes are curves and so are not
        // rectangles to this reader, then a page holding two rectangles at once.
        Assert.Equal(12, drawn.Count);

        // none, ¼, ½, ¾, 1, 1½, 2, 3, 4½, 6 points of outline.
        double[] expected = [0, 0, 0, 0, 0, 2, 2, 2, 4, 6];

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(72 + expected[i], drawn[i].Left, 2);
            Assert.Equal(72 + expected[i], drawn[i].Top, 2);
        }

        // And two shapes on one line, neither offset, the second beginning exactly where the
        // first ends: the offset belongs to the shape rather than to the line it is on.
        Assert.Equal(72, drawn[10].Left, 2);
        Assert.Equal(180, drawn[11].Left, 2);
        Assert.Equal(72, drawn[11].Top, 2);
    }

    /// <summary>
    /// And where it sets its text: against Word, on all three pages of the probe — the insets it
    /// gets when it declares none, none at all, and none against a six point outline.
    /// </summary>
    [Fact]
    public void An_old_box_sets_its_text_where_word_sets_it()
    {
        var (ours, theirs) = BothWays("vml-inset-probe");

        var mine = PdfLineComparison.GroupIntoLines(PdfTextExtractor.Extract(ours));
        var word = PdfLineComparison.GroupIntoLines(PdfTextExtractor.Extract(theirs));

        Assert.Equal(3, word.Count);
        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine(
                $"page {i + 1}: {mine[i].Text.Trim()} at ({mine[i].StartX:0.###}, {mine[i].BaselineY:0.###}), " +
                $"Word at ({word[i].StartX:0.###}, {word[i].BaselineY:0.###})");

            Assert.True(Math.Abs(mine[i].StartX - word[i].StartX) < 0.25 &&
                        Math.Abs(mine[i].BaselineY - word[i].BaselineY) < 0.25,
                $"page {i + 1}: the text is at ({mine[i].StartX:0.###}, {mine[i].BaselineY:0.###}) " +
                $"where Word puts it at ({word[i].StartX:0.###}, {word[i].BaselineY:0.###}).");
        }
    }

    /// <summary>The curved ones, as ink rather than as coordinates.</summary>
    [Fact]
    public void The_curved_old_shapes_cover_what_word_covers()
    {
        var (ours, theirs) = BothWays("vml-shapes");

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

        for (var y = 80; y < 200; y++)
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

        // Lower than the newer spelling's shapes manage, and for a reason worth stating: on this
        // page a rectangle with a two point outline shares its line with a rounded one, and Word
        // nudges the rounded one — and the line under it — about a point from where a shape alone
        // on its line goes. Alone, every weight and every geometry lands exactly where this puts
        // it, which is what the stroke probe asserts. Together, something happens that has not
        // been explained, and this is how much of the page it costs.
        Assert.True(agreement > 95, $"the two pages agree on only {agreement:0.0}% of the shapes");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    /// <summary>A shape that says nothing is filled white, outlined black, and sits in the line.</summary>
    [Fact]
    public void An_old_shape_takes_the_formats_own_defaults()
    {
        var drawing = Assert.IsType<DrawingInline>(
            Parse("""<v:rect style="width:108pt;height:54pt"/>"""));

        var shape = drawing.Shape;
        Assert.NotNull(shape);

        Assert.Equal("rect", shape.Geometry);
        Assert.Equal("FFFFFF", shape.Fill?.Hex);
        Assert.Equal("000000", shape.Line?.Hex);
        Assert.Equal(0.75, shape.LineWidthPoints, 3);
        Assert.Equal(108, Units.EmuToPoints(drawing.WidthEmu), 3);
        Assert.Equal(54, Units.EmuToPoints(drawing.HeightEmu), 3);
    }

    /// <summary>And one that says it is filled and stroked by nothing is drawn as neither.</summary>
    [Fact]
    public void An_old_shape_can_refuse_both()
    {
        var shape = Assert.IsType<DrawingInline>(
            Parse("""<v:oval style="width:36pt;height:36pt" filled="f" stroked="f"/>""")).Shape;

        Assert.NotNull(shape);
        Assert.Equal("ellipse", shape.Geometry);
        Assert.Null(shape.Fill);
        Assert.Null(shape.Line);
    }

    /// <summary>
    /// A shape positioned absolutely floats, and the text goes round it where it says so.
    /// </summary>
    [Fact]
    public void An_absolutely_positioned_shape_floats()
    {
        var anchored = Assert.IsType<AnchoredDrawing>(Parse("""
            <v:shape type="#_x0000_t202"
                     style="position:absolute;margin-left:36pt;margin-top:18pt;width:144pt;height:72pt;
                            mso-position-horizontal-relative:page;mso-position-vertical-relative:margin">
              <w10:wrap type="topAndBottom"/>
            </v:shape>
            """));

        Assert.Equal(TextWrapMode.TopAndBottom, anchored.Wrap);
        Assert.Equal(HorizontalAnchor.Page, anchored.HorizontalFrom);
        Assert.Equal(VerticalAnchor.Margin, anchored.VerticalFrom);
        Assert.Equal(36, Units.EmuToPoints(anchored.HorizontalOffsetEmu ?? 0), 3);
        Assert.Equal(18, Units.EmuToPoints(anchored.VerticalOffsetEmu ?? 0), 3);

        // An eighth of an inch of clearance at the sides where the shape asks for none, which is
        // what Word leaves beside the box in the vml-shapes fixture.
        Assert.Equal(9, Units.EmuToPoints(anchored.DistanceLeftEmu), 3);
        Assert.Equal(0, Units.EmuToPoints(anchored.DistanceTopEmu), 3);
    }

    /// <summary>A shape that declares no wrapping does not part the text at all.</summary>
    [Fact]
    public void A_floating_shape_with_no_wrap_element_sits_over_the_text()
    {
        var anchored = Assert.IsType<AnchoredDrawing>(
            Parse("""<v:rect style="position:absolute;width:72pt;height:72pt;z-index:-251658240"/>"""));

        Assert.Equal(TextWrapMode.None, anchored.Wrap);

        // A negative z-index is what puts a shape behind the text rather than over it.
        Assert.True(anchored.BehindText);
    }

    /// <summary>What a text box holds, how far in it holds it, and where in the height.</summary>
    [Fact]
    public void An_old_text_box_reads_its_content_and_its_insets()
    {
        var shape = Assert.IsType<DrawingInline>(Parse("""
            <v:shape type="#_x0000_t202" style="width:216pt;height:72pt">
              <v:textbox inset="0,2mm,1in," style="mso-fit-shape-to-text:t;v-text-anchor:middle">
                <w:txbxContent><w:p><w:r><w:t>Inside the box.</w:t></w:r></w:p></w:txbxContent>
              </v:textbox>
            </v:shape>
            """)).Shape;

        Assert.NotNull(shape);
        Assert.Equal("Inside the box.", ((Paragraph)shape.Content[0]).GetText());

        Assert.Equal(0, shape.InsetLeftPoints, 3);
        Assert.Equal(2 * 72 / 25.4, shape.InsetTopPoints, 3);
        Assert.Equal(72, shape.InsetRightPoints, 3);

        // The one left empty keeps the default rather than becoming nothing.
        Assert.Equal(3.6, shape.InsetBottomPoints, 3);

        Assert.Equal(ShapeTextAnchor.Center, shape.Anchor);
    }

    /// <summary>Colours may be named rather than numbered, and lengths carry their unit.</summary>
    [Theory]
    [InlineData("width:1in;height:1in", 72)]
    [InlineData("width:2.54cm;height:1in", 72)]
    [InlineData("width:6pc;height:1in", 72)]
    [InlineData("width:96px;height:1in", 72)]
    [InlineData("width:96;height:1in", 72)]
    public void A_length_carries_its_unit(string style, double expected)
    {
        var drawing = Assert.IsType<DrawingInline>(Parse($"<v:rect style=\"{style}\"/>"));

        Assert.Equal(expected, Units.EmuToPoints(drawing.WidthEmu), 2);
    }

    [Theory]
    [InlineData("red", "FF0000")]
    [InlineData("white", "FFFFFF")]
    [InlineData("#0f0", "00FF00")]
    [InlineData("#123456", "123456")]
    [InlineData("#4472c4 [3204]", "4472C4")]
    public void A_colour_may_be_named_or_numbered(string written, string expected)
    {
        var shape = Assert.IsType<DrawingInline>(
            Parse($"<v:rect style=\"width:1in;height:1in\" fillcolor=\"{written}\"/>")).Shape;

        Assert.Equal(expected, shape?.Fill?.Hex);
    }

    /// <summary>
    /// A shape in the older spelling reaches the page when it is all a document offers, which is
    /// what the fallback of a compatibility element is.
    /// </summary>
    [Fact]
    public void The_fallback_is_read_when_there_is_no_choice_to_take()
    {
        var run = XDocument.Parse("""
            <w:r xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                 xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                 xmlns:v="urn:schemas-microsoft-com:vml">
              <mc:AlternateContent>
                <mc:Choice Requires="something-else-entirely">
                  <w:drawing/>
                </mc:Choice>
                <mc:Fallback>
                  <w:pict>
                    <v:rect style="width:72pt;height:36pt" fillcolor="red">
                      <v:textbox><w:txbxContent>
                        <w:p><w:r><w:t>The older one.</w:t></w:r></w:p>
                      </w:txbxContent></v:textbox>
                    </v:rect>
                  </w:pict>
                </mc:Fallback>
              </mc:AlternateContent>
            </w:r>
            """);

        var shape = Assert.IsType<DrawingInline>(
            Assert.Single(DocumentParser.ParseRun(run.Root!).Content)).Shape;

        Assert.NotNull(shape);
        Assert.Equal("FF0000", shape.Fill?.Hex);
        Assert.Equal("The older one.", ((Paragraph)shape.Content[0]).GetText());
    }

    /// <summary>Reads one shape, given the markup that goes inside a <c>w:pict</c>.</summary>
    private static InlineElement Parse(string shapeXml)
    {
        var run = XDocument.Parse($"""
            <w:r xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                 xmlns:v="urn:schemas-microsoft-com:vml"
                 xmlns:w10="urn:schemas-microsoft-com:office:word">
              <w:pict>{shapeXml}</w:pict>
            </w:r>
            """);

        return Assert.Single(DocumentParser.ParseRun(run.Root!).Content);
    }

    private static (byte[] Ours, byte[] Theirs) BothWays(string fixtureName)
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, fixtureName + ".pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        return (Converter.Convert(Fixtures.Build(fixtureName),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));
    }
}
