using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// How wide Word thinks a piece of text is.
/// </summary>
/// <remarks>
/// It is the font's own advances at the font's own resolution, and nothing else: no quantising to
/// the twip, no rounding to the grid, no per-run fudge. text-measure-probe sets every line against
/// the right margin, so where a line begins is the margin less the width Word measured, and repeats
/// the same string up to forty times so that a single rounding is divided by forty. Over eighty
/// lines — Times at eleven, twelve and thirteen and a half points, and Arial at twelve — every one
/// of ours begins exactly where Word's does.
///
/// The probe also lays a trap that this repository walked into and can now walk out of. A PDF
/// records the widths it draws with in thousandths of an em, so reading Word's own export back
/// gives 444 thousandths for Times 'a' — 5.328 points at twelve. Word did not measure it as 5.328:
/// the font says 909 units of 2048, which is 5.32618. Two hundredths of a point per letter, which
/// is nothing on a line and a whole step of the grid across a table column, and it was blamed on
/// "our measure running a hair above Word's" in two commits before this probe was written. It runs
/// exactly with Word's. What is left over in a table column is the column, not the text.
/// </remarks>
public class TextMeasureTests(ITestOutputHelper output)
{
    /// <summary>Every line of the probe begins exactly where Word's does.</summary>
    [Fact]
    public void Every_line_begins_where_words_does()
    {
        if (TestFonts.SkipForMissingFonts("text-measure-probe")) return;

        var word = Starts(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "text-measure-probe.pdf")));
        var ours = Starts(Converter.Convert(Fixtures.Build("text-measure-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        Assert.Equal(word.Count, ours.Count);

        var worst = 0.0;

        for (var i = 0; i < word.Count; i++)
        {
            worst = Math.Max(worst, Math.Abs(word[i].X - ours[i].X));

            Assert.Equal(word[i].PageIndex, ours[i].PageIndex);
            Assert.Equal(word[i].X, ours[i].X, 0.001);
        }

        output.WriteLine($"{word.Count} lines, worst {worst:0.####}pt apart");
        Assert.True(word.Count >= 80, $"the probe should hold eighty lines, not {word.Count}");
    }

    /// <summary>
    /// The widths Word's own page implies are whole numbers of the font's units, and are not whole
    /// numbers of the thousandths its PDF writes them down in.
    /// </summary>
    /// <remarks>
    /// Forty letters to a line divides the reading error by forty, which is what makes the two
    /// tellable apart at all: 909/2048 and 444/1000 are two hundredths of a point apart at twelve
    /// point, and a page of forty spreads that to nearly a tenth.
    /// </remarks>
    [Theory]
    [InlineData(1, 909)]     // Times 'a'
    [InlineData(5, 1024)]    // 'b', which is a round number in both and so tells nothing on its own
    [InlineData(9, 569)]     // 'i'
    [InlineData(13, 1821)]   // 'M'
    public void Word_measures_in_the_fonts_own_units(int line, int units)
    {
        if (TestFonts.SkipForMissingFonts("text-measure-probe")) return;

        // The four lines of each letter are one, five, ten and forty copies; the fortieth is three
        // lines past the first.
        var starts = Starts(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "text-measure-probe.pdf")))
            .Where(start => start.PageIndex == 0)
            .ToList();

        const double Right = 540;
        const double Size = 12;
        const int Copies = 40;
        const int PerEm = 2048;

        var width = (Right - starts[line + 3].X) / Copies;
        var implied = width / Size * PerEm;

        output.WriteLine($"{width:0.#####}pt a letter, which is {implied:0.###} units of {PerEm} " +
                         $"and {width / Size * 1000:0.###} of a thousand");

        Assert.Equal(units, implied, 0.02);

        // And the same width in the units a PDF writes: a whole number only where the font's own
        // number happens to be one, which is why reading it back that way misleads.
        var thousandths = width / Size * 1000;

        if (units * 1000 % PerEm != 0)
        {
            Assert.True(Math.Abs(thousandths - Math.Round(thousandths)) > 0.05,
                $"{thousandths:0.###} thousandths should not be a whole number");
        }
    }

    /// <summary>Where each line of the document begins, in reading order.</summary>
    private static List<(int PageIndex, double X, double BaselineY)> Starts(byte[] pdf)
    {
        var lines = new List<(int PageIndex, double X, double BaselineY)>();

        foreach (var run in PdfTextExtractor.Extract(pdf)
                     .OrderBy(run => run.PageIndex).ThenBy(run => run.BaselineY).ThenBy(run => run.X))
        {
            if (lines.Any(line => line.PageIndex == run.PageIndex &&
                                  Math.Abs(line.BaselineY - run.BaselineY) < 0.01))
            {
                continue;
            }

            lines.Add((run.PageIndex, run.X, run.BaselineY));
        }

        return lines;
    }
}
