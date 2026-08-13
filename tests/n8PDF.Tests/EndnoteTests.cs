using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests endnotes: the same machinery as footnotes with a different destination — the notes
/// collect at the end of the document rather than at the foot of a page — and roman numerals
/// rather than arabic ones.
/// </summary>
public class EndnoteTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string Times10 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"20\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static string Run(string text) =>
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    private static List<string> LinesOf(LaidOutPage page) =>
        page.Lines
            .OrderBy(line => line.BaselineY)
            .Select(line => string.Concat(line.Texts.Select(t => t.Text)))
            .ToList();

    [Fact]
    public void Marks_are_lower_case_roman_numerals()
    {
        var builder = new DocxBuilder();
        var first = builder.AddEndnote(DocxBuilder.EndnoteBody("One.", Times10));
        var second = builder.AddEndnote(DocxBuilder.EndnoteBody("Two.", Times10));
        var third = builder.AddEndnote(DocxBuilder.EndnoteBody("Three.", Times10));

        builder.AddRawParagraph(
            "<w:p>" + Run("A") + DocxBuilder.EndnoteReference(first) +
            Run(" B") + DocxBuilder.EndnoteReference(second) +
            Run(" C") + DocxBuilder.EndnoteReference(third) + "</w:p>");

        Assert.Equal("Ai Bii Ciii", LinesOf(LayoutOf(builder).Pages[0])[0]);
    }

    /// <summary>
    /// Endnotes are not an area of the page: they carry straight on from the last body paragraph,
    /// which is what Word does and what the endnotes fixture shows in its export.
    /// </summary>
    [Fact]
    public void Notes_follow_the_body_rather_than_sitting_at_the_page_foot()
    {
        var builder = new DocxBuilder();
        var note = builder.AddEndnote(DocxBuilder.EndnoteBody("The note.", Times10));

        builder.AddRawParagraph("<w:p>" + Run("A short document") + DocxBuilder.EndnoteReference(note) + "</w:p>");

        var page = LayoutOf(builder).Pages[0];
        var body = page.Lines.First();
        var text = page.Lines.Single(l => l.Texts.Any(t => t.Text.Contains("The note")));

        // Two lines below the body, not seven hundred points below it.
        Assert.InRange(text.BaselineY - body.BaselineY, 10, 60);
    }

    [Fact]
    public void Notes_come_out_in_the_order_they_were_referenced()
    {
        var builder = new DocxBuilder();
        var stored = builder.AddEndnote(DocxBuilder.EndnoteBody("Stored first.", Times10));
        var later = builder.AddEndnote(DocxBuilder.EndnoteBody("Stored second.", Times10));

        builder.AddRawParagraph(
            "<w:p>" + Run("A") + DocxBuilder.EndnoteReference(later) +
            Run(" B") + DocxBuilder.EndnoteReference(stored) + "</w:p>");

        var lines = LinesOf(LayoutOf(builder).Pages[0]);

        Assert.Equal("i Stored second.", lines[^2]);
        Assert.Equal("ii Stored first.", lines[^1]);
    }

    [Fact]
    public void Separator_is_drawn_above_the_notes()
    {
        var builder = new DocxBuilder();
        var note = builder.AddEndnote(DocxBuilder.EndnoteBody("The note.", Times10));
        builder.AddRawParagraph("<w:p>" + Run("Text") + DocxBuilder.EndnoteReference(note) + "</w:p>");

        var page = LayoutOf(builder).Pages[0];

        var rule = Assert.Single(page.Rules);
        var text = page.Lines.Single(l => l.Texts.Any(t => t.Text.Contains("The note")));

        Assert.Equal(144, rule.Width, 2);
        Assert.True(rule.Y < text.BaselineY, "the rule is below the note it introduces");
    }

    /// <summary>
    /// The two kinds do not interfere: a footnote still goes to the foot of its own page while the
    /// endnotes collect after the body.
    /// </summary>
    [Fact]
    public void Footnotes_and_endnotes_coexist()
    {
        var builder = new DocxBuilder();
        var foot = builder.AddFootnote(DocxBuilder.FootnoteBody("At the foot.", Times10));
        var end = builder.AddEndnote(DocxBuilder.EndnoteBody("At the end.", Times10));

        builder.AddRawParagraph(
            "<w:p>" + Run("Text") + DocxBuilder.FootnoteReference(foot) +
            Run(" more") + DocxBuilder.EndnoteReference(end) + "</w:p>");

        var page = LayoutOf(builder).Pages[0];

        // Each kind counts from one in its own sequence, and in its own numerals.
        Assert.Equal("Text1 morei", LinesOf(page)[0]);

        var footnote = page.Lines.Single(l => l.Texts.Any(t => t.Text.Contains("At the foot")));
        var endnote = page.Lines.Single(l => l.Texts.Any(t => t.Text.Contains("At the end")));

        Assert.True(endnote.BaselineY < footnote.BaselineY,
            $"the endnote is at {endnote.BaselineY:0.#} and the footnote at {footnote.BaselineY:0.#}");

        // One rule for each: the footnote area's and the endnotes'.
        Assert.Equal(2, page.Rules.Count);
    }

    [Fact]
    public void Section_can_ask_for_a_different_number_format()
    {
        var builder = new DocxBuilder().WithSection("""
            <w:sectPr>
              <w:endnotePr><w:numFmt w:val="decimal"/></w:endnotePr>
              <w:pgSz w:w="12240" w:h="15840"/>
              <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440" w:header="720" w:footer="720" w:gutter="0"/>
            </w:sectPr>
            """);

        var note = builder.AddEndnote(DocxBuilder.EndnoteBody("The note.", Times10));
        builder.AddRawParagraph("<w:p>" + Run("Text") + DocxBuilder.EndnoteReference(note) + "</w:p>");

        var lines = LinesOf(LayoutOf(builder).Pages[0]);

        Assert.Equal("Text1", lines[0]);
        Assert.Equal("1 The note.", lines[^1]);
    }

    // ----- gathered by section rather than by document -----

    /// <summary>
    /// A document of two sections, each with a note, and a page break between them.
    /// </summary>
    private static DocxBuilder TwoSections(bool bySection, bool inTheSectionsOnly = false)
    {
        var position = bySection ? "sectEnd" : null;

        var builder = new DocxBuilder()
            .WithSection(DocxBuilder.Section(endnotePosition: position));

        if (bySection && !inTheSectionsOnly) builder.WithEndnotePosition("sectEnd");

        var first = builder.AddEndnote(DocxBuilder.EndnoteBody("The first section's note.", Times10));

        builder.AddRawParagraph(
            "<w:p>" + Run("A paragraph of the first section") +
            DocxBuilder.EndnoteReference(first) + Run(".") + "</w:p>");

        builder.AddParagraphWithSectionBreak(
            "The last paragraph of the first section.",
            DocxBuilder.Section(type: "nextPage", endnotePosition: position));

        var second = builder.AddEndnote(DocxBuilder.EndnoteBody("The second section's note.", Times10));

        return builder.AddRawParagraph(
            "<w:p>" + Run("A paragraph of the second section") +
            DocxBuilder.EndnoteReference(second) + Run(".") + "</w:p>");
    }

    /// <summary>
    /// A document may gather each section's endnotes at the end of that section rather than all of
    /// them at the end of itself, which is what a book of chapters does with them.
    /// </summary>
    [Fact]
    public void Notes_are_gathered_at_the_end_of_each_section_where_the_document_asks()
    {
        var layout = LayoutOf(TwoSections(bySection: true));

        Assert.Equal(2, layout.Pages.Count);

        var first = LinesOf(layout.Pages[0]);
        var second = LinesOf(layout.Pages[1]);

        // Each section's note is on the section's own page, after its last paragraph.
        Assert.Contains(first, line => line.Contains("The first section's note."));
        Assert.DoesNotContain(first, line => line.Contains("The second section's note."));

        Assert.Contains(second, line => line.Contains("The second section's note."));
        Assert.DoesNotContain(second, line => line.Contains("The first section's note."));

        // And the first section's note comes after the text of that section, not before it.
        Assert.True(
            first.FindIndex(line => line.Contains("The first section's note.")) >
            first.FindIndex(line => line.StartsWith("The last paragraph")),
            "the note was written before the text it belongs after");
    }

    /// <summary>And a document that says nothing gathers all of them at its own end.</summary>
    [Fact]
    public void Notes_are_gathered_at_the_end_of_the_document_by_default()
    {
        var layout = LayoutOf(TwoSections(bySection: false));

        var last = LinesOf(layout.Pages[^1]);

        Assert.Contains(last, line => line.Contains("The first section's note."));
        Assert.Contains(last, line => line.Contains("The second section's note."));
    }

    /// <summary>
    /// Word reads this from the settings part and not from the section, which is the other way
    /// round from everything else about how a note is set. A document stating it in its sections
    /// alone is gathered at the end regardless — measured, not assumed: Word's export of a
    /// document written that way has all its notes at the end, and Word's own writer puts the
    /// setting in both places.
    /// </summary>
    [Fact]
    public void A_position_stated_only_in_the_sections_is_ignored_the_way_word_ignores_it()
    {
        var layout = LayoutOf(TwoSections(bySection: true, inTheSectionsOnly: true));

        var last = LinesOf(layout.Pages[^1]);

        Assert.Contains(last, line => line.Contains("The first section's note."));
        Assert.Contains(last, line => line.Contains("The second section's note."));
    }

    [Fact]
    public void Reference_to_a_missing_endnote_draws_nothing()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p>" + Run("Text") +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:endnoteReference w:id=\"9\"/></w:r>" +
            Run(" continues.") + "</w:p>");

        var layout = LayoutOf(builder);

        Assert.Equal("Text continues.", LinesOf(layout.Pages[0])[0]);
        Assert.Empty(layout.Pages[0].Rules);
    }

    /// <summary>
    /// Endnotes are ordinary flow content, so they break to a new page when the body has left no
    /// room for them rather than piling up past the bottom margin.
    /// </summary>
    [Fact]
    public void Notes_move_to_a_new_page_when_the_body_fills_the_last_one()
    {
        var builder = new DocxBuilder();
        var note = builder.AddEndnote(DocxBuilder.EndnoteBody("The note.", Times10));

        builder.AddRawParagraph("<w:p>" + Run("Opening") + DocxBuilder.EndnoteReference(note) + "</w:p>");

        // Enough to reach the very bottom of the second page.
        for (var i = 1; i <= 63; i++)
            builder.AddParagraph($"Body paragraph {i}.", runProperties: Times12);

        var layout = LayoutOf(builder);
        var last = layout.Pages[^1];

        var note_ = last.Lines.Single(l => l.Texts.Any(t => t.Text.Contains("The note")));
        var bottomMargin = layout.Section.PageHeightPoints - 72;

        Assert.True(note_.BaselineY < bottomMargin,
            $"the note runs past the bottom margin, at {note_.BaselineY:0.#}");
    }
}
