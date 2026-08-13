namespace n8PDF.Images;

/// <summary>
/// The arithmetic decoder a JPEG may use in place of the usual code tables.
/// </summary>
/// <remarks>
/// Huffman coding gives every symbol a whole number of bits, which wastes a fraction of one on
/// nearly every symbol. Arithmetic coding does not: it keeps a running interval and narrows it by
/// the probability of each decision, so a decision the coder is nearly certain of costs almost
/// nothing at all. The probabilities are not sent — both sides estimate them from what they have
/// already seen, by the same rules, and the table below is those rules: a hundred and thirteen
/// states, each with the probability it stands for and where to move on being right or wrong.
///
/// Nothing recovers from a mistake here. A Huffman stream resynchronises at the next code, but an
/// interval narrowed by the wrong probability is wrong for ever after, so a decoder of this is
/// either right or produces noise. That is worth knowing when reading the tests: agreeing with
/// another decoder on a whole picture is not weak evidence of the table being right, it is nearly
/// conclusive.
/// </remarks>
internal sealed class JpegArithmetic
{
    private readonly byte[] _data;

    public JpegArithmetic(byte[] data, int at)
    {
        _data = data;
        _at = at;

        Begin();
    }

    /// <summary>How likely the less probable answer is, and where each answer leads.</summary>
    private readonly record struct State(int Probability, byte OnLikely, byte OnUnlikely, bool Swap);

    private static readonly State[] States =
    [
        new(0x5A1D, 1, 1, true), new(0x2586, 2, 14, false), new(0x1114, 3, 16, false),
        new(0x080B, 4, 18, false), new(0x03D8, 5, 20, false), new(0x01DA, 6, 23, false),
        new(0x00E5, 7, 25, false), new(0x006F, 8, 28, false), new(0x0036, 9, 30, false),
        new(0x001A, 10, 33, false), new(0x000D, 11, 35, false), new(0x0006, 12, 9, false),
        new(0x0003, 13, 10, false), new(0x0001, 13, 12, false), new(0x5A7F, 15, 15, true),
        new(0x3F25, 16, 36, false), new(0x2CF2, 17, 38, false), new(0x207C, 18, 39, false),
        new(0x17B9, 19, 40, false), new(0x1182, 20, 42, false), new(0x0CEF, 21, 43, false),
        new(0x09A1, 22, 45, false), new(0x072F, 23, 46, false), new(0x055C, 24, 48, false),
        new(0x0406, 25, 49, false), new(0x0303, 26, 51, false), new(0x0240, 27, 52, false),
        new(0x01B1, 28, 54, false), new(0x0144, 29, 56, false), new(0x00F5, 30, 57, false),
        new(0x00B7, 31, 59, false), new(0x008A, 32, 60, false), new(0x0068, 33, 62, false),
        new(0x004E, 34, 63, false), new(0x003B, 35, 32, false), new(0x002C, 9, 33, false),
        new(0x5AE1, 37, 37, true), new(0x484C, 38, 64, false), new(0x3A0D, 39, 65, false),
        new(0x2EF1, 40, 67, false), new(0x261F, 41, 68, false), new(0x1F33, 42, 69, false),
        new(0x19A8, 43, 70, false), new(0x1518, 44, 72, false), new(0x1177, 45, 73, false),
        new(0x0E74, 46, 74, false), new(0x0BFB, 47, 75, false), new(0x09F8, 48, 77, false),
        new(0x0861, 49, 78, false), new(0x0706, 50, 79, false), new(0x05CD, 51, 48, false),
        new(0x04DE, 52, 50, false), new(0x040F, 53, 50, false), new(0x0363, 54, 51, false),
        new(0x02D4, 55, 52, false), new(0x025C, 56, 53, false), new(0x01F8, 57, 54, false),
        new(0x01A4, 58, 55, false), new(0x0160, 59, 56, false), new(0x0125, 60, 57, false),
        new(0x00F6, 61, 58, false), new(0x00CB, 62, 59, false), new(0x00AB, 63, 61, false),
        new(0x008F, 32, 61, false), new(0x5B12, 65, 65, true), new(0x4D04, 66, 80, false),
        new(0x412C, 67, 81, false), new(0x37D8, 68, 82, false), new(0x2FE8, 69, 83, false),
        new(0x293C, 70, 84, false), new(0x2379, 71, 86, false), new(0x1EDF, 72, 87, false),
        new(0x1AA9, 73, 87, false), new(0x174E, 74, 72, false), new(0x1424, 75, 72, false),
        new(0x119C, 76, 74, false), new(0x0F6B, 77, 74, false), new(0x0D51, 78, 75, false),
        new(0x0BB6, 79, 77, false), new(0x0A40, 48, 77, false), new(0x5832, 81, 80, true),
        new(0x4D1C, 82, 88, false), new(0x438E, 83, 89, false), new(0x3BDD, 84, 90, false),
        new(0x34EE, 85, 91, false), new(0x2EAE, 86, 92, false), new(0x299A, 87, 93, false),
        new(0x2516, 71, 86, false), new(0x5570, 89, 88, true), new(0x4CA9, 90, 95, false),
        new(0x44D9, 91, 96, false), new(0x3E22, 92, 97, false), new(0x3824, 93, 99, false),
        new(0x32B4, 94, 99, false), new(0x2E17, 86, 93, false), new(0x56A8, 96, 95, true),
        new(0x4F46, 97, 101, false), new(0x47E5, 98, 102, false), new(0x41CF, 99, 103, false),
        new(0x3C3D, 100, 104, false), new(0x375E, 93, 99, false), new(0x5231, 102, 105, false),
        new(0x4C0F, 103, 106, false), new(0x4639, 104, 107, false), new(0x415E, 99, 103, false),
        new(0x5627, 106, 105, true), new(0x50E7, 107, 108, false), new(0x4B85, 103, 109, false),
        new(0x5597, 109, 110, false), new(0x504F, 107, 111, false), new(0x5A10, 111, 110, true),
        new(0x5522, 109, 112, false), new(0x59EB, 111, 112, true),

        // One more that leads only to itself. Some decisions — the sign of a number, above all —
        // are as likely one way as the other however many have been seen, so they are weighed
        // against a state that never learns.
        new(0x5A1D, Fixed, Fixed, false)
    ];

