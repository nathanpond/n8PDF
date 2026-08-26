using n8PDF.Images;
using Xunit;
using Xunit.Abstractions;

namespace n8PDF.Tests;

/// <summary>
/// The runtime twin of the two custom Semgrep rules — <c>unchecked-round-cast</c> and
/// <c>additive-length-bound-overflow</c> (#266). The library is compiled with
/// <c>CheckForOverflowUnderflow</c>, so an integer length, count or offset read off an untrusted
/// file that overflows on its way to a size, an index or a cast becomes a catchable
/// <see cref="OverflowException"/> at the point it happens rather than a silently wrapped value
/// that sizes a buffer wrong or moves a dimension. This proves the mechanism on a crafted input at
/// a site the structural guards do not reach: the picture decoders bound width and height and the
/// channel count, but an enhanced metafile's frame rectangle is a pair of signed 32-bit
/// coordinates subtracted straight from the header.
/// </summary>
public class CheckedArithmeticTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    /// <summary>
    /// An <c>EMR_HEADER</c> whose frame rectangle runs from far negative to far positive, so that
    /// the width the reader takes as <c>frameRight - frameLeft</c> exceeds what a signed 32-bit
    /// integer holds. Bounds are left at zero so the pixel-count guard has nothing to catch; the
    /// overflow is in the frame subtraction alone.
    /// </summary>
    private static byte[] MetafileWithOverflowingFrame()
    {
        var emf = new byte[88];

        void Put(int at, int value) => BitConverter.GetBytes(value).CopyTo(emf, at);

        Put(0, 1);            // iType: EMR_HEADER
        Put(4, emf.Length);   // nSize: the whole record
        Put(8, 0);            // rclBounds: left, top, right, bottom — a zero device rectangle
        Put(12, 0);
        Put(16, 0);
        Put(20, 0);
        Put(24, -2_000_000_000); // rclFrame: left
        Put(28, 0);              // top
        Put(32, 2_000_000_000);  // right — right - left = 4e9, past int.MaxValue
        Put(36, 0);              // bottom

        // The " EMF" signature the header is recognised by.
        emf[40] = 0x20;
        emf[41] = 0x45;
        emf[42] = 0x4D;
        emf[43] = 0x46;

        return emf;
    }

    [Fact]
    public void An_overflowing_dimension_throws_rather_than_wrapping_to_a_wrong_size()
    {
        var emf = MetafileWithOverflowingFrame();
        _output.WriteLine(
            $"{emf.Length}-byte EMF with a frame of {-2_000_000_000} .. {2_000_000_000}: " +
            "the width subtraction overflows a 32-bit integer.");

        // Without overflow checking this subtraction wraps to a negative width the reader then
        // clamps to one point — a silently wrong dimension, no throw. With it, the overflow is a
        // clean, catchable exception at the point it happens.
        var thrown = Record.Exception(() => EmfDecoder.Decode(emf));

        Assert.IsType<OverflowException>(thrown);
    }

    [Fact]
    public void The_reader_net_turns_that_overflow_into_a_dropped_image_not_a_crash()
    {
        var emf = MetafileWithOverflowingFrame();

        // The same crafted metafile through the public entry point: the OverflowException is one of
        // the types the ImageReader net catches (#48), so a hostile picture costs its own placement
        // and nothing else — it is dropped, and the conversion carries on.
        Assert.Null(ImageReader.TryRead(emf));
    }
}
