namespace n8PDF.Tests.Support;

/// <summary>
/// Compares generated output against a committed snapshot.
/// </summary>
/// <remarks>
/// Goldens are self-referential by design: they prove nothing changed, not that the output is
/// correct. Correctness comes from the reference comparison against Word. When that says we have
/// improved, the goldens get re-blessed by setting <c>N8PDF_BLESS=1</c>.
/// </remarks>
public static class GoldenFile
{
    private const string BlessVariable = "N8PDF_BLESS";

    /// <summary>True when the run has been asked to overwrite goldens instead of asserting.</summary>
    public static bool IsBlessing =>
        Environment.GetEnvironmentVariable(BlessVariable) is "1" or "true";

    public static string PathFor(string name) => Path.Combine(TestPaths.Golden, name + ".json");

    /// <summary>
    /// Asserts that the value matches the stored golden, writing it instead when blessing or
    /// when no golden exists yet.
    /// </summary>
    public static void Verify(string name, string actual)
    {
        var path = PathFor(name);
        Directory.CreateDirectory(TestPaths.Golden);

        if (IsBlessing || !File.Exists(path))
        {
            File.WriteAllText(path, actual);
            return;
        }

        var expected = File.ReadAllText(path);
        if (Normalize(expected) == Normalize(actual)) return;

        // Write the actual output next to the golden so the difference can be diffed directly
        // rather than reconstructed from an assertion message.
        var actualPath = Path.Combine(TestPaths.Artifacts, name + ".actual.json");
        File.WriteAllText(actualPath, actual);

        Assert.Fail(
            $"Layout for '{name}' differs from its golden.\n" +
            $"  golden: {path}\n" +
            $"  actual: {actualPath}\n" +
            $"  first difference: {DescribeFirstDifference(expected, actual)}\n" +
            $"Re-bless with {BlessVariable}=1 once the change is understood to be an improvement.");
    }

    /// <summary>Reports the first differing line, which is the run whose position moved.</summary>
    private static string DescribeFirstDifference(string expected, string actual)
    {
        var expectedLines = expected.ReplaceLineEndings("\n").Split('\n');
        var actualLines = actual.ReplaceLineEndings("\n").Split('\n');

        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var expectedLine = i < expectedLines.Length ? expectedLines[i] : "(missing)";
            var actualLine = i < actualLines.Length ? actualLines[i] : "(missing)";

            if (expectedLine == actualLine) continue;

            return $"line {i + 1}\n    expected: {expectedLine.Trim()}\n    actual:   {actualLine.Trim()}";
        }

        return "(whitespace only)";
    }

    private static string Normalize(string value) => value.ReplaceLineEndings("\n").TrimEnd();
}
