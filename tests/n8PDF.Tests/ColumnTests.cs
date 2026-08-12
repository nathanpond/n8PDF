using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests multi-column sections: how wide each column is, the order text fills them in, and the
/// rule Word draws down the gap.
/// </summary>
public class ColumnTests
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

    /// <summary>Each line of a page as its left edge, baseline and text.</summary>
    private static List<(double X, double Y, string Text)> LinesOf(LaidOutPage page) =>
        page.Lines
            .Where(l => l.Texts.Count > 0)
            .OrderBy(l => l.Texts[0].X).ThenBy(l => l.BaselineY)
            .Select(l => (l.Texts[0].X, l.BaselineY, string.Concat(l.Texts.Select(t => t.Text))))
            .ToList();

    /// <summary>A document of numbered single-line paragraphs in the given section.</summary>
    private static DocxBuilder Filled(string section, int paragraphs)
    {
        var builder = new DocxBuilder().WithSection(section);
        for (var i = 1; i <= paragraphs; i++)
            builder.AddParagraph($"Line {i}.", ZeroSpacing, Times12);

        return builder;
    }

    [Fact]
    public void Text_fills_one_column_before_starting_the_next()
    {
        // A column holds 46 lines of this size, so the forty-seventh opens the second one.
        var layout = LayoutOf(Filled(DocxBuilder.Section(columns: 2), 50));

        var page = Assert.Single(layout.Pages);
        var lines = LinesOf(page);

        var first = lines.Where(l => l.X < 300).ToList();
        var second = lines.Where(l => l.X >= 300).ToList();

        Assert.Equal("Line 1.", first[0].Text);
        Assert.Equal("Line 46.", first[^1].Text);

        // The second column starts back at the top of the page, not below the first.
        Assert.Equal("Line 47.", second[0].Text);
        Assert.Equal(first[0].Y, second[0].Y, 2);
    }

    [Fact]
    public void Equal_columns_divide_what_the_gaps_leave()
    {
        var layout = LayoutOf(Filled(DocxBuilder.Section(columns: 2), 50));
        var lines = LinesOf(layout.Pages[0]);

        // 468pt of content less a 36pt gap, halved.
        Assert.Equal(72, lines[0].X, 2);
        Assert.Equal(324, lines.First(l => l.X >= 300).X, 2);
    }

    [Fact]
    public void Text_is_broken_against_the_column_rather_than_the_page()
    {
        var layout = LayoutOf(new DocxBuilder()
            .WithSection(DocxBuilder.Section(columns: 2))
            .AddParagraph(
                "A paragraph long enough to wrap inside a column but not across the whole page.",
                ZeroSpacing, Times12));

        var lines = LinesOf(layout.Pages[0]);

        Assert.True(lines.Count > 1, "the paragraph did not wrap, so the whole measure was used");
        Assert.All(lines, line => Assert.Equal(72, line.X, 2));
        Assert.All(layout.Pages[0].Lines, line => Assert.True(line.Texts.Sum(t => t.Width) <= 216.5));
    }

    [Fact]
    public void Stated_widths_are_used_as_given()
    {
        var layout = LayoutOf(Fixtures.Build("columns-uneven"));
        var lines = LinesOf(layout.Pages[0]);

        // Four inches, then an inch and a half, then two inches, each with a half-inch gap.
        Assert.Equal([72, 252, 396], lines.Select(l => l.X).Distinct().Order().ToList());
    }

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }

    /// <summary>
    /// A column break moves to the next column, and — the part that is easy to get wrong — the
    /// line after it is wrapped against the column it lands in rather than the one it left.
    /// </summary>
    [Fact]
    public void Column_break_moves_on_and_the_next_line_takes_the_new_measure()
    {
        var layout = LayoutOf(new DocxBuilder()
            .WithSection(DocxBuilder.Section(
                columns: 2, columnWidths: [(1440, 720), (7920, 0)]))
            .AddRawParagraph(
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Narrow.</w:t></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:br w:type=\"column\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>A line that would wrap in the narrow " +
                "column.</w:t></w:r></w:p>"));

        var lines = LinesOf(layout.Pages[0]);

        Assert.Equal("Narrow.", lines[0].Text);
        Assert.Equal(72, lines[0].X, 2);

        // One line, in the wide column: composed against 396pt rather than the 72pt it left.
        var wide = lines.Where(l => l.X > 100).ToList();
        Assert.Single(wide);
        Assert.Equal(180, wide[0].X, 2);
    }

    [Fact]
    public void Filling_the_last_column_starts_a_new_page()
    {
        var layout = LayoutOf(Filled(DocxBuilder.Section(columns: 2), 100));

        Assert.Equal(2, layout.Pages.Count);

        var overflow = LinesOf(layout.Pages[1]);
        Assert.Equal("Line 93.", overflow[0].Text);

        // And it starts in the first column again, at the top.
        Assert.Equal(72, overflow[0].X, 2);
        Assert.Equal(LinesOf(layout.Pages[0])[0].Y, overflow[0].Y, 2);
    }

    [Fact]
    public void A_page_break_inside_a_column_starts_the_next_page_in_the_first_one()
    {
        var layout = LayoutOf(new DocxBuilder()
            .WithSection(DocxBuilder.Section(columns: 2))
            .AddRawParagraph(
                $"<w:p><w:pPr>{ZeroSpacing}</w:pPr>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>First.</w:t></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:br w:type=\"column\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Second column.</w:t></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:br w:type=\"page\"/></w:r>" +
                $"<w:r><w:rPr>{Times12}</w:rPr><w:t>Overleaf.</w:t></w:r></w:p>"));

        Assert.Equal(2, layout.Pages.Count);

        var overleaf = Assert.Single(LinesOf(layout.Pages[1]));
        Assert.Equal("Overleaf.", overleaf.Text);
        Assert.Equal(72, overleaf.X, 2);
    }

    [Fact]
    public void Separator_is_drawn_only_where_the_text_reached()
    {
        // Two columns' worth of text: one rule.
        var used = LayoutOf(Filled(DocxBuilder.Section(columns: 2, columnSeparator: true), 50));
        var rule = Assert.Single(SeparatorsOf(used.Pages[0]));

        Assert.Equal(306, rule.X + rule.Width / 2, 1);
        Assert.Equal(72, rule.Y, 1);

        // A page whose text never left the first column gets none, which is what Word does.
        var unused = LayoutOf(Filled(DocxBuilder.Section(columns: 2, columnSeparator: true), 3));
        Assert.Empty(SeparatorsOf(unused.Pages[0]));
    }

    [Fact]
    public void No_separator_unless_the_document_asks_for_one()
    {
        var layout = LayoutOf(Filled(DocxBuilder.Section(columns: 2), 50));
        Assert.Empty(SeparatorsOf(layout.Pages[0]));
    }

    /// <summary>
    /// Compares the column rule against Word's own. It carries no text, so nothing else in the
    /// harness can see where it starts, how far down it runs, or whether it is there at all.
    /// </summary>
    [Fact]
    public void Separator_matches_word()
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, "columns.pdf");
        Assert.True(File.Exists(referencePath), $"No Word reference PDF at {referencePath}");

        var ours = ColumnRulesOf(PdfPathExtractor.Extract(Converter.Convert(Fixtures.Build("columns"), Options())));
        var theirs = ColumnRulesOf(PdfPathExtractor.ExtractFile(referencePath));

        Assert.NotEmpty(theirs);
        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            Assert.Equal(theirs[i].PageIndex, ours[i].PageIndex);
            Assert.Equal(theirs[i].Left, ours[i].Left, 2);
            Assert.Equal(theirs[i].Width, ours[i].Width, 2);
            Assert.Equal(theirs[i].Top, ours[i].Top, 2);

            // The rule stops at the bottom of the fullest column, so its length is only as good
            // as the agreement about how much text that column took.
            Assert.True(Math.Abs(ours[i].Height - theirs[i].Height) <= 0.5,
                $"the rule is {ours[i].Height:0.###}pt long against Word's {theirs[i].Height:0.###}");
        }
    }

    /// <summary>The tall, thin rectangles of a page: the column rules and nothing else.</summary>
    private static List<PositionedRectangle> SeparatorsOf(LaidOutPage page) =>
        page.Rectangles.Where(r => r is { Width: < 2, Height: > 20 }).ToList();

    private static List<ExtractedRectangle> ColumnRulesOf(IEnumerable<ExtractedRectangle> rectangles) =>
        rectangles
            .Where(r => r is { Width: > 0 and < 2, Height: > 20 })
            .OrderBy(r => r.PageIndex).ThenBy(r => r.Left)
            .ToList();
}
