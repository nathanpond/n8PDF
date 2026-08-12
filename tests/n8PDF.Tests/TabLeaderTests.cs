using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests tab leaders: the run of dots, hyphens or underscores a stop can ask for to fill the gap
/// it opens, which is what makes a table of contents readable across the page.
/// </summary>
public class TabLeaderTests
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

    /// <summary>An entry of a table of contents: a title, a tab, and a page number.</summary>
    private static LaidOutDocument Entry(
        string title, string leader = "dot", string alignment = "right", int positionTwips = 6480)
    {
        var stop = $"<w:tab w:val=\"{alignment}\" w:leader=\"{leader}\" w:pos=\"{positionTwips}\"/>";

        return LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr><w:tabs>{stop}</w:tabs>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>{title}</w:t><w:tab/><w:t>12</w:t></w:r></w:p>"));
    }

    /// <summary>The leader run of the first line, if there is one.</summary>
    private static PositionedText? Leader(LaidOutDocument layout) =>
        layout.Pages[0].Lines[0].Texts.FirstOrDefault(IsLeader);

    /// <summary>A run of one repeated character that is not a letter or a digit.</summary>
    private static bool IsLeader(PositionedText text) =>
        text.Text.Length > 1 && text.Text.Distinct().Count() == 1 && !char.IsLetterOrDigit(text.Text[0]);

    [Fact]
    public void A_dotted_stop_fills_its_gap_with_dots()
    {
        var leader = Leader(Entry("Chapter One"));

        Assert.NotNull(leader);
        Assert.All(leader.Text, c => Assert.Equal('.', c));
    }

    [Theory]
    [InlineData("dot", '.')]
    [InlineData("hyphen", '-')]
    [InlineData("underscore", '_')]
    [InlineData("middleDot", '·')]
    // Word draws a heavy leader with the same glyph as a plain underscore.
    [InlineData("heavy", '_')]
    public void Each_kind_has_its_own_character(string leader, char expected)
    {
        var run = Leader(Entry("Chapter One", leader));

        Assert.NotNull(run);
        Assert.All(run.Text, c => Assert.Equal(expected, c));
    }

    [Fact]
    public void A_stop_with_no_leader_fills_nothing()
    {
        var layout = Entry("Chapter One", "none");

        Assert.Null(Leader(layout));
        Assert.Equal("Chapter One12", string.Concat(layout.Pages[0].Lines[0].Texts.Select(t => t.Text)));
    }

    [Fact]
    public void The_leader_stops_where_the_text_after_the_tab_begins()
    {
        var layout = Entry("Chapter One");

        var leader = Leader(layout)!;
        var number = layout.Pages[0].Lines[0].Texts.Single(t => t.Text == "12");

        // Whole characters only, so the run ends at or just before the number.
        Assert.InRange(number.X - (leader.X + leader.Width), 0, 3.01);

        // And the number is still right-aligned on the stop.
        Assert.Equal(72 + 324, number.X + number.Width, 2);
    }

    /// <summary>
    /// The characters sit on a grid of their own width measured from the edge of the page, so two
    /// entries whose titles are different lengths still have their dots in the same columns.
    /// </summary>
    [Fact]
    public void Leaders_on_different_lines_line_up_with_each_other()
    {
        var dotWidth = 3.0;

        var shortTitle = Leader(Entry("A"))!;
        var longTitle = Leader(Entry("A rather longer chapter title"))!;

        Assert.Equal(0, shortTitle.X % dotWidth, 2);
        Assert.Equal(0, longTitle.X % dotWidth, 2);

        // Both end in the same place, against the same stop.
        Assert.Equal(shortTitle.X + shortTitle.Width, longTitle.X + longTitle.Width, 2);
    }

    [Fact]
    public void A_leader_on_a_left_stop_fills_up_to_the_stop()
    {
        var layout = Entry("Left", alignment: "left", positionTwips: 4320);

        var leader = Leader(layout)!;
        var after = layout.Pages[0].Lines[0].Texts.Single(t => t.Text == "12");

        Assert.Equal(72 + 216, after.X, 2);
        Assert.InRange(after.X - (leader.X + leader.Width), 0, 3.01);
    }

    [Fact]
    public void A_leader_on_a_centre_stop_fills_up_to_the_centred_text()
    {
        var layout = Entry("Centred", alignment: "center", positionTwips: 4320);

        var leader = Leader(layout)!;
        var after = layout.Pages[0].Lines[0].Texts.Single(t => t.Text == "12");

        // Centred on the stop, with the dots stopping short of it.
        Assert.Equal(72 + 216, after.X + after.Width / 2, 2);
        Assert.InRange(after.X - (leader.X + leader.Width), 0, 3.01);
    }

    [Fact]
    public void A_gap_too_narrow_for_one_character_is_left_empty()
    {
        // The stop is barely past the title, so no whole dot fits in what is left.
        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr><w:tabs><w:tab w:val=\"left\" w:leader=\"dot\" w:pos=\"1450\"/></w:tabs>{ZeroSpacing}</w:pPr>" +
            $"<w:r><w:rPr>{Times12}</w:rPr><w:t>A title of about an inch</w:t><w:tab/><w:t>12</w:t></w:r></w:p>"));

        Assert.Null(Leader(layout));
    }

    [Fact]
    public void The_fixture_fills_every_gap_it_asks_to()
    {
        var layout = LayoutOf(Fixtures.Build("tab-leaders"));
        var lines = layout.Pages[0].Lines;

        Assert.Equal(7, lines.Count);

        // Every row asked for a leader, and each got one of at least a few characters.
        var leaders = lines.Select(l => l.Texts.SingleOrDefault(IsLeader)).ToList();
        Assert.All(leaders, leader =>
        {
            Assert.NotNull(leader);
            Assert.True(leader.Text.Length > 3, $"only {leader.Text.Length} character(s) of leader");
        });

        // Each row asks for a different character, and the last two repeat the first.
        Assert.Equal(['.', '-', '_', '·', '_', '.', '.'], leaders.Select(l => l!.Text[0]).ToList());
    }

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }
}
