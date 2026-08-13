namespace n8PDF.Images;

/// <summary>
/// Decodes a baseline JPEG into samples.
/// </summary>
/// <remarks>
/// A JPEG normally passes through this converter untouched, because a PDF carries one as the file
/// it already is: decoding and re-encoding would cost quality for nothing. This exists for the one
/// case where that cannot be done — a TIFF that holds its picture as several JPEGs, one to a strip.
/// They are separate files, and the only way to make one picture of them is to decode each and lay
/// them one above another.
///
/// What a JPEG holds is not the picture but a description of it: each block of eight by eight
/// pixels is written as how much of each of sixty-four waves it is made of, the finer waves
/// divided by larger numbers before rounding so that what is lost is what the eye minds least.
/// Reading one is therefore that in reverse — unpack the numbers, multiply them back up, turn the
/// waves into pixels — with the colours kept apart from the brightness and often at half the
/// detail, since the eye minds that less as well.
///
/// Baseline only: the sequential encoding every camera and scanner writes. Progressive JPEGs, the
/// arithmetic coding almost nothing uses, and the four-channel files a printer makes are not
/// handled, and are reported rather than half-read.
/// </remarks>
internal static class JpegDecoder
{
    public static ImageData Decode(byte[] data)
    {
        var jpeg = new Reader(data);

        return jpeg.Run();
    }

    private sealed class Component
    {
        public int Id;
        public int HorizontalSampling = 1;
        public int VerticalSampling = 1;
        public int QuantizationTable;
        public int DcTable;
        public int AcTable;

        /// <summary>The samples of this channel, at whatever detail it was written in.</summary>
        public byte[] Samples = [];

        public int Width;
        public int Height;
        public int Predictor;
    }

    /// <summary>A Huffman table, as the codes of each length and what each stands for.</summary>
    private sealed class HuffmanTable
    {
        private readonly int[] _minimum = new int[17];
        private readonly int[] _maximum = new int[17];
        private readonly int[] _first = new int[17];
        private readonly byte[] _values;

        public HuffmanTable(ReadOnlySpan<byte> counts, byte[] values)
        {
            _values = values;

            var code = 0;
            var at = 0;

            for (var length = 1; length <= 16; length++)
            {
                _first[length] = at;
                _minimum[length] = code;

                code += counts[length - 1];
                at += counts[length - 1];

                _maximum[length] = counts[length - 1] == 0 ? -1 : code - 1;
                code <<= 1;
            }
        }

        /// <summary>Reads one code, a bit at a time until it is one this table knows.</summary>
        public int Read(ref BitReader bits)
        {
            var code = 0;

            for (var length = 1; length <= 16; length++)
            {
                code = (code << 1) | bits.Bit();

                if (_maximum[length] < 0 || code > _maximum[length]) continue;

                var index = _first[length] + code - _minimum[length];

                return index < _values.Length ? _values[index] : 0;
            }

            return 0;
        }
    }

    private sealed class Reader(byte[] data)
    {
        private readonly List<Component> _components = [];
        private readonly int[][] _quantization = new int[4][];
        private readonly HuffmanTable?[] _dc = new HuffmanTable?[4];
        private readonly HuffmanTable?[] _ac = new HuffmanTable?[4];

        private int _width;
        private int _height;
        private int _restartInterval;

