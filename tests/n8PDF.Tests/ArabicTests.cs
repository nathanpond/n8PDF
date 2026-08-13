using System.Diagnostics;
using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Text;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests Arabic: letters that change shape according to what stands beside them, and pairs that
/// may not be written as two.
/// </summary>
/// <remarks>
/// An Arabic letter is the same character in all four of its shapes. Which shape is drawn depends
/// on whether its neighbours join — most letters join on both sides, a handful only on the right,
/// and a mark between two letters must not break the join at all. A converter that draws the
/// letters as it finds them produces a row of disconnected shapes: legible to nobody, and wrong in
/// the way a word spelled with the wrong letters is wrong rather than the way an ugly line is.
///
/// HarfBuzz is the reference again. It shapes Arabic for nearly everything that draws it, so the
/// check is not a list of words somebody thought of but the glyphs themselves, compared one by one
/// for every word here.
/// </remarks>
public class ArabicTests(ITestOutputHelper output)
{
    private const string Arial = "/System/Library/Fonts/Supplemental/Arial.ttf";

    private static TrueTypeFont Font() => TrueTypeFont.Load(File.ReadAllBytes(Arial));

    /// <summary>The glyphs HarfBuzz makes of a word, in the order it is read.</summary>
    private static List<ushort>? HarfBuzz(string word)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("hb-shape",
                [$"--font-file={Arial}", "--no-glyph-names", word])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(20_000);

            if (output.Length == 0) return null;

            var glyphs = output
                .Trim('[', ']')
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(piece => ushort.Parse(piece[..piece.IndexOf('=')]))
                .ToList();

            // HarfBuzz writes the line in the order it is drawn, which for Arabic is the reverse
            // of the order it is read.
            glyphs.Reverse();

            return glyphs;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// The same, with what HarfBuzz does with each glyph as well as which it is: the offset it is
    /// drawn at and the advance after it, in the order they are drawn.
    /// </summary>
    private static List<string>? HarfBuzzPositions(string word)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("hb-shape",
                [$"--font-file={Arial}", "--no-glyph-names", "--direction=rtl", word])
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

