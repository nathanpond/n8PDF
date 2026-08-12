using System.Diagnostics;

namespace n8PDF.Tests.Support;

/// <summary>Result of running <c>qpdf --check</c> over a file.</summary>
/// <param name="ExitCode">0 clean, 3 warnings only, 2 errors.</param>
/// <param name="Output">Combined stdout and stderr, which carries the diagnostics.</param>
public sealed record QpdfResult(int ExitCode, string Output)
{
    /// <summary>True when qpdf found neither errors nor warnings.</summary>
    public bool IsClean => ExitCode == 0;

    /// <summary>True when qpdf reported warnings but no hard errors.</summary>
    public bool HasWarningsOnly => ExitCode == 3;
}

/// <summary>
/// Runs qpdf as an external structural validator.
/// </summary>
/// <remarks>
/// We hand-rolled the cross-reference table, stream lengths and object graph, so an independent
/// check of exactly those things is worth more than any number of viewers rendering the page
/// correctly — a lenient renderer will happily display a file with a broken xref.
///
/// qpdf is a developer tool, not a dependency of the library or of the normal test run. When it
/// is not installed these tests report and skip, unless <c>N8PDF_REQUIRE_QPDF=1</c> is set, which
/// turns absence into a failure so CI cannot silently lose the coverage.
/// </remarks>
public static class QpdfTool
{
    private static readonly Lazy<string?> ExecutablePath = new(Locate);

    public static bool IsAvailable => ExecutablePath.Value is not null;

    public static string? Path => ExecutablePath.Value;

    /// <summary>
    /// True when the suite has been told to treat a missing qpdf as a failure rather than a skip.
    /// </summary>
    public static bool IsRequired =>
        Environment.GetEnvironmentVariable("N8PDF_REQUIRE_QPDF") is "1" or "true";

    /// <summary>Runs <c>qpdf --check</c> over the given file.</summary>
    public static QpdfResult Check(string filePath)
    {
        var executable = ExecutablePath.Value
            ?? throw new InvalidOperationException("qpdf is not installed.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--check");
        startInfo.ArgumentList.Add(filePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{executable}'.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        // A hung validator should fail the test rather than hang the suite.
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"qpdf did not finish within 60s for '{filePath}'.");
        }

        return new QpdfResult(process.ExitCode, (stdout + stderr).Trim());
    }

    /// <summary>Writes bytes to a temp file and checks them, cleaning up afterwards.</summary>
    public static QpdfResult CheckBytes(byte[] pdf, string name)
    {
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "n8pdf-qpdf");
        Directory.CreateDirectory(directory);

        var path = System.IO.Path.Combine(directory, name + ".pdf");
        File.WriteAllBytes(path, pdf);

        try
        {
            return Check(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Finds qpdf on PATH, then in the usual Homebrew locations. Homebrew on Apple Silicon
    /// installs to /opt/homebrew, which is not always on PATH for a process launched by an IDE.
    /// </summary>
    private static string? Locate()
    {
        string[] candidates =
        [
            "/opt/homebrew/bin/qpdf",
            "/usr/local/bin/qpdf",
            "/usr/bin/qpdf"
        ];

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate)) return candidate;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(System.IO.Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;

            var candidate = System.IO.Path.Combine(directory, "qpdf");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
