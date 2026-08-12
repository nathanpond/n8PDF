using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests footnotes: the mark where the reference sits, the note at the foot of that same page, and
/// the space the notes take away from the body above them.
/// </summary>
public class FootnoteTests
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

    /// <summary>The text of each line on a page, in reading order.</summary>
    private static List<string> LinesOf(LaidOutPage page) =>
        page.Lines
            .OrderBy(line => line.BaselineY)
            .Select(line => string.Concat(line.Texts.Select(t => t.Text)))
            .ToList();

    /// <summary>A document with one note, over as many body paragraphs as asked for.</summary>
    private static DocxBuilder WithOneNote(int paragraphs, string noteText = "The note.")
    {
        var builder = new DocxBuilder();
        var note = builder.AddFootnote(DocxBuilder.FootnoteBody(noteText, Times10));

        builder.AddRawParagraph(
            "<w:p>" + Run("A sentence with a note") + DocxBuilder.FootnoteReference(note) +
            Run(".") + "</w:p>");

        for (var i = 1; i <= paragraphs; i++)
            builder.AddParagraph($"Body paragraph {i}.", runProperties: Times12);

        return builder;
    }

    [Fact]
    public void Reference_draws_the_notes_number_where_it_sits()
    {
        var layout = LayoutOf(WithOneNote(0));

        // The mark joins the sentence it interrupts rather than starting a line of its own.
        Assert.Equal("A sentence with a note1.", LinesOf(layout.Pages[0])[0]);
    }

    [Fact]
    public void Mark_is_superscript_through_the_footnote_reference_style()
    {
        var layout = LayoutOf(WithOneNote(0));

        var body = layout.Pages[0].Lines[0].Texts;
        var sentence = body.First(t => t.Text.StartsWith('A'));
        var mark = body.First(t => t.Text == "1");

        Assert.True(mark.Format.BaselineShiftPoints > 0,
            "the mark sits on the text baseline rather than above it");
        Assert.True(mark.FontSizePoints < sentence.FontSizePoints,
            $"the mark is {mark.FontSizePoints}pt against the text's {sentence.FontSizePoints}pt");
    }

    [Fact]
    public void Note_lands_at_the_foot_of_the_page_that_refers_to_it()
    {
        var layout = LayoutOf(WithOneNote(0));
        var page = layout.Pages[0];

        var note = page.Lines.Single(line => line.Texts.Any(t => t.Text.Contains("The note")));
        var body = page.Lines.First();

        Assert.True(note.BaselineY > body.BaselineY + 500,
            $"the note is at {note.BaselineY:0.#}, not at the foot of the page");

        // Bottom-aligned against the bottom margin: an inch of page below the last note.
        var bottomMargin = layout.Section.PageHeightPoints - 72;
        Assert.InRange(note.BaselineY, bottomMargin - 12, bottomMargin);
    }

    [Fact]
    public void Notes_take_their_space_out_of_the_body()
    {
        // The same document with and without a note. The note runs to several lines so that what
        // it costs the page is more than the slack a page usually has left at the bottom.
        const string long_ = "A note long enough to run to several lines, so that the space it " +
                             "takes at the foot of the page is unmistakably more than the slack " +
                             "left below the last body line of a page without one.";

        var withNote = LayoutOf(WithOneNote(60, long_));

        var plain = new DocxBuilder();
        plain.AddRawParagraph("<w:p>" + Run("A sentence with a note") + Run(".") + "</w:p>");
        for (var i = 1; i <= 60; i++) plain.AddParagraph($"Body paragraph {i}.", runProperties: Times12);

        var without = LayoutOf(plain);

        // Body lines are the twelve-point ones: the notes are ten-point, and the separator's own
        // line carries no text at all.
        var body = withNote.Pages[0].Lines
            .Where(l => l.Texts.Count > 0 && l.Texts.All(t => t.FontSizePoints > 10))
            .ToList();
        var bodyWithout = without.Pages[0].Lines.Count;

        Assert.True(body.Count < bodyWithout,
            $"the page held {body.Count} body lines with a note and {bodyWithout} without");

        // And the two never meet: the body stops above the separator.
        var separator = Assert.Single(withNote.Pages[0].Rules);
        var lastBody = body.Max(l => l.BaselineY);

        Assert.True(lastBody < separator.Y,
            $"the last body line's baseline is at {lastBody:0.#}, below the separator at {separator.Y:0.#}");
    }

    [Fact]
    public void Each_page_keeps_its_own_notes()
    {
        var builder = new DocxBuilder();
        var first = builder.AddFootnote(DocxBuilder.FootnoteBody("First page note.", Times10));
        var second = builder.AddFootnote(DocxBuilder.FootnoteBody("Second page note.", Times10));

        builder.AddRawParagraph("<w:p>" + Run("Opening") + DocxBuilder.FootnoteReference(first) + "</w:p>");
        builder.AddRawParagraph(
            "<w:p><w:pPr><w:pageBreakBefore/></w:pPr>" + Run("Overleaf") +
            DocxBuilder.FootnoteReference(second) + "</w:p>");

        var layout = LayoutOf(builder);
        Assert.Equal(2, layout.Pages.Count);

        Assert.Contains(LinesOf(layout.Pages[0]), line => line.Contains("First page note"));
        Assert.DoesNotContain(LinesOf(layout.Pages[0]), line => line.Contains("Second page note"));

        Assert.Contains(LinesOf(layout.Pages[1]), line => line.Contains("Second page note"));
        Assert.DoesNotContain(LinesOf(layout.Pages[1]), line => line.Contains("First page note"));
    }

    /// <summary>
    /// A note's number is its position in the document, which is the order the references appear
    /// in — not the order the notes are stored in, and not their ids.
    /// </summary>
    [Fact]
    public void Numbers_follow_the_order_of_the_references()
    {
        var builder = new DocxBuilder();
        var a = builder.AddFootnote(DocxBuilder.FootnoteBody("Stored first.", Times10));
        var b = builder.AddFootnote(DocxBuilder.FootnoteBody("Stored second.", Times10));
        var c = builder.AddFootnote(DocxBuilder.FootnoteBody("Stored third.", Times10));

        // Referenced in the reverse of the order they are stored in.
        builder.AddRawParagraph(
            "<w:p>" + Run("One") + DocxBuilder.FootnoteReference(c) +
            Run(" two") + DocxBuilder.FootnoteReference(b) +
            Run(" three") + DocxBuilder.FootnoteReference(a) + "</w:p>");

        var lines = LinesOf(LayoutOf(builder).Pages[0]);

        Assert.Equal("One1 two2 three3", lines[0]);

        // The notes themselves come out in that same order, each opening with its own number.
        Assert.Equal("1 Stored third.", lines[^3]);
        Assert.Equal("2 Stored second.", lines[^2]);
        Assert.Equal("3 Stored first.", lines[^1]);
    }

    [Fact]
    public void Separator_is_drawn_once_on_a_page_with_notes_and_not_on_one_without()
    {
        var builder = new DocxBuilder();
        var first = builder.AddFootnote(DocxBuilder.FootnoteBody("One.", Times10));
        var second = builder.AddFootnote(DocxBuilder.FootnoteBody("Two.", Times10));

        builder.AddRawParagraph(
            "<w:p>" + Run("Text") + DocxBuilder.FootnoteReference(first) +
            Run(" and more") + DocxBuilder.FootnoteReference(second) + "</w:p>");
        builder.AddRawParagraph("<w:p><w:pPr><w:pageBreakBefore/></w:pPr>" + Run("Overleaf") + "</w:p>");

        var layout = LayoutOf(builder);

        Assert.Single(layout.Pages[0].Rules);
        Assert.Empty(layout.Pages[1].Rules);
    }

    [Fact]
    public void Reference_to_a_missing_footnote_draws_nothing()
    {
        // No footnotes part at all, so the reference resolves to nothing.
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p>" + Run("Text") +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:footnoteReference w:id=\"7\"/></w:r>" +
            Run(" continues.") + "</w:p>");

        var layout = LayoutOf(builder);

        Assert.Equal("Text continues.", LinesOf(layout.Pages[0])[0]);
        Assert.Empty(layout.Pages[0].Rules);
    }

    [Fact]
    public void Note_referenced_inside_a_table_cell_reaches_the_page()
    {
        var builder = new DocxBuilder();
        var note = builder.AddFootnote(DocxBuilder.FootnoteBody("A note from a cell.", Times10));

        builder.AddRawParagraph(
            "<w:tbl><w:tblPr><w:tblW w:w=\"5000\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/></w:tblPr>" +
            "<w:tblGrid><w:gridCol w:w=\"5000\"/></w:tblGrid>" +
            "<w:tr><w:tc><w:p>" + Run("Cell text") + DocxBuilder.FootnoteReference(note) +
            "</w:p></w:tc></w:tr></w:tbl>");

        var layout = LayoutOf(builder);
        var lines = LinesOf(layout.Pages[0]);

        Assert.Contains(lines, line => line.StartsWith("Cell text1"));
        Assert.Contains(lines, line => line.Contains("A note from a cell"));
        Assert.Single(layout.Pages[0].Rules);
    }

    /// <summary>
    /// A note taller than the page it belongs to has nowhere to go: splitting one across pages is
    /// not implemented. What must not happen is the conversion breaking page after page looking
    /// for room that will never appear.
    /// </summary>
    [Fact]
    public void Note_too_tall_for_the_page_still_converts()
    {
        var text = string.Join(" ", Enumerable.Range(1, 400).Select(i => $"sentence {i} of the note"));

        var layout = LayoutOf(WithOneNote(3, text));

        Assert.NotEmpty(layout.Pages);
        Assert.Contains(LinesOf(layout.Pages[0]), line => line.StartsWith("A sentence with a note"));
    }

    /// <summary>
    /// Compares the separator against the one Word draws. The rule is the one part of a footnote
    /// that carries no text, so nothing else in the harness can see whether it is in the right
    /// place — or there at all.
    /// </summary>
    [Theory]
    [InlineData("footnotes")]
    [InlineData("footnote-separator-probe")]
    [InlineData("endnotes")]
    [InlineData("notes-mixed")]
    public void Separator_matches_word(string name)
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, name + ".pdf");
        Assert.True(File.Exists(referencePath), $"No Word reference PDF at {referencePath}");

        var ours = SeparatorsOf(PdfPathExtractor.Extract(Converter.Convert(Fixtures.Build(name), Options())));
        var theirs = SeparatorsOf(PdfPathExtractor.ExtractFile(referencePath));

        Assert.NotEmpty(theirs);
        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            var rule = ours[i];
            var reference = theirs[i];

            Assert.Equal(reference.PageIndex, rule.PageIndex);
            Assert.Equal(reference.Left, rule.Left, 1);
            Assert.Equal(reference.Width, rule.Width, 1);
            Assert.Equal(reference.Height, rule.Height, 2);
            // Word quantizes vertical positions to 1/300 inch and rounds the separator's own
            // paragraph along with everything else: for the same construct after the same body
            // text it lands 0.24pt apart in two of these documents. Half a point admits one
            // quantum of that while still being an order of magnitude tighter than any real
            // misplacement would be. A footnote separator, which is measured from the page bottom
            // rather than down through the body, lands within 0.012pt.
            Assert.True(Math.Abs(rule.Top - reference.Top) <= 0.5,
                $"separator {i + 1} is at {rule.Top:0.###} against Word's {reference.Top:0.###}");
        }
    }

    /// <summary>
    /// The separator rules of a document: the wide, thin, black rectangles. A page also carries
    /// clipping rectangles and, in other documents, borders and shading.
    /// </summary>
    private static List<ExtractedRectangle> SeparatorsOf(IEnumerable<ExtractedRectangle> rectangles) =>
        rectangles
            .Where(r => r is { Width: > 100 and < 200, Height: > 0 and < 2 })
            .OrderBy(r => r.PageIndex)
            .ThenBy(r => r.Top)
            .ToList();
}
