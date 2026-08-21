using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using n8PDF.Text;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Words broken at the ends of lines, from <c>w:autoHyphenation</c>.
/// </summary>
/// <remarks>
/// Where a word may be broken is a matter of a language's habits rather than of its letters, so
/// there is a table: Liang's patterns, as TeX has distributed them since 1990, turned into source
/// by <c>tools/make-hyphenation-tables.py</c>. Which of the places it allows is used is this
/// library's business, and it is the last that fits.
///
/// Four fixtures put it to Word, and every line of all four comes out where Word puts it:
///
///   hyphenation-probe        the same paragraph hyphenated, suppressed, justified, in capitals
///   hyphenation-zone-probe   an inch of zone rather than Word's quarter, which stops all of it
///   hyphenation-limit-probe  no more than two lines in a row ending in a hyphen
///   hyphenation-caps-probe   words in capitals left whole
/// </remarks>
public class HyphenationTests(ITestOutputHelper output)
{
    /// <summary>
    /// Where the table says a word may be broken. Two letters must stay behind and two must go
    /// on, which is Word's rule rather than the pattern file's — the file says three must go on,
    /// as a typesetter would, and Word breaks PARTICULAR-LY.
    /// </summary>
    [Theory]
    [InlineData("conspicuous", "con-spic-u-ous")]
    [InlineData("examples", "ex-am-ples")]
    [InlineData("misunderstanding", "mis-un-der-stand-ing")]
    [InlineData("organisation", "or-gan-i-sa-tion")]
    [InlineData("particularly", "par-tic-u-lar-ly")]
    [InlineData("hyphenation", "hy-phen-ation")]
    // Four letters is too few to break: two would have to stay and two to go, and the table finds
    // nothing in between.
    [InlineData("word", "word")]
    // And a word the patterns get wrong is spelled out in the file itself.
    [InlineData("table", "ta-ble")]
    [InlineData("project", "project")]
    public void The_table_says_where_a_word_may_be_broken(string word, string expected)
    {
        var points = Hyphenator.Points(word);
        var pieces = new List<string>();
        var at = 0;

        foreach (var point in points)
        {
            pieces.Add(word[at..point]);
            at = point;
        }

        pieces.Add(word[at..]);

        output.WriteLine($"{word} → {string.Join("-", pieces)}");
        Assert.Equal(expected, string.Join("-", pieces));
    }

    /// <summary>Every line of every hyphenation fixture, against Word's own.</summary>
    [Theory]
    [InlineData("hyphenation-probe", "hyphenated, suppressed, justified and in capitals")]
    [InlineData("hyphenation-zone-probe", "an inch of zone")]
    [InlineData("hyphenation-limit-probe", "two lines in a row at most")]
    [InlineData("hyphenation-caps-probe", "capitals left whole")]
    public void The_lines_break_where_words_break(string fixture, string what)
    {
        if (TestFonts.SkipForMissingFonts(fixture)) return;

        output.WriteLine(what);

        var word = Lines(File.ReadAllBytes(Path.Combine(TestPaths.ReferencePdfs, fixture + ".pdf")));
        var ours = Lines(Ours(fixture));

        foreach (var line in ours) output.WriteLine($"   | {line}");

        Assert.Equal(word, ours);
    }

    /// <summary>
    /// A word is broken at the last place the line has room for. Word's own: conspicuous is broken
    /// after "conspicu" and organisation after "or", each being as much as the line could take.
    /// </summary>
    [Fact]
    public void A_word_is_broken_at_the_last_place_that_fits()
    {
        if (TestFonts.SkipForMissingFonts("hyphenation-probe")) return;

        var broken = Lines(Ours("hyphenation-probe"))
            .Where(line => line.EndsWith('-'))
            .Select(line => line[(line.LastIndexOf(' ') + 1)..])
            .ToList();

        output.WriteLine(string.Join(" ", broken));

        Assert.Contains("conspicu-", broken);
        Assert.Contains("or-", broken);
        Assert.Contains("exam-", broken);
    }

    /// <summary>
    /// A paragraph that says its words are to be left whole keeps them whole, and so does a
    /// document that never asked for hyphenation at all.
    /// </summary>
    [Fact]
    public void A_paragraph_may_refuse_to_have_its_words_broken()
    {
        if (TestFonts.SkipForMissingFonts("hyphenation-probe")) return;

        var pages = Pages(Ours("hyphenation-probe"));

        Assert.Contains(pages[0], line => line.EndsWith('-'));
        Assert.DoesNotContain(pages[1], line => line.EndsWith('-'));
    }

    /// <summary>
    /// The zone is how much white a line may be left with before a word is broken to fill it. An
    /// inch of it is more than any of these lines is left with, so none of them is hyphenated.
    /// </summary>
    [Fact]
    public void A_wide_zone_leaves_the_words_whole()
    {
        if (TestFonts.SkipForMissingFonts("hyphenation-zone-probe")) return;

        Assert.DoesNotContain(Lines(Ours("hyphenation-zone-probe")), line => line.EndsWith('-'));
        Assert.Contains(Lines(Ours("hyphenation-probe")), line => line.EndsWith('-'));
    }

    /// <summary>No more lines in a row may end in a hyphen than the document allows.</summary>
    [Fact]
    public void No_more_lines_run_on_than_the_limit_allows()
    {
        if (TestFonts.SkipForMissingFonts("hyphenation-limit-probe")) return;

        var run = 0;
        var longest = 0;

        foreach (var line in Lines(Ours("hyphenation-limit-probe")))
        {
            run = line.EndsWith('-') ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        output.WriteLine($"the longest run of hyphens is {longest}");

        Assert.Equal(2, longest);
    }

    /// <summary>A word in capitals is left whole where the document says so.</summary>
    [Fact]
    public void Capitals_are_left_whole_where_the_document_says_so()
    {
        if (TestFonts.SkipForMissingFonts("hyphenation-caps-probe")) return;

        Assert.DoesNotContain(Lines(Ours("hyphenation-caps-probe")), line => line.EndsWith('-'));
    }

    private static byte[] Ours(string fixture) =>
        Converter.Convert(Fixtures.Build(fixture),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>Every line of a document, in order, as the words on it.</summary>
    private static List<string> Lines(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .GroupBy(run => (run.PageIndex, Math.Round(run.BaselineY, 2)))
            .OrderBy(line => line.Key.PageIndex).ThenBy(line => line.Key.Item2)
            .Select(line => string.Concat(line.OrderBy(run => run.X).Select(run => run.Text)).Trim())
            .Where(line => line.Length > 0)
            .ToList();

    /// <summary>The same, page by page.</summary>
    private static List<List<string>> Pages(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf)
            .GroupBy(run => run.PageIndex)
            .OrderBy(page => page.Key)
            .Select(page => page
                .GroupBy(run => Math.Round(run.BaselineY, 2))
                .OrderBy(line => line.Key)
                .Select(line => string.Concat(line.OrderBy(run => run.X).Select(run => run.Text)).Trim())
                .Where(line => line.Length > 0)
                .ToList())
            .ToList();
}
