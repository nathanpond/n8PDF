using n8PDF;
using n8PDF.Tests.Support;
using n8PDF.Text;

namespace n8PDF.Tests;

/// <summary>
/// Tests where a line may be broken.
/// </summary>
/// <remarks>
/// Two questions, and only the first has a right answer written down. Where the Unicode algorithm
/// decides, the answers here are the ones its own conformance file gives — the cases below were
/// taken from it, and the whole of it was run against this implementation while it was written:
/// 7310 of its 7654 lines, every one that does not turn on a script needing a dictionary, and all
/// of them pass. The file is not committed, since it is half a megabyte of a thing that changes
/// once a year; the command that fetches it is in tools/make-linebreak-tables.py.
///
/// The second question is the scripts the algorithm does not decide: Thai, Lao, Khmer and Burmese,
/// written without spaces and broken between words that only a dictionary knows the bounds of.
/// There the answers are Word's, read off its own export of the wrapping fixture — it breaks those
/// between one syllable and the next, mid-word, and so does this.
/// </remarks>
public class LineBreakingTests
{
    /// <summary>Marks each place a line may be broken with a bar, which is what reads.</summary>
    private static string Shown(string text)
    {
        var breaks = LineBreaker.Opportunities(text);
        var shown = new System.Text.StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            if (breaks[i]) shown.Append('|');
            shown.Append(text[i]);
        }

        return shown.ToString();
    }

    public static TheoryData<string, string> Latin => new()
    {
        { "The quick brown fox", "The |quick |brown |fox" },

        // A hyphen is a break; the space in a measurement is not.
        { "well-known example", "well-|known |example" },
        { "10 kg of flour", "10 |kg |of |flour" },

        // Nor is anything inside a number: not after the currency mark, not at the separators,
        // and not before the per-cent sign.
        { "$1,234.56 and 78%", "$1,234.56 |and |78%" },

        // Nothing is left alone at the end of a line: not a closing bracket, not a full stop.
        { "(a) end.", "(a) |end." },

        // A no-break space holds what is on either side of it together, which is what it is for.
        { "Mr Smith went", "Mr Smith |went" },

        // An em dash may begin a line or end one.
        { "before—after", "before|—|after" }
    };

    [Theory]
    [MemberData(nameof(Latin))]
    public void Where_a_line_may_be_broken(string text, string expected) =>
        Assert.Equal(expected, Shown(text));

    public static TheoryData<string, string> Ideographic => new()
    {
        // Between one character and the next, since there are no spaces to break at.
        { "中文的排版", "中|文|的|排|版" },

        // But never before a full stop, a comma or a closing bracket, and never after an opening
        // one: those are the kinsoku rules, and they fall out of the algorithm's own classes.
        { "日本語です。", "日|本|語|で|す。" },
        { "（括弧）と、句読点。", "（括|弧）|と、|句|読|点。" }
    };

    [Theory]
    [MemberData(nameof(Ideographic))]
    public void Chinese_and_japanese_break_between_characters(string text, string expected) =>
        Assert.Equal(expected, Shown(text));

    public static TheoryData<string, string> Syllabic => new()
    {
        // Thai: between syllables. A vowel written above or below its consonant belongs to it,
        // and so does sara a, which is written as a letter but sounds after one.
        { "ประเทศไทย", "ป|ระ|เท|ศ|ไท|ย" },
        { "มีประชากร", "มี|ป|ระ|ชา|ก|ร" },

        // The four vowels Thai writes before the consonant they are sounded after are not left
        // at the end of a line by themselves: there is no break between sara e and the mo that
        // follows it, although there is one before every other consonant here.
        { "เมืองหลวง", "เมื|อ|ง|ห|ล|ว|ง" },

        // Lao, which is written the same way.
        { "ພາສາລາວ", "ພາ|ສາ|ລາ|ວ" },

        // Khmer: the sign that turns the next consonant into a subscript keeps the two together.
        { "ភាសាខ្មែរ", "ភា|សា|ខ្មែ|រ" }
    };

    [Theory]
    [MemberData(nameof(Syllabic))]
    public void The_scripts_without_spaces_break_between_syllables(string text, string expected) =>
        Assert.Equal(expected, Shown(text));

    /// <summary>
    /// Nothing is broken where there is nothing to break, and a mark is never parted from what it
    /// is written on.
    /// </summary>
    [Fact]
    public void The_edges_hold()
    {
        Assert.Empty(LineBreaker.Opportunities(""));
        Assert.Equal(new[] { false }, LineBreaker.Opportunities("a"));

        // A combining mark, a joiner and the two halves of a character written as a surrogate
        // pair are each one thing with what stands before them.
        Assert.Equal("é |á", Shown("é á"));
        Assert.Equal("\U0001F469‍\U0001F4BB", Shown("\U0001F469‍\U0001F4BB"));

        // And a flag is two regional indicators, which are not broken in half.
        Assert.Equal("\U0001F1EF\U0001F1F5|\U0001F1EC\U0001F1E7",
            Shown("\U0001F1EF\U0001F1F5\U0001F1EC\U0001F1E7"));
    }

    /// <summary>
    /// And the whole of it against Word: three paragraphs with no spaces in them at all, broken
    /// into the same lines Word breaks them into, to a hundredth of a point.
    /// </summary>
    /// <remarks>
    /// This is the measurement that matters. Every rule above can be right and the lines still
    /// come out elsewhere; what this says is that given the same paragraph and the same faces —
    /// Word's own Mincho and KaiTi, and Ayuthaya for the Thai — the two break it in the same
    /// places, including the Thai break that falls in the middle of a word.
    /// </remarks>
    [Fact]
    public void The_fixture_lines_break_where_word_breaks_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "wrapping.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("wrapping",
            Converter.Convert(Fixtures.Build("wrapping"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));

        // Every line of ours has a counterpart holding the same text, which is the assertion:
        // the lines are broken in the same places.
        Assert.Equal(0, report.UnmatchedCount);

        Assert.True(report.MaxAbsStartXDelta < 0.1,
            $"a line begins {report.MaxAbsStartXDelta:0.###}pt from where Word begins it");

        Assert.True(report.MaxAbsWidthDelta < 0.1,
            $"a line is {report.MaxAbsWidthDelta:0.###}pt wider or narrower than Word's");
    }
}