    /// <summary>
    /// Every glyph of a word, against HarfBuzz's answer for the same word in the same face.
    /// </summary>
    [Theory]
    [InlineData("سلام")]          // salam
    [InlineData("مرحبا")]         // marhaba
    [InlineData("العربية")]       // al-arabiyya, which has the article and a closing ta marbuta
    [InlineData("لا")]            // lam-alef, which is one glyph and not two
    [InlineData("الله")]          // the same ligature inside a word
    [InlineData("كتاب")]          // kitab: alef joins on the right only, so the word breaks inside
    [InlineData("دار")]           // dar: two letters that join only on the right
    [InlineData("بِسْمِ")]           // with vowel marks, which must not break the joins
    [InlineData("اللَّهِ")]           // the name of God: one glyph, and the letters reach across two marks
    public void The_glyphs_are_the_glyphs_harfbuzz_chooses(string word)
    {
        var theirs = HarfBuzz(word);

        if (theirs is null)
        {
            output.WriteLine("hb-shape was not found, so the shaping was not compared.");
            return;
        }

        var ours = TextShaper.Shape(Font(), word).Glyphs.Select(glyph => glyph.Glyph).ToList();

        output.WriteLine($"{word}  ours {string.Join(",", ours)}  them {string.Join(",", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>
    /// The shapes themselves, said plainly: a letter alone, opening a word, inside one and closing
    /// one can be four different glyphs of the same character.
    /// </summary>
    /// <remarks>
    /// Heh is used rather than beh because a font need not draw four shapes where two will do:
    /// Arial's beh opens and continues a word with the same glyph, which HarfBuzz agrees about.
    /// Heh is the letter whose four are four.
    /// </remarks>
    [Fact]
    public void One_letter_has_four_shapes()
    {
        var font = Font();

        const string heh = "ه";

        var alone = TextShaper.Shape(font, heh).Glyphs[0].Glyph;
        var opening = TextShaper.Shape(font, heh + heh).Glyphs[0].Glyph;
        var inside = TextShaper.Shape(font, heh + heh + heh).Glyphs[1].Glyph;
        var closing = TextShaper.Shape(font, heh + heh).Glyphs[1].Glyph;

        output.WriteLine($"alone {alone}, opening {opening}, inside {inside}, closing {closing}");

        Assert.Equal(4, new[] { alone, opening, inside, closing }.Distinct().Count());
    }

    /// <summary>
    /// A letter that joins only on the right ends a word in the middle of itself: what follows it
    /// begins a new shape, though no space came between them.
    /// </summary>
    [Fact]
    public void A_letter_that_joins_on_one_side_breaks_the_word()
    {
        const string alef = "ا";
        const string beh = "ب";

        var forms = ArabicJoining.Forms(beh + alef + beh);

        Assert.Equal(JoiningForm.Initial, forms[0]);   // beh joins forward to the alef
        Assert.Equal(JoiningForm.Final, forms[1]);     // the alef reaches back and no further
        Assert.Equal(JoiningForm.Isolated, forms[2]);  // and the beh after it stands alone
    }

    /// <summary>
    /// A mark stands between two letters without breaking the join between them, which is what the
    /// joining type "transparent" means and the only thing that makes pointed Arabic possible.
    /// </summary>
    [Fact]
    public void A_mark_between_two_letters_does_not_break_the_join()
    {
        const string beh = "ب";
        const string fatha = "َ";

        var forms = ArabicJoining.Forms(beh + fatha + beh);

        Assert.Equal(JoiningForm.Initial, forms[0]);
        Assert.Equal(JoiningForm.Final, forms[2]);
    }

    /// <summary>
    /// Lam followed by alef is written as one glyph. Drawing the two apart is a spelling mistake
    /// rather than a matter of taste.
    /// </summary>
    [Fact]
    public void Lam_and_alef_are_written_as_one()
    {
        var shaped = TextShaper.Shape(Font(), "لا");

        Assert.Single(shaped.Glyphs);

        // And what it stands for can still be found: the glyph names the first character it came
        // from, so the text is still searchable.
        Assert.Equal(0, shaped.Glyphs[0].Cluster);
    }

    /// <summary>
    /// The whole way through: a page of Arabic reads right to left, its letters joined.
    /// </summary>
    /// <summary>
    /// Every glyph of a word placed as well as chosen, against HarfBuzz's answer for the same
    /// word in the same face: the glyph, how far the pen moves after it, and how far the glyph
    /// itself is moved from where the pen stands.
    /// </summary>
    /// <remarks>
    /// The glyphs alone say nothing about where the vowels go, and where they go is most of what
    /// makes a line of pointed Arabic look like Arabic. Compared in the order they are drawn,
    /// which is what the two are asked for here, so that the offsets are the ones that reach a
    /// page rather than an intermediate reading of them.
    /// </remarks>
    [Theory]
    [InlineData("مرحبا")]          // no marks: every offset should be nought
    [InlineData("بِسْمِ")]           // three marks on three letters
    [InlineData("اللَّهِ")]           // marks on a shape standing for four letters
    public void The_glyphs_are_placed_where_harfbuzz_places_them(string word)
    {
        var theirs = HarfBuzzPositions(word);

        if (theirs is null)
        {
            output.WriteLine("hb-shape was not found, so the placing was not compared.");
            return;
        }

        var ours = TextShaper.Shape(Font(), word, applyKerning: false, rightToLeft: true).Glyphs
            .Select(glyph => $"{glyph.Glyph}@{glyph.XOffset},{glyph.YOffset}+{glyph.Advance}")
            .ToList();

        output.WriteLine($"{word}\n  ours {string.Join(" ", ours)}\n  them {string.Join(" ", theirs)}");

        Assert.Equal(theirs, ours);
    }

    /// <summary>
    /// A ligature is matched across the marks where the font says to look past them, and not
    /// where it does not.
    /// </summary>
    /// <remarks>
    /// Both answers are in the same font and both are needed. The lookup that writes lam, lam and
    /// heh as the one shape of the name of God is flagged to ignore marks, and must reach across
    /// the shadda and the vowel written over those letters — a word spelled with the marks left in
    /// is drawn as seven shapes where it should be three. The lookup that combines a shadda with
    /// the vowel beside it is not flagged, and is matching marks: skipping them would leave it
    /// nothing to match. Reading the flag is what tells the two apart; a shaper that always
    /// skipped marks would lose the second, and one that never did would lose the first.
    /// </remarks>
    [Fact]
    public void A_ligature_reaches_across_the_marks_where_the_font_says_it_may()
    {
        var font = Font();

        // Lam, lam and heh with a shadda and a kasra between them.
        var glyphs = TextShaper.Shape(font, "اللَّهِ").Glyphs;

        var letters = glyphs.Where(glyph => !font.IsMark(glyph.Glyph)).ToList();
        var marks = glyphs.Where(glyph => font.IsMark(glyph.Glyph)).ToList();

        output.WriteLine($"{glyphs.Count} glyphs: {string.Join(" ", glyphs.Select(g => g.Glyph))}");

        // One shape for the four letters, and the marks they carried still there.
        Assert.Single(letters);
        Assert.NotEmpty(marks);

        // The two marks over the lam are one glyph: the font holds shadda-with-fatha as a single
        // mark, and combining them is a ligature of marks that a mark-skipping match cannot make.
        var shadda = TextShaper.Shape(font, "\u064E\u0651").Glyphs;

        Assert.Single(shadda);
        Assert.True(font.IsMark(shadda[0].Glyph));
    }

    [Fact]
    public void Arabic_reaches_the_page_joined_and_right_to_left()
    {
        const string run = "<w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\" w:cs=\"Arial\"/><w:sz w:val=\"28\"/>";

        var builder = new DocxBuilder().AddRawParagraph(
            $"<w:p><w:pPr><w:bidi/></w:pPr><w:r><w:rPr>{run}</w:rPr>" +
            "<w:t xml:space=\"preserve\">سلام مرحبا</w:t></w:r></w:p>");

        using var stream = builder.BuildStream();
        var page = Converter.LayoutDocument(stream, new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }).Pages[0];

        var pieces = page.Lines.SelectMany(line => line.Texts).OrderBy(piece => piece.X).ToList();

        Assert.NotEmpty(pieces);

        // A right-to-left paragraph, so the text sits against the right margin.
        Assert.True(pieces[^1].X + pieces[^1].Width > 500, "the line does not reach the right margin");

        // And the glyphs drawn are the joined ones rather than the letters as they were typed:
        // the first word's first letter is drawn in its final shape, not its isolated one.
        var font = TestFonts.CreatePinnedLibrary().Resolve("Arial").Font;

        var joined = TextShaper.Shape(font, "سلام").Glyphs[0].Glyph;
        var alone = TextShaper.Shape(font, "س").Glyphs[0].Glyph;

        Assert.NotEqual(alone, joined);
    }

    /// <summary>
    /// The whole of it against Word: joined words, a lam-alef, a line of vowel marks, and Arabic
    /// with Latin and digits beside it.
    /// </summary>
    /// <remarks>
    /// Every line begins where Word begins it, and the glyphs along it stand where Word stands
    /// them — checked run by run while this was written, and matching to a hundredth of a point
    /// for the letters and to under a point for the marks.
    ///
    /// What is not compared is the text. Word writes Arabic as the presentation forms, one glyph
    /// per run, so a line comes back as a string of characters from U+FE70 upwards where this
    /// converter's comes back as the letters that were typed; and the name of God, which Word
    /// draws as the one glyph the font holds for it, comes back from Word's own file as the letter
    /// J. The two files say the same thing and spell it differently, which is a fact about how
    /// Word encodes Arabic rather than about where anything is drawn.
    ///
    /// The widths of the two joined lines differ by one letter's width for a duller reason: the
    /// last run of each of Word's lines carries no widths this reader can find, so the run is
    /// measured as nothing and the line as one glyph short. Its origin is where ours ends, which
    /// is how that was told from a real disagreement.
    /// </remarks>
    [Fact]
    public void The_fixture_lines_begin_where_word_begins_them()
    {
        var reference = Path.Combine(TestPaths.ReferencePdfs, "arabic.pdf");
        Assert.True(File.Exists(reference), $"No Word reference PDF at {reference}");

        var report = Support.PdfReading.PdfLineComparison.Compare("arabic",
            Converter.Convert(Fixtures.Build("arabic"),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }),
            File.ReadAllBytes(reference));

        output.WriteLine(report.ToText());

        Assert.Equal(0, report.UnmatchedCount);
        Assert.True(report.MaxAbsStartXDelta < 0.5,
            $"a line begins {report.MaxAbsStartXDelta:0.###}pt from where Word begins it");
    }
}
