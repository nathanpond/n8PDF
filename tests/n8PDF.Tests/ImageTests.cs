using System.Text;
using n8PDF;
using n8PDF.Images;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests image decoding and placement. The decoder is checked against images whose pixels the
/// test chose, so a wrong answer is recognisable rather than merely different.
/// </summary>
public class ImageTests
{
    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    [Fact]
    public void Png_decodes_to_the_pixels_it_was_written_with()
    {
        var image = ImageReader.Read(PngWriter.Solid(8, 5, 200, 100, 50));

        Assert.Equal(8, image.Width);
        Assert.Equal(5, image.Height);
        Assert.Equal(ImageColorSpace.Rgb, image.ColorSpace);
        Assert.Equal(ImageEncoding.Raw, image.Encoding);
        Assert.False(image.HasAlpha);
        Assert.Equal(8 * 5 * 3, image.Data.Length);

        // Every pixel should be the colour it was written as, including the last one — an
        // off-by-one in the row filters shows up at the edges first.
        for (var i = 0; i < 8 * 5; i++)
        {
            Assert.Equal(200, image.Data[i * 3]);
            Assert.Equal(100, image.Data[i * 3 + 1]);
            Assert.Equal(50, image.Data[i * 3 + 2]);
        }
    }

    [Fact]
    public void Png_rows_are_not_transposed_or_reversed()
    {
        // A diagonal is asymmetric in both axes, so a decoder that flips or transposes produces a
        // visibly different image rather than an equal-looking one.
        var image = ImageReader.Read(PngWriter.Diagonal(4));

        // Top-right is the light half, bottom-left the dark one.
        var topRight = (0 * 4 + 3) * 3;
        var bottomLeft = (3 * 4 + 0) * 3;

        Assert.Equal(220, image.Data[topRight]);
        Assert.Equal(30, image.Data[bottomLeft]);
    }

    [Fact]
    public void Png_alpha_becomes_a_separate_mask()
    {
        var image = ImageReader.Read(PngWriter.HalfTransparent(8));

        Assert.True(image.HasAlpha);
        Assert.Equal(ImageColorSpace.Rgb, image.ColorSpace);

        // The colour data keeps three components; opacity is carried alongside it.
        Assert.Equal(8 * 8 * 3, image.Data.Length);
        Assert.Equal(8 * 8, image.Alpha!.Length);

        Assert.Equal(0, image.Alpha[0]);       // left half transparent
        Assert.Equal(255, image.Alpha[7]);     // right half opaque
    }

    /// <summary>
    /// A format nothing here reads is reported as one rather than guessed at, and a file that only
    /// begins like one it does read is not read past the point where it stops making sense.
    /// </summary>
    [Fact]
    public void Unsupported_formats_are_reported_rather_than_guessed_at()
    {
        // A metafile, which is drawing commands rather than pixels and is not handled.
        var metafile = new byte[] { 0x01, 0x00, 0x00, 0x00, 0x6c, 0x00, 0x00, 0x00, 0x20, 0x45, 0x4d, 0x46 };

        Assert.False(ImageReader.IsSupported(metafile));
        Assert.Null(ImageReader.TryRead(metafile));
        Assert.Throws<ImageFormatException>(() => ImageReader.Read(metafile));

        // Enough of a GIF to be recognised as one and not enough to be read as a picture.
        Assert.Null(ImageReader.TryRead("GIF89a and then some"u8.ToArray()));

        // Truncated PNG data must fail as a format error, not as an index-out-of-range.
        var truncated = PngWriter.Solid(4, 4, 1, 2, 3)[..20];
        Assert.Null(ImageReader.TryRead(truncated));
    }

