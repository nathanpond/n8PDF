using System.Xml.Linq;
using n8PDF.Packaging;

namespace n8PDF.Ooxml;

/// <summary>
/// The faces a document carries with it: <c>w:embedRegular</c> and its three siblings in
/// <c>fontTable.xml</c>, each pointing at a font part obfuscated with the key written beside it.
/// </summary>
/// <remarks>
/// Word writes an embedded font as an "obfuscated" part: the first 32 bytes of the file are XORed
/// with the 16 bytes of the GUID in <c>w:fontKey</c>, taken in reverse of the order its hex
/// digits are written in. It is not encryption — the key sits in the same package as the data —
/// but the bytes are not a usable SFNT until it is undone (#62).
///
/// A face that cannot be read — larger than <see cref="PackageLimits.MaximumFontBytes"/>, a key
/// that does not parse, bytes that are no SFNT once deobfuscated — is left out the way an
/// unreadable image is left out: the conversion proceeds, and that family resolves as though the
/// document had not carried it. From here on the font parser is reading attacker-controlled
/// bytes, which is what the follow-up audit of <c>Fonts/</c> is about.
/// </remarks>
internal static class EmbeddedFonts
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly string[] EmbedElements =
        ["embedRegular", "embedBold", "embedItalic", "embedBoldItalic"];

    /// <summary>Every embedded font part the font table declares, deobfuscated and bounded.</summary>
    public static IReadOnlyList<byte[]> Read(
        OpcPackage package, string mainPartName, PackageLimits limits)
    {
        var found = new List<byte[]>();

        var tablePart = package.GetRelatedPartName(mainPartName, OpcPackage.FontTableRelationship);
        if (tablePart is null || package.TryReadPartAsXml(tablePart)?.Root is not { } table)
            return found;

        foreach (var font in table.Elements(W + "font"))
        foreach (var element in EmbedElements)
        {
            if (font.Element(W + element) is not { } embed) continue;
            if (embed.Attribute(R + "id")?.Value is not { } id) continue;
            if (package.GetRelationshipById(tablePart, id) is not { IsExternal: false } relationship)
                continue;

            var partName = package.ResolveTarget(tablePart, relationship.Target);
            if (!package.HasPart(partName)) continue;

            byte[] data;
            try
            {
                // A font part larger than the per-part cap throws before the font-size check can
                // even run; and any read failure is that one face's cost, not the conversion's
                // (#199). A whole-package breach is not caught here — it is fatal by design.
                data = package.ReadPart(partName);
            }
            catch (Exception e) when (e is IOException or InvalidDataException
                or PackageTooLargeException { WholePackage: false })
            {
                continue;
            }

            if (data.Length > limits.MaximumFontBytes) continue;

            if (embed.Attribute(W + "fontKey")?.Value is { } fontKey)
                Deobfuscate(data, fontKey);

            if (IsSfnt(data)) found.Add(data);
        }

        return found;
    }

    /// <summary>
    /// Undoes Word's obfuscation in place: the first 32 bytes XORed with the GUID's 16 bytes in
    /// reverse of their written order, applied twice over.
    /// </summary>
    // Internal, not private, so the fuzzing harness can attack the deobfuscate-then-parse
    // path directly (#263); it is still not part of the public surface.
    internal static void Deobfuscate(byte[] data, string fontKey)
    {
        Span<char> hex = stackalloc char[32];
        var digits = 0;

        foreach (var c in fontKey)
        {
            if (!Uri.IsHexDigit(c)) continue;
            if (digits == 32) return;
            hex[digits++] = c;
        }

        if (digits != 32) return;

        Span<byte> key = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
            key[i] = (byte)(Convert.ToInt32(hex.Slice(i * 2, 2).ToString(), 16));

        for (var i = 0; i < Math.Min(32, data.Length); i++)
            data[i] ^= key[15 - (i % 16)];
    }

    /// <summary>Whether the bytes begin the way a font file must.</summary>
    private static bool IsSfnt(byte[] data)
    {
        if (data.Length < 4) return false;

        var tag = (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);
        return tag is 0x00010000 or 0x74727565 /* true */ or 0x4F54544F /* OTTO */ or 0x74746366 /* ttcf */;
    }
}
