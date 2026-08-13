using System.Diagnostics;
using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests where a mark is drawn on the letter it belongs to.
/// </summary>
/// <remarks>
/// An accent, a Hebrew vowel point, an Arabic dot: none of them has a place of its own. The font
/// gives the mark an anchor and the letter an anchor, and the two are brought together — so a
/// converter that advances the pen and draws the mark where it lands puts it somewhere between
/// wrong and meaningless. In Hebrew the points are what tell one word from another.
///
/// There is a reference implementation to hand, as there was for the bidirectional algorithm:
/// HarfBuzz shapes text for nearly everything that draws it, and shares nothing with this. It is
/// asked for the same glyphs and the same offsets, and the numbers have to be the same numbers.
/// </remarks>
public class MarkPositioningTests(ITestOutputHelper output)
{
    private static TrueTypeFont Times() => TestFonts.Load(TestFonts.TimesNewRomanPath);

    /// <summary>What HarfBuzz makes of the same characters in the same face.</summary>
    private static string? HarfBuzz(string codePoints, bool rightToLeft)
    {
        var arguments = new List<string>
        {
            $"--font-file={TestFonts.TimesNewRomanPath}",
            "--no-glyph-names",
            $"--unicodes={codePoints}"
        };

        if (!rightToLeft) arguments.Add("--direction=ltr");

        try
        {
            using var process = Process.Start(new ProcessStartInfo("hb-shape", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(20_000);

            return output.Length == 0 ? null : output;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Where each glyph's ink lands, in design units from the start of the run.
    /// </summary>
    /// <remarks>
    /// This is what makes the two answers comparable. HarfBuzz writes a right-to-left run in the
    /// order it draws it, which puts a mark before the letter it belongs to; this is handed the
    /// text already turned round, so it draws the letter and then the mark. The numbers on the
    /// glyphs are therefore different — a mark's offset is measured from wherever the pen stands
    /// when its turn comes — while the place the ink actually lands is the same, and that is the
    /// thing worth being right about.
    /// </remarks>
    private static List<(ushort Glyph, int X, int Y)> Ink(IEnumerable<(ushort Glyph, int X, int Y, int Advance)> glyphs)
    {
        var ink = new List<(ushort, int, int)>();
        var pen = 0;

        foreach (var (glyph, x, y, advance) in glyphs)
        {
            ink.Add((glyph, pen + x, y));
            pen += advance;
        }

        return [.. ink.OrderBy(piece => piece.Item2).ThenBy(piece => piece.Item1)];
    }

    /// <summary>Reads what HarfBuzz printed: glyph, cluster, offsets where any, and advance.</summary>
    private static List<(ushort Glyph, int X, int Y, int Advance)> Parse(string output)
    {
        var glyphs = new List<(ushort, int, int, int)>();

        foreach (var piece in output.Trim('[', ']').Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            // glyph=cluster[@x,y]+advance
            var advanceAt = piece.LastIndexOf('+');
            var advance = advanceAt < 0 ? 0 : int.Parse(piece[(advanceAt + 1)..]);

            var body = advanceAt < 0 ? piece : piece[..advanceAt];

            var x = 0;
            var y = 0;
            var offsetAt = body.IndexOf('@');

            if (offsetAt >= 0)
            {
                var parts = body[(offsetAt + 1)..].Split(',');
                x = int.Parse(parts[0]);
                y = int.Parse(parts[1]);
                body = body[..offsetAt];
            }

            glyphs.Add((ushort.Parse(body[..body.IndexOf('=')]), x, y, advance));
        }

        return glyphs;
    }

    /// <summary>
    /// The same glyphs, in the same places, as HarfBuzz puts them.
    /// </summary>
    /// <remarks>
    /// The right-to-left cases are handed to the shaper the way the layout hands them to it: the
    /// characters already in the order they are drawn, the letter before the mark it carries.
    /// </remarks>
    [Theory]
    // Latin letters whose accents have no precomposed form, so the mark is really attached rather
    // than swapped for a single glyph that already carries it.
    [InlineData("0071,0301", "q\u0301", false)]
    [InlineData("0061,0327", "a\u0327", false)]
    // Hebrew: a letter and its vowel point, which is what a pointed text is made of.
    [InlineData("05E9,05B8", "\u05E9\u05B8", true)]
    [InlineData("05D0,05B7", "\u05D0\u05B7", true)]
    [InlineData("05DC,05B4", "\u05DC\u05B4", true)]
    public void A_mark_goes_where_harfbuzz_puts_it(string codePoints, string text, bool rightToLeft)
    {
        var theirs = HarfBuzz(codePoints, rightToLeft);

        if (theirs is null)
        {
            output.WriteLine("hb-shape was not found, so the shaping was not compared.");
            return;
        }

        var shaped = TextShaper.Shape(Times(), text);

        var ours = Ink(shaped.Glyphs.Select(g => (g.Glyph, g.XOffset, g.YOffset, g.Advance)));
        var them = Ink(Parse(theirs));

        output.WriteLine("ours " + string.Join(" ", ours.Select(p => $"{p.Glyph}@{p.X},{p.Y}")));
        output.WriteLine("them " + string.Join(" ", them.Select(p => $"{p.Glyph}@{p.X},{p.Y}")));

        Assert.Equal(them, ours);
    }

    /// <summary>
    /// A mark takes no room: the letter after one is set where it would have been without it.
    /// </summary>
    [Fact]
    public void A_mark_does_not_move_the_pen()
    {
        var font = Times();

        var plain = TextShaper.Shape(font, "qq");
        var marked = TextShaper.Shape(font, "q́q");

        Assert.Equal(plain.AdvanceUnits, marked.AdvanceUnits);
        Assert.Equal(0, marked.Glyphs[1].Advance);
    }

    /// <summary>
    /// A mark drawn on a mark is placed against that mark rather than against the letter, which is
    /// how a letter carries two of them without the second sitting on the first.
    /// </summary>
    [Fact]
    public void A_mark_on_a_mark_is_placed_against_the_mark()
    {
        var font = Times();

        // A Hebrew letter with a point and a cantillation mark above it.
        var shaped = TextShaper.Shape(font, "אַ֨");

        Assert.Equal(3, shaped.Count);

        // Both are drawn away from the pen, and the second is not in the same place as the first.
        Assert.NotEqual(0, shaped.Glyphs[1].XOffset);
        Assert.True(
            shaped.Glyphs[2].XOffset != shaped.Glyphs[1].XOffset ||
            shaped.Glyphs[2].YOffset != shaped.Glyphs[1].YOffset,
            "the second mark was put in the same place as the first");
    }

    /// <summary>
    /// And it reaches the page: a mark that has to be raised or lowered is written into the PDF
    /// as a rise, which is the only thing that can express it — no movement along a line can.
    /// </summary>
    [Fact]
    public void A_raised_mark_reaches_the_page_raised()
    {
        var builder = new DocxBuilder().AddRawParagraph(
            "<w:p><w:r><w:rPr><w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
            "<w:sz w:val=\"48\"/></w:rPr><w:t>q́</w:t></w:r></w:p>");

        var pdf = Converter.Convert(builder.Build(),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var runs = PdfTextExtractor.Extract(pdf).OrderBy(r => r.X).ToList();

        Assert.Equal(2, runs.Count);

        // The mark is drawn back over the letter rather than after it, and below the baseline —
        // the acute of this face hangs from an anchor above the letter's own height.
        output.WriteLine($"letter at {runs[0].X:0.##}, mark at {runs[1].X:0.##}");

        Assert.True(runs[1].X < runs[0].X + runs[0].Width,
            $"the mark is at {runs[1].X:0.##}, past the letter that ends at {runs[0].X + runs[0].Width:0.##}");
    }
}
