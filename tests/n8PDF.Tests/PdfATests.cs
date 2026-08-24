using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// PDF/A-2b (#68): opt-in, declared in XMP that agrees with the information dictionary, an sRGB
/// output intent, an identifier in the trailer — and validated by veraPDF, the tool an archive
/// would actually run.
/// </summary>
public class PdfATests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] Convert(string fixture, bool pdfA) =>
        n8PDF.Converter.Convert(Fixtures.Build(fixture), new n8PDF.ConversionOptions
        {
            Fonts = TestFonts.CreatePinnedLibrary(),
            PdfA = pdfA,
            Title = "The probe"
        });

    /// <summary>Opting in adds the declaration, the intent and the identifier.</summary>
    [Fact]
    public void Opting_in_carries_the_declaration()
    {
        var reader = new PdfFileReader(Convert("outline-probe", pdfA: true));
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));

        var metadata = reader.Resolve(reader.GetEntry(root, "Metadata"));
        Assert.True(metadata is PdfStreamValue, "no /Metadata stream");

        var xmp = System.Text.Encoding.UTF8.GetString(((PdfStreamValue)metadata).RawData);
        Assert.Contains("<pdfaid:part>2</pdfaid:part>", xmp, StringComparison.Ordinal);
        Assert.Contains("<pdfaid:conformance>B</pdfaid:conformance>", xmp, StringComparison.Ordinal);
        Assert.Contains(">The probe<", xmp, StringComparison.Ordinal);

        var intents = reader.Resolve(reader.GetEntry(root, "OutputIntents")) as PdfArrayValue;
        Assert.True(intents is { Items.Count: 1 }, "no output intent");

        var intent = (PdfDictValue)reader.Resolve(intents!.Items[0]);
        var profile = reader.Resolve(reader.GetEntry(intent, "DestOutputProfile"));
        Assert.True(profile is PdfStreamValue, "the intent names no profile");

        var id = reader.Resolve(reader.GetEntry(reader.Trailer, "ID")) as PdfArrayValue;
        Assert.True(id is { Items.Count: 2 }, "no /ID pair in the trailer");
    }

    /// <summary>Off by default: a plain conversion carries none of it.</summary>
    [Fact]
    public void The_default_carries_none_of_it()
    {
        var reader = new PdfFileReader(Convert("outline-probe", pdfA: false));
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));

        Assert.Null(reader.GetEntry(root, "Metadata"));
        Assert.Null(reader.GetEntry(root, "OutputIntents"));
        Assert.Null(reader.GetEntry(reader.Trailer, "ID"));
    }

    /// <summary>The identifier comes from the body, so the output stays byte-reproducible.</summary>
    [Fact]
    public void The_output_stays_byte_reproducible()
    {
        Assert.Equal(Convert("outline-probe", pdfA: true), Convert("outline-probe", pdfA: true));
    }

    /// <summary>
    /// veraPDF — the validator an archive would run — passes the claim, on a plain document and
    /// on one that leans on transparency, which is why the level is 2b and not 1b.
    /// </summary>
    [Theory]
    [InlineData("outline-probe")]
    [InlineData("watermark")]
    [InlineData("shape-fill-probe")]
    public void Verapdf_validates_the_claim(string fixture)
    {
        if (!VeraPdfTool.IsAvailable)
        {
            Assert.False(VeraPdfTool.IsRequired, VeraPdfTool.UnavailableMessage);
            _output.WriteLine(VeraPdfTool.UnavailableMessage);
            return;
        }

        if (TestFonts.SkipForMissingFonts(fixture)) return;

        var path = TestPaths.WriteArtifact(fixture + ".pdfa.pdf", Convert(fixture, pdfA: true));
        var result = VeraPdfTool.Validate(path);

        _output.WriteLine(result.Output.Length > 4000 ? result.Output[..4000] : result.Output);
        Assert.True(result.Compliant, $"veraPDF rejects '{fixture}' as PDF/A-2b");
    }
}
