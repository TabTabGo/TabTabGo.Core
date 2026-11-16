using System.Text;

namespace TabTabGo.Core.Api.Middlewares.Logging;

/// <summary>
/// Write-through stream that keeps a copy of the first bytes written to it (for logging) and passes every byte on to the
/// real stream untouched, so large and streamed responses are not buffered in memory.
/// </summary>
internal sealed class CappedCaptureStream(Stream inner, int maxBytes) : Stream
{
    private readonly MemoryStream _captured = new();

    /// <summary>True when more than the cap was written.</summary>
    internal bool Truncated { get; private set; }

    /// <summary>The captured text (at most the cap in bytes).</summary>
    internal string GetCapturedText() => Encoding.UTF8.GetString(_captured.GetBuffer(), 0, (int)_captured.Length);

    private void Capture(ReadOnlySpan<byte> buffer)
    {
        var room = maxBytes - (int)_captured.Length;
        if (room <= 0)
        {
            Truncated |= buffer.Length > 0;
            return;
        }

        if (buffer.Length > room)
        {
            Truncated = true;
            buffer = buffer[..room];
        }

        _captured.Write(buffer);
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => inner.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Capture(buffer.AsSpan(offset, count));
        inner.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Capture(buffer);
        inner.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Capture(buffer.AsSpan(offset, count));
        return inner.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Capture(buffer.Span);
        return inner.WriteAsync(buffer, cancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _captured.Dispose();
        }

        base.Dispose(disposing);
    }
}
