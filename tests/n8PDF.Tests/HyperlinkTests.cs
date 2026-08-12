using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests hyperlinks: that the target survives the trip from a relationship id to a clickable
/// region, and that the region sits over the text it belongs to.
/// </summary>
public class HyperlinkTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static string Run(string text) =>
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    /// <summary>Every link annotation in a converted document, page by page.</summary>
    private static List<(int PageIndex, PdfDictValue Annotation)> AnnotationsOf(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        var result = new List<(int, PdfDictValue)>();

        foreach (var page in reader.GetPages())
        {
            if (reader.GetEntry(page.Dictionary, "Annots") is not PdfArrayValue annotations) continue;

            foreach (var item in annotations.Items)
            {
                if (reader.Resolve(item) is not PdfDictValue annotation) continue;
                if (reader.Resolve(annotation.Get("Subtype")) is PdfNameValue { Name: "Link" })
                    result.Add((page.Index, annotation));
            }
        }

        return result;
    }

    private static string? UrlOf(PdfFileReader reader, PdfDictValue annotation)
    {
        if (reader.Resolve(annotation.Get("A")) is not PdfDictValue action) return null;
        return reader.Resolve(action.Get("URI")) is PdfStringValue uri ? uri.AsLatin1 : null;
    }

    private static List<string> UrlsOf(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        return AnnotationsOf(pdf)
            .Select(a => UrlOf(reader, a.Annotation))
            .Where(url => url is not null)
            .Select(url => url!)
            .ToList();
    }

    [Fact]
    public void External_link_becomes_a_uri_annotation()
    {
        var builder = new DocxBuilder();
        var id = builder.AddExternalHyperlink("https://example.com/reference?page=2#top");
        builder.AddRawParagraph(
            "<w:p>" + Run("See ") + DocxBuilder.Hyperlink("the reference", id, runProperties: Times12) +
            Run(" for more.") + "</w:p>");

        Assert.Equal(["https://example.com/reference?page=2#top"], UrlsOf(Converter.Convert(builder.Build(), Options())));
    }

    [Fact]
    public void Annotation_covers_the_link_text_and_nothing_else()
    {
        var builder = new DocxBuilder();
        var id = builder.AddExternalHyperlink("https://example.com/");
        builder.AddRawParagraph(
            "<w:p>" + Run("See ") + DocxBuilder.Hyperlink("the reference", id, runProperties: Times12) +
            Run(" for more.") + "</w:p>");

        var layout = LayoutOf(builder);
        var linked = layout.Pages[0].Texts.Single(t => t.Link is not null);

        var pdf = Converter.Convert(builder.Build(), Options());
        var (pageIndex, annotation) = Assert.Single(AnnotationsOf(pdf));
        Assert.Equal(0, pageIndex);

        var reader = new PdfFileReader(pdf);
        var rectangle = (PdfArrayValue)reader.Resolve(annotation.Get("Rect"))!;
        var left = PdfFileReader.ToDouble(reader.Resolve(rectangle[0]));
        var right = PdfFileReader.ToDouble(reader.Resolve(rectangle[2]));
        var bottom = PdfFileReader.ToDouble(reader.Resolve(rectangle[1]));
        var top = PdfFileReader.ToDouble(reader.Resolve(rectangle[3]));

        // The rectangle is in PDF space, so its Y grows upward from the bottom of the page.
        var height = layout.Pages[0].HeightPoints;

        // The region is the text's box padded by the same amount at each end, the way Word does
        // it, so it starts a little before the text and ends a little after it.
        var padding = linked.X - left;
        Assert.InRange(padding, 1, 4);
        Assert.Equal(linked.X + linked.Width + padding, right, 2);
        Assert.True(top > bottom, $"the rectangle is inverted: {bottom} to {top}");
        Assert.True(height - top < linked.BaselineY, "the top edge is below the baseline");
        Assert.True(height - bottom > linked.BaselineY, "the bottom edge is above the baseline");
    }

    /// <summary>
    /// A phrase split into several runs by formatting is one link to the reader, and should be one
    /// clickable region rather than a row of abutting ones.
    /// </summary>
    [Fact]
    public void Runs_sharing_a_target_produce_one_annotation()
    {
        var builder = new DocxBuilder();
        var id = builder.AddExternalHyperlink("https://example.com/");
        var bold = DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 24, bold: true);

        builder.AddRawParagraph(
            "<w:p><w:hyperlink r:id=\"" + id + "\">" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">a </w:t></w:r>" +
            $"<w:r><w:rPr>{bold}</w:rPr><w:t>bold</w:t></w:r>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> link</w:t></w:r>" +
            "</w:hyperlink></w:p>");

        var layout = LayoutOf(builder);
        Assert.Equal(3, layout.Pages[0].Texts.Count(t => t.Link is not null));

        Assert.Single(AnnotationsOf(Converter.Convert(builder.Build(), Options())));
    }

    [Fact]
    public void Two_different_targets_stay_apart()
    {
        var builder = new DocxBuilder();
        var first = builder.AddExternalHyperlink("https://example.com/one");
        var second = builder.AddExternalHyperlink("https://example.com/two");

        builder.AddRawParagraph(
            "<w:p>" +
            DocxBuilder.Hyperlink("one", first, runProperties: Times12) +
            DocxBuilder.Hyperlink("two", second, runProperties: Times12) +
            "</w:p>");

        Assert.Equal(
            ["https://example.com/one", "https://example.com/two"],
            UrlsOf(Converter.Convert(builder.Build(), Options())));
    }

    [Fact]
    public void Text_without_a_link_gets_no_annotation()
    {
        var builder = new DocxBuilder().AddParagraph("Nothing here links anywhere.", runProperties: Times12);
        Assert.Empty(AnnotationsOf(Converter.Convert(builder.Build(), Options())));
    }

    /// <summary>
    /// A relationship id that resolves to nothing is not a link. The text must still be drawn:
    /// a broken target should cost the reader a click, not a sentence.
    /// </summary>
    [Fact]
    public void Missing_relationship_leaves_plain_text()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p>" + DocxBuilder.Hyperlink("dangling", "rIdNotThere", runProperties: Times12) + "</w:p>");

        var layout = LayoutOf(builder);
        Assert.Equal("dangling", string.Concat(layout.Pages[0].Texts.Select(t => t.Text)));
        Assert.All(layout.Pages[0].Texts, t => Assert.Null(t.Link));

        Assert.Empty(AnnotationsOf(Converter.Convert(builder.Build(), Options())));
    }

    [Fact]
    public void Internal_link_points_at_the_page_holding_the_bookmark()
    {
        var builder = new DocxBuilder()
            .AddRawParagraph(
                "<w:p>" + Run("Jump to ") +
                DocxBuilder.Hyperlink("the appendix", anchor: "appendix", runProperties: Times12) + "</w:p>")
            .AddRawParagraph(
                "<w:p><w:pPr><w:pageBreakBefore/></w:pPr>" + DocxBuilder.Bookmark("appendix") +
                Run("Appendix") + "</w:p>");

        var layout = LayoutOf(builder);
        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(1, layout.Bookmarks["appendix"].PageIndex);

        var pdf = Converter.Convert(builder.Build(), Options());
        var reader = new PdfFileReader(pdf);
        var pages = reader.GetPages();

        var (pageIndex, annotation) = Assert.Single(AnnotationsOf(pdf));
        Assert.Equal(0, pageIndex);
        Assert.Null(UrlOf(reader, annotation));

        // The destination names the second page and a point on it, expressed as an explicit
        // reference rather than a page number, which is what a PDF destination array holds.
        var destination = (PdfArrayValue)reader.Resolve(annotation.Get("Dest"))!;
        Assert.IsType<PdfRefValue>(destination[0]);
        Assert.Same(pages[1].Dictionary, reader.Resolve(destination[0]));
        Assert.Equal("XYZ", Assert.IsType<PdfNameValue>(reader.Resolve(destination[1])).Name);

        // Word measures down from the top of the page and PDF up from the bottom, so the
        // destination's Y must be near the top rather than near zero.
        var y = PdfFileReader.ToDouble(reader.Resolve(destination[3]));
        Assert.True(y > pages[1].Height * 0.8, $"the destination is {y}pt up a {pages[1].Height}pt page");
    }

    /// <summary>
    /// An anchor with no bookmark behind it goes nowhere, so no region is created: a clickable
    /// region that does nothing when clicked is worse than plain text.
    /// </summary>
    [Fact]
    public void Anchor_without_a_bookmark_gets_no_annotation()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p>" + DocxBuilder.Hyperlink("nowhere", anchor: "missing", runProperties: Times12) + "</w:p>");

        Assert.Empty(AnnotationsOf(Converter.Convert(builder.Build(), Options())));
    }

    /// <summary>
    /// Relationship ids are scoped to the part that declares them, so a header's rId1 and the
    /// body's rId1 are different links. Sharing one table without scoping would silently send one
    /// of them to the other's address.
    /// </summary>
    [Fact]
    public void Header_and_body_links_with_the_same_id_stay_distinct()
    {
        var builder = new DocxBuilder();
        var bodyId = builder.AddExternalHyperlink("https://example.com/body");

        builder
            .WithHeaderFooter(header: true,
                "<w:p>" + DocxBuilder.Hyperlink("header link", "rIdLink1", runProperties: Times12) + "</w:p>",
                headerHyperlinks: [("rIdLink1", "https://example.com/header")])
            .AddRawParagraph(
                "<w:p>" + DocxBuilder.Hyperlink("body link", bodyId, runProperties: Times12) + "</w:p>");

        // The body's first hyperlink id is rIdLink1 too, which is the collision under test.
        Assert.Equal("rIdLink1", bodyId);

        var urls = UrlsOf(Converter.Convert(builder.Build(), Options()));
        Assert.Equal(2, urls.Count);
        Assert.Contains("https://example.com/body", urls);
        Assert.Contains("https://example.com/header", urls);
    }

    [Fact]
    public void Bookmark_contributes_no_text()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p>" + DocxBuilder.Bookmark("top") + Run("Heading") + "</w:p>");

        var layout = LayoutOf(builder);
        Assert.Equal("Heading", string.Concat(layout.Pages[0].Texts.Select(t => t.Text)));
        Assert.True(layout.Bookmarks.ContainsKey("top"));
    }

    /// <summary>
    /// Word writes a _GoBack bookmark recording where the cursor was when the file was saved. It
    /// is an editing artefact, not a destination, and carrying it would make every real document
    /// look like it had a bookmark it does not have.
    /// </summary>
    [Fact]
    public void Word_editing_bookmark_is_ignored()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p>" + DocxBuilder.Bookmark("_GoBack") + Run("Text") + "</w:p>");

        Assert.Empty(LayoutOf(builder).Bookmarks);
    }

    /// <summary>
    /// Compares the clickable regions against the ones Word puts in its own export of the same
    /// document. Everything else about a link can be right while the region sits in the wrong
    /// place, and only Word can say where that place is.
    /// </summary>
    [Fact]
    public void Link_regions_match_word()
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, "hyperlinks.pdf");
        Assert.True(File.Exists(referencePath), $"No Word reference PDF at {referencePath}");

        var ours = RectanglesOf(Converter.Convert(Fixtures.Build("hyperlinks"), Options()));
        var theirs = RectanglesOf(File.ReadAllBytes(referencePath));

        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            var (page, rectangle) = ours[i];
            var (referencePage, reference) = theirs[i];

            Assert.Equal(referencePage, page);

            for (var edge = 0; edge < 4; edge++)
            {
                // Word quantizes vertical positions to 1/300 inch, so a quarter point of
                // disagreement is the floor rather than a defect.
                Assert.True(Math.Abs(rectangle[edge] - reference[edge]) <= 0.3,
                    $"link {i + 1} edge {edge}: {rectangle[edge]:0.##} against Word's {reference[edge]:0.##} " +
                    $"(ours [{string.Join(", ", rectangle.Select(v => v.ToString("0.##")))}], " +
                    $"Word's [{string.Join(", ", reference.Select(v => v.ToString("0.##")))}])");
            }
        }
    }

    /// <summary>The link rectangles of a document, in page then reading order.</summary>
    private static List<(int PageIndex, double[] Rectangle)> RectanglesOf(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        var result = new List<(int, double[])>();

        foreach (var (pageIndex, annotation) in AnnotationsOf(pdf))
        {
            if (reader.Resolve(annotation.Get("Rect")) is not PdfArrayValue rectangle) continue;

            result.Add((pageIndex, [.. Enumerable.Range(0, 4)
                .Select(i => PdfFileReader.ToDouble(reader.Resolve(rectangle[i])))]));
        }

        // Word writes its annotations in an order of its own, so both sides are sorted into
        // reading order before being lined up against each other.
        return result
            .OrderBy(r => r.Item1)
            .ThenByDescending(r => r.Item2[3])
            .ThenBy(r => r.Item2[0])
            .ToList();
    }

    [Fact]
    public void Fixture_produces_links_to_every_target()
    {
        var pdf = Converter.Convert(Fixtures.Build("hyperlinks"), Options());
        var reader = new PdfFileReader(pdf);
        var annotations = AnnotationsOf(pdf);

        Assert.Equal(5, annotations.Count);

        Assert.Equal(
            [
                "https://example.com/", "https://example.com/docs/reference?page=2#top",
                "mailto:someone@example.com", "https://example.com/"
            ],
            annotations.Select(a => UrlOf(reader, a.Annotation)).Where(u => u is not null).ToList());

        // The fourth is the internal one, which has a destination instead of an action.
        var internalLink = annotations.Single(a => UrlOf(reader, a.Annotation) is null);
        Assert.NotNull(reader.Resolve(internalLink.Annotation.Get("Dest")));
    }
}
