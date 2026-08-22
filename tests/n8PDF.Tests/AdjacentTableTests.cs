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
    /// Where a folded table asks to be indented, Word indents its rows and then squeezes the whole
    /// merged table — columns and indents together — into the width the first table declared.
    /// </summary>
    /// <remarks>
    /// The probe's fourth page has a second table indented half an inch against a first 216 points
    /// wide, so the widest row wants 251.52 and gets 216: Word draws the columns 123.6 and 61.92
    /// where they were written 144 and 72, and sets the indented rows 30.48 points in rather than
    /// 36. What squeezes by how much is measured in merged-indent-probe.
    /// </remarks>
    [Fact]
    public void An_indented_fold_is_squeezed_the_way_word_squeezes_it()
    {
        if (TestFonts.SkipForMissingFonts("adjacent-tables-probe")) return;

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "adjacent-tables-probe.pdf")), 3);
        var ours = Lines(Ours(), 3);

        output.WriteLine($"word {string.Join(" | ", word.Select(line => $"{line.Left:0.##}"))}");
        output.WriteLine($"ours {string.Join(" | ", ours.Select(line => $"{line.Left:0.##}"))}");

        Assert.Equal(word.Count, ours.Count);

        for (var i = 0; i < word.Count; i++) Assert.Equal(word[i].Left, ours[i].Left, 0.2);

        // The rows that asked to be indented stand 30.48 in rather than the 36 they asked for.
        Assert.Equal(102.96, ours[2].Left, 0.2);
    }

    /// <summary>
    /// What is squeezed and by how much, over the ten pages of merged-indent-probe.
    /// </summary>
    /// <remarks>
    /// Each page's rows are read as the vertical lines they are drawn between, so the columns can
    /// be measured without going through the text. Word's numbers are given here; where a page has
    /// rows of two widths the wider set is the one named.
    /// </remarks>
    [Theory]
    [InlineData(0, "indented 18 points", new[] { 72.0, 205.2, 271.92 })]
    [InlineData(1, "indented 36", new[] { 72.0, 195.6, 257.52 })]
    [InlineData(2, "indented 72", new[] { 72.0, 180.0, 234.24 })]
    [InlineData(3, "indented 108", new[] { 72.0, 167.76, 216.24 })]
    [InlineData(4, "narrow enough to fit", new[] { 72.0, 216.0, 288.0 })]
    [InlineData(5, "wider, not indented", new[] { 72.0, 186.96, 244.8 })]
    [InlineData(6, "wider and indented", new[] { 72.0, 173.52, 224.64 })]
    [InlineData(7, "the first table indented", new[] { 138.0, 261.6, 323.52 })]
    [InlineData(8, "a width narrower than the grid", new[] { 72.0, 174.72, 226.56 })]
    [InlineData(9, "three tables", new[] { 72.0, 180.0, 234.24 })]
    public void The_rows_of_a_merged_table_are_squeezed_as_word_squeezes_them(
        int page, string what, double[] word)
    {
        if (TestFonts.SkipForMissingFonts("merged-indent-probe")) return;

        output.WriteLine(what);

        var ours = Boundaries(Converter.Convert(Fixtures.Build("merged-indent-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), page);

        output.WriteLine($"word {string.Join(", ", word.Select(x => $"{x:0.##}"))}");
        output.WriteLine($"ours {string.Join(" / ", ours.Select(row => string.Join(", ", row.Select(x => $"{x:0.##}"))))}");

        var mine = ours[0];

        Assert.Equal(word.Length, mine.Count);

        // Word rounds the share each column takes of the squeezed total a shade differently than
        // this does — its first column comes out a whisker narrower than two thirds every time —
        // which is worth a third of a point on the widest of these pages.
        for (var i = 0; i < word.Length; i++) Assert.Equal(word[i], mine[i], 0.4);
    }

    /// <summary>
    /// A merged table whose rows all fit is not squeezed at all, and a row asking for no indent is
    /// not moved.
    /// </summary>
    [Fact]
    public void A_merged_table_that_fits_is_left_alone()
    {
        if (TestFonts.SkipForMissingFonts("merged-indent-probe")) return;

        var rows = Boundaries(Converter.Convert(Fixtures.Build("merged-indent-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), 4);

        // The first table's rows keep the 144 and 72 they were written with...
        Assert.Equal([72, 216, 288], rows[0].Select(x => Math.Round(x, 1)));

        // ...and the folded rows keep theirs, indented by what they asked for less the inset the
        // indent absorbs: 36 points less half a point.
        Assert.Equal([107.5, 179.5, 215.5], rows[^1].Select(x => Math.Round(x, 1)));
    }

    /// <summary>The x of every vertical line on a page, row set by row set, top first.</summary>
    private static List<List<double>> Boundaries(byte[] pdf, int page) =>
        [.. PdfPathExtractor.Extract(pdf)
            .Where(rect => rect.PageIndex == page && rect.Width < 1 && rect.Height > 3)
            .GroupBy(rect => Math.Round(rect.Top, 1))
            .OrderBy(group => group.Key)
            .Select(group => group.Select(rect => Math.Round(rect.Left + rect.Width / 2, 2))
                .Distinct().OrderBy(x => x).ToList())];

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
