namespace n8PDF.Fonts;

/// <summary>
/// Rebuilds a single-face SFNT container from a parsed font's tables.
/// </summary>
/// <remarks>
/// A PDF font file must contain exactly one face, so a face taken from a <c>.ttc</c> collection
/// cannot be embedded as-is — its table directory points into a shared file. Repackaging copies
/// the tables this face actually uses into a fresh container. It also drops hinting and layout
/// tables no PDF consumer reads, which shrinks the embedded program noticeably.
/// </remarks>
internal static class SfntRepackager
{
    /// <summary>
    /// Tables kept for TrueType-outline fonts. Everything a rasteriser needs to draw and space
    /// the glyphs, and nothing else.
    /// </summary>
    private static readonly string[] TrueTypeTables =
        ["cmap", "cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "post", "prep", "OS/2"];

    /// <summary>Tables kept for CFF-outline (OpenType/PostScript) fonts.</summary>
    private static readonly string[] CffTables =
        ["CFF ", "cmap", "head", "hhea", "hmtx", "maxp", "name", "post", "OS/2"];

    /// <summary>
    /// Builds an embeddable font program, holding only the given glyphs where the format allows
    /// it. Passing no glyphs embeds the whole face.
    /// </summary>
    /// <summary>
    /// Subsets a CFF table, keeping the result only if it is actually smaller — a font whose
    /// outlines were nearly all used gains nothing, and a rebuild that came out larger would be
    /// the wrong answer to the question this was asked.
    /// </summary>
    private static byte[]? BuildCffSubset(TrueTypeFont font, IReadOnlyCollection<ushort> usedGlyphs)
    {
        if (!font.HasCffOutlines || !font.Tables.TryGetValue("CFF ", out var cff)) return null;

        var length = Math.Min(cff.Length, font.SourceData.Length - cff.Offset);
        if (length <= 0) return null;

        var subset = CffSubset.Build(font.SourceData, cff.Offset, length, usedGlyphs);

        return subset is not null && subset.Length < length ? subset : null;
    }

    public static byte[] BuildStandalone(
        TrueTypeFont font, IReadOnlyCollection<ushort>? usedGlyphs, out bool subsetted)
    {
        var subset = usedGlyphs is { Count: > 0 } ? GlyphSubset.Build(font, usedGlyphs) : null;
        var charStrings = usedGlyphs is { Count: > 0 } ? BuildCffSubset(font, usedGlyphs) : null;

        // Whether anything was actually left out, which is what decides if the result may be
        // called a subset — a font this cannot rebuild is embedded whole and named plainly.
        subsetted = subset is not null || charStrings is not null;

        var wanted = font.HasCffOutlines ? CffTables : TrueTypeTables;

        // Table records must appear in ascending tag order in the directory.
        var included = wanted
            .Where(font.Tables.ContainsKey)
            .OrderBy(tag => tag, StringComparer.Ordinal)
            .ToList();

        if (included.Count == 0)
            throw new FontFormatException("The font has no tables that can be embedded.");

        var source = font.SourceData;
        var tableCount = included.Count;
        var directorySize = 12 + tableCount * 16;

        // Lay out the tables first so the directory can record real offsets.
        var payloads = new List<(string Tag, byte[] Data, int Offset)>(tableCount);
        var offset = directorySize;
        foreach (var tag in included)
        {
            var record = font.Tables[tag];
            var length = Math.Min(record.Length, source.Length - record.Offset);
            if (length <= 0) continue;

            var data = new byte[length];
            Array.Copy(source, record.Offset, data, 0, length);

            if (charStrings is not null && tag == "CFF ")
            {
                data = charStrings;
            }
            else if (subset is { } tables)
            {
                if (tag == "glyf") data = tables.Glyf;
                else if (tag == "loca") data = tables.Loca;
                else if (tag == "post") data = GlyphSubset.NamelessPost(source, record);
                else if (tag == "hmtx" && tables.Hmtx is { } metrics) data = metrics;

                // The rebuilt locations are in the long format whatever the original used, and
                // head is where a reader is told which to expect.
                else if (tag == "head" && data.Length >= 52) WriteUInt16(data, 50, 1);

                // hhea says how many glyphs carry their own advance, so it has to agree with a
                // metrics table that was cut short.
                else if (tag == "hhea" && tables.Hmtx is not null && data.Length >= 36)
                    WriteUInt16(data, 34, (ushort)tables.MetricCount);
            }

            payloads.Add((tag, data, offset));
            offset += Align4(data.Length);
        }

        var output = new byte[offset];

        // The version tag tells consumers which outline format follows.
        WriteUInt32(output, 0, font.HasCffOutlines ? 0x4f54544fu : 0x00010000u);
        WriteUInt16(output, 4, (ushort)payloads.Count);

        // The binary-search hint fields. Consumers compute their own, but malformed values
        // trip some strict validators, so they are filled in properly.
        var entrySelector = (int)Math.Floor(Math.Log2(Math.Max(payloads.Count, 1)));
        var searchRange = (int)Math.Pow(2, entrySelector) * 16;
        WriteUInt16(output, 6, (ushort)searchRange);
        WriteUInt16(output, 8, (ushort)entrySelector);
        WriteUInt16(output, 10, (ushort)(payloads.Count * 16 - searchRange));

        var directoryPosition = 12;
        foreach (var (tag, data, tableOffset) in payloads)
        {
            Array.Copy(data, 0, output, tableOffset, data.Length);

            // head carries a checksum over the whole file, which cannot be known until the file
            // is assembled. Zero it now and patch it at the end.
            if (tag == "head" && data.Length >= 12)
                WriteUInt32(output, tableOffset + 8, 0);

            output[directoryPosition + 0] = (byte)tag[0];
            output[directoryPosition + 1] = (byte)tag[1];
            output[directoryPosition + 2] = (byte)tag[2];
            output[directoryPosition + 3] = (byte)tag[3];
            WriteUInt32(output, directoryPosition + 4, Checksum(output, tableOffset, data.Length));
            WriteUInt32(output, directoryPosition + 8, (uint)tableOffset);
            WriteUInt32(output, directoryPosition + 12, (uint)data.Length);
            directoryPosition += 16;
        }

        PatchHeadChecksum(output, payloads);
        return output;
    }

    /// <summary>
    /// Writes <c>head.checkSumAdjustment</c>, defined as 0xB1B0AFBA minus the checksum of the
    /// entire file computed with that field set to zero.
    /// </summary>
    private static void PatchHeadChecksum(byte[] output, List<(string Tag, byte[] Data, int Offset)> payloads)
    {
        var head = payloads.FirstOrDefault(p => p.Tag == "head");
        if (head.Tag is null || head.Data.Length < 12) return;

        var fileChecksum = Checksum(output, 0, output.Length);
        WriteUInt32(output, head.Offset + 8, unchecked(0xb1b0afbau - fileChecksum));
    }

    /// <summary>
    /// SFNT checksum: the sum of the region's big-endian 32-bit words, with the tail zero-padded
    /// to a word boundary, wrapping on overflow.
    /// </summary>
    private static uint Checksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        var end = offset + length;

        for (var i = offset; i < end; i += 4)
        {
            uint word = 0;
            for (var b = 0; b < 4; b++)
            {
                word <<= 8;
                if (i + b < end && i + b < data.Length) word |= data[i + b];
            }

            unchecked { sum += word; }
        }

        return sum;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void WriteUInt16(byte[] buffer, int offset, ushort value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    private static void WriteUInt32(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }
}
