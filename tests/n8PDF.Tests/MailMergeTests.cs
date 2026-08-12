using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests the merge fields: the ones a letter written for a mail merge is made of.
/// </summary>
/// <remarks>
/// A document written for a merge does not carry its own data. It names an external source — a
/// spreadsheet, an address book — that only the machine it was written on can reach, so a
/// conversion has nothing to fill the fields from unless it is given something. What Word shows
/// for a document in that state is what the merge fixture measures: each field's own name in
/// guillemets, with whatever the field asks to be printed around it.
///
/// The other half is what this converter adds, and what the fixture cannot show: a record given to
/// the conversion, which fills the fields in.
/// </remarks>
public class MailMergeTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private static ConversionOptions Options(MailMergeRecord? record = null) => new()
    {
        Fonts = TestFonts.CreatePinnedLibrary(),
        MergeRecord = record
    };

    private static LaidOutDocument LayoutOf(DocxBuilder builder, ConversionOptions options)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, options);
    }

    private static LaidOutDocument LayoutOf(byte[] docx, ConversionOptions options)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, options);
    }

    private static string Field(string instruction, string cached = "") =>
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"begin\"/></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr>" +
        $"<w:instrText xml:space=\"preserve\">{instruction}</w:instrText></w:r>" +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
        (cached.Length == 0
            ? $"<w:r><w:rPr>{Times12}</w:rPr><w:t/></w:r>"
            : $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{cached}</w:t></w:r>") +
        $"<w:r><w:rPr>{Times12}</w:rPr><w:fldChar w:fldCharType=\"end\"/></w:r>";

    /// <summary>What one field shows, with the record given or with none at all.</summary>
    private static string Shows(string instruction, MailMergeRecord? record = null, string cached = "")
    {
        var layout = LayoutOf(
            new DocxBuilder().AddRawParagraph(
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>{Field(instruction, cached)}</w:p>"),
            Options(record));

        return string.Concat(layout.Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text)).Trim();
    }

    private static MailMergeRecord Record() => new(new Dictionary<string, string>
    {
        ["FirstName"] = "Ada",
        ["Surname"] = "Lovelace",
        ["Company Name"] = "Difference Ltd",
        ["Title"] = "Countess",
        ["Middle"] = string.Empty
    });

    // ----- a document with nothing to fill it from -----

    [Theory]
    [InlineData(" MERGEFIELD FirstName ", "«FirstName»")]
    [InlineData(" MERGEFIELD \"Company Name\" ", "«Company Name»")]
    [InlineData(" MERGEFIELD Surname \\* MERGEFORMAT ", "«Surname»")]
    [InlineData(" MERGEREC ", "«Merge Record #»")]
    [InlineData(" MERGESEQ ", "«Merge Sequence #»")]
    public void An_unmerged_field_shows_its_own_name(string instruction, string expected)
    {
        Assert.Equal(expected, Shows(instruction));
    }

    /// <summary>
    /// A case switch reaches the name inside the guillemets, which have no case of their own.
    /// </summary>
    [Fact]
    public void A_case_switch_reaches_the_name_it_stands_for()
    {
        Assert.Equal("«SURNAME»", Shows(" MERGEFIELD Surname \\* Upper "));
    }

    /// <summary>
    /// The text a field asks to be printed before and after it is printed around the placeholder
    /// too, so a letter reading "Dear «Title»," reads that way before it is merged.
    /// </summary>
    [Fact]
    public void What_goes_around_a_field_goes_around_its_placeholder()
    {
        Assert.Equal("Dear «Title»,", Shows(" MERGEFIELD Title \\b \"Dear \" \\f \",\" "));
    }

    /// <summary>
    /// The fields that carry a merge from one record to the next show nothing where they stand,
    /// which is what Word draws for them.
    /// </summary>
    [Theory]
    [InlineData(" NEXT ")]
    [InlineData(" NEXTIF 1 = 1 ")]
    [InlineData(" SKIPIF 1 = 2 ")]
    public void The_fields_that_move_a_merge_along_show_nothing(string instruction)
    {
        Assert.Equal("", Shows(instruction, cached: "cached"));
    }

    // ----- a document given a record to fill it from -----

    [Fact]
    public void A_field_given_a_record_shows_what_the_record_holds()
    {
        var record = Record();

        Assert.Equal("Ada", Shows(" MERGEFIELD FirstName ", record));
        Assert.Equal("Difference Ltd", Shows(" MERGEFIELD \"Company Name\" ", record));
        Assert.Equal("LOVELACE", Shows(" MERGEFIELD Surname \\* Upper ", record));
    }

    /// <summary>
    /// The text around a field is only printed where the field has something to print: a record
    /// whose middle name is empty gives a line with nothing of it, brackets and all.
    /// </summary>
    [Fact]
    public void What_goes_around_a_field_goes_only_where_there_is_something_to_surround()
    {
        var record = Record();

        Assert.Equal("Dear Countess,", Shows(" MERGEFIELD Title \\b \"Dear \" \\f \",\" ", record));
        Assert.Equal("", Shows(" MERGEFIELD Middle \\b \"(\" \\f \")\" ", record));
    }

    /// <summary>
    /// A field the record has never heard of keeps its own name, rather than coming out empty: a
    /// letter missing a field says which one is missing.
    /// </summary>
    [Fact]
    public void A_field_the_record_does_not_have_keeps_its_name()
    {
        Assert.Equal("«Postcode»", Shows(" MERGEFIELD Postcode ", Record()));
    }

    [Fact]
    public void The_record_and_sequence_numbers_are_the_records_own()
    {
        var record = Record();
        record.Number = 4;
        record.Sequence = 2;

        Assert.Equal("4", Shows(" MERGEREC ", record));
        Assert.Equal("2", Shows(" MERGESEQ ", record));
    }

    /// <summary>
    /// A question the merge would have asked whoever ran it: nothing here can ask, so the answer
    /// it was given a default of stands.
    /// </summary>
    [Fact]
    public void A_question_shows_the_answer_it_was_given_a_default_of()
    {
        Assert.Equal("today", Shows(" FILLIN \"What is the date?\" \\d \"today\" "));

        // And one with no default keeps whatever the document last showed.
        Assert.Equal("cached", Shows(" FILLIN \"What is the date?\" ", cached: "cached"));
    }

    /// <summary>
    /// The whole of a letter, which is what this is for: the same document converted twice, once
    /// as it stands and once filled in.
    /// </summary>
    [Fact]
    public void A_letter_reads_as_itself_and_then_as_a_letter()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
            Field(" MERGEFIELD Title \\b \"Dear \" \\f \" \" ") +
            Field(" MERGEFIELD Surname \\f \",\" ") + "</w:p>");

        static string TextOf(LaidOutDocument layout) =>
            string.Concat(layout.Pages[0].Lines.SelectMany(l => l.Texts).Select(t => t.Text)).Trim();

        Assert.Equal("Dear «Title» «Surname»,", TextOf(LayoutOf(builder, Options())));
        Assert.Equal("Dear Countess Lovelace,", TextOf(LayoutOf(builder, Options(Record()))));
    }

    [Fact]
    public void The_fixture_shows_what_word_shows_for_an_unmerged_document()
    {
        var lines = LayoutOf(Fixtures.Build("merge"), Options()).Pages[0].Lines
            .Select(line => string.Concat(line.Texts.OrderBy(t => t.X).Select(t => t.Text)).Trim())
            .ToList();

        Assert.Contains("plain: «FirstName»", lines);
        Assert.Contains("upper: «SURNAME»", lines);
        Assert.Contains("before-and-after: Dear «Title»,", lines);
        Assert.Contains("record: «Merge Record #»", lines);

        // And the same fixture filled in reads as a letter would.
        var merged = LayoutOf(Fixtures.Build("merge"), Options(Record())).Pages[0].Lines
            .Select(line => string.Concat(line.Texts.OrderBy(t => t.X).Select(t => t.Text)).Trim())
            .ToList();

        Assert.Contains("plain: Ada", merged);
        Assert.Contains("before-and-after: Dear Countess,", merged);
        Assert.Contains("record: 1", merged);
    }
}
