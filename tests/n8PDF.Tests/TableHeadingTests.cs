using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Which rows of a table Word writes again at the top of the page it runs onto.
/// </summary>
/// <remarks>
/// Four tables in <c>table-heading-probe</c>, each long enough to run past the foot of a page, and
/// every row saying in its own text which it is. What Word's export shows, and what this now does:
///
///   one row marked         it is written again above the rest
///   the first two marked   both are
///   the third marked only  none of them are: a heading is the run at the top of a table
///   every row marked       none of them are: there would be no body to put under them
///
/// The last is the one worth stating, because the obvious reading of the format — repeat whatever
/// is marked — never stops repeating.
/// </remarks>
public class TableHeadingTests(ITestOutputHelper output)
{
    /// <summary>The rows of the table on a page, in the order they are written, by their labels.</summary>
    private static List<string> Rows(IReadOnlyList<ExtractedTextRun> runs, int page) =>
    [
        .. runs.Where(r => r.PageIndex == page && r.X < 250)
            .GroupBy(r => Math.Round(r.BaselineY, 1))
            .OrderBy(g => g.Key)
            .Select(g => string.Concat(g.OrderBy(r => r.X).Select(r => r.Text)).Trim())
            .Where(text => text.Contains(" row "))
    ];

    [Theory]
    [InlineData(1, "One row 1", "the one row marked is written again")]
    [InlineData(3, "Two row 1", "the first of two marked rows")]
    [InlineData(5, "Late row 11", "a row marked below the top of the table is no heading")]
    [InlineData(7, "Only row 11", "a table of nothing but headings repeats none of them")]
    public void The_second_page_of_a_table_begins_where_word_begins_it(int page, string first, string what)
    {
        if (TestFonts.SkipForMissingFonts("table-heading-probe")) return;

        var ours = Rows(PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build("table-heading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })), page);

        var word = Rows(PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "table-heading-probe.pdf")), page);

        output.WriteLine($"{what}\n  ours: {string.Join(" | ", ours)}\n  word: {string.Join(" | ", word)}");

        Assert.Equal(word, ours);
        Assert.Equal(first, ours[0]);
    }

    /// <summary>
    /// And the second of two heading rows is there as well, in the order it was written in.
    /// </summary>
    [Fact]
    public void Both_of_two_heading_rows_are_repeated()
    {
        if (TestFonts.SkipForMissingFonts("table-heading-probe")) return;

        var ours = Rows(PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build("table-heading-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })), 3);

        Assert.Equal(["Two row 1", "Two row 2", "Two row 11"], ours.Take(3));
    }
}
