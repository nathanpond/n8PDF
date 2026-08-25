using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tight and through wrap follow the polygon rather than the bounding box (#65).
/// </summary>
/// <remarks>
/// The probe floats a 120pt picture at the left margin. Page one wraps tight around a triangle —
/// apex at the top, base at the bottom — so the blocked span grows down the page and text stands
/// on both its flanks near the apex. Pages two and three carry the same U-shaped polygon, whose
/// channel (40..80pt into the picture) a through wrap fills with text and a tight wrap leaves
/// empty: that pair is the distinction the two modes exist for. Word's export holds the exact
/// line positions through the generic tiers; these tests hold the shape of the behaviour.
/// </remarks>
public class WrapPolygonTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static List<ExtractedTextRun> Runs()
    {
        var pdf = n8PDF.Converter.Convert(Fixtures.Build("wrap-polygon-probe"),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        return PdfTextExtractor.Extract(pdf);
    }

    private static bool InChannel(ExtractedTextRun run) =>
        run.X is > 105 and < 150 && run.BaselineY is > 132 and < 208;

    /// <summary>Beside the triangle, text stands on both flanks near the apex and neither near the base.</summary>
    [Fact]
    public void The_lines_follow_the_triangle_s_edges()
    {
        var page = Runs().Where(r => r.PageIndex == 0).ToList();

        _output.WriteLine(string.Join(" | ", page.Select(r => $"({r.X:0},{r.BaselineY:0})")));

        // A left flank: a run starting at the margin beside the upper triangle, where the
        // polygon leaves room the bounding box would not.
        Assert.Contains(page, r => r.X < 105 && r.BaselineY is > 85 and < 165);

        // And the right flank starts near the centre at the top — the triangle is narrow there —
        // then strictly further right line by line, which is the slope of its edge.
        Assert.Contains(page, r => r.X is > 125 and < 155 && r.BaselineY < 115);

        var flank = page.Where(r => r.X > 110).OrderBy(r => r.BaselineY).Select(r => r.X).ToList();

        Assert.True(flank.Count >= 3 && flank.Zip(flank.Skip(1)).All(pair => pair.Second > pair.First),
            $"the right flank should march right down the page: {string.Join(", ", flank.Select(x => $"{x:0.0}"))}");
    }

    /// <summary>Through fills the U's channel with text.</summary>
    [Fact]
    public void Through_lets_text_into_the_channel()
    {
        var inside = Runs().Where(r => r.PageIndex == 1 && InChannel(r)).ToList();

        _output.WriteLine(string.Join(" | ", inside.Select(r => $"({r.X:0},{r.BaselineY:0}) '{r.Text}'")));

        Assert.NotEmpty(inside);
    }

    /// <summary>Tight, with the same polygon, leaves it empty — that is the whole difference.</summary>
    [Fact]
    public void Tight_keeps_the_channel_empty()
    {
        var inside = Runs().Where(r => r.PageIndex == 2 && InChannel(r)).ToList();

        Assert.True(inside.Count == 0,
            "text entered the channel under a tight wrap: " +
            string.Join(" | ", inside.Select(r => $"({r.X:0},{r.BaselineY:0}) '{r.Text}'")));
    }
}