        public ImageData Run()
        {
            if (data.Length < 4 || data[0] != 0xff || data[1] != 0xd8)
                throw new ImageFormatException("Not a JPEG.");

            var at = 2;

            while (at + 1 < data.Length)
            {
                if (data[at] != 0xff)
                {
                    at++;
                    continue;
                }

                var marker = data[at + 1];
                at += 2;

                // The markers that stand alone carry nothing after them.
                if (marker is 0xd8 or 0x01 || marker is >= 0xd0 and <= 0xd7) continue;
                if (marker == 0xd9) break;

                if (at + 1 >= data.Length) break;

                var length = (data[at] << 8) | data[at + 1];
                var body = at + 2;
                var end = Math.Min(data.Length, at + length);

                switch (marker)
                {
                    case 0xdb:
                        ReadQuantization(body, end);
                        break;

                    case 0xc4:
                        ReadHuffman(body, end);
                        break;

                    case 0xc0:
                    case 0xc1:
                        ReadFrame(body, end);
                        break;

                    case 0xc2:
                        throw new ImageFormatException("Progressive JPEGs are not handled.");

                    case 0xc3:
                    case 0xc5:
                    case 0xc6:
                    case 0xc7:
                    case 0xc9:
                    case 0xca:
                    case 0xcb:
                    case 0xcd:
                    case 0xce:
                    case 0xcf:
                        throw new ImageFormatException("This kind of JPEG is not handled.");

                    case 0xdd:
                        _restartInterval = body + 1 < data.Length ? (data[body] << 8) | data[body + 1] : 0;
                        break;

                    case 0xda:
                        at = ReadScan(body, end);
                        continue;
                }

                at = end;
            }

            return Finish();
        }

        private void ReadQuantization(int at, int end)
        {
            while (at < end)
            {
                var precision = data[at] >> 4;
                var id = data[at] & 0x0f;
                at++;

                var table = new int[64];

                for (var i = 0; i < 64 && at < end; i++)
                {
                    table[i] = precision == 0 ? data[at] : (data[at] << 8) | data[at + 1];
                    at += precision == 0 ? 1 : 2;
                }

                if (id < _quantization.Length) _quantization[id] = table;
            }
        }

        private void ReadHuffman(int at, int end)
        {
            while (at + 16 < end)
            {
                var kind = data[at] >> 4;
                var id = data[at] & 0x0f;
                at++;

                var counts = data.AsSpan(at, 16);
                at += 16;

                var total = 0;
                foreach (var count in counts) total += count;

                var values = data[at..Math.Min(data.Length, at + total)];
                at += total;

                var table = new HuffmanTable(counts, values);

                if (id >= 4) continue;

                if (kind == 0) _dc[id] = table;
                else _ac[id] = table;
            }
        }

        private void ReadFrame(int at, int end)
        {
            _height = (data[at + 1] << 8) | data[at + 2];
            _width = (data[at + 3] << 8) | data[at + 4];

            var count = data[at + 5];
            at += 6;

            _components.Clear();

            for (var i = 0; i < count && at + 2 < end; i++, at += 3)
            {
                _components.Add(new Component
                {
                    Id = data[at],
                    HorizontalSampling = Math.Max(1, data[at + 1] >> 4),
                    VerticalSampling = Math.Max(1, data[at + 1] & 0x0f),
                    QuantizationTable = data[at + 2]
                });
            }

            if (_width <= 0 || _height <= 0) throw new ImageFormatException("JPEG declares an empty image.");

            if (_components.Count is not (1 or 3))
                throw new ImageFormatException($"A JPEG of {_components.Count} channels is not handled.");
        }

