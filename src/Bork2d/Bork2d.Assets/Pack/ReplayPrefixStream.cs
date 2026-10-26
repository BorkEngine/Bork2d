using System.Threading;

namespace Bork2d.Assets.Pack;

/// <summary>
/// Replays one already-read byte before continuing with the wrapped readable stream.
/// </summary>
/// <remarks>
/// <para>
/// The asset loader peeks the first MessagePack token to reject an empty or <c>nil</c> root without
/// buffering the payload. This wrapper replays that byte and then reads from the payload stream, so
/// the generated-shape deserializer owns the same streaming contract.
/// </para>
/// <para>
/// The wrapper is read-only and non-seekable, and it owns neither the prefix nor the source:
/// disposing it never disposes the payload stream.
/// </para>
/// </remarks>
internal sealed class ReplayPrefixStream : Stream
{
    private readonly byte _prefix;
    private readonly Stream _source;
    private bool _prefixConsumed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReplayPrefixStream"/> class.
    /// </summary>
    /// <param name="prefix">The already-read first byte.</param>
    /// <param name="source">The remaining readable payload stream.</param>
    internal ReplayPrefixStream(byte prefix, Stream source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _prefix = prefix;
        _source = source;
    }

    /// <inheritdoc/>
    public override bool CanRead => true;

    /// <inheritdoc/>
    public override bool CanSeek => false;

    /// <inheritdoc/>
    public override bool CanWrite => false;

    /// <inheritdoc/>
    public override long Length
        => throw new NotSupportedException("Payload streams are read-only and non-seekable.");

    /// <inheritdoc/>
    public override long Position
    {
        get => throw new NotSupportedException("Payload streams are read-only and non-seekable.");
        set => throw new NotSupportedException("Payload streams are read-only and non-seekable.");
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0)
        {
            return 0;
        }

        if (!_prefixConsumed)
        {
            _prefixConsumed = true;
            buffer[0] = _prefix;
            return 1;
        }

        return _source.Read(buffer);
    }

    /// <inheritdoc/>
    public override int ReadByte()
    {
        if (!_prefixConsumed)
        {
            _prefixConsumed = true;
            return _prefix;
        }

        return _source.ReadByte();
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
        {
            return ValueTask.FromResult(0);
        }

        if (!_prefixConsumed)
        {
            _prefixConsumed = true;
            buffer.Span[0] = _prefix;
            return ValueTask.FromResult(1);
        }

        return _source.ReadAsync(buffer, cancellationToken);
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
        // The payload stream is owned by the caller and disposed after the replay wrapper.
        base.Dispose(disposing);
    }
}
