using n8PDF;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests floating images and the text wrapping around them.
/// </summary>
/// <remarks>
/// The thing being checked is not where the picture sits — that is arithmetic — but that the text
/// beside it is genuinely narrower and starts clear of it. A float that is drawn but not honoured
/// by line breaking looks almost right and is wrong in the way that matters.
/// </remarks>
public class FloatingImageTests
{
    private const string Times12 = "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static string Body(int repeats = 30) =>
        string.Join(' ', Enumerable.Repeat("Text flows around the floating picture.", repeats));

    private static Layout.LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    [Fact]
    public void Square_wrap_narrows_the_lines_beside_the_image()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 200, 40, 40));

        // 144pt of picture at the left edge, with 6pt of clearance either side.
        builder.AddAnchoredImageParagraph(id, 144, 108, Body(), runProperties: Times12);

        var page = LayoutOf(builder).Pages[0];
        var image = Assert.Single(page.Images);

        Assert.Equal(72, image.X, 1);

        // Lines whose baseline falls within the picture's band must start clear of it; lines
        // below it must return to the margin.
        var beside = page.Lines.Where(l => l.Texts.Count > 0 && l.BaselineY < image.Y + image.Height).ToList();
        var below = page.Lines.Where(l => l.Texts.Count > 0 && l.BaselineY > image.Y + image.Height + 12).ToList();

        Assert.NotEmpty(beside);
        Assert.NotEmpty(below);

        foreach (var line in beside)
            Assert.True(line.Texts[0].X >= 72 + 144, $"line at {line.BaselineY} starts at {line.Texts[0].X}, inside the picture");

        foreach (var line in below)
            Assert.Equal(72, line.Texts[0].X, 1);
    }

    [Fact]
    public void Square_wrap_leaves_the_beside_lines_shorter_than_the_full_measure()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 40, 40, 200));
        builder.AddAnchoredImageParagraph(id, 144, 108, Body(), runProperties: Times12);

        var page = LayoutOf(builder).Pages[0];
        var image = page.Images[0];

        var beside = page.Lines.First(l => l.Texts.Count > 0 && l.BaselineY < image.Y + image.Height);
        var below = page.Lines.Last(l => l.Texts.Count > 0 && l.BaselineY > image.Y + image.Height + 12);

        var besideWidth = beside.Texts.Max(t => t.X + t.Width) - beside.Texts[0].X;
        var belowWidth = below.Texts.Max(t => t.X + t.Width) - below.Texts[0].X;

        // The measure beside a 144pt picture is about 150pt narrower than the full one, so a line
        // there cannot be as long. Comparing widths catches a float that shifted the text without
        // actually reducing the space it was broken into.
        Assert.True(besideWidth < belowWidth - 100,
            $"line beside the picture is {besideWidth:0.#}pt wide, one below it {belowWidth:0.#}pt");
    }

    [Fact]
    public void A_right_aligned_float_pushes_text_left_rather_than_right()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 40, 160, 40));
        builder.AddAnchoredImageParagraph(id, 144, 108, Body(), alignX: "right", runProperties: Times12);

        var page = LayoutOf(builder).Pages[0];
        var image = Assert.Single(page.Images);

        // Flush to the right margin: 72 + 468 - 144.
        Assert.Equal(72 + 468 - 144, image.X, 1);

        var beside = page.Lines.Where(l => l.Texts.Count > 0 && l.BaselineY < image.Y + image.Height).ToList();
        Assert.NotEmpty(beside);

        foreach (var line in beside)
        {
            Assert.Equal(72, line.Texts[0].X, 1);
            Assert.True(line.Texts.Max(t => t.X + t.Width) <= image.X,
                "text beside a right-hand float must stop before it");
        }
    }

    [Fact]
    public void Top_and_bottom_wrap_leaves_nothing_beside_the_image()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 120, 60, 160));
        builder.AddAnchoredImageParagraph(id, 200, 90, Body(), wrap: "topAndBottom", runProperties: Times12);

        var page = LayoutOf(builder).Pages[0];
        var image = Assert.Single(page.Images);

        // Every line clears the picture vertically; none shares its band.
        foreach (var line in page.Lines.Where(l => l.Texts.Count > 0))
        {
            var top = line.BaselineY - line.Ascent;
            var bottom = line.BaselineY;

            Assert.True(bottom <= image.Y || top >= image.Y + image.Height,
                $"line at {top:0.#}-{bottom:0.#} overlaps the picture at {image.Y:0.#}-{image.Y + image.Height:0.#}");
        }
    }

    [Fact]
    public void Wrap_none_lets_text_run_under_the_image()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 90, 90, 90));
        builder.AddAnchoredImageParagraph(id, 144, 108, Body(), wrap: "none", behindText: true,
            runProperties: Times12);

        var page = LayoutOf(builder).Pages[0];
        Assert.Single(page.Images);

        // wrapNone means the text ignores the picture entirely, so every line uses the full
        // measure and starts at the margin.
        foreach (var line in page.Lines.Where(l => l.Texts.Count > 0))
            Assert.Equal(72, line.Texts[0].X, 1);
    }

    [Fact]
    public void An_offset_float_is_positioned_relative_to_the_paragraph()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 10, 10, 10));

        builder
            .AddParagraph("First paragraph.", runProperties: Times12)
            .AddAnchoredImageParagraph(id, 72, 72, Body(6),
                offsetXPoints: 36, offsetYPoints: 18, runProperties: Times12);

        var document = LayoutOf(builder);
        var page = document.Pages[0];
        var image = Assert.Single(page.Images);

        // Horizontally the offset is from the column, so from the left margin.
        Assert.Equal(72 + 36, image.X, 1);

        // Vertically it is from the paragraph the anchor sits in, which starts below the first.
        var firstLine = page.Lines[0];
        Assert.True(image.Y > firstLine.BaselineY,
            $"the picture at {image.Y:0.#} should sit below the first paragraph at {firstLine.BaselineY:0.#}");
    }

    [Fact]
    public void Floats_do_not_leak_onto_the_next_page()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(20, 20, 200, 200, 40));

        builder.AddAnchoredImageParagraph(id, 144, 108, Body(120), runProperties: Times12);

        var document = LayoutOf(builder);
        Assert.True(document.Pages.Count > 1, "120 repeats should overflow one page");

        // The picture belongs to the page its anchor landed on, so the next page is unobstructed.
        Assert.Single(document.Pages[0].Images);
        Assert.Empty(document.Pages[1].Images);

        foreach (var line in document.Pages[1].Lines.Where(l => l.Texts.Count > 0))
            Assert.Equal(72, line.Texts[0].X, 1);
    }
}
