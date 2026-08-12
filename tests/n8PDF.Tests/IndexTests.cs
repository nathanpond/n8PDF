using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests the index: the entries a document marks where they occur, and the field that gathers them
/// into a list of terms and the pages they were marked on.
/// </summary>
/// <remarks>
/// It is written in two halves. An XE field marks a term where it belongs and draws nothing at all
/// — it is there to be found, not read — and an INDEX field lists every one of them. The shape of
/// the list is read from Word's export of the index fixture: a term, a comma, and the pages;
/// subentries indented under a parent that carries no page of its own; and a line holding the
/// letter each group begins with where the field asks for one.
/// </remarks>
public class IndexTests
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

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }

    /// <summary>The index styles, as Word writes them into a document that has one.</summary>
    private static string Styles() =>
        string.Concat(new[] { 1, 2 }.Select(level =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"Index{level}\">" +
            $"<w:name w:val=\"index {level}\"/>" +
            $"<w:pPr>{ZeroSpacing}" +
            (level == 1 ? string.Empty : $"<w:ind w:left=\"{(level - 1) * 220}\"/>") +
            $"</w:pPr><w:rPr>{Times12}</w:rPr></w:style>")) +
        "<w:style w:type=\"paragraph\" w:styleId=\"IndexHeading\">" +
        "<w:name w:val=\"index heading\"/>" +
        $"<w:pPr>{ZeroSpacing}</w:pPr><w:rPr>{Times12}</w:rPr></w:style>";

    /// <summary>A field, written the long way as Word writes the ones it has not evaluated.</summary>
    private static string Field(string instruction, string cached = "") =>
        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        (cached.Length == 0
            ? $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r>"
            : $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{cached}</w:t></w:r>") +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    /// <summary>A paragraph of text carrying an entry marker.</summary>
    private static string Mark(string instruction, string text = "Text") =>
        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    private static List<string> LinesOf(LaidOutDocument document) =>
        [.. document.Pages
            .SelectMany(page => page.Lines)
            .Select(line => string.Concat(line.Texts.OrderBy(t => t.X).Select(t => t.Text)).Trim())];

    /// <summary>The lines the index itself produced, which follow the text that was marked.</summary>
    private static List<string> IndexLines(DocxBuilder builder) =>
        [.. LinesOf(LayoutOf(builder)).SkipWhile(l => l.StartsWith("Text") || l.Length == 0)];

    private static DocxBuilder Document(string index, params string[] marks)
    {
        var builder = new DocxBuilder().WithExtraStyles(Styles());

        foreach (var mark in marks) builder.AddRawParagraph(Mark(mark));

        return builder.AddRawParagraph(Field(index));
    }

    [Fact]
    public void The_marked_terms_become_the_index()
    {
        var lines = IndexLines(Document(" INDEX ",
            " XE \"Babbage\" ", " XE \"Analysis\" "));

        // Sorted, whatever order they were marked in, and each against the page it was on.
        Assert.Equal(["Analysis, 1", "Babbage, 1"], lines.Where(l => l.Length > 0));
    }

    /// <summary>An entry marker draws nothing: it is there to be found, not read.</summary>
    [Fact]
    public void A_marker_draws_nothing_where_it_stands()
    {
        var lines = LinesOf(LayoutOf(Document(" INDEX ", " XE \"Babbage\" ")));

        Assert.Equal("Text", lines[0]);
    }

    /// <summary>
    /// A term marked twice on one page is one page number; marked on two, it carries both.
    /// </summary>
    [Fact]
    public void The_pages_are_the_pages_it_was_marked_on()
    {
        var builder = new DocxBuilder().WithExtraStyles(Styles())
            .AddRawParagraph(Mark(" XE \"Engine\" "))
            .AddRawParagraph(Mark(" XE \"Engine\" "));

        for (var i = 1; i <= 60; i++) builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(Mark(" XE \"Engine\" "));
        builder.AddRawParagraph(Field(" INDEX "));

        Assert.Contains("Engine, 1, 2", LinesOf(LayoutOf(builder)));
    }

    /// <summary>
    /// A term written with a colon in it is a subentry: it reads under its parent, indented, and
    /// the parent carries no page of its own unless it was marked as a term as well.
    /// </summary>
    [Fact]
    public void A_term_with_a_colon_in_it_is_a_subentry()
    {
        var layout = LayoutOf(Document(" INDEX ",
            " XE \"Engine:analytical\" ", " XE \"Engine:difference\" "));

        var lines = LinesOf(layout);

        Assert.Contains("Engine", lines);
        Assert.Contains("analytical, 1", lines);
        Assert.Contains("difference, 1", lines);

        // The subentries are indented and their parent is not.
        var parent = layout.Pages[0].Lines.Single(l => l.Texts.Any(t => t.Text == "Engine"));
        var child = layout.Pages[0].Lines.Single(l => l.Texts.Any(t => t.Text.StartsWith("analytical")));

        Assert.Equal(72, parent.Texts[0].X, 1);
        Assert.Equal(83, child.Texts[0].X, 1);
    }

    /// <summary>
    /// The <c>\h</c> switch asks for a line before each letter group, with the letter of the group
    /// put where the template's own letter is.
    /// </summary>
    [Fact]
    public void Letter_headings_are_the_template_with_the_letter_in_it()
    {
        var lines = IndexLines(Document(" INDEX \\h \"—A—\" ",
            " XE \"Babbage\" ", " XE \"Analysis\" ", " XE \"Arithmetic\" "));

        Assert.Equal(["—A—", "Analysis, 1", "Arithmetic, 1", "—B—", "Babbage, 1"],
            lines.Where(l => l.Length > 0));
    }

    /// <summary>What goes between a term and its pages, and between one page and the next.</summary>
    [Fact]
    public void The_separators_are_what_the_field_asks_for()
    {
        var builder = new DocxBuilder().WithExtraStyles(Styles())
            .AddRawParagraph(Mark(" XE \"Engine\" "));

        for (var i = 1; i <= 60; i++) builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(Mark(" XE \"Engine\" "))
            .AddRawParagraph(Field(" INDEX \\e \": \" \\l \"; \" "));

        Assert.Contains("Engine: 1; 2", LinesOf(LayoutOf(builder)));
    }

    /// <summary>
    /// A marker can name what to show in place of a page number, which is how an index says
    /// "see something else".
    /// </summary>
    [Fact]
    public void A_marker_can_say_what_to_show_instead_of_a_page()
    {
        var lines = IndexLines(Document(" INDEX ",
            " XE \"Analytical engine\" \\t \"see Engine\" "));

        Assert.Equal(["Analytical engine, see Engine"], lines.Where(l => l.Length > 0));
    }

    /// <summary>
    /// A document can carry more than one index, and each gathers only the markers of its own
    /// type: the entries of the other are not its own.
    /// </summary>
    [Fact]
    public void An_index_gathers_only_the_entries_of_its_own_type()
    {
        var lines = IndexLines(Document(" INDEX \\f \"names\" ",
            " XE \"Babbage\" \\f \"names\" ", " XE \"Engine\" "));

        Assert.Equal(["Babbage, 1"], lines.Where(l => l.Length > 0));
    }

    /// <summary>
    /// A field with nothing to gather keeps what it produced last time rather than emptying the
    /// page: a document with no markers at all has nothing this can say.
    /// </summary>
    [Fact]
    public void An_index_of_a_document_with_no_markers_is_left_as_it_was()
    {
        var builder = new DocxBuilder().WithExtraStyles(Styles())
            .AddRawParagraph(Field(" INDEX ", "What it said before"));

        Assert.Contains("What it said before", LinesOf(LayoutOf(builder)));
    }

    /// <summary>Sorting is by the term rather than by where it was marked, and ignores case.</summary>
    [Fact]
    public void The_terms_are_sorted_whatever_case_they_are_written_in()
    {
        var lines = IndexLines(Document(" INDEX ",
            " XE \"zero\" ", " XE \"Analysis\" ", " XE \"babbage\" "));

        Assert.Equal(["Analysis, 1", "babbage, 1", "zero, 1"], lines.Where(l => l.Length > 0));
    }

    /// <summary>The whole fixture, which is where every rule above was measured.</summary>
    [Fact]
    public void The_fixture_indexes_its_own_markers()
    {
        var lines = LinesOf(LayoutOf(Fixtures.Build("index")))
            .SkipWhile(l => !l.StartsWith("A") || l.Length > 1)
            .Where(l => l.Length > 0)
            .ToList();

        Assert.Equal(
            [
                "A", "Analysis, 1", "Arithmetic, 1",
                "B", "Babbage, 1",
                "E", "Engine", "analytical, 1", "difference, 1",
                "Z", "Zero, 2"
            ],
            lines);
    }

    /// <summary>
    /// A marker's own reading: the term, the levels a colon divides it into, and the switches that
    /// say what to show and which index it belongs to.
    /// </summary>
    [Fact]
    public void A_marker_is_read_for_the_term_it_marks()
    {
        var mark = IndexBuilder.Read(FieldInstruction.Parse(" XE \"Engine:analytical\" \\f \"names\" "));

        Assert.NotNull(mark);
        Assert.Equal(["Engine", "analytical"], mark.Levels);
        Assert.Equal("names", mark.Type);
        Assert.Null(mark.Text);

        // A colon that belongs to the term is written with a backslash before it.
        var escaped = IndexBuilder.Read(FieldInstruction.Parse(" XE \"Ratio 3\\:1\" "));

        Assert.NotNull(escaped);
        Assert.Equal(["Ratio 3:1"], escaped.Levels);

        Assert.Null(IndexBuilder.Read(FieldInstruction.Parse(" INDEX ")));
    }
}
