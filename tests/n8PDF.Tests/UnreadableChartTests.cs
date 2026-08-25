using n8PDF.Tests.Support;
using n8PDF.Tests.Support.PdfReading;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A chart the reader cannot parse still holds the frame Word gives it, so the document below it
/// does not reflow (#95).
/// </summary>
/// <remarks>
/// A chart drawing carries no image fallback, so before #95 a chart that could not be read
/// contributed no extent at all and every block after it moved up the page. Word keeps the frame —
/// a picture it cannot render it drops, but a chart it does not — and so does this now. There are
/// three ways a chart goes unread, and all three reserve the frame the same: its part absent, its
/// XML unparseable, and a plot element the reader does not know.
/// </remarks>
public class UnreadableChartTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static ConversionOptions Options() => new() { Fonts = TestFonts.CreatePinnedLibrary() };

    // A chart the reader parses: a clustered column with one series and its two axes.
    private const string Readable = """
                                    <c:chart><c:plotArea>
                                      <c:barChart><c:barDir val="col"/><c:grouping val="clustered"/>
                                        <c:ser><c:idx val="0"/><c:order val="0"/>
                                          <c:val><c:numRef><c:numCache><c:ptCount val="2"/>
                                            <c:pt idx="0"><c:v>40</c:v></c:pt><c:pt idx="1"><c:v>80</c:v></c:pt>
                                          </c:numCache></c:numRef></c:val></c:ser>
                                        <c:axId val="1"/><c:axId val="2"/></c:barChart>
                                      <c:catAx><c:axId val="1"/><c:scaling><c:orientation val="minMax"/></c:scaling>
                                        <c:delete val="0"/><c:crossAx val="2"/></c:catAx>
                                      <c:valAx><c:axId val="2"/><c:scaling><c:orientation val="minMax"/></c:scaling>
                                        <c:delete val="0"/><c:crossAx val="1"/></c:valAx>
                                    </c:plotArea></c:chart>
                                    """;

    // A plot element outside the ones the reader knows.
    private const string UnknownElement = "<c:chart><c:plotArea><c:tesseractChart/></c:plotArea></c:chart>";

    private static byte[] With360Chart(Func<DocxBuilder, DocxBuilder> chartPart) =>
        chartPart(new DocxBuilder())
            .AddRawParagraph($"<w:p>{DocxBuilder.ChartDrawing(360, 216)}</w:p>")
            .AddParagraph("AFTER")
            .Build();

    private (int Page, double Y)? After(byte[] docx)
    {
        var run = PdfTextExtractor.Extract(Converter.Convert(docx, Options()))
            .FirstOrDefault(r => r.Text.Contains("AFTER"));
        return run is null ? null : (run.PageIndex, Math.Round(run.BaselineY, 1));
    }

    [Fact]
    public void An_unreadable_chart_keeps_its_frame_so_the_text_below_does_not_move()
    {
        var readable = After(With360Chart(b => b.WithChart(Readable)));
        var absent = After(With360Chart(b => b)); // the drawing names rIdChart, which no part answers
        var unparseable = After(With360Chart(b => b.WithPart(
            "word/charts/chart1.xml",
            "application/vnd.openxmlformats-officedocument.drawingml.chart+xml",
            "this is not a chart, or xml",
            fromDocument: ("rIdChart", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"))));
        var unknown = After(With360Chart(b => b.WithChart(UnknownElement)));
        var noDrawing = After(new DocxBuilder().AddParagraph("AFTER").Build());

        _output.WriteLine($"readable={readable}  absent={absent}  unparseable={unparseable}  " +
                          $"unknown={unknown}  no-drawing={noDrawing}");

        Assert.NotNull(readable);
        Assert.NotNull(noDrawing);

        // All three ways of being unreadable land the text exactly where a readable chart does:
        // the 216-point frame is reserved whatever the reader could not understand.
        Assert.Equal(readable, absent);
        Assert.Equal(readable, unparseable);
        Assert.Equal(readable, unknown);

        // And put back wrong — no frame at all — the text is a whole chart's height higher up, which
        // is the reflow this guards against.
        Assert.NotEqual(readable.Value.Y, noDrawing.Value.Y);
        Assert.True(Math.Abs(readable.Value.Y - noDrawing.Value.Y) > 180,
            $"the reserved frame should move the text about a chart's height (216pt); " +
            $"readable {readable.Value.Y}, no drawing {noDrawing.Value.Y}");
    }
}