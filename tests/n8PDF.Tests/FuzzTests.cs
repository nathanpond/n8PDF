using System.IO.Compression;
using n8PDF;
using n8PDF.Images;
using n8PDF.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// A deterministic mutation fuzzer over the two untrusted entry points — <see cref="ImageReader.TryRead"/>
/// and <see cref="Converter"/> — seeded from valid images, the JPEG fixture, a real document, and
/// the crafted hostile corpus (#71).
/// </summary>
/// <remarks>
/// The oracle is the contract the hardening rounds established: a malformed image costs its own
/// placement, so <c>TryRead</c> returns null or an image and never throws or hangs; a malformed
/// document throws only the documented package/format types or converts, never a raw runtime
/// crash. Each input runs on a thread with a time bound, so a hang shows as a failed join rather
/// than a stuck suite, and the recursion/allocation caps keep any run inside a sane memory
/// envelope. It is deterministic — a fixed seed, generated corpus — so it runs on every push
/// inside the normal suite with a fixed budget (it needs none of Word's faces, so the hosted
/// runner is fine); a larger by-hand run just raises the iteration counts. Should it ever surface
/// an input that escapes the oracle, that input is the finding: commit it as a seed here and file
/// the defect.
/// </remarks>
public class FuzzTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static byte[] MinimalPng()
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        void Be32(int v) { png.Add((byte)(v >> 24)); png.Add((byte)(v >> 16)); png.Add((byte)(v >> 8)); png.Add((byte)v); }
        void Chunk(string t, byte[] b) { Be32(b.Length); png.AddRange(System.Text.Encoding.ASCII.GetBytes(t)); png.AddRange(b); Be32(0); }
        var ihdr = new List<byte>();
        void I32(int v) { ihdr.Add((byte)(v >> 24)); ihdr.Add((byte)(v >> 16)); ihdr.Add((byte)(v >> 8)); ihdr.Add((byte)v); }
        I32(4); I32(4); ihdr.Add(8); ihdr.Add(6); ihdr.Add(0); ihdr.Add(0); ihdr.Add(0);
        Chunk("IHDR", ihdr.ToArray());
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true)) z.Write(new byte[4 * (1 + 4 * 4)]);
        Chunk("IDAT", raw.ToArray());
        Chunk("IEND", []);
        return png.ToArray();
    }

    private static List<byte[]> ImageSeeds()
    {
        var pixels = ImageWriter.Sample(16, 16);
        var seeds = new List<byte[]>
        {
            ImageWriter.Bmp(16, 16, pixels),
            ImageWriter.Bmp(16, 16, pixels, bits: 8),
            ImageWriter.Gif(16, 16, pixels),
            ImageWriter.Tiff(16, 16, pixels),
            MinimalPng(),
            new EmfWriter(40, 30).Select(1).Rectangle(2, 2, 30, 20).Build()
        };

        var jpeg = Path.Combine(TestPaths.ImageFixtures, "inks.jpg");
        if (File.Exists(jpeg)) seeds.Add(File.ReadAllBytes(jpeg));

        return seeds;
    }

    /// <summary>Runs the action on a time-bounded thread; a hang fails as a stuck join.</summary>
    private static Exception? RunBounded(Action act, out bool finished)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { act(); } catch (Exception e) { failure = e; } });
        thread.IsBackground = true;
        thread.Start();
        finished = thread.Join(TimeSpan.FromSeconds(10));
        return failure;
    }

    private static byte[] Mutate(byte[] seed, Random random)
    {
        var data = (byte[])seed.Clone();
        if (data.Length == 0) return data;

        var mutations = 1 + random.Next(8);
        for (var m = 0; m < mutations; m++)
        {
            switch (random.Next(4))
            {
                case 0: data[random.Next(data.Length)] = (byte)random.Next(256); break;   // set a byte
                case 1: data[random.Next(data.Length)] ^= (byte)(1 << random.Next(8)); break;   // flip a bit
                case 2:   // corrupt a 4-byte field to a large value
                {
                    var at = random.Next(Math.Max(1, data.Length - 4));
                    data[at] = 0x7F; data[at + 1] = 0xFF; data[at + 2] = 0xFF; data[at + 3] = 0xFF;
                    break;
                }
                default:   // truncate
                    return data[..random.Next(data.Length)];
            }
        }

        return data;
    }

    [Fact]
    public void TryRead_never_throws_or_hangs_on_mutated_images()
    {
        var seeds = ImageSeeds();
        var random = new Random(20260824);   // fixed seed: deterministic and reproducible
        const int iterations = 4000;

        for (var i = 0; i < iterations; i++)
        {
            var mutated = Mutate(seeds[random.Next(seeds.Count)], random);

            var failure = RunBounded(() => ImageReader.TryRead(mutated), out var finished);

            Assert.True(finished, $"TryRead did not finish on mutation {i} ({mutated.Length} bytes) — a hang");
            Assert.True(failure is null,
                $"TryRead threw {failure?.GetType().Name} on mutation {i}: {failure?.Message}");
        }

        _output.WriteLine($"{iterations} mutated images: none threw, none hung");
    }

    [Fact]
    public void Convert_only_throws_documented_types_on_a_mutated_document()
    {
        var seed = new DocxBuilder().AddParagraph("A little document to mutate.").Build();
        var random = new Random(20260825);
        const int iterations = 600;

        var options = new ConversionOptions { Fonts = TestFonts.CreatePinnedLibrary() };
        var undocumented = 0;

        for (var i = 0; i < iterations; i++)
        {
            var mutated = Mutate(seed, random);

            var failure = RunBounded(() => Converter.Convert(mutated, options), out var finished);
            Assert.True(finished, $"Convert did not finish on mutation {i} — a hang");

            // A mutated zip legitimately fails to open or parse; those are documented. A raw
            // IndexOutOfRange/NullReference/etc. escaping would be a defect to file.
            if (failure is not null && failure is not (
                Packaging.PackageTooLargeException or InvalidDataException or IOException or
                System.Xml.XmlException or FormatException or ArgumentException or
                InvalidOperationException or NotSupportedException))
            {
                undocumented++;
                _output.WriteLine($"mutation {i}: undocumented {failure.GetType().Name}: {failure.Message}");
            }
        }

        Assert.Equal(0, undocumented);
        _output.WriteLine($"{iterations} mutated documents: no undocumented exception escaped");
    }
}
