using n8PDF;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests list numbering: the counters, the formats, and where the label lands on the page.
/// </summary>
public class NumberingTests
{
    private const string Times12 = "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    /// <summary>The text of each line, label included, in order.</summary>
    private static List<string> LinesOf(LaidOutDocument document) =>
        document.Pages.SelectMany(p => p.Lines)
            .Where(l => l.Texts.Count > 0)
            .Select(l => string.Concat(l.Texts.Select(t => t.Text)))
            .ToList();

    [Theory]
    [InlineData(1, NumberFormat.Decimal, "1")]
    [InlineData(4, NumberFormat.Decimal, "4")]
    [InlineData(3, NumberFormat.DecimalZero, "03")]
    [InlineData(1, NumberFormat.LowerLetter, "a")]
    [InlineData(26, NumberFormat.LowerLetter, "z")]
    [InlineData(27, NumberFormat.LowerLetter, "aa")]
    [InlineData(2, NumberFormat.UpperLetter, "B")]
    [InlineData(4, NumberFormat.LowerRoman, "iv")]
    [InlineData(9, NumberFormat.UpperRoman, "IX")]
    [InlineData(1990, NumberFormat.UpperRoman, "MCMXC")]
    public void Numbers_render_in_their_declared_format(int value, NumberFormat format, string expected) =>
        Assert.Equal(expected, NumberFormatter.Format(value, format));

