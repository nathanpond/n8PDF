using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The box round a paragraph, from <c>w:pBdr</c>.
/// </summary>
/// <remarks>
/// paragraph-border-probe measures every part of it against Word's own ink:
///
///   the reach     the line stands a fiftieth of an inch clear of the text, and its own declared
///                 space beyond that, the space rounded down to the grid — so a space of four
///                 points comes out 3.84 and one of twelve comes out twelve
///   above         one step of the grid more than the space, which is the same step a line box
///                 keeps above its text everywhere else in this engine
///   below         exactly the space, so the box's foot sits on the last line's foot
///   the weight    the line is as thick as its eighths of a point rounded **down** to the grid —
///                 three points comes out 2.88 — and it grows outward from the reach
///   the indents   they move the box, as they move a background; a first-line indent and a
///                 centred line do not
///   in a row      paragraphs bordered alike share one box, with no line between them unless
///                 <c>w:between</c> asks for one, and then it sits at the foot of the paragraph
///                 above with the usual step under it
///   with shading  the background fills the box rather than the lines, so it reaches as far as
///                 the border does
///   the bar       <c>w:bar</c> draws nothing, which is what Word's export has for it
///
/// The first page comes out ink for ink. The pages after it carry a step of drift in the text,
/// which is the paragraph-to-paragraph rounding this engine has everywhere and not this rule, so
/// what they are held to is the geometry that does not depend on where the text landed: the number
/// of boxes, their widths, and their thicknesses.
/// </remarks>
public class ParagraphBorderTests(ITestOutputHelper output)
{
    /// <summary>The first page, ink for ink: two boxes, one of a line and one of three.</summary>
    [Fact]
    public void The_box_covers_what_words_covers()
    {
        if (TestFonts.SkipForMissingFonts("paragraph-border-probe")) return;

        var word = Covered(Ink(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "paragraph-border-probe.pdf")), 0));
        var ours = Covered(Ink(Ours(), 0));

        output.WriteLine($"word covers {word.Count} squares of the grid, ours {ours.Count}, " +
                         $"{word.Intersect(ours).Count()} shared");

        // Word draws each side as a bar between the corners and fills the corners in; this draws
        // the bars corner to corner. The ground covered is the same, which is what is compared.
        Assert.Empty(word.Except(ours));
        Assert.Empty(ours.Except(word));
    }

    /// <summary>
    /// How far the box stands from the text, and how thick its line is, side by side with Word's.
    /// </summary>
    /// <remarks>
    /// Read off the widths of the horizontal bars, which the vertical drift cannot touch: the
    /// space page is 470.88 points of measure plus twice the reach, and the weight page is the
    /// same measure with the line growing outward from it.
    /// </remarks>
    [Theory]
    [InlineData(1, new[] { 472.8, 480.48, 496.8, 534.72 })]   // space 0, 4, 12, 31
    [InlineData(2, new[] { 471.36, 472.8, 476.64, 482.88 })]  // weight 2, 8, 24, 48 eighths
    public void The_box_reaches_as_far_as_words(int page, double[] widths)
    {
        if (TestFonts.SkipForMissingFonts("paragraph-border-probe")) return;

        var word = Bars(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "paragraph-border-probe.pdf")), page);
        var ours = Bars(Ours(), page);

        output.WriteLine($"word {string.Join(" ", word.Select(b => $"{b.Left:0.##}+{b.Width:0.##}"))}");
        output.WriteLine($"ours {string.Join(" ", ours.Select(b => $"{b.Left:0.##}+{b.Width:0.##}"))}");

        // Two bars to a box — its top and its foot — so each width appears twice.
        Assert.Equal(widths.SelectMany(w => new[] { w, w }), ours.Select(b => Math.Round(b.Width, 2)));

        // Word's own bars are the same span less the corners it fills in separately.
        for (var i = 0; i < widths.Length; i++)
        {
            var thickness = ours[i * 2].Height;
            Assert.Equal(widths[i] - 2 * thickness, Math.Round(word[i * 2].Width, 2), 0.001);
            Assert.Equal(thickness, word[i * 2].Height, 0.001);
        }
    }

    /// <summary>
    /// Paragraphs bordered alike share a box; a declared line between them is drawn and nothing
    /// else is.
    /// </summary>
    [Fact]
    public void Paragraphs_bordered_alike_share_one_box()
    {
        if (TestFonts.SkipForMissingFonts("paragraph-border-probe")) return;

        var ours = Bars(Ours(), 4);

        // Three paragraphs, then two with a line between them: a top and a foot for each box, and
        // one line in the middle of the second — five bars, not ten.
        Assert.Equal(5, ours.Count);

        var (first, second) = (ours[..2], ours[2..]);

        Assert.Equal(2, first.Count);
        Assert.Equal(3, second.Count);

        // The line between sits between the two feet, and spans the box without its corners.
        Assert.True(second[1].Top > second[0].Top && second[1].Top < second[2].Top);
        Assert.Equal(second[0].Width - 2 * second[0].Height, second[1].Width, 0.001);
    }

    /// <summary>A paragraph whose only border is a bar draws nothing, as it does in Word.</summary>
    [Fact]
    public void A_bar_draws_nothing()
    {
        if (TestFonts.SkipForMissingFonts("paragraph-border-probe")) return;

        var word = Ink(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "paragraph-border-probe.pdf")), 6);
        var ours = Ink(Ours(), 6);

        // The page holds a rule under one paragraph, a rule over another, and a bar beside a
        // third: two lines in Word's ink and two in ours.
        Assert.Equal(2, word.Count);
        Assert.Equal(2, ours.Count);
    }

    private static byte[] Ours() => Converter.Convert(Fixtures.Build("paragraph-border-probe"),
        new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    private static List<ExtractedRectangle> Ink(byte[] pdf, int page) =>
        [.. PdfPathExtractor.Extract(pdf)
            .Where(r => r.PageIndex == page)
            .OrderBy(r => r.Top).ThenBy(r => r.Left)];

    /// <summary>The horizontal bars of a page: a box's top, its foot, and any line between.</summary>
    private static List<ExtractedRectangle> Bars(byte[] pdf, int page) =>
        [.. Ink(pdf, page).Where(r => r.Width > 100)];

    /// <summary>Which squares of the grid a page's ink covers, so that two shapes can be compared.</summary>
    private static HashSet<(int X, int Y)> Covered(List<ExtractedRectangle> ink)
    {
        var cells = new HashSet<(int, int)>();

        foreach (var r in ink)
        {
            for (var y = (int)Math.Round(r.Top / 0.24); y < (int)Math.Round(r.Bottom / 0.24); y++)
            {
                for (var x = (int)Math.Round(r.Left / 0.24); x < (int)Math.Round(r.Right / 0.24); x++)
                {
                    cells.Add((x, y));
                }
            }
        }

        return cells;
    }
}
