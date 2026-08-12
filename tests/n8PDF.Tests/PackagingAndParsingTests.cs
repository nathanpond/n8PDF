using n8PDF.Ooxml;
using n8PDF.Packaging;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>Tier 2 tests for the OPC container reader and the WordprocessingML parsers.</summary>
public class PackagingAndParsingTests
{
    [Fact]
    public void Unit_conversions_round_trip_and_match_known_values()
    {
        // One inch: 1440 twips, 914400 EMUs, 72 points. Every layout number depends on these.
        Assert.Equal(72, Units.TwipsToPoints(1440));
        Assert.Equal(72, Units.EmuToPoints(914400));
        Assert.Equal(72, Units.InchesToPoints(1));

        // A 12pt font is stored as 24 half-points; a half-point border as 4 eighth-points.
        Assert.Equal(12, Units.HalfPointsToPoints(24));
        Assert.Equal(0.5, Units.EighthPointsToPoints(4));

        Assert.Equal(1440, Units.PointsToTwips(72));
        Assert.Equal(914400, Units.PointsToEmu(72));
    }

    [Fact]
    public void Package_exposes_parts_content_types_and_relationships()
    {
        using var package = OpcPackage.Open(new DocxBuilder().AddParagraph("Hello").BuildStream());

        Assert.True(package.HasPart("word/document.xml"));
        Assert.True(package.HasPart("/word/document.xml"), "leading slashes should be tolerated");

        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            package.GetContentType("word/document.xml"));

        // The xml extension default applies to parts with no override.
        Assert.Equal("application/xml", package.GetContentType("word/some-other-part.xml"));

        var main = package.GetMainDocumentPartName();
        Assert.Equal("word/document.xml", main);

        var styles = package.GetRelatedPartName(main, OpcPackage.StylesRelationship);
        Assert.Equal("word/styles.xml", styles);

