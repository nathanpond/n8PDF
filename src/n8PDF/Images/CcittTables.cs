namespace n8PDF.Images;

/// <summary>
/// The code tables the fax encodings are built on.
/// </summary>
/// <remarks>
/// A fax is a page of black on white, and what is sent is not the pixels but the lengths of the
/// runs they fall into: so many white, then so many black, and so on to the end of the line. The
/// lengths are written in a Huffman code fixed by the standard rather than built from the page, so
/// these tables are the encoding — every reader and writer of a fax carries the same ones.
///
/// Runs up to 63 have a code of their own. Longer ones are written as a makeup code for the
/// multiple of 64 below them and then a terminating code for the remainder, which is why a run of
/// 64 is two codes and a run of 63 is one. The makeup codes above 1728 are shared by both colours.
/// </remarks>
internal static class CcittTables
{
    /// <summary>A code: its bits, and how many of them there are.</summary>
    internal readonly record struct Code(int Bits, int Length, int Run);

    internal static readonly Code[] White =
    [
        // The runs that have a code of their own.
        new(0b00110101, 8, 0), new(0b000111, 6, 1), new(0b0111, 4, 2), new(0b1000, 4, 3),
        new(0b1011, 4, 4), new(0b1100, 4, 5), new(0b1110, 4, 6), new(0b1111, 4, 7),
        new(0b10011, 5, 8), new(0b10100, 5, 9), new(0b00111, 5, 10), new(0b01000, 5, 11),
        new(0b001000, 6, 12), new(0b000011, 6, 13), new(0b110100, 6, 14), new(0b110101, 6, 15),
        new(0b101010, 6, 16), new(0b101011, 6, 17), new(0b0100111, 7, 18), new(0b0001100, 7, 19),
        new(0b0001000, 7, 20), new(0b0010111, 7, 21), new(0b0000011, 7, 22), new(0b0000100, 7, 23),
        new(0b0101000, 7, 24), new(0b0101011, 7, 25), new(0b0010011, 7, 26), new(0b0100100, 7, 27),
        new(0b0011000, 7, 28), new(0b00000010, 8, 29), new(0b00000011, 8, 30), new(0b00011010, 8, 31),
        new(0b00011011, 8, 32), new(0b00010010, 8, 33), new(0b00010011, 8, 34), new(0b00010100, 8, 35),
        new(0b00010101, 8, 36), new(0b00010110, 8, 37), new(0b00010111, 8, 38), new(0b00101000, 8, 39),
        new(0b00101001, 8, 40), new(0b00101010, 8, 41), new(0b00101011, 8, 42), new(0b00101100, 8, 43),
        new(0b00101101, 8, 44), new(0b00000100, 8, 45), new(0b00000101, 8, 46), new(0b00001010, 8, 47),
        new(0b00001011, 8, 48), new(0b01010010, 8, 49), new(0b01010011, 8, 50), new(0b01010100, 8, 51),
        new(0b01010101, 8, 52), new(0b00100100, 8, 53), new(0b00100101, 8, 54), new(0b01011000, 8, 55),
        new(0b01011001, 8, 56), new(0b01011010, 8, 57), new(0b01011011, 8, 58), new(0b01001010, 8, 59),
        new(0b01001011, 8, 60), new(0b00110010, 8, 61), new(0b00110011, 8, 62), new(0b00110100, 8, 63),

        // And the ones that stand for a multiple of sixty-four.
        new(0b11011, 5, 64), new(0b10010, 5, 128), new(0b010111, 6, 192), new(0b0110111, 7, 256),
        new(0b00110110, 8, 320), new(0b00110111, 8, 384), new(0b01100100, 8, 448), new(0b01100101, 8, 512),
        new(0b01101000, 8, 576), new(0b01100111, 8, 640), new(0b011001100, 9, 704), new(0b011001101, 9, 768),
        new(0b011010010, 9, 832), new(0b011010011, 9, 896), new(0b011010100, 9, 960), new(0b011010101, 9, 1024),
        new(0b011010110, 9, 1088), new(0b011010111, 9, 1152), new(0b011011000, 9, 1216), new(0b011011001, 9, 1280),
        new(0b011011010, 9, 1344), new(0b011011011, 9, 1408), new(0b010011000, 9, 1472), new(0b010011001, 9, 1536),
        new(0b010011010, 9, 1600), new(0b011000, 6, 1664), new(0b010011011, 9, 1728)
    ];

