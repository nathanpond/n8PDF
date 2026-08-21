namespace n8PDF.Packaging;

/// <summary>
/// How much a package is allowed to decompress to.
/// </summary>
/// <remarks>
/// A <c>.docx</c> is a ZIP, and a ZIP says how large its contents are in its own header — which is
/// to say the file describes itself, and a hostile one lies. A part of a few hundred bytes can
/// decompress to gigabytes: zeros compress about a thousand to one, and nothing in the format
/// stops a part being a gigabyte of them. Reading such a part to the end, which is what this did
/// until these limits existed, exhausts the memory of whatever process was asked to convert it.
///
/// So the limits are counted against what actually comes out of the decompressor rather than
/// against what the header claims, and they are absolute sizes rather than a ratio: a ratio says
/// how surprising a part is, and what costs memory is how large it is.
///
/// The defaults are set for documents rather than for these tests. Every fixture here is under
/// 110KB with at most 25 parts, which would justify limits far tighter than these; a report full
/// of photographs would not, and a converter that refused one would be worse than useless. They
/// are meant to be beyond what a real document reaches and far below what exhausts a machine.
/// </remarks>
public sealed class PackageLimits
{
    /// <summary>
    /// The most a single part may decompress to. The largest part of a real document is an image
    /// or an embedded font; 128MB is past any of them and short of trouble.
    /// </summary>
    public long MaximumPartBytes { get; set; } = 128L * 1024 * 1024;

    /// <summary>
    /// The most a package may decompress to in total, counted across every read. Stops the parts
    /// that are each just inside the limit from adding up to something that is not.
    /// </summary>
    /// <remarks>
    /// Every byte that comes out of the decompressor counts, so a part read twice counts twice.
    /// That is deliberate: the cost being bounded is the work done, and reading a part again does
    /// the work again.
    /// </remarks>
    public long MaximumTotalBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>
    /// The most parts a package may declare. A document with several hundred images is ordinary;
    /// one with tens of thousands of parts is an attack on whatever enumerates them.
    /// </summary>
    public int MaximumPartCount { get; set; } = 4096;
}

/// <summary>
/// Thrown when a package asks to decompress more than <see cref="PackageLimits"/> allows.
/// </summary>
/// <remarks>
/// Distinct from the exceptions a merely broken file raises, because it means something different:
/// the package was read successfully and what it holds is more than was allowed, which is a
/// decision the caller can revisit by raising the limit. A caller who genuinely has a 700MB
/// document can catch this and try again.
/// </remarks>
public sealed class PackageTooLargeException(string message) : Exception(message);
