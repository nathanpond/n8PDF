using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What an equation asks of the line that holds it, against what Word's own asks.
/// </summary>
/// <remarks>
/// Both fixtures stand their equations between rails — a two point full stop on a line of its own,
/// with a two point paragraph mark, so that nothing but the equation decides how tall a line comes
/// out. Each probe carries a stop of its own before the equation, which says where the line's
/// baseline is whatever the equation puts on it. Then, for each probe:
///
///     ascent  = (probe - the rail before) - the rail's own descent
///     descent = (the rail after - probe)  - the rail's own ascent
///
/// and the rail's own two come from this engine, whose plain-text line boxes are what every other
/// fixture here already holds it to.
/// </remarks>
public class MathLineBoxTests(ITestOutputHelper output)
{
    private static readonly string[] LineBox =
    [
        "x", "b", "y", "sum glyph", "x at 24", "x at 6",
        "x^2", "x^x", "x_i", "x/x", "1/1", "x/y",
        "root x", "(x)", "(a/b)", "sum limits", "x^(x^x)",
        "x^2 @24", "x_i @24", "1/1 @24", "x/y @24", "root x @24", "(a/b) @24",
        "x^2 @6", "x6 and x24"
    ];

    private static readonly string[] Structure =
    [
        "(x) 12 in 20", "root x 12 in 20", "x^2 12 in 20", "(x) 20 in 20", "x^2 20 in 20"
    ];

    /// <summary>
    /// The n-ary probe's nineteen, which are here for the room a line keeps for a limit that is
    /// not there: eight of them give an operator one limit and write the other empty, which is
    /// what Word writes when a limit is deleted.
    /// </summary>
    private static readonly string[] Nary =
    [
        "sum, limits above", "sum, limits beside", "integral, limits above", "integral, limits beside",
        "product, limits above", "product, limits beside", "contour, limits above", "contour, limits beside",
        "sum with a lower limit", "sum with an upper limit",
        "integral with a lower limit", "integral with an upper limit",
        "sum under x", "sum under 1", "sum over x", "sum over 1", "sum, x either side",
        "integral under x", "sum at twenty-four point"
    ];

    /// <summary>
    /// Every probe of both fixtures, above the line and below it.
    /// </summary>
    /// <remarks>
    /// All of them within three quarters of a point, and most within a quarter — which is the
    /// three hundredth of an inch Word rounds a position to.
    /// </remarks>
    [Theory]
    [InlineData("math-line-box-probe")]
    [InlineData("math-structure-probe")]
    [InlineData("math-nary-probe")]
    public void An_equation_asks_its_line_for_what_word_asks(string fixture)
    {
        if (TestFonts.SkipForMissingFaces()) return;

        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };

        var ours = PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build(fixture), options));
        var word = PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf"));

        using var stream = new MemoryStream(Fixtures.Build(fixture));
        var laidOut = Converter.LayoutDocument(stream, options);

        // The rails are plain text, and are what everything here is measured against.
        var rail = laidOut.Pages[0].Lines[0];
        var railAscent = rail.Ascent;
        var railDescent = rail.Height - rail.Ascent;

        var names = fixture switch
        {
            "math-structure-probe" => Structure,
            "math-nary-probe" => Nary,
            _ => LineBox
        };

        var wordRoom = Room(word, railAscent, railDescent);
        var ourRoom = Room(ours, railAscent, railDescent);

        Assert.Equal(names.Length, wordRoom.Count);
        Assert.Equal(names.Length, ourRoom.Count);

        var worst = 0.0;

        for (var i = 0; i < names.Length; i++)
        {
            var (wordAscent, wordDescent) = wordRoom[i];
            var (ourAscent, ourDescent) = ourRoom[i];

            output.WriteLine(
                $"{names[i],-16} ascent {ourAscent,7:0.###} against {wordAscent,7:0.###}" +
                $"   descent {ourDescent,7:0.###} against {wordDescent,7:0.###}");

            worst = Math.Max(worst, Math.Max(Math.Abs(wordAscent - ourAscent),
                Math.Abs(wordDescent - ourDescent)));

            Assert.True(Math.Abs(ourAscent - wordAscent) < 1,
                $"{names[i]}: the line reaches {ourAscent:0.###} over its baseline " +
                $"where Word's reaches {wordAscent:0.###}");

            Assert.True(Math.Abs(ourDescent - wordDescent) < 1,
                $"{names[i]}: the line reaches {ourDescent:0.###} under its baseline " +
                $"where Word's reaches {wordDescent:0.###}");
        }

        output.WriteLine($"{fixture}: {names.Length} equations, worst {worst:0.###}pt");
    }

    /// <summary>
    /// The size an equation is set at is the size of the text carrying it, not the size its own
    /// runs state.
    /// </summary>
    /// <remarks>
    /// math-structure-probe is twenty point throughout and states twelve on every run inside its
    /// equations. Word draws the letters at twelve and the brackets and the radical round them at
    /// 19.92 — twenty, rounded the way it rounds a size — and this asserts the same of ours,
    /// against Word's own numbers.
    /// </remarks>
    [Fact]
    public void An_equation_is_set_at_the_size_of_the_text_carrying_it()
    {
        if (TestFonts.SkipForMissingFaces()) return;

        var ours = PdfTextExtractor.Extract(Converter.Convert(
            Fixtures.Build("math-structure-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        // The letters state twelve point and are drawn at twelve.
        var letters = ours.Where(run => run.Text is "𝑥").Select(run => run.FontSize).ToList();

        Assert.Contains(letters, size => Math.Abs(size - 12) < 0.01);

        // What is stretched round them is the paragraph's twenty, rounded to Word's own grid.
        foreach (var stretched in ours.Where(run => run.Text is "(" or ")" or "√"))
            Assert.Equal(19.92, stretched.FontSize, 2);

        // And a script of a twelve point run is 8.4: the face's 73%, taken down to a whole half
        // point and rounded like any other size.
        var script = ours.Single(run => run.Text == "2" && run.FontSize < 12);

        Assert.Equal(8.4, script.FontSize, 2);
    }

    /// <summary>What each probe's line asks for above and below its baseline.</summary>
    private static List<(double Ascent, double Descent)> Room(
        IReadOnlyList<ExtractedTextRun> runs, double railAscent, double railDescent)
    {
        var rails = Baselines(runs, "-");
        var probes = Baselines(runs, ".");

        var room = new List<(double, double)>();

        foreach (var probe in probes)
        {
            // Both fixtures run onto a second page, and a rail belongs to the probe it shares a
            // page with.
            var page = Math.Floor(probe / 2000);

            var before = rails.Where(rail => rail < probe && Math.Floor(rail / 2000) == page)
                .DefaultIfEmpty(0).Max();
            var after = rails.Where(rail => rail > probe && Math.Floor(rail / 2000) == page)
                .DefaultIfEmpty(0).Min();

            if (before == 0 || after == 0) continue;

            room.Add((probe - before - railDescent, after - probe - railAscent));
        }

        return room;
    }

    private static List<double> Baselines(IReadOnlyList<ExtractedTextRun> runs, string mark) =>
    [
        .. runs.Where(run => run.Text.Trim() == mark)
            .Select(run => run.PageIndex * 2000.0 + run.BaselineY)
            .Distinct().Order()
    ];
}
