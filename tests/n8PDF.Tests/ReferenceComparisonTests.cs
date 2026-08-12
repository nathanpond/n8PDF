using n8PDF;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tier 3: compares our output against PDFs exported from Word.
/// </summary>
/// <remarks>
/// This is the only tier that can say we match Word rather than merely match ourselves, so every
/// fixture is required to have a reference PDF. A fixture without one fails rather than skipping:
/// a silently skipped comparison looks identical to a passing one in CI, and the whole point of
/// this tier is that its absence should be impossible to overlook.
///
/// Generate the missing references with <c>tools/make-reference-pdfs.sh</c>.
/// </remarks>
public class ReferenceComparisonTests(ITestOutputHelper output)
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

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_has_a_reference_pdf(string name)
    {
        var path = ReferencePathFor(name);

        Assert.True(File.Exists(path),
            $"""
             No Word reference PDF for fixture '{name}'.

             Every fixture must have one: without it, this fixture is only ever compared against
             our own previous output, which cannot detect that we disagree with Word.

             Generate the missing references:
                 tools/make-reference-pdfs.sh

             Or export by hand: open tests/n8PDF.Tests/Fixtures/Minimal/{name}.docx in Word and
             save as PDF to {path}
             """);

        Assert.True(new FileInfo(path).Length > 0, $"The reference PDF for '{name}' is empty: {path}");
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Pagination_and_page_size_match_word(string name)
    {
        var path = ReferencePathFor(name);
        if (!File.Exists(path))
        {
            // Reported by Fixture_has_a_reference_pdf; failing twice for one cause adds noise.
            return;
        }

        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };
        var ours = PdfInspector.Inspect(Converter.Convert(Fixtures.Build(name), options));
        var theirs = PdfInspector.InspectFile(path);

        _output.WriteLine($"{name}: n8PDF {ours.PageCount} page(s), Word {theirs.PageCount} page(s)");

        Assert.True(ours.PageCount == theirs.PageCount,
            $"'{name}' paginated to {ours.PageCount} page(s) but Word produced {theirs.PageCount}.");

        for (var i = 0; i < Math.Min(ours.MediaBoxes.Count, theirs.MediaBoxes.Count); i++)
        {
            var (width, height) = ours.MediaBoxes[i];
            var (referenceWidth, referenceHeight) = theirs.MediaBoxes[i];

            // One point of tolerance absorbs the rounding Word applies to page dimensions.
            Assert.True(Math.Abs(width - referenceWidth) <= 1 && Math.Abs(height - referenceHeight) <= 1,
                $"'{name}' page {i + 1} is {width:0.##}x{height:0.##} but Word's is {referenceWidth:0.##}x{referenceHeight:0.##}.");
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Reference_pdf_holds_the_document_it_claims_to(string name)
    {
        var path = ReferencePathFor(name);
        if (!File.Exists(path)) return;

        // Word exports whichever document it considers current. When an earlier export failed and
        // left a document open, it silently exported that one instead and wrote it under this
        // fixture's name — a reference that looks entirely valid while describing a different
        // document. Comparing the text guards against that, and against a stale reference left
        // behind when a fixture's content changes.
        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };

        var ours = Normalize(PdfTextExtractor.Extract(Converter.Convert(Fixtures.Build(name), options)));
        var theirs = Normalize(PdfTextExtractor.ExtractFile(path));

        Assert.True(ours == theirs,
            $"""
             The reference PDF for '{name}' does not contain that fixture's text.
             Regenerate it: tools/make-reference-pdfs.sh --force

             expected: {Truncate(ours)}
             found:    {Truncate(theirs)}
             """);
    }

    /// <summary>
    /// Reduces a document to its characters with all whitespace removed. Word draws a trailing
    /// space for each paragraph mark and splits runs differently from us, so whitespace carries no
    /// information here — but the character sequence still identifies the document unambiguously,
    /// which is all this check needs.
    /// </summary>
    private static string Normalize(IEnumerable<ExtractedTextRun> runs) =>
        new(string.Concat(runs.Select(r => r.Text)).Where(c => !char.IsWhiteSpace(c)).ToArray());

    private static string Truncate(string text) => text.Length <= 90 ? text : text[..90] + "…";

    [Fact]
    public void Real_word_documents_convert_without_error()
    {
        var documents = Directory.Exists(TestPaths.RealFixtures)
            ? Directory.GetFiles(TestPaths.RealFixtures, "*.docx")
            : [];

        if (documents.Length == 0)
        {
            // Real documents are supplied by hand and are not required the way fixtures are, so
            // this reports rather than fails.
            _output.WriteLine(
                $"No real Word documents found in {TestPaths.RealFixtures}.\n" +
                "Drop .docx files saved by Word there; hand-authored fixtures cannot reproduce\n" +
                "the quirks of real Word output.");
            return;
        }

        foreach (var path in documents)
        {
            // System font discovery stays on here: a real document names fonts we have not
            // pinned, and substitution is part of what needs exercising.
            var pdf = Converter.Convert(File.ReadAllBytes(path));
            var name = Path.GetFileNameWithoutExtension(path);

            var geometry = PdfInspector.Inspect(pdf);
            _output.WriteLine($"{name}: {geometry.PageCount} page(s), {pdf.Length:N0} bytes");

            Assert.True(geometry.PageCount > 0, $"{name} produced no pages");
            TestPaths.WriteArtifact($"real-{name}.pdf", pdf);
        }
    }

    private static string ReferencePathFor(string name) =>
        Path.Combine(TestPaths.ReferencePdfs, name + ".pdf");
}
