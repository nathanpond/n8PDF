using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Equations: what the markup says, what the face says, and where Word puts the result.
/// </summary>
/// <remarks>
/// The numbers asserted here were measured from Word's own export of the equations fixture, which
/// is committed beside it. Where one of them is a rule of the OpenType specification the rule is
/// named; where it is a number Word uses and nothing explains, it says so.
/// </remarks>
public class MathTests(ITestOutputHelper output)
{
    private static byte[] Ours() => Converter.Convert(Fixtures.Build("equations"),
        new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    private static byte[] Theirs() =>
        File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, "equations.pdf"));

    // ------------------------------------------------------------------ the markup

    [Fact]
    public void An_equation_is_read_as_a_tree_rather_than_a_line_of_runs()
    {
        var math = System.Xml.Linq.XElement.Parse(
            """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:f><m:num><m:r><m:t>a</m:t></m:r></m:num>
                  <m:den><m:r><m:t>b</m:t></m:r></m:den></m:f>
            </m:oMath>
            """);

        var fraction = Assert.IsType<MathFraction>(OfficeMath.Parse(math));

        Assert.Equal("a", Assert.IsType<MathText>(fraction.Numerator).Text);
        Assert.Equal("b", Assert.IsType<MathText>(fraction.Denominator).Text);
        Assert.Equal("bar", fraction.Type);
    }

    [Fact]
    public void What_is_not_understood_still_gives_up_its_text()
    {
        var math = System.Xml.Linq.XElement.Parse(
            """
            <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
              <m:invented><m:r><m:t>kept</m:t></m:r></m:invented>
            </m:oMath>
            """);

        var text = Assert.IsType<MathText>(OfficeMath.Parse(math));
        Assert.Equal("kept", text.Text);
    }

    [Theory]
    [InlineData("nor", true)]
    [InlineData("sty", false)]
    public void A_run_says_whether_it_is_upright(string element, bool bare)
    {
        var inner = bare
            ? "<m:nor/>"
            : "<m:sty m:val=\"p\"/>";

        var math = System.Xml.Linq.XElement.Parse(
            $"""
             <m:oMath xmlns:m="http://schemas.openxmlformats.org/officeDocument/2006/math">
               <m:r><m:rPr>{inner}</m:rPr><m:t>sin</m:t></m:r>
             </m:oMath>
             """);

        Assert.True(Assert.IsType<MathText>(OfficeMath.Parse(math)).Upright, element);
    }

    // ------------------------------------------------------------------- the face

    [Fact]
    public void The_math_table_reads_cambrias_own_numbers()
    {
        var font = TestFonts.CreatePinnedLibrary().Resolve("Cambria Math").Font;

        var math = font.Mathematics;

        // The table states them in the face's own design units, and these are Cambria Math's.
        Assert.Equal(585, math.AxisHeight, 0);
        Assert.Equal(750, math.SuperscriptShiftUp, 0);
        Assert.Equal(615, math.SuperscriptShiftUpCramped, 0);
        Assert.Equal(418, math.SubscriptShiftDown, 0);
        Assert.Equal(133, math.FractionRuleThickness, 0);
        Assert.Equal(65, math.RadicalDegreeBottomRaisePercent, 0);

        // Word's own script sizes are seven tenths and 0.58, not the face's.
        Assert.Equal(73, math.ScriptPercentScaleDown, 0);
        Assert.Equal(60, math.ScriptScriptPercentScaleDown, 0);
    }

    [Fact]
    public void The_face_offers_taller_brackets_and_says_how_far_they_lean()
    {
        var font = TestFonts.CreatePinnedLibrary().Resolve("Cambria Math").Font;

        var paren = font.GetGlyphIndex('(');

        Assert.True(font.MathVariants.TryGetValue(paren, out var taller));
        Assert.True(taller!.Count >= 4);

        // The first of them is the ordinary bracket, and each is taller than the last.
        Assert.Equal(paren, taller[0].Glyph);
        for (var i = 1; i < taller.Count; i++) Assert.True(taller[i].Height > taller[i - 1].Height);

        // The integral leans, and the face says by how much: 415 design units.
        Assert.Equal(415, font.ItalicCorrections[font.GetGlyphIndex(0x222B)]);
    }

    // -------------------------------------------------------- where Word puts it

    /// <summary>
    /// Where each equation begins, against where Word begins it.
    /// </summary>
    /// <remarks>
    /// The numbers are read off Word's own export of this fixture, which is committed beside it —
    /// the whole of both files is compared line by line in TextPositionComparisonTests, and this
    /// says in one place what the answers are for the constructs that have a rule of their own.
    ///
    /// Only where an equation begins is asserted here. How tall the line holding it comes out is
    /// the part that is not yet Word's, and is recorded where the comparison records it.
    /// </remarks>
    [Theory]
    [InlineData("Fraction: ", 118.32)]
    [InlineData("Superscript: ", 132.99)]
    [InlineData("Root: ", 101.76)]
    [InlineData("Delimited: ", 126.24)]
    [InlineData("Sum: ", 100.32)]
    [InlineData("Integral: ", 115.68)]
    [InlineData("Matrix: ", 110.99)]
    public void An_equation_begins_where_word_begins_it(string label, double x)
    {
        var runs = PdfTextExtractor.Extract(Ours());

        var words = runs.First(run => run.Text.StartsWith(label, StringComparison.Ordinal));

        var start = runs
            .Where(run => Math.Abs(run.BaselineY - words.BaselineY) < 11 &&
                          run.X >= words.X + words.Width - 0.5 &&
                          !string.IsNullOrWhiteSpace(run.Text))
            .Min(run => run.X);

        output.WriteLine($"{label.Trim()} begins at {start:0.####}, Word's at {x}");

        Assert.InRange(start - x, -0.4, 0.4);
    }

    [Fact]
    public void An_inline_equation_is_set_at_word_s_own_proportions()
    {
        var runs = PdfTextExtractor.Extract(Ours());

        // Word draws the letters at the type size and everything it stretches at 0.92 of it, and
        // sets a script at seven tenths.
        var sizes = runs.Where(run => run.X > 100).Select(run => Math.Round(run.FontSize, 2))
            .Distinct().OrderBy(size => size).ToList();

        Assert.Contains(6.96, sizes);   // 12 x 0.58, a degree
        Assert.Contains(8.4, sizes);    // 12 x 0.7, a script
        Assert.Contains(11.04, sizes);  // 12 x 0.92, a bracket or a radical
        Assert.Contains(12.0, sizes);
    }

    /// <summary>
    /// The four placements the OpenType rules turn on, each against Word's own.
    /// </summary>
    [Theory]
    [InlineData("Superscript: ", "2", -4.08)]
    [InlineData("Subscript: ", "n", 2.16)]
    [InlineData("Both: ", "2", -4.56)]
    [InlineData("Both: ", "i", 2.64)]
    public void A_script_sits_where_word_sits_it(string label, string script, double offset)
    {
        var runs = PdfTextExtractor.Extract(Ours());

        var line = runs.First(run => run.Text.StartsWith(label, StringComparison.Ordinal));

        var mapped = script switch { "n" => "𝑛", "i" => "𝑖", _ => script };

        var piece = runs.First(run =>
            run.Text == mapped && Math.Abs(run.BaselineY - line.BaselineY) < 10 && run.X > line.X);

        Assert.Equal(offset, piece.BaselineY - line.BaselineY, 1);
    }

    [Fact]
    public void A_bracket_grows_by_the_shapes_the_face_keeps_rather_than_by_being_drawn_larger()
    {
        var runs = PdfTextExtractor.Extract(Ours());

        var brackets = runs.Where(run => run.Text is "(" or ")").OrderBy(run => run.BaselineY).ToList();

        Assert.Equal(4, brackets.Count);

        // Every one of them is drawn at the size an inline equation stretches at — none is blown
        // up to reach.
        Assert.All(brackets, bracket => Assert.Equal(11.04, bracket.FontSize, 2));

        // The pair round a+b are the plain shape and the pair round a over b a taller one, which
        // the face draws wider: the room each takes is the shape's own advance and not the text's.
        var round = brackets[1].X - brackets[0].X;
        var tall = brackets[3].X - brackets[2].X;

        Assert.InRange(round, 31, 32.5);
        Assert.InRange(tall, 9.5, 11.0);
    }

    [Fact]
    public void An_equation_on_a_line_of_its_own_is_centred_and_set_at_the_full_size()
    {
        var ours = PdfTextExtractor.Extract(Ours());
        var theirs = PdfTextExtractor.Extract(Theirs());

        // The radical of the quadratic formula: at twelve point in a display equation, where the
        // same radical in a sentence is 11.04.
        var mine = ours.Single(run => run.Text == "√" && run.FontSize > 11.5);
        var word = theirs.Single(run => run.Text == "√" && run.FontSize > 11.5);

        Assert.Equal(word.FontSize, mine.FontSize, 2);
        Assert.InRange(mine.X - word.X, -0.5, 0.5);

        // And centred on the measure rather than set at the margin.
        Assert.True(mine.X > 200, $"a display equation was set at {mine.X:0.#}");
    }

    [Fact]
    public void The_bars_of_a_fraction_are_where_word_draws_them()
    {
        var ours = PdfPathExtractor.Extract(Ours()).OrderBy(rule => rule.Top).ToList();
        var theirs = PdfPathExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "equations.pdf")).OrderBy(rule => rule.Top).ToList();

        Assert.Equal(theirs.Count, ours.Count);

        for (var i = 0; i < ours.Count; i++)
        {
            output.WriteLine($"{ours[i]}   word {theirs[i]}");

            // Where it begins, to within the rounding Word applies to a position — and to within
            // a third of a point on the display fraction, which is centred and so carries half of
            // the difference in its own width.
            Assert.InRange(ours[i].Left - theirs[i].Left, -0.4, 0.4);

            // Word draws a bar a shade wider than what stands over it — an eighth of a point
            // on four of the six, a fifth on the fraction of sums and seven tenths on the display
            // fraction, which is the one place its bar reaches more than a quarter of a point
            // past ours. The root's is a hair the other way.
            Assert.InRange(ours[i].Width - theirs[i].Width, -0.75, 0.05);

            // A bar is drawn at the thickness the face states, which is what Word draws.
            Assert.Equal(theirs[i].Height, ours[i].Height, 2);
        }
    }

    /// <summary>
    /// The page itself, drawn and looked at, against Word's own.
    /// </summary>
    /// <remarks>
    /// Every line is compared against its counterpart aligned on its own baseline, so that what is
    /// asked about is the equation rather than the height of the line it sits on.
    /// </remarks>
    [Fact]
    public void The_equations_cover_what_word_covers()
    {
        var ourBytes = Ours();
        var wordBytes = Theirs();

        const double scale = 4;

        if (PdfRasterizer.Render(ourBytes, 0, scale) is not { } mine ||
            PdfRasterizer.Render(wordBytes, 0, scale) is not { } word)
        {
            Assert.False(PdfRasterizer.IsRequired, PdfRasterizer.UnavailableMessage);
            output.WriteLine(PdfRasterizer.UnavailableMessage);
            return;
        }

        var ourLines = Labels(PdfTextExtractor.Extract(ourBytes));
        var wordLines = Labels(PdfTextExtractor.Extract(wordBytes));

        Assert.Equal(wordLines.Count, ourLines.Count);

        var (agreed, covered, inkOfMine, inkOfTheirs) = (0, 0, 0, 0);

        for (var i = 0; i < ourLines.Count; i++)
        {
            for (var dy = -11.0; dy < 7.5; dy += 0.25)
            for (var x = 66.0; x < 420; x += 0.25)
            {
                var a = mine.At(x, ourLines[i] + dy, scale);
                var b = word.At(x, wordLines[i] + dy, scale);

                var ink = a.R < 200 || a.G < 200 || a.B < 200;
                var theirInk = b.R < 200 || b.G < 200 || b.B < 200;

                if (ink) inkOfMine++;
                if (theirInk) inkOfTheirs++;
                if (ink == theirInk) agreed++;

                covered++;
            }
        }

        var agreement = 100.0 * agreed / covered;

        output.WriteLine($"equations: ink {inkOfMine} here, {inkOfTheirs} in Word's; " +
                         $"the two agree on {agreement:0.00}%");

        Assert.True(agreement > 99, $"the two pages agree on only {agreement:0.00}% of the equations");
        Assert.InRange((double)inkOfMine / inkOfTheirs, 0.9, 1.15);

        static List<double> Labels(IReadOnlyList<ExtractedTextRun> runs) =>
        [
            .. runs.Where(run => run.X < 80).Select(run => run.BaselineY).Distinct().Order()
        ];
    }
}
