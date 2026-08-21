using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Packaging;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Diagrams — SmartArt — and the arrangement a document keeps of one.
/// </summary>
/// <remarks>
/// A diagram is written down twice: as what it means, and as the arrangement it last came to.
/// Word rebuilds the second from the first every time it opens a document, running a layout
/// language a hundred layouts are written in; every other reader draws the cached arrangement, and
/// so does this.
///
/// That is why the fixture here is a real document rather than a hand-written one. A cache written
/// by hand says nothing about Word, since Word will throw it away and lay the diagram out again —
/// which is what the synthetic document below demonstrates, and why it is only ever used to check
/// the reading. The one cache worth holding to Word's drawing is the one Word itself wrote.
/// </remarks>
public class DiagramTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>Every shape of the cached arrangement is read, with its place and its words.</summary>
    [Fact]
    public void A_cached_arrangement_is_read_shape_by_shape()
    {
        var shapes = Diagram.Parse(XDocument.Parse(DocxBuilder.SmartArtCachedDrawing(
            DocxBuilder.SmartArtShape("One", 0, 0, 144, 54),
            DocxBuilder.SmartArtShape("Two", 108, 63, 144, 54, geometry: "ellipse", fillHex: "ED7D31"))));

        Assert.Equal(2, shapes.Count);

        Assert.Equal("roundRect", shapes[0].Shape.Geometry);
        Assert.Equal(0, shapes[0].X, 3);
        Assert.Equal(144, shapes[0].Width, 3);
        Assert.Equal("4472C4", shapes[0].Shape.Fill?.Hex);
        Assert.Equal("One", ((Paragraph)shapes[0].Shape.Content[0]).GetText());

        Assert.Equal("ellipse", shapes[1].Shape.Geometry);
        Assert.Equal(108, shapes[1].X, 3);
        Assert.Equal(63, shapes[1].Y, 3);
        Assert.Equal("ED7D31", shapes[1].Shape.Fill?.Hex);
    }

    /// <summary>
    /// The rectangle the words go in is the one the diagram set aside for them, less what the
    /// body insets from it.
    /// </summary>
    [Fact]
    public void The_words_go_where_the_arrangement_says()
    {
        var shape = Assert.Single(Diagram.Parse(XDocument.Parse(DocxBuilder.SmartArtCachedDrawing(
            DocxBuilder.SmartArtShape("One", 36, 18, 144, 54, textInsetPoints: 9)))));

        // The shape says its text goes nine points inside it; the body's own insets are the ones
        // DrawingML gives everything, a tenth of an inch at the sides and half of that above.
        Assert.Equal(36 + 9 + 7.2, shape.TextX, 3);
        Assert.Equal(18 + 9 + 3.6, shape.TextY, 3);
        Assert.Equal(144 - 18 - 14.4, shape.TextWidth, 3);
        Assert.Equal(54 - 18 - 7.2, shape.TextHeight, 3);
    }

    /// <summary>
    /// A diagram's text is DrawingML rather than WordprocessingML, and says the same things in
    /// different words: hundredths of a point rather than halves, attributes rather than elements.
    /// </summary>
    [Fact]
    public void Text_written_in_drawingml_reads_as_a_paragraph()
    {
        var body = XElement.Parse("""
            <a:txBody xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:bodyPr lIns="0" tIns="0"/>
              <a:p>
                <a:pPr algn="ctr">
                  <a:lnSpc><a:spcPct val="90000"/></a:lnSpc>
                  <a:spcAft><a:spcPct val="35000"/></a:spcAft>
                </a:pPr>
                <a:r>
                  <a:rPr sz="1900" b="1">
                    <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                    <a:latin typeface="Georgia"/>
                  </a:rPr>
                  <a:t>Written down</a:t>
                </a:r>
              </a:p>
              <a:p><a:r><a:rPr lang="en-GB" sz="3800"/><a:t>And again</a:t></a:r></a:p>
            </a:txBody>
            """);

        // Two paragraphs, because the space after the last of them is not kept: see
        // A_diagram_keeps_no_space_after_its_last_paragraph.
        var blocks = DrawingText.Parse(body);
        Assert.Equal(2, blocks.Count);

        var paragraph = Assert.IsType<Paragraph>(blocks[0]);

        Assert.Equal("Written down", paragraph.GetText());
        Assert.Equal(Justification.Center, paragraph.Properties.Justification);

        // 90% of a line, counted in 240ths.
        Assert.Equal(216, paragraph.Properties.Line);

        // And 35% of one, where DrawingML's line is six fifths of the type size: 19pt of type
        // makes a 22.8pt line, and a third of that is 7.98pt, which is 160 twips.
        Assert.Equal(160, paragraph.Properties.SpacingAfterTwips);
        Assert.Equal(0, ((Paragraph)blocks[1]).Properties.SpacingAfterTwips);

        var run = Assert.Single(paragraph.Runs);
        Assert.Equal(38, run.Properties.SizeHalfPoints);
        Assert.True(run.Properties.Bold);
        Assert.Equal("FF0000", run.Properties.Color);
        Assert.Equal("Georgia", run.Properties.AsciiFont);
    }

    /// <summary>A colour may be named by theme slot, and the theme answers for it.</summary>
    [Fact]
    public void Text_may_take_its_colour_from_the_theme()
    {
        var body = XElement.Parse("""
            <a:txBody xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:p><a:r>
                <a:rPr><a:solidFill><a:schemeClr val="lt1"/></a:solidFill></a:rPr>
                <a:t>Pale</a:t>
              </a:r></a:p>
            </a:txBody>
            """);

        var run = ((Paragraph)DrawingText.Parse(body)[0]).Runs[0];
        Assert.Equal("lt1", run.Properties.ColorThemeSlot);

        var theme = new DocumentTheme();
        theme.Colors["lt1"] = "FFFFFF";

        Assert.Equal("FFFFFF", new Styling.StyleResolver(new StyleDefinitions(), theme)
            .ResolveRun(null, run.Properties).ColorHex);
    }

    /// <summary>
    /// A word too wide for the box it is in comes apart between its letters, which nothing on a
    /// page does.
    /// </summary>
    /// <remarks>
    /// Word's own drawing of the fixture sets "Three" across two lines as "Thre" and "e", the box
    /// being 67.84pt wide and the word wider. A page would let such a word overrun the margin
    /// whole; a shape holds its text.
    /// </remarks>
    [Fact]
    public void A_word_too_wide_for_its_box_is_broken()
    {
        if (TestFonts.SkipForMissingFonts("smartart")) return;

        var lines = Lines(Word("smartart"));

        Assert.Contains(lines, line => line.Text.Trim() == "Thre");
        Assert.Contains(lines, line => line.Text.Trim() == "e");

        var ours = Lines(Ours());
        Assert.Contains(ours, line => line.Text.Trim() == "Thre");
        Assert.Contains(ours, line => line.Text.Trim() == "e");
    }

    /// <summary>
    /// The whole diagram against Word: the same lines, in the same places across the page, and
    /// the same distance apart down it.
    /// </summary>
    /// <remarks>
    /// Where each box's text sits as a whole is the one thing that differs, by a constant 3.1pt.
    /// See the note on it in <c>TextPositionComparisonTests.KnownRealDivergences</c>: the two
    /// readings that would explain it cannot be told apart from a document whose diagram Word
    /// arranged itself.
    /// </remarks>
    /// <summary>
    /// Where the first line of a diagram's box sits, against Word, with the text anchored to the
    /// top of its box so that the answer depends on nothing else.
    /// </summary>
    /// <remarks>
    /// Centred — which is how a diagram normally sets its text, and how the smartart fixture has
    /// it — the height of the block and the place of the first baseline inside it are added
    /// together and no measurement can separate them. Against the top they come apart: the first
    /// baseline is the text frame plus one ascent, and that ascent is the line at nine tenths less
    /// the whole descent, which is what LineSpacingRule.Scaled says.
    ///
    /// The three boxes hold one, two and three paragraphs, so a difference that grew with the
    /// count would show as well.
    /// </remarks>
    [Fact]
    public void A_diagram_sets_its_first_line_where_word_sets_it()
    {
        if (TestFonts.SkipForMissingFonts("smartart-lines")) return;

        var docx = File.ReadAllBytes(Path.Combine(TestPaths.RealFixtures, "smartart-lines.docx"));

        var ours = PdfTextExtractor.Extract(Converter.Convert(docx,
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));
        var word = PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "real-smartart-lines.pdf"));

        static List<double> Baselines(IReadOnlyList<ExtractedTextRun> runs, int box) =>
        [
            .. runs.Where(r => r.FontSize > 20 && (int)((r.X - 72) / 152) == box)
                .Select(r => Math.Round(r.BaselineY, 2)).Distinct().Order()
        ];

        for (var box = 0; box < 3; box++)
        {
            var theirs = Baselines(word, box);
            var mine = Baselines(ours, box);

            Assert.Equal(box + 1, theirs.Count);
            Assert.Equal(theirs.Count, mine.Count);

            // Every line within a step of the grid Word writes on, and the first exactly.
            Assert.Equal(theirs[0], mine[0], 2);

            for (var line = 0; line < theirs.Count; line++)
                Assert.InRange(mine[line], theirs[line] - 0.481, theirs[line] + 0.481);
        }
    }

    /// <summary>
    /// A diagram's box keeps no space after its last paragraph, nor before its first, unless its
    /// body asks for them. Word's own diagrams put 35% of a line between paragraphs, so keeping it
    /// at the end makes the block a third of a line too tall — and text centred in its box then
    /// sits half of that too high, which was three points of the smartart fixture's divergence.
    /// </summary>
    [Theory]
    [InlineData(null, 0)]
    [InlineData("0", 0)]
    [InlineData("1", 319)]  // 35% of a 38pt line, in twips
    public void A_diagram_keeps_no_space_after_its_last_paragraph(string? asked, int expected)
    {
        var body = XElement.Parse($"""
            <a:txBody xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <a:bodyPr{(asked is null ? "" : $" spcFirstLastPara=\"{asked}\"")}/>
              <a:p>
                <a:pPr><a:spcAft><a:spcPct val="35000"/></a:spcAft></a:pPr>
                <a:r><a:rPr lang="en-GB" sz="3800"/><a:t>Alone</a:t></a:r>
              </a:p>
            </a:txBody>
            """);

        var paragraph = Assert.IsType<Paragraph>(Assert.Single(DrawingText.Parse(body)));

        Assert.Equal(expected, paragraph.Properties.SpacingAfterTwips);
    }

    [Fact]
    public void The_diagram_is_drawn_where_word_draws_it()
    {
        if (TestFonts.SkipForMissingFonts("smartart")) return;

        var mine = Lines(Ours());
        var word = Lines(Word("smartart"));

        Assert.Equal(word.Count, mine.Count);

        for (var i = 0; i < mine.Count; i++)
        {
            _output.WriteLine(
                $"\"{mine[i].Text.Trim()}\" at ({mine[i].StartX:0.##}, {mine[i].BaselineY:0.##}), " +
                $"Word at ({word[i].StartX:0.##}, {word[i].BaselineY:0.##})");

            Assert.Equal(word[i].Text.Trim(), mine[i].Text.Trim());

            Assert.True(Math.Abs(mine[i].StartX - word[i].StartX) < 0.3,
                $"'{mine[i].Text.Trim()}' begins {mine[i].StartX - word[i].StartX:+0.###;-0.###}pt " +
                "from where Word begins it.");
        }

        // Line to line inside the diagram, which is what says the spacing in a box is right even
        // though the block as a whole sits a little high. The steps into and out of the diagram
        // are not compared: they carry that offset rather than any spacing.
        for (var i = 2; i < mine.Count - 1; i++)
        {
            var ours = mine[i].BaselineY - mine[i - 1].BaselineY;
            var theirs = word[i].BaselineY - word[i - 1].BaselineY;

            Assert.True(Math.Abs(ours - theirs) < 0.6,
                $"the step from '{mine[i - 1].Text.Trim()}' to '{mine[i].Text.Trim()}' is " +
                $"{ours:0.###}pt where Word's is {theirs:0.###}pt.");
        }
    }

    /// <summary>
    /// And the boxes themselves, drawn where the arrangement puts them.
    /// </summary>
    [Fact]
    public void Every_shape_of_the_diagram_reaches_the_page()
    {
        using var stream = new MemoryStream(
            File.ReadAllBytes(Path.Combine(TestPaths.RealFixtures, "smartart.docx")));

        var laidOut = Converter.LayoutDocument(stream);
        var drawn = Assert.Single(laidOut.Pages[0].Images);

        var drawing = drawn.Image.Drawing;
        Assert.NotNull(drawing);

        // Three boxes, each filled and outlined, in one drawing: a diagram travels to the page as
        // one thing because it moves as one thing.
        Assert.Equal(3, drawing.Operations.Count);
        Assert.All(drawing.Operations, operation => Assert.IsType<Images.PathOperation>(operation));
    }

    /// <summary>
    /// And the whole of it as ink on the page, boxes included.
    /// </summary>
    /// <remarks>
    /// The boxes are rounded rectangles, which no comparison of coordinates can settle — Word
    /// writes its corners as arcs and this writes Béziers of its own — so both pages are
    /// rasterised and the ink counted. What is left of the disagreement is the outlines, which
    /// are a point wide and never land on quite the same pixels, and the 3.1pt each box's text
    /// sits above Word's.
    /// </remarks>
    [Fact]
    public void The_diagram_covers_what_word_covers()
    {
        const double scale = 3;

        if (PdfRasterizer.Render(Ours(), 0, scale) is not { } mine ||
            PdfRasterizer.Render(Word("smartart"), 0, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var (agreed, covered, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        // The diagram's own part of the page, between the two paragraphs.
        for (var y = 84.0; y < 268; y++)
        for (var x = 66.0; x < 546; x++)
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
            $"ink: {inkOfMine} here, {inkOfTheirs} in Word's; the two agree on {agreement:0.00}% of the diagram");

        Assert.True(agreement > 96, $"the two pages agree on only {agreement:0.0}% of the diagram");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.1);
    }

    /// <summary>
    /// Word throws away a cached arrangement it did not write, which is why the fixture is a real
    /// document. This is that fact, kept as a test so it is not quietly forgotten.
    /// </summary>
    [Fact]
    public void Word_lays_a_diagram_out_again_rather_than_trusting_the_cache()
    {
        using var package = OpcPackage.Open(new MemoryStream(
            File.ReadAllBytes(Path.Combine(TestPaths.RealFixtures, "smartart.docx"))));

        var shapes = Diagram.Parse(package.ReadPartAsXml("word/diagrams/drawing1.xml"));

        // The seed asked for three boxes stepping down the frame, 144 by 54, at (0,0), (108,63)
        // and (216,126). What came back from Word is a row of three, each a third of the frame
        // wide and the whole of it tall.
        Assert.Equal(3, shapes.Count);
        Assert.All(shapes, shape => Assert.Equal(180, shape.Height, 1));
        Assert.All(shapes, shape => Assert.Equal(0, shape.Y, 1));

        Assert.Equal([0, 120, 240], shapes.Select(shape => Math.Round(shape.X / 10) * 10));
    }

    private static List<TextLine> Lines(byte[] pdf) =>
        [.. PdfLineComparison.GroupIntoLines(PdfTextExtractor.Extract(pdf))
            .OrderBy(line => line.BaselineY)];

    private static byte[] Ours() =>
        Converter.Convert(File.ReadAllBytes(Path.Combine(TestPaths.RealFixtures, "smartart.docx")));

    private static byte[] Word(string name)
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "real-" + name + ".pdf");
        Assert.True(File.Exists(path),
            $"No Word reference for the {name} document. Regenerate: tools/make-real-fixtures.sh");

        return File.ReadAllBytes(path);
    }
}
