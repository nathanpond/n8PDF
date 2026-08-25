using System.Text;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The PDF document outline (#66): the tree of headings a reader's navigation pane shows,
/// compared node for node against the one Word's own export writes.
/// </summary>
public class OutlineTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private sealed record Entry(string Title, int Depth, int PageIndex);

    /// <summary>Flattens a PDF's outline tree, depth-first, resolving each destination's page.</summary>
    private static List<Entry> Read(byte[] pdf)
    {
        var reader = new PdfFileReader(pdf);
        var result = new List<Entry>();

        if (reader.Resolve(reader.GetEntry(reader.Trailer, "Root")) is not PdfDictValue root ||
            reader.Resolve(reader.GetEntry(root, "Outlines")) is not PdfDictValue outlines)
        {
            return result;
        }

        var pages = reader.GetPages();

        int PageOf(PdfValue? dest)
        {
            if (reader.Resolve(dest) is not PdfArrayValue array || array.Items.Count == 0) return -1;

            var target = reader.Resolve(array.Items[0]);
            foreach (var page in pages)
                if (ReferenceEquals(page.Dictionary, target))
                    return page.Index;

            return -1;
        }

        string Text(PdfValue? value) =>
            reader.Resolve(value) is PdfStringValue text
                ? text.Bytes.Length >= 2 && text.Bytes[0] == 0xFE && text.Bytes[1] == 0xFF
                    ? Encoding.BigEndianUnicode.GetString(text.Bytes, 2, text.Bytes.Length - 2)
                    : text.AsLatin1
                : "";

        void Visit(PdfDictValue node, int depth)
        {
            var current = reader.Resolve(reader.GetEntry(node, "First"));
            var guard = 0;

            while (current is PdfDictValue item && guard++ < 500)
            {
                // Word writes /Dest; some producers write a GoTo action instead. Take either.
                var dest = reader.GetEntry(item, "Dest");
                if (dest is null && reader.Resolve(reader.GetEntry(item, "A")) is PdfDictValue action)
                    dest = reader.GetEntry(action, "D");

                result.Add(new Entry(Text(reader.GetEntry(item, "Title")), depth, PageOf(dest)));
                Visit(item, depth + 1);
                current = reader.Resolve(reader.GetEntry(item, "Next"));
            }
        }

        Visit(outlines, 0);
        return result;
    }

    private static byte[] Ours(string fixture) =>
        n8PDF.Converter.Convert(Fixtures.Build(fixture),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

    /// <summary>
    /// Word for Mac's own export writes no outline at all — measured, not assumed: its reference
    /// PDF for this very fixture has no <c>/Outlines</c> anywhere in it.
    /// </summary>
    /// <remarks>
    /// On Windows, Word's "create bookmarks using headings" produces the tree this feature
    /// mirrors; the Mac export path drops it, so there is no Word tree to compare against and
    /// the structural tests below carry the feature instead. If a future export round ever makes
    /// this fail, Word has started writing one — compare against it then.
    /// </remarks>
    [Fact]
    public void Word_for_mac_writes_no_outline_of_its_own()
    {
        var path = Path.Combine(TestPaths.ReferencePdfs, "outline-probe.pdf");
        if (!File.Exists(path)) return; // reported by Fixture_has_a_reference_pdf

        var words = Read(File.ReadAllBytes(path));

        _output.WriteLine(words.Count == 0
            ? "Word's export carries no outline"
            : "word: " + string.Join(" | ", words.Select(e => $"{e.Title}@{e.Depth}/p{e.PageIndex}")));

        Assert.Empty(words);
    }

    /// <summary>
    /// The shape on its own terms: seven entries, the level skip closing to the nearest shallower
    /// heading, the hand-promoted paragraph present, and the last entry on the second page.
    /// </summary>
    [Fact]
    public void Skips_close_and_a_paragraph_level_override_counts()
    {
        var entries = Read(Ours("outline-probe"));

        Assert.Equal(
        [
            new Entry("The first part", 0, 0),
            new Entry("A section within it", 1, 0),
            new Entry("A detail of the section", 2, 0),
            new Entry("The second part", 0, 0),
            new Entry("Straight to a detail", 1, 0),
            new Entry("Promoted by hand", 1, 0),
            new Entry("On the second page", 1, 1)
        ], entries);
    }

    /// <summary>Every node open: the root's count is the total and no count is negative.</summary>
    [Fact]
    public void The_counts_are_positive_and_total_at_the_root()
    {
        var reader = new PdfFileReader(Ours("outline-probe"));
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));
        var outlines = (PdfDictValue)reader.Resolve(reader.GetEntry(root, "Outlines"));

        Assert.Equal(7, ((PdfNumberValue)reader.Resolve(reader.GetEntry(outlines, "Count"))!).Value);

        void Visit(PdfDictValue node)
        {
            var current = reader.Resolve(reader.GetEntry(node, "First"));
            while (current is PdfDictValue item)
            {
                if (reader.Resolve(reader.GetEntry(item, "Count")) is PdfNumberValue count)
                    Assert.True(count.Value > 0, "a closed (negative) count was written");
                Visit(item);
                current = reader.Resolve(reader.GetEntry(item, "Next"));
            }
        }

        Visit(outlines);
    }

    /// <summary>A document with no headings gets no <c>/Outlines</c> entry at all.</summary>
    [Fact]
    public void No_headings_means_no_outline_entry()
    {
        var pdf = n8PDF.Converter.Convert(
            new DocxBuilder().AddParagraph("Just a paragraph.").Build(),
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var reader = new PdfFileReader(pdf);
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));

        Assert.Null(reader.GetEntry(root, "Outlines"));
    }
}
