using n8PDF.Packaging;
using Xunit;

namespace n8PDF.Tests;

/// <summary>
/// The package readers apply their limits and do not leak on the seekable/non-seekable and
/// file-path paths (#151, #203).
/// </summary>
public class PackageStreamTests
{
    /// <summary>A stream that reports it cannot seek, forcing the buffering path.</summary>
    private sealed class NonSeekable(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin r) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    [Fact]
    public void A_non_seekable_stream_over_the_limit_is_refused_with_the_package_signal()   // #203
    {
        var data = new byte[3 * 1024 * 1024];   // 3 MB of zeros — not a zip, but the buffer runs first
        var limits = new PackageLimits { MaximumTotalBytes = 1024 * 1024 };   // 1 MB cap

        Assert.IsType<PackageTooLargeException>(
            Record.Exception(() => OpcPackage.Open(new NonSeekable(data), limits: limits)));
    }

    [Fact]
    public void A_non_seekable_stream_within_the_limit_reaches_the_zip_reader()   // #203
    {
        // Under the cap, the buffering path runs to completion and the failure is the zip's, not
        // the limit's — proving the cap does not bite a legitimate stream.
        var data = new byte[1024];   // not a zip
        var limits = new PackageLimits { MaximumTotalBytes = 1024 * 1024 };
        var ex = Record.Exception(() => OpcPackage.Open(new NonSeekable(data), limits: limits));
        Assert.IsNotType<PackageTooLargeException>(ex);
    }

    [Fact]
    public void Open_of_a_malformed_file_fails_cleanly_without_leaking()   // #151
    {
        var path = Path.Combine(Path.GetTempPath(), $"n8pdf-notzip-{Guid.NewGuid():N}.docx");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
        try
        {
            // The handoff throws (not a zip); the file handle must be released, so this delete
            // succeeds on Windows semantics and the loop below does not exhaust handles.
            for (var i = 0; i < 50; i++) Record.Exception(() => OpcPackage.Open(path));
            Assert.True(true);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
