using System.Diagnostics;
using n8PDF;
using n8PDF.Fonts;
using n8PDF.Packaging;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Caller-controlled cancellation through the conversion (#267). The failure mode of a document
/// converter is availability, and the defence in depth behind the depth and allocation caps is a
/// <see cref="CancellationToken"/> the layout and render loops honour at coarse boundaries — so a
/// caller who forgets to isolate a hostile document still cannot be hung indefinitely.
/// </summary>
public class CancellationTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>A document of very many blocks — the layout loop that carries the check iterates them.</summary>
    private static byte[] ManyBlocks(int paragraphs)
    {
        var builder = new DocxBuilder();
        for (var i = 0; i < paragraphs; i++)
            builder.AddParagraph("The quick brown fox jumps over the lazy dog, again and again.");

        return builder.Build();
    }

    private static ConversionOptions Options() =>
        new() { Fonts = TestFonts.CreatePinnedLibrary() };

    [Fact]
    public void An_already_cancelled_token_stops_the_conversion()
    {
        var thrown = Record.Exception(() =>
            Converter.Convert(ManyBlocks(2_000), Options(), new CancellationToken(canceled: true)));

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
    }

    [Fact]
    public void Cancellation_is_distinct_from_the_package_and_format_exceptions()
    {
        // The documented type is OperationCanceledException, not one of the package or format types
        // a malformed document raises — a caller can tell "I stopped this" from "this was broken".
        var thrown = Record.Exception(() =>
            Converter.Convert(ManyBlocks(2_000), Options(), new CancellationToken(canceled: true)));

        Assert.IsAssignableFrom<OperationCanceledException>(thrown);
        Assert.IsNotType<PackageTooLargeException>(thrown);
        Assert.IsNotType<FontFormatException>(thrown);
    }

    [Fact]
    public void A_pathological_document_aborts_rather_than_running_to_completion()
    {
        // A hundred thousand blocks lay out in about ten seconds uncancelled; with the deadline set
        // to fifty milliseconds, the run must stop far short of that. Run on a background thread so
        // a cancellation that failed to bind shows as a stuck join at the bound rather than as the
        // whole ten-second run — and so the bound catches it whatever the machine's load.
        var docx = ManyBlocks(100_000);

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));
        var token = cts.Token;

        Exception? captured = null;
        var thread = new Thread(() =>
            captured = Record.Exception(() => Converter.Convert(docx, Options(), token)))
            { IsBackground = true };

        var elapsed = Stopwatch.StartNew();
        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(8));
        elapsed.Stop();

        Assert.True(finished,
            "the conversion did not stop within 8s of a 50ms deadline — cancellation is not bounding it");
        Assert.IsAssignableFrom<OperationCanceledException>(captured);

        _output.WriteLine(
            $"a 100,000-block document (about 10s of layout) stopped after {elapsed.ElapsedMilliseconds} ms");
    }
}
