using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests the table of contents: the one field whose answer is a run of paragraphs rather than a
/// few words, one to each heading the document holds.
/// </summary>
/// <remarks>
/// It is worked out again rather than read back from what the field last produced. A stale table
/// of contents is as wrong as a stale page number, and a document that has never had one built —
/// which is what a file written by hand is — has nothing to read back at all.
///
/// Every rule here is read from Word's export of the toc fixture, with its fields updated first:
/// the entry styles, the tab the page numbers hang from, and the empty line the field leaves
/// behind it.
/// </remarks>
public class TableOfContentsTests
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

    /// <summary>The styles a document carrying a table of contents defines, as Word writes them.</summary>
    private static string Styles(bool withTocStyles = true)
    {
        var headings = string.Concat(new[] { 0, 1 }.Select(level =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"Heading{level + 1}\">" +
            $"<w:name w:val=\"heading {level + 1}\"/>" +
            $"<w:pPr>{ZeroSpacing}<w:outlineLvl w:val=\"{level}\"/></w:pPr>" +
            $"<w:rPr>{Times12}</w:rPr></w:style>"));

        if (!withTocStyles) return headings;

        return headings + string.Concat(new[] { 1, 2 }.Select(level =>
            $"<w:style w:type=\"paragraph\" w:styleId=\"TOC{level}\"><w:name w:val=\"toc {level}\"/>" +
            "<w:pPr><w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9360\"/></w:tabs>" +
            $"{ZeroSpacing}" +
            (level == 1 ? string.Empty : $"<w:ind w:left=\"{(level - 1) * 220}\"/>") +
            $"</w:pPr><w:rPr>{Times12}</w:rPr></w:style>"));
    }

    private static string Field(string instruction) =>
        $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";

    private static string Heading(int level, string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"Heading{level}\"/>{ZeroSpacing}</w:pPr>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r></w:p>";

    /// <summary>A document of the given headings, with a table of contents at the top of it.</summary>
    private static DocxBuilder Document(
        string instruction = " TOC \\o \"1-3\" ", bool withTocStyles = true,
        params (int Level, string Text)[] headings)
    {
        var builder = new DocxBuilder()
            .WithExtraStyles(Styles(withTocStyles))
            .AddRawParagraph(Field(instruction));

        foreach (var (level, text) in headings) builder.AddRawParagraph(Heading(level, text));

        return builder;
    }

    /// <summary>
    /// The lines of a page, as text. Read across the page rather than in the order the line put
    /// them down: a tab's leader is drawn once the gap it fills is known, which is after whatever
    /// follows it on the line.
    /// </summary>
    private static List<string> LinesOf(LaidOutPage page) =>
        [.. page.Lines.Select(l =>
            string.Concat(l.Texts.OrderBy(t => t.X).Select(t => t.Text)).Trim())];

    /// <summary>Where a line's rightmost text ends, which is where its page number is.</summary>
    private static double RightEdgeOf(LaidOutLine line) =>
        line.Texts.Max(t => t.X + t.Width);

    /// <summary>The lines of every page of a document, as text.</summary>
    private static List<string> LinesOf(LaidOutDocument document) =>
        [.. document.Pages.SelectMany(LinesOf)];

    [Fact]
    public void The_headings_of_the_document_become_its_entries()
    {
        var layout = LayoutOf(Document(
            headings: [(1, "Alpha"), (2, "Beta"), (1, "Gamma")]));

        var lines = LinesOf(layout.Pages[0]);

        // Each entry is its heading, a leader, and the page it is on — all three headings are on
        // the first page here, since the table of contents is all that precedes them.
        Assert.StartsWith("Alpha", lines[0]);
        Assert.EndsWith("1", lines[0]);
        Assert.StartsWith("Beta", lines[1]);
        Assert.StartsWith("Gamma", lines[2]);

        // Then the empty line the field leaves behind, and the headings themselves.
        Assert.Equal("", lines[3]);
        Assert.Equal("Alpha", lines[4]);
    }

    /// <summary>
    /// Entries take the style named for their level, which is where their indent and the tab their
    /// page numbers hang from come from.
    /// </summary>
    [Fact]
    public void An_entry_is_set_in_the_style_named_for_its_level()
    {
        var layout = LayoutOf(Document(headings: [(1, "Alpha"), (2, "Beta")]));

        var lines = layout.Pages[0].Lines;

        // TOC1 has no indent and TOC2 has eleven points of one, which is what the styles say.
        Assert.Equal(72, lines[0].Texts.Min(t => t.X), 1);
        Assert.Equal(83, lines[1].Texts.Min(t => t.X), 1);

        // Both hang their page numbers from the same stop, at the far margin: a tab stop is
        // measured from the margin rather than from the paragraph's own indent.
        Assert.Equal(540, RightEdgeOf(lines[0]), 0);
        Assert.Equal(540, RightEdgeOf(lines[1]), 0);
    }

    /// <summary>
    /// Which headings it gathers is what the instruction says: <c>\o</c> names a range of levels,
    /// and anything below it is left out.
    /// </summary>
    [Fact]
    public void The_instruction_says_which_levels_are_gathered()
    {
        var deep = LinesOf(LayoutOf(Document(" TOC \\o \"1-2\" ",
            headings: [(1, "Alpha"), (2, "Beta")])).Pages[0]);

        // Both headings are entries, then the empty line, then the headings themselves.
        Assert.StartsWith("Alpha", deep[0]);
        Assert.StartsWith("Beta", deep[1]);
        Assert.Equal("", deep[2]);

        var shallow = LayoutOf(Document(" TOC \\o \"1-1\" ",
            headings: [(1, "Alpha"), (2, "Beta")]));

        var lines = LinesOf(shallow.Pages[0]);

        Assert.StartsWith("Alpha", lines[0]);
        Assert.Equal("", lines[1]);
        Assert.Equal("Alpha", lines[2]);
    }

    /// <summary>The <c>\n</c> switch asks for the headings without the pages they are on.</summary>
    [Fact]
    public void Page_numbers_can_be_left_off()
    {
        var layout = LayoutOf(Document(" TOC \\o \"1-3\" \\n ",
            headings: [(1, "Alpha")]));

        Assert.Equal("Alpha", LinesOf(layout.Pages[0])[0]);
    }

    /// <summary>
    /// A table of contents in a document with no styles to set it in still reads as one: the entry
    /// carries the indent and the leader itself, since a page number run up against its heading
    /// would be no table at all.
    /// </summary>
    [Fact]
    public void A_document_with_no_entry_styles_still_gets_a_table()
    {
        var layout = LayoutOf(Document(withTocStyles: false,
            headings: [(1, "Alpha"), (2, "Beta")]));

        var lines = layout.Pages[0].Lines;

        Assert.Equal(72, lines[0].Texts.Min(t => t.X), 1);
        Assert.Equal(83, lines[1].Texts.Min(t => t.X), 1);

        // The page number still lands at the far margin, with a leader running out to it.
        Assert.Equal(540, RightEdgeOf(lines[0]), 0);
        Assert.Contains(lines[0].Texts, t => t.Text.Contains('.'));
    }

    /// <summary>
    /// The page numbers are the pages the headings are really on, which is why a document holding
    /// a table of contents is laid out twice.
    /// </summary>
    [Fact]
    public void The_pages_are_the_ones_the_headings_landed_on()
    {
        var builder = new DocxBuilder()
            .WithExtraStyles(Styles())
            .AddRawParagraph(Field(" TOC \\o \"1-3\" "))
            .AddRawParagraph(Heading(1, "Alpha"));

        for (var i = 1; i <= 60; i++) builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(Heading(1, "Beta"));

        var layout = LayoutOf(builder);

        Assert.Equal(2, layout.Pages.Count);

        var lines = LinesOf(layout.Pages[0]);

        Assert.EndsWith("1", lines[0]);
        Assert.EndsWith("2", lines[1]);
    }

    /// <summary>
    /// What the field produced last time is replaced rather than added to: the entries a document
    /// carries are the field's own result, and laying them out as well would print the table
    /// twice.
    /// </summary>
    [Fact]
    public void A_table_the_document_already_carries_is_replaced()
    {
        // A field written the way Word writes one it has built: it opens in the paragraph holding
        // its first entry and closes in a paragraph of its own further down.
        var builder = new DocxBuilder()
            .WithExtraStyles(Styles())
            .AddRawParagraph(
                "<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/></w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr>" +
                "<w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" </w:instrText></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Stale entry</w:t></w:r></w:p>")
            .AddRawParagraph(
                "<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/></w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Another stale entry</w:t></w:r></w:p>")
            .AddRawParagraph(
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>")
            .AddRawParagraph(Heading(1, "Alpha"));

        var lines = LinesOf(LayoutOf(builder));

        Assert.DoesNotContain(lines, l => l.Contains("stale", StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith("Alpha", lines[0]);
    }

    /// <summary>
    /// A field whose entries cannot be worked out keeps what it produced last time: a document
    /// with no headings at all would otherwise lose the table it carries.
    /// </summary>
    [Fact]
    public void A_table_of_a_document_with_no_headings_is_left_as_it_was()
    {
        var builder = new DocxBuilder()
            .WithExtraStyles(Styles())
            .AddRawParagraph(
                "<w:p><w:pPr><w:pStyle w:val=\"TOC1\"/></w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr>" +
                "<w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" </w:instrText></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>What it said before</w:t></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

        Assert.Contains("What it said before", LinesOf(LayoutOf(builder)));
    }

    /// <summary>
    /// A field that runs past the end of its paragraph keeps the content it opened with, which a
    /// reader would otherwise lose: the first entry of a table of contents lives in the same
    /// paragraph as the instruction that produced it.
    /// </summary>
    [Fact]
    public void A_field_that_runs_past_its_paragraph_keeps_what_it_opened_with()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
            $"<w:r><w:rPr>{Times12}</w:rPr>" +
            "<w:instrText xml:space=\"preserve\"> SOMETHING </w:instrText></w:r>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>The first line of it</w:t></w:r></w:p>");

        builder.AddParagraph("The second line of it.", ZeroSpacing, Times12);

        builder.AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

        var lines = LinesOf(LayoutOf(builder));

        Assert.Equal("The first line of it", lines[0]);
        Assert.Equal("The second line of it.", lines[1]);
    }

    /// <summary>The whole fixture: five headings over three pages, and the table that finds them.</summary>
    [Fact]
    public void The_fixture_tables_its_own_contents()
    {
        var layout = LayoutOf(Fixtures.Build("toc"));

        var lines = LinesOf(layout.Pages[0]);

        Assert.StartsWith("The first chapter", lines[0]);
        Assert.EndsWith("1", lines[0]);
        Assert.StartsWith("A section of it", lines[1]);
        Assert.StartsWith("The second chapter", lines[2]);
        Assert.StartsWith("Another section", lines[3]);
        Assert.EndsWith("2", lines[3]);

        // The longest entry wraps, and the page number goes with the end of it.
        Assert.StartsWith("The third chapter", lines[4]);
        Assert.EndsWith("3", lines[5]);
    }

    /// <summary>
    /// The instruction's own reading: a range of levels, styles named outright, and the switches
    /// that turn the page numbers off.
    /// </summary>
    [Fact]
    public void The_instruction_is_read_for_what_it_gathers()
    {
        var scope = TableOfContentsBuilder.ScopeOf(FieldInstruction.Parse(" TOC \\o \"2-4\" "));

        Assert.Null(scope.LevelOf(0, null));
        Assert.Equal(2, scope.LevelOf(1, null));
        Assert.Equal(4, scope.LevelOf(3, null));
        Assert.Null(scope.LevelOf(4, null));

        // Styles named outright enter at the level they are given, whatever level they stand at.
        var named = TableOfContentsBuilder.ScopeOf(
            FieldInstruction.Parse(" TOC \\t \"Caption,1;Figure,2\" "));

        Assert.Equal(1, named.LevelOf(null, "Caption"));
        Assert.Equal(2, named.LevelOf(null, "Figure"));
        Assert.Null(named.LevelOf(0, "Heading1"));

        Assert.True(TableOfContentsBuilder.ShowsPageNumbers(FieldInstruction.Parse(" TOC ")));
        Assert.False(TableOfContentsBuilder.ShowsPageNumbers(FieldInstruction.Parse(" TOC \\n ")));
    }
}
