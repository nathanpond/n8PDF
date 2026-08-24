using System.Diagnostics;

namespace n8PDF.Tests.Support;

/// <summary>Result of validating a file against the PDF/A-2b profile.</summary>
public sealed record VeraPdfResult(bool Compliant, string Output);

/// <summary>
/// Runs veraPDF as an external PDF/A validator — the fourth second opinion (#68).
/// </summary>
/// <remarks>
/// The conformance claim is exactly the kind of thing that cannot be tested by agreeing with
/// ourselves: an archive will run a validator, so the suite runs the validator the archives run.
/// Like the other three checkers it is optional locally and required in CI —
/// <c>N8PDF_REQUIRE_VERAPDF=1</c> turns absence into a failure.
/// </remarks>
public static class VeraPdfTool
{
    private static readonly Lazy<string?> ExecutablePath = new(Locate);

    public static bool IsAvailable => ExecutablePath.Value is not null;

    public static bool IsRequired =>
        Environment.GetEnvironmentVariable("N8PDF_REQUIRE_VERAPDF") is "1" or "true";

    public static string UnavailableMessage =>
        "veraPDF is not installed; PDF/A validation skipped. brew install verapdf";

    /// <summary>Validates a file against the PDF/A-2b profile.</summary>
    public static VeraPdfResult Validate(string filePath)
    {
        var executable = ExecutablePath.Value
            ?? throw new InvalidOperationException("veraPDF is not installed.");

        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--flavour");
        startInfo.ArgumentList.Add("2b");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("text");
        startInfo.ArgumentList.Add(filePath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("veraPDF failed to start.");

        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        // Text format opens its verdict with PASS or FAIL.
        return new VeraPdfResult(output.TrimStart().StartsWith("PASS", StringComparison.Ordinal), output);
    }

    private static string? Locate()
    {
        string[] candidates =
        [
            "/opt/homebrew/bin/verapdf", "/usr/local/bin/verapdf", "/usr/bin/verapdf"
        ];

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        foreach (var root in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(root, "verapdf");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }
}
