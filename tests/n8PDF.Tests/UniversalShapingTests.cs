using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Text;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the shaping engine that belongs to no script in particular.
/// </summary>
/// <remarks>
/// Most of the writing systems descended from Brahmi are not given rules of their own: there are
/// too many of them, they are alike enough, and what differs between them is what the font already
/// describes. One set of rules is applied to all, working from what each character *is* — something
/// to build on, a vowel drawn above, a consonant drawn below, a mark on a mark — rather than from
/// which script it belongs to.
///
/// Which is why the list below is the test. Forty-nine scripts, none of which has a line of code to
/// itself, every one of them compared against HarfBuzz glyph for glyph and offset for offset. A
/// rule that is nearly right passes for one script and fails for the next.
/// </remarks>
public class UniversalShapingTests(ITestOutputHelper output)
{
    private const string Fonts = "/System/Library/Fonts/Supplemental/";

    public static TheoryData<string, string> Words => new()
    {
        { "𞤀𞤣𞤤𞤢𞤥", "NotoSansAdlam-Regular.ttf" },   // Adlam
        { "ᯅᯖᯂ᯲", "NotoSansBatak-Regular.ttf" },   // Batak
        { "𑰥𑰹𑰎𑰿𑰬𑰲", "NotoSansBhaiksuki-Regular.ttf" },   // Bhaiksuki
        { "𑀩𑁆𑀭𑀸𑀳𑁆𑀫𑀻", "NotoSansBrahmi-Regular.ttf" },   // Brahmi
        { "ᨅᨔ", "NotoSansBuginese-Regular.ttf" },   // Buginese
        { "ᝊᝓᝑᝒᝇ", "NotoSansBuhid-Regular.ttf" },   // Buhid
        { "𑄌𑄋𑄴𑄟", "NotoSansChakma-Regular.ttf" },   // Chakma
        { "ꨌꩌ", "NotoSansCham-Regular.ttf" },   // Cham
        { "𛰃𛱁𛱚", "NotoSansDuployan-Regular.ttf" },   // Duployan
        { "𓂀𓃰𓅓", "NotoSansEgyptianHieroglyphs-Regular.ttf" },   // EgyptianHieroglyphs
        { "𑵶𑶊𑵵𑶋", "NotoSansGunjalaGondi-Regular.otf" },   // GunjalaGondi
        { "𐴌𐴠𐴟𐴇𐴥𐴝𐴚𐴒𐴠𐴝", "NotoSansHanifiRohingya-Regular.ttf" },   // HanifiRohingya
        { "ᜱᜨᜳᜨᜢᜦ", "NotoSansHanunoo-Regular.ttf" },   // Hanunoo
        { "ꦧꦱꦗꦮ", "NotoSansJavanese-Regular.otf" },   // Javanese
        { "𑂍𑂶𑂟𑂲", "NotoSansKaithi-Regular.ttf" },   // Kaithi
        { "ꤊꤠꤢꤛꤢ", "NotoSansKayahLi-Regular.ttf" },   // KayahLi
        { "𐨑𐨪𐨫𐨁", "NotoSansKharoshthi-Regular.ttf" },   // Kharoshthi
        { "𑈈𑈵𑈬𑈈𑈺", "NotoSansKhojki-Regular.ttf" },   // Khojki
        { "𑊻𑋠𑋡𑋂", "NotoSansKhudawadi-Regular.ttf" },   // Khudawadi
        { "ᰛᰩᰵᰛᰧᰵ", "NotoSansLepcha-Regular.ttf" },   // Lepcha
        { "ᤛᤡᤖᤡᤈᤨᤅᤠ", "NotoSansLimbu-Regular.ttf" },   // Limbu
        { "ꓷꓶꓹ", "NotoSansLisu-Regular.ttf" },   // Lisu
        { "𑅬𑅭𑅱", "NotoSansMahajani-Regular.ttf" },   // Mahajani
        { "ࡌࡀࡍࡃࡀࡉࡉࡀ", "NotoSansMandaic-Regular.ttf" },   // Mandaic
        { "𐫖𐫀𐫗𐫏", "NotoSansManichaean-Regular.ttf" },   // Manichaean
        { "𑱲𑲏𑲒", "NotoSansMarchen-Regular.ttf" },   // Marchen
        { "ꯃꯤꯇꯩ", "NotoSansMeeteiMayek-Regular.ttf" },   // MeeteiMayek
        { "𖼏𖽡𖽪", "NotoSansMiao-Regular.ttf" },   // Miao
        { "𑘦𑘻𑘚𑘲", "NotoSansModi-Regular.ttf" },   // Modi
        { "ᠮᠣᠩᠭᠣᠯ", "NotoSansMongolian-Regular.ttf" },   // Mongolian
        { "𑊠𑊣𑊚", "NotoSansMultani-Regular.ttf" },   // Multani
        { "𑐣𑐾𑐥𑟵𑐮", "NotoSansNewa-Regular.ttf" },   // Newa
        { "ߒߞߏ", "NotoSansNKo-Regular.ttf" },   // Nko
        { "𖬖𖬰𖬅𖬲", "NotoSansPahawhHmong-Regular.ttf" },   // PahawhHmong
        { "ꡖꡒꡞ", "NotoSansPhagsPa-Regular.ttf" },   // PhagsPa
        { "𐮀𐮁𐮂", "NotoSansPsalterPahlavi-Regular.ttf" },   // PsalterPahlavi
        { "ꤽꥍꤺꥏ", "NotoSansRejang-Regular.ttf" },   // Rejang
        { "ꢱꣃꢬꢵꢰ꣄ꢜ꣄ꢬ", "NotoSansSaurashtra-Regular.ttf" },   // Saurashtra
        { "𑆯𑆳𑆫𑆢𑆳", "NotoSansSharada-Regular.ttf" },   // Sharada
        { "සිංහල", "Sinhala Sangam MN.ttc" },   // Sinhala
        { "ᮞᮥᮔ᮪ᮓ", "NotoSansSundanese-Regular.ttf" },   // Sundanese
        { "ꠍꠤꠟꠐꠤ", "NotoSansSylotiNagri-Regular.ttf" },   // SylotiNagri
        { "ᥖᥭᥰᥘᥫᥴ", "NotoSansTaiLe-Regular.ttf" },   // TaiLe
        { "ᨲᩫ᩠ᩅᨾᩮᩬᩡ", "NotoSansTaiTham-Regular.ttf" },   // TaiTham
        { "ꪎꪳ ꪼꪕ", "NotoSansTaiViet-Regular.ttf" },   // TaiViet
        { "𑚔𑚭𑚊𑚤𑚯", "NotoSansTakri-Regular.ttf" },   // Takri
        { "བོད་སྐད", "Kailasa.ttc" },   // Tibetan
        { "𑒞𑒱𑒩𑒯𑒳𑒞𑒰", "NotoSansTirhuta-Regular.ttf" },   // Tirhuta
        { "𞋒𞋀𞋉𞋃", "NotoSansWancho-Regular.ttf" },   // Wancho
    };

