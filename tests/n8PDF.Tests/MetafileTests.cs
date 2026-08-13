using n8PDF;
using n8PDF.Images;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests reading an enhanced metafile, which is not a picture but the record of one being drawn.
/// </summary>
/// <remarks>
/// Everything else this converter reads is pixels; a metafile is a list of the commands a program
/// gave — move here, line to there, fill with this — so what comes out of it is a drawing, and it
/// stays one all the way to the PDF. That is what keeps a chart sharp at any size, and it is why
/// the text of a metafile can still be selected in the PDF it ends up in.
///
/// Two tiers again. The records are written by hand and read back, so a wrong answer names the
/// record that gave it; and then the page is drawn by macOS's own PDF reader and looked at, since
/// a drawing is the one thing here that cannot be checked by reading text positions.
/// </remarks>
public class MetafileTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    /// <summary>A drawing of a filled rectangle, an outlined ellipse, a line and some text.</summary>
    private static byte[] Sample()
    {
        var writer = new EmfWriter(200, 120);

        var pen = writer.CreatePen(180, 20, 30, 2);
        var brush = writer.CreateBrush(40, 90, 200);
        writer.Select(pen).Select(brush).Rectangle(10, 10, 95, 70);

        var hollow = writer.CreateHollowBrush();
        writer.Select(hollow).Ellipse(105, 10, 190, 70);

        writer.MoveTo(10, 85).LineTo(190, 110);

        var font = writer.CreateFont("Times New Roman", 14);
        writer.Select(font).TextColor(0, 110, 60).Text(12, 112, "Drawn by its records");

        return writer.Build();
    }

    private static VectorDrawing Drawing(byte[] emf)
    {
        var image = ImageReader.Read(emf);

        Assert.True(image.IsDrawing, "a metafile should come back as a drawing rather than pixels");

        return image.Drawing!;
    }

    [Fact]
    public void A_metafile_comes_back_as_the_commands_that_draw_it()
    {
        var drawing = Drawing(Sample());

        // The size comes from the frame the metafile declares, in points.
        Assert.Equal(200, drawing.Width, 0);
        Assert.Equal(120, drawing.Height, 0);

        // Four things drawn: two shapes, a line and a piece of text.
        Assert.Equal(4, drawing.Operations.Count);

        Assert.Equal(3, drawing.Operations.OfType<PathOperation>().Count());
        Assert.Single(drawing.Operations.OfType<TextOperation>());
    }

    /// <summary>
    /// A shape is drawn with the pen and the brush that were selected when it was met, and the
    /// hollow brush is what makes an outline rather than a fill.
    /// </summary>
    [Fact]
    public void A_shape_takes_the_pen_and_the_brush_that_were_selected()
    {
        var paths = Drawing(Sample()).Operations.OfType<PathOperation>().ToList();

        var rectangle = paths[0];
        Assert.Equal(new DrawingColor(40, 90, 200), rectangle.Fill);
        Assert.Equal(new DrawingColor(180, 20, 30), rectangle.Stroke);

        // Five steps: four corners and the close.
        Assert.Equal(5, rectangle.Steps.Count);
        Assert.Equal(PathStepKind.Close, rectangle.Steps[^1].Kind);

        // The ellipse was drawn under the hollow brush, so it is outline and nothing else — and it
        // is four curves rather than an approximation in straight lines.
        var ellipse = paths[1];
        Assert.Null(ellipse.Fill);
        Assert.NotNull(ellipse.Stroke);
        Assert.Equal(4, ellipse.Steps.Count(step => step.Kind == PathStepKind.Curve));
    }

    /// <summary>
    /// A metafile's coordinates are its own; what comes out is in points, worked out from the
    /// frame it declares against the bounds it covers.
    /// </summary>
    [Fact]
    public void The_coordinates_come_out_in_points()
    {
        var paths = Drawing(Sample()).Operations.OfType<PathOperation>().ToList();

        var first = paths[0].Steps[0].Points[0];

        Assert.Equal(10, first.X, 0);
        Assert.Equal(10, first.Y, 0);
    }

    /// <summary>
    /// Text stays text rather than being turned into outlines, so the reader that ends up with it
    /// can select it, search it and print it at any size.
    /// </summary>
    [Fact]
    public void Text_stays_text()
    {
        var text = Drawing(Sample()).Operations.OfType<TextOperation>().Single();

        Assert.Equal("Drawn by its records", text.Text);
        Assert.Equal("Times New Roman", text.FontFamily);
        Assert.Equal(new DrawingColor(0, 110, 60), text.Color);
        Assert.Equal(12, text.X, 0);
        Assert.Equal(112, text.Y, 0);
        Assert.Equal(14, text.SizePoints, 0);
    }

    /// <summary>A metafile can carry a picture, which is a bitmap without its file header.</summary>
    [Fact]
    public void A_metafile_can_carry_a_picture()
    {
        var writer = new EmfWriter(100, 100);
        writer.Bitmap(10, 10, 40, 40, ImageWriter.Bmp(8, 8, ImageWriter.Sample(8, 8)));

        var picture = Drawing(writer.Build()).Operations.OfType<ImageOperation>().Single();

        Assert.Equal(8, picture.Image.Width);
        Assert.Equal(8, picture.Image.Height);
        Assert.Equal(10, picture.X, 0);
        Assert.Equal(40, picture.Width, 0);
    }

    /// <summary>
    /// A path is a run of records between a begin and an end, drawn once at the end rather than as
    /// it goes, and filled or stroked or both as the record that closes it asks.
    /// </summary>
    [Fact]
    public void A_path_is_drawn_when_it_is_closed_rather_than_as_it_goes()
    {
        var writer = new EmfWriter(100, 100);
        var brush = writer.CreateBrush(10, 20, 30);

        writer.Select(brush)
            .BeginPath()
            .MoveTo(10, 10)
            .LineTo(90, 10)
            .LineTo(50, 80)
            .CloseFigure()
            .EndPath()
            .FillPath();

        var path = Drawing(writer.Build()).Operations.OfType<PathOperation>().Single();

        Assert.Equal(new DrawingColor(10, 20, 30), path.Fill);
        Assert.Null(path.Stroke);
        Assert.Equal(4, path.Steps.Count);
    }

    /// <summary>
    /// A file that draws nothing this can read is reported rather than drawn blank — which is what
    /// a metafile whose drawing is all in EMF+ records comes to.
    /// </summary>
    [Fact]
    public void A_metafile_that_draws_nothing_readable_is_reported()
    {
        var empty = new EmfWriter(100, 100).Build();

        Assert.True(EmfDecoder.IsEmf(empty));
        Assert.Null(ImageReader.TryRead(empty));
    }

    // ----- the newer records, which travel inside the comments of the old -----

    /// <summary>A drawing recorded only the newer way, with no old records to fall back on.</summary>
    private static byte[] Plus(Action<EmfWriter> draw)
    {
        var writer = new EmfWriter(200, 120);
        writer.PlusHeader();

        draw(writer);

        return writer.Build();
    }

    /// <summary>
    /// The newer records draw the same kinds of thing, and are read into the same drawing.
    /// </summary>
    [Fact]
    public void The_newer_records_draw_what_the_old_ones_draw()
    {
        var drawing = Drawing(Plus(writer =>
        {
            writer.PlusFillRectangle(40, 90, 200, 10, 10, 85, 60);
            writer.PlusPen(1, 180, 20, 30, 2);
            writer.PlusDrawLines(1, closed: false, (10, 85), (190, 110));
            writer.PlusFillEllipse(230, 180, 20, 105, 10, 85, 60);
            writer.PlusFont(2, "Times New Roman", 14);
            writer.PlusString(2, 0, 110, 60, 12, 100, "Drawn the newer way");
        }));

        var paths = drawing.Operations.OfType<PathOperation>().ToList();

        Assert.Equal(3, paths.Count);

        // The rectangle is filled and not stroked, since a fill names no pen.
        Assert.Equal(new DrawingColor(40, 90, 200), paths[0].Fill);
        Assert.Null(paths[0].Stroke);
        Assert.Equal(10, paths[0].Steps[0].Points[0].X, 0);

        // The line is stroked with the pen it names, at the width that pen was given.
        Assert.Equal(new DrawingColor(180, 20, 30), paths[1].Stroke);
        Assert.Equal(2, paths[1].StrokeWidth, 1);

        // And the ellipse is four curves, as it is either way round.
        Assert.Equal(4, paths[2].Steps.Count(step => step.Kind == PathStepKind.Curve));

        var text = drawing.Operations.OfType<TextOperation>().Single();

        Assert.Equal("Drawn the newer way", text.Text);
        Assert.Equal("Times New Roman", text.FontFamily);
        Assert.Equal(14, text.SizePoints, 0);
        Assert.Equal(new DrawingColor(0, 110, 60), text.Color);
    }

    /// <summary>
    /// A path is an object of its own in the newer records: its points, and a byte for each of
    /// them saying whether it begins a figure, continues one, or is part of a curve.
    /// </summary>
    [Fact]
    public void A_path_object_is_read_from_its_points_and_their_kinds()
    {
        var drawing = Drawing(Plus(writer =>
        {
            writer.PlusPath(3, (10, 10, 0), (90, 10, 1), (50, 80, 0x81));
            writer.PlusFillPath(3, 20, 40, 60);
        }));

        var path = drawing.Operations.OfType<PathOperation>().Single();

        Assert.Equal(new DrawingColor(20, 40, 60), path.Fill);
        Assert.Equal(PathStepKind.Move, path.Steps[0].Kind);
        Assert.Equal(PathStepKind.Line, path.Steps[1].Kind);
        Assert.Equal(PathStepKind.Close, path.Steps[^1].Kind);
    }

    /// <summary>
    /// The newer records are properly transformed: a point goes through the transform the drawing
    /// has set before it is anywhere, and the transforms compose.
    /// </summary>
    [Fact]
    public void The_transform_a_drawing_sets_moves_what_follows_it()
    {
        var plain = Drawing(Plus(writer => writer.PlusFillRectangle(10, 20, 30, 10, 10, 20, 20)));
        var moved = Drawing(Plus(writer =>
        {
            writer.PlusTranslate(40, 25);
            writer.PlusFillRectangle(10, 20, 30, 10, 10, 20, 20);
        }));

        var from = plain.Operations.OfType<PathOperation>().Single().Steps[0].Points[0];
        var to = moved.Operations.OfType<PathOperation>().Single().Steps[0].Points[0];

        Assert.Equal(from.X + 40, to.X, 0);
        Assert.Equal(from.Y + 25, to.Y, 0);

        // And a scale multiplies what a translation has already moved.
        var scaled = Drawing(Plus(writer =>
        {
            writer.PlusScale(2, 3);
            writer.PlusFillRectangle(10, 20, 30, 10, 10, 20, 20);
        }));

        var bigger = scaled.Operations.OfType<PathOperation>().Single().Steps[0].Points[0];

        Assert.Equal(from.X * 2, bigger.X, 0);
        Assert.Equal(from.Y * 3, bigger.Y, 0);
    }

    /// <summary>
    /// A file carrying both formats draws one picture, not two, and it is the newer records that
    /// draw it. That is what they are for: the old ones beside them are a copy left for readers
    /// that have never heard of the new, and a file whose halves differ at all differs by the new
    /// half being the fuller one.
    /// </summary>
    /// <remarks>
    /// The two halves here draw different words, which no real file does, so that which of them
    /// was read can be asked at all. What says the reading of the newer half is right rather than
    /// merely chosen is the fixture, whose halves draw one picture: Word draws the old half of it,
    /// this draws the new, and the two have to reach the page in the same place.
    /// </remarks>
    [Fact]
    public void A_file_written_both_ways_is_drawn_the_newer_way()
    {
        var writer = new EmfWriter(200, 120);

        writer.PlusHeader(dual: true);
        writer.PlusFillRectangle(40, 90, 200, 10, 10, 85, 60);
        writer.PlusFont(2, "Times New Roman", 14);
        writer.PlusString(2, 0, 110, 60, 12, 100, "Drawn the newer way");

        var brush = writer.CreateBrush(40, 90, 200);
        writer.Select(brush).Rectangle(10, 10, 95, 70);

        var font = writer.CreateFont("Times New Roman", 14);
        writer.Select(font).Text(12, 100, "Drawn the older way");

        var drawing = Drawing(writer.Build());

        var text = drawing.Operations.OfType<TextOperation>().Single();

        Assert.Equal("Drawn the newer way", text.Text);

        // Once, not twice: the old records drew a rectangle of their own.
        Assert.Single(drawing.Operations.OfType<PathOperation>());
    }

    /// <summary>
    /// The old records are not always only a copy. A file may hand the drawing back to them part
    /// way through — for something the newer interface had no way to record — and says so where it
    /// does. From there they draw, until the newer records resume.
    /// </summary>
    [Fact]
    public void The_older_records_draw_what_the_newer_ones_hand_back_to_them()
    {
        var writer = new EmfWriter(200, 120);

        writer.PlusHeader();
        writer.PlusFillRectangle(40, 90, 200, 10, 10, 85, 60);

        // Handed back: what follows is drawn by the old records.
        writer.PlusGetDC();

        var brush = writer.CreateBrush(200, 40, 90);
        writer.Select(brush).Rectangle(120, 10, 190, 70);

        // And taken up again, after which they are not.
        writer.PlusFillEllipse(10, 200, 90, 10, 85, 60, 30);

        var ignored = writer.CreateBrush(1, 2, 3);
        writer.Select(ignored).Rectangle(0, 0, 5, 5);

        var paths = Drawing(writer.Build()).Operations.OfType<PathOperation>().ToList();

        // The rectangle the newer records filled, the one the old records drew while they had the
        // drawing, and the ellipse the newer ones drew on taking it back. Not the last rectangle,
        // which the old records drew after the drawing had gone back to the newer ones.
        Assert.Equal(3, paths.Count);
        Assert.Equal(new DrawingColor(40, 90, 200), paths[0].Fill);
        Assert.Equal(new DrawingColor(200, 40, 90), paths[1].Fill);
        Assert.Equal(new DrawingColor(10, 200, 90), paths[2].Fill);

        // And in the order the file draws them, which is the order they cover one another in.
        Assert.Equal(120, paths[1].Steps[0].Points[0].X, 0);
    }

    /// <summary>The metafile a document carries, taken back out of it.</summary>
    private static byte[] MetafileOf(byte[] docx)
    {
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(docx));

        var part = archive.Entries.Single(entry => entry.FullName.EndsWith(".emf"));

        using var stream = part.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    // ----- the whole way through -----

    [Fact]
    public void A_drawing_reaches_the_pdf_as_drawing_commands()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(Sample(), "emf");
        builder.AddImageParagraph(id, 200, 120);

        var pdf = Converter.Convert(builder.Build(), Options());
        var content = System.Text.Encoding.Latin1.GetString(pdf);

        // Drawn rather than embedded: there is no image in the file at all.
        Assert.DoesNotContain("/Subtype /Image", content);

        if (!QpdfTool.IsAvailable) return;

        var result = QpdfTool.CheckBytes(pdf, "metafile");
        Assert.True(result.IsClean || result.HasWarningsOnly, result.Output);
    }

    /// <summary>
    /// A drawing says where the top of its text goes and a PDF says where the baseline goes, so
    /// the text drops by the height of the characters themselves — the em less what hangs below
    /// the line, with the leading above them left out.
    /// </summary>
    /// <remarks>
    /// Word's own rendering of the fixture is what settles it: at fourteen points of Times New
    /// Roman it puts the baseline 10.98pt below the point the record names, and the em less the
    /// descent is 10.97pt of it.
    /// </remarks>
    [Fact]
    public void Text_drops_from_where_the_record_puts_it_to_where_a_baseline_goes()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(Sample(), "emf");
        builder.AddImageParagraph(id, 200, 120);

        var pdf = Converter.Convert(builder.Build(), Options());

        var line = PdfLineComparison
            .GroupIntoLines(PdfTextExtractor.Extract(pdf))
            .Single(l => l.Text.Contains("Drawn"));

        // The drawing's own text sits at 112 points down it, and the drawing starts at the foot
        // of the paragraph above — which is the top margin here, since it is the first thing.
        var metrics = TestFonts.CreatePinnedLibrary().Resolve("Times New Roman", false, false).Font.Metrics;
        var toBaseline = (metrics.UnitsPerEm - metrics.WinDescent) * 14.0 / metrics.UnitsPerEm;

        Assert.Equal(72 + 112 + toBaseline, line.BaselineY, 1);
    }

    /// <summary>
    /// What a drawing looks like cannot be read out of the text of a PDF, so the page is drawn by
    /// a reader that shares nothing with this one and the pixels are looked at: the shapes have to
    /// be where they were put, in the colours they were given.
    /// </summary>
    [Fact]
    public void The_drawing_appears_on_the_page_where_it_was_put()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(Sample(), "emf");
        builder.AddImageParagraph(id, 200, 120);

        var pdf = Converter.Convert(builder.Build(), Options());

        if (PdfRasterizer.Render(pdf, scale: 3) is not { } rendered)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // The drawing sits at the left margin, a little below the top of the page.
        const double left = 72;
        const double top = 72;

        var inside = rendered.At(left + 50, top + 40, 3);
        var outside = rendered.At(left + 150, top + 100, 3);

        _output.WriteLine($"inside the rectangle {inside}, outside the drawing {outside}");

        // The rectangle was filled in blue, and is where it was drawn.
        Assert.True(inside.B > 150 && inside.R < 100, $"the filled rectangle is {inside}");

        // And the paper around the drawing is still paper.
        Assert.True(outside is { R: > 240, G: > 240, B: > 240 }, $"the page is {outside} where it should be blank");

        // The text of a drawing is text, so a reader can find it.
        Assert.Contains("Drawn by its records", rendered.Text);
    }

    /// <summary>
    /// And the newer records draw what the old ones draw, measured against another implementation
    /// rather than against this one's idea of them.
    /// </summary>
    /// <remarks>
    /// This is the check the newer records had no way of having until they were the ones drawn.
    /// Word renders classic metafile records and draws nothing whatever for these, so its export
    /// of the fixture is its rendering of the old half — while this page is the new half. The two
    /// halves record one picture, so the two pages have to be one picture: not pixel for pixel,
    /// since two renderers differ along every edge they draw, but in what is covered and what is
    /// left as paper.
    ///
    /// Where they agree the newer records have been read right, since nothing about this file's
    /// old half reached the page. Where a shape were misplaced, misscaled, or missing, the two
    /// would part company by far more than the edges of either can account for.
    /// </remarks>
    [Fact]
    public void The_newer_records_draw_the_page_the_old_records_draw()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "images-metafile-plus.pdf");

        Assert.True(File.Exists(reference),
            $"No Word reference for the metafile fixture. Generate it: tools/make-reference-pdfs.sh");

        var pdf = Converter.Convert(Fixtures.Build("images-metafile-plus"), Options());

        const double scale = 3;

        if (PdfRasterizer.Render(pdf, scale: scale) is not { } ours ||
            PdfRasterizer.Render(File.ReadAllBytes(reference), scale: scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            _output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // The drawing's own corner of the page, in points, with room around it.
        var covered = 0;
        var agreed = 0;
        var mine = 0;
        var theirs = 0;

        for (var y = 70; y < 200; y++)
        for (var x = 70; x < 280; x++)
        {
            var a = ours.At(x, y, scale);
            var b = word.At(x, y, scale);

            var inkOfMine = a.R < 200 || a.G < 200 || a.B < 200;
            var inkOfTheirs = b.R < 200 || b.G < 200 || b.B < 200;

            if (inkOfMine) mine++;
            if (inkOfTheirs) theirs++;
            if (inkOfMine == inkOfTheirs) agreed++;

            covered++;
        }

        var agreement = 100.0 * agreed / covered;

        _output.WriteLine(
            $"ink: {mine} here, {theirs} in Word's; the two pages agree on {agreement:0.00}% of the drawing");

        Assert.True(agreement > 95, $"the two pages agree on only {agreement:0.0}% of the drawing");

        // And neither draws substantially more of it than the other, which is what a shape drawn
        // by one and not the other would look like however well the rest lined up.
        Assert.InRange((double)mine / theirs, 0.8, 1.25);
    }
}