    internal static readonly Code[] Black =
    [
        new(0b0000110111, 10, 0), new(0b010, 3, 1), new(0b11, 2, 2), new(0b10, 2, 3),
        new(0b011, 3, 4), new(0b0011, 4, 5), new(0b0010, 4, 6), new(0b00011, 5, 7),
        new(0b000101, 6, 8), new(0b000100, 6, 9), new(0b0000100, 7, 10), new(0b0000101, 7, 11),
        new(0b0000111, 7, 12), new(0b00000100, 8, 13), new(0b00000111, 8, 14), new(0b000011000, 9, 15),
        new(0b0000010111, 10, 16), new(0b0000011000, 10, 17), new(0b0000001000, 10, 18),
        new(0b00001100111, 11, 19), new(0b00001101000, 11, 20), new(0b00001101100, 11, 21),
        new(0b00000110111, 11, 22), new(0b00000101000, 11, 23), new(0b00000010111, 11, 24),
        new(0b00000011000, 11, 25), new(0b000011001010, 12, 26), new(0b000011001011, 12, 27),
        new(0b000011001100, 12, 28), new(0b000011001101, 12, 29), new(0b000001101000, 12, 30),
        new(0b000001101001, 12, 31), new(0b000001101010, 12, 32), new(0b000001101011, 12, 33),
        new(0b000011010010, 12, 34), new(0b000011010011, 12, 35), new(0b000011010100, 12, 36),
        new(0b000011010101, 12, 37), new(0b000011010110, 12, 38), new(0b000011010111, 12, 39),
        new(0b000001101100, 12, 40), new(0b000001101101, 12, 41), new(0b000011011010, 12, 42),
        new(0b000011011011, 12, 43), new(0b000001010100, 12, 44), new(0b000001010101, 12, 45),
        new(0b000001010110, 12, 46), new(0b000001010111, 12, 47), new(0b000001100100, 12, 48),
        new(0b000001100101, 12, 49), new(0b000001010010, 12, 50), new(0b000001010011, 12, 51),
        new(0b000000100100, 12, 52), new(0b000000110111, 12, 53), new(0b000000111000, 12, 54),
        new(0b000000100111, 12, 55), new(0b000000101000, 12, 56), new(0b000001011000, 12, 57),
        new(0b000001011001, 12, 58), new(0b000000101011, 12, 59), new(0b000000101100, 12, 60),
        new(0b000001011010, 12, 61), new(0b000001100110, 12, 62), new(0b000001100111, 12, 63),

        new(0b0000001111, 10, 64), new(0b000011001000, 12, 128), new(0b000011001001, 12, 192),
        new(0b000001011011, 12, 256), new(0b000000110011, 12, 320), new(0b000000110100, 12, 384),
        new(0b000000110101, 12, 448), new(0b0000001101100, 13, 512), new(0b0000001101101, 13, 576),
        new(0b0000001001010, 13, 640), new(0b0000001001011, 13, 704), new(0b0000001001100, 13, 768),
        new(0b0000001001101, 13, 832), new(0b0000001110010, 13, 896), new(0b0000001110011, 13, 960),
        new(0b0000001110100, 13, 1024), new(0b0000001110101, 13, 1088), new(0b0000001110110, 13, 1152),
        new(0b0000001110111, 13, 1216), new(0b0000001010010, 13, 1280), new(0b0000001010011, 13, 1344),
        new(0b0000001010100, 13, 1408), new(0b0000001010101, 13, 1472), new(0b0000001011010, 13, 1536),
        new(0b0000001011011, 13, 1600), new(0b0000001100100, 13, 1664), new(0b0000001100101, 13, 1728)
    ];

    /// <summary>The long runs, which are written the same way whatever colour they are.</summary>
    internal static readonly Code[] Extended =
    [
        new(0b00000001000, 11, 1792), new(0b00000001100, 11, 1856), new(0b00000001101, 11, 1920),
        new(0b000000010010, 12, 1984), new(0b000000010011, 12, 2048), new(0b000000010100, 12, 2112),
        new(0b000000010101, 12, 2176), new(0b000000010110, 12, 2240), new(0b000000010111, 12, 2304),
        new(0b000000011100, 12, 2368), new(0b000000011101, 12, 2432), new(0b000000011110, 12, 2496),
        new(0b000000011111, 12, 2560)
    ];

    /// <summary>The code that ends a line, which a fax may put before every one of them.</summary>
    internal const int EndOfLine = 0b000000000001;

    internal const int EndOfLineLength = 12;
}
