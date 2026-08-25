using n8PDF.Images;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The contract both doc comments promise (#48): a malformed image costs its own placement, not
/// the conversion — <c>TryRead</c> returns null rather than letting anything escape.
/// </summary>
/// <remarks>
/// This corpus holds the net to its word, one malformed file per family plus the shapes the
/// audit saw escape. It deliberately tests <c>TryRead</c> and nothing deeper: the individual
/// decoder defects (#2–#47) each carry their own decoder-level test, where this net cannot
/// swallow the evidence, and this file is only the promise that whatever they throw stays
/// inside the boundary.
/// </remarks>
public class HostileImageTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Corpus.Keys) data.Add(name);
            return data;
        }
    }

    private static byte[] Bytes(params int[] values) => [.. values.Select(v => (byte)v)];

    private static byte[] Garbage(int count)
    {
        // Deterministic noise: hostile enough to wander, fixed enough to reproduce.
        var bytes = new byte[count];
        for (var i = 0; i < count; i++) bytes[i] = (byte)(i * 73 + 41);
        return bytes;
    }

    private static readonly IReadOnlyDictionary<string, byte[]> Corpus = new Dictionary<string, byte[]>
    {
        // Each begins the way its family must, so IsSupported lets it through to the decoder,
        // and goes wrong immediately after.
        ["png-garbage"] = [.. Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A), .. Garbage(24)],
        ["png-huge-ihdr"] =
        [
            .. Bytes(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            .. Bytes(0, 0, 0, 13), .. "IHDR"u8.ToArray(),
            .. Bytes(0x7F, 0xFF, 0xFF, 0xFF), .. Bytes(0x7F, 0xFF, 0xFF, 0xFF),
            .. Bytes(16, 3, 0, 0, 0), .. Garbage(8)
        ],
        ["jpeg-garbage"] = [.. Bytes(0xFF, 0xD8, 0xFF, 0xE0), .. Garbage(32)],
        ["gif-truncated"] = [.. "GIF89a"u8.ToArray(), .. Garbage(4)],
        ["gif-descriptor-cut"] =
        [
            .. "GIF87a"u8.ToArray(),
            .. Bytes(4, 0, 4, 0, 0x00, 0, 0), // screen, no colour table
            0x2C, 0, 0                        // an image descriptor with its tail missing
        ],
        ["bmp-garbage"] = [.. "BM"u8.ToArray(), .. Garbage(24)],
        ["tiff-le-garbage"] = [.. Bytes(0x49, 0x49, 0x2A, 0x00), .. Garbage(32)],
        ["tiff-be-garbage"] = [.. Bytes(0x4D, 0x4D, 0x00, 0x2A), .. Garbage(32)],
        ["emf-garbage"] =
        [
            .. Bytes(0x01, 0, 0, 0), .. Garbage(36),
            0x20, 0x45, 0x4D, 0x46, .. Garbage(40)
        ]
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void A_malformed_image_costs_its_own_placement_and_nothing_else(string name)
    {
        var thrown = Record.Exception(() =>
        {
            var image = ImageReader.TryRead(Corpus[name]);
            _output.WriteLine($"{name}: {(image is null ? "left out" : "decoded")}");
            Assert.Null(image);
        });

        Assert.True(thrown is null,
            $"'{name}' escaped the boundary with {thrown?.GetType().Name}: {thrown?.Message}");
    }
}
