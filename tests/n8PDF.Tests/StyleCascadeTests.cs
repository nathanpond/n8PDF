using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Styling;

namespace n8PDF.Tests;

/// <summary>
/// Tier 2 tests for the formatting cascade. Fidelity is lost here long before layout runs, so
/// these check the precedence rules directly rather than through a rendered page.
/// </summary>
public class StyleCascadeTests
{
    [Fact]
    public void Document_defaults_apply_when_nothing_else_does()
    {
        var resolver = Resolver("""
            <w:docDefaults>
              <w:rPrDefault><w:rPr><w:rFonts w:ascii="Georgia"/><w:sz w:val="24"/></w:rPr></w:rPrDefault>
              <w:pPrDefault><w:pPr><w:spacing w:after="200"/></w:pPr></w:pPrDefault>
            </w:docDefaults>
            """);

        var run = resolver.ResolveRun(null, null);
        Assert.Equal("Georgia", run.FontFamily);
        Assert.Equal(12, run.FontSizePoints);

        var paragraph = resolver.ResolveParagraph(null);
        Assert.Equal(10, paragraph.SpaceAfterPoints);
    }

    [Fact]
    public void Falls_back_to_the_spec_defaults_when_the_document_says_nothing()
    {
        var resolver = Resolver(string.Empty);
        var run = resolver.ResolveRun(null, null);

        Assert.Equal(StyleResolver.FallbackFontFamily, run.FontFamily);
        Assert.Equal(StyleResolver.FallbackFontSizePoints, run.FontSizePoints);
    }

    [Fact]
    public void Style_chain_applies_from_ancestor_to_descendant()
    {
        var resolver = Resolver("""
            <w:style w:type="paragraph" w:styleId="Normal">
              <w:rPr><w:rFonts w:ascii="Arial"/><w:sz w:val="20"/></w:rPr>
            </w:style>
            <w:style w:type="paragraph" w:styleId="Body">
              <w:basedOn w:val="Normal"/>
              <w:rPr><w:sz w:val="24"/></w:rPr>
            </w:style>
            """);

        var run = resolver.ResolveRun(new ParagraphProperties { StyleId = "Body" }, null);

        // Body overrides the size but inherits the font from Normal.
        Assert.Equal("Arial", run.FontFamily);
        Assert.Equal(12, run.FontSizePoints);
    }

    [Fact]
    public void Direct_formatting_beats_the_style()
    {
        var resolver = Resolver("""
            <w:style w:type="paragraph" w:styleId="Body">
              <w:rPr><w:rFonts w:ascii="Arial"/><w:sz w:val="20"/></w:rPr>
            </w:style>
            """);

        var run = resolver.ResolveRun(
            new ParagraphProperties { StyleId = "Body" },
            new RunProperties { AsciiFont = "Courier New", SizeHalfPoints = 28 });

        Assert.Equal("Courier New", run.FontFamily);
        Assert.Equal(14, run.FontSizePoints);
    }

    [Fact]
    public void Two_styles_that_both_turn_bold_on_cancel_out()
    {
        var resolver = Resolver("""
            <w:style w:type="paragraph" w:styleId="Heading"><w:rPr><w:b/></w:rPr></w:style>
            <w:style w:type="character" w:styleId="Strong"><w:rPr><w:b/></w:rPr></w:style>
            """);

        // This is the defining behaviour of a toggle property: bold applied at two levels of the
        // style hierarchy XORs to non-bold. Treating it as a plain override gets this backwards.
        var bothStyles = resolver.ResolveRun(
            new ParagraphProperties { StyleId = "Heading" },
            new RunProperties { StyleId = "Strong" });
        Assert.False(bothStyles.Bold);

        var headingOnly = resolver.ResolveRun(new ParagraphProperties { StyleId = "Heading" }, null);
        Assert.True(headingOnly.Bold);
    }

    [Fact]
    public void Direct_bold_sets_rather_than_toggles()
    {
        var resolver = Resolver("""
            <w:style w:type="paragraph" w:styleId="Heading"><w:rPr><w:b/></w:rPr></w:style>
            """);

        // Direct formatting is an absolute statement, not another participant in the XOR: a user
        // pressing Ctrl+B on already-bold text must not silently un-bold it.
        var stillBold = resolver.ResolveRun(
            new ParagraphProperties { StyleId = "Heading" },
            new RunProperties { Bold = true });
        Assert.True(stillBold.Bold);

        var explicitlyOff = resolver.ResolveRun(
            new ParagraphProperties { StyleId = "Heading" },
            new RunProperties { Bold = false });
        Assert.False(explicitlyOff.Bold);
    }

    [Fact]
    public void An_explicit_off_in_a_style_forces_the_toggle_off()
    {
        var resolver = Resolver("""
            <w:style w:type="paragraph" w:styleId="Heading"><w:rPr><w:b/></w:rPr></w:style>
            <w:style w:type="paragraph" w:styleId="Quiet">
              <w:basedOn w:val="Heading"/>
              <w:rPr><w:b w:val="0"/></w:rPr>
            </w:style>
            """);

        var run = resolver.ResolveRun(new ParagraphProperties { StyleId = "Quiet" }, null);

        // An explicit off is not a toggle participant; it turns the property off outright.
        Assert.False(run.Bold);
    }

    [Fact]
    public void Toggle_rule_is_applied_uniformly_across_toggle_properties()
    {
        Assert.False(StyleResolver.ApplyToggle(current: true, value: true, isDirect: false));
        Assert.True(StyleResolver.ApplyToggle(current: false, value: true, isDirect: false));
        Assert.False(StyleResolver.ApplyToggle(current: true, value: false, isDirect: false));

        Assert.True(StyleResolver.ApplyToggle(current: true, value: true, isDirect: true));
        Assert.False(StyleResolver.ApplyToggle(current: true, value: false, isDirect: true));
    }

