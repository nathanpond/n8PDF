using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Decomposed Hangul jamo are <b>not</b> composed into syllables — because Word is not composing
/// them either, and Word's export is what this library is measured against (#70).
/// </summary>
/// <remarks>
/// The issue assumed composition is what Word does. Measured, it is not: Word for Mac sets the
/// decomposed spelling of 한글 한글아 as fourteen separate full-width jamo in Malgun Gothic —
/// 229.7pt of line at 16pt type against the 85.7pt of the precomposed spelling, every glyph from
/// the requested face's own cmap, no fallback involved. Composition was implemented here and
/// then reverted when the fixture showed it diverging from the reference renderer by 144pt of
/// line width. The fixture pins Word's actual behaviour; if a future export round ever fails the
/// generic comparison on it with our line the narrower, Word has started composing — put the
/// composition back then (the arithmetic is in this issue's history) and delete this remark.
/// </remarks>
public class HangulTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string Malgun =
        "/Applications/Microsoft Word.app/Contents/Resources/DFonts/malgun.ttf";

    /// <summary>Each jamo keeps its own full-width glyph, exactly as Word draws them.</summary>
    [Fact]
    public void Decomposed_jamo_stay_decomposed_at_full_width()
    {
        if (!File.Exists(Malgun))
        {
            Assert.False(TestFonts.OfficeFontsRequired, "Malgun Gothic is required and missing");
            return;
        }

        var font = TrueTypeFont.Load(File.ReadAllBytes(Malgun));
        var shaped = TextShaper.Shape(font, "한글아");

        _output.WriteLine($"{shaped.Glyphs.Count} glyphs, advances {string.Join(",", shaped.Glyphs.Select(g => g.Advance))}");

        Assert.Equal(8, shaped.Glyphs.Count);
        Assert.All(shaped.Glyphs, glyph => Assert.Equal(2048, glyph.Advance));
    }

    /// <summary>
    /// On the page, the decomposed spelling is fourteen full-width jamo and the precomposed five
    /// syllables — the two widths Word's own export draws for the same words.
    /// </summary>
    [Fact]
    public void The_two_spellings_differ_exactly_as_words_do()
    {
        if (TestFonts.SkipForMissingFonts("hangul-jamo-probe")) return;

        var pdf = n8PDF.Converter.Convert(Fixtures.Build("hangul-jamo-probe"),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var runs = PdfTextExtractor.Extract(pdf)
            .Where(r => !r.Text.StartsWith("A control", StringComparison.Ordinal))
            .OrderBy(r => r.BaselineY)
            .ToList();

        var decomposed = runs.Where(r => r.BaselineY < runs[^1].BaselineY).Sum(r => r.Width - r.TrailingWhitespaceWidth);
        _output.WriteLine(string.Join(" | ", runs.Select(r => $"{r.Width:0.00}pt @{r.BaselineY:0.00}")));

        // 14 jamo at 16pt plus the space against 5 syllables plus the space, as Word draws them.
        var first = runs.First();
        var last = runs.Last();

        Assert.InRange(first.Width, 226, 233);
        Assert.InRange(last.Width, 83, 89);
    }
}
