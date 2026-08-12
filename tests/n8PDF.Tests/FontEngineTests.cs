using n8PDF.Fonts;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tier 2 tests for the SFNT parser, checked against the well-known metrics of the fonts Word
/// documents actually use.
/// </summary>
public class FontEngineTests
{
    [Fact]
    public void Times_new_roman_reports_its_documented_metrics()
    {
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);

        Assert.Equal("Times New Roman", font.FamilyName);
        Assert.Equal(2048, font.UnitsPerEm);
        Assert.False(font.IsBold);
        Assert.False(font.IsItalic);

        // Monotype's Times New Roman has used these values since the original TrueType release.
        Assert.Equal(1825, font.Metrics.Ascender);
        Assert.Equal(-443, font.Metrics.Descender);
        Assert.Equal(400, font.Metrics.WeightClass);
        Assert.Equal(0, font.Metrics.ItalicAngle);
    }

    [Fact]
    public void Advance_widths_match_the_known_design_units()
    {
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);

        // Times New Roman has a 2048-unit em. These widths are checkable against the published
        // Times Roman metrics, which are quoted per 1000 units: space is 250 (a quarter em,
        // 512 here), 'M' is 889 (1821 here), and the em dash is a full em by definition.
        Assert.Equal(512, font.GetAdvanceWidth(font.GetGlyphIndex(' ')));
        Assert.Equal(1821, font.GetAdvanceWidth(font.GetGlyphIndex('M')));
        Assert.Equal(2048, font.GetAdvanceWidth(font.GetGlyphIndex('—')));

        // Digits are tabular in this face, so they must all share a width.
        var zeroWidth = font.GetAdvanceWidth(font.GetGlyphIndex('0'));
        for (var digit = '1'; digit <= '9'; digit++)
            Assert.Equal(zeroWidth, font.GetAdvanceWidth(font.GetGlyphIndex(digit)));
    }

    [Fact]
    public void Measuring_a_string_converts_design_units_to_points()
    {
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);

        var units = 0;
        foreach (var ch in "Hello")
            units += font.GetAdvanceWidth(font.GetGlyphIndex(ch));

        var points = font.Metrics.ToPoints(units, 12);

        // "Hello" at 12pt Times is a shade under 27pt. The tolerance here is wide enough to
        // survive a font revision but tight enough to catch a unit-conversion error.
        Assert.InRange(points, 26.0, 28.0);
    }

    [Fact]
    public void Cmap_maps_ascii_and_beyond_the_bmp_where_supported()
    {
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);

        Assert.NotEqual(0, font.GetGlyphIndex('A'));
        Assert.NotEqual(0, font.GetGlyphIndex('z'));
        Assert.NotEqual(0, font.GetGlyphIndex('é'));
        Assert.NotEqual(0, font.GetGlyphIndex('—')); // em dash
        Assert.NotEqual(0, font.GetGlyphIndex('“'));

        // An unassigned code point must resolve to .notdef rather than to a wrong glyph.
        Assert.Equal(0, font.GetGlyphIndex(0x10fffd));
    }

    [Fact]
    public void Distinct_characters_get_distinct_glyphs()
    {
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);

        // A cmap segment parsed with an off-by-one lands every character on one glyph, which
        // still "works" until you look at the page.
        var glyphs = new HashSet<ushort>();
        foreach (var ch in "abcdefghijklmnopqrstuvwxyz")
            glyphs.Add(font.GetGlyphIndex(ch));

        Assert.Equal(26, glyphs.Count);
    }

    [Fact]
    public void Bold_and_italic_faces_identify_themselves()
    {
        var bold = TestFonts.Load(TestFonts.TimesNewRomanBoldPath);
        Assert.True(bold.IsBold);
        Assert.False(bold.IsItalic);
        Assert.True(bold.Metrics.WeightClass >= 600);

        var italic = TestFonts.Load(TestFonts.TimesNewRomanItalicPath);
        Assert.True(italic.IsItalic);
        Assert.True(italic.Metrics.ItalicAngle < 0);
    }

    [Fact]
    public void Bold_is_wider_than_regular()
    {
        var regular = TestFonts.Load(TestFonts.TimesNewRomanPath);
        var bold = TestFonts.Load(TestFonts.TimesNewRomanBoldPath);

        var regularWidth = Measure(regular, "The quick brown fox", 12);
        var boldWidth = Measure(bold, "The quick brown fox", 12);

        Assert.True(boldWidth > regularWidth, $"bold {boldWidth} should exceed regular {regularWidth}");
    }

    [Fact]
    public void Font_collections_expose_every_face()
    {
        if (!TestFonts.Exists(TestFonts.HelveticaCollectionPath)) return;

        var data = File.ReadAllBytes(TestFonts.HelveticaCollectionPath);
        var faceCount = TrueTypeFont.GetFaceCount(data);

        Assert.True(faceCount > 1, "Helvetica.ttc is expected to hold several faces");

        var faces = TrueTypeFont.LoadAll(data);
        Assert.NotEmpty(faces);
        Assert.All(faces, face => Assert.False(string.IsNullOrWhiteSpace(face.FamilyName)));
    }

    [Fact]
    public void Repackaged_font_program_is_a_valid_standalone_sfnt()
    {
        var font = TestFonts.Load(TestFonts.TimesNewRomanPath);
        var program = font.GetEmbeddableFontProgram();

        // The repackaged bytes must parse back through our own reader and measure identically,
        // since this is exactly what gets embedded in the PDF.
        var reloaded = TrueTypeFont.Load(program);

        Assert.Equal(font.FamilyName, reloaded.FamilyName);
        Assert.Equal(font.UnitsPerEm, reloaded.UnitsPerEm);
        Assert.Equal(font.GlyphCount, reloaded.GlyphCount);
        Assert.Equal(
            font.GetAdvanceWidth(font.GetGlyphIndex('M')),
            reloaded.GetAdvanceWidth(reloaded.GetGlyphIndex('M')));
        Assert.True(reloaded.HasTable("glyf"));
        Assert.True(reloaded.HasTable("loca"));
    }

    [Fact]
    public void Collection_faces_repackage_into_embeddable_programs()
    {
        if (!TestFonts.Exists(TestFonts.HelveticaCollectionPath)) return;

        // This is the case that makes repackaging necessary: a face inside a .ttc cannot be
        // embedded as-is, because its table directory points into a shared file.
        var faces = TrueTypeFont.LoadAll(File.ReadAllBytes(TestFonts.HelveticaCollectionPath));
        var face = faces[0];

        var reloaded = TrueTypeFont.Load(face.GetEmbeddableFontProgram());

        Assert.Equal(face.FamilyName, reloaded.FamilyName);
        Assert.Equal(face.GetAdvanceWidth(face.GetGlyphIndex('A')),
            reloaded.GetAdvanceWidth(reloaded.GetGlyphIndex('A')));
    }

    [Fact]
    public void Library_resolves_registered_families_and_styles()
    {
        var library = TestFonts.CreatePinnedLibrary();

        var regular = library.Resolve("Times New Roman");
        Assert.Equal("Times New Roman", regular.Font.FamilyName);
        Assert.True(regular.IsExact);

        var bold = library.Resolve("Times New Roman", bold: true);
        Assert.True(bold.Font.IsBold);
        Assert.True(bold.IsExact);

        var italic = library.Resolve("Times New Roman", bold: false, italic: true);
        Assert.True(italic.Font.IsItalic);
        Assert.True(italic.IsExact);
    }

    [Fact]
    public void Library_flags_synthesis_when_a_face_is_missing()
    {
        var library = new FontLibrary { UseSystemFonts = false };
        library.RegisterFile(TestFonts.TimesNewRomanPath);

        // Only the regular face is registered, so bold italic has to be faked in both axes.
        var selection = library.Resolve("Times New Roman", bold: true, italic: true);

        Assert.True(selection.SyntheticBold);
        Assert.True(selection.SyntheticItalic);
        Assert.False(selection.IsExact);
    }

    [Fact]
    public void Library_substitutes_rather_than_failing_on_an_unknown_family()
    {
        var library = TestFonts.CreatePinnedLibrary();

        // Word substitutes silently for fonts it does not have; failing the conversion outright
        // would be worse than rendering with a stand-in.
        Assert.True(library.TryResolve("Definitely Not Installed MMXXVI", false, false, out var selection));
        Assert.NotNull(selection.Font);
    }

    [Fact]
    public void Library_with_nothing_registered_reports_failure()
    {
        var library = new FontLibrary { UseSystemFonts = false };

        Assert.False(library.TryResolve("Times New Roman", false, false, out _));
        Assert.Throws<FontFormatException>(() => library.Resolve("Times New Roman"));
    }

    [Fact]
    public void Malformed_data_raises_a_font_format_exception()
    {
        Assert.Throws<FontFormatException>(() => TrueTypeFont.Load(new byte[64]));
        Assert.Throws<FontFormatException>(() => TrueTypeFont.Load("not a font at all"u8.ToArray()));
    }

    private static double Measure(TrueTypeFont font, string text, double sizePoints)
    {
        var units = 0;
        foreach (var ch in text)
            units += font.GetAdvanceWidth(font.GetGlyphIndex(ch));

        return font.Metrics.ToPoints(units, sizePoints);
    }
}
