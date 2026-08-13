namespace n8PDF.Tests.Support;

/// <summary>
/// Writes a copy of a font face with Apple's own layout tables taken out.
/// </summary>
/// <remarks>
/// This exists to make HarfBuzz answer the question being asked. Several of the faces macOS ships
/// for these scripts carry two complete descriptions of how to shape them: the OpenType tables
/// that every other platform reads, and Apple's <c>morx</c> state machines. HarfBuzz prefers the
/// Apple tables wherever a font has them, so asking it to shape one of those faces answers a
/// question about <c>morx</c> — which is not what this converter implements, and not what Word
/// reads either.
///
/// The two mostly agree. Where they do not, the difference is real and worth seeing rather than
/// worth hiding: Khmer Sangam MN's OpenType tables write a consonant and its vowel as a shape plus
/// a blank, and its state machine deletes the blank instead. Taking the Apple tables out is what
/// makes the comparison one about the same tables on both sides.
/// </remarks>
public static class OpenTypeOnly
{
    private static readonly string[] Apple = ["morx", "mort", "kerx", "feat", "ankr"];

    /// <summary>The other way round: a copy holding Apple's tables and no OpenType ones.</summary>
    /// <remarks>
    /// <c>GDEF</c> stays: it says what each glyph is rather than what to do with it, and every
    /// engine reads it whichever description of the shaping it is following.
    /// </remarks>
    private static readonly string[] OpenType = ["GSUB", "GPOS"];

    private static readonly Dictionary<string, string> Written = [];

    /// <summary>The path of a copy of one face of a font file, holding no Apple layout tables.</summary>
    /// <summary>
    /// The mirror of this: a copy with the OpenType tables taken out instead, for asking what a
    /// face's own tables say where it carries both descriptions.
    /// </summary>
    public static string AppleOnly(string path, int face = 0) => Copy(path, face, apple: true);

    public static string Copy(string path, int face = 0, bool apple = false)
    {
        var key = $"{path}#{face}#{apple}";

        lock (Written)
        {
            if (Written.TryGetValue(key, out var written) && File.Exists(written)) return written;

            var data = File.ReadAllBytes(path);
            var start = FaceOffset(data, face);

            var count = ReadUInt16(data, start + 4);
            var tables = new List<(string Tag, int Offset, int Length)>();

            for (var i = 0; i < count; i++)
            {
                var record = start + 12 + i * 16;

                var tag = System.Text.Encoding.ASCII.GetString(data, record, 4);
                if (Array.IndexOf(apple ? OpenType : Apple, tag) >= 0) continue;

                tables.Add((tag, (int)ReadUInt32(data, record + 8), (int)ReadUInt32(data, record + 12)));
            }

            tables.Sort((a, b) => string.CompareOrdinal(a.Tag, b.Tag));

            var directory = 12 + tables.Count * 16;
            var body = new List<byte>();
            var records = new List<byte>();

            foreach (var (tag, offset, length) in tables)
            {
                while (body.Count % 4 != 0) body.Add(0);

                var at = directory + body.Count;

                records.AddRange(System.Text.Encoding.ASCII.GetBytes(tag));
                records.AddRange(BigEndian(0));       // the checksum, which nothing here verifies
                records.AddRange(BigEndian((uint)at));
                records.AddRange(BigEndian((uint)length));

                body.AddRange(data.AsSpan(offset, length).ToArray());
            }

            var output = new List<byte>();

            output.AddRange(BigEndian(ReadUInt32(data, start)));  // the same kind of outlines
            output.AddRange(BigEndian16((ushort)tables.Count));
            output.AddRange(BigEndian16(0));
            output.AddRange(BigEndian16(0));
            output.AddRange(BigEndian16(0));
            output.AddRange(records);
            output.AddRange(body);

            var name = Path.GetFileNameWithoutExtension(path).Replace(' ', '-');

            var file = Path.Combine(Path.GetTempPath(),
                $"n8pdf-{(apple ? "apple" : "opentype")}-{name}-{face}.ttf");

            File.WriteAllBytes(file, [.. output]);

            return Written[key] = file;
        }
    }

    private static int FaceOffset(byte[] data, int face)
    {
        var tag = System.Text.Encoding.ASCII.GetString(data, 0, 4);

        return tag == "ttcf" ? (int)ReadUInt32(data, 12 + face * 4) : 0;
    }

    private static ushort ReadUInt16(byte[] data, int at) => (ushort)((data[at] << 8) | data[at + 1]);

    private static uint ReadUInt32(byte[] data, int at) =>
        ((uint)data[at] << 24) | ((uint)data[at + 1] << 16) | ((uint)data[at + 2] << 8) | data[at + 3];

    private static byte[] BigEndian(uint value) =>
        [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];

    private static byte[] BigEndian16(ushort value) => [(byte)(value >> 8), (byte)value];
}
