using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests font subsetting: embedding the outlines a document draws and leaving out the rest, while
/// the font that comes back out still says the same things about the glyphs that survived.
/// </summary>
public class SubsettingTests
{
    private const string Times12 =
        "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/><w:sz w:val=\"24\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static TrueTypeFont Times() =>
        TestFonts.CreatePinnedLibrary().Resolve("Times New Roman", false, false).Font;

    private static List<ushort> GlyphsOf(TrueTypeFont font, string text) =>
        text.Select(c => font.GetGlyphIndex(c)).Distinct().ToList();

    [Fact]
    public void A_subset_is_a_fraction_of_the_face_it_came_from()
    {
        var font = Times();

        var whole = font.GetEmbeddableFontProgram();
        var subset = font.GetEmbeddableFontProgram(GlyphsOf(font, "A single line of text."));

        Assert.True(subset.Length * 4 < whole.Length,
            $"the subset is {subset.Length:N0} bytes against the whole face's {whole.Length:N0}");
    }

    /// <summary>
    /// The subset has to be a font in its own right, not just a smaller file: reading it back has
    /// to give the same answers about the glyphs it kept.
    /// </summary>
    [Fact]
    public void The_glyphs_it_keeps_are_unchanged()
    {
        var font = Times();
        var glyphs = GlyphsOf(font, "The quick brown fox, 1234.");

        var subset = TrueTypeFont.Load(font.GetEmbeddableFontProgram(glyphs));

        Assert.Equal(font.GlyphCount, subset.GlyphCount);

        foreach (var glyph in glyphs)
        {
            Assert.Equal(font.GetAdvanceWidth(glyph), subset.GetAdvanceWidth(glyph));
            AssertSameOutline(font, subset, glyph);
        }
    }

    [Fact]
    public void The_glyphs_it_drops_are_empty()
    {
        var font = Times();
        var glyphs = GlyphsOf(font, "A");

        var subset = TrueTypeFont.Load(font.GetEmbeddableFontProgram(glyphs));

        // A glyph the document never drew, which had an outline before and has none now.
        var unused = font.GetGlyphIndex('Z');

        Assert.DoesNotContain(unused, glyphs);
        Assert.NotEmpty(OutlineOf(font, unused));
        Assert.Empty(OutlineOf(subset, unused));
    }

    /// <summary>
    /// An accented letter is drawn as the letter and the accent placed together, so keeping it
    /// means keeping both — a subset that dropped the pieces would leave a hole where the glyph
    /// that needed them draws.
    /// </summary>
    [Fact]
    public void A_composite_glyph_keeps_what_it_is_built_from()
    {
        var font = Times();

        var composite = font.GetGlyphIndex('é');
        var components = ComponentsOf(font, composite);

        Assert.NotEmpty(components);

        var subset = TrueTypeFont.Load(font.GetEmbeddableFontProgram([composite]));

        foreach (var component in components)
        {
            Assert.NotEmpty(OutlineOf(subset, component));
            AssertSameOutline(font, subset, component);
        }
    }

    /// <summary>
    /// The glyph names go: nothing reads them here, and in a text face they are a third of what a
    /// subset would otherwise weigh. Version 3.0 is the one that says there are none.
    /// </summary>
    [Fact]
    public void The_glyph_names_are_dropped()
    {
        var font = Times();
        var subset = font.GetEmbeddableFontProgram(GlyphsOf(font, "Text"));

        var post = TableOf(subset, "post");

        Assert.Equal(32, post.Length);
        Assert.Equal(0x00030000u, ReadUInt32(post, 0));
    }

    /// <summary>
    /// A PDF names a subset with a six-letter tag so two documents carrying different parts of one
    /// face are not taken for the same font.
    /// </summary>
    [Fact]
    public void An_embedded_subset_is_named_with_a_tag()
    {
        var pdf = Converter.Convert(Fixtures.Build("single-line"), Options());
        var name = BaseFontsOf(pdf).Single();

        Assert.Matches("^[A-Z]{6}\\+", name);
        Assert.EndsWith("TimesNewRomanPSMT", name);
    }

