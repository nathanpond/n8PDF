using n8PDF;
using n8PDF.Text;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The quadratic hazards found by the complexity audit are bounded: adversarial input that made a
/// scan O(N²) now completes in time rather than hanging (#211, #213, #214, #215).
/// </summary>
public class ComplexityTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static void CompletesWithin(string what, TimeSpan bound, Action act)
    {
        var thread = new Thread(() => act()) { IsBackground = true };
        thread.Start();
        Assert.True(thread.Join(bound), $"{what} did not finish within {bound.TotalSeconds}s — not bounded");
    }

    [Fact]
    public void Bidi_resolves_control_interleaved_text_in_time()   // #214, #215
    {
        // 60,000 characters alternating a letter with an explicit-formatting control (dropped to
        // BN), plus a run of isolates — the shapes the O(N²) scans hung on.
        var interleaved = string.Concat(Enumerable.Repeat("a‪", 30_000));   // a + LRE
        var isolates = string.Concat(Enumerable.Repeat("⁦", 20_000));        // LRI run

        CompletesWithin("bidi on control-interleaved text", TimeSpan.FromSeconds(10),
            () => { Bidi.Resolve(interleaved); Bidi.Resolve(isolates); });
        _output.WriteLine("bidi resolved 60k interleaved + 20k isolates without hanging");
    }

    [Fact]
    public void A_single_enormous_word_is_laid_out_in_time()   // #213
    {
        // One unbreakable word of 150,000 letters in a normal-width document. The old SplitToFit
        // allocated a string per letter per line over the shrinking remainder — O(L²).
        var docx = new DocxBuilder()
            .AddParagraph(new string('m', 150_000))
            .Build();

        CompletesWithin("layout of a 150,000-letter word", TimeSpan.FromSeconds(20),
            () => Converter.LayoutDocument(new MemoryStream(docx),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));
        _output.WriteLine("a 150,000-letter word laid out without hanging");
    }

    [Fact]
    public void A_chart_axis_with_an_abusive_major_unit_is_bounded()   // #241
    {
        // A value axis whose XML asks for 100 million marks — min 0, max 1e8, majorUnit 1. Word
        // ignores such a unit and draws tens; the old Marked loop honoured it literally and drew
        // 10^8, a DoS from a few hundred bytes. The cap holds it to MostMarks.
        const string chart = """
            <c:chart><c:plotArea>
              <c:barChart>
                <c:barDir val="col"/><c:grouping val="clustered"/>
                <c:ser><c:idx val="0"/>
                  <c:val><c:numRef><c:numCache><c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt></c:numCache></c:numRef></c:val>
                </c:ser>
              </c:barChart>
              <c:valAx><c:axId val="2"/>
                <c:scaling><c:min val="0"/><c:max val="100000000"/></c:scaling>
                <c:axPos val="l"/><c:majorGridlines/><c:majorUnit val="1"/>
              </c:valAx>
              <c:catAx><c:axId val="1"/><c:axPos val="b"/></c:catAx>
            </c:plotArea></c:chart>
            """;

        var docx = new DocxBuilder()
            .WithChart(chart)
            .AddRawParagraph($"<w:p>{DocxBuilder.ChartDrawing(300, 200)}</w:p>")
            .Build();

        CompletesWithin("layout of a chart whose axis asks for 10^8 marks", TimeSpan.FromSeconds(20),
            () => Converter.LayoutDocument(new MemoryStream(docx),
                new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() }));
        _output.WriteLine("an abusive-major-unit chart axis was laid out without hanging");
    }
}