    /// <summary>The state that never moves, for the decisions nothing can be learned about.</summary>
    public const byte Fixed = 113;

    /// <summary>What has been read of the stream, which the interval is placed against.</summary>
    private long _code;

    /// <summary>The width of the interval the decisions so far have narrowed the stream to.</summary>
    private long _interval;

    /// <summary>How many bits of the register are still worth reading, less sixteen at the start.</summary>
    private int _count;

    private int _at;

    /// <summary>True once a marker has been met, after which the reader is fed noughts.</summary>
    private bool _ended;

    public int Position => _at;

    /// <summary>Empties the register, which the first decision fills from the stream.</summary>
    private void Begin()
    {
        _code = 0;
        _interval = 0;
        _count = -16;
        _ended = false;
    }

    /// <summary>
    /// Reads one decision against the statistics kept for its context.
    /// </summary>
    /// <remarks>
    /// The interval is divided in two — the part the coder thinks likely and the part it does not
    /// — and which part the code falls in is the answer. Where the unlikely part has grown to be
    /// the larger of the two, the two change places, which is the conditional exchange that makes
    /// this so easy to write almost correctly.
    ///
    /// The other easy thing to get wrong is which of the two ways of holding the register this is
    /// written in. The standard describes the coder with the stream at the top of the register and
    /// a nought stuffed after every 0xff as a bit; JPEG streams are written the other way, with the
    /// stream shifted in at the bottom and the nought stuffed as a whole byte, exactly as elsewhere
    /// in the format. The two are not compatible and nothing warns you which you have — a decoder
    /// written the first way reads a real file as noise.
    /// </remarks>
    public int Decode(byte[] statistics, int index)
    {
        // Widen the interval until it fills the register again, taking in bytes as it goes. The
        // count starts below nought so that the first decision draws in two bytes before deciding
        // anything.
        while (_interval < 0x8000)
        {
            if (--_count < 0)
            {
                _code = (_code << 8) | (uint)NextByte();

                if ((_count += 8) < 0 && ++_count == 0) _interval = 0x8000;
            }

            _interval <<= 1;
        }

        var stored = statistics[index];
        var state = States[stored & 0x7f];

        var probability = (long)state.Probability;
        var onLikely = state.OnLikely;

        // The swap rides on the top bit of where being wrong leads, which is where the answer to a
        // decision is kept as well, so that both are one exclusive or.
        var onUnlikely = (byte)(state.OnUnlikely | (state.Swap ? 0x80 : 0));

        var mark = _interval - probability;
        _interval = mark;
        mark <<= _count;

        if (_code >= mark)
        {
            _code -= mark;

            // In the unlikely part — unless that part is now the larger, in which case the two have
            // changed places and this is the likely answer after all.
            if (_interval < probability) statistics[index] = (byte)((stored & 0x80) ^ onLikely);
            else
            {
                statistics[index] = (byte)((stored & 0x80) ^ onUnlikely);
                stored ^= 0x80;
            }

            _interval = probability;
        }
        else if (_interval < 0x8000)
        {
            // The likely part, but too narrow to have stayed the larger of the two.
            if (_interval < probability)
            {
                statistics[index] = (byte)((stored & 0x80) ^ onUnlikely);
                stored ^= 0x80;
            }
            else statistics[index] = (byte)((stored & 0x80) ^ onLikely);
        }

        return stored >> 7;
    }

    /// <summary>
    /// Takes in the next byte. A 0xff is written with a nought after it so that it cannot be taken
    /// for a marker, and where it is a marker after all the stream is over: what follows is not
    /// picture, and the reader is fed noughts for as long as it asks.
    /// </summary>
    private int NextByte()
    {
        if (_ended) return 0;

        var at = _at;
        var value = _at < _data.Length ? _data[_at++] : 0xd9;

        if (value != 0xff) return value;

        while (value == 0xff) value = _at < _data.Length ? _data[_at++] : 0xd9;

        if (value == 0) return 0xff;

        // A marker. Leave the reader standing at it rather than past it, since what reads the
        // stream above this looks there for what to do next.
        _ended = true;
        _at = at;

        return 0;
    }

    /// <summary>Begins again at the next run of the stream, after a restart marker.</summary>
    public void Restart()
    {
        while (_at + 1 < _data.Length)
        {
            if (_data[_at] == 0xff && _data[_at + 1] >= 0xd0 && _data[_at + 1] <= 0xd7)
            {
                _at += 2;
                break;
            }

            _at++;
        }

        Begin();
    }
}
