using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests the two rules about what a break may not separate: <c>w:keepNext</c>, which keeps a
/// paragraph with the one after it, and <c>w:keepLines</c>, which keeps a paragraph's own lines
/// together.
/// </summary>
public class KeepTogetherTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>Forty-six of these lines fill a US Letter page's text area exactly.</summary>
    private const int LinesPerPage = 46;

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static string Text(LaidOutLine line) => string.Concat(line.Texts.Select(t => t.Text));

    private static List<string> LinesOf(LaidOutPage page) =>
        page.Lines.OrderBy(l => l.BaselineY).Select(Text).ToList();

    private static DocxBuilder Filled(int lines)
    {
        var builder = new DocxBuilder();
        for (var i = 1; i <= lines; i++)
            builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        return builder;
    }

    /// <summary>
    /// A paragraph of exactly the given number of lines, broken by hand rather than left to wrap,
    /// so that which line falls where is something the test can rely on.
    /// </summary>
    private static string Paragraph(string label, int lines, string paragraphProperties)
    {
        var markup = $"<w:p><w:pPr>{paragraphProperties}</w:pPr>";

        for (var i = 1; i <= lines; i++)
        {
            if (i > 1) markup += $"<w:r><w:rPr>{Times12}</w:rPr><w:br/></w:r>";
            markup += $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{label} {i}.</w:t></w:r>";
        }

        return markup + "</w:p>";
    }

    [Fact]
    public void A_heading_follows_the_paragraph_it_is_kept_with()
    {
        // Room for the heading and nothing after it, so its body has to start the next page.
        var layout = LayoutOf(Filled(LinesPerPage - 1)
            .AddParagraph("Heading.", "<w:keepNext/>" + ZeroSpacing, Times12)
            .AddRawParagraph(Paragraph("Body", 3, ZeroSpacing)));

        Assert.Equal(2, layout.Pages.Count);

        Assert.Equal(LinesPerPage - 1, layout.Pages[0].Lines.Count);
        Assert.DoesNotContain("Heading.", LinesOf(layout.Pages[0]));

        Assert.Equal("Heading.", LinesOf(layout.Pages[1])[0]);
        Assert.Equal("Body 1.", LinesOf(layout.Pages[1])[1]);
    }

    /// <summary>
    /// Keeping is transitive: whatever is kept with the paragraph that moved moves as well, and
    /// whatever is kept with that.
    /// </summary>
    [Fact]
    public void A_chain_of_kept_paragraphs_moves_together()
    {
        var layout = LayoutOf(Filled(LinesPerPage - 2)
            .AddParagraph("Chapter.", "<w:keepNext/>" + ZeroSpacing, Times12)
            .AddParagraph("Section.", "<w:keepNext/>" + ZeroSpacing, Times12)
            .AddRawParagraph(Paragraph("Body", 3, ZeroSpacing)));

        Assert.Equal(LinesPerPage - 2, layout.Pages[0].Lines.Count);
        Assert.Equal(["Chapter.", "Section.", "Body 1.", "Body 2.", "Body 3."], LinesOf(layout.Pages[1]));
    }

    /// <summary>
    /// A paragraph that keeps some of its lines above the break is still beside the one before it,
    /// so nothing needs to move.
    /// </summary>
    [Fact]
    public void Keeping_does_not_fire_when_the_next_paragraph_only_partly_moves()
    {
        // Two of the body's four lines fit, which widow control is content with.
        var layout = LayoutOf(Filled(LinesPerPage - 3)
            .AddParagraph("Heading.", "<w:keepNext/>" + ZeroSpacing, Times12)
            .AddRawParagraph(Paragraph("Body", 4, ZeroSpacing)));

        Assert.Equal(LinesPerPage, layout.Pages[0].Lines.Count);
        Assert.Contains("Heading.", LinesOf(layout.Pages[0]));
        Assert.Equal(["Body 3.", "Body 4."], LinesOf(layout.Pages[1]));
    }

    [Fact]
    public void Keeping_with_the_next_paragraph_does_nothing_at_the_end_of_a_document()
    {
        var layout = LayoutOf(Filled(LinesPerPage - 1)
            .AddParagraph("Last.", "<w:keepNext/>" + ZeroSpacing, Times12));

        var page = Assert.Single(layout.Pages);
        Assert.Equal("Last.", LinesOf(page)[^1]);
    }

    [Fact]
    public void A_paragraph_that_keeps_its_lines_is_not_split()
    {
        // Three of its four lines would fit, which widow control alone would have allowed.
        var layout = LayoutOf(Filled(LinesPerPage - 3)
            .AddRawParagraph(Paragraph("Kept", 4, "<w:keepLines/>" + ZeroSpacing)));

        Assert.Equal(LinesPerPage - 3, layout.Pages[0].Lines.Count);
        Assert.Equal(["Kept 1.", "Kept 2.", "Kept 3.", "Kept 4."], LinesOf(layout.Pages[1]));
    }

    /// <summary>
    /// Without the rule the same paragraph splits, which is what says the rule did the work rather
    /// than the arithmetic happening to come out that way.
    /// </summary>
    [Fact]
    public void The_same_paragraph_splits_without_the_rule()
    {
        var layout = LayoutOf(Filled(LinesPerPage - 3)
            .AddRawParagraph(Paragraph("Kept", 4, ZeroSpacing)));

        Assert.Equal(LinesPerPage - 1, layout.Pages[0].Lines.Count);
        Assert.Equal(["Kept 3.", "Kept 4."], LinesOf(layout.Pages[1]));
    }

    [Fact]
    public void Keeping_lines_together_applies_to_columns_too()
    {
        var builder = new DocxBuilder().WithSection(DocxBuilder.Section(columns: 2));
        for (var i = 1; i <= LinesPerPage - 2; i++)
            builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(Paragraph("Kept", 3, "<w:keepLines/>" + ZeroSpacing));

        var page = Assert.Single(LayoutOf(builder).Pages);

        var first = page.Lines.Where(l => l.Texts[0].X < 300).ToList();
        var second = page.Lines.Where(l => l.Texts[0].X >= 300).OrderBy(l => l.BaselineY).ToList();

        Assert.Equal(LinesPerPage - 2, first.Count);
        Assert.Equal(["Kept 1.", "Kept 2.", "Kept 3."], second.Select(Text).ToList());
    }

    /// <summary>
    /// A paragraph taller than the page cannot be kept together anywhere, so it splits where it
    /// falls rather than being pushed from page to page.
    /// </summary>
    [Fact]
    public void A_paragraph_too_tall_to_keep_together_is_split_anyway()
    {
        var layout = LayoutOf(Filled(LinesPerPage)
            .AddRawParagraph(Paragraph("Kept", LinesPerPage + 10, "<w:keepLines/>" + ZeroSpacing)));

        Assert.Equal(3, layout.Pages.Count);
        Assert.Equal(LinesPerPage, layout.Pages[1].Lines.Count);
        Assert.Equal("Kept 1.", LinesOf(layout.Pages[1])[0]);
    }

    [Fact]
    public void The_fixture_keeps_both_kinds_together()
    {
        var layout = LayoutOf(Fixtures.Build("keep-together"));

        // The heading went with its body rather than sitting alone at the foot of page one.
        Assert.DoesNotContain(LinesOf(layout.Pages[0]), l => l.StartsWith("A heading"));
        Assert.StartsWith("A heading", LinesOf(layout.Pages[1])[0]);

        // And the four-line paragraph that may not be split moved whole.
        Assert.DoesNotContain(LinesOf(layout.Pages[1]), l => l.StartsWith("Kept line"));
        Assert.Equal(
            ["Kept line 1.", "Kept line 2.", "Kept line 3.", "Kept line 4."],
            LinesOf(layout.Pages[2]));
    }

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }
}
