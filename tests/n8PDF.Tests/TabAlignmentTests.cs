using n8PDF;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests tab stops that align what follows them: centre, right and decimal. Unlike a left stop,
/// none of them can be resolved until the text after the tab has been measured.
/// </summary>
public class TabAlignmentTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    /// <summary>The left margin, which every position here is measured from.</summary>
    private const double Margin = 72;

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    /// <summary>A paragraph with one tab stop, some text, a tab, and some more.</summary>
    private static LaidOutDocument OneStop(string alignment, int positionTwips, string before, string after)
    {
        var stops = $"<w:tabs><w:tab w:val=\"{alignment}\" w:pos=\"{positionTwips}\"/></w:tabs>";

        return LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{stops}{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            $"<w:t>{before}</w:t><w:tab/><w:t>{after}</w:t></w:r></w:p>"));
    }

    /// <summary>The run of a line holding the given text, with its left edge and width.</summary>
    private static (double X, double Width) Run(LaidOutDocument layout, string text)
    {
        var run = layout.Pages[0].Lines
            .SelectMany(l => l.Texts)
            .Single(t => t.Text.Trim() == text);

        return (run.X, run.Width);
    }

    [Fact]
    public void A_centre_stop_centres_what_follows_it()
    {
        var layout = OneStop("center", 2880, "Left", "Centred");
        var (x, width) = Run(layout, "Centred");

        Assert.Equal(Margin + 144, x + width / 2, 2);
    }

    [Fact]
    public void A_right_stop_ends_what_follows_it_on_the_stop()
    {
        var layout = OneStop("right", 2880, "Left", "Right");
        var (x, width) = Run(layout, "Right");

        Assert.Equal(Margin + 144, x + width, 2);
    }

    [Fact]
    public void A_decimal_stop_puts_the_separator_on_the_stop()
    {
        // Both numerals end in the same two characters, so wherever the point of one lands the
        // other's lands too — which makes the two runs end together however wide their whole
        // parts are. That alone is the alignment; the second assertion pins it to the stop.
        var narrow = Run(OneStop("decimal", 2880, "Left", "1.5"), "1.5");
        var wide = Run(OneStop("decimal", 2880, "Left", "333.5"), "333.5");

        Assert.Equal(narrow.X + narrow.Width, wide.X + wide.Width, 2);

        // The same figures without their fraction, right-aligned, end where the point sits.
        var point = Run(OneStop("right", 2880, "Left", "1"), "1");
        Assert.Equal(Margin + 144, point.X + point.Width, 2);
        Assert.True(narrow.X + narrow.Width > Margin + 144, "the fraction is left of the stop");
    }

    /// <summary>
    /// A decimal stop with nothing to align falls back to the right edge, which is what keeps a
    /// "Total" line flush with the column of figures above it.
    /// </summary>
    [Fact]
    public void A_decimal_stop_right_aligns_a_run_with_no_separator()
    {
        var layout = OneStop("decimal", 2880, "Left", "Total");
        var (x, width) = Run(layout, "Total");

        Assert.Equal(Margin + 144, x + width, 2);
    }

    /// <summary>
    /// A stop the line has already passed cannot pull text backwards, so the tab takes the next
    /// stop instead — and where there is none, the next default one.
    /// </summary>
    [Fact]
    public void A_stop_already_passed_is_not_used()
    {
        var stops =
            "<w:tabs><w:tab w:val=\"center\" w:pos=\"720\"/><w:tab w:val=\"right\" w:pos=\"5760\"/></w:tabs>";

        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{stops}{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            "<w:t>A left run wider than the first stop</w:t><w:tab/><w:t>After</w:t></w:r></w:p>"));

        var (x, width) = Run(layout, "After");

        // The centre stop at half an inch is long gone, so the right stop at four inches takes it.
        Assert.Equal(Margin + 288, x + width, 2);
    }

    [Fact]
    public void A_bar_stop_is_not_a_tab_stop()
    {
        var stops =
            "<w:tabs><w:tab w:val=\"bar\" w:pos=\"1440\"/><w:tab w:val=\"right\" w:pos=\"4320\"/></w:tabs>";

        var layout = LayoutOf(new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr>{stops}{ZeroSpacing}</w:pPr><w:r><w:rPr>{Times12}</w:rPr>" +
            "<w:t>Left</w:t><w:tab/><w:t>After</w:t></w:r></w:p>"));

        var (x, width) = Run(layout, "After");

        // The tab passes through the bar at one inch and takes the right stop at three.
        Assert.Equal(Margin + 216, x + width, 2);
    }

    [Fact]
    public void Aligned_stops_reach_the_pdf()
    {
        var pdf = Converter.Convert(Fixtures.Build("tabs-aligned"), Options());
        var runs = PdfTextExtractor.Extract(pdf);

        // The right-hand column ends on the stop at six and a half inches from the margin.
        var lastOfFirstLine = runs
            .Where(r => Math.Abs(r.BaselineY - runs.Min(x => x.BaselineY)) < 0.5)
            .MaxBy(r => r.X);

        Assert.NotNull(lastOfFirstLine);
        Assert.Equal(Margin + 468, lastOfFirstLine.X + lastOfFirstLine.Width, 1);
    }

    /// <summary>
    /// Compares where each tabbed run starts against Word's own export. The line comparison in
    /// the harness only sees where a line begins and ends, so a centred run could sit anywhere
    /// between the two and go unnoticed; this is what pins the positions in between.
    /// </summary>
    [Theory]
    [InlineData("tabs-aligned")]
    [InlineData("tab-leaders")]
    public void Tabbed_positions_match_word(string name)
    {
        var referencePath = Path.Combine(TestPaths.ReferencePdfs, name + ".pdf");
        Assert.True(File.Exists(referencePath), $"No Word reference PDF at {referencePath}");

        var ours = SegmentsOf(PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build(name), Options())));
        var theirs = SegmentsOf(PdfTextExtractor.ExtractFile(referencePath));

        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            Assert.Equal(theirs[i].Text, ours[i].Text);
            Assert.True(Math.Abs(ours[i].X - theirs[i].X) <= 0.3,
                $"'{ours[i].Text}' starts at {ours[i].X:0.###} against Word's {theirs[i].X:0.###}");
        }
    }

    /// <summary>
    /// The runs of a document gathered into the groups the tabs separate. Word splits its text
    /// into runs wherever it pleases, so adjacency rather than run boundaries is what identifies
    /// a tabbed column.
    /// </summary>
    private static List<(double X, string Text)> SegmentsOf(List<ExtractedTextRun> runs)
    {
        var segments = new List<(double X, string Text)>();
        var end = 0.0;

        foreach (var run in runs.OrderBy(r => r.BaselineY).ThenBy(r => r.X))
        {
            if (run.Text.Trim().Length == 0) continue;

            // More than a hair of space means a tab rather than the next piece of the same word.
            if (segments.Count == 0 || run.X - end > 1.5) segments.Add((run.X, run.Text));
            else segments[^1] = (segments[^1].X, segments[^1].Text + run.Text);

            end = run.X + run.Width;
        }

        return segments.Select(s => (s.X, s.Text.Trim())).ToList();
    }
}
