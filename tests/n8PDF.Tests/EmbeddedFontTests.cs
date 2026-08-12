using System.Text;
using n8PDF.Pdf;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tier 2 tests for embedding real fonts as Type0/CIDFontType2, the path every glyph on every
/// converted page goes through.
/// </summary>
public class EmbeddedFontTests
{
    [Fact]
    public void Encoding_produces_two_bytes_of_glyph_index_per_character()
    {
        var builder = new PdfBuilder();
        var font = builder.UseFont(TestFonts.Load(TestFonts.TimesNewRomanPath));

        var encoded = font.Encode("AB");

        Assert.Equal(4, encoded.Length);

        var glyphs = font.MapToGlyphs("AB");
        Assert.Equal(glyphs[0], (ushort)((encoded[0] << 8) | encoded[1]));
        Assert.Equal(glyphs[1], (ushort)((encoded[2] << 8) | encoded[3]));
    }

    [Fact]
    public void Surrogate_pairs_encode_to_a_single_glyph()
    {
        var builder = new PdfBuilder();
        var font = builder.UseFont(TestFonts.Load(TestFonts.ArialPath));

        // U+1D400 is outside the BMP, so it arrives as two chars but is one code point and must
        // map to one glyph, not two.
        const string beyondBmp = "\U0001D400";
        Assert.Equal(2, beyondBmp.Length);

        Assert.Single(font.MapToGlyphs(beyondBmp));
        Assert.Equal(2, font.Encode(beyondBmp).Length);
    }

    [Fact]
    public void Glyph_widths_convert_to_text_space_thousandths()
    {
        var typeface = TestFonts.Load(TestFonts.TimesNewRomanPath);
        var builder = new PdfBuilder();
        var font = builder.UseFont(typeface);

        // 'M' is 1821 design units in a 2048-unit em, which is 889 thousandths — the published
        // Times Roman width.
        var width = font.GetGlyphWidth1000(typeface.GetGlyphIndex('M'));
        Assert.Equal(889, width, 0);

        var space = font.GetGlyphWidth1000(typeface.GetGlyphIndex(' '));
        Assert.Equal(250, space, 0);
    }

    [Fact]
    public void Embedded_font_produces_a_complete_object_graph()
    {
        var builder = new PdfBuilder { Title = "Embedded font smoke test" };
        var page = builder.AddPage(612, 792);
        var font = builder.UseFont(TestFonts.Load(TestFonts.TimesNewRomanPath));

        page.Content.BeginText()
            .SetFont(font.ResourceName, 24)
            .SetTextPosition(72, 700)
            .ShowGlyphs(font.Encode("Embedded Times New Roman"))
            .EndText();

        var bytes = builder.ToArray();
        var text = Encoding.Latin1.GetString(bytes);

        Assert.Contains("/Subtype /Type0", text);
        Assert.Contains("/Encoding /Identity-H", text);
        Assert.Contains("/Subtype /CIDFontType2", text);
        Assert.Contains("/CIDToGIDMap /Identity", text);
        Assert.Contains("/FontFile2", text);
        Assert.Contains("/Length1", text);
        Assert.Contains("/ToUnicode", text);
        Assert.Contains("/Ordering (Identity)", text);

        // The descriptor must claim Nonsymbolic (bit 6, value 32) for a text font, and must not
        // also claim Symbolic (bit 3, value 4).
        Assert.Contains("/Flags 34", text);
    }

    [Fact]
    public void Only_used_glyphs_appear_in_the_width_array()
    {
        var builder = new PdfBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.UseFont(TestFonts.Load(TestFonts.TimesNewRomanPath));

        page.Content.BeginText().SetFont(font.ResourceName, 12).SetTextPosition(72, 700)
            .ShowGlyphs(font.Encode("abc")).EndText();

        // Three distinct characters were shown, so three glyphs should be described even though
        // the embedded program holds thousands.
        Assert.Equal(3, font.UsedGlyphCount);

        var text = Encoding.Latin1.GetString(builder.ToArray());
        var widthsStart = text.IndexOf("/W [", StringComparison.Ordinal);
        Assert.True(widthsStart > 0, "expected a /W array");

        var widths = text[widthsStart..(text.IndexOf("/CIDToGIDMap", widthsStart, StringComparison.Ordinal))];
        Assert.Contains("[", widths);
    }

