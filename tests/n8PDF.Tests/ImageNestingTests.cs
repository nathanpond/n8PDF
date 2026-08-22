using n8PDF.Images;
using n8PDF.Tests.Support;

namespace n8PDF.Tests;

/// <summary>
/// Tests that a picture holding a picture is read only so far down.
/// </summary>
/// <remarks>
/// Two of the formats carry whole image files inside themselves: a metafile's image object holds
/// one, and a TIFF's JPEG strip is one. Both are read by handing the bytes back to
/// <see cref="ImageReader"/>, which works out what they are by looking at them — so neither knows,
/// or can know, what it is already inside. A file that embeds itself is therefore read again at
/// every level.
///
/// What makes that worth a test of its own rather than a line in the format tests is the shape of
/// the failure. Every other way a picture can be malformed ends in an exception, which
/// <see cref="ImageReader.TryRead"/> catches and turns into a picture left out; this one ends in a
/// <see cref="StackOverflowException"/>, which .NET does not let anything catch, so the process
/// goes rather than the conversion. It is the one failure in this layer that the rest of its error
/// handling cannot reach.
///
/// So the bound is counted at the one place both paths pass through, and these check it from both
/// sides: that the counting is right at the boundary, and that each of the two ways down actually
/// increments it.
/// </remarks>
public class ImageNestingTests
{
    private const int Width = 8;
    private const int Height = 6;

    /// <summary>
    /// A JPEG far enough along to be recognised, which is as far as anything here reads one.
    /// </summary>
    /// <remarks>
    /// Written by hand rather than taken from <c>sips</c>, because these are checks on a
    /// vulnerability and they have to run wherever the suite runs. Nothing decodes it: a JPEG in a
    /// TIFF is handed to the PDF as the file it already is, so a start-of-frame marker is the
    /// whole of what is read.
    /// </remarks>
    private static byte[] MinimalJpeg(int width, int height) =>
    [
        0xff, 0xd8, // start of image
        0xff, 0xc0, 0x00, 0x11, 0x08, // a baseline frame, 17 bytes, 8-bit samples
        (byte)(height >> 8), (byte)height,
        (byte)(width >> 8), (byte)width,
        0x03, // three components, so it reads as colour
        0x01, 0x11, 0x00,
        0x02, 0x11, 0x01,
        0x03, 0x11, 0x01,
        0xff, 0xd9 // end of image
    ];

    /// <summary>A drawing that also carries a picture, which is the shape the metafile path takes.</summary>
    /// <remarks>
    /// The picture is drawn as well as carried, which is what makes the difference visible: a
    /// picture that was read becomes an <c>ImageOperation</c> in the drawing, and one that was
    /// refused becomes nothing at all. The rectangle is there so the metafile still draws
    /// something either way — a metafile that draws nothing is rejected outright, which would hide
    /// which of the two happened.
    /// </remarks>
    private static byte[] DrawingHolding(byte[] file)
    {
        var writer = new EmfWriter(120, 90);

        writer.PlusHeader();
        writer.PlusImage(1, file);
        writer.PlusDrawImage(1, 5, 5, 100, 70);
        writer.PlusFillRectangle(20, 90, 160, 5, 5, 100, 70);

        return writer.Build();
    }

    /// <summary>How many pictures a drawing actually put on the page.</summary>
    private static int PicturesDrawn(ImageData? drawing) =>
        drawing?.Drawing?.Operations.Count(operation => operation is ImageOperation) ?? 0;

    /// <summary>
    /// The bound itself: a picture is read at the limit and refused one past it.
    /// </summary>
    /// <remarks>
    /// Straight at <see cref="ImageReader.Read"/> with the depth handed in, so the boundary is
    /// pinned exactly rather than inferred from however many wrappers a format needed. An
    /// <see cref="ImageFormatException"/> is the right refusal because it is the one
    /// <see cref="ImageReader.TryRead"/> catches: the picture is left out and the document still
    /// converts, which is what both this layer and <c>PackageLimits</c> promise.
    /// </remarks>
    [Fact]
    public void A_picture_is_read_at_the_nesting_limit_and_refused_one_past_it()
    {
        var bmp = ImageWriter.Bmp(Width, Height, ImageWriter.Sample(Width, Height));

        for (var depth = 0; depth <= ImageLimits.MaximumNesting; depth++)
        {
            var image = ImageReader.Read(bmp, ImageLimits.DefaultMaximumPixels, depth);
            Assert.Equal(Width, image.Width);
        }

        var past = ImageLimits.MaximumNesting + 1;

        var refused =
            Assert.Throws<ImageFormatException>(() => ImageReader.Read(bmp, ImageLimits.DefaultMaximumPixels, past));

        Assert.Contains("nested", refused.Message);

        // And the refusal costs the picture rather than the conversion.
        Assert.Null(ImageReader.TryRead(bmp, ImageLimits.DefaultMaximumPixels, past));
    }

