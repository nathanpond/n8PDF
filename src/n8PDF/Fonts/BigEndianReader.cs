namespace n8PDF.Fonts;

/// <summary>
/// Cursor over a font file. SFNT data is big-endian throughout, which is the opposite of the
/// host order on every platform we target, so every multi-byte read goes through here.
/// </summary>
internal struct BigEndianReader(byte[] data, int position = 0)
{
    private readonly byte[] _data = data;

    public int Position { get; set; } = position;

    public readonly int Length => _data.Length;

    public void Skip(int count) => Position += count;

    public byte ReadByte()
    {
        Require(1);
        return _data[Position++];
    }

    public ushort ReadUInt16()
    {
        Require(2);
        var value = (ushort)((_data[Position] << 8) | _data[Position + 1]);
        Position += 2;
        return value;
    }

    public short ReadInt16() => (short)ReadUInt16();

    public uint ReadUInt32()
    {
        Require(4);
        var value = (uint)((_data[Position] << 24) | (_data[Position + 1] << 16) |
                           (_data[Position + 2] << 8) | _data[Position + 3]);
        Position += 4;
        return value;
    }

    public int ReadInt32() => (int)ReadUInt32();

    /// <summary>Reads a 16.16 fixed-point value, used for versions and the italic angle.</summary>
    public double ReadFixed() => ReadInt32() / 65536.0;

    /// <summary>Reads a four-character table tag as ASCII.</summary>
    public string ReadTag()
    {
        Require(4);
        var tag = new string([
            (char)_data[Position], (char)_data[Position + 1],
            (char)_data[Position + 2], (char)_data[Position + 3]
        ]);
        Position += 4;
        return tag;
    }

    public byte[] ReadBytes(int count)
    {
        Require(count);
        var bytes = new byte[count];
        Array.Copy(_data, Position, bytes, 0, count);
        Position += count;
        return bytes;
    }

    private readonly void Require(int count)
    {
        if (Position < 0 || Position + count > _data.Length)
            throw new FontFormatException(
                $"Read of {count} byte(s) at offset {Position} runs past the end of the font data ({_data.Length} bytes).");
    }
}

/// <summary>Raised when font data is malformed or uses a construct we do not support.</summary>
public sealed class FontFormatException(string message) : Exception(message);