    [Fact]
    public void Image_occupies_its_declared_size_on_the_page()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(10, 10, 255, 0, 0));
        builder.AddImageParagraph(id, widthPoints: 90, heightPoints: 45);

        using var stream = builder.BuildStream();
        var page = Converter.LayoutDocument(stream, Options()).Pages.Single();

        var image = Assert.Single(page.Images);

        // The display size comes from the drawing, not from the pixel dimensions: a 10x10 picture
        // asked to be 90x45 points is stretched, exactly as Word would.
        Assert.Equal(90, image.Width, 2);
        Assert.Equal(45, image.Height, 2);
        Assert.Equal(72, image.X, 2);
        Assert.Equal(10, image.Image.Width);
    }

    [Fact]
    public void Image_sets_the_line_height_and_rests_on_the_baseline()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(10, 10, 0, 255, 0));
        builder
            .AddParagraph("Above.", runProperties: DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 24))
            .AddImageParagraph(id, widthPoints: 40, heightPoints: 60);

        using var stream = builder.BuildStream();
        var document = Converter.LayoutDocument(stream, Options());

        var lines = document.Pages.Single().Lines;
        var imageLine = lines[^1];
        var image = Assert.Single(document.Pages.Single().Images);

        // A 60pt image makes a line at least 60pt tall — far more than the 12pt text above it.
        Assert.True(imageLine.Height >= 60, $"line height {imageLine.Height} should accommodate the image");

        // It rests on the baseline, so its bottom edge and the baseline coincide.
        Assert.Equal(imageLine.BaselineY, image.Y + image.Height, 2);
    }

    [Fact]
    public void Image_wraps_to_the_next_line_when_it_does_not_fit_beside_text()
    {
        var builder = new DocxBuilder();
        var id = builder.AddImagePart(PngWriter.Solid(10, 10, 0, 0, 255));

        // 400pt of image after a long run of text cannot share the 468pt measure.
        builder.AddImageParagraph(id, widthPoints: 400, heightPoints: 20,
            leadingText: "This leading text is long enough that four hundred points of image cannot follow it. ");

        using var stream = builder.BuildStream();
        var document = Converter.LayoutDocument(stream, Options());
        var page = document.Pages.Single();

        var image = Assert.Single(page.Images);

        // Pushed onto a line of its own, so it starts at the left margin rather than mid-line.
        Assert.Equal(72, image.X, 2);
        Assert.True(page.Lines.Count > 1, "the image should have wrapped onto its own line");
    }

    [Fact]
    public void Converted_pdf_carries_the_image_as_an_xobject()
    {
        var pdf = Converter.Convert(Fixtures.Build("images"), Options());
        var text = Encoding.Latin1.GetString(pdf);

        Assert.Contains("/Subtype /Image", text);
        Assert.Contains("/ColorSpace /DeviceRGB", text);
        Assert.Contains("/BitsPerComponent 8", text);

        // Transparency is carried as a soft mask rather than a fourth channel.
        Assert.Contains("/SMask", text);
        Assert.Contains("/ColorSpace /DeviceGray", text);

        // Three distinct pictures, so three XObjects.
        Assert.Contains("/Im1", text);
        Assert.Contains("/Im2", text);
        Assert.Contains("/Im3", text);

        TestPaths.WriteArtifact("images.pdf", pdf);
    }

    [Fact]
    public void The_same_picture_used_twice_is_embedded_once()
    {
        var builder = new DocxBuilder();
        var bytes = PngWriter.Solid(12, 12, 90, 90, 90);

        // Two drawings pointing at one part, which is what Word writes when a picture is reused.
        var id = builder.AddImagePart(bytes);
        builder.AddImageParagraph(id, 30, 30).AddImageParagraph(id, 30, 30);

        var text = Encoding.Latin1.GetString(Converter.Convert(builder.Build(), Options()));

        Assert.Contains("/Im1", text);
        Assert.DoesNotContain("/Im2", text);
    }

    [Fact]
    public void A_missing_or_broken_image_costs_only_its_own_placement()
    {
        // A drawing referring to a relationship that does not exist: the text around it must
        // still convert rather than the whole document failing.
        var builder = new DocxBuilder()
            .AddParagraph("Before.")
            .AddImageParagraph("rIdMissing", 40, 40)
            .AddParagraph("After.");

        var pdf = Converter.Convert(builder.Build(), Options());
        var extracted = string.Concat(PdfTextExtractor.Extract(pdf).Select(r => r.Text));

        Assert.Contains("Before.", extracted);
        Assert.Contains("After.", extracted);
    }
}
