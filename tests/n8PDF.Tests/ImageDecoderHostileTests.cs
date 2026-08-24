using System.IO.Compression;
using n8PDF.Images;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The image decoders against hostile input (tier 2: unbounded allocation and hangs). Each case
/// is a small crafted file that, before the bound, drove a decoder to gigabytes of allocation or
/// a spin; each is asserted at the decoder so the <see cref="ImageReader"/> net (#48) cannot
/// swallow the evidence, and returns cleanly rather than exhausting the machine.
/// </summary>
public class ImageDecoderHostileTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    private static void Timed(string what, Action act)
    {
        // A spin shows as time, not a throw, so the hang findings are held by a wall-clock bound.
        var thread = new Thread(() => { try { act(); } catch { /* a throw is the good case */ } });
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), $"{what} did not finish — it is not bounded");
    }

    // ----- GIF (#8) -----

    [Fact]
    public void Gif_frame_dimensions_are_bounded()
    {
        // GIF89a, 1x1 logical screen, then an image descriptor claiming 65535 x 65535.
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());
        gif.AddRange([1, 0, 1, 0, 0, 0, 0]);                 // screen: 1x1, no global table
        gif.Add(0x2C);                                        // image descriptor
        gif.AddRange([0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF]);  // left,top,width=FFFF,height=FFFF
        gif.Add(0x00);                                        // no local table, packed
        _output.WriteLine($"{gif.Count}-byte GIF declares a 65535x65535 frame");

        Assert.IsType<ImageFormatException>(Record.Exception(() => GifDecoder.Decode(gif.ToArray())));
    }

    // ----- JPEG (#40) -----

    [Fact]
    public void Jpeg_frame_dimensions_are_bounded()
    {
        // SOI, SOF0 with height=width=0xFFFF and one component, then EOI.
        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0B };
        jpeg.AddRange([0xFF, 0xFF, 0xFF, 0xFF]);   // height, width
        jpeg.Add(1);                                // one component
        jpeg.AddRange([1, 0x11, 0]);                // component spec
        jpeg.AddRange([0xFF, 0xD9]);                // EOI
        _output.WriteLine($"{jpeg.Count}-byte JPEG declares 65535x65535");

        Assert.IsType<ImageFormatException>(Record.Exception(() => JpegDecoder.Decode(jpeg.ToArray())));
    }

    // ----- EMF (#17) -----

    [Fact]
    public void Emf_record_size_near_int_max_does_not_spin()
    {
        // A valid EMF header, then a record declaring a size near int.MaxValue: the walk must
        // stop rather than let at+size wrap negative and move the cursor backwards.
        var emf = new List<byte>();
        void I32(int v) { emf.Add((byte)v); emf.Add((byte)(v >> 8)); emf.Add((byte)(v >> 16)); emf.Add((byte)(v >> 24)); }

        I32(1);            // EMR_HEADER type
        I32(88);           // header size
        for (var i = 0; i < 8; i++) I32(0);            // bounds + frame
        emf.AddRange([0x20, 0x45, 0x4D, 0x46]);        // " EMF"
        while (emf.Count < 88) emf.Add(0);
        // A second record with a hostile size.
        I32(2);            // some record type
        I32(0x7FFFFFFF);   // size near int.MaxValue
        while (emf.Count < 200) emf.Add(0);

        Timed("EMF hostile record", () => ImageReader.TryRead(emf.ToArray()));
    }

    // ----- TIFF: a minimal builder -----

    private sealed class TiffBuilder
    {
        private readonly List<(int Id, int Type, int Count, int Value)> _tags = [];
        private byte[] _blob = [];

        public TiffBuilder Tag(int id, int type, int count, int value)
        {
            _tags.Add((id, type, count, value));
            return this;
        }

        /// <summary>Appends a data blob after the IFD and returns the offset to reference it by.</summary>
        public int Blob(byte[] data)
        {
            // The IFD sits at offset 8: header (2+2+4) then count (2) + entries (12 each) + next (4).
            var ifdEnd = 8 + 2 + _tagsSoFar() * 12 + 4;
            _blob = data;
            _blobOffset = ifdEnd;
            return ifdEnd;
        }

        private int _blobOffset;
        private int _plannedTags = -1;
        private int _tagsSoFar() => _plannedTags >= 0 ? _plannedTags : _tags.Count;

        /// <summary>Fixes how many tags the IFD will hold, so a blob offset can be computed early.</summary>
        public TiffBuilder Tags(int count) { _plannedTags = count; return this; }

        public byte[] Build()
        {
            var b = new List<byte>();
            void U16(int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
            void U32(int v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }

            b.AddRange([(byte)'I', (byte)'I']);
            U16(42);
            U32(8);                              // IFD at offset 8
            _tags.Sort((x, y) => x.Id.CompareTo(y.Id));
            U16(_tags.Count);
            foreach (var (id, type, count, value) in _tags)
            {
                U16(id); U16(type); U32(count); U32(value);
            }
            U32(0);                              // no next IFD
            if (_blob.Length > 0)
            {
                while (b.Count < _blobOffset) b.Add(0);
                b.AddRange(_blob);
            }
            return b.ToArray();
        }
    }

    private static byte[] Zlib(byte[] raw)
    {
        using var m = new MemoryStream();
        using (var z = new ZLibStream(m, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw, 0, raw.Length);
        return m.ToArray();
    }

    [Fact]
    public void Tiff_samples_per_pixel_is_bounded()   // #29
    {
        var tiff = new TiffBuilder()
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 8)      // 8x8
            .Tag(258, 3, 1, 8)                         // bits
            .Tag(259, 3, 1, 1)                         // no compression
            .Tag(277, 3, 1, 40000)                     // SamplesPerPixel — absurd
            .Tag(273, 4, 1, 8).Tag(279, 4, 1, 8)
            .Build();
        _output.WriteLine($"{tiff.Length}-byte TIFF declares 40000 samples a pixel");

        Assert.IsType<ImageFormatException>(Record.Exception(() => TiffDecoder.Decode(tiff)));
    }

    [Fact]
    public void Tiff_tag_value_count_does_not_allocate_from_the_file_s_word()   // #27
    {
        // A BitsPerSample tag claiming ~2 billion values in a tiny file must not pre-size a list
        // from that count. Decode fails cleanly (or draws nothing), but does not OOM or hang.
        var tiff = new TiffBuilder()
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 8)
            .Tag(258, 3, 0x7FFFFFFF, 100)              // BitsPerSample: 2-billion count, offset 100
            .Tag(259, 3, 1, 1).Tag(277, 3, 1, 1)
            .Tag(273, 4, 1, 8).Tag(279, 4, 1, 8)
            .Build();
        _output.WriteLine($"{tiff.Length}-byte TIFF declares a 2-billion-value tag");

        Timed("TIFF huge tag count", () => ImageReader.TryRead(tiff));
    }

    [Fact]
    public void Tiff_deflate_strip_is_bounded_against_its_rows()   // #30
    {
        var bomb = Zlib(new byte[16 * 1024 * 1024]);   // 16 MB of zeros, a few KB compressed
        var b = new TiffBuilder().Tags(8);
        var stripAt = b.Blob(bomb);
        var tiff = b
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 8)       // 8x8
            .Tag(258, 3, 1, 8).Tag(277, 3, 1, 1)
            .Tag(259, 3, 1, 8)                          // Deflate
            .Tag(273, 4, 1, stripAt).Tag(279, 4, 1, bomb.Length)
            .Build();
        _output.WriteLine($"{tiff.Length}-byte TIFF, an 8x8 image with a 16 MB Deflate strip");

        Timed("TIFF deflate bomb", () => ImageReader.TryRead(tiff));
    }

    [Fact]
    public void Ccitt_zero_run_strip_does_not_spin()   // #46
    {
        // An 8x1 fax image whose Modified-Huffman strip is nothing but white run-length-zero
        // codes (0b00110101): a0 never advances, so without the per-line transition bound one
        // scanline consumes the whole strip forever.
        var strip = new byte[256 * 1024];
        Array.Fill(strip, (byte)0x35);
        var b = new TiffBuilder().Tags(8);
        var stripAt = b.Blob(strip);
        var tiff = b
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 1)       // 8x1
            .Tag(258, 3, 1, 1).Tag(277, 3, 1, 1)
            .Tag(259, 3, 1, 2)                          // Modified Huffman (CCITT RLE)
            .Tag(273, 4, 1, stripAt).Tag(279, 4, 1, strip.Length)
            .Build();
        _output.WriteLine($"{tiff.Length}-byte TIFF, an 8x1 fax of {strip.Length} zero-run bytes");

        Timed("CCITT zero-run strip", () => ImageReader.TryRead(tiff));
    }

    [Fact]
    public void Gif_truncated_after_the_descriptor_marker_fails_cleanly()   // #9
    {
        // GIF89a, a 1x1 screen, then the 0x2C image-descriptor marker and nothing after it.
        var gif = new List<byte>();
        gif.AddRange("GIF89a"u8.ToArray());
        gif.AddRange([1, 0, 1, 0, 0, 0, 0]);
        gif.Add(0x2C);      // descriptor marker with no descriptor bytes following
        Assert.IsType<ImageFormatException>(Record.Exception(() => GifDecoder.Decode(gif.ToArray())));
        Assert.Null(ImageReader.TryRead(gif.ToArray()));
    }

    [Fact]
    public void Jpeg_out_of_range_spectral_selection_does_not_throw_out_of_range()   // #41
    {
        // A progressive frame then a scan whose Ss/Se bytes are 0xFF (past the 64 coefficients).
        var jpeg = new List<byte> { 0xFF, 0xD8 };
        jpeg.AddRange([0xFF, 0xC2, 0x00, 0x0B]);          // SOF2 progressive
        jpeg.AddRange([8, 0, 8, 0, 8]);                    // precision, 8x8, 1 component
        jpeg.AddRange([1, 0x11, 0]);
        jpeg.AddRange([0xFF, 0xDA, 0x00, 0x08]);          // SOS
        jpeg.AddRange([1, 1, 0, 0xFF, 0xFF, 0x00]);        // 1 comp, then Ss=FF, Se=FF, Ah/Al
        jpeg.AddRange([0xFF, 0xD9]);
        var ex = Record.Exception(() => JpegDecoder.Decode(jpeg.ToArray()));
        Assert.True(ex is null or ImageFormatException,
            $"a runtime exception escaped: {ex?.GetType().Name}");
    }

    [Fact]
    public void Tiff_rows_per_strip_zero_does_not_divide_by_zero()   // #33
    {
        var tiff = new TiffBuilder()
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 8).Tag(258, 3, 1, 8)
            .Tag(259, 3, 1, 1).Tag(277, 3, 1, 1)
            .Tag(278, 4, 1, 0)                          // RowsPerStrip = 0
            .Tag(273, 4, 1, 8).Tag(279, 4, 1, 8)
            .Build();
        var ex = Record.Exception(() => ImageReader.TryRead(tiff));
        Assert.True(ex is null, $"a runtime exception escaped: {ex?.GetType().Name}");
    }

    [Fact]
    public void Tiff_bits_per_sample_zero_count_does_not_index_an_empty_list()   // #32
    {
        var tiff = new TiffBuilder()
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 8)
            .Tag(258, 3, 0, 0)                          // BitsPerSample with a zero value count
            .Tag(259, 3, 1, 1).Tag(277, 3, 1, 1)
            .Tag(273, 4, 1, 8).Tag(279, 4, 1, 8)
            .Build();
        var ex = Record.Exception(() => ImageReader.TryRead(tiff));
        Assert.True(ex is null, $"a runtime exception escaped: {ex?.GetType().Name}");
    }

    [Fact]
    public void Tiff_strip_byte_count_of_max_uint_does_not_slice_backwards()   // #34
    {
        var b = new TiffBuilder().Tags(8);
        var stripAt = b.Blob(new byte[64]);
        var tiff = b
            .Tag(256, 3, 1, 8).Tag(257, 3, 1, 8).Tag(258, 3, 1, 8)
            .Tag(259, 3, 1, 1).Tag(277, 3, 1, 1)
            .Tag(273, 4, 1, stripAt).Tag(279, 4, 1, unchecked((int)0xFFFFFFFF))   // count = -1
            .Build();
        var ex = Record.Exception(() => ImageReader.TryRead(tiff));
        Assert.True(ex is null, $"a runtime exception escaped: {ex?.GetType().Name}");
    }

    [Fact]
    public void Jpeg_with_an_unmappable_component_count_is_refused()   // #44
    {
        // SOF0 declaring two components, which maps to no PDF colour space.
        var jpeg = new List<byte> { 0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x0E };
        jpeg.AddRange([8, 0, 8, 0, 8]);       // precision, 8x8, 2 components
        jpeg.Add(2);
        jpeg.AddRange([1, 0x11, 0, 2, 0x11, 0]);
        jpeg.AddRange([0xFF, 0xD9]);
        Assert.Null(ImageReader.TryRead(jpeg.ToArray()));
    }
}
