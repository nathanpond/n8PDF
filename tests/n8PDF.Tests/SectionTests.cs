using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests section breaks: a document whose page size, orientation or margins change part-way
/// through, and which running heads each of its pages takes.
/// </summary>
public class SectionTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static string TextOf(LaidOutPage page) =>
        string.Join(" | ", page.Lines.OrderBy(l => l.BaselineY)
            .Select(l => string.Concat(l.Texts.Select(t => t.Text))));

    /// <summary>Landscape US Letter, half-inch margins.</summary>
    private static string Landscape(string? type = null, int left = 720) =>
        DocxBuilder.Section(
            type: type, widthTwips: 15840, heightTwips: 12240, landscape: true,
            top: 720, right: 720, bottom: 720, left: left);

    [Fact]
    public void Next_page_break_starts_a_page_of_the_new_size()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak("Portrait.", DocxBuilder.Section(), ZeroSpacing, Times12)
            .AddParagraph("Landscape.", ZeroSpacing, Times12)
            .WithSection(Landscape()));

        Assert.Equal(2, layout.Pages.Count);

        Assert.Equal(612, layout.Pages[0].WidthPoints, 1);
        Assert.Equal(792, layout.Pages[0].HeightPoints, 1);
        Assert.Equal("Portrait.", TextOf(layout.Pages[0]));

        Assert.Equal(792, layout.Pages[1].WidthPoints, 1);
        Assert.Equal(612, layout.Pages[1].HeightPoints, 1);
        Assert.Equal("Landscape.", TextOf(layout.Pages[1]));
    }

    [Fact]
    public void New_section_uses_its_own_margins()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak("One inch in.", DocxBuilder.Section(), ZeroSpacing, Times12)
            .AddParagraph("Half an inch in.", ZeroSpacing, Times12)
            .WithSection(Landscape()));

        Assert.Equal(72, layout.Pages[0].Lines[0].Texts[0].X, 2);
        Assert.Equal(36, layout.Pages[1].Lines[0].Texts[0].X, 2);

        // And the top margin too: the second page starts higher up.
        Assert.True(layout.Pages[1].Lines[0].BaselineY < layout.Pages[0].Lines[0].BaselineY);
    }

    /// <summary>
    /// A continuous break carries on down the same page under the new margins, which is the only
    /// way a document can change its measure without changing the paper.
    /// </summary>
    [Fact]
    public void Continuous_break_changes_the_margins_on_the_same_page()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak(
                "Narrow margin.",
                DocxBuilder.Section(type: "continuous"),
                ZeroSpacing, Times12)
            .AddParagraph("Wide margin.", ZeroSpacing, Times12)
            .WithSection(DocxBuilder.Section(type: "continuous", left: 2880)));

        var page = Assert.Single(layout.Pages);
        var lines = page.Lines.OrderBy(l => l.BaselineY).ToList();

        Assert.Equal(72, lines[0].Texts[0].X, 2);
        Assert.Equal(144, lines[1].Texts[0].X, 2);

        // Straight after it, not on a page of its own.
        Assert.True(lines[1].BaselineY - lines[0].BaselineY < 20);
    }

    /// <summary>
    /// Word cannot honour a continuous break that changes the paper — two page sizes cannot share
    /// one sheet — so such a section starts a new page however it was declared.
    /// </summary>
    [Fact]
    public void Continuous_break_onto_different_paper_starts_a_new_page_anyway()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak(
                "Portrait.", DocxBuilder.Section(type: "continuous"), ZeroSpacing, Times12)
            .AddParagraph("Landscape.", ZeroSpacing, Times12)
            .WithSection(Landscape(type: "continuous")));

        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(792, layout.Pages[1].WidthPoints, 1);
    }

    [Fact]
    public void Even_page_break_leaves_a_blank_page_when_it_has_to()
    {
        // One page of content, so the next page is page two — already even, no blank needed.
        var noBlank = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak("First.", DocxBuilder.Section(), ZeroSpacing, Times12)
            .AddParagraph("Second.", ZeroSpacing, Times12)
            .WithSection(DocxBuilder.Section(type: "evenPage")));

        Assert.Equal(2, noBlank.Pages.Count);
        Assert.NotEmpty(noBlank.Pages[1].Lines);

        // Two pages of content, so the next page is three — a blank one is left behind.
        var blank = LayoutOf(new DocxBuilder()
            .AddParagraph("First.", ZeroSpacing, Times12)
            .AddParagraphWithSectionBreak(
                "Second.", DocxBuilder.Section(), "<w:pageBreakBefore/>" + ZeroSpacing, Times12)
            .AddParagraph("Third.", ZeroSpacing, Times12)
            .WithSection(DocxBuilder.Section(type: "evenPage")));

        Assert.Equal(4, blank.Pages.Count);
        Assert.Empty(blank.Pages[2].Lines);
        Assert.Equal("Third.", TextOf(blank.Pages[3]));
    }

    [Fact]
    public void Odd_page_break_leaves_a_blank_page_when_it_has_to()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak("First.", DocxBuilder.Section(), ZeroSpacing, Times12)
            .AddParagraph("Second.", ZeroSpacing, Times12)
            .WithSection(DocxBuilder.Section(type: "oddPage")));

        // The next page would be page two, so the section waits for page three.
        Assert.Equal(3, layout.Pages.Count);
        Assert.Empty(layout.Pages[1].Lines);
        Assert.Equal("Second.", TextOf(layout.Pages[2]));
    }

    [Fact]
    public void Pages_carry_the_section_they_belong_to()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddParagraphWithSectionBreak("Portrait.", DocxBuilder.Section(), ZeroSpacing, Times12)
            .AddParagraph("Landscape.", ZeroSpacing, Times12)
            .WithSection(Landscape()));

        Assert.Equal(12240, layout.Pages[0].Section.PageWidthTwips);
        Assert.Equal(15840, layout.Pages[1].Section.PageWidthTwips);

        // Each section counts its own pages, which is what a title page is measured against.
        Assert.Equal(0, layout.Pages[0].IndexInSection);
        Assert.Equal(0, layout.Pages[1].IndexInSection);
    }

    /// <summary>
    /// A section that states no running heads keeps the previous section's, which is what Word's
    /// "link to previous" means — and it writes nothing at all in that case, so a converter that
    /// took the absence literally would drop the header half way through the document.
    /// </summary>
    [Fact]
    public void Section_without_its_own_header_inherits_the_previous_one()
    {
        var builder = new DocxBuilder()
            .WithHeaderFooter(header: true,
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
                "<w:t>Running head</w:t></w:r></w:p>",
                referenceFromFinalSection: false);

        builder
            .AddParagraphWithSectionBreak(
                "Portrait.",
                DocxBuilder.Section(headerFooterReferences: [("header:default", "rIdHF1")]),
                ZeroSpacing, Times12)
            .AddParagraph("Landscape.", ZeroSpacing, Times12)
            .WithSection(Landscape());

        var layout = LayoutOf(builder);

        Assert.All(layout.Pages, page =>
            Assert.Contains(page.Lines, l => l.Texts.Any(t => t.Text.Contains("Running head"))));
    }

    /// <summary>
    /// Linking is per kind. A section that unlinks only its first-page header keeps the previous
    /// section's for every other page, which is what Word writes when only one of the two was
    /// changed.
    /// </summary>
    [Fact]
    public void Header_inheritance_is_per_kind()
    {
        var builder = new DocxBuilder()
            // The opening section owns the default head; the closing one never mentions it.
            .WithHeaderFooter(header: true,
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Inherited head</w:t></w:r></w:p>",
                referenceFromFinalSection: false)
            // The closing section declares this one, and nothing else.
            .WithHeaderFooter(header: true,
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Own first head</w:t></w:r></w:p>",
                kind: "first")
            .WithTitlePage();

        builder
            .AddParagraphWithSectionBreak(
                "One.",
                DocxBuilder.Section(headerFooterReferences: [("header:default", "rIdHF1")]),
                ZeroSpacing, Times12)
            .AddParagraph("Two.", ZeroSpacing, Times12)
            .AddParagraph("Three.", "<w:pageBreakBefore/>" + ZeroSpacing, Times12);

        var layout = LayoutOf(builder);

        // Page one is in the section that owns the default head.
        Assert.Contains(layout.Pages[0].Lines, l => l.Texts.Any(t => t.Text.Contains("Inherited head")));

        // Page two opens the closing section, which declares a first-page head of its own.
        Assert.Contains(layout.Pages[1].Lines, l => l.Texts.Any(t => t.Text.Contains("Own first head")));

        // Page three is that same section's second page. It said nothing about a default head, so
        // it keeps the one it inherited rather than going bare.
        Assert.Contains(layout.Pages[2].Lines, l => l.Texts.Any(t => t.Text.Contains("Inherited head")));
    }

    /// <summary>
    /// A title page is the first page of the section that asks for one, not the first page of the
    /// document — so the second section's opening page takes the first-page header while the
    /// document's own opening page, in a section that asked for nothing, takes the default.
    /// </summary>
    [Fact]
    public void Title_page_is_the_first_page_of_its_own_section()
    {
        var builder = new DocxBuilder()
            .WithHeaderFooter(header: true,
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Default head</w:t></w:r></w:p>",
                referenceFromFinalSection: false)
            .WithHeaderFooter(header: true,
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>First head</w:t></w:r></w:p>",
                kind: "first", referenceFromFinalSection: false)
            .WithTitlePage();

        builder
            .AddParagraphWithSectionBreak(
                "One.",
                DocxBuilder.Section(headerFooterReferences:
                    [("header:default", "rIdHF1"), ("header:first", "rIdHF2")]),
                ZeroSpacing, Times12)
            .AddParagraph("Two.", ZeroSpacing, Times12);

        var layout = LayoutOf(builder);

        // The opening section asks for no title page, so its first page takes the default head.
        Assert.Contains(layout.Pages[0].Lines, l => l.Texts.Any(t => t.Text.Contains("Default head")));

        // The closing section inherits both parts and does ask, so its own first page — the
        // document's second — takes the first-page head.
        Assert.Contains(layout.Pages[1].Lines, l => l.Texts.Any(t => t.Text.Contains("First head")));
    }

    [Fact]
    public void Page_sizes_reach_the_pdf()
    {
        var pdf = Converter.Convert(Fixtures.Build("sections"), Options());
        var pages = new PdfFileReader(pdf).GetPages();

        Assert.Equal(4, pages.Count);

        Assert.Equal(612, pages[0].Width, 1);
        Assert.Equal(792, pages[1].Width, 1);
        Assert.Equal(612, pages[2].Width, 1);
        Assert.Equal(612, pages[3].Width, 1);
    }

    [Fact]
    public void Document_without_any_break_is_one_section()
    {
        var layout = LayoutOf(new DocxBuilder().AddParagraph("Alone.", ZeroSpacing, Times12));

        var page = Assert.Single(layout.Pages);
        Assert.Equal(612, page.WidthPoints, 1);
        Assert.Equal("Alone.", TextOf(page));
    }
}
