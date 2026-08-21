using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Text turned on its side in a table cell, from <c>w:textDirection</c>.
/// </summary>
/// <remarks>
/// <c>cell-direction-probe</c> is eleven tables, each on a page of its own so that one's height
/// cannot carry into the next one's place. What they settle, all read off Word's own export:
///
///   * A turned cell is laid out in a frame turned a quarter circle: the line runs along the
///     cell's height and the lines stack across its width.
///   * <c>btLr</c> reads from the foot of the cell upwards and stacks its lines from the left;
///     <c>tbRl</c> reads from the head down and stacks from the right.
///   * Word does not make the row any taller to hold it. A turned cell in a row one line tall
///     breaks its text every two characters and runs out of the cell to the right, and Word draws
///     it there.
///   * <c>w:vAlign</c> moves the stack of lines across the cell rather than down it.
///   * The paragraph's own alignment works along the turned line, so a centred one sits in the
///     middle of the cell's height.
///
/// The comparison against Word's export in TextPositionComparisonTests leaves turned runs alone —
/// a turned baseline cannot be set against an upright one's — so what Word did with them is
/// checked here instead, line by line.
/// </remarks>
public class CellDirectionTests(ITestOutputHelper output)
{
    /// <summary>Every turned line of every page, against Word's own.</summary>
    [Theory]
    [InlineData(0, true, "btLr in a row one line tall, which breaks the text every two letters")]
    [InlineData(1, false, "tbRl in the same")]
    [InlineData(2, true, "btLr down two inches of row")]
    [InlineData(3, false, "tbRl down two inches of row")]
    [InlineData(4, true, "btLr with more text than the row is tall")]
    [InlineData(5, true, "btLr in a row of one line, running out of the cell")]
    [InlineData(6, true, "against the top of the cell")]
    [InlineData(7, true, "against the middle")]
    [InlineData(8, true, "against the foot")]
    [InlineData(9, true, "centred along its own line")]
    public void The_turned_lines_stand_where_words_stand(int page, bool upwards, string what)
    {
        if (TestFonts.SkipForMissingFonts("cell-direction-probe")) return;

        output.WriteLine(what);

        var word = Turned(
            File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "cell-direction-probe.pdf")),
            page, upwards);

        var ours = Turned(Ours(), page, upwards);

        output.WriteLine($"word {string.Join(" | ", word)}");
        output.WriteLine($"ours {string.Join(" | ", ours)}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            // The spaces are dropped rather than kept: Word breaks a turned line into pieces
            // where this writes it in one, and which piece a space at a line's end belongs to is
            // a question for the reader rather than for the page.
            Assert.Equal(Bare(word[i].Text), Bare(ours[i].Text));
            Assert.Equal(word[i].Across, ours[i].Across, 0.5);
            Assert.Equal(word[i].Along, ours[i].Along, 0.5);
        }
    }

    /// <summary>
    /// Which way a turned line reads. <c>btLr</c> runs up the page, so its later letters stand
    /// higher than its earlier ones; <c>tbRl</c> runs down.
    /// </summary>
    [Theory]
    [InlineData(2, true, "btLr reads upwards")]
    [InlineData(3, false, "tbRl reads downwards")]
    public void A_turned_line_reads_the_way_the_cell_says(int page, bool upwards, string what)
    {
        if (TestFonts.SkipForMissingFonts("cell-direction-probe")) return;

        output.WriteLine(what);

        var runs = PdfTextExtractor.Extract(Ours())
            .Where(run => run.PageIndex == page && run.Turned)
            .OrderBy(run => run.X)
            .ToList();

        var line = Assert.Single(runs);

        // The pen begins at the foot of the cell for one and at its head for the other, and the
        // turn is the other way round with it.
        Assert.Equal(upwards, line.BaselineY > 150);
    }

    /// <summary>
    /// <c>w:vAlign</c> moves the stack of lines across the cell. The probe's cell is an inch wide,
    /// so its one line stands eleven points in from the left, in the middle, or hard against the
    /// right.
    /// </summary>
    [Theory]
    [InlineData(6, 227.52, "top")]
    [InlineData(7, 256.32, "centre")]
    [InlineData(8, 284.88, "bottom")]
    public void The_stack_of_lines_is_placed_across_the_cell(int page, double across, string what)
    {
        if (TestFonts.SkipForMissingFonts("cell-direction-probe")) return;

        output.WriteLine(what);

        var line = Assert.Single(Turned(Ours(), page, upwards: true));
        Assert.Equal(across, line.Across, 0.3);
    }

    /// <summary>
    /// A row is not made taller to hold turned text: the lines stack across the cell and run out
    /// of it where there are too many, which is what Word does with them.
    /// </summary>
    [Fact]
    public void A_turned_cell_does_not_make_the_row_taller()
    {
        if (TestFonts.SkipForMissingFonts("cell-direction-probe")) return;

        var word = File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "cell-direction-probe.pdf"));
        var pdf = Ours();

        // Page five holds a row of one line with far more turned text than it can take.
        var theirs = Box(word, 5);
        var ours = Box(pdf, 5);


        output.WriteLine($"word row {theirs.Bottom - theirs.Top:0.##}pt tall, ours {ours.Bottom - ours.Top:0.##}pt");

        Assert.Equal(theirs.Bottom - theirs.Top, ours.Bottom - ours.Top, 0.3);
        Assert.True(ours.Bottom - ours.Top < 16, "the row grew to hold the turned text");

        // And the lines run out past the cell, as Word's do.
        var last = Turned(pdf, 5, upwards: true)[^1];
        Assert.True(last.Across > ours.Right, $"the last line stands at {last.Across:0.##}, inside the table");
    }

    /// <summary>
    /// A word too wide for a cell is broken inside rather than left to overrun it — in an upright
    /// cell as much as a turned one, which is what the probe's last page is for: a cell a fifth of
    /// an inch wide, in which Word breaks "Unturnable" into "U", "nt", "ur", "na", "bl" and "e",
    /// taking whatever fits and no more.
    /// </summary>
    [Fact]
    public void A_word_too_wide_for_a_cell_is_broken_inside()
    {
        if (TestFonts.SkipForMissingFonts("cell-direction-probe")) return;

        var runs = PdfTextExtractor.Extract(Ours())
            .Where(run => run.PageIndex == 10 && run.X > 200)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(line => line.Key)
            .Select(line => string.Concat(line.OrderBy(run => run.X).Select(run => run.Text)))
            .ToList();

        output.WriteLine(string.Join(" / ", runs));

        Assert.Equal(["U", "nt", "ur", "na", "bl", "e"], runs);
    }

    /// <summary>A string with its spaces taken out.</summary>
    private static string Bare(string text) => new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static byte[] Ours() =>
        Converter.Convert(Fixtures.Build("cell-direction-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>
    /// The turned lines of a page: where each stands across the cell, where it begins along it,
    /// and what it says, in reading order.
    /// </summary>
    private static List<(double Across, double Along, string Text)> Turned(
        byte[] pdf, int page, bool upwards)
    {
        var runs = PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && run.Turned)
            .ToList();

        // A line of a turned cell stands at one place across the page and runs up or down it, so
        // the runs of one line share an x. Which way they read is told by where the cell's own
        // lines stack: leftward for one direction and rightward for the other.
        return runs
            .GroupBy(run => Math.Round(run.X, 1))
            .OrderBy(line => line.Key)
            .Select(line =>
            {
                var ordered = upwards
                    ? line.OrderByDescending(run => run.BaselineY).ToList()
                    : line.OrderBy(run => run.BaselineY).ToList();

                return (line.Min(run => run.X), ordered[0].BaselineY,
                    string.Concat(ordered.Select(run => run.Text)).Trim());
            })
            .ToList();
    }

    private static (double Left, double Top, double Right, double Bottom) Box(byte[] pdf, int page)
    {
        var rects = PdfPathExtractor.Extract(pdf).Where(r => r.PageIndex == page).ToList();

        return (rects.Min(r => r.Left), rects.Min(r => r.Top),
            rects.Max(r => r.Right), rects.Max(r => r.Bottom));
    }
}
