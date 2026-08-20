using System.Diagnostics;
using n8PDF.Images;

namespace n8PDF.Tests.Support;

/// <summary>A rendered page: its pixels, and the text the reader found on it.</summary>
internal sealed record RenderedPage(ImageData Pixels, string Text)
{
    /// <summary>The colour at a point of the page, in points from its top-left corner.</summary>
    public (byte R, byte G, byte B) At(double x, double y, double scale)
    {
        var px = Math.Clamp((int)(x * scale), 0, Pixels.Width - 1);
        var py = Math.Clamp((int)(y * scale), 0, Pixels.Height - 1);
        var at = (py * Pixels.Width + px) * 3;

        return (Pixels.Data[at], Pixels.Data[at + 1], Pixels.Data[at + 2]);
    }
}

/// <summary>
/// Draws a page of a PDF with a reader that shares nothing with this one, so that what a page
/// looks like can be asked about rather than only what it says.
/// </summary>
/// <remarks>
/// Everything else here is measured by reading text positions out of a PDF, which says nothing at
/// all about a drawing: a chart could be drawn upside down and the comparison would not notice.
/// So for those, macOS's own PDF reader draws the page and the pixels are looked at.
///
/// It is a developer tool, like the font reader: where swiftc is missing the tests that use it
/// report and skip, unless <c>N8PDF_REQUIRE_RASTERIZER=1</c> makes its absence a failure.
/// </remarks>
public static class PdfRasterizer
{
    private static readonly Lazy<string?> Tool = new(Build);

    public static bool IsAvailable => Tool.Value is not null;

    public static bool IsRequired =>
        Environment.GetEnvironmentVariable("N8PDF_REQUIRE_RASTERIZER") == "1";

    public static string UnavailableMessage =>
        "The page rasterizer was not built, so no page was drawn and looked at.\n" +
        "It needs swiftc, which comes with the Xcode command line tools:\n" +
        "  xcode-select --install\n" +
        "Set N8PDF_REQUIRE_RASTERIZER=1 to make its absence a failure rather than a skip.";

    /// <summary>Draws one page, at the given points-to-pixels scale.</summary>
    internal static RenderedPage? Render(byte[] pdf, int page = 0, double scale = 2)
    {
        if (Tool.Value is not { } tool) return null;

        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-rasterizer");
        Directory.CreateDirectory(directory);

        var source = Path.Combine(directory, $"page-{Guid.NewGuid():N}.pdf");
        var target = Path.ChangeExtension(source, ".png");

        File.WriteAllBytes(source, pdf);

        try
        {
            var text = Run(tool, [source, page.ToString(), scale.ToString("0.###"), target]);
            if (text is null || !File.Exists(target)) return null;

            return new RenderedPage(PngDecoder.Decode(File.ReadAllBytes(target)), text);
        }
        finally
        {
            Delete(source);
            Delete(target);
        }
    }

    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// Compiles the tool on first use and keeps it, since compiling it takes longer than every
    /// test that uses it put together.
    /// </summary>
    private static string? Build()
    {
        var source = Path.Combine(TestPaths.RepoRoot, "tools", "rasterize.swift");
        if (!File.Exists(source)) return null;

        var directory = Path.Combine(Path.GetTempPath(), "n8pdf-rasterizer");
        Directory.CreateDirectory(directory);

        var tool = Path.Combine(directory, "rasterize");

        if (File.Exists(tool) && File.GetLastWriteTimeUtc(tool) > File.GetLastWriteTimeUtc(source))
            return tool;

        return Run("swiftc", ["-O", source, "-o", tool]) is not null && File.Exists(tool) ? tool : null;
    }

    private static string? Run(string executable, string[] arguments)
    {
        try
        {
            var start = new ProcessStartInfo(executable)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();

            if (!process.WaitForExit(120_000)) return null;

            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException
                                     or IOException or PlatformNotSupportedException)
        {
            return null;
        }
    }
}
