using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A character named by its code in a face of its own: <c>w:sym</c>.
/// </summary>
/// <remarks>
/// How Word writes a tick, an arrow, or anything else from the symbol faces. The face belongs to
/// the character rather than to the run — a run may carry text in one face and end with a
/// character from another — and the code is written in the private-use block those faces keep
/// their glyphs in, which Word's own export strips back off.
/// </remarks>
public class SymbolTests(ITestOutputHelper output)
{
    /// <summary>
    /// The code with the private-use block on it and the same code without: Word draws the same
    /// character for both, and so does this.
    /// </summary>
    [Theory]
    [InlineData("F0FC", 'ü')]
    [InlineData("00FC", 'ü')]
    [InlineData("F0E0", 'à')]
    [InlineData("f0e0", 'à')]
    public void A_symbol_is_the_character_its_code_names(string code, char expected)
    {
        var run = ParseRun($"<w:sym w:font=\"Wingdings\" w:char=\"{code}\"/>");
        var symbol = Assert.IsType<SymbolInline>(Assert.Single(run.Content));

        Assert.Equal(expected.ToString(), symbol.Text);
        Assert.Equal("Wingdings", symbol.Font);
    }

    /// <summary>A symbol naming no face is set in the run's own.</summary>
    [Fact]
    public void A_symbol_naming_no_face_takes_the_run_s()
    {
        var run = ParseRun("<w:sym w:char=\"F0FC\"/>");

        Assert.Null(Assert.IsType<SymbolInline>(Assert.Single(run.Content)).Font);
    }

    /// <summary>And nonsense is left out rather than drawn as something else.</summary>
    [Theory]
    [InlineData("<w:sym w:font=\"Wingdings\"/>")]
    [InlineData("<w:sym w:font=\"Wingdings\" w:char=\"\"/>")]
    [InlineData("<w:sym w:font=\"Wingdings\" w:char=\"not a number\"/>")]
    public void A_symbol_that_names_no_character_is_not_drawn(string markup) =>
        Assert.Empty(ParseRun(markup).Content);

    /// <summary>
    /// And on the page: every symbol of the fixture, in the face Word set it in, at the width
    /// Word gave it.
    /// </summary>
    [Theory]
    [InlineData("Wingdings arrow", "Wingdings", 'à')]
    [InlineData("Wingdings tick", "Wingdings", 'ü')]
    [InlineData("Symbol pi", "Symbol", 'p')]
    [InlineData("Webdings globe", "Webdings", 'W')]
    [InlineData("Unprefixed", "Wingdings", 'ü')]
    public void A_symbol_reaches_the_page_in_its_own_face(string label, string face, char character)
    {
        if (TestFonts.SkipForMissingFonts("symbols")) return;

        var ours = PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build("symbols"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));

        var word = PdfTextExtractor.ExtractFile(Path.Combine(TestPaths.ReferencePdfs, "symbols.pdf"));

        var line = ours.Where(r => r.Text.Contains(label, StringComparison.Ordinal))
            .Select(r => r.BaselineY).First();

        // By the face rather than by the character: the label says "Symbol pi", and a search for
        // a p would find the label as readily as the symbol.
        var mine = ours.Single(r => Math.Abs(r.BaselineY - line) < 0.01 &&
                                    r.FontFamily.StartsWith(face.Replace(" ", ""), StringComparison.OrdinalIgnoreCase));

        Assert.Equal(character.ToString(), mine.Text);
        var theirs = word.Where(r => r.Text.Trim() == character.ToString() &&
                                     r.FontFamily.StartsWith(face.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
            .ToList();

        output.WriteLine($"{label}: ours {mine.FontFamily} w{mine.Width:0.###}, " +
                         $"word {string.Join(", ", theirs.Select(t => $"{t.FontFamily} w{t.Width:0.###}"))}");

        Assert.StartsWith(face.Replace(" ", ""), mine.FontFamily, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(theirs, t => Math.Abs(t.Width - mine.Width) < 0.05);
    }

    private static Run ParseRun(string inner)
    {
        var docx = new DocxBuilder()
            .AddRawParagraph($"<w:p><w:r><w:rPr><w:sz w:val=\"24\"/></w:rPr>{inner}</w:r></w:p>")
            .Build();

        using var package = Packaging.OpcPackage.Open(new MemoryStream(docx));
        var document = DocumentParser.Parse(package.ReadPartAsXml(package.GetMainDocumentPartName()));

        return document.Body.OfType<Paragraph>().SelectMany(p => p.Runs).First();
    }
}
