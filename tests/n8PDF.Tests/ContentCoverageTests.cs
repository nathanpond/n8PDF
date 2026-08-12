using n8PDF;
using n8PDF.Ooxml;
using n8PDF.Packaging;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Asserts that every piece of text in a document reaches the PDF.
/// </summary>
/// <remarks>
/// Position tests say the text we drew is in the right place. They say nothing about text we
/// never drew. A construct the layout engine does not handle is dropped in silence: the
/// conversion succeeds, the PDF opens, the pages look plausible, and the content is simply gone —
/// which is indistinguishable from success unless something checks for it.
///
/// The expected text comes from the parsed document model rather than from layout, so this
/// compares two independent stages against each other rather than the engine against itself.
/// </remarks>
public class ContentCoverageTests(ITestOutputHelper output)
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
    public void Every_fixture_reaches_the_pdf_intact(string name)
    {
        var docx = Fixtures.Build(name);
        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };

        AssertNothingDropped(name, docx, Converter.Convert(docx, options));
    }

    [Fact]
    public void Real_word_documents_reach_the_pdf_intact()
    {
        var documents = Directory.Exists(TestPaths.RealFixtures)
            ? Directory.GetFiles(TestPaths.RealFixtures, "*.docx")
                .Where(f => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                .ToArray()
            : [];

        if (documents.Length == 0)
        {
            _output.WriteLine(
                $"No real Word documents in {TestPaths.RealFixtures}.\n" +
                "Documents saved by Word carry a full styles.xml, settings.xml and theme that\n" +
                "hand-authored fixtures do not, and they are the only thing that exercises\n" +
                "constructs we have not thought to write a fixture for.");
            return;
        }

        foreach (var path in documents)
        {
            var docx = File.ReadAllBytes(path);
            AssertNothingDropped(Path.GetFileNameWithoutExtension(path), docx, Converter.Convert(docx));
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Every_placeable_image_reaches_the_page(string name)
    {
        var docx = Fixtures.Build(name);

        using var package = OpcPackage.Open(new MemoryStream(docx));
        var mainPart = package.GetMainDocumentPartName();
        var document = DocumentParser.Parse(package.ReadPartAsXml(mainPart));

        // Only drawings whose picture is present and in a format we handle are expected on the
        // page. An unsupported format is a documented gap; one that should have worked and did
        // not is a silent loss, which is what this is for.
        var expected = 0;
        foreach (var drawing in AllDrawings(document))
        {
            if (drawing.RelationshipId is null) continue;

            var target = package.GetRelationships(mainPart)
                .FirstOrDefault(r => r.Id == drawing.RelationshipId && !r.IsExternal);
            if (target is null) continue;

            var partName = package.ResolveTarget(mainPart, target.Target);
            if (!package.HasPart(partName)) continue;

            if (n8PDF.Images.ImageReader.TryRead(package.ReadPart(partName)) is not null) expected++;
        }

        if (expected == 0) return;

        using var stream = new MemoryStream(docx);
        var laidOut = Converter.LayoutDocument(stream,
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var placed = laidOut.Pages.Sum(page => page.Images.Count);

        Assert.True(placed == expected,
            $"'{name}' has {expected} placeable image(s) but only {placed} reached the page. " +
            "An image dropped by layout leaves no trace in the output.");
    }

    private static IEnumerable<DrawingInline> AllDrawings(WordDocument document)
    {
        var blocks = new Queue<BlockElement>(document.Body);

        while (blocks.Count > 0)
        {
            switch (blocks.Dequeue())
            {
                case Paragraph paragraph:
                    foreach (var drawing in paragraph.Runs.SelectMany(r => r.Content).OfType<DrawingInline>())
                        yield return drawing;
                    break;

                case Table table:
                    foreach (var child in table.Rows.SelectMany(r => r.Cells).SelectMany(c => c.Content))
                        blocks.Enqueue(child);
                    break;
            }
        }
    }

    private void AssertNothingDropped(string name, byte[] docx, byte[] pdf)
    {
        var expected = Normalize(ReadDocumentText(docx));
        var actual = Normalize(string.Concat(PdfTextExtractor.Extract(pdf).Select(r => r.Text)));

        if (expected == actual)
        {
            _output.WriteLine($"{name}: {expected.Length} characters, all present");
            return;
        }

        var common = CommonPrefixLength(expected, actual);

        Assert.Fail(
            $"""
             '{name}' lost content on the way to the PDF.

             {expected.Length} characters in the document, {actual.Length} in the PDF.
             They agree for the first {common} character(s), then diverge:

               document: {Excerpt(expected, common)}
               pdf:      {Excerpt(actual, common)}

             Text that appears in the parsed document but never in the PDF has been dropped by
             layout — usually a block construct the engine does not handle yet.
             """);
    }

    /// <summary>
    /// Collects every text run in the document, including inside tables, from the parsed model.
    /// </summary>
    private static string ReadDocumentText(byte[] docx)
    {
        using var package = OpcPackage.Open(new MemoryStream(docx));
        var document = DocumentParser.Parse(package.ReadPartAsXml(package.GetMainDocumentPartName()));

        var text = new System.Text.StringBuilder();
        foreach (var block in document.Body) AppendBlock(block, text);
        return text.ToString();
    }

    private static void AppendBlock(BlockElement block, System.Text.StringBuilder text)
    {
        switch (block)
        {
            case Paragraph paragraph:
                text.Append(paragraph.GetText());
                break;

            case Table table:
                foreach (var cell in table.Rows.SelectMany(row => row.Cells))
                {
                    foreach (var child in cell.Content) AppendBlock(child, text);
                }

                break;
        }
    }

    /// <summary>
    /// Strips whitespace and case before comparing. Line breaking redistributes whitespace, and
    /// the caps and small-caps properties change the case of what is drawn, so neither carries
    /// information about whether content survived.
    /// </summary>
    private static string Normalize(string text) =>
        new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

    private static int CommonPrefixLength(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        return i;
    }

    private static string Excerpt(string text, int from) =>
        from >= text.Length ? "(end of text)" : $"\"{text[from..Math.Min(text.Length, from + 60)]}…\"";
}