    /// <summary>
    /// The scripts that run from the right, for the comparison to ask about in the same direction.
    /// </summary>
    private static bool RightToLeft(string word) =>
        char.ConvertToUtf32(word, 0) is >= 0x1E900 and <= 0x1E95F      // Adlam
            or >= 0x10D00 and <= 0x10D3F                               // Hanifi Rohingya
            or >= 0x0840 and <= 0x085F                                 // Mandaic
            or >= 0x07C0 and <= 0x07FF                                 // N'Ko
            or >= 0x10AC0 and <= 0x10AFF                               // Manichaean
            or >= 0x10B80 and <= 0x10BAF                               // Psalter Pahlavi
            or >= 0x10A00 and <= 0x10A5F;                              // Kharoshthi

    [Theory]
    [MemberData(nameof(Words))]
    public void The_glyphs_are_the_glyphs_harfbuzz_chooses(string word, string face)
    {
        var path = Fonts + face;

        if (!File.Exists(path))
        {
            output.WriteLine($"{face} is not installed, so {word} was not compared.");
            return;
        }

        var file = OpenTypeOnly.Copy(path);

        if (HarfBuzz.Shape(file, word, RightToLeft(word)) is not { } theirs)
        {
            output.WriteLine("hb-shape was not found, so the shaping was not compared.");
            return;
        }

        var font = TrueTypeFont.Load(File.ReadAllBytes(file));
        var ours = HarfBuzz.Describe(TextShaper.Shape(font, word, false, RightToLeft(word)));

        output.WriteLine($"{word}\n  ours {string.Join(" ", ours)}\n  them {string.Join(" ", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>
    /// A vowel written to the left of its consonant is drawn before it, though it is stored after.
    /// </summary>
    [Fact]
    public void A_vowel_written_to_the_left_is_drawn_first()
    {
        var font = TrueTypeFont.Load(File.ReadAllBytes(
            OpenTypeOnly.Copy(Fonts + "NotoSansJavanese-Regular.otf")));

        var shaped = TextShaper.Shape(font, "ꦏꦺ");   // ka, then taling

        output.WriteLine(string.Join(" ", shaped.Glyphs.Select(g => $"{g.Glyph}/{g.Cluster}")));

        Assert.Equal(2, shaped.Count);
        Assert.Equal(1, shaped.Glyphs[0].Cluster);
        Assert.Equal(0, shaped.Glyphs[1].Cluster);
    }

    /// <summary>
    /// A vowel written on both sides of its consonant at once is taken apart first, and its left
    /// half is then drawn to the left.
    /// </summary>
    [Fact]
    public void A_vowel_written_on_two_sides_is_taken_apart()
    {
        var font = TrueTypeFont.Load(File.ReadAllBytes(
            OpenTypeOnly.Copy(Fonts + "Sinhala Sangam MN.ttc")));

        var whole = TextShaper.Shape(font, "පො");     // pa, then the two-sided vowel

        output.WriteLine(string.Join(" ", Enumerable.Range(0, whole.Count)
            .Select(at => $"{whole.Glyphs[at].Glyph}/\"{whole.TextOf(at)}\"")));

        // Three glyphs for two characters: the half drawn to the left, the letter, the half after.
        Assert.Equal(3, whole.Count);

        // And each half stands for the half it is, which is what Word writes into its own files:
        // a reader copying the line out gets something readable rather than half a vowel.
        Assert.Equal("ෙ", whole.TextOf(0));
        Assert.Equal("ප", whole.TextOf(1));
        Assert.Equal("ා", whole.TextOf(2));
    }

    /// <summary>
    /// A cluster with nothing to hang from is drawn on a dotted circle, which is how a reader is
    /// told that what was typed does not spell anything.
    /// </summary>
    [Fact]
    public void A_cluster_with_no_base_is_drawn_on_a_circle()
    {
        var font = TrueTypeFont.Load(File.ReadAllBytes(
            OpenTypeOnly.Copy(Fonts + "NotoSansKhojki-Regular.ttf")));

        var circle = font.GetGlyphIndex(0x25CC);
        Assert.NotEqual(0, circle);

        // A vowel sign with no consonant before it.
        var shaped = TextShaper.Shape(font, "\U0001122C");

        output.WriteLine(string.Join(" ", shaped.Glyphs.Select(g => g.Glyph)));

        Assert.Equal(2, shaped.Count);
        Assert.Equal(circle, shaped.Glyphs[0].Glyph);
    }

    /// <summary>
    /// A font that files its rules under no script in particular is not shaped by this engine.
    /// </summary>
    /// <remarks>
    /// Noto Sans Tai Tham is such a font: it says nothing about which script its rules are for, and
    /// draws a left-side vowel by moving the glyph rather than by expecting the character to have
    /// been moved. Reordering the run first would move it twice.
    /// </remarks>
    [Fact]
    public void A_font_that_names_no_script_is_left_alone()
    {
        var font = TrueTypeFont.Load(File.ReadAllBytes(
            OpenTypeOnly.Copy(Fonts + "NotoSansTaiTham-Regular.ttf")));

        var shaped = TextShaper.Shape(font, "ᨾᩮ");   // ma, then the vowel written to its left

        output.WriteLine(string.Join(" ", shaped.Glyphs.Select(g => $"{g.Glyph}/{g.Cluster}")));

        // Stored order kept, and the font moves the vowel itself.
        Assert.Equal(2, shaped.Count);
        Assert.Equal(0, shaped.Glyphs[0].Cluster);
        Assert.True(shaped.Glyphs[1].XOffset < 0, "the font did not move the vowel");
    }

    /// <summary>
    /// What a character is to the engine is worked out from the database rather than from a list
    /// of scripts, which is what lets it shape one it has never heard of.
    /// </summary>
    [Fact]
    public void The_categories_come_from_the_character_database()
    {
        Assert.Equal(UseCategory.Base, UseSyllables.CategoryOf('ක'));               // Sinhala ka
        Assert.Equal(UseCategory.HalantOrVowelModifier, UseSyllables.CategoryOf('්'));
        Assert.Equal(UseCategory.VowelPre, UseSyllables.CategoryOf('ෙ'));
        Assert.Equal(UseCategory.VowelPost, UseSyllables.CategoryOf('ා'));

        Assert.Equal(UseCategory.Base, UseSyllables.CategoryOf(0x1E900));           // Adlam alif
        Assert.Equal(UseCategory.VowelPre, UseSyllables.CategoryOf('ꦺ'));           // Javanese taling
        Assert.Equal(UseCategory.Sakot, UseSyllables.CategoryOf('᩠'));              // Tai Tham sakot

        // And which name a font files each script under.
        Assert.Equal("sinh", UseSyllables.ScriptTagOf('ක'));
        Assert.Equal("java", UseSyllables.ScriptTagOf('ꦺ'));
        Assert.Equal("adlm", UseSyllables.ScriptTagOf(0x1E900));

        // Latin is not one of them: it has no rules here and needs none.
        Assert.Null(UseSyllables.ScriptTagOf('A'));
    }

    /// <summary>
    /// Against Word: five lines of Sinhala holding a conjunct asked for with a joiner, a vowel
    /// written on both sides of its letter, and one written to the left.
    /// </summary>
    /// <remarks>
    /// Sinhala rather than one of the seventy others because Word can draw it. Asked for Tibetan,
    /// Javanese or Cham on this machine, Word draws the letters side by side without stacking or
    /// reordering anything — so for those there is nothing to hold it to, and HarfBuzz is the only
    /// reference.
    /// </remarks>
    [Fact]
    public void The_fixture_lines_go_where_word_puts_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "universal.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("universal",
            Converter.Convert(Fixtures.Build("universal"),
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
