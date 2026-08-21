using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A table taken out of the flow by <c>w:tblpPr</c>: where it stands, and how much room the text
/// beside it gives up.
/// </summary>
/// <remarks>
/// <c>floating-table-probe</c> is seven pages of one export:
///
///   0  against the left margin, anchored to the text it was written among
///   1  against the right margin
///   2  anchored to the paper: two inches down and one across, wherever the text is
///   3  half an inch of daylight on every side of it
///   4  no daylight at all
///   5  half an inch down from where it would have stood
///   6  the left-hand place again, drawn with a three point border
///
/// What they settle, all of it read off Word's own export:
///
///   * The place names the cell's text edge, not the table's edge. The thick-bordered page says
///     so: its border grows outward and its text stays on the margin.
///   * Down the page the place names the table's outer edge instead — Word draws the thin border
///     and the thick one with their tops in the same place.
///   * The daylight is measured from the outside of the border, which is why the text beside the
///     thick-bordered table stands a point and a half further out than beside the thin one.
///   * A table anchored to the text stands where it would have stood, plus whatever
///     <c>w:tblpY</c> says; one anchored to the paper stands where it is told, and the text above
///     and below it carries on regardless.
///
/// Two things Word does that this does not are held in <c>floating-table-wrap-probe</c> and
/// written up at the foot of this file.
/// </remarks>
public class FloatingTableTests(ITestOutputHelper output)
{
    /// <summary>Where the table itself is drawn, ink against ink.</summary>
    [Theory]
    [InlineData(0, "against the left margin")]
    [InlineData(1, "against the right margin")]
    [InlineData(2, "two inches down the paper and one across it")]
    [InlineData(3, "half an inch of daylight all round")]
    [InlineData(4, "no daylight at all")]
    [InlineData(5, "half an inch down from where it would have stood")]
    [InlineData(6, "a three point border")]
    public void The_table_stands_where_word_puts_it(int page, string what)
    {
        if (TestFonts.SkipForMissingFonts("floating-table-probe")) return;

        output.WriteLine(what);

        var word = Box(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "floating-table-probe.pdf")), page);
        var ours = Box(Ours("floating-table-probe"), page);

        output.WriteLine($"word {word}");
        output.WriteLine($"ours {ours}");

        Assert.Equal(word.Left, ours.Left, 0.2);
        Assert.Equal(word.Right, ours.Right, 0.2);

        // The line itself is drawn half its width higher here than in Word's own file, which is
        // how every table of this library is drawn and not a matter of floating: Word puts the
        // line inside the table's box and this straddles the edge with it. The three point border
        // is where that shows, and it is a point and a half.
        Assert.Equal(word.Top, ours.Top, 1.6);
        Assert.Equal(word.Bottom, ours.Bottom, 1.6);
    }

    /// <summary>
    /// Where the text beside the table begins, and how many lines give up the room. The daylight
    /// is measured from the outside of the border: nine points beside a half point border puts the
    /// text at 224.64, and beside a three point one at 225.12.
    /// </summary>
    [Theory]
    [InlineData(0, 224.64, "an eighth of an inch of daylight")]
    [InlineData(3, 251.76, "half an inch of it")]
    [InlineData(4, 216.24, "none at all")]
    [InlineData(6, 225.12, "an eighth of an inch beside a thick border")]
    public void The_text_beside_it_begins_where_words_does(int page, double word, string what)
    {
        if (TestFonts.SkipForMissingFonts("floating-table-probe")) return;

        output.WriteLine(what);

        var ours = Beside(Ours("floating-table-probe"), page);

        output.WriteLine($"word {word} ours {ours}");

        // Within half a point of Word's: the table's own edge falls a fiftieth of a point from
        // Word's, and where that lands on the grid decides the last step.
        Assert.Equal(word, ours, 0.5);
    }

    /// <summary>
    /// A table anchored to the paper takes no notice of where the text has got to, and the text
    /// takes no notice of it beyond making room: the page's first line is where it would be with
    /// no table at all.
    /// </summary>
    [Fact]
    public void A_table_anchored_to_the_paper_stands_where_it_is_told()
    {
        if (TestFonts.SkipForMissingFonts("floating-table-probe")) return;

        var ours = Box(Ours("floating-table-probe"), 2);
        var word = Box(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "floating-table-probe.pdf")), 2);

        // Two inches down and one across, measured from the paper's own corner.
        Assert.Equal(144, ours.Top, 0.3);
        Assert.Equal(word.Left, ours.Left, 0.2);
    }

    /// <summary>
    /// The two things Word does with a floating table that this does not: they are held here so
    /// that the difference is written down rather than merely absent.
    /// </summary>
    /// <remarks>
    /// Word flows text down both sides of a table with room either side of it. This puts the text
    /// down the wider side and leaves the narrower one empty, so a paragraph beside a centred
    /// table takes twice as many lines here as in Word.
    ///
    /// Word also shortens a line its clearance reaches back over — a table with half an inch of
    /// daylight above it shortens the line already written above it. This leaves that line whole,
    /// because by the time the table is reached the line has been placed.
    /// </remarks>
    [Fact]
    public void What_word_does_with_a_float_that_this_does_not()
    {
        if (TestFonts.SkipForMissingFonts("floating-table-wrap-probe")) return;

        var pdf = Ours("floating-table-wrap-probe");
        var word = File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "floating-table-wrap-probe.pdf"));

        // The centred table: Word's line beside it begins at the margin and carries on past the
        // table, so it is one line; ours begins at the margin and stops at the table.
        var wordWidest = Widest(word, 0, Box(word, 0));
        var oursWidest = Widest(pdf, 0, Box(pdf, 0));

        output.WriteLine($"widest line beside a centred table: word {wordWidest:0.##} ours {oursWidest:0.##}");
        Assert.True(wordWidest > oursWidest + 100,
            $"Word's line beside a centred table runs both sides of it ({wordWidest:0.##}pt) " +
            $"where this stops at the table ({oursWidest:0.##}pt)");

        // The clearance above: Word shortens the line above the table, and this does not.
        var wordAbove = FirstLineStart(word, 1);
        var oursAbove = FirstLineStart(pdf, 1);

        output.WriteLine($"line above a table with half an inch of daylight: word {wordAbove:0.##} ours {oursAbove:0.##}");
        Assert.True(wordAbove > 200, "Word should shorten the line its clearance reaches back over");
        Assert.Equal(72.0, oursAbove, 0.5);
    }

    private static byte[] Ours(string fixture) =>
        Converter.Convert(Fixtures.Build(fixture),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>The ink the table is drawn with on a page, as one box.</summary>
    private static (double Left, double Top, double Right, double Bottom) Box(byte[] pdf, int page)
    {
        var rects = PdfPathExtractor.Extract(pdf).Where(r => r.PageIndex == page).ToList();

        return (rects.Min(r => r.Left), rects.Min(r => r.Top),
            rects.Max(r => r.Right), rects.Max(r => r.Bottom));
    }

    /// <summary>
    /// Where the flowing text beside the table begins — the furthest in any of its lines starts,
    /// which is the ones the table shortened. The lines of the flow are told from the table's own
    /// by what they say: only this library's own output is read this way, and it writes a line as
    /// one piece.
    /// </summary>
    private static double Beside(byte[] pdf, int page) =>
        PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && run.Text.Contains(" line "))
            .Max(run => run.X);

    /// <summary>
    /// The widest line standing beside the table, measured from its first piece to its last. A
    /// line that runs both sides of the table is as wide as the whole measure; one that stops at
    /// it is not.
    /// </summary>
    private static double Widest(
        byte[] pdf, int page, (double Left, double Top, double Right, double Bottom) box) =>
        PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page &&
                          run.BaselineY > box.Top && run.BaselineY < box.Bottom)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .Where(line => line.Min(run => run.X) < box.Left)
            .Select(line => line.Max(run => run.X + run.Width) - line.Min(run => run.X))
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>Where the topmost line of a page begins.</summary>
    private static double FirstLineStart(byte[] pdf, int page)
    {
        var runs = PdfTextExtractor.Extract(pdf).Where(run => run.PageIndex == page).ToList();
        var top = runs.Min(run => run.BaselineY);

        return runs.Where(run => Math.Abs(run.BaselineY - top) < 0.5).Min(run => run.X);
    }
}
