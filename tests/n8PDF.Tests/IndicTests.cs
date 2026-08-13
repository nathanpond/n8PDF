using System.Diagnostics;
using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Text;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the Indic scripts, which are drawn neither in the order they are stored nor one shape to
/// a letter.
/// </summary>
/// <remarks>
/// Three things happen in a syllable of these scripts that happen nowhere in Latin. A vowel may be
/// written to the left of the consonant it is pronounced after, though it is stored after it.
/// Consonants with no vowel between them are written as one stacked shape. And an r at the head of
/// a cluster is written as a small mark at the end of it. A converter that walks the characters and
/// looks each one up draws none of that: it draws the letters of a word in the wrong order, which
/// is not a matter of ugliness but of the word saying something else.
///
/// HarfBuzz is the reference, as it was for Arabic — with one thing to be careful about, which is
/// what <see cref="OpenTypeOnly"/> is for. It is asked about every word here, and not only about
/// which glyphs but where each of them is put.
/// </remarks>
public class IndicTests(ITestOutputHelper output)
{
    private const string Fonts = "/System/Library/Fonts/Supplemental/";

    public static TheoryData<string, string> Words => new()
    {
        // Devanagari, in which Hindi, Marathi, Nepali and Sanskrit are written.
        { "नमस्ते", "Devanagari Sangam MN.ttc" },      // namaste: a conjunct in the middle
        { "हिन्दी", "Devanagari Sangam MN.ttc" },       // hindi: a vowel drawn before its consonant
        { "क्षत्रिय", "Devanagari Sangam MN.ttc" },      // kshatriya: three consonants in one shape
        { "कर्म", "Devanagari Sangam MN.ttc" },        // karma: an r drawn as a mark at the end
        { "मुंबई", "Devanagari Sangam MN.ttc" },        // mumbai: marks above and below
        { "विद्यालय", "Devanagari Sangam MN.ttc" },     // vidyalaya: a conjunct and a left vowel
        { "श्री", "Devanagari Sangam MN.ttc" },         // shri: two consonants and a vowel as one
        { "अंग्रेज़ी", "Devanagari Sangam MN.ttc" },      // angrezi: a nukta, which changes a letter

        { "தமிழ்", "Tamil Sangam MN.ttc" },
        { "வணக்கம்", "Tamil Sangam MN.ttc" },

        { "বাংলা", "Bangla Sangam MN.ttc" },
        { "ভারত", "Bangla Sangam MN.ttc" },

        { "ਪੰਜਾਬੀ", "Gurmukhi Sangam MN.ttc" },
        { "ગુજરાતી", "Gujarati Sangam MN.ttc" },
        { "ଓଡ଼ିଶା", "Oriya Sangam MN.ttc" },
        { "తెలుగు", "Telugu Sangam MN.ttc" },
        { "ಕನ್ನಡ", "Kannada Sangam MN.ttc" },
        { "മലയാളം", "Malayalam Sangam MN.ttc" },
        { "കൃഷ്ണൻ", "Malayalam Sangam MN.ttc" }
    };

