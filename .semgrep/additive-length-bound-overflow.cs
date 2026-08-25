class Fixture
{
    // ruleid: additive-length-bound-overflow
    bool Bad(byte[] data, int pos, int count) => pos + count > data.Length;

    void BadGuard(byte[] data, int position, int n)
    {
        // ruleid: additive-length-bound-overflow
        if (position + n >= data.Length) throw new System.IO.EndOfStreamException();
    }

    // A small constant addend cannot overflow a buffer position — the decoders' common form,
    // deliberately not flagged.
    // ok: additive-length-bound-overflow
    bool OkConst(byte[] data, int offset) => offset + 4 > data.Length;

    // The committed fix (#181): subtract from the length so the sum cannot overflow.
    // ok: additive-length-bound-overflow
    bool Good(byte[] data, int pos, int count) => count < 0 || pos > data.Length - count;
}
