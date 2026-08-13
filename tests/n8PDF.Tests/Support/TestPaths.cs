using System.Reflection;

namespace n8PDF.Tests.Support;

/// <summary>
/// Locates repo directories from the test binary. Fixtures and goldens are read from the source
/// tree rather than from copied output so that goldens can be re-blessed in place.
/// </summary>
public static class TestPaths
{
    private static readonly Lazy<string> RepoRootLazy = new(FindRepoRoot);

    public static string RepoRoot => RepoRootLazy.Value;

    public static string TestProject => Path.Combine(RepoRoot, "tests", "n8PDF.Tests");

    public static string MinimalFixtures => Path.Combine(TestProject, "Fixtures", "Minimal");

    public static string RealFixtures => Path.Combine(TestProject, "Fixtures", "Real");

    public static string ReferencePdfs => Path.Combine(TestProject, "Fixtures", "Reference");

    /// <summary>
    /// Pictures committed as files rather than written by the tests. Only what nothing on this
    /// machine can produce on demand belongs here — a JPEG of separated inks, which no converter
    /// installed alongside these tests will write.
    /// </summary>
    public static string ImageFixtures => Path.Combine(TestProject, "Fixtures", "Images");

    public static string Golden => Path.Combine(TestProject, "Golden");

    /// <summary>
    /// Where tests drop PDFs for eyeballing. Git-ignored — these are inspection aids, not
    /// assertions.
    /// </summary>
    public static string Artifacts
    {
        get
        {
            var path = Path.Combine(RepoRoot, "artifacts", "test-output");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    /// <summary>Writes a PDF into the artifacts directory and returns its full path.</summary>
    public static string WriteArtifact(string fileName, byte[] content)
    {
        var path = Path.Combine(Artifacts, fileName);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "n8PDF.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root (no n8PDF.sln found above the test binary).");
    }
}
