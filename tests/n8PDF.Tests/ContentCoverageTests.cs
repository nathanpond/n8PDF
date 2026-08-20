using n8PDF;
using System.Text;
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

    /// <summary>
    /// Fixtures whose pages hold their text in a different order from the document, and where
    /// that is the point of the fixture rather than a fault.
    /// </summary>
    /// <remarks>
    /// The Hebrew fixture is here for a different reason from the tables: its text reaches the
    /// page in the order it is drawn rather than the order it is stored, which for a script that
    /// runs right to left is the reverse. What has to be true of it is that every character
    /// arrives, and that is what is asked.
    /// </remarks>
    /// <remarks>
    /// The page-numbering fixture is here for a third reason again: a field that is recomputed
    /// shows a number the document never stored, so the text on the page and the text in the part
    /// genuinely differ. What has to be true of it is the same — that nothing went missing.
    /// </remarks>
    /// <remarks>
    /// And the equations fixture for a fourth: an equation is set as a page of its own and drawn
    /// into the line that holds it, so its letters reach the file as a group rather than in among
    /// the words on either side of them. Every letter is there and selectable, and each is where
    /// Word puts it; what a reader dragging across the whole line copies comes out in a different
    /// order from Word's, which is worth writing down and is not content lost.
    /// </remarks>
    private static readonly HashSet<string> Reorders =
            [
        "table-split", "table-vertical-merge", "table-merge-split",
        "hebrew", "font-fallback", "marks", "arabic", "indic", "southeast-asian", "universal", "apple",
        "page-numbering-restart", "equations", "math-line-box-probe", "math-structure-probe", "math-kern-probe"
    ];

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

    /// <summary>
    /// A character that is drawn as two, and read back as the two it was drawn as.
    /// </summary>
    /// <remarks>
    /// A vowel written on both sides of its consonant is stored as one character and cannot be
    /// drawn as one: half of it goes to the left of the letter and half to the right. Both halves
    /// reach the page and both are readable, but what a reader copies out is the halves rather
    /// than the character that was typed — which is also what Word's own files give back.
    /// </remarks>
    private static readonly Dictionary<char, string> TakenApart = new()
    {
        ['\u0DDA'] = "\u0DD9\u0DCA", ['\u0DDC'] = "\u0DD9\u0DCF",
        ['\u0DDD'] = "\u0DD9\u0DCF\u0DCA", ['\u0DDE'] = "\u0DD9\u0DDF"
    };

    private void AssertNothingDropped(string name, byte[] docx, byte[] pdf)
    {
        var expected = Normalize(ReadDocumentText(docx));

        foreach (var (whole, halves) in TakenApart)
            expected = expected.Replace(whole.ToString(), halves);

        var actual = Normalize(string.Concat(PdfTextExtractor.Extract(pdf).Select(r => r.Text)));

        // Where a table row is broken across a page, the order the document keeps its text in and
        // the order the pages hold it are genuinely different: the row's later cells sit on the
        // page the row began on, while its first cell carries on over the page. A vertically
        // merged cell is the same the other way about — it is drawn once the last row it covers
        // has been, so it follows text the document holds before it. Only the presence of the
        // characters can be asked about there, not their order.
        if (Reorders.Contains(name))
        {
            AssertNothingMissing(name, expected, actual);
            return;
        }

        // The document's characters must all appear, in order, but the PDF may hold more: list
        // numbers and bullets are generated during layout rather than stored in the document, so
        // requiring the two to match exactly would fail for every list.
        var missingAt = FirstUnmatched(expected, actual);

        if (missingAt < 0)
        {
            _output.WriteLine($"{name}: {expected.Length} document character(s), all present");
            return;
        }

        Assert.Fail(
            $"""
             '{name}' lost content on the way to the PDF.

             {expected.Length} characters in the document, {actual.Length} in the PDF.
             The first {missingAt} were found, then the document's text ran ahead of the PDF's:

               document from there: {Excerpt(expected, missingAt)}
               pdf:                 {Excerpt(actual, 0)}

             Text that appears in the parsed document but never in the PDF has been dropped by
             layout — usually a block construct the engine does not handle yet.
             """);
    }

    /// <summary>
    /// Collects every text run in the document, including inside tables, from the parsed model.
    /// </summary>
    /// <summary>
    /// What the document says, read from the part itself rather than from the model built out of
    /// it.
    /// </summary>
    /// <remarks>
    /// Reading the model would compare the engine against a reading of the document that the same
    /// code made, so anything the reader dropped would be missing from both sides and the test
    /// would pass. That is not a hypothetical: a content control wrapping a paragraph was dropped
    /// by the body walk for as long as this read the model, and nothing here noticed.
    ///
    /// Four kinds of text in a part are deliberately not on the page and are left out: what a
    /// content control holds as its own properties rather than its content, the branch of a
    /// compatibility alternative that was not taken, the text of a deletion, and the instructions
    /// of a field — which are what the field is told to compute, not what it shows.
    /// </remarks>
    private static string ReadDocumentText(byte[] docx)
    {
        using var package = OpcPackage.Open(new MemoryStream(docx));
        var root = package.ReadPartAsXml(package.GetMainDocumentPartName()).Root;

        var text = new System.Text.StringBuilder();
        if (root is null) return text.ToString();

        foreach (var element in root.Descendants())
        {
            // An equation's text is in a namespace of its own and is content like any other: it
            // is what used to go missing wholesale, and what has to be counted for that not to
            // happen again quietly.
            if (element.Name != W.Main + "t" && element.Name != W.Main + "tab" &&
                element.Name != OfficeMath.Main + "t")
            {
                continue;
            }

            if (element.Ancestors().Any(ancestor =>
                    ancestor.Name == W.Main + "sdtPr" ||
                    ancestor.Name == W.Main + "del" ||
                    ancestor.Name == W.Main + "instrText" ||
                    ancestor.Name == W.Compatibility + "Fallback"))
            {
                continue;
            }

            text.Append(element.Name == W.Main + "tab" ? "\t" : element.Value);
        }

        return text.ToString();
    }

    private static void AppendBlock(BlockElement block, System.Text.StringBuilder text)
    {
        switch (block)
        {
            case Paragraph paragraph:
                // Run by run rather than through GetText, so that what a shape holds is counted
                // where the shape stands. A text box's paragraphs are text like any other, and
                // before they were laid out they were the plainest example there is of content
                // lost in silence: the box drew nothing and the document looked shorter.
                foreach (var run in paragraph.Runs)
                foreach (var content in run.Content)
                {
                    switch (content)
                    {
                        case TextInline inline:
                            text.Append(inline.Text);
                            break;

                        case DrawingInline { Shape: { } shape }:
                            foreach (var child in shape.Content) AppendBlock(child, text);
                            break;

                        case AnchoredDrawing { Shape: { } anchored }:
                            foreach (var child in anchored.Content) AppendBlock(child, text);
                            break;
                    }
                }

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
    private static string Normalize(string text)
    {
        var plain = new System.Text.StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune)) continue;

            plain.Append(Plain(rune));
        }

        return plain.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// A letter of an equation written back as the letter it stands for.
    /// </summary>
    /// <remarks>
    /// A variable is not an italic x: it is U+1D465, a character of its own that a face meant for
    /// mathematics draws differently. So the document says x and the page says 𝑥, and both are
    /// right. The two are compared as the letter, which is what a reader would say either is.
    /// </remarks>
    private static string Plain(Rune rune) => rune.Value switch
    {
        >= 0x1D434 and <= 0x1D44D => ((char)('A' + rune.Value - 0x1D434)).ToString(),
        >= 0x1D44E and <= 0x1D467 => ((char)('a' + rune.Value - 0x1D44E)).ToString(),
        0x210E => "h",
        0x2212 => "-",
        _ => rune.ToString()
    };

    /// <summary>
    /// Index of the first character of <paramref name="expected"/> that does not appear, in
    /// order, in <paramref name="actual"/>; -1 when all of them do.
    /// </summary>
    /// <summary>
    /// Checks that every character of the document is somewhere in the PDF, without asking where.
    /// </summary>
    private void AssertNothingMissing(string name, string expected, string actual)
    {
        var available = new Dictionary<char, int>();
        foreach (var character in actual)
        {
            available[character] = available.GetValueOrDefault(character) + 1;
        }

        foreach (var character in expected)
        {
            var left = available.GetValueOrDefault(character);

            Assert.True(left > 0,
                $"'{name}' lost a '{character}' on the way to the PDF: the document holds more of " +
                "them than the pages do.");

            available[character] = left - 1;
        }

        _output.WriteLine($"{name}: {expected.Length} document character(s), all present, in page order");
    }

    private static int FirstUnmatched(string expected, string actual)
    {
        var j = 0;

        for (var i = 0; i < expected.Length; i++)
        {
            while (j < actual.Length && actual[j] != expected[i]) j++;
            if (j >= actual.Length) return i;
            j++;
        }

        return -1;
    }

    private static string Excerpt(string text, int from) =>
        from >= text.Length ? "(end of text)" : $"\"{text[from..Math.Min(text.Length, from + 60)]}…\"";
}