    /// <summary>
    /// Every glyph of a word, and everything done to it, against HarfBuzz's answer for the same
    /// word in the same face.
    /// </summary>
    [Theory]
    [MemberData(nameof(Words))]
    public void The_glyphs_are_the_glyphs_harfbuzz_chooses(string word, string face)
    {
        var path = OpenTypeOnly.Copy(Fonts + face);

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
    /// A vowel stored after its consonant and written before it comes out before it.
    /// </summary>
    /// <remarks>
    /// Said on its own because it is the thing most easily got wrong without anything looking
    /// wrong: the glyphs are all there and all correct, in an order that spells another word.
    /// </remarks>
    [Fact]
    public void A_vowel_written_to_the_left_is_drawn_before_its_consonant()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy(Fonts + "Devanagari Sangam MN.ttc")));

        // ka, then the vowel sign i — stored in that order, drawn the other way round.
        var shaped = TextShaper.Shape(font, "कि");

        var vowel = font.GetGlyphIndex('ि');
        var consonant = font.GetGlyphIndex('क');

        output.WriteLine($"drawn: {string.Join(" ", shaped.Glyphs.Select(glyph => glyph.Glyph))}, " +
                         $"vowel {vowel}, consonant {consonant}");

        Assert.Equal(2, shaped.Count);
        Assert.Equal(consonant, shaped.Glyphs[1].Glyph);

        // The vowel is drawn first, whichever of its forms the font chose for it.
        Assert.NotEqual(consonant, shaped.Glyphs[0].Glyph);

        // And it still says which character it came from, so the word can be found again.
        Assert.Equal("ि", shaped.TextOf(0));
    }

    /// <summary>
    /// Two consonants with the mark that joins them are written as one shape, and that shape
    /// stands for all three characters.
    /// </summary>
    [Fact]
    public void Consonants_joined_by_a_halant_become_one_shape()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy(Fonts + "Devanagari Sangam MN.ttc")));

        var apart = TextShaper.Shape(font, "दय");     // da, ya
        var joined = TextShaper.Shape(font, "द्य");    // da, halant, ya

        output.WriteLine($"apart {apart.Count} glyphs, joined {joined.Count}");

        Assert.Equal(2, apart.Count);
        Assert.Equal(1, joined.Count);

        // What is drawn as one shape is still read as the three characters it was made of, so the
        // word can be searched for and copied out.
        Assert.Equal("द्य", joined.TextOf(0));
    }

    /// <summary>
    /// An r at the head of a cluster is drawn as a mark at the end of it — after the consonant it
    /// was stored before.
    /// </summary>
    [Fact]
    public void An_r_at_the_head_of_a_cluster_is_drawn_at_its_end()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy(Fonts + "Devanagari Sangam MN.ttc")));

        // ra, halant, ma: the r is not drawn as a letter at all.
        var shaped = TextShaper.Shape(font, "र्म");

        output.WriteLine(string.Join(" ", Enumerable.Range(0, shaped.Count).Select(
            at => $"{shaped.Glyphs[at].Glyph}/{shaped.TextOf(at)}")));

        Assert.Equal(2, shaped.Count);

        // The consonant it was stored before is drawn first, and the mark it became is drawn last
        // and advances the pen by nothing.
        Assert.Equal(font.GetGlyphIndex('म'), shaped.Glyphs[0].Glyph);
        Assert.Equal(0, shaped.Glyphs[1].Advance);
    }

    /// <summary>
    /// A syllable's rules are its own: what a font says about a consonant followed by a vowel is
    /// not a licence to reach into the next word.
    /// </summary>
    [Fact]
    public void A_rule_does_not_reach_across_a_syllable()
    {
        var font = TrueTypeFont.Load(
            File.ReadAllBytes(OpenTypeOnly.Copy(Fonts + "Devanagari Sangam MN.ttc")));

        // Two consonants standing side by side are two syllables, and neither is a half form of
        // anything: la is drawn as itself.
        var alone = TextShaper.Shape(font, "ल");
        var beside = TextShaper.Shape(font, "लय");

        output.WriteLine($"alone {alone.Glyphs[0].Glyph}, beside {beside.Glyphs[0].Glyph}");

        Assert.Equal(alone.Glyphs[0].Glyph, beside.Glyphs[0].Glyph);
    }

    /// <summary>
    /// What a character is to the shaper comes from the database rather than from a list somebody
    /// typed, and the categories that matter are the ones the reordering turns on.
    /// </summary>
    [Fact]
    public void The_categories_come_from_the_character_database()
    {
        Assert.Equal(IndicCategory.Consonant, IndicSyllables.CategoryOf('क'));
        Assert.Equal(IndicCategory.Ra, IndicSyllables.CategoryOf('र'));
        Assert.Equal(IndicCategory.Halant, IndicSyllables.CategoryOf('्'));
        Assert.Equal(IndicCategory.Matra, IndicSyllables.CategoryOf('ि'));
        Assert.Equal(IndicCategory.Vowel, IndicSyllables.CategoryOf('अ'));
        Assert.Equal(IndicCategory.Nukta, IndicSyllables.CategoryOf('़'));

        // And where it goes: the vowel sign i is written to the left of everything, which is the
        // one position that makes a syllable come out in a different order from the one stored.
        Assert.Equal(IndicPosition.PreMatra, IndicSyllables.PositionOf('ि'));
        Assert.Equal(IndicPosition.BaseConsonant, IndicSyllables.PositionOf('क'));
    }

    /// <summary>
    /// A font written to the older rules is shaped by the older rules.
    /// </summary>
    /// <remarks>
    /// The specification for these scripts was rewritten, and a font says which set of rules it was
    /// drawn against by which of two names it files its script under. Under the older ones a
    /// joining mark after the base is moved to the end of the syllable, the below-base feature is
    /// not applied before the base, an r joined to what follows is asked for by name, and the
    /// questions put to the font about a pair of letters are about the pair in company rather than
    /// standing alone.
    ///
    /// Two faces here are written that way: Shree Devanagari 714, and Arial Unicode MS, which files
    /// five scripts under their older names. Both are real fonts of the kind a document may well
    /// name, rather than a new-spec font with its label changed.
    /// </remarks>
    [Theory]
    [InlineData("नमस्ते", "/System/Library/Fonts/Supplemental/Shree714.ttc")]
    [InlineData("हिन्दी", "/System/Library/Fonts/Supplemental/Shree714.ttc")]
    [InlineData("क्षत्रिय", "/System/Library/Fonts/Supplemental/Shree714.ttc")]
    [InlineData("कर्म", "/System/Library/Fonts/Supplemental/Shree714.ttc")]
    [InlineData("विद्यालय", "/System/Library/Fonts/Supplemental/Shree714.ttc")]
    [InlineData("नमस्ते", "/Library/Fonts/Arial Unicode.ttf")]
    [InlineData("हिन्दी", "/Library/Fonts/Arial Unicode.ttf")]
    [InlineData("கணிதம்", "/Library/Fonts/Arial Unicode.ttf")]
    [InlineData("ਪੰਜਾਬੀ", "/Library/Fonts/Arial Unicode.ttf")]
    [InlineData("ગુજરાતી", "/Library/Fonts/Arial Unicode.ttf")]
    public void A_font_written_to_the_older_rules_is_shaped_by_them(string word, string path)
    {
        if (!File.Exists(path))
        {
            output.WriteLine($"{path} is not installed, so the older rules were not compared.");
            return;
        }

        var file = OpenTypeOnly.Copy(path);

        if (HarfBuzz.Shape(file, word) is not { } theirs)
        {
            output.WriteLine("hb-shape was not found, so the shaping was not compared.");
            return;
        }

        var font = TrueTypeFont.Load(File.ReadAllBytes(file));
        var ours = HarfBuzz.Describe(TextShaper.Shape(font, word));

        output.WriteLine($"{word}\n  ours {string.Join(" ", ours)}\n  them {string.Join(" ", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>
    /// The whole of it against Word: five lines of Devanagari holding a conjunct, a left-side
    /// vowel, a three-consonant stack and a repha, then Tamil and Bengali.
    /// </summary>
    /// <remarks>
    /// What is compared is where the text goes. What it says cannot be: Word writes a shaped
    /// syllable as glyphs mapped back to nothing in particular — a conjunct comes out of its file
    /// as "#$" — so the two files agree about the page and disagree about the alphabet.
    /// </remarks>
    [Fact]
    public void The_fixture_lines_go_where_word_puts_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "indic.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("indic",
            Converter.Convert(Fixtures.Build("indic"),
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

/// <summary>Shapes a word with HarfBuzz, for the tests that compare against it.</summary>
internal static class HarfBuzz
{
    /// <summary>
    /// What HarfBuzz makes of a word: each glyph, where it is drawn from the pen, and what it
    /// advances the pen by. Null where hb-shape is not installed.
    /// </summary>
    public static List<string>? Shape(string path, string word, bool rightToLeft = false)
    {
        try
        {
            var arguments = new List<string>
            {
                $"--font-file={path}", "--no-glyph-names", "--features=-kern", word
            };

            if (rightToLeft) arguments.Insert(1, "--direction=rtl");

            using var process = Process.Start(new ProcessStartInfo("hb-shape", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return null;

            var written = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(20_000);

            if (written.Length == 0) return null;

            // Each piece is glyph=cluster@x,y+advance, with the offset left out where it is
            // nought and the cluster of no interest here.
            return written
                .Trim('[', ']')
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(piece =>
                {
                    var glyph = piece[..piece.IndexOf('=')];
                    var rest = piece[(piece.IndexOf('=') + 1)..];
                    var advance = rest[(rest.IndexOf('+') + 1)..];

                    var at = rest.IndexOf('@');
                    var offset = at < 0 ? "0,0" : rest[(at + 1)..rest.IndexOf('+')];

                    return $"{glyph}@{offset}+{advance}";
                })
                .ToList();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>The same, said of what this converter's shaper produced.</summary>
    public static List<string> Describe(ShapedText shaped) =>
        [.. shaped.Glyphs.Select(glyph =>
            $"{glyph.Glyph}@{glyph.XOffset},{glyph.YOffset}+{glyph.Advance}")];
}
