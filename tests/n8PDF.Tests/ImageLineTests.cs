using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// How tall a line is when a picture on it stands taller than the text beside it, and the
/// paragraph asks for a multiple of the line.
/// </summary>
/// <remarks>
/// A multiple is a multiple of the line the <em>text</em> would have made. The picture raises the
/// ascent and is not scaled with it, so the room left under a picture is the room left under the
/// text alone: a ninety-six point picture on Word's own 1.08 line is 99.6 points tall, not the
/// 106.8 that multiplying the whole line box gives.
///
/// image-line-probe measures it sixteen ways — pictures of six, twelve, twenty-four and ninety-six
/// points at multiples of one, 1.08, one and a half and two. Two of the heights are shorter than
/// the line twelve point Times makes on its own, so the plain text rule is measured in the same
/// document as the rule that replaces it.
///
/// Every fixture written by hand here sets the spacing to a single line, where a multiple of one
/// makes the two readings identical; it took a document Word wrote — brochure, whose picture
/// paragraph inherits the 1.08 of Word's Normal — to tell them apart, which is what the real
/// documents are for.
/// </remarks>
public class ImageLineTests(ITestOutputHelper output)
{
    /// <summary>
    /// Every baseline of the probe against Word's own.
    /// </summary>
    /// <remarks>
    /// Twenty-two of the thirty-two land exactly on Word's and the other ten a single step of the
    /// grid off, which is the grid model's own residual rather than this rule's: what is left over
    /// is where a snapped height and a snapped descent leave the position after them, the same
    /// last step line-ascent-probe and line-grid-probe both stop one short on. The rule this
    /// fixture exists for is worth 6.8 points at ninety-six, so a regression in it could not hide
    /// inside a step.
    /// </remarks>
    [Fact]
    public void Every_line_of_the_probe_is_within_a_step_of_words()
    {
        if (TestFonts.SkipForMissingFonts("image-line-probe")) return;

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "image-line-probe.pdf")));
        var ours = Lines(Converter.Convert(Fixtures.Build("image-line-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        Assert.Equal(word.Count, ours.Count);

        var apart = word.Zip(ours, (w, o) => Math.Abs(w.Baseline - o.Baseline)).ToList();

        for (var i = 0; i < word.Count; i++)
        {
            output.WriteLine(
                $"page {word[i].Page,2}: word {word[i].Baseline,7:0.##}  ours {ours[i].Baseline,7:0.##}" +
                (apart[i] > 0.001 ? $"  {apart[i]:0.##} out" : ""));
        }

        Assert.True(apart.Max() <= Step + 0.001,
            $"a baseline is {apart.Max():0.###}pt from Word's");

        var exact = apart.Count(a => a <= 0.001);
        output.WriteLine($"{exact} of {apart.Count} exact");

        Assert.True(exact >= 22,
            $"only {exact} of {apart.Count} baselines are exactly Word's, where 22 were");
    }

    /// <summary>
    /// The rule itself, read off Word's own export and then off ours: the room under a picture
    /// does not grow with the picture.
    /// </summary>
    /// <remarks>
    /// Each page holds a picture line and, under it, a marker paragraph of plain text at a single
    /// line. The gap between the two baselines is the picture line's descent plus the marker's own
    /// ascent, and the marker never changes — so the gap is the measurement. Taking the multiple
    /// of the whole line box instead would grow that gap by nearly eight points as the picture
    /// grows from twelve points to ninety-six.
    ///
    /// The six point picture is left out of the comparison: it is shorter than the text's own
    /// ascent, so its line is a plain text line and belongs to the rule this one replaces.
    /// </remarks>
    [Fact]
    public void The_room_under_a_picture_does_not_grow_with_the_picture()
    {
        if (TestFonts.SkipForMissingFonts("image-line-probe")) return;

        // The probe walks the four multiples in turn, four picture heights inside each, and the
        // first height of each four is the short one.
        double[] expected = [13.68, 14.64, 20.64, 27.36];

        Check("word", File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "image-line-probe.pdf")));
        Check("ours", Converter.Convert(Fixtures.Build("image-line-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        void Check(string whose, byte[] pdf)
        {
            var lines = Lines(pdf);
            Assert.Equal(32, lines.Count);

            var gaps = new List<double>();
            for (var i = 0; i < lines.Count; i += 2) gaps.Add(lines[i + 1].Baseline - lines[i].Baseline);

            output.WriteLine($"{whose}: {string.Join("  ", gaps.Select(g => g.ToString("0.##")))}");

            for (var multiple = 0; multiple < 4; multiple++)
            {
                for (var height = 1; height < 4; height++)
                {
                    var gap = gaps[multiple * 4 + height];

                    Assert.True(Math.Abs(gap - expected[multiple]) <= Step + 0.001,
                        $"{whose}: picture {height} at multiple {multiple} leaves {gap:0.###}pt " +
                        $"under it, not the {expected[multiple]}pt the text alone asks for");
                }
            }
        }
    }

    private const double Step = 0.24;

    private static List<(int Page, double Baseline)> Lines(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .GroupBy(run => (run.PageIndex, Math.Round(run.BaselineY, 2)))
            .Select(line => (Page: line.Key.PageIndex, Baseline: line.Key.Item2))
            .OrderBy(line => line.Page).ThenBy(line => line.Baseline)
            .ToList();
}
