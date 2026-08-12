using n8PDF;
using n8PDF.Fonts;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests subsetting a PostScript-outline face, whose glyphs live in a <c>CFF </c> table rather
/// than in <c>glyf</c>.
/// </summary>
/// <remarks>
/// Every CFF font on this machine is for a script this converter cannot shape — Devanagari,
/// Gujarati, Chinese — so unlike every other feature here there is no fixture Word could be asked
/// to render. What can be checked is the font program itself, and it is checked by an
/// implementation that shares nothing with this one: fontTools reads the subset back and draws
/// its glyphs, which is a stronger statement than parsing it again with the code that wrote it.
/// </remarks>
public class CffSubsettingTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>A face with plain CFF outlines, and one that is CID-keyed with an FDArray.</summary>
    public static TheoryData<string, bool> Faces => new()
    {
        { "/System/Library/Fonts/KohinoorGujarati.ttc", false },
        { "/System/Library/Fonts/Hiragino Sans GB.ttc", true }
    };

    private static TrueTypeFont? Load(string path)
    {
        if (!File.Exists(path)) return null;

        var library = new FontLibrary { UseSystemFonts = false };
        library.RegisterFile(path);

        var family = library.RegisteredFamilies.FirstOrDefault();
        if (family is null) return null;

        var font = library.Resolve(family).Font;
        return font.HasCffOutlines ? font : null;
    }

    /// <summary>Glyphs spread across the font, avoiding the ends where a face is thinnest.</summary>
    private static List<ushort> Sample(TrueTypeFont font) =>
        [.. new[] { 3, 40, 41, font.GlyphCount / 4, font.GlyphCount / 2 }
            .Where(g => g > 0 && g < font.GlyphCount)
            .Select(g => (ushort)g)
            .Distinct()];

    [Theory]
    [MemberData(nameof(Faces))]
    public void A_subset_is_a_fraction_of_the_face_it_came_from(string path, bool cidKeyed)
    {
        if (Load(path) is not { } font)
        {
            _output.WriteLine($"No usable CFF face at {path}; nothing to subset.");
            return;
        }

        var whole = font.GetEmbeddableFontProgram();
        var subset = font.GetEmbeddableFontProgram(Sample(font));

        _output.WriteLine(
            $"{(cidKeyed ? "CID-keyed" : "plain")} {font.FamilyName}: " +
            $"{font.GlyphCount:N0} glyphs, {whole.Length:N0} bytes whole, {subset.Length:N0} subset");

        Assert.True(subset.Length * 2 < whole.Length,
            $"the subset is {subset.Length:N0} bytes against the whole face's {whole.Length:N0}");
    }

    /// <summary>
    /// The glyphs that were kept still draw exactly what they drew, and the ones that were not
    /// draw nothing. Executing them is what proves the subroutines they call still resolve — a
    /// charstring whose calls point at the wrong place parses perfectly and draws rubbish.
    /// </summary>
    [Theory]
    [MemberData(nameof(Faces))]
    public void The_glyphs_it_keeps_draw_what_they_drew(string path, bool cidKeyed)
    {
        if (Load(path) is not { } font)
        {
            _output.WriteLine($"No usable CFF face at {path}; nothing to subset.");
            return;
        }

        if (!FontToolsCheck.IsAvailable)
        {
            Assert.False(FontToolsCheck.IsRequired, FontToolsCheck.UnavailableMessage);
            _output.WriteLine(FontToolsCheck.UnavailableMessage);
            return;
        }

        var kept = Sample(font);
        var dropped = Dropped(font, kept);
        var asked = kept.Select(g => (int)g).Concat(dropped).ToList();

        var whole = FontToolsCheck.Read(font.GetEmbeddableFontProgram(), asked);
        var subset = FontToolsCheck.Read(font.GetEmbeddableFontProgram(kept), asked);

        Assert.NotNull(whole);
        Assert.NotNull(subset);

        // Glyph numbering is left alone, so the count cannot change: the codes in the content
        // stream are glyph indices and everything downstream is keyed by them.
        Assert.Equal(whole.Glyphs, subset.Glyphs);
        Assert.Equal(font.GlyphCount, subset.Glyphs);

        foreach (var glyph in kept)
        {
            Assert.True(subset.Drawn.TryGetValue(glyph, out var after), $"glyph {glyph} was not read back");
            Assert.Equal(whole.Drawn[glyph], after);
        }

        foreach (var glyph in dropped)
        {
            Assert.NotEqual("nothing", whole.Drawn[glyph]);
            Assert.Equal("nothing", subset.Drawn[glyph]);
        }

        _output.WriteLine(
            $"{(cidKeyed ? "CID-keyed" : "plain")} {font.FamilyName}: " +
            $"{kept.Count} glyphs kept and unchanged, {dropped.Count} emptied, of {subset.Glyphs:N0}");
    }

    /// <summary>
    /// The subroutines nothing reaches are emptied along with the glyphs.
    /// </summary>
    /// <remarks>
    /// This is only worth asserting beside the check that the kept glyphs still draw what they
    /// drew: emptying subroutines is easy, and emptying one that a glyph still calls would show
    /// there rather than here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Faces))]
    public void The_subroutines_nothing_reaches_are_emptied(string path, bool cidKeyed)
    {
        if (Load(path) is not { } font)
        {
            _output.WriteLine($"No usable CFF face at {path}; nothing to subset.");
            return;
        }

        if (!FontToolsCheck.IsAvailable)
        {
            Assert.False(FontToolsCheck.IsRequired, FontToolsCheck.UnavailableMessage);
            _output.WriteLine(FontToolsCheck.UnavailableMessage);
            return;
        }

        var whole = FontToolsCheck.Read(font.GetEmbeddableFontProgram(), []);
        var subset = FontToolsCheck.Read(font.GetEmbeddableFontProgram(Sample(font)), []);

        Assert.NotNull(whole);
        Assert.NotNull(subset);

        // The count cannot change either: a call names a subroutine by its position.
        Assert.Equal(whole.Subroutines, subset.Subroutines);
        Assert.True(subset.Subroutines > 0, "the face declares no subroutines to prune");

        _output.WriteLine(
            $"{(cidKeyed ? "CID-keyed" : "plain")} {font.FamilyName}: " +
            $"{subset.EmptySubroutines:N0} of {subset.Subroutines:N0} subroutines emptied, " +
            $"against {whole.EmptySubroutines:N0} in the whole face");

        // Five glyphs of a text face reach a handful of them at most.
        Assert.True(subset.EmptySubroutines > subset.Subroutines * 0.9,
            $"only {subset.EmptySubroutines:N0} of {subset.Subroutines:N0} were emptied");
    }

    /// <summary>A few glyphs that were not asked for and that draw something in the whole face.</summary>
    private static List<int> Dropped(TrueTypeFont font, List<ushort> kept) =>
        [.. new[] { font.GlyphCount / 3, font.GlyphCount / 5, font.GlyphCount - 2 }
            .Where(g => g > 0 && g < font.GlyphCount && !kept.Contains((ushort)g))
            .Distinct()];

    /// <summary>
    /// The whole way through: a document set in a CFF face, converted, with the font that comes
    /// out the other end a fraction of the one that went in and still readable.
    /// </summary>
    /// <remarks>
    /// Chinese is the one script here that needs no shaping — each character is its own glyph —
    /// so it is the only text that can exercise this end to end without asking the converter for
    /// something it cannot do.
    /// </remarks>
    [Fact]
    public void A_document_set_in_a_cff_face_embeds_a_subset()
    {
        const string path = "/System/Library/Fonts/Hiragino Sans GB.ttc";
        if (Load(path) is not { } font)
        {
            _output.WriteLine($"No usable CFF face at {path}; nothing to convert.");
            return;
        }

        var fonts = new FontLibrary { UseSystemFonts = false };
        fonts.RegisterFile(path);

        var docx = new DocxBuilder()
            .AddParagraph("汉字排版", runProperties: DocxBuilder.RunProperties(font.FamilyName, 24))
            .Build();

        var pdf = Converter.Convert(docx, new ConversionOptions { Fonts = fonts });

        var program = EmbeddedProgram(pdf);
        Assert.NotNull(program);

        _output.WriteLine(
            $"{font.FamilyName}: {program.Length:N0} bytes embedded, {font.GlyphCount:N0} glyphs in the face");

        // A twentieth of the face at most, now that its subroutines go with its outlines.
        var whole = font.GetEmbeddableFontProgram().Length;

        Assert.True(program.Length * 20 < whole,
            $"the embedded program is {program.Length:N0} bytes of the face's {whole:N0}");

        if (!QpdfTool.IsAvailable)
        {
            _output.WriteLine("qpdf was not found, so the file was not checked structurally.");
            return;
        }

        var result = QpdfTool.CheckBytes(pdf, "cff-subset");
        Assert.True(result.IsClean || result.HasWarningsOnly, result.Output);
    }

    /// <summary>The font program a PDF embeds, whichever kind of outline it holds.</summary>
    private static byte[]? EmbeddedProgram(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);

        foreach (var page in reader.GetPages())
        {
            if (reader.GetEntry(page.Resources, "Font") is not PdfDictValue fonts) continue;

            foreach (var (_, value) in fonts.Entries)
            {
                if (reader.Resolve(value) is not PdfDictValue font) continue;
                if (reader.Resolve(font.Get("DescendantFonts")) is not PdfArrayValue descendants) continue;
                if (reader.Resolve(descendants[0]) is not PdfDictValue descendant) continue;

                var descriptor = reader.Resolve(descendant.Get("FontDescriptor")) as PdfDictValue;

                foreach (var key in new[] { "FontFile2", "FontFile3" })
                {
                    if (reader.Resolve(descriptor?.Get(key)) is PdfStreamValue stream)
                        return reader.DecodeStream(stream);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A face whose outlines were nearly all used gains nothing from being rebuilt, and a rebuild
    /// that came out larger would be the wrong answer — so the whole table is kept instead.
    /// </summary>
    [Fact]
    public void A_subset_that_saves_nothing_is_not_used()
    {
        if (Load("/System/Library/Fonts/KohinoorGujarati.ttc") is not { } font)
        {
            _output.WriteLine("No usable CFF face; nothing to subset.");
            return;
        }

        var everything = Enumerable.Range(0, font.GlyphCount).Select(g => (ushort)g).ToList();

        var whole = font.GetEmbeddableFontProgram();
        var subset = font.GetEmbeddableFontProgram(everything);

        Assert.Equal(whole.Length, subset.Length);
    }
}
