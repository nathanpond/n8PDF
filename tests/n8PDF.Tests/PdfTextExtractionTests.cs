using n8PDF;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tests the content-stream reader against PDFs whose contents we already know from layout.
/// </summary>
/// <remarks>
/// The reader has to be trustworthy before any conclusion drawn with it means anything, so it is
/// verified against our own output first: layout says exactly where every run was placed, and the
/// extractor must recover those same numbers from the bytes.
/// </remarks>
public class PdfTextExtractionTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    [Fact]
    public void Extractor_recovers_text_from_our_own_output()
    {
        var pdf = Converter.Convert(
            new DocxBuilder().AddParagraph("Hello extractor",
                runProperties: DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 24)).Build(),
            Options());

        var run = Assert.Single(PdfTextExtractor.Extract(pdf));

        Assert.Equal("Hello extractor", run.Text);
        Assert.Equal(12, run.FontSize, 2);

        // The name reported is the PostScript one from /BaseFont, which never contains spaces.
        // Word writes the same name behind a subset tag, so both sides normalise to this.
        Assert.Equal("TimesNewRoman", run.FontFamily);
    }

    [Fact]
    public void Extracted_positions_match_the_layout_that_produced_them()
    {
        var docx = new DocxBuilder()
            .AddParagraph("First line of the document",
                runProperties: DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 24))
            .AddParagraph("Second line, somewhat longer than the first",
                runProperties: DocxBuilder.RunProperties(font: "Times New Roman", halfPoints: 24))
            .Build();

        using var stream = new MemoryStream(docx);
        var layout = Converter.LayoutDocument(stream, Options());
        var extracted = PdfTextExtractor.Extract(Converter.Convert(docx, Options()));

        var placed = layout.Pages.SelectMany(p => p.Texts).ToList();
        Assert.Equal(placed.Count, extracted.Count);

        // This is the round trip that matters: layout decided a position, the writer encoded it,
        // and the reader recovered it. Agreement to a thousandth of a point means all three
        // agree about the coordinate system, including the top-left/bottom-left flip.
        for (var i = 0; i < placed.Count; i++)
        {
            Assert.Equal(placed[i].Text, extracted[i].Text);
            Assert.Equal(placed[i].X, extracted[i].X, 3);
            Assert.Equal(placed[i].BaselineY, extracted[i].BaselineY, 3);
            Assert.Equal(placed[i].Width, extracted[i].Width, 2);
        }
    }

    [Fact]
    public void Multi_page_documents_report_the_right_page_for_each_run()
    {
        var pdf = Converter.Convert(Fixtures.Build("multi-page"), Options());
        var runs = PdfTextExtractor.Extract(pdf);

        Assert.Equal(3, runs.Select(r => r.PageIndex).Distinct().Count());
        Assert.Contains(runs, r => r.Text.Contains("number 1 of eighty"));
        Assert.Contains(runs, r => r.Text.Contains("number 80 of eighty"));

        // Page indices must be non-decreasing, since pages are walked in tree order.
        var pageIndices = runs.Select(r => r.PageIndex).ToList();
        Assert.Equal(pageIndices.OrderBy(i => i), pageIndices);
    }

    [Fact]
    public void Justified_text_accounts_for_tj_adjustments()
    {
        // n8PDF justifies with TJ adjustments rather than Tw, so the extractor's handling of
        // those adjustments is what keeps the reported width honest.
        var pdf = Converter.Convert(Fixtures.Build("alignment"), Options());
        var runs = PdfTextExtractor.Extract(pdf);

        // Group first, then select whole lines. A justified line is emitted as one run per
        // space-terminated segment, so filtering runs by their text keeps only part of a line
        // and its maximum is not the line's right edge.
        var lines = runs
            .GroupBy(r => Math.Round(r.BaselineY, 1))
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                Text = string.Concat(group.Select(r => r.Text)),
                Right = group.Max(r => r.X + r.Width)
            })
            .Where(line => line.Text.Contains("Justified"))
            .ToList();

        Assert.NotEmpty(lines);

        // Every justified line but the last reaches the right margin, 540pt in.
        foreach (var line in lines.Take(lines.Count - 1))
            Assert.Equal(540, line.Right, 1.0);
    }

    [Fact]
    public void Word_reference_pdfs_can_be_read()
    {
        var references = Directory.Exists(TestPaths.ReferencePdfs)
            ? Directory.GetFiles(TestPaths.ReferencePdfs, "*.pdf").OrderBy(p => p).ToArray()
            : [];

        if (references.Length == 0) return;

        // Word's PDFs use a different encoding to ours — simple TrueType fonts with subset
        // remapped single-byte codes — so this exercises a genuinely separate path through the
        // reader from the tests above.
        foreach (var path in references)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var runs = PdfTextExtractor.ExtractFile(path);

            Assert.True(runs.Count > 0, $"No text extracted from Word's {name}.pdf");

            var text = string.Concat(runs.Select(r => r.Text));
            Assert.False(string.IsNullOrWhiteSpace(text), $"Only blank text extracted from {name}.pdf");

            _output.WriteLine($"{name}: {runs.Count} run(s), first = \"{Truncate(runs[0].Text)}\" " +
                              $"at ({runs[0].X:0.##}, {runs[0].BaselineY:0.##}) {runs[0].FontFamily} {runs[0].FontSize:0.##}pt");
        }
    }

    [Fact]
    public void Word_reference_text_matches_the_fixture_it_came_from()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "single-line.pdf");
        if (!File.Exists(path)) return;

        var runs = PdfTextExtractor.ExtractFile(path);
        var text = Normalize(string.Concat(runs.Select(r => r.Text)));

        // Proves the ToUnicode CMap is being applied: Word's codes start at 33 and mean nothing
        // without it, so recovering the real sentence is a strong check.
        Assert.Contains("quick brown fox", text);
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[])[' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string text) =>
        text.Length <= 40 ? text : text[..40] + "…";
}
