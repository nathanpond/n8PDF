namespace n8PDF.Images;

/// <summary>
/// How large a picture is allowed to be, measured in the pixels it decodes to.
/// </summary>
/// <remarks>
/// An image file says its own size in its header, and every decoder here allocates from what it
/// says before reading a byte of the picture itself: a PNG of a few hundred bytes can declare
/// itself fifty thousand pixels square, and asking for that is asking for seven and a half
/// gigabytes. The compressed part limits cannot see this coming — the file really is small — so
/// the guard has to stand where the dimensions are read.
///
/// The bound is on the product rather than on either side of it. A picture a million pixels wide
/// and one tall costs a megabyte and is merely strange; it is the area that is the memory. It is
/// computed in <see cref="long"/> arithmetic for a second reason: multiplied as <see cref="int"/>
/// the product of two plausible-looking dimensions overflows, and an overflowed length allocates
/// something small and then writes past it.
/// </remarks>
internal static class ImageLimits
{
    /// <summary>
    /// Fifty million pixels, which is 150MB of the three bytes a pixel this keeps them in. A page
    /// of A4 scanned at 600dpi is 35 million; a photograph from a very good camera is 50. Beyond
    /// that a document is not carrying a picture, it is carrying an argument.
    /// </summary>
    public const long DefaultMaximumPixels = 50_000_000;

    /// <summary>
    /// How deeply one picture may be embedded inside another.
    /// </summary>
    /// <remarks>
    /// A picture can hold a picture. A metafile's image object carries a whole image file, and a
    /// TIFF's JPEG strip is a whole JPEG; both are read by handing the bytes back to
    /// <see cref="ImageReader"/>, which decides what they are by looking at them. Neither asks
    /// what it is already inside, so a file that embeds itself is read again, and again.
    ///
    /// Nothing about that is expensive per level — it is the stack that runs out. A few hundred
    /// levels cost a few tens of kilobytes on disk and exhaust the thread; and a
    /// <see cref="StackOverflowException"/> cannot be caught, so the process goes rather than the
    /// conversion. That is the one failure in this layer the rest of its error handling cannot
    /// reach, which is why the bound is here rather than in whichever decoder was noticed first.
    ///
    /// Two, because one level is what really happens — a drawing with a picture in it, a TIFF
    /// holding its JPEG — and nothing Word writes goes deeper. The second is slack, not need.
    /// </remarks>
    public const int MaximumNesting = 2;

    /// <summary>
    /// Refuses a picture whose declared dimensions are past the limit, before anything is
    /// allocated for it.
    /// </summary>
    /// <remarks>
    /// An <see cref="ImageFormatException"/> rather than anything grander, because that is what
    /// the rest of this layer throws for a picture it will not read, and it is what
    /// <see cref="ImageReader.TryRead"/> catches: the picture is left out and the document
    /// converts. That matters more than it looks. Twenty bytes of nonsense beginning "GIF89a"
    /// declare themselves 24,864 by 25,710, and a document that happens to hold such a thing
    /// should lose the picture rather than the conversion — which is how the rule was settled,
    /// by a test that already held exactly that.
    /// </remarks>
    public static void Check(long width, long height, long maximum, string format)
    {
        if (width <= 0 || height <= 0) return;

        if (width * height > maximum)
        {
            throw new ImageFormatException(
                $"A {format} image declares itself {width:N0} by {height:N0} pixels, which is " +
                $"{width * height:N0} against a limit of {maximum:N0}.");
        }
    }
}
