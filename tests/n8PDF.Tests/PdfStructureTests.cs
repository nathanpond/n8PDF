using n8PDF;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Validates the PDFs we write with qpdf, an independent structural checker.
/// </summary>
/// <remarks>
/// Every other test proves the file says what we intended. This one proves the file is a valid
/// PDF: cross-reference offsets resolve, stream lengths agree with their data, and the object
/// graph is consistent. Those are precisely the things we hand-rolled, and precisely the things a
/// tolerant viewer will render correctly anyway — so a rendering check cannot substitute for it.
///
/// Install with <c>brew install qpdf</c>. Without it these tests report and skip; set
/// <c>N8PDF_REQUIRE_QPDF=1</c> to make the absence a failure instead.
/// </remarks>
public class PdfStructureTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    public static TheoryData<string> FixtureNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Fixtures.All.Keys) data.Add(name);
            return data;
        }
    }

    [Fact]
    public void Qpdf_is_available_or_explicitly_optional()
    {
        if (QpdfTool.IsAvailable)
        {
            _output.WriteLine($"qpdf found at {QpdfTool.Path}");
            return;
        }

        // The lesson from the reference PDFs: a silently skipped tier is indistinguishable from
        // a passing one. This states plainly that the coverage is absent.
        var message =
            "qpdf is not installed, so PDF structural validation is being skipped.\n" +
            "Install it with: brew install qpdf\n" +
            "Set N8PDF_REQUIRE_QPDF=1 to turn this skip into a failure (recommended in CI).";

        Assert.False(QpdfTool.IsRequired, message);
        _output.WriteLine(message);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Converted_fixture_passes_qpdf_check(string name)
    {
        if (!QpdfTool.IsAvailable)
        {
            Assert.False(QpdfTool.IsRequired, "qpdf is not installed; run 'brew install qpdf'.");
            return;
        }

        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };
        var result = QpdfTool.CheckBytes(Converter.Convert(Fixtures.Build(name), options), name);

        _output.WriteLine(result.Output);

        // Warnings are treated as failures. We control every byte of this writer, so anything
        // qpdf finds worth mentioning is worth fixing rather than tolerating.
        Assert.True(result.IsClean,
            $"""
             qpdf reported problems in the PDF produced for '{name}' (exit {result.ExitCode}):

             {result.Output}
             """);
    }

    [Fact]
    public void Word_reference_pdfs_also_pass_qpdf_check()
    {
        if (!QpdfTool.IsAvailable)
        {
            Assert.False(QpdfTool.IsRequired, "qpdf is not installed; run 'brew install qpdf'.");
            return;
        }

        var references = Directory.Exists(TestPaths.ReferencePdfs)
            ? Directory.GetFiles(TestPaths.ReferencePdfs, "*.pdf")
            : [];

        if (references.Length == 0) return;

        // A control, not a test of our code: if Word's own output also trips a qpdf complaint,
        // then that complaint says something about qpdf's strictness rather than about our
        // writer. Reported rather than asserted for exactly that reason.
        var unclean = 0;
        foreach (var path in references)
        {
            var result = QpdfTool.Check(path);
            if (result.IsClean) continue;

            unclean++;
            _output.WriteLine($"{Path.GetFileName(path)}: exit {result.ExitCode}\n{result.Output}\n");
        }

        _output.WriteLine($"{references.Length - unclean}/{references.Length} Word references are qpdf-clean.");
    }
}
