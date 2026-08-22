using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// How far a word may overrun the measure before Word breaks it: not at all.
/// </summary>
/// <remarks>
/// break-tolerance-probe moves the measure a twip at a time — a twentieth of a point, five times
/// finer than the grid — past the width of a word that has nowhere to break. Ten capital Ms of
/// Times at twelve point are 106.6992 points wide. A measure of 106.7 holds them; 106.65 does not,
/// and Word sets nine of them and carries the tenth. There is no slack in it, and the same is true
/// in a table cell.
///
/// That answers a question two probes had raised the other way. A word had seemed to survive in a
/// column a tenth of a point too narrow for it, which looked like tolerance and was not: a column
/// is **drawn** on the grid but the text in it is broken against the width the arithmetic gave,
/// and the two differ by up to half a step. Hence the exact widths carried alongside the snapped
/// ones in ComputeColumnWidths — with them, every column of four table probes is Word's exactly.
///
/// It also corrects what this repository believed about a page. "A page lets a long word overrun
/// the margin and stay whole" was written from two probes that both held boxes; measured directly,
/// a page breaks the word exactly as a box does.
/// </remarks>
public class BreakToleranceTests(ITestOutputHelper output)
{
    /// <summary>The word's own width, which is where the threshold sits.</summary>
    private const double Word = 106.6992;

    /// <summary>
    /// The measures the probe sets, in the order it sets them: an indent of 7220 twips leaves
    /// 107 points, and every twip after that takes a twentieth of a point off.
    /// </summary>
    private static readonly int[] Indents =
        [7220, 7224, 7226, 7227, 7228, 7229, 7230, 7231, 7232, 7233, 7234, 7236, 7238, 7240, 7245];

    [Fact]
    public void A_word_is_broken_as_soon_as_it_passes_the_measure()
    {
        if (TestFonts.SkipForMissingFonts("break-tolerance-probe")) return;

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "break-tolerance-probe.pdf")), 0);
        var ours = Lines(Converter.Convert(Fixtures.Build("break-tolerance-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), 0);

        Assert.Equal(word.Count, ours.Count);
        Assert.Equal(word, ours);

        // Walk the paragraphs in order: each is one line while it fits and two once it does not.
        var line = 0;

        foreach (var indent in Indents)
        {
            var measure = 468 - indent / 20.0;
            var fits = measure >= Word;

            output.WriteLine($"{measure:0.###}pt of measure: {(fits ? "whole" : "broken")}");

            Assert.Equal(fits ? 10 : 9, word[line].Length);
            Assert.Equal(fits ? 10 : 9, ours[line].Length);

            line += fits ? 1 : 2;
        }
    }

    /// <summary>The same in a cell, where the column can only be set to a step of the grid.</summary>
    [Fact]
    public void A_cell_breaks_the_word_on_the_same_terms()
    {
        if (TestFonts.SkipForMissingFonts("break-tolerance-probe")) return;

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "break-tolerance-probe.pdf")), 1);
        var ours = Lines(Converter.Convert(Fixtures.Build("break-tolerance-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }), 1);

        Assert.Equal(word, ours);

        // The six cells are 106.8 points wide and then five steps narrower in turn, so only the
        // first can hold the word; the rest break it. The dashes between them are the rails.
        var ofTheWord = word.Where(text => text.StartsWith('M')).ToList();

        Assert.Equal(11, ofTheWord.Count);
        Assert.Equal(10, ofTheWord[0].Length);

        for (var i = 1; i < ofTheWord.Count; i += 2)
        {
            Assert.Equal(9, ofTheWord[i].Length);
            Assert.Equal(1, ofTheWord[i + 1].Length);
        }
    }

    /// <summary>The text of each line of a page, in order down the page.</summary>
    private static List<string> Lines(byte[] pdf, int page) =>
        [.. PdfTextExtractor.Extract(pdf)
            .Where(run => run.PageIndex == page && run.Text.Trim().Length > 0)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(group => group.Key)
            .Select(group => string.Concat(group.OrderBy(run => run.X).Select(run => run.Text)).Trim())];
}