        /// <summary>Reads the scan, which is the picture itself.</summary>
        private int ReadScan(int at, int end)
        {
            var count = data[at];
            at++;

            for (var i = 0; i < count && at + 1 < end; i++, at += 2)
            {
                var component = _components.FirstOrDefault(c => c.Id == data[at]);
                if (component is null) continue;

                component.DcTable = data[at + 1] >> 4;
                component.AcTable = data[at + 1] & 0x0f;
            }

            // Three bytes of spectral selection, which a baseline scan does not use.
            at = end;

            var maxH = _components.Max(c => c.HorizontalSampling);
            var maxV = _components.Max(c => c.VerticalSampling);

            var mcusAcross = (_width + maxH * 8 - 1) / (maxH * 8);
            var mcusDown = (_height + maxV * 8 - 1) / (maxV * 8);

            foreach (var component in _components)
            {
                component.Width = mcusAcross * component.HorizontalSampling * 8;
                component.Height = mcusDown * component.VerticalSampling * 8;
                component.Samples = new byte[component.Width * component.Height];
                component.Predictor = 0;
            }

            var bits = new BitReader(data, at);
            var block = new int[64];
            var pixels = new byte[64];

            var untilRestart = _restartInterval;

            for (var mcuY = 0; mcuY < mcusDown; mcuY++)
            for (var mcuX = 0; mcuX < mcusAcross; mcuX++)
            {
                // A scan may be broken into runs that can each be read on their own, so that a
                // fault in one does not carry into the rest.
                if (_restartInterval > 0 && untilRestart == 0)
                {
                    bits.Restart();

                    foreach (var component in _components) component.Predictor = 0;

                    untilRestart = _restartInterval;
                }

                foreach (var component in _components)
                {
                    for (var v = 0; v < component.VerticalSampling; v++)
                    for (var h = 0; h < component.HorizontalSampling; h++)
                    {
                        ReadBlock(ref bits, component, block);
                        Idct(block, pixels);

                        var x = (mcuX * component.HorizontalSampling + h) * 8;
                        var y = (mcuY * component.VerticalSampling + v) * 8;

                        for (var row = 0; row < 8; row++)
                        {
                            var target = (y + row) * component.Width + x;
                            if (target + 8 > component.Samples.Length) break;

                            Array.Copy(pixels, row * 8, component.Samples, target, 8);
                        }
                    }
                }

                untilRestart--;
            }

            return bits.Position;
        }

        /// <summary>
        /// One block: a difference from the block before for the flat part of it, then runs of
        /// nothing and the waves between them, in the order that puts the coarsest first.
        /// </summary>
        private void ReadBlock(ref BitReader bits, Component component, int[] block)
        {
            Array.Clear(block);

            var quantization = _quantization[component.QuantizationTable & 3] ?? Default;

            var dc = _dc[component.DcTable & 3];
            var ac = _ac[component.AcTable & 3];

            if (dc is null || ac is null) throw new ImageFormatException("JPEG scan names a table it has not.");

            var category = dc.Read(ref bits);
            var difference = category == 0 ? 0 : Extend(bits.Read(category), category);

            component.Predictor += difference;
            block[0] = component.Predictor * quantization[0];

            for (var i = 1; i < 64;)
            {
                var symbol = ac.Read(ref bits);
                var run = symbol >> 4;
                var size = symbol & 0x0f;

                if (size == 0)
                {
                    // Sixteen of nothing, or nothing at all to the end of the block.
                    if (run != 15) break;

                    i += 16;
                    continue;
                }

                i += run;
                if (i > 63) break;

                block[Zigzag[i]] = Extend(bits.Read(size), size) * quantization[i];
                i++;
            }
        }

        private ImageData Finish()
        {
            if (_components.Count == 0 || _components[0].Samples.Length == 0)
                throw new ImageFormatException("JPEG holds no scan.");

            var grey = _components.Count == 1;
            var pixels = new byte[_width * _height * (grey ? 1 : 3)];

            var maxH = _components.Max(c => c.HorizontalSampling);
            var maxV = _components.Max(c => c.VerticalSampling);

            for (var y = 0; y < _height; y++)
            for (var x = 0; x < _width; x++)
            {
                if (grey)
                {
                    pixels[y * _width + x] = At(_components[0], x, y, maxH, maxV);
                    continue;
                }

                // The colours are kept apart from the brightness, and often at half the detail:
                // a channel written smaller is read by taking the sample the pixel falls into.
                var luma = At(_components[0], x, y, maxH, maxV);
                var blue = At(_components[1], x, y, maxH, maxV) - 128;
                var red = At(_components[2], x, y, maxH, maxV) - 128;

                var at = (y * _width + x) * 3;

                pixels[at] = Clamp(luma + 1.402 * red);
                pixels[at + 1] = Clamp(luma - 0.344136 * blue - 0.714136 * red);
                pixels[at + 2] = Clamp(luma + 1.772 * blue);
            }

            return new ImageData(_width, _height, pixels, ImageEncoding.Raw,
                grey ? ImageColorSpace.Gray : ImageColorSpace.Rgb);
        }