    /// <summary>
    /// A TIFF inside a TIFF is counted, and the chain is cut at the limit.
    /// </summary>
    /// <remarks>
    /// The strip of a TIFF written as JPEG is handed back to the reader to be identified, and the
    /// check that it really was a JPEG runs only once that has returned — too late to stop a TIFF
    /// holding a TIFF. Each wrapper is one level: at the limit the innermost JPEG still comes back
    /// up through all of them, and one past it nothing does.
    ///
    /// The tables are kept with the scan rather than split out, because splitting looks for JPEG
    /// markers and would take apart the TIFF standing in for one.
    /// </remarks>
    [Fact]
    public void A_tiff_holding_a_tiff_is_counted_and_cut_at_the_limit()
    {
        var core = MinimalJpeg(Width, Height);

        for (var wrappers = 1; wrappers <= ImageLimits.MaximumNesting; wrappers++)
        {
            var image = ImageReader.TryRead(Wrapped(core, wrappers));

            Assert.NotNull(image);

            // What comes back is the innermost JPEG, passed up through every wrapper untouched.
            Assert.Equal(ImageEncoding.Jpeg, image.Encoding);
            Assert.Equal(Width, image.Width);
        }

        Assert.Null(ImageReader.TryRead(Wrapped(core, ImageLimits.MaximumNesting + 1)));

        static byte[] Wrapped(byte[] core, int times)
        {
            var file = core;
            for (var i = 0; i < times; i++) file = ImageWriter.JpegTiff(Width, Height, file, separateTables: false);

            return file;
        }
    }

    /// <summary>
    /// A metafile holding a metafile is refused rather than followed down.
    /// </summary>
    /// <remarks>
    /// This is the one that used to take the process with it. The chain is kept short on purpose:
    /// a depth that really would exhaust the stack cannot be asserted against, because a
    /// <see cref="StackOverflowException"/> would end the test run rather than fail a test. What
    /// is checked instead is the property that makes any depth safe — that the picture inside a
    /// drawing is counted as one level deeper than the drawing itself.
    ///
    /// That is visible because the drawing paints the picture as well as carrying it: when the
    /// picture is read the drawing has an image on it, and when it is refused the drawing is still
    /// a drawing but the image is gone. Reading the same bytes at each depth in turn therefore
    /// shows exactly where the counting stops it — which a test that only asked whether the whole
    /// thing came back would not, since the rectangle keeps it readable either way.
    /// </remarks>
    [Fact]
    public void A_metafile_holding_a_metafile_is_refused_rather_than_followed()
    {
        var picture = ImageWriter.Bmp(Width, Height, ImageWriter.Sample(Width, Height));
        var drawing = DrawingHolding(picture);

        // The picture inside sits one level below the drawing, so it survives while there is a
        // level left for it and goes when the drawing itself is already at the limit.
        for (var depth = 0; depth < ImageLimits.MaximumNesting; depth++)
        {
            var read = ImageReader.TryRead(drawing, ImageLimits.DefaultMaximumPixels, depth);

            Assert.NotNull(read);
            Assert.Equal(1, PicturesDrawn(read));
        }

        var atTheLimit = ImageReader.TryRead(drawing, ImageLimits.DefaultMaximumPixels, ImageLimits.MaximumNesting);

        Assert.NotNull(atTheLimit);
        Assert.Equal(0, PicturesDrawn(atTheLimit));

        // And the drawing itself is refused a level after that, picture or no picture.
        Assert.Null(ImageReader.TryRead(drawing, ImageLimits.DefaultMaximumPixels, ImageLimits.MaximumNesting + 1));

        // And a chain of drawings, each holding the last, is read to the bound and no further.
        // What that costs is not visible from the top — the outermost drawing paints its own
        // child whatever happened below it — so what is checked here is that the read returns at
        // all, which is the whole of the original defect: it used to go down until the stack did.
        var nested = drawing;
        for (var i = 0; i < ImageLimits.MaximumNesting; i++) nested = DrawingHolding(nested);

        Assert.Equal(1, PicturesDrawn(ImageReader.TryRead(nested)));
    }
}