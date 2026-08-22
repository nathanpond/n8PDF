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
    /// Counted rather than weighed, and counted on a library of its own. What this used to do was
    /// measure the memory the whole process held before and after resolving a family: under the
    /// parallel run every other test allocating at the same moment went into the answer, and it
    /// failed for reasons that had nothing to do with fonts. It also made the machine's own font
    /// collection part of the test.
    ///
    /// What is actually being asserted is the library's own arrangement — that a face indexed
    /// from a directory learns its name and reads the rest of itself only when something asks for
    /// the face — so the test indexes a directory it wrote itself and asks that directly. Three
    /// files in, nothing read; one family out, one file read.
    /// </remarks>
    [Fact]
    public void Resolving_one_family_does_not_read_the_rest()
    {
        // Two faces of the family that is asked for and one of another, so that what is read is
        // the face that was chosen rather than merely the family that was named.
        string[] fonts =
            [TestFonts.TimesNewRomanPath, TestFonts.TimesNewRomanBoldPath, TestFonts.ArialPath];

        if (fonts.Any(font => !File.Exists(font)))
        {
            _output.WriteLine("the faces this indexes are not on this machine; nothing to count.");
            return;
        }

        var directory = Directory.CreateTempSubdirectory("n8pdf-fonts-").FullName;

        try
        {
            foreach (var font in fonts)
                File.Copy(font, Path.Combine(directory, Path.GetFileName(font)));

            var library = new FontLibrary { UseSystemFonts = false };
            var indexed = library.IndexDirectory(directory);

            _output.WriteLine($"{indexed} faces indexed, {library.FacesRead} read");

            Assert.Equal(fonts.Length, indexed);
            Assert.Equal(0, library.FacesRead);

            Assert.True(library.TryResolve("Times New Roman", false, false, out var selection),
                "the family that was just indexed did not resolve.");

            Assert.False(selection.Font.IsBold, "the upright face was not the one chosen.");

            _output.WriteLine($"after resolving one family, {library.FacesRead} read");

            Assert.Equal(1, library.FacesRead);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
