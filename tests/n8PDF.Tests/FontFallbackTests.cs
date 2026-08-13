using n8PDF;
using n8PDF.Fonts;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests what happens when a run asks its font for a character the font has not got.
/// </summary>
/// <remarks>
/// Most fonts hold very few of the characters there are. Arial Hebrew has no Latin letters at all;
/// Times New Roman has no Japanese. A document that names one font for a paragraph of two scripts
/// is not unusual — it is what a document written in Hebrew with an English name in it looks like
/// — and a converter that draws only what the named font holds loses text the document plainly
/// has, silently and without failing.
/// </remarks>
public class FontFallbackTests(ITestOutputHelper output)
{
    private const string Hebrew = "שלום";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    /// <summary>The pieces of text on the first page, with the face each is drawn in.</summary>
    private static List<(string Text, string Family)> Drawn(string family, string text)
    {
        var run = $"<w:rFonts w:ascii=\"{family}\" w:hAnsi=\"{family}\" w:cs=\"{family}\"/><w:sz w:val=\"24\"/>";

        var builder = new DocxBuilder().AddRawParagraph(
            $"<w:p><w:r><w:rPr>{run}</w:rPr><w:t xml:space=\"preserve\">{text}</w:t></w:r></w:p>");

        using var stream = builder.BuildStream();

        return Converter.LayoutDocument(stream, Options()).Pages[0].Lines
            .SelectMany(line => line.Texts)
            .OrderBy(piece => piece.X)
            .Select(piece => (piece.Text, piece.Font.Font.FamilyName))
            .ToList();
    }

    /// <summary>
    /// A face that can draw the whole run draws the whole run: nothing is borrowed where nothing
    /// needs to be.
    /// </summary>
    [Fact]
    public void A_face_that_can_draw_it_all_draws_it_all()
    {
        var drawn = Drawn("Times New Roman", $"Latin and {Hebrew} together");

        Assert.All(drawn, piece => Assert.Equal("Times New Roman", piece.Family));

        // Broken where the direction changes and nowhere else: the Latin either side of the
        // Hebrew is whole rather than one piece to a word or a letter.
        Assert.Equal(3, drawn.Count);
        Assert.Contains(drawn, piece => piece.Text.Contains("Latin and"));
        Assert.Contains(drawn, piece => piece.Text.Contains("together"));
    }

    /// <summary>
    /// A character its own face cannot draw is drawn in one that can, and the rest of the run is
    /// left where it was.
    /// </summary>
    [Fact]
    public void A_character_its_face_cannot_draw_is_borrowed_from_another()
    {
        var drawn = Drawn("Arial Hebrew", $"Latin {Hebrew}");

        foreach (var (text, family) in drawn) output.WriteLine($"  \"{text}\" in {family}");

        // The Latin is somewhere, and not in Arial Hebrew, which has none of it.
        var latin = drawn.Single(piece => piece.Text.Contains("Latin"));

        Assert.NotEqual("Arial Hebrew", latin.Family);

        // The Hebrew is still in the face the document asked for.
        var hebrew = drawn.Single(piece => piece.Text.Contains(Hebrew[0]));

        Assert.Equal("Arial Hebrew", hebrew.Family);
    }

    /// <summary>
    /// A borrowed face is borrowed for the whole of what it is needed for: a word set in one does
    /// not come apart into letters.
    /// </summary>
    [Fact]
    public void A_word_drawn_in_a_borrowed_face_stays_one_word()
    {
        var drawn = Drawn("Arial Hebrew", "Borrowed");

        Assert.Single(drawn);
        Assert.Equal("Borrowed", drawn[0].Text);
        Assert.NotEqual("Arial Hebrew", drawn[0].Family);
    }

    /// <summary>
    /// Where nothing available can draw a character, the run keeps its own face rather than the
    /// conversion failing. The document is short of a glyph either way; it is not short of a page.
    /// </summary>
    [Fact]
    public void A_character_nothing_can_draw_leaves_the_run_as_it_was()
    {
        // None of the pinned faces has Syriac.
        var drawn = Drawn("Times New Roman", "Syriac: \u0710\u0712\u0713");

        Assert.All(drawn, piece => Assert.Equal("Times New Roman", piece.Family));
    }

    /// <summary>
    /// The library answers the question directly too, and prefers what it was given: a face that
    /// can draw the character is never swapped for another.
    /// </summary>
    [Fact]
    public void The_library_keeps_a_face_that_can_draw_the_character()
    {
        var library = TestFonts.CreatePinnedLibrary();
        var times = library.Resolve("Times New Roman");

        Assert.Same(times, library.ResolveForCharacter('A', times, false, false));
        Assert.Same(times, library.ResolveForCharacter(Hebrew[0], times, false, false));

        var hebrewOnly = library.Resolve("Arial Hebrew");

        Assert.Same(hebrewOnly, library.ResolveForCharacter(Hebrew[0], hebrewOnly, false, false));
        Assert.NotSame(hebrewOnly, library.ResolveForCharacter('A', hebrewOnly, false, false));

        // Nothing pinned has Syriac, and saying so is better than pretending otherwise.
        Assert.Null(library.ResolveForCharacter(0x0710, times, false, false));
    }

    /// <summary>
    /// And the fixture, whose lines Word draws too: every line begins where Word begins it, and
    /// the text a font could not draw is on the page rather than missing from it.
    /// </summary>
    [Fact]
    public void The_fixture_keeps_the_text_its_fonts_could_not_draw()
    {
        var pdf = Converter.Convert(Fixtures.Build("font-fallback"), Options());
        var text = string.Concat(Support.PdfReading.PdfTextExtractor.Extract(pdf).Select(r => r.Text));

        // The Latin of the line whose face has no Latin.
        Assert.Contains("Arial Hebrew, which has no Latin at all", text);
        Assert.Contains("Numbers 8601 and punctuation", text);
    }
}
