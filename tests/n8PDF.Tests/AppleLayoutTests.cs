using System.Diagnostics;
using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the faces that describe their shaping in Apple's tables and carry no OpenType tables at
/// all.
/// </summary>
/// <remarks>
/// There are a hundred and sixty of them on this machine — Devanagari MT, Gujarati MT, Gurmukhi MT,
/// Thonburi, Geeza Pro, Corsiva Hebrew, and the whole Helvetica, Palatino and Optima families. A
/// converter that reads only OpenType draws their scripts as rows of unjoined letters, and there is
/// nothing in the file to fall back on: the shaping is all in <c>morx</c>, said as state machines
/// rather than as lookups.
///
/// HarfBuzz reads the same table and is the reference here, as it is everywhere else — and for once
/// without the caveat, since for these faces there is no other table for it to prefer.
/// </remarks>
public class AppleLayoutTests(ITestOutputHelper output)
{
    private const string Fonts = "/System/Library/Fonts/Supplemental/";

    public static TheoryData<string, string> Words => new()
    {
        // Devanagari: conjuncts, a vowel drawn before its consonant, an r drawn as a mark.
        { "नमस्ते", "DevanagariMT.ttc" },
        { "हिन्दी", "DevanagariMT.ttc" },
        { "कर्म", "DevanagariMT.ttc" },
        { "क्षत्रिय", "DevanagariMT.ttc" },
        { "विद्यालय", "DevanagariMT.ttc" },
        { "श्री", "DevanagariMT.ttc" },
        { "अंग्रेज़ी", "DevanagariMT.ttc" },
        { "क्ष", "DevanagariMT.ttc" },
        { "त्र", "DevanagariMT.ttc" },

        { "ગુજરાતી", "GujaratiMT.ttc" },
        { "શ્રી", "GujaratiMT.ttc" },
        { "શબ્દ", "GujaratiMT.ttc" },
        { "સંસ્કૃત", "GujaratiMT.ttc" },

        { "ਪੰਜਾਬੀ", "Gurmukhi.ttf" },
        { "ਸਿੱਖ", "Gurmukhi.ttf" },

        // Thai, whose face carries a hundred and thirty-eight of these machines.
        { "สวัสดี", "Thonburi.ttc" },
        { "สวัสดีครับ", "Thonburi.ttc" },
        { "ภาษาไทย", "Thonburi.ttc" },
        { "กรุงเทพมหานคร", "Thonburi.ttc" },
        { "ประเทศไทย", "Thonburi.ttc" },

        // Arabic, where the machines pick the shape each letter takes from its neighbours.
        { "مرحبا", "Baghdad.ttc" },
        { "العربية", "AlBayan.ttc" },

        { "שלום", "Corsiva.ttc" },

        // And Latin, where what these tables mostly hold is the ligatures the face was drawn with.
        { "office fjord", "../Helvetica.ttc" },
        { "Waffle office", "Optima.ttc" },
        { "difficult", "Palatino.ttc" }
    };

