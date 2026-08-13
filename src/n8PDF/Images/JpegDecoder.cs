namespace n8PDF.Images;

/// <summary>
/// Decodes a JPEG into samples.
/// </summary>
/// <remarks>
/// A JPEG normally passes through this converter untouched, because a PDF carries one as the file
/// it already is: decoding and re-encoding would cost quality for nothing. This exists for the one
/// case where that cannot be done — a TIFF that holds its picture as several JPEGs, one to a strip.
/// They are separate files, and the only way to make one picture of them is to decode each and lay
/// them one above another.
///
/// What a JPEG holds is not the picture but a description of it: each block of eight by eight
/// pixels is written as how much of each of sixty-four waves it is made of, the finer waves divided
/// by larger numbers before rounding so that what is lost is what the eye minds least. Reading one
/// is therefore that in reverse — unpack the numbers, multiply them back up, turn the waves into
/// pixels — with the colours kept apart from the brightness and often at half the detail, since the
/// eye minds that less as well.
///
/// The numbers may be written all at once or a little at a time. A sequential file gives every wave
/// of a block before moving to the next; a progressive one gives the coarsest waves of the whole
/// picture first and returns for the rest, so that something recognisable appears before all of it
/// has arrived — and may even send the high bits of a number in one pass and its low bits in
/// another. So the numbers are gathered first and turned into pixels only when the last pass has
/// been read, which is the one structural difference between the two.
/// </remarks>
internal static class JpegDecoder
{
    public static ImageData Decode(byte[] data) => new Reader(data).Run();

    private sealed class Component
    {
        public int Id;
        public int HorizontalSampling = 1;
        public int VerticalSampling = 1;
        public int QuantizationTable;
        public int DcTable;
        public int AcTable;

        /// <summary>The blocks of this channel across the whole picture, as they were written.</summary>
        public int[] Coefficients = [];

        public int BlocksPerLine;
        public int BlocksPerColumn;

        /// <summary>How many blocks hold picture rather than the padding out to whole blocks.</summary>
        public int RealBlocksPerLine;

        public int RealBlocksPerColumn;

        public int Predictor;

        /// <summary>
        /// What the last difference of this channel came to, in the round terms the arithmetic
        /// coder keeps: whether it was nothing, small or large, and which way.
        /// </summary>
        public int DifferenceContext;

        public byte[] Samples = [];
        public int Width;
        public int Height;
    }

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
        public int Read(BitReader bits)
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
        private readonly int[]?[] _quantization = new int[4][];
        private readonly HuffmanTable?[] _dc = new HuffmanTable?[4];
        private readonly HuffmanTable?[] _ac = new HuffmanTable?[4];

        private int _width;
        private int _height;
        private int _restartInterval;
        private bool _progressive;
        private bool _arithmetic;

        // What the arithmetic coder has learned so far, kept apart for each table the scan names,
        // and the conditioning the file asks for.
        private readonly byte[][] _dcStatistics = [new byte[64], new byte[64], new byte[64], new byte[64]];
        private readonly byte[][] _acStatistics = [new byte[256], new byte[256], new byte[256], new byte[256]];
        // The context the coder never learns from, which is what a sign is weighed against.
        private readonly byte[] _fixed = [JpegArithmetic.Fixed];
        private readonly int[] _lowerBound = [0, 0, 0, 0];
        private readonly int[] _upperBound = [1, 1, 1, 1];
        private readonly int[] _blockBound = [5, 5, 5, 5];
        private JpegArithmetic? _arithmeticReader;
        private int _mcusAcross;
        private int _mcusDown;

        // What a scan carries over from one block to the next: how many blocks are known to be
        // nothing but what has already been sent.
        private int _endOfBandRun;

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
                        ReadFrame(body, end, progressive: false, arithmetic: false);
                        break;

                    case 0xc2:
                        ReadFrame(body, end, progressive: true, arithmetic: false);
                        break;

                    // The same two again, coded arithmetically rather than with tables.
                    case 0xc9:
                        ReadFrame(body, end, progressive: false, arithmetic: true);
                        break;

                    case 0xca:
                        ReadFrame(body, end, progressive: true, arithmetic: true);
                        break;

                    case 0xcc:
                        ReadConditioning(body, end);
                        break;

                    case 0xc3:
                    case 0xc5:
                    case 0xc6:
                    case 0xc7:
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

                if (id >= 4) continue;

                var table = new HuffmanTable(counts, values);

