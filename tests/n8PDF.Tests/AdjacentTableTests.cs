using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Two tables written one after the other with nothing in between, which Word reads as one.
/// </summary>
/// <remarks>
/// A document that means two tables must put a paragraph between them; two <c>w:tbl</c> elements
/// that touch are one table to Word. <c>adjacent-tables-probe</c> shows what that comes to — four
/// pages, each table bordered three points at the top and bottom and half a point inside, so the
/// join can be read straight off the ink:
///
///   1  two tables of the same grid, touching: one line round the pair, none where they meet
///   2  the same two with a paragraph between them, which are two tables and look it
///   3  two tables of different grids, touching: the rows keep the columns they were written with
///   4  a second table that asks to be indented, where Word does something this does not follow
///
/// What the second table said about itself is not thrown away with it: its rows keep their own
/// columns and their own indent. What it said about its borders is — the line round the merged
/// table is the first table's.
/// </remarks>
public class AdjacentTableTests(ITestOutputHelper output)
{
    /// <summary>Every line of the probe, against Word's own.</summary>
    [Theory]
    [InlineData(0, "the same grid, touching")]
    [InlineData(1, "a paragraph between them")]
    [InlineData(2, "different grids, touching")]
    public void The_rows_stand_where_words_stand(int page, string what)
    {
        if (TestFonts.SkipForMissingFonts("adjacent-tables-probe")) return;

        output.WriteLine(what);

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "adjacent-tables-probe.pdf")), page);
        var ours = Lines(Ours(), page);

        output.WriteLine($"word {string.Join(" | ", word)}");
        output.WriteLine($"ours {string.Join(" | ", ours)}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++)
        {
            Assert.Equal(word[i].Text, ours[i].Text);
            Assert.Equal(word[i].Baseline, ours[i].Baseline, 0.3);
            Assert.Equal(word[i].Left, ours[i].Left, 0.3);
        }
    }

    /// <summary>
    /// Two touching tables are one: the pair takes one thick line at the top and one at the foot,
    /// where two tables would take four, and the rows run on with nothing between them.
    /// </summary>
    [Fact]
    public void Two_touching_tables_are_drawn_as_one()
    {
        if (TestFonts.SkipForMissingFonts("adjacent-tables-probe")) return;

        var pdf = Ours();

        var joined = Thick(pdf, 0);
        var apart = Thick(pdf, 1);

        output.WriteLine($"touching: {joined.Count} thick lines; with a paragraph between: {apart.Count}");

        Assert.Equal(2, joined.Count);
        Assert.Equal(4, apart.Count);

        // And the rows follow one another by a row's height, not by a row plus two borders.
        var lines = Lines(pdf, 0);
        var steps = lines.Zip(lines.Skip(1), (a, b) => b.Baseline - a.Baseline).ToList();

        output.WriteLine($"the rows step by {string.Join(", ", steps.Select(step => $"{step:0.##}"))}");
        Assert.All(steps, step => Assert.True(step < 15, $"a step of {step:0.##} between rows"));
    }

    /// <summary>
    /// A folded row keeps the columns it was written with. The probe's second table names its
    /// columns the other way round — a narrow one first — and the row that came from it keeps
    /// them that way.
    /// </summary>
    [Fact]
    public void A_folded_row_keeps_the_columns_it_was_written_with()
    {
        if (TestFonts.SkipForMissingFonts("adjacent-tables-probe")) return;

        var runs = PdfTextExtractor.Extract(Ours())
            .Where(run => run.PageIndex == 2)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(line => line.Key)
            .Select(line => line.OrderBy(run => run.X).ToList())
            .ToList();

        // The first table's second column begins where 2880 twips of first column end; the folded
        // table's begins where 1440 of them do.
        var wide = runs[0].Last().X;
        var narrow = runs[2].Last().X;

        output.WriteLine($"the wide table's second column begins at {wide:0.##}, the narrow one's at {narrow:0.##}");

        Assert.Equal(216.5, wide, 1.0);
        Assert.Equal(144.5, narrow, 1.0);
    }

    /// <summary>
    /// The one thing Word does here that this does not: where a folded table asks to be indented,
    /// Word indents its rows and then refits the whole merged table — columns and indent together
    /// — into the width the first table declared.
    /// </summary>
    /// <remarks>
    /// The probe's fourth page has a second table indented half an inch. Word draws the merged
    /// table's columns at 123.12 and 61.44 points where they were written 144 and 72, and sets the
    /// indented rows 30 points in rather than 36 — the pair fitted into the 216 points the first
    /// table declared. This indents the rows by what they ask for and leaves the columns alone, so
    /// its rows stand 5.54 points further in than Word's.
    ///
    /// Held here so the difference is written down rather than merely absent. Everything else about
    /// the merge agrees with Word outright.
    /// </remarks>
    [Fact]
    public void An_indented_fold_is_not_refitted_the_way_word_refits_it()
    {
        if (TestFonts.SkipForMissingFonts("adjacent-tables-probe")) return;

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "adjacent-tables-probe.pdf")), 3);
        var ours = Lines(Ours(), 3);

        output.WriteLine($"word {string.Join(" | ", word.Select(line => $"{line.Left:0.##}"))}");
        output.WriteLine($"ours {string.Join(" | ", ours.Select(line => $"{line.Left:0.##}"))}");

        // The rows that were not indented stand where Word's stand.
        Assert.Equal(word[0].Left, ours[0].Left, 0.3);

        // The indented ones do not: Word refits them, this does not.
        Assert.Equal(102.96, word[2].Left, 0.1);
        Assert.Equal(108.5, ours[2].Left, 0.3);
    }

    private static byte[] Ours() =>
        Converter.Convert(Fixtures.Build("adjacent-tables-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>Every line of a page: where it begins, what it says, and what it sits on.</summary>
    private static List<(double Left, double Baseline, string Text)> Lines(byte[] pdf, int page) =>
        PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(line => line.Key)
            .Select(line => (
                line.Min(run => run.X),
                line.Key,
                new string(string.Concat(line.OrderBy(run => run.X).Select(run => run.Text))
                    .Where(c => !char.IsWhiteSpace(c)).ToArray())))
            .Where(line => line.Item3.Length > 0)
            .ToList();

    /// <summary>The thick lines on a page, which are the edges of a table.</summary>
    private static List<ExtractedRectangle> Thick(byte[] pdf, int page) =>
        PdfPathExtractor.Extract(pdf)
            .Where(rect => rect.PageIndex == page && rect.Height is > 1 and < 5 && rect.Width > 100)
            .OrderBy(rect => rect.Top)
            .ToList();
}