    [Theory]
    [MemberData(nameof(Words))]
    public void The_glyphs_are_the_glyphs_harfbuzz_chooses(string word, string face)
    {
        var path = Path.GetFullPath(Fonts + face);

        if (!File.Exists(path))
        {
            output.WriteLine($"{face} is not installed, so {word} was not compared.");
            return;
        }

        // These faces are asked about as they are: HarfBuzz prefers Apple's tables wherever a font
        // has them, and here that is the only kind it has.
        var rightToLeft = word[0] is >= '֐' and <= 'ࣿ';

        // Kerning off: what is being compared here is which glyphs the machines choose, and some
        // of these faces carry the old kerning table as well, which is a separate question.
        if (Shape(path, word, rightToLeft, kerned: false) is not { } theirs)
        {
            output.WriteLine("hb-shape was not found, so the shaping was not compared.");
            return;
        }

        var font = TrueTypeFont.Load(File.ReadAllBytes(path));
        var ours = HarfBuzz.Describe(TextShaper.Shape(font, word, false, rightToLeft));

        output.WriteLine($"{word}\n  ours {string.Join(" ", ours)}\n  them {string.Join(" ", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>The same question, put to one face of a collection.</summary>
    private static List<string>? Shape(
        string path, string word, bool rightToLeft, int index = 0, bool kerned = true)
    {
        try
        {
            var arguments = new List<string>
            {
                $"--font-file={path}", $"--face-index={index}", "--no-glyph-names"
            };

            if (!kerned) arguments.Add("--features=-kern");

            if (rightToLeft) arguments.Add("--direction=rtl");
            arguments.Add(word);

            using var process = Process.Start(new ProcessStartInfo("hb-shape", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return null;

            var written = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(20_000);

            if (written.Length == 0) return null;

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

    /// <summary>
    /// Where the glyphs go, for the faces that say it in Apple's tables rather than in
    /// <c>GPOS</c>.
    /// </summary>
    /// <remarks>
    /// Two quite different things live in that table. Most of it is kerning — by naming pairs, by
    /// naming classes of glyphs, or by a machine that keeps a stack of what it has passed and moves
    /// several at once. The rest is attachment: a machine that marks a letter and then fastens what
    /// follows to it, by naming a point on each out of a table of anchors.
    ///
    /// The faces here are asked with their OpenType tables taken out, because most of them carry
    /// both descriptions and this converter reads the OpenType one where there is a choice. That is
    /// not a way of hiding a difference: it is the only way to ask the question at all, since a
    /// face carrying both is never read this way in earnest.
    /// </remarks>
    [Theory]
    [InlineData("മലയാളം", "Malayalam Sangam MN.ttc")]      // kerning by classes
    [InlineData("കൃഷ്ണൻ", "Malayalam Sangam MN.ttc")]
    [InlineData("తెలుగు", "Telugu MN.ttc")]                 // marks fastened by anchor
    [InlineData("తెలుగు భాష", "Telugu Sangam MN.ttc")]
    [InlineData("ଓଡ଼ିଶା", "Oriya MN.ttc")]
    [InlineData("বাংলা", "Bangla Sangam MN.ttc")]
    [InlineData("ভারত", "Bangla MN.ttc")]
    [InlineData("ಕನ್ನಡ", "Kannada MN.ttc")]
    [InlineData("ગુજરાતી", "Gujarati Sangam MN.ttc")]
    [InlineData("मराठी", "Devanagari Sangam MN.ttc")]
    [InlineData("မြန်မာ", "Myanmar Sangam MN.ttc")]
    [InlineData("שלום", "../ArialHB.ttc")]                  // a machine, read backwards
    [InlineData("Waffle AVA To", "../Helvetica.ttc")]       // pairs and classes, in Helvetica Light
    public void The_glyphs_go_where_harfbuzz_puts_them(string word, string face)
    {
        var path = Path.GetFullPath(Fonts + face);

        if (!File.Exists(path))
        {
            output.WriteLine($"{face} is not installed, so {word} was not compared.");
            return;
        }

        // Helvetica Light is the face of that collection with Apple's positioning tables in it.
        var index = face.Contains("Helvetica") ? 4 : 0;
        var file = OpenTypeOnly.AppleOnly(path, index);

        var rightToLeft = word[0] is >= '֐' and <= 'ࣿ';

        if (Shape(file, word, rightToLeft, index: 0) is not { } theirs)
        {
            output.WriteLine("hb-shape was not found, so the placing was not compared.");
            return;
        }

        var font = TrueTypeFont.Load(File.ReadAllBytes(file));

        // Asked for with kerning on, since kerning is half of what this table holds.
        var ours = HarfBuzz.Describe(TextShaper.Shape(font, word, true, rightToLeft));

        output.WriteLine($"{word}\n  ours {string.Join(" ", ours)}\n  them {string.Join(" ", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>
    /// A face that carries both kinds of table is read as OpenType, since that is the description
    /// Word reads.
    /// </summary>
    /// <remarks>
    /// The same holds for where the glyphs go, and for the same reason.
    /// Several faces carry the same shaping twice over. Where they differ the difference is real —
    /// Khmer Sangam MN's OpenType tables write a consonant and its vowel as a shape plus a blank,
    /// and its state machine deletes the blank instead — and it is the OpenType answer that has to
    /// be given, because it is the one every other reader of the file will give.
    /// </remarks>
    [Fact]
    public void A_face_with_both_kinds_of_table_is_read_as_opentype()
    {
        var both = TrueTypeFont.Load(
            File.ReadAllBytes(Fonts + "Devanagari Sangam MN.ttc"));

        var apple = TrueTypeFont.Load(File.ReadAllBytes(Fonts + "DevanagariMT.ttc"));

        Assert.NotNull(both.Substitutor);
        Assert.Null(both.Metamorphosis);

        Assert.Null(apple.Substitutor);
        Assert.NotNull(apple.Metamorphosis);

        // And a face that says where its glyphs go in both places is read the same way round.
        var positioned = TrueTypeFont.Load(File.ReadAllBytes(Fonts + "Telugu MN.ttc"));

        Assert.NotNull(positioned.Positioner);
        Assert.Null(positioned.ExtendedKerning);
    }

    /// <summary>
    /// A face whose name is written in its own script is still known by the name a document calls
    /// it.
    /// </summary>
    /// <remarks>
    /// These faces carry their family name several times over, in several languages. Gujarati MT
    /// calls itself ગુજરાતી એચટી in Gujarati and "Gujarati MT" in English, and a document naming it
    /// means the second: it is the name Word knows it by. Taking the first record of the right kind
    /// rather than the English one loses the font altogether — it is then never matched, and the
    /// text is drawn in whatever face is borrowed for it.
    /// </remarks>
    [Theory]
    [InlineData("DevanagariMT.ttc", "Devanagari MT")]
    [InlineData("GujaratiMT.ttc", "Gujarati MT")]
    [InlineData("Gurmukhi.ttf", "Gurmukhi MT")]
    public void A_face_is_known_by_its_english_name(string file, string expected)
    {
        var font = TrueTypeFont.Load(File.ReadAllBytes(Fonts + file));

        output.WriteLine($"{file}: {font.FamilyName}");

        Assert.Equal(expected, font.FamilyName);
    }

    /// <summary>
    /// The whole of it against Word: Devanagari, Gujarati and Gurmukhi in faces that hold no
    /// OpenType tables.
    /// </summary>
    /// <remarks>
    /// Word reads these tables too, and reads them the same way on four of the five lines — the
    /// widths agree to four hundredths of a point. Two things are left out of the fixture because
    /// Word does something else with them, and both are compared against HarfBuzz above instead.
    /// Asked for Thai in Thonburi, Word draws the line in a font of its own. And for the word for
    /// kshatriya, Word's reading of Devanagari MT comes out two points wider than HarfBuzz's; which
    /// of the two Apple's own engine agrees with is not a question this machine can answer.
    /// </remarks>
    [Fact]
    public void The_fixture_lines_go_where_word_puts_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "apple.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("apple",
            Converter.Convert(Fixtures.Build("apple"),
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