                if (kind == 0) _dc[id] = table;
                else _ac[id] = table;
            }
        }

        /// <summary>
        /// What the arithmetic coder is to assume before it has seen anything: which differences
        /// count as small, and how far into a block the finer waves begin.
        /// </summary>
        private void ReadConditioning(int at, int end)
        {
            while (at + 1 < end)
            {
                var kind = data[at] >> 4;
                var id = data[at] & 3;
                var value = data[at + 1];

                if (kind == 0)
                {
                    _lowerBound[id] = value & 0x0f;
                    _upperBound[id] = value >> 4;
                }
                else
                {
                    _blockBound[id] = value;
                }

                at += 2;
            }
        }

        private void ReadFrame(int at, int end, bool progressive, bool arithmetic)
        {
            _progressive = progressive;
            _arithmetic = arithmetic;

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

            var maxH = _components.Max(c => c.HorizontalSampling);
            var maxV = _components.Max(c => c.VerticalSampling);

            _mcusAcross = (_width + maxH * 8 - 1) / (maxH * 8);
            _mcusDown = (_height + maxV * 8 - 1) / (maxV * 8);

            foreach (var component in _components)
            {
                component.BlocksPerLine = _mcusAcross * component.HorizontalSampling;
                component.BlocksPerColumn = _mcusDown * component.VerticalSampling;

                // A scan of one channel walks the blocks the picture really has rather than the
                // ones it was padded out to, and the two differ wherever the picture does not
                // divide evenly.
                var channelWidth = (_width * component.HorizontalSampling + maxH - 1) / maxH;
                var channelHeight = (_height * component.VerticalSampling + maxV - 1) / maxV;

                component.RealBlocksPerLine = (channelWidth + 7) / 8;
                component.RealBlocksPerColumn = (channelHeight + 7) / 8;

                component.Width = component.BlocksPerLine * 8;
                component.Height = component.BlocksPerColumn * 8;
                component.Coefficients = new int[component.BlocksPerLine * component.BlocksPerColumn * 64];
            }
        }

        /// <summary>Reads one scan, which may be all of the picture or one pass over part of it.</summary>
        private int ReadScan(int at, int end)
        {
            var count = data[at];
            at++;

            var scan = new List<Component>();

            for (var i = 0; i < count && at + 1 < end; i++, at += 2)
            {
                var component = _components.FirstOrDefault(c => c.Id == data[at]);
                if (component is null) continue;

                component.DcTable = data[at + 1] >> 4;
                component.AcTable = data[at + 1] & 0x0f;

                scan.Add(component);
            }

            // Which of the sixty-four waves this pass carries, and which bits of them.
            var from = at < end ? data[at] : 0;
            var to = at + 1 < end ? data[at + 1] : 63;
            var previous = at + 2 < end ? data[at + 2] >> 4 : 0;
            var point = at + 2 < end ? data[at + 2] & 0x0f : 0;

            if (!_progressive)
            {
                from = 0;
                to = 63;
                previous = 0;
                point = 0;
            }

            var bits = new BitReader(data, end);

            if (_arithmetic)
            {
                _arithmeticReader = new JpegArithmetic(data, end);

                // Every scan begins knowing nothing, whatever the one before it learned.
                foreach (var statistics in _dcStatistics) Array.Clear(statistics);
                foreach (var statistics in _acStatistics) Array.Clear(statistics);

                _fixed[0] = JpegArithmetic.Fixed;
            }

            foreach (var component in scan)
            {
                component.Predictor = 0;
                component.DifferenceContext = 0;
            }

            _endOfBandRun = 0;

            var untilRestart = _restartInterval;

            void Restart()
            {
                if (_restartInterval <= 0 || untilRestart > 0) return;

                if (_arithmetic)
                {
                    _arithmeticReader!.Restart();

                    foreach (var statistics in _dcStatistics) Array.Clear(statistics);
                    foreach (var statistics in _acStatistics) Array.Clear(statistics);

                    _fixed[0] = JpegArithmetic.Fixed;
                }
                else
                {
                    bits.Restart();
                }

                foreach (var component in scan)
                {
                    component.Predictor = 0;
                    component.DifferenceContext = 0;
                }

                _endOfBandRun = 0;
                untilRestart = _restartInterval;
            }

            if (scan.Count == 1)
            {
                // One channel at a time: its own blocks, in reading order, and no padding.
                var component = scan[0];

                for (var row = 0; row < component.RealBlocksPerColumn; row++)
                for (var column = 0; column < component.RealBlocksPerLine; column++)
                {
                    Restart();

                    ReadBlock(bits, component, (row * component.BlocksPerLine + column) * 64,
                        from, to, previous, point);

                    untilRestart--;
                }
            }
            else
            {
                for (var mcuY = 0; mcuY < _mcusDown; mcuY++)
                for (var mcuX = 0; mcuX < _mcusAcross; mcuX++)
                {
                    Restart();

                    foreach (var component in scan)
                    {
                        for (var v = 0; v < component.VerticalSampling; v++)
                        for (var h = 0; h < component.HorizontalSampling; h++)
                        {
                            var row = mcuY * component.VerticalSampling + v;
                            var column = mcuX * component.HorizontalSampling + h;

                            ReadBlock(bits, component, (row * component.BlocksPerLine + column) * 64,
                                from, to, previous, point);
                        }
                    }

                    untilRestart--;
                }
            }

            return _arithmetic ? _arithmeticReader!.Position : bits.Position;
        }

        /// <summary>
        /// One block of one pass. A sequential file gives the whole of it at once; a progressive
        /// one gives the flat part and the waves in separate passes, and may give the high bits of
        /// a number before its low ones.
        /// </summary>
        private void ReadBlock(
            BitReader bits, Component component, int block, int from, int to, int previous, int point)
        {
            var coefficients = component.Coefficients;
            if (block + 63 >= coefficients.Length) return;

            if (_arithmetic)
            {
                ReadArithmeticBlock(component, coefficients, block, from, to, previous, point);
                return;
            }

            if (from == 0)
            {
                if (previous == 0) ReadDc(bits, component, coefficients, block, point);
                else if (bits.Bit() == 1) coefficients[block] |= 1 << point;

                if (!_progressive) ReadAc(bits, component, coefficients, block, 1, 63, 0);

                return;
            }

            if (previous == 0) ReadAc(bits, component, coefficients, block, from, to, point);
            else RefineAc(bits, component, coefficients, block, from, to, point);
        }

        /// <summary>
        /// One block read arithmetically. Every number is a run of yes-or-no decisions, each
        /// weighed against what the picture has already been seen to do — and each context has its
        /// own statistics, so a decision about a coarse wave learns nothing from a fine one.
        /// </summary>
        private void ReadArithmeticBlock(
            Component component, int[] coefficients, int block, int from, int to, int previous, int point)
        {
            var reader = _arithmeticReader!;

            if (from == 0)
            {
                if (previous == 0) ReadArithmeticDc(reader, component, coefficients, block, point);
                else if (reader.Decode(_fixed, 0) == 1) coefficients[block] |= 1 << point;

                if (!_progressive) ReadArithmeticAc(reader, component, coefficients, block, 1, 63, 0);

                return;
            }

            if (previous == 0) ReadArithmeticAc(reader, component, coefficients, block, from, to, point);
            else RefineArithmeticAc(reader, component, coefficients, block, from, to, point);
        }

        /// <summary>
        /// The flat part of a block: whether it differs from the block before at all, then which
        /// way, then how large, then the number itself — each decision weighed against how the
        /// last difference of this channel turned out.
        /// </summary>
        private void ReadArithmeticDc(
            JpegArithmetic reader, Component component, int[] coefficients, int block, int point)
        {
            var table = component.DcTable & 3;
            var statistics = _dcStatistics[table];
            var context = component.DifferenceContext;

            if (reader.Decode(statistics, context) == 0)
            {
                component.DifferenceContext = 0;
                coefficients[block] = component.Predictor << point;

                return;
            }

            var negative = reader.Decode(statistics, context + 1);
            var at = context + 2 + negative;

            var magnitude = reader.Decode(statistics, at);

            if (magnitude != 0)
            {
                // How many bits the number takes, counted up one at a time.
                at = 20;

                while (reader.Decode(statistics, at) == 1)
                {
                    magnitude <<= 1;

                    if (magnitude == 0x8000) throw new ImageFormatException("JPEG holds a number too large.");

                    at++;
                }
            }

            // Which of the three sizes this difference counts as, for the block after it.
            component.DifferenceContext = magnitude < (1 << _lowerBound[table]) >> 1
                ? 0
                : magnitude > (1 << _upperBound[table]) >> 1
                    ? 12 + negative * 4
                    : 4 + negative * 4;

            var value = magnitude;

            at += 14;

            while ((magnitude >>= 1) != 0)
            {
                if (reader.Decode(statistics, at) == 1) value |= magnitude;
            }

            value++;
            if (negative != 0) value = -value;

            component.Predictor += value;
            coefficients[block] = component.Predictor << point;
        }

        /// <summary>
        /// The waves of a block: at each place, whether the block ends here, then whether this
        /// place holds anything, and then the number if it does.
        /// </summary>
        private void ReadArithmeticAc(
            JpegArithmetic reader, Component component, int[] coefficients, int block, int from, int to,
            int point)
        {
            var table = component.AcTable & 3;
            var statistics = _acStatistics[table];

            for (var k = from; k <= to;)
            {
                var context = 3 * (k - 1);

                if (reader.Decode(statistics, context) == 1) break;

                while (reader.Decode(statistics, context + 1) == 0)
                {
                    context += 3;
                    k++;

                    if (k > to) return;
                }

                // A sign is the one decision nothing can be learned about, so it has a context to
                // itself that is never updated by anything else.
                var negative = reader.Decode(_fixed, 0);
                context += 2;

                var magnitude = reader.Decode(statistics, context);

                if (magnitude != 0)
                {
                    if (reader.Decode(statistics, context) == 1)
                    {
                        magnitude <<= 1;

                        // The finer waves of a block are counted apart from the coarser ones.
                        context = k <= _blockBound[table] ? 189 : 217;

                        while (reader.Decode(statistics, context) == 1)
                        {
                            magnitude <<= 1;

                            if (magnitude == 0x8000)
                                throw new ImageFormatException("JPEG holds a number too large.");

                            context++;
                        }
                    }
                }

                var value = magnitude;

                context += 14;

                while ((magnitude >>= 1) != 0)
                {
                    if (reader.Decode(statistics, context) == 1) value |= magnitude;
                }

                value++;
                if (negative != 0) value = -value;

                coefficients[block + Zigzag[k]] = value << point;

                k++;
            }
        }

        /// <summary>
        /// A pass that adds a bit to the waves already sent, which asks two things at each place:
        /// whether a number already there moves, and whether a place still empty fills.
        /// </summary>
        private void RefineArithmeticAc(
            JpegArithmetic reader, Component component, int[] coefficients, int block, int from, int to,
            int point)
        {
            var statistics = _acStatistics[component.AcTable & 3];

            var step = 1 << point;
            var negativeStep = -1 << point;

            // How far the passes before this one reached, past which a block may end.
            var reached = to;
            while (reached > 0 && coefficients[block + Zigzag[reached]] == 0) reached--;

            for (var k = from; k <= to; k++)
            {
                var context = 3 * (k - 1);

                if (k > reached && reader.Decode(statistics, context) == 1) break;

                while (true)
                {
                    var at = block + Zigzag[k];

                    if (coefficients[at] != 0)
                    {
                        if (reader.Decode(statistics, context + 2) == 1)
                            coefficients[at] += coefficients[at] < 0 ? negativeStep : step;

                        break;
                    }

                    if (reader.Decode(statistics, context + 1) == 1)
                    {
                        coefficients[at] = reader.Decode(_fixed, 0) == 1 ? negativeStep : step;
                        break;
                    }

                    context += 3;
                    k++;

                    if (k > to) return;
                }
            }
        }

        /// <summary>The flat part of a block, written as the difference from the block before it.</summary>
        private void ReadDc(BitReader bits, Component component, int[] coefficients, int block, int point)
        {
            var table = _dc[component.DcTable & 3] ??
                        throw new ImageFormatException("JPEG scan names a table it has not.");

            var category = table.Read(bits);
            var difference = category == 0 ? 0 : Extend(bits.Read(category), category);

            component.Predictor += difference;
            coefficients[block] = component.Predictor << point;
        }

        /// <summary>
        /// The waves of a block: runs of nothing and the numbers between them, ending where the
        /// rest of the block is nothing at all.
        /// </summary>
        private void ReadAc(
            BitReader bits, Component component, int[] coefficients, int block, int from, int to, int point)
        {
            if (_endOfBandRun > 0)
            {
                _endOfBandRun--;
                return;
            }

            var table = _ac[component.AcTable & 3] ??
                        throw new ImageFormatException("JPEG scan names a table it has not.");

            for (var k = from; k <= to;)
            {
                var symbol = table.Read(bits);
                var run = symbol >> 4;
                var size = symbol & 0x0f;

                if (size == 0)
                {
                    if (run < 15)
                    {
                        // The rest of this block, and of as many after it as the run says, is
                        // nothing more than has already been sent.
                        _endOfBandRun = (1 << run) - 1;

                        if (run > 0) _endOfBandRun += bits.Read(run);

                        break;
                    }

                    k += 16;
                    continue;
                }

                k += run;
                if (k > to) break;

                coefficients[block + Zigzag[k]] = Extend(bits.Read(size), size) << point;
                k++;
            }
        }

        /// <summary>
        /// A pass that adds a bit to numbers already sent. Every number the block already has gets
        /// one bit of correction, and the runs count only the places that are still nothing — which
        /// is what makes this the fiddliest reading in the format.
        /// </summary>
        private void RefineAc(
            BitReader bits, Component component, int[] coefficients, int block, int from, int to, int point)
        {
            var table = _ac[component.AcTable & 3] ??
                        throw new ImageFormatException("JPEG scan names a table it has not.");

            var step = 1 << point;
            var k = from;

            if (_endOfBandRun <= 0)
            {
                while (k <= to)
                {
                    var symbol = table.Read(bits);
                    var run = symbol >> 4;
                    var size = symbol & 0x0f;
                    var value = 0;

                    if (size == 0)
                    {
                        if (run < 15)
                        {
                            _endOfBandRun = (1 << run) - 1;

                            if (run > 0) _endOfBandRun += bits.Read(run);

                            break;
                        }
                    }
                    else
                    {
                        // A number arriving now can only be one step either side of nothing.
                        value = bits.Bit() == 1 ? step : -step;
                    }

                    while (k <= to)
                    {
                        var at = block + Zigzag[k];

                        if (coefficients[at] != 0)
                        {
                            // A number already there is corrected rather than replaced.
                            if (bits.Bit() == 1 && (coefficients[at] & step) == 0)
                                coefficients[at] += coefficients[at] >= 0 ? step : -step;
                        }
                        else
                        {
                            if (run == 0)
                            {
                                if (value != 0) coefficients[at] = value;

                                k++;
                                break;
                            }

                            run--;
                        }

                        k++;
                    }
                }
            }

            if (_endOfBandRun <= 0) return;

            // Even a block with nothing new in it has its numbers corrected.
            while (k <= to)
            {
                var at = block + Zigzag[k];

                if (coefficients[at] != 0 && bits.Bit() == 1 && (coefficients[at] & step) == 0)
                    coefficients[at] += coefficients[at] >= 0 ? step : -step;

                k++;
            }

            _endOfBandRun--;
        }

        /// <summary>
        /// Turns the numbers into pixels, once every pass over them has been read.
        /// </summary>
        private ImageData Finish()
        {
            if (_components.Count == 0 || _components[0].Coefficients.Length == 0)
                throw new ImageFormatException("JPEG holds no scan.");

            var block = new int[64];
            var pixels = new byte[64];

            foreach (var component in _components)
            {
                var quantization = _quantization[component.QuantizationTable & 3] ?? Flat;
                component.Samples = new byte[component.Width * component.Height];

                for (var row = 0; row < component.BlocksPerColumn; row++)
                for (var column = 0; column < component.BlocksPerLine; column++)
                {
                    var at = (row * component.BlocksPerLine + column) * 64;

                    for (var i = 0; i < 64; i++)
                        block[Zigzag[i]] = component.Coefficients[at + Zigzag[i]] * quantization[i];

                    Idct(block, pixels);

                    for (var y = 0; y < 8; y++)
                    {
                        var target = (row * 8 + y) * component.Width + column * 8;
                        if (target + 8 > component.Samples.Length) break;

                        Array.Copy(pixels, y * 8, component.Samples, target, 8);
                    }
                }
            }

            return Combine();
        }

        private ImageData Combine()
        {
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

        private static readonly int[] Flat = [.. Enumerable.Repeat(1, 64)];
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
    private sealed class BitReader(byte[] data, int at)
    {
        private int _at = at;
        private int _bits;
        private int _count;

        public int Position => _at;

        public int Bit()
        {
            if (_count == 0)
            {
                if (_at >= data.Length) return 0;

                var value = data[_at];

                // A marker ends the scan: a reader that ran on into one would read the next scan's
                // header as though it were picture.
                if (value == 0xff)
                {
                    var next = _at + 1 < data.Length ? data[_at + 1] : 0;

                    if (next != 0x00) return 0;

                    // A 0xff inside a scan is written with a nought after it, which is not part of
                    // the picture.
                    _at++;
                }

                _at++;
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
