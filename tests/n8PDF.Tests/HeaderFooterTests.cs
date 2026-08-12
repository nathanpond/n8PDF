using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests headers, footers and the fields they usually carry.
/// </summary>
public class HeaderFooterTests
{
    private const string Times12 = "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static string Paragraph(string text) =>
        $"<w:p><w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r></w:p>";

    /// <summary>
    /// The topmost line's text. Headers are laid out after the body — the page count has to be
    /// known first — so they are last in the list even though they are first on the page.
    /// </summary>
    private static string TopLineOf(LaidOutPage page) =>
        string.Concat(page.Lines.Where(l => l.Texts.Count > 0)
            .OrderBy(l => l.BaselineY).First().Texts.Select(t => t.Text));

    private static LaidOutLine TopOf(LaidOutPage page) =>
        page.Lines.Where(l => l.Texts.Count > 0).OrderBy(l => l.BaselineY).First();

    private static LaidOutLine BottomOf(LaidOutPage page) =>
        page.Lines.Where(l => l.Texts.Count > 0).OrderByDescending(l => l.BaselineY).First();

    private static List<string> TextsOn(LaidOutPage page) =>
        page.Lines.Where(l => l.Texts.Count > 0)
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)))
            .ToList();

    private static DocxBuilder ManyPages(int paragraphs = 120)
    {
        var builder = new DocxBuilder();
        for (var i = 1; i <= paragraphs; i++)
            builder.AddParagraph($"Body paragraph {i}.", runProperties: Times12);

        return builder;
    }

    [Fact]
    public void A_header_appears_on_every_page_above_the_body()
    {
        var document = LayoutOf(ManyPages().WithHeaderFooter(header: true, Paragraph("Running header")));

        Assert.True(document.Pages.Count > 1);

        foreach (var page in document.Pages)
        {
            var header = TopOf(page);
            Assert.Equal("Running header", string.Concat(header.Texts.Select(t => t.Text)));

            // It sits in the top margin, above where the body is allowed to start.
            Assert.True(header.BaselineY < 72,
                $"header baseline {header.BaselineY:0.##} should be inside the top margin");
        }
    }

    [Fact]
    public void A_footer_sits_in_the_bottom_margin()
    {
        var document = LayoutOf(ManyPages().WithHeaderFooter(header: false, Paragraph("Running footer")));

        foreach (var page in document.Pages)
        {
            var footer = BottomOf(page);
            Assert.Equal("Running footer", string.Concat(footer.Texts.Select(t => t.Text)));

            // Below the body's bottom margin, and still on the page.
            Assert.True(footer.BaselineY > page.HeightPoints - 72,
                $"footer baseline {footer.BaselineY:0.##} should be below the bottom margin");
            Assert.True(footer.BaselineY < page.HeightPoints);
        }
    }

    [Fact]
    public void A_page_field_counts_up_through_the_document()
    {
        var document = LayoutOf(ManyPages()
            .WithHeaderFooter(header: false, DocxBuilder.FieldParagraph(" PAGE ", "1", Times12)));

        Assert.True(document.Pages.Count >= 3);

        for (var i = 0; i < document.Pages.Count; i++)
        {
            // The cached value says 1 on every page; only evaluating the field gives 1, 2, 3.
            Assert.Equal((i + 1).ToString(),
                string.Concat(BottomOf(document.Pages[i]).Texts.Select(t => t.Text)));
        }
    }

    [Fact]
    public void A_numpages_field_knows_the_total()
    {
        var document = LayoutOf(ManyPages()
            .WithHeaderFooter(header: false, DocxBuilder.FieldParagraph(" NUMPAGES ", "1", Times12)));

        var total = document.Pages.Count.ToString();

        foreach (var page in document.Pages)
        {
            Assert.Equal(total, string.Concat(BottomOf(page).Texts.Select(t => t.Text)));
        }
    }

    [Fact]
    public void An_unrecognised_field_falls_back_to_what_word_computed()
    {
        var document = LayoutOf(new DocxBuilder()
            .AddRawParagraph(DocxBuilder.FieldParagraph(" AUTHOR ", "A. Writer", Times12)));

        Assert.Contains("A. Writer", TextsOn(document.Pages[0]));
    }

    [Fact]
    public void A_complex_field_keeps_both_its_instruction_and_its_result()
    {
        // The begin/separate/end form: the instruction is in one run and the cached value in
        // another. Reading it as ordinary runs would show the value but lose the instruction, so
        // the page number could never be recomputed.
        var document = LayoutOf(ManyPages().WithHeaderFooter(header: false, """
            <w:p>
              <w:r><w:fldChar w:fldCharType="begin"/></w:r>
              <w:r><w:instrText xml:space="preserve"> PAGE </w:instrText></w:r>
              <w:r><w:fldChar w:fldCharType="separate"/></w:r>
              <w:r><w:t>1</w:t></w:r>
              <w:r><w:fldChar w:fldCharType="end"/></w:r>
            </w:p>
            """));

        Assert.Equal("2", string.Concat(BottomOf(document.Pages[1]).Texts.Select(t => t.Text)));
    }

    [Fact]
    public void A_title_page_takes_its_own_header()
    {
        var document = LayoutOf(ManyPages()
            .WithHeaderFooter(header: true, Paragraph("First page header"), kind: "first")
            .WithHeaderFooter(header: true, Paragraph("Later header"))
            .WithTitlePage());

        Assert.Equal("First page header", TopLineOf(document.Pages[0]));
        Assert.Equal("Later header", TopLineOf(document.Pages[1]));
    }

    [Fact]
    public void Even_pages_take_their_own_header_when_the_document_asks()
    {
        var document = LayoutOf(ManyPages()
            .WithHeaderFooter(header: true, Paragraph("Odd header"))
            .WithHeaderFooter(header: true, Paragraph("Even header"), kind: "even")
            .WithEvenAndOddHeaders());

        Assert.True(document.Pages.Count >= 3);

        Assert.Equal("Odd header", TopLineOf(document.Pages[0]));
        Assert.Equal("Even header", TopLineOf(document.Pages[1]));
        Assert.Equal("Odd header", TopLineOf(document.Pages[2]));
    }

    [Fact]
    public void Without_the_setting_every_page_uses_the_default_header()
    {
        // The even header is declared but not enabled, so it must not be used.
        var document = LayoutOf(ManyPages()
            .WithHeaderFooter(header: true, Paragraph("Only header"))
            .WithHeaderFooter(header: true, Paragraph("Unused even header"), kind: "even"));

        foreach (var page in document.Pages)
        {
            Assert.Equal("Only header", TopLineOf(page));
        }
    }

    [Fact]
    public void The_body_is_unaffected_by_the_header()
    {
        var withHeader = LayoutOf(ManyPages(20).WithHeaderFooter(header: true, Paragraph("Header")));
        var without = LayoutOf(ManyPages(20));

        // A header lives in the margin, so the body's first baseline is where it always was.
        var bodyLine = withHeader.Pages[0].Lines.First(l =>
            string.Concat(l.Texts.Select(t => t.Text)).StartsWith("Body", StringComparison.Ordinal));

        Assert.Equal(without.Pages[0].Lines[0].BaselineY, bodyLine.BaselineY, 2);
    }
}
