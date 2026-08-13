using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Text;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests Thai, Lao, Khmer and Myanmar.
/// </summary>
/// <remarks>
/// Four scripts of two kinds. Thai and Lao stack vowels and tone marks above and below their
/// consonants but store every character in the order it is drawn, so what they need is the marks
/// put where the font says and nothing else. Khmer and Myanmar descend from the same writing as
/// the Indic scripts and reorder like them: a consonant written under the one before it, an r
/// written before the cluster it belongs to, a vowel written to the left of the consonant it
/// follows.
///
/// Compared against HarfBuzz for the glyphs, and against Word for where they land.
/// </remarks>
public class SoutheastAsianTests(ITestOutputHelper output)
{
    private const string Supplemental = "/System/Library/Fonts/Supplemental/";

    public static TheoryData<string, string> Words => new()
    {
        { "สวัสดี", Supplemental + "Ayuthaya.ttf" },                 // sawatdi
        { "ภาษาไทย", Supplemental + "Ayuthaya.ttf" },                // the Thai language
        { "กรุงเทพมหานคร", Supplemental + "Ayuthaya.ttf" },           // Bangkok, at length

        { "ກະລຸນາ", Supplemental + "Lao Sangam MN.ttf" },             // please, in Lao
        { "ພາສາລາວ", Supplemental + "Lao Sangam MN.ttf" },           // the Lao language

        { "ភាសាខ្មែរ", Supplemental + "Khmer Sangam MN.ttf" },        // the Khmer language
        { "ខ្ញុំ", Supplemental + "Khmer Sangam MN.ttf" },               // I, with a subscript consonant
        { "ព្រះរាជាណាចក្រ", Supplemental + "Khmer Sangam MN.ttf" },    // kingdom: an r under a consonant
        { "មនុស្ស", Supplemental + "Khmer Sangam MN.ttf" },            // person

        { "မြန်မာ", "/System/Library/Fonts/NotoSansMyanmar.ttc" },   // myanmar: a medial r
        { "ကျွန်တော်", "/System/Library/Fonts/NotoSansMyanmar.ttc" }, // I, with two medials
        { "ဗမာစာ", "/System/Library/Fonts/NotoSansMyanmar.ttc" }     // the Burmese language
    };

    [Theory]
    [MemberData(nameof(Words))]
    public void The_glyphs_are_the_glyphs_harfbuzz_chooses(string word, string face)
    {
        var path = OpenTypeOnly.Copy(face);

        if (HarfBuzz.Shape(path, word) is not { } theirs)
        {
            output.WriteLine("hb-shape was not found, so the shaping was not compared.");
            return;
        }

        var font = TrueTypeFont.Load(File.ReadAllBytes(path));
        var ours = HarfBuzz.Describe(TextShaper.Shape(font, word));

        output.WriteLine($"{word}\n  ours {string.Join(" ", ours)}\n  them {string.Join(" ", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>
    /// Khmer moves an r written under a consonant to the front of the whole cluster, which is
    /// where it is drawn.
    /// </summary>
    [Fact]
    public void A_khmer_r_written_below_is_drawn_before_the_cluster()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy(Supplemental + "Khmer Sangam MN.ttf")));

        // pa, coeng, ro: the r is stored last and drawn first.
        var shaped = TextShaper.Shape(font, "ព្រ");

        output.WriteLine(string.Join(" ", Enumerable.Range(0, shaped.Count)
            .Select(at => $"{shaped.Glyphs[at].Glyph}/{shaped.TextOf(at)}")));

        Assert.True(shaped.Count >= 2);

        // What is drawn first came from the end of the text rather than the start of it.
        Assert.True(shaped.Glyphs[0].Cluster > shaped.Glyphs[^1].Cluster,
            "the r was not moved to the front of the cluster");
    }

    /// <summary>
    /// Myanmar draws a medial r before the consonant it is written on, though it is stored after
    /// it.
    /// </summary>
    [Fact]
    public void A_myanmar_medial_r_is_drawn_before_its_consonant()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy("/System/Library/Fonts/NotoSansMyanmar.ttc")));

        var shaped = TextShaper.Shape(font, "မြ");   // ma, then the medial ra

        output.WriteLine(string.Join(" ", Enumerable.Range(0, shaped.Count)
            .Select(at => $"{shaped.Glyphs[at].Glyph}/{shaped.Glyphs[at].Cluster}")));

        Assert.Equal(2, shaped.Count);
        Assert.Equal(1, shaped.Glyphs[0].Cluster);
        Assert.Equal(0, shaped.Glyphs[1].Cluster);
    }

    /// <summary>
    /// Thai stores everything in the order it is drawn, so nothing is reordered — and its vowels
    /// and tone marks are still put where the font says rather than where the pen is.
    /// </summary>
    [Fact]
    public void Thai_is_not_reordered_and_its_marks_are_placed()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy(Supplemental + "Ayuthaya.ttf")));

        var shaped = TextShaper.Shape(font, "สวัสดี");

        // Every character comes out in the order it went in.
        Assert.Equal(
            Enumerable.Range(0, 6),
            shaped.Glyphs.Select(glyph => glyph.Cluster));

        // The vowel above and the one after it advance the pen by nothing: they are drawn on the
        // letters beside them.
        var marks = shaped.Glyphs.Count(glyph => glyph.Advance == 0);

        output.WriteLine($"{shaped.Count} glyphs, {marks} of them drawn on a letter");

        Assert.Equal(2, marks);
    }

    /// <summary>
    /// Khmer's coeng and Myanmar's asat come from the character database like everything else.
    /// </summary>
    [Fact]
    public void The_categories_come_from_the_character_database()
    {
        Assert.Equal(IndicCategory.Halant, IndicSyllables.CategoryOf('្'));      // Khmer coeng
        Assert.Equal(IndicCategory.Ra, IndicSyllables.CategoryOf('រ'));          // Khmer ro
        Assert.Equal(IndicCategory.VowelPre, IndicSyllables.CategoryOf('េ'));     // a Khmer left vowel

        Assert.Equal(IndicCategory.Asat, IndicSyllables.CategoryOf('်'));        // Myanmar asat
        Assert.Equal(IndicCategory.MedialRa, IndicSyllables.CategoryOf('ြ'));    // Myanmar medial ra
    }

    /// <summary>
    /// The whole of it against Word: a line of each script, holding the vowels that stack, the
    /// consonants that go underneath, and the two kinds of letter that are drawn before what they
    /// are stored after.
    /// </summary>
    [Fact]
    public void The_fixture_lines_go_where_word_puts_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "southeast-asian.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("southeast-asian",
            Converter.Convert(Fixtures.Build("southeast-asian"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));

        output.WriteLine(report.ToText());

        Assert.Equal(0, report.UnmatchedCount);

        Assert.True(report.MaxAbsStartXDelta < 0.1,
            $"a line begins {report.MaxAbsStartXDelta:0.###}pt from where Word begins it");

        Assert.True(report.MaxAbsWidthDelta < 0.5,
            $"a line is {report.MaxAbsWidthDelta:0.###}pt wider or narrower than Word's");
    }
}