    [Fact]
    public void ToUnicode_maps_glyphs_back_to_their_characters()
    {
        var typeface = TestFonts.Load(TestFonts.TimesNewRomanPath);
        var builder = new PdfBuilder();
        var page = builder.AddPage(612, 792);
        var font = builder.UseFont(typeface);

        page.Content.BeginText().SetFont(font.ResourceName, 12).SetTextPosition(72, 700)
            .ShowGlyphs(font.Encode("A")).EndText();

        // The ToUnicode stream is compressed in the output, so check the mapping by decoding it.
        var text = Encoding.Latin1.GetString(builder.ToArray());
        Assert.Contains("/ToUnicode", text);

        var glyph = typeface.GetGlyphIndex('A');
        var cmap = ExtractDecodedStreams(builder.ToArray())
            .FirstOrDefault(s => s.Contains("beginbfchar", StringComparison.Ordinal));

        Assert.NotNull(cmap);
        Assert.Contains($"<{glyph:X4}> <0041>", cmap);
        Assert.Contains("/CMapType 2 def", cmap);
    }

    [Fact]
    public void Font_registry_reuses_one_resource_per_face()
    {
        var builder = new PdfBuilder();
        var typeface = TestFonts.Load(TestFonts.TimesNewRomanPath);

        var first = builder.UseFont(typeface);
        var second = builder.UseFont(typeface);
        var other = builder.UseFont(TestFonts.Load(TestFonts.ArialPath));

        Assert.Same(first, second);
        Assert.Equal("F1", first.ResourceName);
        Assert.Equal("F2", other.ResourceName);
    }

    [Fact]
    public void Embedded_font_renders_selectable_text_on_a_real_page()
    {
        var builder = new PdfBuilder { Title = "n8PDF embedded font" };
        var page = builder.AddPage(612, 792);

        var regular = builder.UseFont(TestFonts.Load(TestFonts.TimesNewRomanPath));
        var bold = builder.UseFont(TestFonts.Load(TestFonts.TimesNewRomanBoldPath));
        var italic = builder.UseFont(TestFonts.Load(TestFonts.TimesNewRomanItalicPath));

        var y = 720.0;
        foreach (var (font, label) in new[]
                 {
                     (regular, "Regular — the quick brown fox jumps over the lazy dog"),
                     (bold, "Bold — the quick brown fox jumps over the lazy dog"),
                     (italic, "Italic — the quick brown fox jumps over the lazy dog")
                 })
        {
            page.Content.BeginText()
                .SetFont(font.ResourceName, 14)
                .SetTextPosition(72, y)
                .ShowGlyphs(font.Encode(label))
                .EndText();
            y -= 24;
        }

        var bytes = builder.ToArray();
        var path = TestPaths.WriteArtifact("embedded-fonts.pdf", bytes);

        Assert.True(new FileInfo(path).Length > 10_000, "an embedded font program should dominate the file size");
        Assert.Equal(1, builder.Document.PageCount);
    }

    /// <summary>
    /// Pulls every Flate-compressed stream out of a PDF and decodes it. Enough of a reader to
    /// assert on stream contents without a full parser.
    /// </summary>
    private static List<string> ExtractDecodedStreams(byte[] pdf)
    {
        var text = Encoding.Latin1.GetString(pdf);
        var results = new List<string>();
        var index = 0;

        // Anchored on the dictionary's closing ">>" because a bare "stream\n" search also
        // matches the tail of "endstream".
        const string marker = ">>\nstream\n";

        while (true)
        {
            var start = text.IndexOf(marker, index, StringComparison.Ordinal);
            if (start < 0) break;

            var dataStart = start + marker.Length;
            var end = text.IndexOf("\nendstream", dataStart, StringComparison.Ordinal);
            if (end < 0) break;

            var data = new byte[end - dataStart];
            Array.Copy(pdf, dataStart, data, 0, data.Length);

            try
            {
                results.Add(Encoding.Latin1.GetString(PdfFilters.FlateDecode(data)));
            }
            catch (InvalidDataException)
            {
                // Not Flate-compressed: a short stream that deflate would have grown, so it was
                // stored verbatim. Still worth inspecting.
                results.Add(Encoding.Latin1.GetString(data));
            }

            index = end + 1;
        }

        return results;
    }
}
