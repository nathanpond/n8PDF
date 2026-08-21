using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The boxes a form is filled in by: <c>w:checkBox</c> inside a legacy form field.
/// </summary>
/// <remarks>
/// A checkbox draws no text at all — the box is the field, and Word draws it with lines rather
/// than setting a character from a face. <c>checkbox-probe</c> puts fifteen of them to Word, ten
/// sizes from eight point to seventy-two, stated on the field and taken from the text round it,
/// and three numbers come straight off the drawing:
///
///   * The field is 1.15 times the size wide — exactly that, at every size measured.
///   * The box is drawn in the middle of it, 2.2 points narrower.
///   * Its foot sits below the baseline by 0.216 of the size, less 1.2 points.
///
/// The line is drawn three quarters of a point thick whatever the size of the box, and a ticked
/// one takes a cross of two half-point lines corner to corner. Word strokes its square where this
/// fills the four sides of one, which covers the same ground: the comparison is of ink.
/// </remarks>
public class CheckBoxTests(ITestOutputHelper output)
{
    private const double Scale = 4;

    /// <summary>The whole page, ink for ink.</summary>
    [Fact]
    public void The_boxes_cover_what_words_cover()
    {
        if (TestFonts.SkipForMissingFonts("checkbox-probe")) return;

        var reference = File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "checkbox-probe.pdf"));
        var ours = Ours();

        if (PdfRasterizer.Render(ours, 0, Scale) is not { } mine ||
            PdfRasterizer.Render(reference, 0, Scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        // Only where the boxes are: the text beside them is compared line by line elsewhere, and a
        // baseline a step of the grid from Word's would count here as ink out of place.
        var boxes = Boxes(ours);
        var apart = 0;
        var ink = 0;

        foreach (var box in boxes)
        for (var y = box.Top - 2; y < box.Bottom + 2; y += 1.0 / Scale)
        for (var x = box.Left - 2; x < box.Right + 2; x += 1.0 / Scale)
        {
            var (r1, g1, b1) = mine.At(x, y, Scale);
            var (r2, g2, b2) = word.At(x, y, Scale);

            var here = r1 + g1 + b1 < 700;
            var theirs = r2 + g2 + b2 < 700;

            if (here) ink++;
            if (here != theirs) apart++;
        }

        output.WriteLine($"{boxes.Count} boxes, {ink} points of ink, {apart} apart");

        // Within a tenth of the ink: a stroked square and four filled bars cover the same ground,
        // but not to the last pixel of the rasteriser's edges.
        Assert.True(apart < ink / 10,
            $"{apart} points differ where the boxes are, of {ink} points of ink");
    }

    /// <summary>
    /// How wide the field is: 1.15 times the size, which is what the text either side of it says.
    /// </summary>
    [Theory]
    [InlineData(0, 12, "sized to the twelve point text round it")]
    [InlineData(5, 8, "eight point, stated")]
    [InlineData(8, 24, "twenty-four point, stated")]
    [InlineData(11, 72, "seventy-two point, stated")]
    [InlineData(14, 24, "twenty-four point text, sized to it")]
    public void The_field_is_as_wide_as_word_makes_it(int line, double size, string what)
    {
        if (TestFonts.SkipForMissingFonts("checkbox-probe")) return;

        output.WriteLine(what);

        var gaps = Gaps(Ours());
        var word = Gaps(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "checkbox-probe.pdf")));

        output.WriteLine($"word {word[line]:0.###}, ours {gaps[line]:0.###}, 1.15 x size {size * 1.15:0.###}");

        Assert.Equal(size * 1.15, gaps[line], 0.15);
        Assert.Equal(word[line], gaps[line], 0.15);
    }

    /// <summary>
    /// Where the box itself is drawn, against the numbers read out of Word's own file. Word
    /// strokes a square of 11.52 points about (111.36, 72.96) for a twelve point box, three
    /// quarters of a point thick, so its ink runs from 111.0 to 123.24 across and 72.6 to 84.84
    /// down — the line falling half either side of the path.
    /// </summary>
    [Fact]
    public void The_box_is_drawn_where_word_draws_it()
    {
        if (TestFonts.SkipForMissingFonts("checkbox-probe")) return;

        var box = Boxes(Ours())[0];

        output.WriteLine($"ours {box.Left:0.##}..{box.Right:0.##} x {box.Top:0.##}..{box.Bottom:0.##}");

        Assert.Equal(111.0, box.Left, 0.1);
        Assert.Equal(123.24, box.Right, 0.1);
        Assert.Equal(72.6, box.Top, 0.1);
        Assert.Equal(84.84, box.Bottom, 0.1);
    }

    /// <summary>
    /// A ticked box is crossed and an empty one is not, which is a matter of how much ink stands
    /// inside the square.
    /// </summary>
    [Fact]
    public void A_ticked_box_is_crossed_and_an_empty_one_is_not()
    {
        if (TestFonts.SkipForMissingFonts("checkbox-probe")) return;

        var ours = Ours();

        if (PdfRasterizer.Render(ours, 0, Scale) is not { } page)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            return;
        }

        // The first box is empty and the second ticked; the inside of one is bare and of the other
        // is not.
        var boxes = Boxes(ours);

        double Inside((double Left, double Top, double Right, double Bottom) box)
        {
            var ink = 0;
            var all = 0;

            for (var y = box.Top + 2; y < box.Bottom - 2; y += 1.0 / Scale)
            for (var x = box.Left + 2; x < box.Right - 2; x += 1.0 / Scale)
            {
                var (r, g, b) = page.At(x, y, Scale);
                all++;
                if (r + g + b < 700) ink++;
            }

            return all == 0 ? 0 : (double)ink / all;
        }

        var empty = Inside(boxes[0]);
        var ticked = Inside(boxes[1]);

        output.WriteLine($"empty {empty:P1} inked, ticked {ticked:P1}");

        Assert.True(empty < 0.01, $"the empty box has {empty:P1} of ink inside it");
        Assert.True(ticked > 0.05, $"the ticked box has only {ticked:P1} of ink inside it");
    }

    private static byte[] Ours() =>
        Converter.Convert(Fixtures.Build("checkbox-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>Where each box stands, gathered from the four bars it is drawn with.</summary>
    private static List<(double Left, double Top, double Right, double Bottom)> Boxes(byte[] pdf)
    {
        var bars = PdfPathExtractor.Extract(pdf).Where(rect => rect.PageIndex == 0).ToList();
        var boxes = new List<(double Left, double Top, double Right, double Bottom)>();

        foreach (var bar in bars.OrderBy(bar => bar.Top).ThenBy(bar => bar.Left))
        {
            var at = boxes.FindIndex(box =>
                bar.Left < box.Right + 1 && bar.Right > box.Left - 1 &&
                bar.Top < box.Bottom + 1 && bar.Bottom > box.Top - 1);

            if (at < 0)
            {
                boxes.Add((bar.Left, bar.Top, bar.Right, bar.Bottom));
                continue;
            }

            var found = boxes[at];
            boxes[at] = (Math.Min(found.Left, bar.Left), Math.Min(found.Top, bar.Top),
                Math.Max(found.Right, bar.Right), Math.Max(found.Bottom, bar.Bottom));
        }

        return boxes.OrderBy(box => box.Top).ToList();
    }

    /// <summary>The widest gap in each line, which is the room the field took.</summary>
    private static List<double> Gaps(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .GroupBy(run => Math.Round(run.BaselineY, 2))
            .OrderBy(line => line.Key)
            .Select(line =>
            {
                var runs = line.OrderBy(run => run.X).ToList();
                var widest = 0.0;

                for (var i = 1; i < runs.Count; i++)
                    widest = Math.Max(widest, runs[i].X - (runs[i - 1].X + runs[i - 1].Width));

                return widest;
            })
            .ToList();
}
