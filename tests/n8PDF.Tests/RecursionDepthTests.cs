using System.Xml.Linq;
using n8PDF.Ooxml;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The parser's recursive descents are bounded (#143–#146): a document that nests inline
/// wrappers, tables, equations or text boxes without end is truncated rather than overflowing
/// the call stack and killing the process.
/// </summary>
/// <remarks>
/// <c>StackOverflowException</c> cannot be caught in .NET, so a test cannot assert on it — it
/// would take the whole run down. Instead each vector is parsed on a thread with a deliberately
/// 1 MB stack: the guard caps the walk at a few hundred frames, which fits easily, so
/// the parse returns and the thread sets its result. Were the guard removed, the same deep
/// document would overflow that stack and kill the process — a regression this test makes
/// loud rather than silent. The nesting used here (tens of thousands of levels) is far past the
/// 256-level cap and far past anything Word writes.
/// </remarks>
public class RecursionDepthTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private const string DocumentNamespaces =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\"";

    private static XDocument Document(string body) =>
        XDocument.Parse($"<w:document {DocumentNamespaces}><w:body>{body}</w:body></w:document>");

    /// <summary>Parses on a small-stack thread; the returned document proves the walk was bounded.</summary>
    private WordDocument ParseBounded(XDocument xml)
    {
        WordDocument? result = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try { result = DocumentParser.Parse(xml); }
            catch (Exception e) { failure = e; }
        }, maxStackSize: 1024 * 1024);

        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(30));

        Assert.True(finished, "the parse did not finish — the recursion is not bounded");
        Assert.Null(failure);
        Assert.NotNull(result);
        return result!;
    }

    [Fact]
    public void Nested_inline_wrappers_do_not_overflow_the_stack()
    {
        const int depth = 50_000;
        var body = "<w:p>" + string.Concat(Enumerable.Repeat("<w:ins>", depth)) +
                   "<w:r><w:t>deep</w:t></w:r>" +
                   string.Concat(Enumerable.Repeat("</w:ins>", depth)) + "</w:p>";

        var document = ParseBounded(Document(body));
        _output.WriteLine($"{depth} nested w:ins parsed to {document.Body.Count} block(s)");
        Assert.NotEmpty(document.Body);
    }

    [Fact]
    public void Nested_tables_do_not_overflow_the_stack()
    {
        const int depth = 12_000;
        var open = "<w:tbl><w:tr><w:tc>";
        var close = "</w:tc></w:tr></w:tbl>";
        var body = string.Concat(Enumerable.Repeat(open, depth)) +
                   "<w:p><w:r><w:t>deep</w:t></w:r></w:p>" +
                   string.Concat(Enumerable.Repeat(close, depth));

        var document = ParseBounded(Document(body));
        _output.WriteLine($"{depth} nested w:tbl parsed to {document.Body.Count} block(s)");
        Assert.NotEmpty(document.Body);
    }

    [Fact]
    public void Nested_equations_do_not_overflow_the_stack()
    {
        const int depth = 50_000;
        var body = "<w:p><w:r><m:oMath>" +
                   string.Concat(Enumerable.Repeat("<m:e>", depth)) +
                   "<m:r><m:t>x</m:t></m:r>" +
                   string.Concat(Enumerable.Repeat("</m:e>", depth)) +
                   "</m:oMath></w:r></w:p>";

        var document = ParseBounded(Document(body));
        _output.WriteLine($"{depth} nested m:e parsed to {document.Body.Count} block(s)");
        Assert.NotEmpty(document.Body);
    }

    [Fact]
    public void Nested_text_boxes_do_not_overflow_the_stack()
    {
        const int depth = 5_000;
        var open =
            "<w:p><w:r><w:drawing><wp:inline><a:graphic><a:graphicData " +
            "uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
            "<wps:wsp><wps:txbx><w:txbxContent>";
        var close =
            "</w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing></w:r></w:p>";

        var body = string.Concat(Enumerable.Repeat(open, depth)) +
                   "<w:p><w:r><w:t>deep</w:t></w:r></w:p>" +
                   string.Concat(Enumerable.Repeat(close, depth));

        var document = ParseBounded(Document(body));
        _output.WriteLine($"{depth} nested text boxes parsed to {document.Body.Count} block(s)");
        Assert.NotEmpty(document.Body);
    }
}
