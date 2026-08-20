using System.Xml.Linq;
using n8PDF.Ooxml;
using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Content wrapped in something that is neither a paragraph nor a table.
/// </summary>
/// <remarks>
/// A body holds paragraphs and tables, and a reader that walks it looking only for those two is
/// right about every document written by hand and wrong about most documents written by Word. A
/// content control wraps the cover page, the table of contents and every placeholder a template
/// leaves to be filled in; a compatibility alternative wraps content offered twice over; the
/// custom XML element wraps whatever an old document tagged. All three hold ordinary blocks, and
/// all three used to be passed over here — which lost everything inside them, in silence, on
/// exactly the documents most likely to hold them.
///
/// What Word does with each is measured in <c>content-controls</c>, and the answer is the plain
/// one: it draws what is inside, where it would have been anyway.
/// </remarks>
public class ContentControlTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static XElement Body(string inner) =>
        XElement.Parse($"""
            <w:body xmlns:w="{W.Main}"
                    xmlns:mc="{W.Compatibility}">
              {inner}
            </w:body>
            """);

    /// <summary>Every kind of wrapper gives up what it holds.</summary>
    [Theory]
    [InlineData("<w:sdt><w:sdtPr/><w:sdtContent><w:p/></w:sdtContent></w:sdt>", "p")]
    [InlineData("<w:sdt><w:sdtPr/><w:sdtContent><w:tbl/></w:sdtContent></w:sdt>", "tbl")]
    [InlineData("<w:customXml w:element=\"x\"><w:p/></w:customXml>", "p")]
    [InlineData("<mc:AlternateContent><mc:Choice Requires=\"w14\"><w:p/></mc:Choice>" +
                "<mc:Fallback><w:tbl/></mc:Fallback></mc:AlternateContent>", "p")]
    [InlineData("<mc:AlternateContent><mc:Fallback><w:tbl/></mc:Fallback></mc:AlternateContent>", "tbl")]
    public void A_wrapper_gives_up_what_it_holds(string markup, string expected)
    {
        var blocks = DocumentParser.Blocks(Body(markup)).ToList();

        var block = Assert.Single(blocks);
        Assert.Equal(expected, block.Name.LocalName);
    }

    /// <summary>
    /// And a wrapper inside a wrapper, which is what a control round a table of contents holds.
    /// </summary>
    [Fact]
    public void Wrappers_nest()
    {
        var blocks = DocumentParser.Blocks(Body("""
            <w:sdt><w:sdtPr/><w:sdtContent>
              <w:p/>
              <w:sdt><w:sdtPr/><w:sdtContent><w:tbl/></w:sdtContent></w:sdt>
            </w:sdtContent></w:sdt>
            """)).ToList();

        Assert.Equal(["p", "tbl"], blocks.Select(block => block.Name.LocalName));
    }

    /// <summary>
    /// Anything that is not a wrapper is handed on untouched, so that what a body says about its
    /// own section still reaches the reader that wants it.
    /// </summary>
    [Fact]
    public void Everything_else_is_passed_through()
    {
        var blocks = DocumentParser.Blocks(Body("<w:p/><w:sectPr/>")).ToList();

        Assert.Equal(["p", "sectPr"], blocks.Select(block => block.Name.LocalName));
    }

    /// <summary>
    /// A document written to be awkward can wrap a wrapper as many times as it likes; the walk
    /// stops rather than running out of stack.
    /// </summary>
    [Fact]
    public void A_walk_of_wrappers_ends()
    {
        var markup = "<w:p/>";
        for (var i = 0; i < 500; i++)
            markup = $"<w:sdt><w:sdtPr/><w:sdtContent>{markup}</w:sdtContent></w:sdt>";

        Assert.Empty(DocumentParser.Blocks(Body(markup)));
    }

    /// <summary>
    /// And the whole of it on a page: a control in the body, round a table, inside a cell, two
    /// deep, in a running head and in a note, an alternative's choice, and the old custom XML.
    /// </summary>
    [Fact]
    public void Everything_wrapped_reaches_the_page()
    {
        var pdf = Converter.Convert(Fixtures.Build("content-controls"),
            new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() });

        var text = string.Concat(PdfTextExtractor.Extract(pdf).Select(run => run.Text));
        _output.WriteLine(text);

        foreach (var line in new[]
                 {
                     "Before the control.", "Inside a control.", "After the control.",
                     "A table inside a control.", "A control inside a cell.",
                     "Two controls deep.", "Inside custom XML.",
                     "A running head in a control.", "A note wrapped in a control."
                 })
        {
            Assert.Contains(line, text);
        }

        // Word draws the choice of an alternative, not the fallback, which is what the two
        // different lines on that page are there to say.
        Assert.Contains("The choice, not the fallback.", text);
        Assert.DoesNotContain("The fallback, not the choice.", text);
    }
}
