namespace Bork2d.Assets.Pack;

/// <summary>
/// A read-only, seekable window over a shared pack stream.
/// </summary>
/// <remarks>
/// The underlying stream position is mutated only while the archive I/O lock is held, so several
/// windows can be used from different threads without corrupting each other. Disposing a window
/// never disposes the shared pack stream.
/// </remarks>
internal sealed class PackSubStream : Stream
{
    private readonly Stream _source;
    private readonly Lock _ioLock;
    private readonly long _start;
    private readonly long _length;
    private long _position;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PackSubStream"/> class.
    /// </summary>
    /// <param name="source">The shared pack stream.</param>
    /// <param name="ioLock">The lock guarding the shared stream position.</param>
    /// <param name="start">The absolute start offset of the window.</param>
    /// <param name="length">The window byte length.</param>
    internal PackSubStream(Stream source, Lock ioLock, long start, long length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        _source = source;
        _ioLock = ioLock;
        _start = start;
        _length = length;
    }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanSeek => !_disposed;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _length;

    /// <inheritdoc/>
    public override long Position
    {
        get => _position;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, _length);
            _position = value;
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0 || _position >= _length)
        {
            return 0;
        }

        int count = (int)Math.Min(buffer.Length, _length - _position);
        int read;
        lock (_ioLock)
        {
            _source.Position = _start + _position;
            read = _source.Read(buffer[..count]);
        }

        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unknown seek origin."),
        };

        if (target < 0 || target > _length)
        {
            throw new IOException("An attempt was made to seek outside the pack payload.");
        }

        _position = target;
        return _position;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
        => throw new NotSupportedException("Pack windows are read-only.");

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Pack windows are read-only.");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        _disposed = true;
        base.Dispose(disposing);
    }
}