        var theme = package.GetRelatedPartName(main, OpcPackage.ThemeRelationship);
        Assert.Equal("word/theme/theme1.xml", theme);
    }

    [Fact]
    public void Relationship_targets_resolve_relative_to_the_declaring_part()
    {
        using var package = OpcPackage.Open(new DocxBuilder().AddParagraph("x").BuildStream());

        Assert.Equal("word/styles.xml", package.ResolveTarget("word/document.xml", "styles.xml"));
        Assert.Equal("word/theme/theme1.xml", package.ResolveTarget("word/document.xml", "theme/theme1.xml"));
        Assert.Equal("word/styles.xml", package.ResolveTarget("word/document.xml", "/word/styles.xml"));

        // Producers do write ".." segments; they must collapse rather than leak into a part name.
        Assert.Equal("word/media/image1.png",
            package.ResolveTarget("word/embeddings/thing.xml", "../media/image1.png"));
    }

    [Fact]
    public void Opening_something_that_is_not_a_docx_fails_clearly()
    {
        using var empty = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(empty, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("readme.txt");
        }

        empty.Position = 0;
        using var package = OpcPackage.Open(empty);

        Assert.Throws<InvalidDataException>(() => package.GetMainDocumentPartName());
    }

    [Fact]
    public void Paragraphs_and_runs_parse_with_their_text()
    {
        var document = ParseDocument(new DocxBuilder()
            .AddParagraph("First paragraph")
            .AddParagraph("Second paragraph"));

        Assert.Equal(2, document.Body.Count);
        Assert.Equal("First paragraph", document.Paragraphs.First().GetText());
        Assert.Equal("Second paragraph", document.Paragraphs.Last().GetText());
    }

    [Fact]
    public void Preserved_whitespace_survives_but_unmarked_text_is_trimmed()
    {
        var document = ParseDocument(new DocxBuilder()
            .AddRawParagraph("<w:p><w:r><w:t xml:space=\"preserve\">  spaced  </w:t></w:r></w:p>")
            .AddRawParagraph("<w:p><w:r><w:t>  trimmed  </w:t></w:r></w:p>"));

        var paragraphs = document.Paragraphs.ToList();

        // Word relies on xml:space to distinguish meaningful spaces from pretty-printing.
        Assert.Equal("  spaced  ", paragraphs[0].GetText());
        Assert.Equal("trimmed", paragraphs[1].GetText());
    }

    [Fact]
    public void Run_properties_parse_including_toggle_off_values()
    {
        var document = ParseDocument(new DocxBuilder().AddParagraphWithRuns([
            ("bold", "<w:b/>"),
            ("not bold", "<w:b w:val=\"0\"/>"),
            ("styled", "<w:rFonts w:ascii=\"Arial\"/><w:sz w:val=\"28\"/><w:i/><w:color w:val=\"FF0000\"/><w:u w:val=\"single\"/>")
        ]));

        var runs = document.Paragraphs.First().Runs;

        // An element with no val means on; val="0" means off. Both differ from "unspecified",
        // which is what makes the style cascade resolvable.
        Assert.True(runs[0].Properties.Bold);
        Assert.False(runs[1].Properties.Bold);
        Assert.Null(runs[0].Properties.Italic);

        Assert.Equal("Arial", runs[2].Properties.AsciiFont);
        Assert.Equal(28, runs[2].Properties.SizeHalfPoints);
        Assert.True(runs[2].Properties.Italic);
        Assert.Equal("FF0000", runs[2].Properties.Color);
        Assert.Equal(UnderlineStyle.Single, runs[2].Properties.Underline);
    }

    [Fact]
    public void Automatic_colour_is_treated_as_unspecified()
    {
        var document = ParseDocument(new DocxBuilder()
            .AddParagraphWithRuns([("auto", "<w:color w:val=\"auto\"/>")]));

        // "auto" asks the consumer to choose a contrasting colour rather than naming one, so it
        // must not be mistaken for a literal colour value.
        Assert.Null(document.Paragraphs.First().Runs[0].Properties.Color);
    }

    [Fact]
    public void Paragraph_properties_parse()
    {
        var document = ParseDocument(new DocxBuilder().AddParagraph(
            "text",
            paragraphProperties: """
                <w:pStyle w:val="Heading1"/>
                <w:jc w:val="center"/>
                <w:ind w:left="720" w:right="360" w:firstLine="240"/>
                <w:spacing w:before="240" w:after="120" w:line="360" w:lineRule="auto"/>
                <w:keepNext/>
                <w:tabs><w:tab w:val="right" w:pos="9360" w:leader="dot"/></w:tabs>
                """));

        var properties = document.Paragraphs.First().Properties;

        Assert.Equal("Heading1", properties.StyleId);
        Assert.Equal(Justification.Center, properties.Justification);
        Assert.Equal(720, properties.IndentLeftTwips);
        Assert.Equal(360, properties.IndentRightTwips);
        Assert.Equal(240, properties.IndentFirstLineTwips);
        Assert.Equal(240, properties.SpacingBeforeTwips);
        Assert.Equal(120, properties.SpacingAfterTwips);
        Assert.Equal(360, properties.Line);
        Assert.Equal(LineSpacingRule.Auto, properties.LineRule);
        Assert.True(properties.KeepNext);

        var tab = Assert.Single(properties.TabStops);
        Assert.Equal(9360, tab.PositionTwips);
        Assert.Equal(TabAlignment.Right, tab.Alignment);
        Assert.Equal(TabLeader.Dot, tab.Leader);
    }

    [Fact]
    public void Tabs_and_breaks_parse_as_inline_elements()
    {
        var document = ParseDocument(new DocxBuilder().AddRawParagraph(
            """
            <w:p><w:r><w:t>a</w:t><w:tab/><w:t>b</w:t><w:br/><w:t>c</w:t><w:br w:type="page"/></w:r></w:p>
            """));

        var content = document.Paragraphs.First().Runs[0].Content;

        Assert.Collection(content,
            item => Assert.Equal("a", Assert.IsType<TextInline>(item).Text),
            item => Assert.IsType<TabInline>(item),
            item => Assert.Equal("b", Assert.IsType<TextInline>(item).Text),
            item => Assert.Equal(BreakKind.Line, Assert.IsType<BreakInline>(item).Kind),
            item => Assert.Equal("c", Assert.IsType<TextInline>(item).Text),
            item => Assert.Equal(BreakKind.Page, Assert.IsType<BreakInline>(item).Kind));
    }

    [Fact]
    public void Hyperlink_and_tracked_insertion_content_is_not_lost()
    {
        var document = ParseDocument(new DocxBuilder().AddRawParagraph(
            """
            <w:p>
              <w:r><w:t xml:space="preserve">before </w:t></w:r>
              <w:hyperlink r:id="rId9"><w:r><w:t>linked</w:t></w:r></w:hyperlink>
              <w:ins><w:r><w:t xml:space="preserve"> inserted</w:t></w:r></w:ins>
              <w:del><w:r><w:delText> deleted</w:delText></w:r></w:del>
            </w:p>
            """));

        // Wrappers hold runs rather than replacing them; skipping the wrapper silently drops
        // text. Deleted content is the one case that should not render.
        Assert.Equal("before linked inserted", document.Paragraphs.First().GetText());
    }

    [Fact]
    public void Section_properties_parse_and_convert_to_points()
    {
        var document = ParseDocument(new DocxBuilder()
            .AddParagraph("x")
            .WithPage(widthTwips: 12240, heightTwips: 15840, top: 1440, right: 1080, bottom: 1440, left: 1080));

        var section = document.Section;

        Assert.Equal(612, section.PageWidthPoints);
        Assert.Equal(792, section.PageHeightPoints);

        // 8.5in wide less two 0.75in margins leaves 7in of text.
        Assert.Equal(504, section.ContentWidthPoints);
        Assert.Equal(648, section.ContentHeightPoints);
    }

    [Fact]
    public void Section_defaults_to_us_letter_when_absent()
    {
        var document = ParseDocument(new DocxBuilder().AddParagraph("x").WithSection(string.Empty));

        Assert.Equal(612, document.Section.PageWidthPoints);
        Assert.Equal(792, document.Section.PageHeightPoints);
    }

    [Fact]
    public void Landscape_orientation_is_detected()
    {
        var document = ParseDocument(new DocxBuilder().AddParagraph("x").WithSection(
            """
            <w:sectPr>
              <w:pgSz w:w="15840" w:h="12240" w:orient="landscape"/>
              <w:pgMar w:top="1440" w:right="1440" w:bottom="1440" w:left="1440"/>
            </w:sectPr>
            """));

        Assert.True(document.Section.Landscape);
        Assert.Equal(792, document.Section.PageWidthPoints);
        Assert.Equal(612, document.Section.PageHeightPoints);
    }

    [Fact]
    public void Tables_parse_into_the_model()
    {
        var document = ParseDocument(new DocxBuilder().AddRawParagraph(
            """
            <w:tbl>
              <w:tr>
                <w:tc><w:tcPr><w:tcW w:w="4680" w:type="dxa"/></w:tcPr><w:p><w:r><w:t>A1</w:t></w:r></w:p></w:tc>
                <w:tc><w:tcPr><w:gridSpan w:val="2"/></w:tcPr><w:p><w:r><w:t>B1</w:t></w:r></w:p></w:tc>
              </w:tr>
            </w:tbl>
            """));

        // Tables are not laid out yet, but they must survive parsing rather than disappear.
        var table = Assert.IsType<Table>(document.Body[0]);
        var row = Assert.Single(table.Rows);

        Assert.Equal(2, row.Cells.Count);
        Assert.Equal(4680, row.Cells[0].WidthTwips);
        Assert.Equal(2, row.Cells[1].GridSpan);
        Assert.Equal("A1", Assert.IsType<Paragraph>(row.Cells[0].Content[0]).GetText());
    }

    [Fact]
    public void Styles_and_document_defaults_parse()
    {
        using var package = OpcPackage.Open(new DocxBuilder().AddParagraph("x").BuildStream());
        var main = package.GetMainDocumentPartName();
        var styles = StylesParser.Parse(
            package.TryReadPartAsXml(package.GetRelatedPartName(main, OpcPackage.StylesRelationship)!));

        Assert.Equal(22, styles.DefaultRunProperties.SizeHalfPoints);
        Assert.Equal("minorHAnsi", styles.DefaultRunProperties.AsciiTheme);
        Assert.Equal(160, styles.DefaultParagraphProperties.SpacingAfterTwips);
        Assert.Equal("Normal", styles.DefaultParagraphStyleId);
    }

    [Fact]
    public void Style_inheritance_chain_runs_from_ancestor_to_descendant()
    {
        var styles = StylesParser.Parse(System.Xml.Linq.XDocument.Parse("""
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Body"><w:basedOn w:val="Normal"/></w:style>
              <w:style w:type="paragraph" w:styleId="Quote"><w:basedOn w:val="Body"/></w:style>
            </w:styles>
            """));

        var chain = styles.GetInheritanceChain("Quote");

        // Most general first, so that applying them in order produces the right precedence.
        Assert.Equal(["Normal", "Body", "Quote"], chain.Select(s => s.Id));
    }

    [Fact]
    public void Style_inheritance_survives_a_cycle()
    {
        var styles = StylesParser.Parse(System.Xml.Linq.XDocument.Parse("""
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:style w:type="paragraph" w:styleId="A"><w:basedOn w:val="B"/></w:style>
              <w:style w:type="paragraph" w:styleId="B"><w:basedOn w:val="A"/></w:style>
            </w:styles>
            """));

        // A malformed document must not hang the converter.
        var chain = styles.GetInheritanceChain("A");
        Assert.Equal(2, chain.Count);
    }

    [Fact]
    public void Theme_fonts_resolve_by_slot()
    {
        using var package = OpcPackage.Open(
            new DocxBuilder().AddParagraph("x").WithTheme("Cambria", "Calibri").BuildStream());

        var main = package.GetMainDocumentPartName();
        var theme = StylesParser.ParseTheme(
            package.TryReadPartAsXml(package.GetRelatedPartName(main, OpcPackage.ThemeRelationship)!));

        Assert.Equal("Cambria", theme.MajorLatinFont);
        Assert.Equal("Calibri", theme.MinorLatinFont);
        Assert.Equal("Calibri", theme.Resolve("minorHAnsi"));
        Assert.Equal("Cambria", theme.Resolve("majorHAnsi"));
        Assert.Null(theme.Resolve(null));
    }

    private static WordDocument ParseDocument(DocxBuilder builder)
    {
        using var package = OpcPackage.Open(builder.BuildStream());
        return DocumentParser.Parse(package.ReadPartAsXml(package.GetMainDocumentPartName()));
    }
}
