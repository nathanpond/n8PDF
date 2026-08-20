using System.Xml.Linq;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Guards the constraint the whole project is built around: n8PDF converts DOCX to PDF from
/// scratch, in one assembly, with nothing else required at run time.
/// </summary>
public class LibraryInvariantTests
{
    [Fact]
    public void The_library_has_no_package_references()
    {
        var csproj = XDocument.Load(Path.Combine(TestPaths.RepoRoot, "src", "n8PDF", "n8PDF.csproj"));

        var packages = csproj.Descendants("PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? "(unnamed)")
            .ToList();

        Assert.True(packages.Count == 0,
            "src/n8PDF must depend on nothing but the base class library, but it references: "
            + string.Join(", ", packages));
    }

    [Fact]
    public void The_library_references_no_other_projects()
    {
        var csproj = XDocument.Load(Path.Combine(TestPaths.RepoRoot, "src", "n8PDF", "n8PDF.csproj"));

        // A single shippable assembly: a consumer adds one reference and calls one method.
        Assert.Empty(csproj.Descendants("ProjectReference"));
    }

    /// <summary>
    /// The package says what it is, who wrote it, and on what terms.
    /// </summary>
    /// <remarks>
    /// A package published without these is published without a licence anyone can rely on, and
    /// without anything on its page to say what it does. They are cheap to state and easy to lose
    /// in a merge, so they are asserted rather than remembered.
    /// </remarks>
    [Fact]
    public void The_library_carries_the_metadata_a_package_needs()
    {
        var csproj = XDocument.Load(Path.Combine(TestPaths.RepoRoot, "src", "n8PDF", "n8PDF.csproj"));

        foreach (var property in new[]
                 {
                     "PackageId", "Version", "Authors", "Description", "Copyright",
                     "PackageLicenseExpression", "PackageReadmeFile", "PackageTags"
                 })
        {
            var value = csproj.Descendants(property).FirstOrDefault()?.Value;

            Assert.False(string.IsNullOrWhiteSpace(value),
                $"src/n8PDF/n8PDF.csproj states no {property}, which a published package needs.");
        }

        // The readme the package points at has to be there, and packed with it.
        var readme = csproj.Descendants("PackageReadmeFile").First().Value;

        Assert.True(File.Exists(Path.Combine(TestPaths.RepoRoot, "src", "n8PDF", readme)),
            $"the package names {readme} as its readme, and there is no such file.");

        Assert.Contains(csproj.Descendants("None"),
            none => none.Attribute("Include")?.Value == readme &&
                    none.Attribute("Pack")?.Value == "true");
    }

    /// <summary>
    /// The licence the package claims is the licence the repository carries.
    /// </summary>
    [Fact]
    public void The_licence_is_the_one_the_package_declares()
    {
        var csproj = XDocument.Load(Path.Combine(TestPaths.RepoRoot, "src", "n8PDF", "n8PDF.csproj"));
        var expression = csproj.Descendants("PackageLicenseExpression").First().Value;

        Assert.Equal("MIT", expression);

        var path = Path.Combine(TestPaths.RepoRoot, "LICENSE");
        Assert.True(File.Exists(path), "the repository has no LICENSE file.");

        var licence = File.ReadAllText(path);

        Assert.StartsWith("MIT License", licence);
        Assert.Contains("Copyright (c)", licence);
        Assert.Contains("WITHOUT WARRANTY OF ANY KIND", licence);

        // And it is packed, so that what is installed carries its own terms.
        Assert.Contains(csproj.Descendants("None"),
            none => (none.Attribute("Include")?.Value ?? string.Empty).EndsWith("LICENSE") &&
                    none.Attribute("Pack")?.Value == "true");
    }

    [Fact]
    public void The_library_loads_no_assemblies_outside_the_framework()
    {
        var assembly = typeof(Converter).Assembly;

        var referenced = assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal) &&
                           name != "netstandard" &&
                           name != "mscorlib")
            .ToList();

        Assert.True(referenced.Count == 0,
            "the compiled library pulled in non-framework assemblies: " + string.Join(", ", referenced));
    }
}
