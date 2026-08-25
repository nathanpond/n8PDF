using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Tagged PDF (#67): the structure tree, its reading order, the alt text a picture carries, and
/// the language on the catalogue.
/// </summary>
public class TaggedPdfTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] Sample()
    {
        var builder = new DocxBuilder()
            .WithStyles(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
                "<w:docDefaults><w:rPrDefault><w:rPr>" +
                "<w:rFonts w:ascii=\"Times New Roman\" w:hAnsi=\"Times New Roman\"/>" +
                "<w:sz w:val=\"24\"/><w:lang w:val=\"fr-FR\"/>" +
                "</w:rPr></w:rPrDefault></w:docDefaults>" +
                "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\">" +
                "<w:name w:val=\"Normal\"/></w:style></w:styles>");

        var image = builder.AddImagePart(ImageWriter.Bmp(8, 8, ImageWriter.Sample(8, 8)), "bmp");

        var docx = builder
            .AddRawParagraph("<w:p><w:pPr><w:outlineLvl w:val=\"0\"/></w:pPr>" +
                             "<w:r><w:t>The heading</w:t></w:r></w:p>")
            .AddRawParagraph("<w:p><w:r><w:t>Before </w:t></w:r>" +
                             "<w:bookmarkStart w:id=\"7\" w:name=\"there\"/><w:bookmarkEnd w:id=\"7\"/>" +
                             "<w:hyperlink w:anchor=\"there\"><w:r><w:t>a link</w:t></w:r></w:hyperlink>" +
                             "<w:r><w:t> after.</w:t></w:r></w:p>")
            .AddRawParagraph("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr>" +
                             "<w:tblGrid><w:gridCol w:w=\"2400\"/><w:gridCol w:w=\"2400\"/></w:tblGrid>" +
                             "<w:tr><w:tc><w:tcPr/><w:p><w:r><w:t>One</w:t></w:r></w:p></w:tc>" +
                             "<w:tc><w:tcPr/><w:p><w:r><w:t>Two</w:t></w:r></w:p></w:tc></w:tr></w:tbl>")
            .AddImageParagraph(image, 40, 40, description: "A described picture")
            .Build();

        return n8PDF.Converter.Convert(docx,
            new n8PDF.ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });
    }

    private sealed record Node(string Role, string? Alt, int Marked, int Annotations, List<Node> Children);

    private static Node Walk(PdfFileReader reader, PdfDictValue element)
    {
        var role = reader.Resolve(reader.GetEntry(element, "S")) is PdfNameValue name ? name.Name : "?";
        var alt = reader.Resolve(reader.GetEntry(element, "Alt")) is PdfStringValue text ? text.AsLatin1 : null;

        var node = new Node(role, alt, 0, 0, []);
        var marked = 0;
        var annotations = 0;

        if (reader.Resolve(reader.GetEntry(element, "K")) is PdfArrayValue kids)
        {
            foreach (var kid in kids.Items)
            {
                if (reader.Resolve(kid) is not PdfDictValue child) continue;

                var type = reader.Resolve(reader.GetEntry(child, "Type")) is PdfNameValue t ? t.Name : null;

                if (type == "MCR") marked++;
                else if (type == "OBJR") annotations++;
                else if (type == "StructElem" || reader.GetEntry(child, "S") is not null)
                    node.Children.Add(Walk(reader, child));
            }
        }

        return node with { Marked = marked, Annotations = annotations };
    }

    private static List<Node> Flatten(Node node)
    {
        var all = new List<Node> { node };
        foreach (var child in node.Children) all.AddRange(Flatten(child));
        return all;
    }

    /// <summary>
    /// The tree holds the heading, the paragraphs, the table with its row and cells, the figure
    /// with the document's own description, and the link — in reading order.
    /// </summary>
    [Fact]
    public void The_tree_carries_the_document_s_structure()
    {
        var reader = new PdfFileReader(Sample());
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));

        var markInfo = reader.Resolve(reader.GetEntry(root, "MarkInfo")) as PdfDictValue;
        Assert.True(markInfo is not null &&
                    reader.Resolve(reader.GetEntry(markInfo, "Marked")) is PdfBoolValue { Value: true },
            "/MarkInfo does not declare the file tagged");

        var treeRoot = reader.Resolve(reader.GetEntry(root, "StructTreeRoot")) as PdfDictValue;
        Assert.True(treeRoot is not null, "no /StructTreeRoot");

        var document = reader.Resolve(reader.GetEntry(treeRoot!, "K")) as PdfDictValue;
        Assert.True(document is not null, "the tree root holds no Document");

        var tree = Walk(reader, document!);
        var all = Flatten(tree);

        _output.WriteLine(string.Join(" | ", all.Select(n => $"{n.Role}({n.Marked}mc,{n.Annotations}a)")));

        Assert.Equal("Document", tree.Role);
        Assert.Contains(all, n => n is { Role: "H1", Marked: > 0 });
        Assert.Contains(all, n => n is { Role: "P", Marked: > 0 });
        Assert.Contains(all, n => n.Role == "Table");
        Assert.Contains(all, n => n.Role == "TR");
        Assert.Contains(all, n => n is { Role: "TD", Children.Count: > 0 });
        Assert.Contains(all, n => n is { Role: "Figure", Alt: "A described picture" });
        Assert.Contains(all, n => n is { Role: "Link", Annotations: > 0 });

        // Reading order at the top level: the heading first, the table after the paragraphs,
        // exactly as the document wrote them.
        var top = tree.Children.Select(n => n.Role).ToList();
        Assert.Equal("H1", top[0]);
        Assert.True(top.IndexOf("Table") > top.IndexOf("P"), string.Join(",", top));

        // And the parent tree stands, with the pages pointing in.
        Assert.True(reader.Resolve(reader.GetEntry(treeRoot!, "ParentTree")) is PdfDictValue,
            "no /ParentTree");
    }

    /// <summary>
    /// Reading order: walking the tree in order visits each page's marked content in the order
    /// it was drawn — and the drawing order is document order, which ContentCoverageTests
    /// asserts of every fixture. The chain of the two is the criterion.
    /// </summary>
    [Fact]
    public void The_tree_reads_in_drawing_order()
    {
        var reader = new PdfFileReader(Sample());
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));
        var treeRoot = (PdfDictValue)reader.Resolve(reader.GetEntry(root, "StructTreeRoot"));
        var document = (PdfDictValue)reader.Resolve(reader.GetEntry(treeRoot, "K"));

        var visited = new List<(int Page, int Mcid)>();
        var pages = reader.GetPages();

        void Visit(PdfDictValue element)
        {
            if (reader.Resolve(reader.GetEntry(element, "K")) is not PdfArrayValue kids) return;

            // A figure's ink is painted before any text — z-order, so the letters sit on top —
            // and its MCID says so; what reading order is about is the text, and the tree.
            var role = reader.Resolve(reader.GetEntry(element, "S")) is PdfNameValue s ? s.Name : "";

            foreach (var kid in kids.Items)
            {
                if (reader.Resolve(kid) is not PdfDictValue child) continue;

                var type = reader.Resolve(reader.GetEntry(child, "Type")) is PdfNameValue t ? t.Name : null;

                if (type == "MCR" && role != "Figure")
                {
                    var target = reader.Resolve(reader.GetEntry(child, "Pg"));
                    var page = pages.FindIndex(p => ReferenceEquals(p.Dictionary, target));
                    var mcid = (int)((PdfNumberValue)reader.Resolve(reader.GetEntry(child, "MCID"))!).Value;
                    visited.Add((page, mcid));
                }
                else if (type != "OBJR")
                {
                    Visit(child);
                }
            }
        }

        Visit(document);

        _output.WriteLine(string.Join(" ", visited.Select(v => $"{v.Page}:{v.Mcid}")));
        Assert.True(visited.Count > 0, "the tree holds no marked content at all");

        foreach (var group in visited.GroupBy(v => v.Page))
        {
            var order = group.Select(v => v.Mcid).ToList();
            Assert.True(order.SequenceEqual(order.OrderBy(m => m)),
                $"page {group.Key}'s marked content is out of drawing order in the tree: " +
                string.Join(",", order));
        }
    }

    /// <summary>The document's stated language reaches the catalogue.</summary>
    [Fact]
    public void The_language_reaches_the_catalogue()
    {
        var reader = new PdfFileReader(Sample());
        var root = (PdfDictValue)reader.Resolve(reader.GetEntry(reader.Trailer, "Root"));

        Assert.True(reader.Resolve(reader.GetEntry(root, "Lang")) is PdfStringValue { AsLatin1: "fr-FR" },
            "the catalogue names no /Lang");
    }

    /// <summary>The link annotation names its place in the tree.</summary>
    [Fact]
    public void The_link_annotation_names_its_parent()
    {
        var reader = new PdfFileReader(Sample());
        var page = reader.GetPages()[0];

        var annotations = reader.Resolve(reader.GetEntry(page.Dictionary, "Annots")) as PdfArrayValue;
        Assert.True(annotations is { Items.Count: > 0 }, "no link annotation");

        var annotation = (PdfDictValue)reader.Resolve(annotations!.Items[0]);
        Assert.True(reader.GetEntry(annotation, "StructParent") is not null,
            "the annotation names no /StructParent");

        Assert.True(reader.GetEntry(page.Dictionary, "StructParents") is not null,
            "the page names no /StructParents");
    }
}
