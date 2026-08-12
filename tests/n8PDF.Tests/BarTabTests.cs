using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests the bar tab stop, which is not a place for text to land at all: it asks for a vertical
/// rule down every line of the paragraph that declares it.
/// </summary>
public class BarTabTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private const double Margin = 72;

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

    /// <summary>A paragraph with one bar stop an inch and a half in.</summary>
    private static LaidOutDocument WithBar(string text, int positionTwips = 2160) =>
        LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr><w:tabs><w:tab w:val=\"bar\" w:pos=\"{positionTwips}\"/></w:tabs>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{text}</w:t></w:r></w:p>"));

    /// <summary>The vertical rules of a page: tall, thin, and nothing else on these pages.</summary>
    private static List<PositionedRectangle> Rules(LaidOutPage page) =>
        page.Rectangles.Where(r => r.Width < 2).OrderBy(r => r.Y).ThenBy(r => r.X).ToList();

    [Fact]
    public void A_bar_stop_draws_a_rule_at_its_position()
    {
        var layout = WithBar("Short.");

        var rule = Assert.Single(Rules(layout.Pages[0]));
        Assert.Equal(Margin + 108, rule.X, 2);
        Assert.Equal(0.24, rule.Width, 3);
    }

    /// <summary>
    /// The rule has nothing to do with tab characters: a paragraph that declares a bar stop and
    /// never tabs still gets one, on every line it runs to.
    /// </summary>
    [Fact]
    public void It_is_drawn_on_every_line_of_the_paragraph()
    {
        var layout = WithBar(
            "A paragraph with no tab character in it at all, written long enough that it has to " +
            "run to more than one line of the page it is set on.");

        var page = layout.Pages[0];

        Assert.True(page.Lines.Count > 1, "the paragraph did not wrap");
        Assert.Equal(page.Lines.Count, Rules(page).Count);
    }

    [Fact]
    public void It_spans_the_line_it_is_drawn_on()
    {
        var layout = WithBar("Short.");

        var line = layout.Pages[0].Lines[0];
        var rule = Rules(layout.Pages[0])[0];

        // From the top of the line box to the bottom of it.
        Assert.Equal(line.BaselineY - line.Ascent, rule.Y, 2);
        Assert.Equal(line.Height, rule.Height, 2);
    }

    /// <summary>An empty paragraph still has a line box, so it still has its rule.</summary>
    [Fact]
    public void An_empty_paragraph_is_still_ruled()
    {
        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr><w:tabs><w:tab w:val=\"bar\" w:pos=\"2160\"/></w:tabs>{ZeroSpacing}</w:pPr></w:p>"));

        var rule = Assert.Single(Rules(layout.Pages[0]));
        Assert.True(rule.Height > 5, $"the rule is only {rule.Height:0.##}pt tall");
    }

    [Fact]
    public void Two_bar_stops_draw_two_rules()
    {
        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(
            "<w:p><w:pPr><w:tabs>" +
            "<w:tab w:val=\"bar\" w:pos=\"1440\"/><w:tab w:val=\"bar\" w:pos=\"5760\"/>" +
            $"</w:tabs>{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr><w:t>Two.</w:t></w:r></w:p>"));

        var rules = Rules(layout.Pages[0]);

        Assert.Equal(2, rules.Count);
        Assert.Equal(Margin + 72, rules[0].X, 2);
        Assert.Equal(Margin + 288, rules[1].X, 2);
    }

    [Fact]
    public void A_paragraph_without_one_is_not_ruled()
    {
        var layout = LayoutOf(new DocxBuilder().AddParagraph("Plain.", ZeroSpacing, Times12));
        Assert.Empty(Rules(layout.Pages[0]));
    }

    /// <summary>
    /// The rule belongs to the paragraph that declares it, so the one after it is left alone.
    /// </summary>
    [Fact]
    public void It_does_not_carry_to_the_next_paragraph()
    {
        var layout = LayoutOf(new DocxBuilder()
            .AddRawParagraph(
                $"<w:p><w:pPr><w:tabs><w:tab w:val=\"bar\" w:pos=\"2160\"/></w:tabs>{ZeroSpacing}</w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Ruled.</w:t></w:r></w:p>")
            .AddParagraph("Not ruled.", ZeroSpacing, Times12));

        var rule = Assert.Single(Rules(layout.Pages[0]));
        var ruled = layout.Pages[0].Lines[0];

        Assert.Equal(ruled.BaselineY - ruled.Ascent, rule.Y, 2);
    }

    /// <summary>
    /// Compares the rules against Word's own. Word strokes them rather than filling, which is why
    /// the harness reads strokes as well as fills — without that this document looks to it like a
    /// page with no rules on it.
    /// </summary>
    [Fact]
    public void Bars_match_word()
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, "tab-bars.pdf");
        Assert.True(File.Exists(referencePath), $"No Word reference PDF at {referencePath}");

        var ours = BarsOf(PdfPathExtractor.Extract(Converter.Convert(Fixtures.Build("tab-bars"), Options())));
        var theirs = BarsOf(PdfPathExtractor.ExtractFile(referencePath));

        Assert.NotEmpty(theirs);
        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            Assert.Equal(theirs[i].Left, ours[i].Left, 2);
            Assert.Equal(theirs[i].Width, ours[i].Width, 2);

            // Word quantizes vertical positions to 1/300 inch, which is the floor on both of these.
            Assert.True(Math.Abs(ours[i].Top - theirs[i].Top) <= 0.25,
                $"rule {i + 1} starts at {ours[i].Top:0.###} against Word's {theirs[i].Top:0.###}");

            Assert.True(Math.Abs(ours[i].Height - theirs[i].Height) <= 0.25,
                $"rule {i + 1} is {ours[i].Height:0.###}pt tall against Word's {theirs[i].Height:0.###}");
        }
    }

    private static List<ExtractedRectangle> BarsOf(IEnumerable<ExtractedRectangle> rectangles) =>
        rectangles
            .Where(r => r is { Width: > 0 and < 2, Height: > 5 })
            .OrderBy(r => r.PageIndex).ThenBy(r => r.Top).ThenBy(r => r.Left)
            .ToList();

    [Fact]
    public void The_fixture_rules_every_line_that_asked()
    {
        var layout = LayoutOf(Fixtures.Build("tab-bars"));
        var rules = Rules(layout.Pages[0]);

        // Two lines of the first paragraph, one each for the large and the two-bar rows — that
        // one twice over — and one for the empty paragraph. The plain paragraph gets none.
        Assert.Equal(6, rules.Count);
    }
}
