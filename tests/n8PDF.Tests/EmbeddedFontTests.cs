using n8PDF.Fonts;
using n8PDF.Ooxml;
using n8PDF.Packaging;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The faces a document carries with it (#62): <c>w:embedRegular</c>, the obfuscation undone,
/// and the embedded face beating whatever the machine has of the same name.
/// </summary>
/// <remarks>
/// The probe face (tools/make-embed-font.py) sets every letter as a solid block one em wide, so
/// ten letters at 24pt are 240pt of line — half again anything a substitute would give — and the
/// width alone says which face set the text. Word's own render of the fixture is held to ours by
/// the generic comparison tiers, which line the runs up against the reference PDF.
/// </remarks>
public class EmbeddedFontTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string FontTableType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml";

    private const string ObfuscatedFontType =
        "application/vnd.openxmlformats-officedocument.obfuscatedFont";

    private static ExtractedTextRun ProbeLine(byte[] pdf) =>
        PdfTextExtractor.Extract(pdf).First(r => r.Text.StartsWith("HHHH", StringComparison.Ordinal));

    /// <summary>The four steps of the read, checked at the seam: part found, key undone, bytes intact.</summary>
    [Fact]
    public void The_obfuscation_is_undone_and_the_bytes_come_back_intact()
    {
        var docx = Fixtures.Build("embedded-font-probe");
        using var package = OpcPackage.Open(new MemoryStream(docx), leaveOpen: false);

        var fonts = EmbeddedFonts.Read(package, package.GetMainDocumentPartName(), new PackageLimits());

        Assert.Single(fonts);
        Assert.Equal(Fixtures.ProbeFont(), fonts[0]);
    }

    /// <summary>
    /// The conversion sets the probe line in the embedded face: ten block letters at 24pt are
    /// 240pt of line, which no substitute comes near.
    /// </summary>
    [Fact]
    public void The_embedded_face_sets_the_line_at_an_em_per_letter()
    {
        var pdf = n8PDF.Converter.Convert(Fixtures.Build("embedded-font-probe"),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var line = ProbeLine(pdf);
        _output.WriteLine($"the probe line is {line.Width:0.00}pt wide");

        Assert.InRange(line.Width - line.TrailingWhitespaceWidth, 239.0, 241.0);
    }

    /// <summary>Put back wrong — the same text with no embedding — the line is a substitute's.</summary>
    [Fact]
    public void Without_the_embedding_the_line_is_set_in_a_substitute()
    {
        var docx = new DocxBuilder()
            .AddParagraph("HHHHHHHHHH",
                runProperties: DocxBuilder.RunProperties(font: "n8PDF Probe", halfPoints: 48))
            .Build();

        var pdf = n8PDF.Converter.Convert(docx,
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var line = ProbeLine(pdf);
        _output.WriteLine($"unembedded, the line is {line.Width:0.00}pt wide");

        Assert.True(line.Width < 220,
            $"the line is {line.Width:0.00}pt wide, which is the embedded width — the fallback " +
            "was expected here, so the probe can no longer tell the two apart");
    }

    /// <summary>
    /// An embedded face outranks a registered face of the same name: the narrow sibling loses to
    /// the embedded block face even though it was there first.
    /// </summary>
    [Fact]
    public void An_embedded_face_outranks_a_face_of_the_same_name()
    {
        var library = new FontLibrary { UseSystemFonts = false };
        library.Register(Fixtures.NarrowProbeFont());

        var narrow = library.Resolve("n8PDF Probe");
        Assert.Equal(500, narrow.Font.GetAdvanceWidth(narrow.Font.GetGlyphIndex('H')));

        var overlay = new FontLibrary(library);
        overlay.RegisterEmbedded(Fixtures.ProbeFont());

        var chosen = overlay.Resolve("n8PDF Probe");
        Assert.Equal(1000, chosen.Font.GetAdvanceWidth(chosen.Font.GetGlyphIndex('H')));

        // And the caller's library did not learn the document's face.
        var still = library.Resolve("n8PDF Probe");
        Assert.Equal(500, still.Font.GetAdvanceWidth(still.Font.GetGlyphIndex('H')));
    }

    /// <summary>A font past the byte limit is left out, and the conversion proceeds without it.</summary>
    [Fact]
    public void A_font_past_the_byte_limit_is_left_out()
    {
        var docx = Fixtures.Build("embedded-font-probe");

        using (var package = OpcPackage.Open(new MemoryStream(docx)))
        {
            Assert.Empty(EmbeddedFonts.Read(package, package.GetMainDocumentPartName(),
                new PackageLimits { MaximumFontBytes = 100 }));
        }

        var pdf = n8PDF.Converter.Convert(docx, new n8PDF.ConversionOptions
        {
            Fonts = TestFonts.CreatePinnedLibrary(),
            Limits = new PackageLimits { MaximumFontBytes = 100 }
        });

        var line = ProbeLine(pdf);
        _output.WriteLine($"capped at 100 bytes, the line is {line.Width:0.00}pt wide");

        Assert.True(line.Width < 220, "the capped font was still embedded");
    }

    /// <summary>A face the SFNT parser refuses is left out rather than taking the document with it.</summary>
    [Fact]
    public void A_face_that_will_not_parse_does_not_abort_the_conversion()
    {
        var garbage = new byte[64];
        garbage[1] = 0x01; // 0x00010000, the sfnt magic, over a table directory of zeros

        var docx = new DocxBuilder()
            .WithBinaryPart("word/fonts/font1.odttf", ObfuscatedFontType, garbage)
            .WithPart("word/fontTable.xml", FontTableType,
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:fonts xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<w:font w:name=\"Broken\"><w:embedRegular r:id=\"rIdFont1\"/></w:font></w:fonts>",
                fromDocument: ("rIdFontTable",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable"),
                own: [("rIdFont1",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/font",
                    "fonts/font1.odttf")])
            .AddParagraph("Still converts.")
            .Build();

        var pdf = n8PDF.Converter.Convert(docx,
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        Assert.Contains(PdfTextExtractor.Extract(pdf), r => r.Text.Contains("Still converts"));
    }
}
