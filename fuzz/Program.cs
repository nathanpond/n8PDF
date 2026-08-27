using System.IO.Compression;
using n8PDF;
using n8PDF.Fonts;
using n8PDF.Images;
using n8PDF.Ooxml;
using n8PDF.Packaging;
using SharpFuzz;

namespace n8PDF.Fuzz;

/// <summary>
/// Coverage-guided fuzzing harnesses over the untrusted entry points (#263). Where
/// <c>FuzzTests</c> and <c>PropertyTests</c> attack from the test project with blind mutation and
/// generated inputs, these run under libFuzzer (through SharpFuzz), which instruments the library
/// and evolves inputs toward unexplored branches — the coverage a <c>.docx</c>'s CRC otherwise
/// hides from a byte-flip.
/// </summary>
/// <remarks>
/// One harness per entry point, selected by the <c>FUZZ_TARGET</c> environment variable so
/// libFuzzer's own arguments pass through untouched. Each swallows only the documented exceptions;
/// anything else — an index-out-of-range, a null reference, a raw overflow, a hang — escapes and
/// libFuzzer records it as a crash, which is the same oracle the hardening rounds established.
///
/// Coverage-guided runs need libFuzzer, which ships with clang on Linux and not on macOS, so those
/// run there (see <c>README.md</c>). Everywhere, <c>replay</c> runs the same harness over the
/// seeded corpus on a time-bounded thread — the check that no known input escapes the oracle — and
/// <c>seed</c> builds that corpus from the committed fixtures and a few crafted headers.
/// </remarks>
internal static class Program
{
    private static readonly Dictionary<string, Action<byte[]>> Harnesses = new()
    {
        ["image"] = ImageHarness,
        ["font"] = FontHarness,
        ["deobfuscate"] = DeobfuscateHarness,
        ["package"] = PackageHarness,
        ["document"] = DocumentHarness
    };

    private static int Main(string[] args)
    {
        var target = Environment.GetEnvironmentVariable("FUZZ_TARGET") ?? "document";

        if (!Harnesses.TryGetValue(target, out var harness))
        {
            Console.Error.WriteLine(
                $"unknown FUZZ_TARGET '{target}' — one of: {string.Join(", ", Harnesses.Keys)}");
            return 2;
        }

        if (args.Length > 0 && args[0] == "seed")
        {
            Seed();
            return 0;
        }

        if (args.Length > 0 && args[0] == "replay")
            return Replay(target, harness) ? 0 : 1;

        // Under libFuzzer: the library is instrumented, and this drives it. A crash or a hang is a
        // finding libFuzzer minimises and writes to disk.
        Fuzzer.LibFuzzer.Run(span => harness(span.ToArray()));
        return 0;
    }

    // ----- the harnesses: run the target, swallow only the documented exceptions -----

    // TryRead is itself the net (#48): a malformed image returns null. Anything it throws escapes.
    private static void ImageHarness(byte[] data) => ImageReader.TryRead(data);

    private static void FontHarness(byte[] data)
    {
        try { new FontLibrary { UseSystemFonts = false }.Register(data); }
        catch (FontFormatException) { }
    }

    // The obfuscated-embedded-font path: undo Word's XOR against a fixed key, then parse. The
    // bytes are the fuzz; a document carries the key in w:fontKey, which is not the attack surface.
    private static void DeobfuscateHarness(byte[] data)
    {
        var copy = (byte[])data.Clone();
        EmbeddedFonts.Deobfuscate(copy, "1a2b3c4d-5e6f-7081-9231-45a6b7c8d9e0");

        try { new FontLibrary { UseSystemFonts = false }.Register(copy); }
        catch (FontFormatException) { }
    }

    private static void PackageHarness(byte[] data)
    {
        try
        {
            using var package = OpcPackage.Open(new MemoryStream(data));
            _ = package.ReadPartAsXml(package.GetMainDocumentPartName());
        }
        catch (Exception e) when (Documented(e)) { }
    }

    private static void DocumentHarness(byte[] data)
    {
        try { Converter.Convert(data, Options.Value); }
        catch (Exception e) when (Documented(e)) { }
    }

    // No system faces: deterministic and fast, so the corpus means the same thing on every host —
    // Linux CI included, where the machine's fonts differ. One registered probe face stands in for
    // every family the document names, through the fallback list, so a valid document still
    // converts rather than failing to resolve a font it cannot find.
    private static readonly Lazy<ConversionOptions> Options = new(() =>
    {
        var fonts = new FontLibrary { UseSystemFonts = false };
        var probe = Path.Combine(FixturesRoot(), "Fonts", "n8PDFProbe.ttf");

        if (File.Exists(probe))
        {
            fonts.RegisterFile(probe);
            foreach (var family in fonts.RegisteredFamilies.ToList())
                fonts.FallbackFamilies.Insert(0, family);
        }

        return new ConversionOptions { Fonts = fonts };
    });

