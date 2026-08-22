using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The box round a run, from <c>w:bdr</c>.
/// </summary>
/// <remarks>
/// It is not the box round a paragraph in miniature, and it is not a highlight with a line round
/// it. run-border-probe measures what it is:
///
///   the box      the **run's own** box — its ascent and descent — with a step of the grid over
///                it, where a highlight takes the whole line's. A twelve point run beside a
///                thirty-six point one is boxed to its own 13.92 points and highlighted to the
///                line's 41.52
///   the room     the box takes room along the line as well: the weight on each side, and the
///                declared space beyond that, so a space of four points widens the run by eight
///                and heightens its line by the same on each side
///   the weight   floored to the grid, as every other border here is, and drawn outward
///   the joins    runs bordered alike and touching share one box; a plain space between them
///                leaves two
///   the break    a run too long for the line is boxed on each line it takes, closed on both
///                sides, and the line is filled with the closing side's room in hand
///
/// What is left between us and Word is the step of drift the flow carries from paragraph to
/// paragraph, which moves a box with its baseline rather than changing its shape.
/// </remarks>
public class RunBorderTests(ITestOutputHelper output)
{
    /// <summary>
    /// The box is the run's own, which the page holding two sizes on one line settles.
    /// </summary>
    [Fact]
    public void The_box_is_the_runs_own_and_not_the_lines()
    {
        if (TestFonts.SkipForMissingFonts("run-border-probe")) return;

        var word = Boxes(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "run-border-probe.pdf")), 4);
        var ours = Boxes(Ours(), 4);

        output.WriteLine($"word {string.Join(" ", word.Select(b => $"{b.Height:0.##}"))}");
        output.WriteLine($"ours {string.Join(" ", ours.Select(b => $"{b.Height:0.##}"))}");

        // Two boxes: a twelve point run beside a thirty-six point one, then the other way about.
        Assert.Equal(2, word.Count);
        Assert.Equal(2, ours.Count);

        // The first is the little run's own box and not the tall line's; the second is the tall
        // run's. Word draws 13.92 and 41.52.
        Assert.Equal(13.92, word[0].Height, 0.001);
        Assert.Equal(41.52, word[1].Height, 0.001);

        Assert.Equal(word[0].Height, ours[0].Height, 0.001);
        Assert.Equal(word[1].Height, ours[1].Height, 0.001);
    }

    /// <summary>
    /// What the box takes along the line: the weight on each side and the space beyond it, which
    /// the text after it is moved by.
    /// </summary>
    /// <summary>
    /// The space widens the box by twice itself and heightens it by twice itself.
    /// </summary>
    /// <remarks>
    /// Word draws each side of a spaced box in three pieces where this draws it in one, so the
    /// sides cannot be lined up one for one; what is compared instead is how much bigger each box
    /// is than the box that was given no space, which is the rule itself.
    /// </remarks>
    [Fact]
    public void The_space_widens_the_box_by_twice_itself()
    {
        if (TestFonts.SkipForMissingFonts("run-border-probe")) return;

        var ours = Boxes(Ours(), 2);

        output.WriteLine(string.Join(" ", ours.Select(b => $"{b.Width:0.##}x{b.Height:0.##}")));

        Assert.Equal(3, ours.Count);

        // No space, four points, twelve: twice each, in both directions.
        Assert.Equal(ours[0].Width + 8, ours[1].Width, 0.25);
        Assert.Equal(ours[0].Height + 8, ours[1].Height, 0.25);
        Assert.Equal(ours[0].Width + 24, ours[2].Width, 0.25);
        Assert.Equal(ours[0].Height + 24, ours[2].Height, 0.25);
    }

    /// <summary>
    /// Runs bordered alike and touching share a box; a space between them leaves two; a run too
    /// long for the line is boxed on each line it takes.
    /// </summary>
    [Fact]
    public void Runs_bordered_alike_and_touching_share_a_box()
    {
        if (TestFonts.SkipForMissingFonts("run-border-probe")) return;

        var word = Boxes(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "run-border-probe.pdf")), 3);
        var ours = Boxes(Ours(), 3);

        output.WriteLine($"word {string.Join(" ", word.Select(b => $"{b.Left:0.##}+{b.Width:0.##}"))}");
        output.WriteLine($"ours {string.Join(" ", ours.Select(b => $"{b.Left:0.##}+{b.Width:0.##}"))}");

        // One box for the two runs written together, two for the pair with a space between them
        // (which this reader takes together, sharing a top), and one on each of the two lines the
        // long run takes.
        Assert.Equal(4, word.Count);
        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.Equal(word[i].Left, ours[i].Left, 0.25);
            Assert.Equal(word[i].Width, ours[i].Width, 1.0);
        }
    }

    private static byte[] Ours() => Converter.Convert(Fixtures.Build("run-border-probe"),
        new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>Each box of a page, as the rectangle its four sides enclose.</summary>
    private static List<(double Left, double Top, double Width, double Height)> Boxes(byte[] pdf, int page)
    {
        // The sides are the tall thin rectangles; each box has two, and its height is theirs.
        var sides = PdfPathExtractor.Extract(pdf)
            .Where(r => r.PageIndex == page && r.Height > r.Width + 0.01)
            .OrderBy(r => r.Top).ThenBy(r => r.Left)
            .ToList();

        // The two sides of a box share a top and a height; a page may hold several at once.
        return [.. sides
            .GroupBy(r => (Math.Round(r.Top, 2), Math.Round(r.Height, 2)))
            .OrderBy(g => g.Key.Item1).ThenBy(g => g.Min(r => r.Left))
            .Select(g => (g.Min(r => r.Left), g.Key.Item1,
                g.Max(r => r.Right) - g.Min(r => r.Left), g.Key.Item2))];
    }


}
