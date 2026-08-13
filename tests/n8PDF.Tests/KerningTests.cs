using n8PDF;
using n8PDF.Fonts;
using n8PDF.Layout;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;

namespace n8PDF.Tests;

/// <summary>
/// Tests kerning: reading the pairs a font declares, in either of the two places it can declare
/// them, and applying them only where the document asks for it.
/// </summary>
public class KerningTests
{
    private const string ZeroSpacing =
        "<w:spacing w:before=\"0\" w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/>";

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static LaidOutDocument LayoutOf(DocxBuilder builder)
    {
        using var stream = builder.BuildStream();
        return Converter.LayoutDocument(stream, Options());
    }

    private static LaidOutDocument LayoutOf(byte[] docx)
    {
        using var stream = new MemoryStream(docx);
        return Converter.LayoutDocument(stream, Options());
    }

    private static TrueTypeFont Font(string family) =>
        TestFonts.CreatePinnedLibrary().Resolve(family, false, false).Font;

    private static short Kerning(TrueTypeFont font, string pair) =>
        font.GetKerning(font.GetGlyphIndex(pair[0]), font.GetGlyphIndex(pair[1]));

    /// <summary>The width of a paragraph's only line.</summary>
    private static double WidthOf(string text, string runProperties)
    {
        var layout = LayoutOf(new DocxBuilder().AddParagraph(text, ZeroSpacing, runProperties));
        return layout.Pages[0].Lines[0].Texts.Sum(t => t.Width);
    }

    private static string Times(int halfPoints = 24, int? kern = null) =>
        DocxBuilder.RunProperties(
            font: "Times New Roman", halfPoints: halfPoints, kerningHalfPoints: kern);

    /// <summary>
    /// Calibri carries no legacy kern table, so anything it kerns it kerns through GPOS. If this
    /// stops finding pairs, GPOS reading has broken however well the other font behaves.
    /// </summary>
    [Fact]
    public void Calibri_kerns_through_gpos()
    {
        var positioning = PositioningOf(TestFonts.CalibriPath);

        Assert.NotNull(positioning);

        var font = Font("Calibri");
        var value = positioning.GetAdjustment(font.GetGlyphIndex('A'), font.GetGlyphIndex('V'));

        Assert.True(value < 0, $"AV came back as {value}");
        Assert.Equal(value, Kerning(font, "AV"));
    }

    /// <summary>
    /// Times New Roman's GPOS carries only mark positioning — no kern feature at all — so its
    /// kerning has to come from the legacy table. Both places have to be read to cover both fonts.
    /// </summary>
    [Fact]
    public void Times_kerns_through_the_legacy_table()
    {
        var positioning = PositioningOf(TestFonts.TimesNewRomanPath);

        // The table is there, and has plenty to say about where marks go.
        Assert.NotNull(positioning);

        var font = Font("Times New Roman");

        // It says nothing about kerning, though, so the pair has to come from the old table.
        Assert.Equal(0, positioning.GetAdjustment(font.GetGlyphIndex('A'), font.GetGlyphIndex('V')));
        Assert.True(Kerning(font, "AV") < 0);
    }

    /// <summary>Reads a font file's GPOS kerning directly, so the source of a pair is not in doubt.</summary>
    private static GlyphPositioning? PositioningOf(string path)
    {
        var data = File.ReadAllBytes(path);
        var reader = new BigEndianReader(data, 4);

        int tableCount = reader.ReadUInt16();
        reader.Skip(6);

        for (var i = 0; i < tableCount; i++)
        {
            var tag = reader.ReadTag();
            reader.ReadUInt32();
            var offset = (int)reader.ReadUInt32();
            var length = (int)reader.ReadUInt32();

            if (tag == "GPOS") return GlyphPositioning.Read(data, offset, length);
        }

        return null;
    }

    [Fact]
    public void Kerning_needs_the_document_to_ask_for_it()
    {
        const string text = "AV AW To Ta Wa Yo";

        Assert.Equal(WidthOf(text, Times()), WidthOf(text, Times(kern: 0)), 3);
        Assert.True(WidthOf(text, Times(kern: 16)) < WidthOf(text, Times()));
    }

