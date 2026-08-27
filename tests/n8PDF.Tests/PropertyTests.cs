using System.IO.Compression;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using n8PDF;
using n8PDF.Images;
using n8PDF.Packaging;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// The fuzz oracle stated as a property rather than enforced by a fixed corpus (#272). Where
/// <see cref="FuzzTests"/> mutates known seeds a fixed number of times, this hands a generator the
/// same invariant and lets it attack across a far wider input space — arbitrary bytes and
/// structure-aware inputs alike — deterministically, inside the normal suite.
/// </summary>
/// <remarks>
/// The invariant, unchanged from the hardening rounds: the two untrusted entry points yield bounded
/// output or a documented package/format exception, and never a hang, an out-of-memory, a
/// wrong-sized allocation, or a raw runtime crash. Every input runs on a time-bounded thread, so a
/// hang shows as a failed join rather than a stuck suite. FsCheck is a dev/test dependency confined
/// to this project; <c>src/n8PDF</c> keeps its zero-<c>PackageReference</c> invariant. Each property
/// is pinned with a fixed <c>Replay</c> seed and a fixed <c>MaxTest</c> budget, so it is
/// reproducible and needs none of Word's faces — the hosted runner is fine. Should the generator
/// ever find an input that escapes the oracle, that input is the finding: commit it as a seed in
/// <see cref="FuzzTests"/> and file the defect.
/// </remarks>
public class PropertyTests
{
    // ----- the oracle -----