        /// <summary>The sample of a channel that a pixel of the picture falls into.</summary>
        private static byte At(Component component, int x, int y, int maxH, int maxV)
        {
            var sx = x * component.HorizontalSampling / maxH;
            var sy = y * component.VerticalSampling / maxV;

            var at = sy * component.Width + sx;

            return at >= 0 && at < component.Samples.Length ? component.Samples[at] : (byte)0;
        }

        private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

        /// <summary>
        /// A number of the given size, where the size says how many bits it takes and the top bit
        /// says which side of nought it is.
        /// </summary>
        private static int Extend(int value, int size) =>
            size == 0 ? 0 : value < 1 << (size - 1) ? value - (1 << size) + 1 : value;

        private static readonly int[] Default = Enumerable.Repeat(1, 64).ToArray();
    }

    /// <summary>
    /// The order the sixty-four waves of a block are written in: out from the corner in diagonals,
    /// so that the coarsest come first and the run of nothing at the end is as long as possible.
    /// </summary>
    private static readonly int[] Zigzag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63
    ];

    private static readonly double[] Cosines = BuildCosines();

    private static double[] BuildCosines()
    {
        var table = new double[64];

        for (var x = 0; x < 8; x++)
        for (var u = 0; u < 8; u++)
            table[x * 8 + u] = Math.Cos((2 * x + 1) * u * Math.PI / 16) * (u == 0 ? Math.Sqrt(0.5) : 1);

        return table;
    }

    /// <summary>
    /// Turns a block of waves back into pixels: the same sum along the rows and then down the
    /// columns, which is what makes it sixty-four multiplications a row rather than four thousand
    /// a block.
    /// </summary>
    private static void Idct(int[] block, byte[] pixels)
    {
        Span<double> rows = stackalloc double[64];

        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                var sum = 0.0;

                for (var u = 0; u < 8; u++) sum += Cosines[x * 8 + u] * block[y * 8 + u];

                rows[y * 8 + x] = sum / 2;
            }
        }

        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < 8; y++)
            {
                var sum = 0.0;

                for (var v = 0; v < 8; v++) sum += Cosines[y * 8 + v] * rows[v * 8 + x];

                // The samples were written as differences from the middle of the range.
                pixels[y * 8 + x] = (byte)Math.Clamp(Math.Round(sum / 2) + 128, 0, 255);
            }
        }
    }

    /// <summary>
    /// Reads the bits of a scan, biggest first, stepping over the way a scan writes a byte that
    /// would otherwise look like a marker.
    /// </summary>
    private ref struct BitReader(byte[] data, int at)
    {
        private int _at = at;
        private int _bits;
        private int _count;

        public readonly int Position => _at;

        public int Bit()
        {
            if (_count == 0)
            {
                if (_at >= data.Length) return 0;

                var value = data[_at++];

                // A 0xff inside a scan is written with a nought after it, so that it cannot be
                // mistaken for a marker; the nought is not part of the picture.
                if (value == 0xff)
                {
                    if (_at < data.Length && data[_at] == 0x00) _at++;
                    else if (_at < data.Length && data[_at] >= 0xd0 && data[_at] <= 0xd7)
                    {
                        // A restart marker, which the reader steps over when it is told to.
                    }
                }

                _bits = value;
                _count = 8;
            }

            _count--;

            return (_bits >> _count) & 1;
        }

        public int Read(int length)
        {
            var value = 0;

            for (var i = 0; i < length; i++) value = (value << 1) | Bit();

            return value;
        }

        /// <summary>Steps to the next run of the scan, which begins on a byte after its marker.</summary>
        public void Restart()
        {
            _count = 0;

            while (_at + 1 < data.Length)
            {
                if (data[_at] == 0xff && data[_at + 1] >= 0xd0 && data[_at + 1] <= 0xd7)
                {
                    _at += 2;
                    return;
                }

                _at++;
            }
        }
    }
}
