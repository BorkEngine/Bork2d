using Bork2d.Api;
namespace Bork2d.Assets;

/// <summary>
/// Enforces the declared decompressed byte count of a pack payload.
/// </summary>
/// <remarks>
/// <para>
/// Reads are bounded to the declared <c>Size</c>. A source that ends early or yields even one byte
/// more is reported as a <see cref="Bork2dException"/>. The extra-byte check runs when the declared
/// size is reached, so a payload with trailing decompressed bytes fails on the read that completes
/// the payload rather than only on a later attempt to read past the end.
/// </para>
/// <para>
/// Each payload read owns a fresh bounded window and codec, so a failure does not corrupt the
/// archive or affect later reads. This stream owns and disposes its source.
/// </para>
/// </remarks>
internal sealed class ExactSizeReadStream : Stream
{
    private readonly Stream _source;
    private readonly long _expected;
    private readonly string _description;
    private long _remaining;
    private bool _completed;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExactSizeReadStream"/> class.
    /// </summary>
    /// <param name="source">The decompressed payload source.</param>
    /// <param name="expectedSize">The declared payload size.</param>
    /// <param name="description">A human-readable payload description for diagnostics.</param>
    internal ExactSizeReadStream(Stream source, uint expectedSize, string description)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(description);

        _source = source;
        _expected = expectedSize;
        _description = description;
        _remaining = expectedSize;
    }

    /// <inheritdoc/>
    public override bool CanRead => !_disposed;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length => _expected;

    /// <inheritdoc/>
    public override long Position
    {
        get => _expected - _remaining;
        set => throw new NotSupportedException("Payload streams are read-only and non-seekable.");
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (buffer.Length == 0)
        {
            return 0;
        }

        if (_completed)
        {
            return 0;
        }

        if (_remaining == 0)
        {
            VerifyEnd();
            return 0;
        }

        int count = (int)Math.Min(buffer.Length, _remaining);
        int read;
        try
        {
            read = _source.Read(buffer[..count]);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            throw Failure("could not be decompressed", exception);
        }

        if (read == 0)
        {
            throw Failure(
                $"decompressed to {_expected - _remaining} of the declared {_expected} bytes");
        }

        _remaining -= read;
        if (_remaining == 0)
        {
            VerifyEnd();
        }

        return read;
    }

    /// <inheritdoc/>
    public override void Flush()
    {
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException("Payload streams are read-only and non-seekable.");

    /// <inheritdoc/>
    public override void SetLength(long value)
        => throw new NotSupportedException("Payload streams are read-only and non-seekable.");

    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count)
        => throw new NotSupportedException("Payload streams are read-only and non-seekable.");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            _disposed = true;
            if (disposing)
            {
                _source.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private static bool IsSourceFailure(Exception exception)
        => exception is InvalidDataException or IOException or NotSupportedException;

    private void VerifyEnd()
    {
        Span<byte> probe = stackalloc byte[1];
        int extra;
        try
        {
            extra = _source.Read(probe);
        }
        catch (Exception exception) when (IsSourceFailure(exception))
        {
            throw Failure("could not be fully decompressed", exception);
        }

        if (extra != 0)
        {
            throw Failure($"decompressed to more than the declared {_expected} bytes");
        }

        _completed = true;
    }

    private Bork2dException Failure(string reason, Exception? innerException = null)
    {
        string message = $"Asset pack {_description} {reason}.";
        return innerException is null
            ? new Bork2dException(message)
            : new Bork2dException(message, innerException);
    }
}
