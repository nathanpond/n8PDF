using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using n8PDF.Packaging;
using n8PDF.Tests.Support;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// What a hostile document is allowed to cost.
/// </summary>
/// <remarks>
/// This library's whole purpose is reading files it did not write, and a <c>.docx</c> is a ZIP: it
/// describes its own size, and a hostile one describes it wrongly. The tests here build the attack
/// rather than describe it — a real bomb, a real lying header — because a limit nobody has fired a
/// shot at is a comment rather than a defence.
/// </remarks>
public class PackageLimitTests(ITestOutputHelper output)
{
    /// <summary>
    /// A part of a hundred megabytes of zeros, which compresses to about a hundred kilobytes. Not
    /// the largest a ZIP can express by any means — this is a thousand to one, and the format
    /// permits far worse — but enough that reading it to the end is felt, and small enough that
    /// building it in a test is not.
    /// </summary>
    private const long BombBytes = 100L * 1024 * 1024;

    private static byte[] Bomb(string partName = "word/document.xml", long bytes = BombBytes)
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry(partName, CompressionLevel.Optimal).Open();

            var zeros = new byte[64 * 1024];
            for (long written = 0; written < bytes; written += zeros.Length)
                stream.Write(zeros, 0, (int)Math.Min(zeros.Length, bytes - written));
        }

        return buffer.ToArray();
    }

    [Fact]
    public void A_part_that_decompresses_past_the_limit_is_refused()
    {
        var bomb = Bomb();
        output.WriteLine($"{BombBytes:N0} bytes of zeros compressed to {bomb.Length:N0} " +
                         $"— {(double)BombBytes / bomb.Length:N0} to one");

        using var package = OpcPackage.Open(
            new MemoryStream(bomb), limits: new PackageLimits { MaximumPartBytes = 1024 * 1024 });

        var thrown = Assert.Throws<PackageTooLargeException>(() => package.ReadPart("word/document.xml"));
        output.WriteLine(thrown.Message);
    }

    /// <summary>
    /// A header that understates cannot smuggle a bomb past the check, because the framework
    /// itself stops the decompressor at the size the header declares: patch a hundred megabyte
    /// part to say four kilobytes and four kilobytes is what comes out. So the declared size is
    /// what refuses an honest bomb, and a dishonest one truncates itself.
    /// </summary>
    /// <remarks>
    /// Which leaves the counting on the stream doing two things the declared size cannot: adding
    /// parts up against the total, and standing there if this framework behaviour ever changes.
    /// That is what this test is really for — it pins the behaviour the shorter check relies on.
    /// </remarks>
    [Fact]
    public void A_header_that_understates_the_size_truncates_rather_than_smuggles()
    {
        var bomb = Bomb();
        var honest = Declared(bomb);

        Lie(bomb, BombBytes, 4096);
        Assert.Equal(4096, Declared(bomb));

        using var package = OpcPackage.Open(
            new MemoryStream(bomb), limits: new PackageLimits { MaximumPartBytes = 1024 * 1024 });

        var read = package.ReadPart("word/document.xml");

        output.WriteLine($"the header said {honest:N0} bytes and now says 4,096; reading gave {read.Length:N0}");
        Assert.Equal(4096, read.Length);
    }

    /// <summary>
    /// A part inside the limit, several times over, is not inside the limit. Three parts of a
    /// megabyte each against a total of two.
    /// </summary>
    [Fact]
    public void Parts_that_are_each_small_enough_are_stopped_when_they_add_up()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "a.bin", "b.bin", "c.bin" })
            {
                using var stream = archive.CreateEntry(name, CompressionLevel.Optimal).Open();
                stream.Write(new byte[1024 * 1024]);
            }
        }

        using var package = OpcPackage.Open(new MemoryStream(buffer.ToArray()), limits: new PackageLimits
        {
            MaximumPartBytes = 4 * 1024 * 1024,
            MaximumTotalBytes = 2 * 1024 * 1024
        });

        package.ReadPart("a.bin");
        package.ReadPart("b.bin");

        var thrown = Assert.Throws<PackageTooLargeException>(() => package.ReadPart("c.bin"));

        Assert.Contains("in total", thrown.Message);
        output.WriteLine(thrown.Message);
    }

    [Fact]
    public void A_package_of_too_many_parts_is_refused_before_any_of_them_is_read()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var i = 0; i < 200; i++) archive.CreateEntry($"part{i}.bin", CompressionLevel.NoCompression);
        }

        var thrown = Assert.Throws<PackageTooLargeException>(() =>
            OpcPackage.Open(new MemoryStream(buffer.ToArray()), limits: new PackageLimits { MaximumPartCount = 100 }));

        Assert.Contains("200 parts", thrown.Message);
        output.WriteLine(thrown.Message);
    }

    /// <summary>
    /// The same attack a layer up: a part that is small, and says it is made of an entity that is
    /// not. XML has its own way of turning a kilobyte into a gigabyte, and the reader must not
    /// play along.
    /// </summary>
    [Fact]
    public void A_part_that_declares_entities_is_refused()
    {
        var buffer = new MemoryStream();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = new StreamWriter(archive.CreateEntry("word/document.xml").Open());

            // The billion laughs: ten entities, each ten of the one below it.
            stream.Write("""
                <?xml version="1.0"?>
                <!DOCTYPE lol [
                  <!ENTITY lol "lol">
                  <!ENTITY lol1 "&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;&lol;">
                  <!ENTITY lol2 "&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;&lol1;">
                  <!ENTITY lol3 "&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;&lol2;">
                  <!ENTITY lol4 "&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;&lol3;">
                  <!ENTITY lol5 "&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;&lol4;">
                  <!ENTITY lol6 "&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;&lol5;">
                  <!ENTITY lol7 "&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;&lol6;">
                  <!ENTITY lol8 "&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;&lol7;">
                  <!ENTITY lol9 "&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;&lol8;">
                ]>
                <lol>&lol9;</lol>
                """);
        }

        using var package = OpcPackage.Open(new MemoryStream(buffer.ToArray()));

        // Not a size limit: the reader refuses the definition outright, which is what stops the
        // expansion before it starts. Left to itself XDocument.Load parses it and expands the
        // entities without bound, which is how this was found.
        var thrown = Assert.Throws<XmlException>(() => package.ReadPartAsXml("word/document.xml"));

        Assert.Contains("DTD is prohibited", thrown.Message);
        output.WriteLine(thrown.Message);
    }

    /// <summary>
    /// A picture says its own size and is allocated from what it says, so the file need not be
    /// large to ask for a great deal. Fifty thousand pixels square is a header of a few bytes and
    /// seven and a half gigabytes of memory; each format here is asked for it.
    /// </summary>
    [Theory]
    [InlineData("PNG")]
    [InlineData("BMP")]
    [InlineData("GIF")]
    public void A_picture_that_declares_more_pixels_than_it_may_is_refused(string format)
    {
        const int Side = 50_000;

        var image = format switch
        {
            "PNG" => Png(Side, Side),
            "BMP" => Bmp(Side, Side),
            _ => Gif(Side, Side)
        };

        output.WriteLine($"a {format} of {image.Length} bytes declaring {(long)Side * Side:N0} pixels");

        var limit = new PackageLimits().MaximumImagePixels;

        var thrown = Assert.Throws<Images.ImageFormatException>(() => Images.ImageReader.Read(image, limit));
        Assert.Contains("against a limit of", thrown.Message);
        output.WriteLine(thrown.Message);

        // And the forgiving path leaves it out rather than giving up on the document, which is
        // what every other unreadable picture does.
        Assert.Null(Images.ImageReader.TryRead(image, limit));
    }

    /// <summary>
    /// The limit is on the area rather than on either side of it, and it is counted in long
    /// arithmetic: 70,000 squared is 4.9 billion, which does not fit in the int the pixels would
    /// have been allocated with — it comes out as 605 million, so the check would pass and the
    /// buffer would then be written past its end.
    /// </summary>
    [Fact]
    public void A_picture_whose_pixels_overflow_an_int_is_refused()
    {
        const int Side = 70_000;

        unchecked
        {
            Assert.True(Side * Side < 0 || Side * Side < (long)Side * Side, "the overflow this guards is gone");
        }

        var thrown = Assert.Throws<Images.ImageFormatException>(() =>
            Images.ImageReader.Read(Png(Side, Side), new PackageLimits().MaximumImagePixels));

        output.WriteLine(thrown.Message);
    }

    /// <summary>A picture inside the limit is read as it always was.</summary>
    [Fact]
    public void A_picture_inside_the_limit_is_read()
    {
        var image = Images.ImageReader.Read(Png(4, 4), new PackageLimits().MaximumImagePixels);

        Assert.Equal(4, image.Width);
        Assert.Equal(4, image.Height);
    }

    /// <summary>
    /// A document that is merely large is still a document. The limits mean nothing if they are
    /// set where real work lands, so this is what the defaults have to clear: the largest fixture
    /// here, and the real ones Word wrote.
    /// </summary>
    [Fact]
    public void Every_fixture_converts_well_inside_the_defaults()
    {
        var limits = new PackageLimits();
        var worst = (Part: 0L, Total: 0L, Count: 0, Name: string.Empty);

        IEnumerable<(string Name, byte[] Docx)> everything =
            [.. Fixtures.All.Keys.Order().Select(name => (name, Fixtures.Build(name))), .. TestFonts.RealDocuments()];

        foreach (var (name, docx) in everything)
        {
            using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);

            var part = archive.Entries.Max(e => e.Length);
            var total = archive.Entries.Sum(e => e.Length);

            if (part > worst.Part) worst = (part, total, archive.Entries.Count, name);
        }

        output.WriteLine(
            $"the largest of {Fixtures.All.Count} fixtures and the real documents is '{worst.Name}': " +
            $"{worst.Part:N0} bytes in one part, {worst.Total:N0} in all, {worst.Count} parts");

        // Two orders of magnitude of headroom, which is the point: these limits are for documents
        // nobody here has written, and they are nowhere near what this suite reaches.
        Assert.True(worst.Part * 100 < limits.MaximumPartBytes, "a fixture is within 100x of the part limit");
        Assert.True(worst.Total * 100 < limits.MaximumTotalBytes, "a fixture is within 100x of the total limit");
        Assert.True(worst.Count * 100 < limits.MaximumPartCount, "a fixture is within 100x of the part count");
    }

    /// <summary>
    /// A PNG that declares the given size. Its IHDR is honest and its pixel data is not there at
    /// all, which is the point: the allocation happens on the strength of the header.
    /// </summary>
    private static byte[] Png(int width, int height)
    {
        var png = new MemoryStream();
        png.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0d, 0x0a, 0x1a, 0x0a]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;  // bits a sample
        header[9] = 2;  // truecolour

        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", Idat(width, height));
        Chunk(png, "IEND", []);

        return png.ToArray();

        // A row of pixels is a filter byte and three bytes a pixel, the lot deflated. Written
        // honestly for a small picture; for a large one it is a few bytes that claim to be a great
        // many, which is the attack.
        static byte[] Idat(int width, int height)
        {
            if ((long)width * height > 1_000_000) return [0x78, 0x9c, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];

            var raw = new byte[height * (1 + width * 3)];
            var deflated = new MemoryStream();

            using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
                zlib.Write(raw);

            return deflated.ToArray();
        }

        static void Chunk(Stream into, string type, byte[] body)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
            into.Write(length);
            into.Write(Encoding.ASCII.GetBytes(type));
            into.Write(body);
            into.Write([0, 0, 0, 0]);  // the checksum, which this reader does not verify
        }
    }

    /// <summary>The same for a Windows bitmap: a forty byte header and no pixels.</summary>
    private static byte[] Bmp(int width, int height)
    {
        var bmp = new byte[64];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), width);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), height);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);

        return bmp;
    }

    /// <summary>And for a GIF, whose logical screen is what is allocated.</summary>
    private static byte[] Gif(int width, int height)
    {
        var gif = new byte[32];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(gif, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(6), (ushort)Math.Min(width, ushort.MaxValue));
        BinaryPrimitives.WriteUInt16LittleEndian(gif.AsSpan(8), (ushort)Math.Min(height, ushort.MaxValue));

        return gif;
    }

    /// <summary>What a package's central directory says a part decompresses to.</summary>
    private static long Declared(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return archive.Entries[0].Length;
    }

    /// <summary>
    /// Rewrites the uncompressed size a package declares for its only part, wherever it appears —
    /// the local header, the central directory, and the data descriptor if there is one.
    /// </summary>
    private static void Lie(byte[] package, long from, long to)
    {
        var truth = BitConverter.GetBytes((uint)from);
        var lie = BitConverter.GetBytes((uint)to);
        var found = 0;

        for (var i = 0; i + 4 <= package.Length; i++)
        {
            if (package[i] != truth[0] || package[i + 1] != truth[1] ||
                package[i + 2] != truth[2] || package[i + 3] != truth[3])
            {
                continue;
            }

            lie.CopyTo(package, i);
            found++;
        }

        Assert.True(found > 0, "the declared size was not found in the package");
    }
}
