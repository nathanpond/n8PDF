using n8PDF.Fonts;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the step that turns text into the glyphs that draw it.
/// </summary>
/// <remarks>
/// What it does today is what the Latin, Greek and Cyrillic scripts need and no more: a character
/// takes the glyph the font's character map gives it, and a pair is drawn closer together where
/// the face says so. What these tests are really about is the shape of the thing rather than its
/// cleverness — that a glyph carries its own advance and knows which character it came from, and
/// that measuring a run and drawing it are the same walk over the same glyphs. A script that joins
/// its letters or reorders them needs all three of those before it needs anything else.
/// </remarks>
public class ShapingTests(ITestOutputHelper output)
{
    private static TrueTypeFont Times() => TestFonts.Load(TestFonts.TimesNewRomanPath);

    [Fact]
    public void A_character_becomes_a_glyph_that_knows_where_it_came_from()
    {
        var font = Times();
        var shaped = TextShaper.Shape(font, "Ab");

        Assert.Equal(2, shaped.Count);

        // Each glyph is the one the font's character map gives, and each says which character of
        // the text it stands for.
        Assert.Equal(font.GetGlyphIndex('A'), shaped.Glyphs[0].Glyph);
        Assert.Equal(font.GetGlyphIndex('b'), shaped.Glyphs[1].Glyph);

        Assert.Equal(0, shaped.Glyphs[0].Cluster);
        Assert.Equal(1, shaped.Glyphs[1].Cluster);

        Assert.Equal('A', shaped.CodePointOf(0));
        Assert.Equal('b', shaped.CodePointOf(1));
    }

    /// <summary>
    /// A character outside the basic multilingual plane is written as two, and is one glyph rather
    /// than two broken ones — and the glyph names the whole character rather than half of it.
    /// </summary>
    [Fact]
    public void A_character_written_as_two_is_one_glyph()
    {
        const string beyondBmp = "\U0001D400";
        Assert.Equal(2, beyondBmp.Length);

        var shaped = TextShaper.Shape(TestFonts.Load(TestFonts.ArialPath), beyondBmp);

        Assert.Single(shaped.Glyphs);
        Assert.Equal(0x1D400, shaped.CodePointOf(0));
    }

    /// <summary>
    /// A glyph carries its own advance, so kerning is a property of the glyph on the left of a
    /// pair rather than a gap between two characters. That is what lets it survive the journey to
    /// the page: what is written there is a glyph and how far the pen moves after it.
    /// </summary>
    [Fact]
    public void Kerning_shortens_the_advance_of_the_glyph_it_belongs_to()
    {
        var font = Times();

        // A pair Times kerns, and the same pair with nothing between them to kern against.
        var plain = TextShaper.Shape(font, "AV");
        var kerned = TextShaper.Shape(font, "AV", applyKerning: true);

        var expected = font.GetKerning(font.GetGlyphIndex('A'), font.GetGlyphIndex('V'));

        output.WriteLine($"the face kerns AV by {expected} units");

        Assert.True(expected < 0, "Times New Roman does not kern AV, so this proves nothing");

        // The pair is tighter by exactly what the face says, and it is the first glyph that gives.
        Assert.Equal(plain.AdvanceUnits + expected, kerned.AdvanceUnits);
        Assert.Equal(plain.Glyphs[0].Advance + expected, kerned.Glyphs[0].Advance);
        Assert.Equal(plain.Glyphs[1].Advance, kerned.Glyphs[1].Advance);
    }

    /// <summary>
    /// Measuring a run is reading the advances of its glyphs, so a width and the glyphs written
    /// for it cannot part company: they are the same walk.
    /// </summary>
    [Theory]
    [InlineData("Hamburgefonstiv", false)]
    [InlineData("Hamburgefonstiv", true)]
    [InlineData("AV Wa To", true)]
    public void A_runs_width_is_the_sum_of_its_glyphs_advances(string text, bool kerned)
    {
        var font = Times();
        var shaped = TextShaper.Shape(font, text, kerned);

        var summed = font.Metrics.ToPoints(shaped.AdvanceUnits, 12);
        var measured = TextMeasurer.Measure(font, text, 12, applyKerning: kerned);

        Assert.Equal(summed, measured, 6);
    }

    /// <summary>
    /// Character spacing is counted per glyph. It is the one measurement that will change meaning
    /// when a script arrives whose glyphs and characters do not correspond — Word spaces what it
    /// draws — and it is written this way now so that it will not need finding again then.
    /// </summary>
    [Fact]
    public void Character_spacing_is_counted_once_for_each_glyph()
    {
        var font = Times();

        var plain = TextMeasurer.Measure(font, "abcd", 12);
        var spaced = TextMeasurer.Measure(font, "abcd", 12, characterSpacingPoints: 2);

        Assert.Equal(plain + 8, spaced, 6);
    }

    [Fact]
    public void Nothing_shapes_to_nothing()
    {
        var shaped = TextShaper.Shape(Times(), string.Empty);

        Assert.Empty(shaped.Glyphs);
        Assert.Equal(0, shaped.AdvanceUnits);
        Assert.Equal(0, TextMeasurer.Measure(Times(), string.Empty, 12));
    }
}
