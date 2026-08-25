class Reader { public int ReadInt32() => 0; public ushort ReadUInt16() => 0; }

class Fixture
{
    // ruleid: alloc-sized-by-unbounded-read
    byte[] Bad(Reader r) => new byte[r.ReadInt32()];

    // ruleid: alloc-sized-by-unbounded-read
    int[] BadShort(Reader r) => new int[r.ReadUInt16()];

    // Read, bound, then allocate — the count never sizes the array straight off the wire.
    // ok: alloc-sized-by-unbounded-read
    byte[] Good(Reader r, int remaining)
    {
        var count = r.ReadInt32();
        if (count < 0 || count > remaining) return System.Array.Empty<byte>();
        return new byte[count];
    }
}
