using n8PDF;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests the fields a document holds in place of text it cannot write down: page numbers, the
/// properties it carries about itself, counters, and references to places in it.
/// </summary>
/// <remarks>
/// The fields fixture is where these are measured against Word, one field to a line. What is here
/// instead is what that fixture cannot ask: values that would differ between two machines — the
/// clock, the reader's time zone, the name of the file — and the spellings a single page number
/// cannot show, since a one is a one in every format there is.
/// </remarks>
public class FieldTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>An instant with a date, a time and a day of the week worth naming.</summary>
    private static readonly DateTimeOffset Instant =
        new(2019, 3, 4, 14, 5, 9, TimeSpan.Zero);

    private static ConversionOptions Options() => new()
    {
        Fonts = TestFonts.CreatePinnedLibrary(),
        FieldsAsOf = Instant
    };

    private static LaidOutDocument LayoutOf(DocxBuilder builder, ConversionOptions? options = null)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, options ?? Options());
    }

    private static LaidOutDocument LayoutOf(byte[] docx, ConversionOptions? options = null)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, options ?? Options());
    }

    /// <summary>A paragraph holding one field, written as Word writes them.</summary>
    private static string Field(string instruction, string cached = "")
    {
        var text = cached.Length == 0 ? "<w:t/>" : $"<w:t xml:space=\"preserve\">{cached}</w:t>";

        return $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
               $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
               $"<w:r><w:rPr>{Times12}</w:rPr>" +
               $"<w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
               $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
               $"<w:r><w:rPr>{Times12}</w:rPr>{text}</w:r>" +
               $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>";
    }

    /// <summary>What one field shows, in a document holding nothing else.</summary>
    private static string Shows(string instruction, string cached = "", ConversionOptions? options = null)
    {
        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(Field(instruction, cached)), options);

        return string.Concat(layout.Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text)).Trim();
    }

    // ----- page numbering begun again in a section -----

    /// <summary>
    /// A document of three sections: the first numbered as it comes, the second begun again at
    /// one, the third at a number of its own. Each is a page and a bit, so the numbering is asked
    /// about more than once inside a section as well as across the breaks.
    /// </summary>
    private static DocxBuilder ThreeSections(int? second, int? third)
    {
        var builder = new DocxBuilder();

        void Fill(string label, int count)
        {
            for (var i = 1; i <= count; i++)
                builder.AddParagraph($"{label} paragraph {i}.", ZeroSpacing, Times12);
        }

        Fill("First", 60);
        builder.AddParagraphWithSectionBreak("The first section ends.",
            DocxBuilder.Section(type: "nextPage"), ZeroSpacing, Times12);

        Fill("Second", 60);
        builder.AddParagraphWithSectionBreak("The second section ends.",
            DocxBuilder.Section(type: "nextPage", pageNumberStart: second), ZeroSpacing, Times12);

        Fill("Third", 10);

        return builder.WithSection(DocxBuilder.Section(pageNumberStart: third));
    }

    /// <summary>
    /// A section may begin the page numbering again, which is what a document with a preface does
    /// — and what it begins again at is its own business, not necessarily one.
    /// </summary>
    /// <remarks>
    /// The properties on a section break describe the section it closes, not the one it opens, so
    /// the number stated on the first break belongs to the section before it.
    /// </remarks>
    [Fact]
    public void Page_numbers_begin_again_where_a_section_says_so()
    {
        var layout = LayoutOf(ThreeSections(second: 1, third: 20));

        var numbers = layout.Pages.Select(page => page.PageNumber).ToList();
        var sections = layout.Pages.Select(page => page.IndexInSection).ToList();

        // Three sections of two, two and one page: numbered 1,2 then 1,2 then 20.
        Assert.Equal([0, 1, 0, 1, 0], sections);
        Assert.Equal([1, 2, 1, 2, 20], numbers);
    }

    /// <summary>And a document whose sections say nothing is numbered straight through.</summary>
    [Fact]
    public void Page_numbers_run_through_a_document_whose_sections_say_nothing()
    {
        var layout = LayoutOf(ThreeSections(second: null, third: null));

        Assert.Equal([1, 2, 3, 4, 5], layout.Pages.Select(page => page.PageNumber));
    }

    /// <summary>
    /// What the fields make of it: the page number follows the restart, the total counts the
    /// document through regardless, and a reference to a page shows the number it is printed as
    /// rather than where it stands.
    /// </summary>
    [Fact]
    public void The_fields_show_the_number_a_page_is_printed_as()
    {
        var builder = new DocxBuilder();

        for (var i = 1; i <= 60; i++)
            builder.AddParagraph($"First section, paragraph {i}.", ZeroSpacing, Times12);

        builder.AddParagraphWithSectionBreak("The first section ends.",
            DocxBuilder.Section(type: "nextPage"), ZeroSpacing, Times12);

        // The second section begins again at one, and holds the bookmark.
        builder.AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr><w:bookmarkStart w:id=\"1\" w:name=\"marked\"/>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>The marked paragraph.</w:t></w:r>" +
            "<w:bookmarkEnd w:id=\"1\"/></w:p>");

        builder.AddRawParagraph(Field(" PAGE ", "?"));
        builder.AddRawParagraph(Field(" NUMPAGES ", "?"));
        builder.AddRawParagraph(Field(" PAGEREF marked ", "?"));

        var layout = LayoutOf(builder.WithSection(DocxBuilder.Section(pageNumberStart: 1)));

        var lines = layout.Pages[^1].Lines
            .OrderBy(line => line.BaselineY)
            .Select(line => string.Concat(line.Texts.Select(text => text.Text)))
            .ToList();

        Assert.Equal(3, layout.Pages.Count);

        // The page it is on is printed as one; the document still holds three pages; and the
        // bookmark's page is named by the number it is printed as rather than by where it stands.
        Assert.Equal(["The marked paragraph.", "1", "3", "1"], lines);
    }

    /// <summary>A document carrying properties, so that the fields reading them have an answer.</summary>
    private static DocxBuilder Described() =>
        new DocxBuilder().WithDocumentProperties(
            title: "Analytical Engine",
            creator: "Ada Lovelace",
            created: "2019-03-04T14:05:09Z",
            modified: "2021-11-30T12:00:00Z",
            lastPrinted: "2022-06-08T12:00:00Z",
            custom: ("Category", "Reference"));

    [Fact]
    public void A_field_nothing_can_work_out_shows_what_Word_last_computed()
    {
        Assert.Equal("what Word last computed", Shows(" ADDIN SOMETHING ", "what Word last computed"));

        // Including one whose keyword is known but whose document says nothing: a document with no
        // properties has no author, and showing an empty line would lose what Word put there.
        Assert.Equal("A Lovelace", Shows(" AUTHOR ", "A Lovelace"));
    }

    /// <summary>
    /// DATE and TIME are the clock rather than the document, so a conversion that has to come out
    /// the same twice pins the instant they report.
    /// </summary>
    [Fact]
    public void The_clock_fields_report_the_instant_the_conversion_is_given()
    {
        Assert.Equal("2019-03-04", Shows(" DATE \\@ \"yyyy-MM-dd\" "));
        Assert.Equal("2019", Shows(" TIME \\@ \"yyyy\" "));
    }

    /// <summary>
    /// Word shows a document's own dates in the reader's time zone, which is why the fixture keeps
    /// to dates and leaves the time of day here.
    /// </summary>
    [Fact]
    public void A_pictured_date_is_spelled_the_way_the_picture_asks()
    {
        var local = Instant.ToLocalTime();

        Assert.Equal(local.ToString("HH:mm:ss"), Shows(" DATE \\@ \"HH:mm:ss\" "));
        Assert.Equal(local.ToString("h:mm tt"), Shows(" DATE \\@ \"h:mm AM/PM\" "));
        Assert.Equal(local.ToString("h:mm t"), Shows(" DATE \\@ \"h:mm A/P\" "));
        Assert.Equal(local.ToString("dddd"), Shows(" DATE \\@ \"dddd\" "));
    }

    /// <summary>
    /// Text between the tokens of a picture is text, not more tokens: the "of" in a date written
    /// out longhand would otherwise be read as a day and a fraction of a second.
    /// </summary>
    [Fact]
    public void Text_in_a_picture_is_left_as_it_was_written()
    {
        Assert.Equal("4 of March, 2019", Shows(" DATE \\@ \"d 'of' MMMM, yyyy\" "));
        Assert.Equal("4-3-2019", Shows(" DATE \\@ \"d-M-yyyy\" "));
    }

    /// <summary>The dates the document carries rather than the clock.</summary>
    [Fact]
    public void The_document_dates_come_from_its_properties()
    {
        var builder = Described()
            .AddRawParagraph(Field(" CREATEDATE \\@ \"yyyy-MM-dd\" "))
            .AddRawParagraph(Field(" SAVEDATE \\@ \"yyyy-MM-dd\" "))
            .AddRawParagraph(Field(" PRINTDATE \\@ \"yyyy-MM-dd\" "));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(["2019-03-04", "2021-11-30", "2022-06-08"], lines);
    }

    /// <summary>
    /// A date field with no picture takes the one Word takes from the reader's own settings, which
    /// is a short date and the time of day.
    /// </summary>
    [Fact]
    public void A_date_with_no_picture_is_shown_short()
    {
        Assert.Equal(
            Instant.ToLocalTime().ToString("M/d/yy h:mm:ss tt"),
            Shows(" DATE "));
    }

    /// <summary>
    /// The name of the file, which a conversion only knows if it is told: converting a stream
    /// leaves the field showing what Word last computed.
    /// </summary>
    [Fact]
    public void The_file_name_is_shown_when_the_conversion_knows_it()
    {
        var options = Options();
        options.FileName = "/documents/Report of 1843.docx";

        Assert.Equal("Report of 1843.docx", Shows(" FILENAME ", options: options));
        Assert.Equal("/documents/Report of 1843.docx", Shows(" FILENAME \\p ", options: options));

        Assert.Equal("last.docx", Shows(" FILENAME ", "last.docx"));
    }

    /// <summary>
    /// Every spelling of a number, which the fixture can only show for one: a page number of 1 is
    /// the same in most of them.
    /// </summary>
    [Theory]
    [InlineData("", "24")]
    [InlineData(" \\* arabic", "24")]
    [InlineData(" \\* roman", "xxiv")]
    [InlineData(" \\* ROMAN", "XXIV")]
    [InlineData(" \\* alphabetic", "x")]
    [InlineData(" \\* ALPHABETIC", "X")]
    [InlineData(" \\* Ordinal", "24th")]
    [InlineData(" \\* CardText", "twenty-four")]
    [InlineData(" \\* OrdText", "twenty-fourth")]
    [InlineData(" \\* Hex", "18")]
    [InlineData(" \\* DollarText", "twenty-four and 00/100")]
    [InlineData(" \\* MERGEFORMAT", "24")]
    public void A_number_is_spelled_the_way_its_switch_asks(string format, string expected)
    {
        Assert.Equal(expected, Shows($" SEQ Item \\r 24{format} "));
    }

    /// <summary>The awkward ones, which are not formed like the numbers around them.</summary>
    [Theory]
    [InlineData(1, "first")]
    [InlineData(2, "second")]
    [InlineData(3, "third")]
    [InlineData(5, "fifth")]
    [InlineData(9, "ninth")]
    [InlineData(12, "twelfth")]
    [InlineData(20, "twentieth")]
    [InlineData(21, "twenty-first")]
    [InlineData(113, "one hundred thirteenth")]
    public void Spelled_positions_are_formed_the_way_English_forms_them(int value, string expected)
    {
        Assert.Equal(expected, Shows($" SEQ Item \\r {value} \\* OrdText "));
    }

    [Theory]
    [InlineData(11, "eleven")]
    [InlineData(40, "forty")]
    [InlineData(105, "one hundred five")]
    [InlineData(1042, "one thousand forty-two")]
    public void Spelled_numbers_are_formed_the_way_English_forms_them(int value, string expected)
    {
        Assert.Equal(expected, Shows($" SEQ Item \\r {value} \\* CardText "));
    }

    /// <summary>
    /// A counter belongs to its name, and a run of fields with the same name counts through the
    /// document. This is the part the fixture measures against Word; what it adds here is that a
    /// name with a space in it is one name.
    /// </summary>
    [Fact]
    public void A_counter_named_in_quotes_is_one_counter()
    {
        var builder = new DocxBuilder()
            .AddRawParagraph(Field(" SEQ \"Figure and Table\" "))
            .AddRawParagraph(Field(" SEQ \"Figure and Table\" "))
            .AddRawParagraph(Field(" SEQ Figure "));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(["1", "2", "1"], lines);
    }

    /// <summary>A property the document names itself, and one it does not.</summary>
    [Fact]
    public void Doc_property_reads_the_documents_own_properties()
    {
        var builder = Described()
            .AddRawParagraph(Field(" DOCPROPERTY \"Category\" "))
            .AddRawParagraph(Field(" DOCPROPERTY Title "))
            .AddRawParagraph(Field(" DOCPROPERTY Nothing ", "cached"));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(["Reference", "Analytical Engine", "cached"], lines);
    }

    /// <summary>
    /// A REF shows the text its bookmark covers, which is a range rather than a place: it ends
    /// where the bookmark ends, not where the paragraph does.
    /// </summary>
    [Fact]
    public void A_reference_shows_the_text_its_bookmark_covers()
    {
        var builder = new DocxBuilder()
            .AddRawParagraph(
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">Before </w:t></w:r>" +
                "<w:bookmarkStart w:id=\"3\" w:name=\"middle\"/>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>the marked words</w:t></w:r>" +
                "<w:bookmarkEnd w:id=\"3\"/>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\"> after.</w:t></w:r></w:p>")
            .AddRawParagraph(Field(" REF middle "))
            .AddRawParagraph(Field(" REF missing ", "cached"));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal("the marked words", lines[1]);
        Assert.Equal("cached", lines[2]);
    }

    /// <summary>
    /// A page number in the body is only known once the document has been paginated, so it is laid
    /// out again — and a field far into a long document reports the page it actually lands on.
    /// </summary>
    [Fact]
    public void A_page_number_in_the_body_is_the_page_it_lands_on()
    {
        var builder = new DocxBuilder();
        for (var i = 1; i <= 100; i++) builder.AddParagraph($"Filler {i}.", ZeroSpacing, Times12);

        builder.AddRawParagraph(Field(" PAGE "));
        builder.AddRawParagraph(Field(" NUMPAGES "));

        var layout = LayoutOf(builder);

        var last = layout.Pages[^1].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(layout.Pages.Count.ToString(), last[^2]);
        Assert.Equal(layout.Pages.Count.ToString(), last[^1]);
    }

    /// <summary>
    /// Laying the document out twice must not double anything that counts as it goes: the second
    /// pass starts the counters again rather than carrying on from where the first left off.
    /// </summary>
    [Fact]
    public void Laying_out_twice_does_not_count_twice()
    {
        var builder = new DocxBuilder()
            .AddRawParagraph(Field(" SEQ Figure "))
            .AddRawParagraph(Field(" SEQ Figure "))

            // Enough to make a page number worth working out, which is what asks for the second
            // pass in the first place.
            .AddRawParagraph(Field(" PAGE "));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(["1", "2", "1"], lines);
    }

    /// <summary>
    /// The instruction is a command line of sorts, and its arguments and switches are told apart
    /// the way one is: quotes hold a value together, and a switch may be written against its value
    /// or apart from it.
    /// </summary>
    [Fact]
    public void An_instruction_is_split_into_its_keyword_arguments_and_switches()
    {
        var instruction = FieldInstruction.Parse(" DOCPROPERTY \"Category name\" \\* Upper ");

        Assert.Equal("DOCPROPERTY", instruction.Keyword);
        Assert.Equal("Category name", instruction.Argument);
        Assert.Equal("Upper", instruction.SwitchValue('*'));

        // A switch written against its value.
        Assert.Equal("roman", FieldInstruction.Parse(" PAGE \\*roman ").SwitchValue('*'));

        // One that carries no value, and one whose value is the next word.
        var sequence = FieldInstruction.Parse(" SEQ Figure \\c \\r 4 ");
        Assert.Equal("Figure", sequence.Argument);
        Assert.True(sequence.HasSwitch('c'));
        Assert.Equal("4", sequence.SwitchValue('r'));

        // A flag before an argument does not swallow it.
        var reference = FieldInstruction.Parse(" REF target \\h ");
        Assert.Equal("target", reference.Argument);
        Assert.True(reference.HasSwitch('h'));
    }

    // ----- STYLEREF -----

    private const string HeadingStyle =
        "<w:style w:type=\"paragraph\" w:styleId=\"Heading1\">" +
        "<w:name w:val=\"heading 1\"/><w:pPr>" +
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>" +
        "</w:pPr></w:style>";

    private static string Heading(string text) =>
        $"<w:p><w:pPr><w:pStyle w:val=\"Heading1\"/>{ZeroSpacing}</w:pPr>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r></w:p>";

    /// <summary>
    /// In the body a STYLEREF looks backwards from where it stands, which is the nearest heading
    /// above it rather than the first or the last in the document.
    /// </summary>
    [Fact]
    public void In_the_body_a_style_reference_looks_backwards()
    {
        var builder = new DocxBuilder().WithExtraStyles(HeadingStyle)
            .AddRawParagraph(Heading("Alpha"))
            .AddRawParagraph(Field(" STYLEREF \"Heading 1\" "))
            .AddRawParagraph(Heading("Beta"))
            .AddRawParagraph(Field(" STYLEREF \"Heading 1\" "));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(["Alpha", "Alpha", "Beta", "Beta"], lines);
    }

    /// <summary>
    /// A field with nothing of that style above it looks forward instead, which is the one case
    /// where it shows a heading it comes before.
    /// </summary>
    [Fact]
    public void A_style_reference_with_nothing_above_it_looks_forward()
    {
        var builder = new DocxBuilder().WithExtraStyles(HeadingStyle)
            .AddRawParagraph(Field(" STYLEREF \"Heading 1\" "))
            .AddRawParagraph(Heading("Alpha"));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal("Alpha", lines[0]);
    }

    /// <summary>
    /// The style is named rather than identified. Word answers a field naming the style's id with
    /// an error telling the reader to apply the style, so an id is not a name here even where it
    /// looks like one — and what cannot be worked out keeps its cached result.
    /// </summary>
    [Fact]
    public void A_style_reference_names_a_style_rather_than_identifying_it()
    {
        var builder = new DocxBuilder().WithExtraStyles(HeadingStyle)
            .AddRawParagraph(Heading("Alpha"))
            .AddRawParagraph(Field(" STYLEREF Heading1 ", "cached"))
            .AddRawParagraph(Field(" STYLEREF \"No Such Style\" ", "also cached"));

        var lines = LayoutOf(builder).Pages[0].Lines
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)).Trim())
            .ToList();

        Assert.Equal(["Alpha", "cached", "also cached"], lines);
    }

    /// <summary>
    /// The whole point of the field: a running head that follows the headings down a document. On
    /// a page holding a heading it shows that heading, and on one holding none it carries on
    /// showing the last one before it.
    /// </summary>
    [Fact]
    public void A_running_head_follows_the_headings_down_the_document()
    {
        var layout = LayoutOf(Fixtures.Build("styleref"), Options());

        static string HeaderOf(LaidOutPage page) =>
            string.Concat(page.Lines
                .Where(l => l.Texts.Any(t => t.Text.StartsWith("head")))
                .SelectMany(l => l.Texts)
                .Select(t => t.Text));

        Assert.Equal(3, layout.Pages.Count);

        // The first page holds two headings: the head shows the first and \l the last.
        Assert.Equal("head: Alpha / last: Beta", HeaderOf(layout.Pages[0]));

        Assert.Equal("head: Gamma / last: Gamma", HeaderOf(layout.Pages[1]));

        // The last page holds no heading at all — a line of its own saying nothing else — and
        // carries the one before it.
        Assert.DoesNotContain(layout.Pages[2].Lines,
            l => string.Concat(l.Texts.Select(t => t.Text)).Trim() is "Alpha" or "Beta" or "Gamma");

        Assert.Equal("head: Gamma / last: Gamma", HeaderOf(layout.Pages[2]));
    }

    /// <summary>
    /// A footer looks down its page like a header rather than up it, which is not what would be
    /// guessed: on a page with two headings it shows the first, and takes the last only when asked
    /// for it.
    /// </summary>
    [Fact]
    public void A_footer_looks_down_its_page_like_a_header()
    {
        var footer = LayoutOf(Fixtures.Build("styleref"), Options()).Pages[0].Lines
            .Where(l => l.Texts.Any(t => t.Text.StartsWith("foot")))
            .SelectMany(l => l.Texts)
            .Select(t => t.Text);

        Assert.Equal("foot: Alpha / last: Beta", string.Concat(footer));
    }

    /// <summary>
    /// A field written the short way, as one element with its instruction in an attribute, is the
    /// same field.
    /// </summary>
    [Fact]
    public void A_simple_field_is_evaluated_like_any_other()
    {
        var builder = Described().AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
            "<w:fldSimple w:instr=\" AUTHOR \\* Upper \">" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r></w:fldSimple></w:p>");

        var text = string.Concat(
            LayoutOf(builder).Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text)).Trim();

        Assert.Equal("ADA LOVELACE", text);
    }
}