    /// <summary>
    /// The tag stands for which glyphs the subset holds, so two documents that used different
    /// text get different tags and the same document converted twice gets the same one.
    /// </summary>
    [Fact]
    public void The_tag_follows_the_glyphs_and_nothing_else()
    {
        var once = BaseFontsOf(Converter.Convert(Fixtures.Build("single-line"), Options())).Single();
        var again = BaseFontsOf(Converter.Convert(Fixtures.Build("single-line"), Options())).Single();

        Assert.Equal(once, again);

        var other = BaseFontsOf(Converter.Convert(
            new DocxBuilder().AddParagraph("Wholly different letters", runProperties: Times12).Build(),
            Options())).Single();

        Assert.NotEqual(once, other);
    }

    [Fact]
    public void Subsetting_does_not_change_what_the_page_says()
    {
        // The widths in the file are the font's own, so the text has to measure the same as ever.
        var pdf = Converter.Convert(Fixtures.Build("wrapping"), Options());
        var runs = PdfTextExtractor.Extract(pdf);

        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.True(run.Width > 0));
    }

    /// <summary>
    /// Asserts a glyph came through unchanged. Outlines are written on four-byte boundaries, so
    /// one may be followed by up to three bytes of padding that were not there before.
    /// </summary>
    private static void AssertSameOutline(TrueTypeFont font, TrueTypeFont subset, ushort glyph)
    {
        var original = OutlineOf(font, glyph);
        var copied = OutlineOf(subset, glyph);

        Assert.True(copied.Length >= original.Length,
            $"glyph {glyph} lost {original.Length - copied.Length} bytes");

        Assert.Equal(original, copied[..original.Length]);
        Assert.All(copied[original.Length..], padding => Assert.Equal(0, padding));
        Assert.True(copied.Length - original.Length < 4, $"glyph {glyph} gained more than padding");
    }

    /// <summary>A glyph's outline bytes, or empty when it has none.</summary>
    private static byte[] OutlineOf(TrueTypeFont font, ushort glyph)
    {
        var glyf = font.Tables["glyf"];
        var loca = font.Tables["loca"];
        var head = font.Tables["head"];

        var source = font.SourceData;
        var longLoca = new BigEndianReader(source, head.Offset + 50).ReadInt16() != 0;

        var reader = new BigEndianReader(source, loca.Offset + (longLoca ? glyph * 4 : glyph * 2));
        var start = longLoca ? (int)reader.ReadUInt32() : reader.ReadUInt16() * 2;
        var end = longLoca ? (int)reader.ReadUInt32() : reader.ReadUInt16() * 2;

        if (end <= start) return [];

        var outline = new byte[end - start];
        Array.Copy(source, glyf.Offset + start, outline, 0, outline.Length);
        return outline;
    }

    /// <summary>The glyphs a composite is assembled from.</summary>
    private static List<ushort> ComponentsOf(TrueTypeFont font, ushort glyph)
    {
        var outline = OutlineOf(font, glyph);
        var components = new List<ushort>();

        if (outline.Length < 10) return components;

        var reader = new BigEndianReader(outline, 0);
        if (reader.ReadInt16() >= 0) return components;

        reader.Skip(8);

        while (true)
        {
            var flags = reader.ReadUInt16();
            components.Add(reader.ReadUInt16());

            reader.Skip((flags & 0x0001) != 0 ? 4 : 2);

            if ((flags & 0x0008) != 0) reader.Skip(2);
            else if ((flags & 0x0040) != 0) reader.Skip(4);
            else if ((flags & 0x0080) != 0) reader.Skip(8);

            if ((flags & 0x0020) == 0) break;
        }

        return components;
    }

    private static byte[] TableOf(byte[] program, string tag)
    {
        var reader = new BigEndianReader(program, 4);
        int count = reader.ReadUInt16();
        reader.Skip(6);

        for (var i = 0; i < count; i++)
        {
            var found = reader.ReadTag();
            reader.ReadUInt32();
            var offset = (int)reader.ReadUInt32();
            var length = (int)reader.ReadUInt32();

            if (found != tag) continue;

            var table = new byte[length];
            Array.Copy(program, offset, table, 0, length);
            return table;
        }

        return [];
    }

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);

    /// <summary>The base font name of every font the PDF embeds.</summary>
    private static List<string> BaseFontsOf(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        var names = new List<string>();

        foreach (var page in reader.GetPages())
        {
            if (reader.GetEntry(page.Resources, "Font") is not PdfDictValue fonts) continue;

            foreach (var (_, value) in fonts.Entries)
            {
                if (reader.Resolve(value) is PdfDictValue font &&
                    reader.Resolve(font.Get("BaseFont")) is PdfNameValue name)
                {
                    names.Add(name.Name);
                }
            }
        }

        return names.Distinct().ToList();
    }
}
