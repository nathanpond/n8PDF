using n8PDF.Fonts;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What it costs a conversion to find the fonts it needs.
/// </summary>
/// <remarks>
/// A library that says nothing about fonts discovers the platform's, and the platform's are 651
/// files and 1.3GB on the machine this was measured on. Reading all of them to draw a page of
/// Calibri took 450ms and held 1.5GB while it did it — and a second conversion did the whole thing
/// again, because nothing was kept.
///
/// Two things fixed it. The index — every face's name, style and file — is read once for the
/// process and shared by every library that wants the platform's fonts; and a face reads its own
/// file only when something asks for the face itself. A conversion that sets two families now
/// reads two files rather than six hundred, and the second conversion reads none.
/// </remarks>
public class FontLibraryCacheTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// Two libraries asking for the same family get the same face, not two of it.
    /// </summary>
    /// <remarks>
    /// This is the whole of the saving stated as a fact: if they were different objects they would
    /// be different copies of the same file, and a hundred conversions would be a hundred copies.
    /// </remarks>
    [Fact]
    public void Two_libraries_share_the_face_they_both_want()
    {
        var first = new FontLibrary();
        var second = new FontLibrary();

        if (!first.TryResolve("Calibri", false, false, out var one) ||
            !second.TryResolve("Calibri", false, false, out var other))
        {
            _output.WriteLine("Calibri is not installed; nothing to compare.");
            return;
        }

        Assert.Same(one.Font, other.Font);
    }

    /// <summary>
    /// And a library that wants one family does not read the rest.
    /// </summary>
    /// <remarks>
    /// Measured rather than asserted exactly: what is held after resolving one family used to be
    /// the whole of the platform's fonts, which was 1.5GB here. A hundred megabytes is far more
    /// than a face or two costs and far less than reading everything, so it tells the two apart
    /// without turning into a test of this machine's font collection.
    /// </remarks>
    [Fact]
    public void Resolving_one_family_does_not_read_the_rest()
    {
        // Warm the index first: what is being measured is what a resolve costs, not what the one
        // scan of the platform's directories costs.
        new FontLibrary().TryResolve("Calibri", false, false, out _);

        var before = GC.GetTotalMemory(true);

        var library = new FontLibrary();
        library.TryResolve("Times New Roman", false, false, out _);

        var after = GC.GetTotalMemory(true);
        var held = (after - before) / 1024.0 / 1024;

        _output.WriteLine($"{library.RegisteredFaceCount} faces indexed, {held:0.0}MB held");

        Assert.True(held < 100,
            $"resolving one family held {held:0.0}MB, which is the whole of the platform's fonts " +
            "rather than the one that was asked for.");
    }

    /// <summary>
    /// The same document converted on several threads at once comes out the same every time.
    /// </summary>
    /// <remarks>
    /// The index is shared and so are the faces in it, which means two conversions can be reading
    /// the same font's tables at the same moment. A face that read them without a lock would let
    /// one of the two carry on with tables the other had not finished reading, and what came out
    /// would depend on the timing.
    /// </remarks>
    [Fact]
    public void The_same_document_on_many_threads_comes_out_the_same()
    {
        var docx = Fixtures.Build("paragraph-spacing");
        var expected = Converter.Convert(docx, Options());

        var results = new byte[8][];

        Parallel.For(0, results.Length, i => results[i] = Converter.Convert(docx, Options()));

        foreach (var result in results) Assert.Equal(expected, result);

        static ConversionOptions Options() =>
            new() { CreationDate = DateTimeOffset.UnixEpoch, Fonts = TestFonts.CreatePinnedLibrary() };
    }

    /// <summary>
    /// And with the platform's own fonts, where the faces really are shared between the threads.
    /// </summary>
    [Fact]
    public void The_same_document_on_many_threads_shares_its_faces()
    {
        var docx = Fixtures.Build("paragraph-spacing");

        var results = new byte[8][];

        Parallel.For(0, results.Length, i =>
            results[i] = Converter.Convert(docx, new ConversionOptions
            {
                CreationDate = DateTimeOffset.UnixEpoch
            }));

        foreach (var result in results) Assert.Equal(results[0], result);
    }
}
