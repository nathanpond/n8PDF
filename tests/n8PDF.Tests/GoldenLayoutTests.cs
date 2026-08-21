using n8PDF;
using n8PDF.Diagnostics;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tier 1: every fixture's layout is compared against a committed trace. These catch any
/// regression to the millipoint and name the run that moved.
/// </summary>
public class GoldenLayoutTests
{
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
    public void Layout_matches_its_golden(string name)
    {
        if (TestFonts.SkipForMissingFonts(name)) return;

        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };

        using var stream = new MemoryStream(Fixtures.Build(name));
        var trace = LayoutTrace.Write(Converter.LayoutDocument(stream, options));

        GoldenFile.Verify(name, trace);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Fixture_converts_to_a_pdf_with_matching_page_count(string name)
    {
        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };
        var docx = Fixtures.Build(name);

        using var stream = new MemoryStream(docx);
        var laidOut = Converter.LayoutDocument(stream, options);
        var pdf = Converter.Convert(docx, options);

        TestPaths.WriteArtifact(name + ".pdf", pdf);

        // The PDF must contain exactly the pages layout produced, read back out of the file
        // rather than trusted from the builder.
        var geometry = PdfInspector.Inspect(pdf);
        Assert.Equal(laidOut.Pages.Count, geometry.PageCount);

        for (var i = 0; i < laidOut.Pages.Count; i++)
        {
            Assert.Equal(laidOut.Pages[i].WidthPoints, geometry.MediaBoxes[i].Width, 1);
            Assert.Equal(laidOut.Pages[i].HeightPoints, geometry.MediaBoxes[i].Height, 1);
        }
    }

    [Fact]
    public void Fixtures_are_written_to_disk_for_inspection_in_word()
    {
        // The same documents the goldens describe, as files that can be opened in Word and
        // exported to Fixtures/Reference for the Tier 3 comparison.
        var written = Fixtures.MaterializeAll();

        Assert.Equal(Fixtures.All.Count, written.Count);
        Assert.All(written, path => Assert.True(new FileInfo(path).Length > 0));
    }

    [Fact]
    public void Real_fixture_seeds_are_written_for_word_to_round_trip()
    {
        // Written to the artifacts directory rather than into the repository: these are input to
        // tools/make-real-fixtures.sh, and what gets committed is Word's version of them.
        var directory = Path.Combine(TestPaths.Artifacts, "real-seeds");
        var written = Fixtures.MaterializeRealSeeds(directory);

        Assert.Equal(Fixtures.RealSeeds.Count, written.Count);
        Assert.All(written, path => Assert.True(new FileInfo(path).Length > 0));
    }

    [Fact]
    public void Every_fixture_has_a_committed_golden()
    {
        // A fixture with no golden silently tests nothing, since Verify writes one on first run.
        var missing = Fixtures.All.Keys
            .Where(name => !File.Exists(GoldenFile.PathFor(name)))
            .ToList();

        Assert.True(missing.Count == 0,
            $"These fixtures have no committed golden: {string.Join(", ", missing)}. " +
            "Run the suite once to generate them, then commit the Golden directory.");
    }
}
