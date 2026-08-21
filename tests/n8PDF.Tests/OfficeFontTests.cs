using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// Whether this machine can compare against Word at all, and says so when it cannot.
/// </summary>
/// <remarks>
/// Word's reference PDFs are set in the faces Word brings with it — Calibri, Cambria, and the
/// Japanese and Chinese faces it carries rather than takes from the system. A machine without Word
/// cannot render those fixtures as Word rendered them, so the tests that compare them are left
/// alone rather than failed.
///
/// Which is exactly the arrangement the reference PDFs themselves warn against: a silently skipped
/// tier is indistinguishable from a passing one. So this states plainly how much was skipped, and
/// <c>N8PDF_REQUIRE_OFFICE_FONTS=1</c> turns the skip into a failure — which is what the full CI
/// run sets, on a machine that has Word.
/// </remarks>
public class OfficeFontTests(ITestOutputHelper output)
{
    [Fact]
    public void The_faces_word_brings_are_available_or_explicitly_optional()
    {
        var all = Fixtures.All.Keys.Concat(TestFonts.RealDocuments().Select(d => d.Name)).ToList();
        var needed = all.Where(TestFonts.NeedsOfficeFonts).ToList();

        if (TestFonts.OfficeFontsAvailable)
        {
            output.WriteLine($"Word's faces found; {needed.Count} of {all.Count} documents ask for one.");
            return;
        }

        var message =
            $"{TestFonts.OfficeFontsUnavailableMessage}\n\n" +
            $"{needed.Count} of {all.Count} documents are affected: {string.Join(", ", needed)}";

        Assert.False(TestFonts.OfficeFontsRequired, message);
        output.WriteLine(message);
    }

    /// <summary>
    /// The list of fixtures written in Word's own faces, checked by rendering each fixture twice —
    /// once with those faces and once without — and seeing which files come out different. A list
    /// that has gone stale would silently skip a fixture that could have been compared, or fail
    /// one that could not; this is what stops either.
    /// </summary>
    [Fact]
    public void The_list_of_fixtures_needing_word_s_faces_is_current()
    {
        if (TestFonts.SkipForMissingFaces()) return;

        var measured = TestFonts.MeasureWhichNeedOfficeFonts().ToList();
        var listed = Fixtures.All.Keys.Concat(TestFonts.RealDocuments().Select(d => d.Name))
            .Where(TestFonts.NeedsOfficeFonts).Order().ToList();

        var missing = measured.Except(listed).ToList();
        var extra = listed.Except(measured).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"TestFonts.WrittenInOfficeFaces is out of date.\n" +
            $"Add: {string.Join(", ", missing)}\nRemove: {string.Join(", ", extra)}");
    }
}
