using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Which shape a bracket grows into, against Word's own.
/// </summary>
/// <remarks>
/// The face keeps a series of round brackets — eight of them, each taller and wider than the last
/// — and a recipe for building one taller than any of them. math-bracket-probe walks a bracket up
/// the whole series by growing what it holds from twelve point to seventy-two while the equation
/// stays at twelve, so the bracket is drawn at twelve throughout and only the shape changes, and
/// then does it again in a twenty-four point equation.
///
/// Which shape was picked is read off the page as the room the opening bracket takes: the eight
/// differ in width, and the letter after it begins that much further along.
/// </remarks>
public class MathBracketTests(ITestOutputHelper output)
{
    [Fact]
    public void Every_bracket_is_the_shape_word_picked()
    {
        var ours = Brackets(PdfTextExtractor.Extract(Converter.Convert(
            Fixtures.Build("math-bracket-probe"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() })));

        var word = Brackets(PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "math-bracket-probe.pdf")));

        // One of the twenty-two is left out: the built-up bracket, whose pieces stand well off
        // the line and which A_bracket_past_the_series_is_built_out_of_pieces measures instead.
        Assert.Equal(22, word.Count);
        Assert.Equal(word.Count, ours.Count);

        var worst = 0.0;

        for (var i = 0; i < word.Count; i++)
        {
            worst = Math.Max(worst, Math.Abs(ours[i] - word[i]));

            output.WriteLine($"bracket {i,2}: {ours[i],7:0.####} against {word[i],7:0.####}");

            Assert.True(Math.Abs(ours[i] - word[i]) < 0.06,
                $"bracket {i} takes {ours[i]:0.####} where Word's takes {word[i]:0.####}, " +
                $"which is a different shape");
        }

        output.WriteLine($"twenty-two brackets, worst {worst:0.####}pt");
    }

    /// <summary>
    /// Past the end of the series, a bracket is built out of the pieces the face keeps for it.
    /// </summary>
    /// <remarks>
    /// The last probe holds a seventy-two point letter in a twelve point equation, which asks for
    /// a bracket 61.5 points tall where the tallest shape the face keeps is 52. Word builds one
    /// there and so does this: three pieces, a head and a foot with a middle between them, their
    /// baselines 12.96 and 25.92 apart.
    /// </remarks>
    [Fact]
    public void A_bracket_past_the_series_is_built_out_of_pieces()
    {
        var word = PdfTextExtractor.ExtractFile(
            Path.Combine(TestPaths.ReferencePdfs, "math-bracket-probe.pdf"));

        // The seventy-two point letter, and the pieces standing to the left of it. Only one of
        // the three carries any text — so that a reader copies one bracket rather than three
        // pieces of one — which is why ours are counted in the layout rather than in the file.
        var theirs = word.Last(run => run.FontSize > 70);

        var wordPieces = word
            .Where(run => run.FontSize > 3 && run.X > 72.3 && run.X < 72.7 &&
                          run.PageIndex == theirs.PageIndex &&
                          Math.Abs(run.BaselineY - theirs.BaselineY) < 40)
            .OrderBy(run => run.BaselineY)
            .ToList();

        using var stream = new MemoryStream(Fixtures.Build("math-bracket-probe"));
        var laidOut = Converter.LayoutDocument(stream,
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var letter = laidOut.Pages[^1].Lines
            .SelectMany(line => line.Texts)
            .Where(text => text.Format.FontSizePoints > 70)
            .OrderBy(text => text.BaselineY)
            .Last();

        var pieces = laidOut.Pages[^1].Lines
            .SelectMany(line => line.Texts)
            .Where(text => text.Glyph is not null && text.X > 72.3 && text.X < 72.7 &&
                           Math.Abs(text.BaselineY - letter.BaselineY) < 40)
            .OrderBy(text => text.BaselineY)
            .ToList();

        Assert.Equal(3, wordPieces.Count);
        Assert.Equal(3, pieces.Count);

        // Stacked in the same places, to within the three hundredth of an inch Word rounds to.
        for (var i = 0; i < 3; i++)
        {
            var mine = pieces[i].BaselineY - letter.BaselineY;
            var word3 = wordPieces[i].BaselineY - theirs.BaselineY;

            output.WriteLine($"piece {i}: {mine:0.###} against {word3:0.###}");
            Assert.InRange(mine - word3, -0.3, 0.3);
        }

        // One text between the three of them.
        Assert.Equal(1, pieces.Count(text => text.Text.Trim().Length > 0));

    }

    /// <summary>
    /// The shape that was chosen is the shape that reaches the page.
    /// </summary>
    /// <remarks>
    /// An equation is laid out as a page of its own and copied onto the real one, and what is
    /// copied has to include which shape each piece asked for: a bracket grown to fit is a glyph
    /// no character stands for, so a copy that keeps only the text draws the plain bracket
    /// instead. It did, until this was written — every grown bracket and every stretched radical
    /// in the fixtures was drawn as its plain shape, at the right size and in the right place, so
    /// nothing that measured position noticed.
    /// </remarks>
    [Fact]
    public void A_grown_bracket_reaches_the_page_as_the_shape_it_asked_for()
    {
        using var stream = new MemoryStream(Fixtures.Build("math-bracket-probe"));
        var laidOut = Converter.LayoutDocument(stream,
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var brackets = laidOut.Pages
            .SelectMany(page => page.Lines)
            .SelectMany(line => line.Texts)
            .Where(text => text.Text is "(" or ")")
            .ToList();

        // Two to a probe, on both pages.
        Assert.Equal(44, brackets.Count);

        // Every one of them names the shape it is drawn as, on the page rather than in the page
        // of its own the equation was set in.
        Assert.All(brackets, text => Assert.NotNull(text.Glyph));

        // And they are not all the same shape: the fixture walks a bracket up the whole series.
        Assert.True(brackets.Select(text => text.Glyph).Distinct().Count() >= 8,
            "the brackets of the probe are all the same shape");

        output.WriteLine($"{brackets.Count} brackets, " +
                         $"{brackets.Select(text => text.Glyph).Distinct().Count()} shapes between them");
    }

    [Fact]
    public void The_face_says_how_to_build_one()
    {
        var font = TestFonts.CreatePinnedLibrary().Resolve("Cambria Math").Font;

        var assembly = font.MathAssemblies[font.GetGlyphIndex('(')];

        Assert.Equal(200, assembly.MinimumOverlap);
        Assert.Equal(3, assembly.Parts.Count);

        // A foot, a middle that may be repeated, a head — listed from the bottom up.
        Assert.False(assembly.Parts[0].Extender);
        Assert.True(assembly.Parts[1].Extender);
        Assert.False(assembly.Parts[2].Extender);

        Assert.Equal(4733, assembly.Parts[0].FullAdvance);
        Assert.Equal(2501, assembly.Parts[1].FullAdvance);

        // How much of each the piece above and below may cover.
        Assert.Equal(300, assembly.Parts[0].EndConnector);
        Assert.Equal(2500, assembly.Parts[1].StartConnector);
    }

    /// <summary>How wide each probe's opening bracket is: where what it holds begins, less
    /// where the bracket begins.</summary>
    private static List<double> Brackets(IReadOnlyList<ExtractedTextRun> runs)
    {
        var anchors = runs.Where(run => run.Text.Trim() == "." && run.X < 72.45)
            .Select(run => run.PageIndex * 2000.0 + run.BaselineY)
            .Distinct().Order().ToList();

        var rooms = new List<double>();

        foreach (var anchor in anchors)
        {
            // The equation on this line: its opening bracket at the margin, and what it holds.
            var line = runs.Where(run => run.FontSize > 3 && run.X > 72.4 &&
                                         Math.Abs(run.PageIndex * 2000.0 + run.BaselineY - anchor) < 12)
                .OrderBy(run => run.X).ToList();

            if (line.Count < 2) continue;

            // The opening bracket, and the first thing it holds: the room between the two is the
            // width of the shape that was picked, which is what tells the eight apart.
            rooms.Add(line.First(run => run.X > line[0].X + 0.5).X - line[0].X);
        }

        return rooms;
    }
}