    [Fact]
    public void A_simple_list_counts_from_one()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(DocxBuilder.NumberingLevel(0, "decimal", "%1."))
            .AddListParagraph("First", numId: 1, runProperties: Times12)
            .AddListParagraph("Second", numId: 1, runProperties: Times12)
            .AddListParagraph("Third", numId: 1, runProperties: Times12));

        Assert.Equal(["1.First", "2.Second", "3.Third"], LinesOf(document));
    }

    [Fact]
    public void A_list_can_start_at_something_other_than_one()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(DocxBuilder.NumberingLevel(0, "decimal", "%1.", start: 5))
            .AddListParagraph("First", numId: 1, runProperties: Times12)
            .AddListParagraph("Second", numId: 1, runProperties: Times12));

        Assert.Equal(["5.First", "6.Second"], LinesOf(document));
    }

    [Fact]
    public void Nested_levels_count_independently_and_restart()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(
                DocxBuilder.NumberingLevel(0, "decimal", "%1.") +
                DocxBuilder.NumberingLevel(1, "lowerLetter", "%2)"))
            .AddListParagraph("One", 1, runProperties: Times12)
            .AddListParagraph("One A", 1, level: 1, runProperties: Times12)
            .AddListParagraph("One B", 1, level: 1, runProperties: Times12)
            .AddListParagraph("Two", 1, runProperties: Times12)
            .AddListParagraph("Two A", 1, level: 1, runProperties: Times12));

        // The inner counter restarts when the outer one advances, which is the whole point of
        // resetting deeper levels rather than letting them run on.
        Assert.Equal(["1.One", "a)One A", "b)One B", "2.Two", "a)Two A"], LinesOf(document));
    }

    [Fact]
    public void A_multi_level_template_shows_the_levels_above_it()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(
                DocxBuilder.NumberingLevel(0, "decimal", "%1.") +
                DocxBuilder.NumberingLevel(1, "decimal", "%1.%2."))
            .AddListParagraph("One", 1, runProperties: Times12)
            .AddListParagraph("One one", 1, level: 1, runProperties: Times12)
            .AddListParagraph("One two", 1, level: 1, runProperties: Times12)
            .AddListParagraph("Two", 1, runProperties: Times12)
            .AddListParagraph("Two one", 1, level: 1, runProperties: Times12));

        Assert.Equal(["1.One", "1.1.One one", "1.2.One two", "2.Two", "2.1.Two one"], LinesOf(document));
    }

    [Fact]
    public void Two_lists_sharing_a_document_count_separately()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(
                DocxBuilder.NumberingLevel(0, "decimal", "%1."),
                DocxBuilder.NumberingLevel(0, "decimal", "%1."))
            .AddListParagraph("First list one", 1, runProperties: Times12)
            .AddListParagraph("Second list one", 2, runProperties: Times12)
            .AddListParagraph("First list two", 1, runProperties: Times12));

        // Interleaving the two must not make either of them skip.
        Assert.Equal(["1.First list one", "1.Second list one", "2.First list two"], LinesOf(document));
    }

    [Fact]
    public void Bullets_use_their_character_rather_than_a_counter()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(DocxBuilder.NumberingLevel(0, "bullet", "•"))
            .AddListParagraph("First", 1, runProperties: Times12)
            .AddListParagraph("Second", 1, runProperties: Times12));

        Assert.Equal(["•First", "•Second"], LinesOf(document));
    }

    [Fact]
    public void The_label_hangs_left_of_the_text_it_belongs_to()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(DocxBuilder.NumberingLevel(0, "decimal", "%1."))
            .AddListParagraph(
                "An item long enough to wrap onto a second line, which is what shows that the "
                + "continuation aligns with the text rather than with the label hanging back from it.",
                1, runProperties: Times12));

        var page = document.Pages[0];
        var lines = page.Lines.Where(l => l.Texts.Count > 0).ToList();

        // Level zero indents the text half an inch and hangs the label a quarter inch back, so
        // the label starts at 72 + 36 - 18 and the text at 72 + 36.
        Assert.Equal(72 + 18, lines[0].Texts[0].X, 1);

        var textAfterLabel = lines[0].Texts.First(t => !t.Text.StartsWith('1'));
        Assert.Equal(72 + 36, textAfterLabel.X, 1);

        // The continuation lines align with the text, not with the label.
        Assert.True(lines.Count > 1, "the item should wrap");
        Assert.Equal(72 + 36, lines[1].Texts[0].X, 1);
    }

    [Fact]
    public void Deeper_levels_indent_further()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(
                DocxBuilder.NumberingLevel(0, "decimal", "%1.") +
                DocxBuilder.NumberingLevel(1, "decimal", "%2."))
            .AddListParagraph("Outer", 1, runProperties: Times12)
            .AddListParagraph("Inner", 1, level: 1, runProperties: Times12));

        var lines = document.Pages[0].Lines.Where(l => l.Texts.Count > 0).ToList();

        Assert.Equal(72 + 18, lines[0].Texts[0].X, 1);   // level 0: indent 36, hanging 18
        Assert.Equal(72 + 54, lines[1].Texts[0].X, 1);   // level 1: indent 72, hanging 18
    }

    [Fact]
    public void A_space_suffix_is_used_instead_of_a_tab_when_asked_for()
    {
        var document = LayoutOf(new DocxBuilder()
            .WithNumbering(DocxBuilder.NumberingLevel(0, "decimal", "%1.", suffix: "space"))
            .AddListParagraph("Item", 1, runProperties: Times12));

        var line = document.Pages[0].Lines.First(l => l.Texts.Count > 0);
        var text = string.Concat(line.Texts.Select(t => t.Text));

        // With a space the text follows the label directly rather than being carried to the
        // paragraph indent, so it starts well left of where a tab would have put it.
        Assert.Equal("1. Item", text);

        var item = line.Texts.Last();
        Assert.True(item.X < 72 + 36, $"text at {item.X:0.##} should not have been tabbed to the indent");
    }

    [Fact]
    public void A_paragraph_referring_to_a_missing_list_is_left_unnumbered()
    {
        // A numPr pointing at a numId with no definition must not invent a label or fail.
        var document = LayoutOf(new DocxBuilder()
            .AddListParagraph("Orphan", numId: 99, runProperties: Times12));

        Assert.Equal(["Orphan"], LinesOf(document));
    }

    [Fact]
    public void Numbering_survives_the_round_trip_to_pdf()
    {
        var pdf = Converter.Convert(Fixtures.Build("numbering"), Options());
        var text = string.Concat(Support.PdfReading.PdfTextExtractor.Extract(pdf).Select(r => r.Text));

        Assert.Contains("1.", text);
        Assert.Contains("2.", text);
        Assert.Contains("•", text);
    }
}
