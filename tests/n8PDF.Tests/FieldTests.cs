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
