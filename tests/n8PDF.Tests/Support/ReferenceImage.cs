using System.Diagnostics;
using n8PDF.Images;

namespace n8PDF.Tests.Support;

/// <summary>
/// What an independent decoder made of the same image bytes, as RGB pixels — the second opinion a
/// differential fuzz oracle compares against (#265).
/// </summary>
/// <remarks>
/// The reference is <c>sips</c>, the image converter macOS ships, which shares nothing with this
/// project's decoders — the same tool the format round-trip tests already lean on. The input is
/// handed to it as it stands and asked for as a PNG, which is lossless, so what comes back is
/// sips's interpretation of the bytes with no re-encoding loss; the PNG is then read by this
/// project's own (separately tested) PNG decoder into RGB. It is a developer tool, not a
/// dependency: where it is absent the differential tests report and skip, unless
/// <c>N8PDF_REQUIRE_SIPS=1</c> makes its absence a failure.
/// </remarks>
public static class ReferenceImage
{
    public static bool IsAvailable => File.Exists("/usr/bin/sips");

    public static bool IsRequired => Environment.GetEnvironmentVariable("N8PDF_REQUIRE_SIPS") == "1";

    public static string UnavailableMessage =>
        "sips was not found, so no independent decoder read the images back for comparison.\n" +
        "It ships with macOS; this differential oracle is one of the checks that runs there.\n" +
        "Set N8PDF_REQUIRE_SIPS=1 to make its absence a failure rather than a skip.";

    /// <summary>
    /// Decodes the bytes with the reference tool and returns RGB pixels, or null where the tool
    /// rejects the input (or is not installed) — the signal that the reference does not read it
    /// either, so there is nothing to compare.
    /// </summary>
    public static (int Width, int Height, byte[] Rgb)? Decode(byte[] bytes, string extension)
    {
        if (!IsAvailable) return null;

        var directory = Path.Combine(Path.GetTempPath(), $"n8pdf-diff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var input = Path.Combine(directory, $"input.{extension}");
            var output = Path.Combine(directory, "reference.png");
            File.WriteAllBytes(input, bytes);

            try
            {
                using var process = Process.Start(new ProcessStartInfo("/usr/bin/sips",
                    ["-s", "format", "png", input, "--out", output])
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                });

                if (process is null || !process.WaitForExit(30_000) || process.ExitCode != 0)
                    return null;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
            {
                return null;
            }

            if (!File.Exists(output)) return null;

            ImageData png;
            try { png = PngDecoder.Decode(File.ReadAllBytes(output)); }
            catch (Exception e) when (e is ImageFormatException or IndexOutOfRangeException) { return null; }

            return (png.Width, png.Height, ToRgb(png));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    /// <summary>Flattens an image to three bytes a pixel, so two decoders' output can be compared.</summary>
    internal static byte[] ToRgb(ImageData image)
    {
        var pixels = image.Width * image.Height;
        var rgb = new byte[pixels * 3];
        var channels = image.ComponentCount;

        for (var i = 0; i < pixels; i++)
        {
            var source = i * channels;

            switch (image.ColorSpace)
            {
                case ImageColorSpace.Gray:
                    rgb[i * 3] = rgb[i * 3 + 1] = rgb[i * 3 + 2] = image.Data[source];
                    break;

                case ImageColorSpace.Cmyk:
                    // Naive CMYK→RGB, enough to compare against a reference that did the same.
                    var k = image.Data[source + 3];
                    rgb[i * 3] = (byte)((255 - image.Data[source]) * (255 - k) / 255);
                    rgb[i * 3 + 1] = (byte)((255 - image.Data[source + 1]) * (255 - k) / 255);
                    rgb[i * 3 + 2] = (byte)((255 - image.Data[source + 2]) * (255 - k) / 255);
                    break;

                default:
                    rgb[i * 3] = image.Data[source];
                    rgb[i * 3 + 1] = image.Data[source + 1];
                    rgb[i * 3 + 2] = image.Data[source + 2];
                    break;
            }
        }

        return rgb;
    }

    /// <summary>The mean absolute per-channel difference between two equal-length RGB buffers.</summary>
    public static double MeanDifference(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return double.MaxValue;

        long total = 0;
        for (var i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]);

        return (double)total / a.Length;
    }
}