    // A malformed package or document legitimately fails to open or parse; those types are the
    // contract. A raw IndexOutOfRange / NullReference / OverflowException escaping is the defect.
    private static bool Documented(Exception e) => e is
        PackageTooLargeException or InvalidDataException or IOException or
        System.Xml.XmlException or FormatException or ArgumentException or
        InvalidOperationException or NotSupportedException;

    // ----- replay: run the corpus through the harness, bounded, and report escapers -----

    private static bool Replay(string target, Action<byte[]> harness)
    {
        var directory = Path.Combine(CorpusRoot(), target);

        if (!Directory.Exists(directory))
        {
            Console.Error.WriteLine($"no corpus at {directory} — run `dotnet run -- seed` first");
            return false;
        }

        var files = Directory.GetFiles(directory);
        var escapers = 0;

        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Exception? failure = null;

            var thread = new Thread(() =>
            {
                try { harness(bytes); }
                catch (Exception e) { failure = e; }
            }) { IsBackground = true };

            thread.Start();

            if (!thread.Join(TimeSpan.FromSeconds(15)))
            {
                Console.Error.WriteLine($"HANG: {target} on {Path.GetFileName(file)} ({bytes.Length} bytes)");
                escapers++;
                continue;
            }

            if (failure is not null)
            {
                Console.Error.WriteLine(
                    $"ESCAPED: {target} threw {failure.GetType().Name} on {Path.GetFileName(file)}:\n{failure}");
                escapers++;
            }
        }

        Console.WriteLine($"replay {target}: {files.Length} inputs, {escapers} escaped the oracle");
        return escapers == 0;
    }

    // ----- seed: build the corpus from the fixtures and a few crafted headers -----

    private static void Seed()
    {
        var fixtures = FixturesRoot();
        var corpus = CorpusRoot();

        var image = Ensure(corpus, "image");
        CopyIfPresent(Path.Combine(fixtures, "Images", "inks.jpg"), image, "inks.jpg");
        File.WriteAllBytes(Path.Combine(image, "minimal.png"), MinimalPng());
        File.WriteAllBytes(Path.Combine(image, "gif-header"), "GIF89a"u8.ToArray());
        File.WriteAllBytes(Path.Combine(image, "bmp-header"), "BM"u8.ToArray());
        File.WriteAllBytes(Path.Combine(image, "empty"), []);

        var font = Path.Combine(fixtures, "Fonts", "n8PDFProbe.ttf");
        foreach (var target in new[] { "font", "deobfuscate" })
        {
            var directory = Ensure(corpus, target);
            CopyIfPresent(font, directory, "probe.ttf");
            File.WriteAllBytes(Path.Combine(directory, "sfnt-header"), [0x00, 0x01, 0x00, 0x00]);
            File.WriteAllBytes(Path.Combine(directory, "empty"), []);
        }

        var docx = FirstDocx(fixtures);
        foreach (var target in new[] { "package", "document" })
        {
            var directory = Ensure(corpus, target);
            if (docx is not null) CopyIfPresent(docx, directory, "document.docx");
            File.WriteAllBytes(Path.Combine(directory, "zip-header"), [0x50, 0x4B, 0x03, 0x04]);
            File.WriteAllBytes(Path.Combine(directory, "empty"), []);
        }

        Console.WriteLine($"seeded corpus at {corpus}");
    }

    private static string Ensure(string corpus, string target)
    {
        var directory = Path.Combine(corpus, target);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void CopyIfPresent(string source, string directory, string name)
    {
        if (File.Exists(source)) File.Copy(source, Path.Combine(directory, name), overwrite: true);
    }

    private static string? FirstDocx(string fixtures)
    {
        var real = Path.Combine(fixtures, "Real");
        return Directory.Exists(real)
            ? Directory.EnumerateFiles(real, "*.docx").OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault()
            : null;
    }

    // ----- locating the repository from wherever the binary sits -----

    private static string CorpusRoot() => Path.Combine(RepositoryRoot(), "fuzz", "corpus");

    private static string FixturesRoot() =>
        Path.Combine(RepositoryRoot(), "tests", "n8PDF.Tests", "Fixtures");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "tests", "n8PDF.Tests", "Fixtures")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "could not find the repository root (a directory holding tests/n8PDF.Tests/Fixtures)");
    }

    private static byte[] MinimalPng()
    {
        var png = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        void Be32(int v) { png.Add((byte)(v >> 24)); png.Add((byte)(v >> 16)); png.Add((byte)(v >> 8)); png.Add((byte)v); }
        void Chunk(string tag, byte[] body)
        {
            Be32(body.Length);
            png.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
            png.AddRange(body);
            Be32(0);
        }

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
}
