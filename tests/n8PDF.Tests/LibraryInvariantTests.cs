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
