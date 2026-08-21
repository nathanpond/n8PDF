using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests where a section sits its text between the top and bottom margins.
/// </summary>
public class VerticalAlignmentTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>The margins of a US Letter page, which every position here is measured against.</summary>
    private const double Top = 72;

    private const double Bottom = 720;

    /// <summary>
    /// How far a line box may stand from where the arithmetic puts it: one step of the grid Word
    /// writes its baselines on. The box is stacked exactly and its baseline rounded, so a top or
    /// bottom edge — which is read back from that baseline — carries the rounding with it.
    /// </summary>
    private const double Step = 0.24;

    private static void Near(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= Step + 0.001,
            $"{what}: expected {expected:0.###}, got {actual:0.###}, which is more than a step of "
            + "the grid away");

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }

    /// <summary>A page of the given alignment holding the given number of single lines.</summary>
    private static LaidOutPage Page(string alignment, int lines = 2)
    {
        var builder = new DocxBuilder().WithSection(DocxBuilder.Section(verticalAlignment: alignment));

        for (var i = 1; i <= lines; i++)
            builder.AddParagraph($"Line {i}.", ZeroSpacing, Times12);

        return LayoutOf(builder).Pages[0];
    }

    /// <summary>Where the text on a page begins and ends, top edge to bottom edge.</summary>
    private static (double Top, double Bottom) Extent(LaidOutPage page)
    {
        var lines = page.Lines.Where(l => l.Texts.Count > 0).OrderBy(l => l.BaselineY).ToList();

        return (lines[0].BaselineY - lines[0].Ascent,
            lines[^1].BaselineY - lines[^1].Ascent + lines[^1].Height);
    }

    [Fact]
    public void Text_sits_at_the_top_unless_the_section_says_otherwise()
    {
        Near(Top, Extent(Page("top")).Top, "the top of a page aligned to the top");

        // The same page without the element at all.
        var plain = LayoutOf(new DocxBuilder().AddParagraph("Line 1.", ZeroSpacing, Times12)).Pages[0];
        Near(Top, Extent(plain).Top, "the top of a page with no alignment at all");
    }

    [Fact]
    public void Centred_text_has_as_much_below_it_as_above()
    {
        var (top, bottom) = Extent(Page("center"));

        Near(top - Top, Bottom - bottom, "the room above against the room below");
        Assert.True(top > Top + 100, $"the text starts at {top:0.#}, which is hardly centred");
    }

    [Fact]
    public void Text_aligned_to_the_bottom_ends_on_the_bottom_margin()
    {
        var (_, bottom) = Extent(Page("bottom"));
        Assert.Equal(Bottom, bottom, 1);
    }

    /// <summary>
    /// Justified alignment spreads the spare height between the paragraphs: the first stays where
    /// it was and the last ends on the bottom margin, with equal gaps in between.
    /// </summary>
    [Fact]
    public void Justified_text_spreads_its_paragraphs_down_the_page()
    {
        var page = Page("both", lines: 4);
        var lines = page.Lines.OrderBy(l => l.BaselineY).ToList();

        Near(Top, Extent(page).Top, "the top of the justified page");
        Assert.Equal(Bottom, Extent(page).Bottom, 1);

        var gaps = lines.Zip(lines.Skip(1), (a, b) => b.BaselineY - a.BaselineY).ToList();

        Assert.All(gaps, gap => Near(gaps[0], gap, "one gap against the first"));
    }

    /// <summary>
    /// The gaps go between paragraphs, not between lines, so a paragraph that wraps stays whole.
    /// </summary>
    [Fact]
    public void Justified_text_does_not_open_a_paragraph_up()
    {
        var builder = new DocxBuilder()
            .WithSection(DocxBuilder.Section(verticalAlignment: "both"))
            .AddParagraph(
                "A paragraph written at enough length that it has to wrap onto a second line of " +
                "the page it is set on, rather than fitting across the measure just once.",
                ZeroSpacing, Times12)
            .AddParagraph("A short one.", ZeroSpacing, Times12);

        var page = LayoutOf(builder).Pages[0];
        var lines = page.Lines.OrderBy(l => l.BaselineY).ToList();

        Assert.Equal(3, lines.Count);

        // The two lines of the first paragraph stay a line apart; the gap falls after them.
        Near(lines[0].Height, lines[1].BaselineY - lines[0].BaselineY, "the gap inside the paragraph");
        Assert.True(lines[2].BaselineY - lines[1].BaselineY > 100);
    }

    /// <summary>
    /// A page only moves by what it has spare, so a full one barely moves at all — which is why a
    /// long section aligned to the bottom looks much the same until its last page.
    /// </summary>
    [Fact]
    public void A_page_moves_only_by_what_it_has_spare()
    {
        var builder = new DocxBuilder().WithSection(DocxBuilder.Section(verticalAlignment: "bottom"));
        for (var i = 1; i <= 60; i++) builder.AddParagraph($"Line {i}.", ZeroSpacing, Times12);

        var layout = LayoutOf(builder);
        Assert.Equal(2, layout.Pages.Count);

        // The first page holds all it can, so what is left over is less than the line that would
        // not fit; the last holds fourteen lines and moves most of the page.
        var full = Extent(layout.Pages[0]);
        Assert.InRange(full.Top - Top, 0, 14);
        Assert.Equal(Bottom, full.Bottom, 1);

        Assert.Equal(Bottom, Extent(layout.Pages[1]).Bottom, 1);
        Assert.True(Extent(layout.Pages[1]).Top > 500, "the last page hardly moved");
    }

    /// <summary>
    /// Footnotes belong to the page rather than to the text, so they stay at its foot however the
    /// text above them is placed — and the text is centred in what they leave.
    /// </summary>
    [Fact]
    public void Footnotes_stay_at_the_foot_of_a_centred_page()
    {
        var builder = new DocxBuilder().WithSection(DocxBuilder.Section(verticalAlignment: "center"));

        var note = builder.AddFootnote(DocxBuilder.FootnoteBody("The note.",
            DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 20)));

        builder.AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Text</w:t></w:r>" +
            DocxBuilder.FootnoteReference(note) + "</w:p>");

        var page = LayoutOf(builder).Pages[0];

        var body = page.Lines.Single(l => l.Texts.Any(t => t.Text == "Text"));
        var footnote = page.Lines.Single(l => l.Texts.Any(t => t.Text.Contains("The note")));

        Assert.InRange(body.BaselineY, 300, 400);
        Assert.InRange(footnote.BaselineY, Bottom - 12, Bottom);
    }

    /// <summary>
    /// A page takes the alignment of its own section, not of the one that follows it. The two are
    /// only told apart by finishing a page before the next section's margins take over.
    /// </summary>
    [Fact]
    public void Each_section_places_its_own_pages()
    {
        var builder = new DocxBuilder()
            .AddParagraphWithSectionBreak(
                "Top.", DocxBuilder.Section(), ZeroSpacing, Times12)
            .AddParagraph("Bottom.", ZeroSpacing, Times12)
            .WithSection(DocxBuilder.Section(verticalAlignment: "bottom"));

        var layout = LayoutOf(builder);

        Near(Top, Extent(layout.Pages[0]).Top, "the top of the first page");
        Assert.Equal(Bottom, Extent(layout.Pages[1]).Bottom, 1);
    }

    [Fact]
    public void The_fixture_places_each_of_its_four_pages()
    {
        var layout = LayoutOf(Fixtures.Build("vertical-alignment"));

        Assert.Equal(4, layout.Pages.Count);

        Near(Top, Extent(layout.Pages[0]).Top, "the top of the fixture's first page");

        var centred = Extent(layout.Pages[1]);
        Near(centred.Top - Top, Bottom - centred.Bottom, "the centred page's room above and below");

        Assert.Equal(Bottom, Extent(layout.Pages[2]).Bottom, 1);

        var justified = Extent(layout.Pages[3]);
        Near(Top, justified.Top, "the top of the justified page");
        Assert.Equal(Bottom, justified.Bottom, 1);
    }
}