    /// <summary>Runs the action on a time-bounded thread; a hang shows as an unfinished join.</summary>
    private static (bool Finished, Exception? Failure) RunBounded(Action act)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { act(); }
            catch (Exception e) { failure = e; }
        }) { IsBackground = true };

        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(10));

        return (finished, failure);
    }

    /// <summary>The image contract: a malformed image costs its placement, so TryRead never throws or hangs.</summary>
    private static void ImageOracle(byte[] data)
    {
        var (finished, failure) = RunBounded(() => ImageReader.TryRead(data));

        if (!finished)
            throw new Xunit.Sdk.XunitException($"TryRead hung on {data.Length} bytes");

        if (failure is not null)
            throw new Xunit.Sdk.XunitException(
                $"TryRead threw {failure.GetType().Name} on {data.Length} bytes: {failure.Message}");
    }

    /// <summary>The document contract: bounded output or a documented type, never a hang or raw crash.</summary>
    private static void DocumentOracle(byte[] docx, ConversionOptions options)
    {
        var (finished, failure) = RunBounded(() => Converter.Convert(docx, options));

        if (!finished)
            throw new Xunit.Sdk.XunitException($"Convert hung on {docx.Length} bytes");

        if (failure is not null && !Documented(failure))
            throw new Xunit.Sdk.XunitException(
                $"Convert threw undocumented {failure.GetType().Name} on {docx.Length} bytes: {failure.Message}");
    }

    // A mutated zip legitimately fails to open or parse; those are documented. A raw
    // IndexOutOfRange/NullReference/etc. escaping would be the defect to file.
    private static bool Documented(Exception e) => e is
        PackageTooLargeException or InvalidDataException or IOException or
        System.Xml.XmlException or FormatException or ArgumentException or
        InvalidOperationException or NotSupportedException;

    // ----- seeds and mutation -----

    private static readonly byte[][] Images = BuildImageSeeds();

    private static readonly byte[] Document =
        new DocxBuilder().AddParagraph("A little document to mutate, field by field.").Build();

    // One pinned library for the whole property, not one per generated input.
    private static readonly ConversionOptions Options = new() { Fonts = TestFonts.CreatePinnedLibrary() };

    private static byte[][] BuildImageSeeds()
    {
        var pixels = ImageWriter.Sample(16, 16);

        return
        [
            ImageWriter.Bmp(16, 16, pixels),
            ImageWriter.Bmp(16, 16, pixels, bits: 8),
            ImageWriter.Gif(16, 16, pixels),
            ImageWriter.Tiff(16, 16, pixels),
            MinimalPng(),
            new EmfWriter(40, 30).Select(1).Rectangle(2, 2, 30, 20).Build()
        ];
    }

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

    /// <summary>One byte-level edit, its operation and target generated by FsCheck.</summary>
    private readonly record struct Edit(int Op, int Position, int Value);

    private static Gen<Edit> EditGen =>
        from op in Gen.Choose(0, 3)
        from position in Gen.Choose(0, int.MaxValue)
        from value in Gen.Choose(0, 255)
        select new Edit(op, position, value);

    private static int Mod(int value, int modulus) => modulus <= 0 ? 0 : Math.Abs(value % modulus);

    /// <summary>Applies the edits to a copy of the seed — the same repertoire the mutation fuzzer uses.</summary>
    private static byte[] Apply(byte[] seed, Edit[] edits)
    {
        var data = (byte[])seed.Clone();

        foreach (var edit in edits)
        {
            if (data.Length == 0) break;

            switch (edit.Op)
            {
                case 0:   // set a byte
                    data[Mod(edit.Position, data.Length)] = (byte)edit.Value;
                    break;

                case 1:   // flip a bit
                    data[Mod(edit.Position, data.Length)] ^= (byte)(1 << (edit.Value & 7));
                    break;

                case 2:   // corrupt a four-byte field to a large value
                {
                    if (data.Length < 4) goto case 0;   // too short for a field — set a byte instead

                    var at = Mod(edit.Position, data.Length - 3);   // at + 3 stays in bounds
                    data[at] = 0x7F; data[at + 1] = 0xFF; data[at + 2] = 0xFF; data[at + 3] = 0xFF;
                    break;
                }

                default:   // truncate
                    data = data[..Mod(edit.Position, data.Length)];
                    break;
            }
        }

        return data;
    }

    /// <summary>
    /// Rebuilds the document as a valid zip with its main part's bytes mutated — a valid container
    /// (correct CRCs, so it opens) carrying a mutated part, which is what reaches past the zip and
    /// into the OPC reader and the WordprocessingML parsers.
    /// </summary>
    private static byte[] MutatedDocument(Edit[] edits)
    {
        using var input = new MemoryStream(Document);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();

        using (var rebuilt = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                entryStream.CopyTo(buffer);

                var bytes = entry.FullName == "word/document.xml"
                    ? Apply(buffer.ToArray(), edits)
                    : buffer.ToArray();

                var target = rebuilt.CreateEntry(entry.FullName);
                using var targetStream = target.Open();
                targetStream.Write(bytes, 0, bytes.Length);
            }
        }

        return output.ToArray();
    }

    // ----- generators -----

    private static Gen<byte[]> RawBytes =>
        Gen.ArrayOf(Gen.Choose(0, 255).Select(i => (byte)i));

    private static Gen<byte[]> MutatedImage =>
        from index in Gen.Choose(0, Images.Length - 1)
        from edits in Gen.ArrayOf(EditGen)
        select Apply(Images[index], edits);

    private static Gen<byte[]> MutatedDocx =>
        from edits in Gen.ArrayOf(EditGen)
        select MutatedDocument(edits);

    // ----- the properties -----

    [Property(MaxTest = 3000, Replay = "(2720001,101)", QuietOnSuccess = true)]
    public Property TryRead_yields_an_image_or_null_and_never_throws_or_hangs() =>
        Prop.ForAll(
            Arb.From(Gen.OneOf(RawBytes, MutatedImage)),
            data =>
            {
                ImageOracle(data);
                return true;
            });

    [Property(MaxTest = 1200, Replay = "(2720002,103)", QuietOnSuccess = true)]
    public Property Convert_throws_only_documented_types_and_never_hangs() =>
        Prop.ForAll(
            Arb.From(Gen.OneOf(RawBytes, MutatedDocx)),
            docx =>
            {
                DocumentOracle(docx, Options);
                return true;
            });
}
