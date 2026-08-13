using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests text that runs right to left, laid out rather than merely resolved.
/// </summary>
/// <remarks>
/// The algorithm that decides which way each character runs is tested against another
/// implementation of it in <see cref="BidiTests"/>. What is tested here is the rest: that a
/// paragraph told to run right to left begins at the right, that its words are drawn from there
/// leftwards, that a word is drawn from its own far end, and that what has a direction of its own
/// — a number, a Latin name — keeps it.
/// </remarks>
public class HebrewTests(ITestOutputHelper output)
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\" w:cs=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string Spacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>"shalom" and "olam" — hello, world.</summary>
    private const string Shalom = "שלום";

    private const string Olam = "עולם";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    /// <summary>One paragraph of the given text, running whichever way is asked.</summary>
    private static byte[] Page(string text, bool rightToLeft)
    {
        var builder = new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{(rightToLeft ? "<w:bidi/>" : string.Empty)}{Spacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>");

        return Converter.Convert(builder.Build(), Options());
    }

    /// <summary>
    /// What is drawn on the page, in the order it is drawn: leftmost first.
    /// </summary>
    /// <remarks>
    /// Read back out of the PDF rather than off the layout, because the layout no longer holds the
    /// answer. A run is stored in the order it is read — the only order it can be shaped in, since
    /// which letter joins to which and which mark belongs to which letter are facts about the text
    /// — and it is the glyphs that are turned round on their way to the page. What is drawn is
    /// therefore only visible where the drawing is.
    /// </remarks>
    private static List<(double X, string Text)> Drawn(byte[] pdf) =>
        Support.PdfReading.PdfTextExtractor.Extract(pdf)
            .OrderBy(run => run.X)
            .Select(run => (run.X, run.Text))
            .ToList();

    /// <summary>
    /// A word of Hebrew is drawn from its own far end: the first character stored is the rightmost
    /// character drawn.
    /// </summary>
    [Fact]
    public void A_hebrew_word_is_drawn_from_its_far_end()
    {
        var drawn = Drawn(Page(Shalom, rightToLeft: true));

        var text = string.Concat(drawn.Select(piece => piece.Text));

        output.WriteLine($"stored {Shalom}, drawn {text}");

        // Stored shin-lamed-vav-mem, drawn mem-vav-lamed-shin.
        Assert.Equal(new string([.. Shalom.Reverse()]), text);
    }

    /// <summary>
    /// And the words of a line likewise: the first word stored is the rightmost word drawn.
    /// </summary>
    [Fact]
    public void The_first_word_of_a_hebrew_line_is_drawn_furthest_right()
    {
        var drawn = Drawn(Page($"{Shalom} {Olam}", rightToLeft: true));

        foreach (var piece in drawn) output.WriteLine($"{piece.X:0.##} \"{piece.Text}\"");

        var text = string.Concat(drawn.Select(piece => piece.Text));

        // Reading the drawn line from the left: olam reversed, then shalom reversed.
        Assert.Equal(new string([.. Olam.Reverse()]) + " " + new string([.. Shalom.Reverse()]), text);
    }

    /// <summary>
    /// A paragraph that runs right to left begins at the right margin, which is what a document
    /// means by asking for it and saying nothing about alignment.
    /// </summary>
    [Fact]
    public void A_paragraph_that_runs_right_to_left_begins_at_the_right()
    {
        var right = Drawn(Page($"{Shalom} {Olam}", rightToLeft: true));
        var left = Drawn(Page($"{Shalom} {Olam}", rightToLeft: false));

        var width = 612.0 - 144;

        output.WriteLine($"right to left starts at {right[0].X:0.##}, left to right at {left[0].X:0.##}");

        // The one ends at the right margin; the other begins at the left one.
        Assert.Equal(72, left[0].X, 1);
        Assert.True(right[0].X > 72 + width / 2, $"the line begins at {right[0].X:0.##}");
    }

    /// <summary>
    /// A number is written left to right whatever is around it, which is the whole reason the
    /// algorithm exists rather than a rule about paragraphs.
    /// </summary>
    [Fact]
    public void A_number_inside_hebrew_keeps_its_own_direction()
    {
        var drawn = Drawn(Page($"{Shalom} 8601 {Olam}", rightToLeft: true));

        var text = string.Concat(drawn.Select(piece => piece.Text));

        output.WriteLine($"drawn: {text}");

        // The digits read as they were written although everything around them was turned round.
        Assert.Contains("8601", text);

        // And they sit between the two Hebrew words, with the first of them on the right.
        // Told apart by their first letters, since both words end in the same one.
        var digits = drawn.First(piece => piece.Text.Contains('8'));
        var first = drawn.First(piece => piece.Text.Contains(Shalom[0]));
        var second = drawn.First(piece => piece.Text.Contains(Olam[0]));

        Assert.True(second.X < digits.X, "the second word is not to the left of the number");
        Assert.True(digits.X < first.X, "the number is not to the left of the first word");
    }

    /// <summary>
    /// Latin text inside a right-to-left paragraph keeps its own direction too, and is placed
    /// where the reader expects rather than reversed.
    /// </summary>
    [Fact]
    public void Latin_inside_hebrew_is_left_alone()
    {
        var drawn = Drawn(Page($"{Shalom} ISO {Olam}", rightToLeft: true));

        var text = string.Concat(drawn.Select(piece => piece.Text));

        output.WriteLine($"drawn: {text}");

        Assert.Contains("ISO", text);
        Assert.DoesNotContain("OSI", text);
    }

    /// <summary>
    /// A bracket faces the way the reader is going: what is stored as an opening bracket is drawn
    /// as a closing one where the line runs the other way.
    /// </summary>
    [Fact]
    public void Brackets_face_the_way_the_line_runs()
    {
        var drawn = Drawn(Page($"({Shalom})", rightToLeft: true));

        var text = string.Concat(drawn.Select(piece => piece.Text));

        output.WriteLine($"stored ({Shalom}), drawn {text}");

        // Drawn from the left: an opening bracket, the word reversed, a closing bracket — so the
        // pair still opens on the side the reader starts from.
        Assert.StartsWith("(", text);
        Assert.EndsWith(")", text);
    }

    /// <summary>
    /// Hebrew in an ordinary paragraph is still Hebrew: the direction of a paragraph says where
    /// its lines begin, not which way its characters run.
    /// </summary>
    [Fact]
    public void Hebrew_in_a_left_to_right_paragraph_is_still_drawn_right_to_left()
    {
        var drawn = Drawn(Page($"{Shalom} {Olam}", rightToLeft: false));

        var text = string.Concat(drawn.Select(piece => piece.Text));

        // The line begins at the left margin, and the Hebrew inside it is turned round.
        Assert.Equal(72, drawn[0].X, 1);
        Assert.Equal(new string([.. Olam.Reverse()]) + " " + new string([.. Shalom.Reverse()]), text);
    }

    /// <summary>
    /// The whole of it against Word: the fixture holds Hebrew alone, Hebrew with Latin, Hebrew
    /// with a number, brackets and punctuation, in paragraphs running both ways.
    /// </summary>
    [Fact]
    public void The_fixture_lines_begin_where_word_begins_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "hebrew.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("hebrew",
            Converter.Convert(Fixtures.Build("hebrew"), Options()),
            File.ReadAllBytes(reference));

        output.WriteLine(report.ToText());

        // Every line, including the ones that begin at the right margin, starts where Word starts
        // it. What is on them is compared by eye and by the algorithm's own tests: Word writes a
        // line of Hebrew as runs this reader cannot put back together exactly.
        Assert.Equal(0, report.UnmatchedCount);
        Assert.True(report.MaxAbsStartXDelta < 0.5,
            $"a line begins {report.MaxAbsStartXDelta:0.###}pt from where Word begins it");
    }
}
