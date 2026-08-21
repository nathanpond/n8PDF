using n8PDF;
using n8PDF.Layout;
using n8PDF.Ooxml;
using n8PDF.Styling;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests how tall a line is when what is on it is not all one size or one font.
/// </summary>
/// <remarks>
/// Three rules, all of them measured from Word's export of <c>superscript-probe</c> and
/// <c>numbering</c> rather than reasoned about, and none of them what this did before it asked.
/// They matter out of proportion to how they sound: a line that is a fraction of a point wrong is
/// invisible on its own and moves every line under it, so a page of forty ends up two or three
/// points out — which is what these fixtures were built to catch.
/// </remarks>
public class LineBoxTests(ITestOutputHelper output)
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static string Times(int halfPoints) =>
        $"<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"{halfPoints}\"/>";

    private const string Spacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>The height of the one line a document of one paragraph has.</summary>
    private double HeightOf(string runsXml)
    {
        var builder = new DocxBuilder().AddRawParagraph($"<w:p><w:pPr>{Spacing}</w:pPr>{runsXml}</w:p>");

        using var stream = builder.BuildStream();
        var layout = Converter.LayoutDocument(stream, new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var line = layout.Pages[0].Lines.Single();

        output.WriteLine($"ascent {line.Ascent:0.###}, height {line.Height:0.###}");

        return line.Height;
    }

    private static string Run(string properties, string text) =>
        $"<w:r><w:rPr>{properties}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r>";

    /// <summary>
    /// A raised or lowered run keeps the line box of the size it was given rather than the smaller
    /// size it is drawn at, so a superscript no bigger than the text around it costs the line
    /// nothing at all.
    /// </summary>
    [Theory]
    [InlineData("superscript")]
    [InlineData("subscript")]
    public void A_shifted_run_of_the_texts_own_size_leaves_the_line_alone(string alignment)
    {
        var plain = HeightOf(Run(Times12, "Text"));
        var shifted = HeightOf(
            Run(Times12, "Text") + Run($"{Times12}<w:vertAlign w:val=\"{alignment}\"/>", "8"));

        Assert.Equal(plain, shifted, 3);
    }

    /// <summary>
    /// And one given a larger size makes the line as tall as that size would, though it is drawn
    /// far smaller: the box is the size it was given.
    /// </summary>
    /// <summary>
    /// How far Word raises and lowers a run, against this engine's fitted share of the type size,
    /// at the sizes a document uses.
    /// </summary>
    /// <remarks>
    /// The numbers are Word's own, read from superscript-shift-probe, and the gaps are stated
    /// case by case rather than hidden behind one tolerance — because they are the point. A share
    /// of the size cannot follow a rule with a face in it: Times New Roman and Arial come within a
    /// step of the grid at every size here, and Calibri is three steps out at twenty-four point,
    /// where Word raises 0.33 of the size and this raises 0.358.
    ///
    /// ResolvedRunFormat.BaselineShiftPoints has the measurement and what was ruled out by it.
    /// </remarks>
    [Theory]
    [InlineData("Times New Roman", 8, 3.12, 0.24, 0.48, 0.24)]
    [InlineData("Times New Roman", 12, 4.08, 0.24, 0.96, 0)]
    [InlineData("Times New Roman", 24, 8.4, 0.24, 1.92, 0)]
    [InlineData("Arial", 8, 3.12, 0.24, 0.48, 0.24)]
    [InlineData("Arial", 12, 4.08, 0.24, 0.96, 0)]
    [InlineData("Arial", 24, 8.4, 0.24, 1.44, 0.48)]
    [InlineData("Calibri", 8, 2.4, 0.48, 0.48, 0.24)]
    [InlineData("Calibri", 12, 4.08, 0.24, 0.96, 0)]
    [InlineData("Calibri", 24, 7.92, 0.72, 2.4, 0.48)]
    public void A_raised_run_moves_about_as_far_as_word_moves_it(
        string face, double size, double raised, double raisedGap, double lowered, double loweredGap)
    {
        var format = new ResolvedRunFormat { FontFamily = face, FontSizePoints = size };

        // What reaches the page is the shift rounded to Word's grid, so that is what is compared.
        var up = Grid.Snap((format with { VerticalAlignment = VerticalTextAlignment.Superscript })
            .BaselineShiftPoints);
        var down = Grid.Snap(-(format with { VerticalAlignment = VerticalTextAlignment.Subscript })
            .BaselineShiftPoints);

        Assert.Equal(raisedGap, Math.Abs(up - raised), 2);
        Assert.Equal(loweredGap, Math.Abs(down - lowered), 2);
    }

    [Fact]
    public void A_shifted_run_of_a_larger_size_makes_the_line_that_size()
    {
        var plain12 = HeightOf(Run(Times12, "Text"));
        var plain40 = HeightOf(Run(Times(80), "Text"));

        var raised = HeightOf(Run(Times12, "Text") + Run($"{Times(80)}<w:vertAlign w:val=\"superscript\"/>", "8"));

        Assert.True(raised > plain12 * 2, $"a forty point superscript left the line {raised:0.##}pt");
        Assert.Equal(plain40, raised, 3);
    }

    /// <summary>
    /// A line's box is the tallest ascent over the deepest descent of what is on it, which is not
    /// the tallest of the runs' own boxes: two fonts whose boxes are each shorter than the result
    /// can make a line deeper than either of them would alone.
    /// </summary>
    [Fact]
    public void A_line_of_two_fonts_takes_the_ascent_of_one_and_the_descent_of_the_other()
    {
        if (!TestFonts.OfficeFontsAvailable)
        {
            Assert.False(TestFonts.OfficeFontsRequired, TestFonts.OfficeFontsUnavailableMessage);
            return;
        }

        const string calibri11 = "<w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/><w:sz w:val=\"22\"/>";

        var times = HeightOf(Run(Times12, "Text"));
        var calibri = HeightOf(Run(calibri11, "Text"));
        var both = HeightOf(Run(Times12, "Text") + Run(calibri11, " and"));

        output.WriteLine($"times {times:0.###}, calibri {calibri:0.###}, both {both:0.###}");

        // Times is the taller and Calibri the deeper, so the line of both is taller than either.
        Assert.True(both > times, $"the line of two fonts is {both:0.###} against Times' {times:0.###}");
        Assert.True(both > calibri, $"the line of two fonts is {both:0.###} against Calibri's {calibri:0.###}");
    }

    /// <summary>
    /// A list's number is drawn on the line without being part of its box, which is the one thing
    /// on a line that is not. A numbered paragraph in a font whose descent is deeper than the
    /// text's is the same height as the same paragraph without a number.
    /// </summary>
    [Fact]
    public void A_lists_number_is_not_part_of_the_line_it_is_drawn_on()
    {
        var plain = HeightOf(Run(Times12, "Item"));

        var builder = new DocxBuilder()
            .WithNumbering(DocxBuilder.NumberingLevel(0, "decimal", "%1."))
            .AddRawParagraph(
                $"<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr>{Spacing}</w:pPr>" +
                Run(Times12, "Item") + "</w:p>");

        using var stream = builder.BuildStream();
        var layout = Converter.LayoutDocument(stream, new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var numbered = layout.Pages[0].Lines.Single().Height;

        output.WriteLine($"plain {plain:0.###}, numbered {numbered:0.###}");

        Assert.Equal(plain, numbered, 3);
    }
}
