using n8PDF.Images;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A differential fuzz oracle for the image decoders (#265). The crash oracle in
/// <see cref="FuzzTests"/> asks only "did it throw, hang, or exhaust memory?" — a decoder that
/// reads a malformed image and returns the <em>wrong pixels</em> passes it silently, though a
/// wrong decode is both a correctness and a security problem. This compares each decode against an
/// independent reference decoder on the same bytes and flags divergence past a tolerance, turning
/// "it didn't crash" into "it decoded what a hardened decoder does".
/// </summary>
/// <remarks>
/// The reference is <c>sips</c> (see <see cref="ReferenceImage"/>) — a developer tool, out of
/// process, never a dependency of the library — so where it is absent these report and skip. The
/// tolerances are measured, not guessed: a lossless format read two ways matches to a hair, and the
/// fuzz pass allows only for the small, legitimate ways two decoders read the same malformed bytes
/// while still catching a gross mis-decode.
/// </remarks>
public class DifferentialImageTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] SamplePixels(int width, int height) => ImageWriter.Sample(width, height);

    /// <summary>A format, a valid image of it, and the decoder under test.</summary>
    private sealed record Format(string Name, string Extension, byte[] Valid, Func<byte[], ImageData> Decode, double ValidTolerance);

    private static IEnumerable<Format> Formats()
    {
        var pixels = SamplePixels(24, 24);

        yield return new Format("bmp", "bmp", ImageWriter.Bmp(24, 24, pixels), b => BmpDecoder.Decode(b), 1);
        yield return new Format("gif", "gif", ImageWriter.Gif(24, 24, pixels), b => GifDecoder.Decode(b), 1);
        yield return new Format("tiff", "tiff", ImageWriter.Tiff(24, 24, pixels), b => TiffDecoder.Decode(b), 1);
        yield return new Format("png", "png", PngWriter.Write(24, 24, pixels, hasAlpha: false), b => PngDecoder.Decode(b), 1);

        var jpeg = Path.Combine(TestPaths.ImageFixtures, "inks.jpg");
        if (File.Exists(jpeg))
            yield return new Format("jpeg", "jpg", File.ReadAllBytes(jpeg), b => JpegDecoder.Decode(b), 40);
    }

    private static ImageData? TryDecode(Func<byte[], ImageData> decode, byte[] bytes)
    {
        try { return decode(bytes); }
        catch (Exception e) when (e is ImageFormatException or IndexOutOfRangeException
            or ArgumentException or OverflowException or DivideByZeroException or InvalidDataException)
        {
            return null;
        }
    }

    [Fact]
    public void Valid_images_decode_like_the_reference()
    {
        if (!ReferenceImage.IsAvailable && !ReferenceImage.IsRequired)
        {
            _output.WriteLine(ReferenceImage.UnavailableMessage);
            return;
        }

        foreach (var format in Formats())
        {
            var ours = TryDecode(format.Decode, format.Valid);
            var theirs = ReferenceImage.Decode(format.Valid, format.Extension);

            Assert.NotNull(ours);
            Assert.NotNull(theirs);
            Assert.Equal(theirs.Value.Width, ours.Width);
            Assert.Equal(theirs.Value.Height, ours.Height);

            var difference = ReferenceImage.MeanDifference(ReferenceImage.ToRgb(ours), theirs.Value.Rgb);
            _output.WriteLine($"{format.Name}: valid image differs from the reference by {difference:F2}");

            Assert.True(difference <= format.ValidTolerance,
                $"{format.Name}: a valid image decoded {difference:F2} away from the reference (> {format.ValidTolerance})");
        }
    }

    [Fact]
    public void Mutated_images_are_compared_against_the_reference_and_divergence_reported()
    {
        if (!ReferenceImage.IsAvailable && !ReferenceImage.IsRequired)
        {
            _output.WriteLine(ReferenceImage.UnavailableMessage);
            return;
        }

        // The harness reports rather than asserts, because two hardened decoders legitimately
        // diverge on the same malformed bytes: a flipped byte in an LZW or Deflate stream cascades,
        // and both readers recover from it their own way. A divergence past this tolerance on an
        // input both decoders accept is a candidate mis-decode to look at by hand, and — per the
        // backlog rules — to reduce, seed into FuzzTests, and file if it proves real. What would be
        // a defect is the harness never finding a comparison to make; that it does is asserted.
        const double reportThreshold = 48;

        var random = new Random(20260827);
        var compared = 0;
        var flagged = 0;
        var worst = 0.0;

        foreach (var format in Formats())
        {
            for (var i = 0; i < 200; i++)
            {
                var mutated = Mutate(format.Valid, random);

                var ours = TryDecode(format.Decode, mutated);
                if (ours is null) continue;

                var theirs = ReferenceImage.Decode(mutated, format.Extension);
                if (theirs is null || theirs.Value.Width != ours.Width || theirs.Value.Height != ours.Height) continue;

                var difference = ReferenceImage.MeanDifference(ReferenceImage.ToRgb(ours), theirs.Value.Rgb);
                compared++;
                worst = Math.Max(worst, difference);

                if (difference > reportThreshold)
                {
                    flagged++;
                    _output.WriteLine(
                        $"candidate: {format.Name} mutation {i} decoded {difference:F1} from the reference (> {reportThreshold})");
                }
            }
        }

        _output.WriteLine(
            $"{compared} mutated images decoded by both readers; {flagged} past {reportThreshold}, worst {worst:F1}");

        Assert.True(compared > 0, "the differential harness found no input both decoders read — it is not comparing anything");
    }

    [Fact]
    public void The_oracle_catches_a_wrong_decode_the_crash_oracle_passes()
    {
        // A valid image, its known source pixels the ground truth. This runs everywhere: it is the
        // differential mechanism itself under test, not the reference tool.
        var pixels = SamplePixels(16, 16);
        var correct = ReferenceImage.ToRgb(BmpDecoder.Decode(ImageWriter.Bmp(16, 16, pixels)));

        // A decoder that silently returns wrong pixels — the red channel inverted, say. It throws
        // nothing, so the crash oracle sees a clean run and passes it.
        var wrong = (byte[])correct.Clone();
        for (var i = 0; i < wrong.Length; i += 3) wrong[i] = (byte)(255 - wrong[i]);

        const double tolerance = 2;
        var right = ReferenceImage.MeanDifference(correct, pixels);
        var off = ReferenceImage.MeanDifference(wrong, pixels);

        _output.WriteLine($"real decode differs by {right:F2}; the wrong decode by {off:F2} (tolerance {tolerance})");

        Assert.True(right <= tolerance, "the real decode should match its source within tolerance");
        Assert.True(off > tolerance, "the differential oracle must flag the wrong decode the crash oracle passed");
    }

    private static byte[] Mutate(byte[] seed, Random random)
    {
        var data = (byte[])seed.Clone();
        if (data.Length == 0) return data;

        var edits = 1 + random.Next(6);
        for (var e = 0; e < edits; e++)
        {
            switch (random.Next(3))
            {
                case 0: data[random.Next(data.Length)] = (byte)random.Next(256); break;
                case 1: data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8)); break;
                default:
                    var at = random.Next(data.Length);
                    var run = Math.Min(data.Length - at, 1 + random.Next(8));
                    for (var k = 0; k < run; k++) data[at + k] = (byte)random.Next(256);
                    break;
            }
        }

        return data;
    }
}