    [Fact]
    public void Theme_fonts_resolve_through_the_theme_part()
    {
        var theme = new DocumentTheme { MajorLatinFont = "Cambria", MinorLatinFont = "Calibri" };
        var resolver = Resolver("""
            <w:docDefaults>
              <w:rPrDefault><w:rPr><w:rFonts w:asciiTheme="minorHAnsi"/></w:rPr></w:rPrDefault>
            </w:docDefaults>
            <w:style w:type="paragraph" w:styleId="Heading1">
              <w:rPr><w:rFonts w:asciiTheme="majorHAnsi"/></w:rPr>
            </w:style>
            """, theme);

        Assert.Equal("Calibri", resolver.ResolveRun(null, null).FontFamily);
        Assert.Equal("Cambria",
            resolver.ResolveRun(new ParagraphProperties { StyleId = "Heading1" }, null).FontFamily);
    }

    [Fact]
    public void A_literal_font_name_overrides_an_inherited_theme_slot()
    {
        var theme = new DocumentTheme { MinorLatinFont = "Calibri" };
        var resolver = Resolver("""
            <w:docDefaults>
              <w:rPrDefault><w:rPr><w:rFonts w:asciiTheme="minorHAnsi"/></w:rPr></w:rPrDefault>
            </w:docDefaults>
            """, theme);

        // Picking a font from the ribbon writes a literal name, which must displace the slot
        // rather than lose to it.
        var run = resolver.ResolveRun(null, new RunProperties { AsciiFont = "Verdana" });
        Assert.Equal("Verdana", run.FontFamily);
    }

    [Fact]
    public void Hanging_and_first_line_indents_are_mutually_exclusive()
    {
        var resolver = Resolver("""
            <w:style w:type="paragraph" w:styleId="ListLike">
              <w:pPr><w:ind w:left="720" w:hanging="360"/></w:pPr>
            </w:style>
            """);

        var hanging = resolver.ResolveParagraph(new ParagraphProperties { StyleId = "ListLike" });
        Assert.Equal(36, hanging.IndentLeftPoints);
        Assert.Equal(-18, hanging.IndentFirstLinePoints);

        // A direct first-line indent must clear the style's hanging indent, not stack with it.
        var overridden = resolver.ResolveParagraph(new ParagraphProperties
        {
            StyleId = "ListLike",
            IndentFirstLineTwips = 240
        });
        Assert.Equal(12, overridden.IndentFirstLinePoints);
    }

    [Fact]
    public void Line_spacing_rules_convert_correctly()
    {
        var resolver = Resolver(string.Empty);

        var single = resolver.ResolveParagraph(new ParagraphProperties());
        Assert.Equal(LineSpacingRule.Auto, single.LineRule);
        Assert.Equal(1.0, single.LineSpacingMultiple);

        // 360 in 240ths is one-and-a-half lines.
        var oneAndAHalf = resolver.ResolveParagraph(new ParagraphProperties { Line = 360, LineRule = LineSpacingRule.Auto });
        Assert.Equal(1.5, oneAndAHalf.LineSpacingMultiple);

        // With an exact rule the same field is twips instead.
        var exact = resolver.ResolveParagraph(new ParagraphProperties { Line = 360, LineRule = LineSpacingRule.Exact });
        Assert.Equal(18, exact.LineSpacingPoints);
    }

    [Fact]
    public void Paragraph_mark_formatting_is_resolved_for_empty_paragraphs()
    {
        var resolver = Resolver("""
            <w:docDefaults>
              <w:rPrDefault><w:rPr><w:sz w:val="22"/></w:rPr></w:rPrDefault>
            </w:docDefaults>
            """);

        // An empty paragraph still occupies a line, and its height comes from the mark's own run
        // properties rather than from any run.
        var format = resolver.ResolveParagraph(new ParagraphProperties
        {
            MarkRunProperties = new RunProperties { SizeHalfPoints = 48 }
        });

        Assert.Equal(24, format.MarkFormat.FontSizePoints);
    }

    [Fact]
    public void Superscript_reduces_the_drawn_size_and_raises_the_baseline()
    {
        var resolver = Resolver(string.Empty);

        var superscript = resolver.ResolveRun(null, new RunProperties
        {
            SizeHalfPoints = 24,
            VerticalAlignment = VerticalTextAlignment.Superscript
        });

        Assert.Equal(12, superscript.FontSizePoints);
        Assert.True(superscript.EffectiveFontSizePoints < 12);
        Assert.True(superscript.BaselineShiftPoints > 0);

        var subscript = resolver.ResolveRun(null, new RunProperties
        {
            SizeHalfPoints = 24,
            VerticalAlignment = VerticalTextAlignment.Subscript
        });
        Assert.True(subscript.BaselineShiftPoints < 0);
    }

    [Fact]
    public void Colours_convert_to_pdf_components()
    {
        var resolver = Resolver(string.Empty);

        var red = resolver.ResolveRun(null, new RunProperties { Color = "FF0000" }).GetColor();
        Assert.Equal((1.0, 0.0, 0.0), red);

        // Unspecified and malformed colours both render black rather than throwing.
        Assert.Equal((0.0, 0.0, 0.0), resolver.ResolveRun(null, null).GetColor());
        Assert.Equal((0.0, 0.0, 0.0), resolver.ResolveRun(null, new RunProperties { Color = "ZZZZZZ" }).GetColor());
    }

    private static StyleResolver Resolver(string stylesXmlBody, DocumentTheme? theme = null)
    {
        var xml = XDocument.Parse($"""
            <w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              {stylesXmlBody}
            </w:styles>
            """);

        return new StyleResolver(StylesParser.Parse(xml), theme);
    }
}
