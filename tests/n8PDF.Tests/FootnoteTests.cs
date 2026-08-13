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

    // ----- a note too long for the page it belongs to -----

    /// <summary>A note of as many numbered lines as asked for, each its own paragraph.</summary>
    private static string LongNote(int lines) =>
        string.Join("", Enumerable.Range(1, lines).Select(i =>
            "<w:p><w:pPr><w:pStyle w:val=\"FootnoteText\"/>" +
            "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>" +
            (i == 1 ? "<w:r><w:rPr><w:rStyle w:val=\"FootnoteReference\"/></w:rPr><w:footnoteRef/></w:r>" : "") +
            $"<w:r><w:rPr>{Times10}</w:rPr><w:t xml:space=\"preserve\">" +
            $"Line {i} of a note far too long for the foot of one page." +
            "</w:t></w:r></w:p>"));

    /// <summary>A document whose note is too long for the page its reference falls on.</summary>
    private static DocxBuilder WithLongNote(int noteLines, int paragraphs, int referenceAt)
    {
        var builder = new DocxBuilder();
        var note = builder.AddFootnote(LongNote(noteLines));

        const string spacing = "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

        for (var i = 1; i <= paragraphs; i++)
        {
            if (i == referenceAt)
            {
                builder.AddRawParagraph(
                    $"<w:p><w:pPr>{spacing}</w:pPr>" +
                    Run($"Body paragraph {i}, which carries the long note") +
                    DocxBuilder.FootnoteReference(note) +
                    Run(".") + "</w:p>");

                continue;
            }

            builder.AddRawParagraph($"<w:p><w:pPr>{spacing}</w:pPr>{Run($"Body paragraph {i} of forty.")}</w:p>");
        }

        return builder;
    }

    /// <summary>
    /// A note too long for the room left under the page its reference falls on is divided between
    /// that page and the next, rather than moved off the page its reference is on or run over the
    /// bottom of it.
    /// </summary>
    /// <remarks>
    /// Where it divides is Word's answer, read off its export of <c>footnote-split-probe</c>: the
    /// note takes everything left under the line that refers to it, the body stops there, and the
    /// remainder goes to the foot of the page after. Nineteen of that note's twenty lines fit.
    /// </remarks>
    [Fact]
    public void A_note_too_long_for_its_page_is_divided_between_two_of_them()
    {
        var layout = LayoutOf(WithLongNote(20, 40, 30));

        Assert.Equal(2, layout.Pages.Count);

        var first = LinesOf(layout.Pages[0]);
        var second = LinesOf(layout.Pages[1]);

        // The reference stays with the beginning of its note.
        Assert.Contains(first, line => line.StartsWith("Body paragraph 30,"));
        Assert.Contains(first, line => line.Contains("Line 1 of a note"));

        // Nineteen lines of it fit under that page; the twentieth is on the next.
        Assert.Contains(first, line => line.Contains("Line 19 of a note"));
        Assert.DoesNotContain(first, line => line.Contains("Line 20 of a note"));
        Assert.Contains(second, line => line.Contains("Line 20 of a note"));

        // The body stops where the note begins, and carries on over the page.
        Assert.DoesNotContain(first, line => line.StartsWith("Body paragraph 31"));
        Assert.Contains(second, line => line.StartsWith("Body paragraph 31"));

        // Both pages' notes are bottom-aligned, so each ends on the same line of the page.
        Assert.Equal(Bottom(layout.Pages[0], "Line 19 of a note"), Bottom(layout.Pages[1], "Line 20 of a note"), 2);
    }

    /// <summary>Where the last line holding some text sits, down the page.</summary>
    private static double Bottom(LaidOutPage page, string text) =>
        page.Lines
            .Where(line => string.Concat(line.Texts.Select(t => t.Text)).Contains(text))
            .Select(line => line.BaselineY)
            .Max();

    /// <summary>
    /// A note longer than a whole page is divided again on the page after, and again, until there
    /// is none of it left.
    /// </summary>
    [Fact]
    public void A_note_longer_than_a_page_is_divided_again()
    {
        var layout = LayoutOf(WithLongNote(120, 6, 3));

        var pages = layout.Pages
            .Select((page, index) => (Index: index, Lines: LinesOf(page)))
            .Where(page => page.Lines.Any(line => line.Contains("of a note far too long")))
            .ToList();

        // Twenty lines to a page at most, so a note of a hundred and twenty needs several.
        Assert.True(pages.Count >= 3, $"the note was divided over only {pages.Count} page(s)");

        // Every line of it, once, in order, and nothing missing in the middle.
        var found = layout.Pages
            .SelectMany(LinesOf)
            .Where(line => line.Contains("of a note far too long"))
            // The first line of the note opens with the note's own number, so what follows the
            // word "Line" is the line's number either way.
            .Select(line => int.Parse(line.Split(' ')[1]))
            .ToList();

        Assert.Equal(Enumerable.Range(1, 120), found);
    }

    /// <summary>
    /// A note begins on the page its reference is on, whatever else has to move for it. Where
    /// there is no room under the reference for even the rule and one line of the note, it is the
    /// line carrying the reference that goes to the next page rather than the note leaving it.
    /// </summary>
    /// <remarks>
    /// Swept across a range of body lengths rather than aimed at one, since which line the
    /// reference lands on is the whole question and a single document only asks it once.
    /// </remarks>
    [Theory]
    [InlineData(38)]
    [InlineData(39)]
    [InlineData(40)]
    [InlineData(41)]
    [InlineData(42)]
    [InlineData(43)]
    [InlineData(44)]
    [InlineData(45)]
    [InlineData(46)]
    public void A_note_always_begins_on_the_page_its_reference_is_on(int referenceAt)
    {
        var layout = LayoutOf(WithLongNote(20, referenceAt + 6, referenceAt));

        var reference = layout.Pages
            .Select(LinesOf)
            .Select((lines, index) => (Index: index, Lines: lines))
            .Single(page => page.Lines.Any(line => line.StartsWith($"Body paragraph {referenceAt},")));

        Assert.Contains(reference.Lines, line => line.Contains("Line 1 of a note"));
    }

    /// <summary>
    /// A note may outlast the document it belongs to: one referenced near the end and long enough
    /// to fill several pages has no body text left to carry it onto them. Word makes the pages
    /// anyway, each holding nothing but the rest of the note, bottom-aligned as ever — its export
    /// of <c>footnote-overrun-probe</c> gives a second page with no body at all and the last
    /// thirty-seven lines of the note at the foot of it.
    /// </summary>
    [Fact]
    public void A_note_that_outlasts_the_document_is_finished_on_pages_of_its_own()
    {
        var layout = LayoutOf(WithLongNote(90, 1, 1));

        Assert.True(layout.Pages.Count >= 2, "the note was not carried past the page its reference is on");

        var lines = layout.Pages.Select(LinesOf).ToList();

        // The body is one paragraph, on the first page and nowhere else.
        Assert.Contains(lines[0], line => line.StartsWith("Body paragraph 1,"));
        for (var i = 1; i < lines.Count; i++)
        {
            Assert.DoesNotContain(lines[i], line => line.StartsWith("Body paragraph"));
        }

        // And every line of the note is there, in order, ending on the last page.
        var found = lines
            .SelectMany(page => page)
            .Where(line => line.Contains("of a note far too long"))
            .Select(line => int.Parse(line.Split(' ')[1]))
            .ToList();

        Assert.Equal(Enumerable.Range(1, 90), found);
        Assert.Contains(lines[^1], line => line.Contains("Line 90 of a note"));
    }

    /// <summary>
    /// The rest of a note is ruled off right across the measure rather than by the two inches
    /// drawn above a note that begins where it stands. That is Word's way of saying, without
    /// words, that what follows the rule is the end of something begun on the page before.
    /// </summary>
    [Fact]
    public void The_rest_of_a_note_is_ruled_off_right_across_the_measure()
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, "footnote-split-probe.pdf");
        Assert.True(File.Exists(referencePath), $"No Word reference PDF at {referencePath}");

        var ours = RulesOf(PdfPathExtractor.Extract(
            Converter.Convert(Fixtures.Build("footnote-split-probe"), Options())));

        var theirs = RulesOf(PdfPathExtractor.ExtractFile(referencePath));

        Assert.Equal(2, theirs.Count);
        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            Assert.Equal(theirs[i].PageIndex, ours[i].PageIndex);
            Assert.Equal(theirs[i].Left, ours[i].Left, 1);
            Assert.Equal(theirs[i].Width, ours[i].Width, 1);
        }

        // The first page's rule is the usual two inches and the second's is the whole measure.
        Assert.Equal(144, ours[0].Width, 1);
        Assert.Equal(468, ours[1].Width, 1);

        // The rule above the carried part is within a hundredth of a point of Word's. The one on
        // the page before is a little further out, and for a reason worth knowing: the notes are
        // bottom-aligned, so nineteen lines of the difference between our line height and Word's
        // quantised one accumulate upwards from the foot of the page to the rule above them.
        Assert.Equal(theirs[1].Top, ours[1].Top, 1);
        Assert.True(Math.Abs(ours[0].Top - theirs[0].Top) < 1,
            $"the first page's rule is at {ours[0].Top:0.###} against Word's {theirs[0].Top:0.###}");
    }

    /// <summary>The separator rules of a document, of either width, in the order they are drawn.</summary>
    private static List<ExtractedRectangle> RulesOf(IEnumerable<ExtractedRectangle> rectangles) =>
        rectangles
            .Where(r => r is { Width: > 100, Height: > 0 and < 2 })
            .OrderBy(r => r.PageIndex)
            .ThenBy(r => r.Top)
            .ToList();

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
