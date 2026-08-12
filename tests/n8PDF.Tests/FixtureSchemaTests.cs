using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Packaging;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Checks that generated fixtures satisfy the parts of the OOXML schema Word actually enforces.
/// </summary>
/// <remarks>
/// Our own parser is deliberately order-agnostic and tolerant, so a fixture can be malformed and
/// still round-trip through n8PDF perfectly. Word is not tolerant: it refuses the document with
/// "found unreadable content", which is only discovered by opening it by hand. These tests move
/// that discovery into the suite.
///
/// The two rules encoded here are the ones already violated in practice: <c>CT_RPr</c> and
/// <c>CT_PPr</c> are sequences with a required child order, and a theme must carry a complete
/// set of scheme elements.
/// </remarks>
public class FixtureSchemaTests
{
    /// <summary>
    /// Child order of <c>CT_RPr</c> (ECMA-376 Part 1, §17.3.2). Bold, italic, strike and colour
    /// all precede the font size, which is the trap when properties are concatenated by hand.
    /// </summary>
    private static readonly string[] RunPropertyOrder =
    [
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike",
        "outline", "shadow", "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden",
        "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect",
        "bdr", "shd", "fitText", "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout",
        "specVanish", "oMath"
    ];

    /// <summary>Child order of <c>CT_PPr</c> (ECMA-376 Part 1, §17.3.1).</summary>
    private static readonly string[] ParagraphPropertyOrder =
    [
        "pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr",
        "suppressLineNumbers", "pBdr", "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap",
        "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd",
        "snapToGrid", "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap",
        "jc", "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId",
        "cnfStyle", "rPr", "sectPr", "pPrChange"
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
    public void Run_and_paragraph_properties_are_in_schema_order(string name)
    {
        var inspected = 0;

        foreach (var (partName, xml) in ReadXmlParts(name))
        {
            foreach (var rPr in xml.Descendants(W.Main + "rPr"))
            {
                AssertOrder(name, partName, "w:rPr", rPr, RunPropertyOrder);
                inspected++;
            }

            foreach (var pPr in xml.Descendants(W.Main + "pPr"))
            {
                AssertOrder(name, partName, "w:pPr", pPr, ParagraphPropertyOrder);
                inspected++;
            }
        }

        // Without this the test would pass just as happily on a fixture whose properties it
        // never found — a schema check that inspects nothing is worse than no check.
        Assert.True(inspected > 0, $"Fixture '{name}' yielded no rPr or pPr elements to validate.");
    }

    [Fact]
    public void The_order_check_actually_rejects_bad_order()
    {
        // Proves the validator has teeth: this is exactly the shape the fixtures used to emit,
        // with sz before b, which is what made Word reject them.
        var bad = XElement.Parse(
            """
            <w:rPr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:rFonts w:ascii="Times New Roman"/>
              <w:sz w:val="24"/>
              <w:b/>
            </w:rPr>
            """);

        Assert.ThrowsAny<Exception>(() =>
            AssertOrder("synthetic", "word/document.xml", "w:rPr", bad, RunPropertyOrder));

        var good = XElement.Parse(
            """
            <w:rPr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:rFonts w:ascii="Times New Roman"/>
              <w:b/>
              <w:sz w:val="24"/>
            </w:rPr>
            """);

        AssertOrder("synthetic", "word/document.xml", "w:rPr", good, RunPropertyOrder);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Theme_declares_every_required_scheme(string name)
    {
        var theme = ReadXmlParts(name)
            .FirstOrDefault(part => part.PartName.Contains("theme", StringComparison.OrdinalIgnoreCase));

        if (theme.Xml is null) return;

        var elements = theme.Xml.Root?.Element(W.Drawing + "themeElements");
        Assert.NotNull(elements);

        // DrawingML requires all three, in this order. Word rejects the document otherwise.
        var schemes = elements!.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.Equal(["clrScheme", "fontScheme", "fmtScheme"], schemes);

        // Every font collection needs all three script slots, not just latin.
        foreach (var collection in elements.Descendants(W.Drawing + "majorFont")
                     .Concat(elements.Descendants(W.Drawing + "minorFont")))
        {
            var slots = collection.Elements().Select(e => e.Name.LocalName).ToList();
            Assert.Equal(["latin", "ea", "cs"], slots);
        }

        // The colour scheme must define all twelve slots.
        var colors = elements.Element(W.Drawing + "clrScheme")!.Elements().Count();
        Assert.Equal(12, colors);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Every_declared_part_and_relationship_target_exists(string name)
    {
        using var package = OpcPackage.Open(new MemoryStream(Fixtures.Build(name)));

        var main = package.GetMainDocumentPartName();
        Assert.True(package.HasPart(main));

        // A relationship pointing at a missing part is another thing Word treats as corruption.
        foreach (var relationship in package.GetRelationships(main))
        {
            if (relationship.IsExternal) continue;

            var target = package.ResolveTarget(main, relationship.Target);
            Assert.True(package.HasPart(target),
                $"Fixture '{name}' declares relationship {relationship.Id} pointing at missing part '{target}'.");
        }
    }

    private static void AssertOrder(
        string fixtureName, string partName, string parent, XElement container, string[] order)
    {
        var lastIndex = -1;
        var lastName = string.Empty;

        foreach (var child in container.Elements())
        {
            var localName = child.Name.LocalName;
            var index = Array.IndexOf(order, localName);

            // An element we do not know about cannot be ordered, so it is skipped rather than
            // failing the fixture.
            if (index < 0) continue;

            Assert.True(index >= lastIndex,
                $"""
                 Fixture '{fixtureName}', part '{partName}': {parent} children are out of schema order.
                 '{localName}' must come before '{lastName}', but appears after it.
                 Word rejects the document with "found unreadable content".
                 Build run properties with DocxBuilder.RunProperties, which orders them correctly.
                 """);

            lastIndex = index;
            lastName = localName;
        }
    }

    private static List<(string PartName, XDocument Xml)> ReadXmlParts(string fixtureName)
    {
        using var package = OpcPackage.Open(new MemoryStream(Fixtures.Build(fixtureName)));

        var parts = new List<(string, XDocument)>();
        foreach (var partName in package.PartNames)
        {
            if (!partName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            if (partName.Contains("_rels", StringComparison.OrdinalIgnoreCase)) continue;
            if (partName.StartsWith("[Content_Types]", StringComparison.OrdinalIgnoreCase)) continue;

            parts.Add((partName, package.ReadPartAsXml(partName)));
        }

        return parts;
    }
}