    /// <summary>
    /// The value is the size at which kerning starts, not a switch: below it the same run is set
    /// exactly as it would be without kerning at all.
    /// </summary>
    [Fact]
    public void Kerning_starts_at_the_size_the_document_names()
    {
        const string text = "AVATAR";

        // Asked for from twenty-four point up.
        Assert.Equal(WidthOf(text, Times(24)), WidthOf(text, Times(24, kern: 48)), 3);
        Assert.True(WidthOf(text, Times(48, kern: 48)) < WidthOf(text, Times(48)));
    }

    [Fact]
    public void Kerning_narrows_the_pairs_a_font_declares()
    {
        var font = Font("Times New Roman");

        // Five pairs, four of which this font kerns.
        var units = Kerning(font, "AV") + Kerning(font, "VA") + Kerning(font, "AT") +
                    Kerning(font, "TA") + Kerning(font, "AR");

        var expected = font.Metrics.ToPoints(units, 12);

        Assert.Equal(expected, WidthOf("AVATAR", Times(kern: 16)) - WidthOf("AVATAR", Times()), 3);
    }

    /// <summary>
    /// A pair straddling a space is kerned like any other, which Word's own export shows it doing.
    /// Measurement splits its text at spaces, so this is the pair that is easiest to lose.
    /// </summary>
    [Fact]
    public void Pairs_spanning_a_space_are_kerned()
    {
        var font = Font("Times New Roman");

        Assert.True(Kerning(font, "V ") < 0, "the font does not kern V before a space");
        Assert.True(Kerning(font, " A") < 0, "the font does not kern a space before A");

        var units = Kerning(font, "V ") + Kerning(font, " A");
        var expected = font.Metrics.ToPoints(units, 12);

        // "V A" holds no pair except the two that straddle its space.
        Assert.Equal(expected, WidthOf("V A", Times(kern: 16)) - WidthOf("V A", Times()), 3);
    }

    /// <summary>
    /// A word opening a line has nothing before it to kern against, so the pair it would have
    /// made with the word it followed does not come with it.
    /// </summary>
    [Fact]
    public void A_word_opening_a_line_is_not_kerned_against_the_one_before_it()
    {
        // Wide enough that the last word wraps, and ending in a pair the font kerns.
        var builder = new DocxBuilder().AddParagraph(
            "Filler text written at some length so that it has to wrap, and ending in a letter " +
            "the font kerns against what follows it, which is a V " +
            "AVATAR",
            ZeroSpacing, Times(kern: 16));

        var layout = LayoutOf(builder);
        var lines = layout.Pages[0].Lines;

        Assert.True(lines.Count > 1, "the paragraph did not wrap");

        var second = lines[^1];
        var text = string.Concat(second.Texts.Select(t => t.Text));

        // Flush against the margin, and exactly as wide as the same words are on their own — a
        // line that kept the kern against the word it followed would come out narrower.
        Assert.Equal(72, second.Texts[0].X, 3);
        Assert.Equal(WidthOf(text, Times(kern: 16)), second.Texts.Sum(t => t.Width), 3);
    }

    /// <summary>
    /// What is drawn has to be spaced the way it was measured. Nothing in a PDF font can kern, so
    /// the adjustments have to reach the content stream — without them the text would be laid out
    /// tight and drawn loose.
    /// </summary>
    [Fact]
    public void The_drawn_text_is_kerned_too()
    {
        var docx = new DocxBuilder().AddParagraph("AVATAR", ZeroSpacing, Times(kern: 16)).Build();

        var expected = LayoutOf(docx).Pages[0].Lines[0].Texts.Sum(t => t.Width);
        var drawn = PdfTextExtractor.Extract(Converter.Convert(docx, Options()));

        var left = drawn.Min(r => r.X);
        var right = drawn.Max(r => r.X + r.Width);

        Assert.Equal(expected, right - left, 2);
    }

    [Fact]
    public void The_fixture_sets_the_same_text_four_ways()
    {
        var lines = LayoutOf(Fixtures.Build("kerning")).Pages[0].Lines
            .Select(l => l.Texts.Sum(t => t.Width))
            .ToList();

        // Kerned, then not asked for, then asked for above the size it is set at.
        Assert.True(lines[0] < lines[1], "the first line is no narrower than the unkerned one");
        Assert.Equal(lines[1], lines[2], 3);

        // Calibri kerned against Calibri not.
        Assert.True(lines[4] < lines[5], "the Calibri line is no narrower than the unkerned one");
    }
}
