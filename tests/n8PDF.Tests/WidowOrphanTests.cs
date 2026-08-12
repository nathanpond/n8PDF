using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests widow and orphan control: a paragraph is never split with only one of its lines on one
/// side of a page or column break.
/// </summary>
public class WidowOrphanTests
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

    /// <summary>
    /// A document of single-line paragraphs, then one of exactly the given number of lines. The
    /// filler decides where the page boundary falls inside that last paragraph.
    /// </summary>
    /// <remarks>
    /// The straddling paragraph's lines are separated by explicit breaks rather than by wrapping,
    /// which makes its shape exact: these tests turn on which line of it lands where, and a
    /// paragraph that wrapped to one line more than expected would prove something else.
    /// </remarks>
    private static DocxBuilder Straddling(
        int fillerLines, int paragraphLines, bool widowControl = true, string? runProperties = null)
    {
        var builder = new DocxBuilder();
        for (var i = 1; i <= fillerLines; i++)
            builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        return builder.AddRawParagraph(
            StraddlingParagraph(paragraphLines, widowControl, runProperties ?? Times12));
    }

    private static string StraddlingParagraph(int lines, bool widowControl, string runProperties)
    {
        var properties = widowControl ? ZeroSpacing : "<w:widowControl w:val=\"0\"/>" + ZeroSpacing;
        var markup = $"<w:p><w:pPr>{properties}</w:pPr>";

        for (var i = 1; i <= lines; i++)
        {
            if (i > 1) markup += $"<w:r><w:rPr>{runProperties}</w:rPr><w:br/></w:r>";
            markup += $"<w:r><w:rPr>{runProperties}</w:rPr><w:t>Straddle line {i}.</w:t></w:r>";
        }

        return markup + "</w:p>";
    }

    private static int LinesOfParagraphOn(LaidOutPage page) =>
        page.Lines.Count(l => Text(l).StartsWith("Straddle"));

    [Fact]
    public void Straddling_paragraph_has_the_expected_number_of_lines()
    {
        // The rest of these tests depend on the shape of this paragraph, so it is worth stating.
        var layout = LayoutOf(Straddling(0, 4));
        Assert.Equal(4, layout.Pages.Sum(LinesOfParagraphOn));
    }

    /// <summary>
    /// One line of a paragraph at the foot of a page is an orphan: the line goes with the rest of
    /// its paragraph instead.
    /// </summary>
    [Fact]
    public void First_line_is_not_left_alone_at_the_foot_of_a_page()
    {
        var layout = LayoutOf(Straddling(LinesPerPage - 1, 2));

        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(LinesPerPage - 1, layout.Pages[0].Lines.Count);
        Assert.Equal(0, LinesOfParagraphOn(layout.Pages[0]));
        Assert.Equal(2, LinesOfParagraphOn(layout.Pages[1]));
    }

    /// <summary>
    /// The last line alone at the top of a page is a widow: the line above it comes too, so the
    /// two arrive together.
    /// </summary>
    [Fact]
    public void Last_line_is_not_carried_alone_to_the_next_page()
    {
        // Three of the four lines would fit, leaving the fourth by itself.
        var layout = LayoutOf(Straddling(LinesPerPage - 3, 4));

        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(2, LinesOfParagraphOn(layout.Pages[0]));
        Assert.Equal(2, LinesOfParagraphOn(layout.Pages[1]));
    }

    /// <summary>
    /// Two lines on each side is four, so a three-line paragraph cannot be split at all and moves
    /// whole — which is what Word does, and what the widow-orphan fixture shows it doing.
    /// </summary>
    [Fact]
    public void Three_line_paragraph_moves_whole()
    {
        var layout = LayoutOf(Straddling(LinesPerPage - 2, 3));

        Assert.Equal(0, LinesOfParagraphOn(layout.Pages[0]));
        Assert.Equal(3, LinesOfParagraphOn(layout.Pages[1]));
        Assert.Equal(LinesPerPage - 2, layout.Pages[0].Lines.Count);
    }

    [Fact]
    public void A_paragraph_with_room_on_both_sides_splits_where_it_falls()
    {
        // Six lines fit and two do not: both sides have their two, so nothing moves.
        var layout = LayoutOf(Straddling(LinesPerPage - 6, 8));

        Assert.Equal(6, LinesOfParagraphOn(layout.Pages[0]));
        Assert.Equal(2, LinesOfParagraphOn(layout.Pages[1]));
        Assert.Equal(LinesPerPage, layout.Pages[0].Lines.Count);
    }

    [Fact]
    public void A_paragraph_that_turns_it_off_splits_as_it_falls()
    {
        var layout = LayoutOf(Straddling(LinesPerPage - 1, 2, widowControl: false));

        Assert.Equal(LinesPerPage, layout.Pages[0].Lines.Count);
        Assert.Equal(1, LinesOfParagraphOn(layout.Pages[0]));
        Assert.Equal(1, LinesOfParagraphOn(layout.Pages[1]));
    }

    /// <summary>
    /// The same rule at a column boundary, where the paragraph moves to the next column rather
    /// than the next page.
    /// </summary>
    [Fact]
    public void It_applies_to_columns_too()
    {
        var builder = new DocxBuilder().WithSection(DocxBuilder.Section(columns: 2));

        for (var i = 1; i <= LinesPerPage - 1; i++)
            builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(StraddlingParagraph(2, widowControl: true, Times12));

        var page = Assert.Single(LayoutOf(builder).Pages);

        var first = page.Lines.Where(l => l.Texts[0].X < 300).ToList();
        var second = page.Lines.Where(l => l.Texts[0].X >= 300).ToList();

        Assert.Equal(LinesPerPage - 1, first.Count);
        Assert.All(first, l => Assert.StartsWith("Filler", Text(l)));
        Assert.Equal(2, second.Count);
        Assert.StartsWith("Straddle", Text(second[0]));
    }

    /// <summary>
    /// Taking a line back off a page has to take everything placing it added: not just the text
    /// but the rules drawn under it, or the page keeps an underline with nothing beneath it.
    /// </summary>
    [Fact]
    public void A_moved_line_takes_its_underline_with_it()
    {
        var layout = LayoutOf(Straddling(
            LinesPerPage - 1, 2,
            runProperties: DocxBuilder.RunProperties(
                font: "Times New Roman", halfPoints: 24, underline: "single")));

        Assert.Empty(layout.Pages[0].Rules);
        Assert.Equal(2, layout.Pages[1].Rules.Count);
    }

    /// <summary>
    /// A footnote belongs to the page its reference ends up on, so moving the line that refers to
    /// it moves the note as well — and gives the first page back the space it had set aside.
    /// </summary>
    [Fact]
    public void A_moved_line_takes_its_footnote_with_it()
    {
        var builder = new DocxBuilder();
        var note = builder.AddFootnote(DocxBuilder.FootnoteBody("The note.",
            DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 20)));

        // Filled so that the note's own line is the last that fits — the space the note itself
        // takes at the foot is what pushes the line after it over.
        for (var i = 1; i <= LinesPerPage - 2; i++)
            builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Straddle line 1, with a note</w:t></w:r>" +
            DocxBuilder.FootnoteReference(note) +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:br/></w:r>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Straddle line 2.</w:t></w:r></w:p>");

        var layout = LayoutOf(builder);

        Assert.Equal(2, layout.Pages.Count);

        // The paragraph moved, so the note went with it: page one has neither the note nor the
        // rule that would have introduced it.
        Assert.DoesNotContain(layout.Pages[0].Lines, l => Text(l).Contains("The note"));
        Assert.Empty(layout.Pages[0].Rules);

        Assert.Contains(layout.Pages[1].Lines, l => Text(l).Contains("The note"));
        Assert.Single(layout.Pages[1].Rules);
    }

    /// <summary>
    /// A paragraph that starts a page and is taller than one cannot be moved anywhere better, so
    /// it stays and splits. Pushing it would only ask the same question of the next page.
    /// </summary>
    [Fact]
    public void A_paragraph_taller_than_the_page_is_not_pushed_forever()
    {
        var layout = LayoutOf(Straddling(0, 60));

        Assert.Equal(2, layout.Pages.Count);
        Assert.Equal(LinesPerPage, layout.Pages[0].Lines.Count);
    }
}
