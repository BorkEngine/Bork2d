using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using Bork2d.Api;
using Bork2d.Assets.Vfs;

namespace Bork2d.Assets.Pack;

/// <summary>
/// An opened, validated v1 asset pack.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Open(string)"/> opens the file read-only, shares it for reading only, and keeps the
/// handle open until <see cref="Dispose"/>. The runtime mounts packs for the process lifetime and
/// never disposes them; tooling and tests may dispose an archive when they are finished with it.
/// </para>
/// <para>
/// The archive owns the stream passed to <see cref="Open(Stream)"/> and disposes it with the
/// archive. Every read uses a fresh bounded window and codec, so concurrent readers are serialized
/// only around the shared stream position and a failed payload read leaves the archive usable. The
/// archive is immutable after <see cref="Open(string)"/>; disposal must not race an active read.
/// </para>
/// <para>
/// Structural validation runs once at open. Payloads are not decompressed at open: each
/// decompression enforces the entry's declared <c>Size</c> exactly, lazily on the read.
/// </para>
/// </remarks>
internal sealed class PackArchive : IDisposable
{
    private readonly Stream _stream;
    private readonly Lock _ioLock;
    private readonly PackEntry[] _entries;
    private readonly Dictionary<string, PackEntry> _entriesByPath;
    private readonly string _displayPath;
    private readonly long _length;
    private bool _disposed;

    private PackArchive(
        Stream stream,
        Lock ioLock,
        PackEntry[] entries,
        Dictionary<string, PackEntry> entriesByPath,
        string displayPath,
        long length)
    {
        _stream = stream;
        _ioLock = ioLock;
        _entries = entries;
        _entriesByPath = entriesByPath;
        _displayPath = displayPath;
        _length = length;
    }

    /// <summary>Gets the entries in their stored (writer-sorted) order.</summary>
    internal IReadOnlyList<PackEntry> Entries => _entries;

    /// <summary>Gets the pack identifier used in diagnostics.</summary>
    internal string DisplayPath => _displayPath;

    /// <summary>Gets the pack file length in bytes.</summary>
    internal long Length => _length;

    /// <summary>
    /// Opens and validates a pack file read-only.
    /// </summary>
    /// <param name="path">The filesystem path; relative paths resolve against the current directory.</param>
    /// <returns>The validated archive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or whitespace.</exception>
    /// <exception cref="Bork2dException">The file cannot be opened or is not a valid v1 pack.</exception>
    internal static PackArchive Open(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("The pack path must not be empty or whitespace.", nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            throw new ArgumentException(
                $"Invalid pack path '{path}': {exception.Message}",
                nameof(path),
                exception);
        }

        FileStream stream;
        try
        {
            stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or NotSupportedException or ArgumentException)
        {
            throw new Bork2dException(
                $"Failed to open asset pack '{fullPath}': {exception.Message}",
                exception);
        }

        try
        {
            return Create(stream, fullPath);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens and validates a pack from a caller-supplied seekable stream.
    /// </summary>
    /// <param name="stream">The readable, seekable pack stream; the archive takes ownership.</param>
    /// <returns>The validated archive.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream is not readable and seekable.</exception>
    /// <exception cref="Bork2dException">The stream is not a valid v1 pack.</exception>
    internal static PackArchive Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
        {
            throw new ArgumentException("The pack stream must be readable.", nameof(stream));
        }

        if (!stream.CanSeek)
        {
            throw new ArgumentException("The pack stream must be seekable.", nameof(stream));
        }

        try
        {
            return Create(stream, "<stream>");
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Looks up an entry by its complete canonical path.
    /// </summary>
    /// <param name="path">The canonical path.</param>
    /// <param name="entry">The matching entry, when found.</param>
    /// <returns><see langword="true"/> when the path exists in this pack.</returns>
    internal bool TryGetEntry(string path, [NotNullWhen(true)] out PackEntry? entry)
    {
        ArgumentNullException.ThrowIfNull(path);
        ThrowIfDisposed();
        return _entriesByPath.TryGetValue(path, out entry);
    }

    /// <summary>
    /// Opens a stream over the stored payload bytes (still compressed when
    /// <see cref="PackEntry.IsCompressed"/> is <see langword="true"/>).
    /// </summary>
    /// <param name="entry">An entry belonging to this archive.</param>
    /// <returns>A bounded read-only stream over the stored bytes.</returns>
    internal Stream OpenStoredStream(PackEntry entry)
    {
        ValidateEntry(entry);
        return new PackSubStream(_stream, _ioLock, (long)entry.Offset, entry.Length);
    }

    /// <summary>
    /// Reads the stored payload bytes (still compressed when <see cref="PackEntry.IsCompressed"/>
    /// is <see langword="true"/>).
    /// </summary>
    /// <param name="entry">An entry belonging to this archive.</param>
    /// <returns>A fresh array containing the stored bytes.</returns>
    internal byte[] ReadStoredBytes(PackEntry entry)
    {
        ValidateEntry(entry);
        if (entry.Length > Array.MaxLength)
        {
            throw Failure(
                entry,
                $"has a stored length of {entry.Length} bytes and exceeds Array.MaxLength");
        }

        byte[] result = new byte[(int)entry.Length];
        using Stream stream = OpenStoredStream(entry);
        ReadExactly(entry, stream, result);
        return result;
    }

    /// <summary>
    /// Opens a stream that yields the decompressed payload and enforces the declared
    /// <see cref="PackEntry.Size"/> exactly.
    /// </summary>
    /// <remarks>
    /// This is the streaming path intended for MessagePack asset deserialization: the payload is
    /// decompressed as it is read and need not fit in one <see cref="byte"/> array. A payload that
    /// decompresses to fewer or more bytes than the declared size fails with
    /// <see cref="Bork2dException"/> from a read; the archive remains usable.
    /// </remarks>
    /// <param name="entry">An entry belonging to this archive.</param>
    /// <returns>A read-only, non-seekable stream over the decompressed payload.</returns>
    internal Stream OpenDecompressedStream(PackEntry entry)
    {
        ValidateEntry(entry);

        Stream stored = OpenStoredStream(entry);
        Stream payload = stored;
        if (entry.IsCompressed)
        {
            try
            {
                payload = PackCompression.OpenDecompressionStream(stored);
            }
            catch
            {
                stored.Dispose();
                throw;
            }
        }

        string description = $"'{_displayPath}' entry '{entry.Path}'";
        return new ExactSizeReadStream(payload, entry.Size, description);
    }

    /// <summary>
    /// Decompresses a whole payload, enforcing the declared <see cref="PackEntry.Size"/> exactly.
    /// </summary>
    /// <param name="entry">An entry belonging to this archive.</param>
    /// <returns>A fresh array containing the decompressed payload.</returns>
    internal byte[] ReadDecompressedBytes(PackEntry entry)
    {
        ValidateEntry(entry);
        if (entry.Size > Array.MaxLength)
        {
            throw Failure(
                entry,
                $"has a size of {entry.Size} bytes and exceeds Array.MaxLength");
        }

        byte[] result = new byte[(int)entry.Size];
        using Stream stream = OpenDecompressedStream(entry);

        int total = 0;
        while (total < result.Length)
        {
            int read = stream.Read(result, total, result.Length - total);
            if (read == 0)
            {
                throw Failure(entry, $"decompressed to {total} of the declared {entry.Size} bytes");
            }

            total += read;
        }

        if (result.Length == 0)
        {
            Span<byte> probe = stackalloc byte[1];
            _ = stream.Read(probe);
        }

        return result;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
    }

    private static PackArchive Create(Stream stream, string displayPath)
    {
        long length = stream.Length;
        if (length < PackFormat.PrefixLength)
        {
            throw Failure(displayPath, "the file is shorter than the 28-byte prefix");
        }

        byte[] prefix = new byte[PackFormat.PrefixLength];
        stream.Position = 0;
        if (!TryReadExactly(stream, prefix))
        {
            throw Failure(displayPath, "the file is shorter than the 28-byte prefix");
        }

        if (!prefix.AsSpan(0, PackFormat.Magic.Length).SequenceEqual(PackFormat.Magic))
        {
            throw Failure(displayPath, "the magic is not 'BORK2DPK'");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(8, 4));
        if (version != PackFormat.CurrentVersion)
        {
            throw Failure(
                displayPath,
                $"the version is {version}; only version {PackFormat.CurrentVersion} is supported");
        }

        uint headerCompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(12, 4));
        uint headerUncompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(16, 4));
        ulong headerChecksum = BinaryPrimitives.ReadUInt64LittleEndian(prefix.AsSpan(20, 8));

        if (headerCompressedLength == 0)
        {
            throw Failure(displayPath, "the compressed header length is zero");
        }

        if ((long)headerCompressedLength > length - PackFormat.PrefixLength)
        {
            throw Failure(
                displayPath,
                $"the compressed header length {headerCompressedLength} exceeds the file length");
        }

        if (headerUncompressedLength == 0)
        {
            throw Failure(displayPath, "the uncompressed header length is zero");
        }

        if (headerUncompressedLength > (uint)Array.MaxLength)
        {
            throw Failure(
                displayPath,
                $"the uncompressed header length {headerUncompressedLength} exceeds Array.MaxLength");
        }

        var ioLock = new Lock();
        byte[] headerBytes = ReadHeader(
            stream,
            ioLock,
            displayPath,
            headerCompressedLength,
            headerUncompressedLength);

        if (PackCompression.ComputeChecksum(headerBytes) != headerChecksum)
        {
            throw Failure(displayPath, "the header checksum does not match");
        }

        PackHeader? header;
        try
        {
            header = PackSerialization.DeserializeHeader(headerBytes);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw Failure(displayPath, "the header is not a v1 PackHeader", exception);
        }

        if (header is null)
        {
            throw Failure(displayPath, "the header is null");
        }

        ulong dataStart = (ulong)PackFormat.PrefixLength + headerCompressedLength;
        var entriesByPath = ValidateEntries(
            displayPath,
            header,
            length,
            dataStart);

        return new PackArchive(
            stream,
            ioLock,
            header.Entries,
            entriesByPath,
            displayPath,
            length);
    }

    private static byte[] ReadHeader(
        Stream stream,
        Lock ioLock,
        string displayPath,
        uint compressedLength,
        uint uncompressedLength)
    {
        byte[] headerBytes = new byte[(int)uncompressedLength];
        using var stored = new PackSubStream(
            stream,
            ioLock,
            PackFormat.PrefixLength,
            compressedLength);
        using Stream decompression = PackCompression.OpenDecompressionStream(stored);

        int total = 0;
        try
        {
            while (total < headerBytes.Length)
            {
                int read = decompression.Read(
                    headerBytes,
                    total,
                    headerBytes.Length - total);
                if (read == 0)
                {
                    throw Failure(
                        displayPath,
                        $"the header decompressed to {total} of the declared {uncompressedLength} bytes");
                }

                total += read;
            }

            Span<byte> probe = stackalloc byte[1];
            if (decompression.Read(probe) != 0)
            {
                throw Failure(
                    displayPath,
                    $"the header decompressed to more than the declared {uncompressedLength} bytes");
            }
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException
            or NotSupportedException)
        {
            throw Failure(displayPath, "the header is not valid maximum-quality Zstd", exception);
        }

        return headerBytes;
    }

    private static Dictionary<string, PackEntry> ValidateEntries(
        string displayPath,
        PackHeader header,
        long fileLength,
        ulong dataStart)
    {
        PackEntry[] entries = header.Entries;
        if (entries is null)
        {
            throw Failure(displayPath, "the header Entries array is null");
        }

        var entriesByPath = new Dictionary<string, PackEntry>(entries.Length, StringComparer.Ordinal);
        var payloadRanges = new List<(ulong Start, ulong End)>(entries.Length);

        foreach (PackEntry? entry in entries)
        {
            if (entry is null)
            {
                throw Failure(displayPath, "the header contains a null entry");
            }

            string? path = entry.Path;
            if (path is null)
            {
                throw Failure(displayPath, "the header contains an entry with a null path");
            }

            if (!AssetPath.TryValidateCanonical(path, out string? pathReason))
            {
                throw Failure(displayPath, $"entry path '{path}' is not canonical: {pathReason}");
            }

            if (!entriesByPath.TryAdd(path, entry))
            {
                throw Failure(displayPath, $"duplicate asset path '{path}'");
            }

            if (entry.Offset < dataStart)
            {
                throw Failure(
                    displayPath,
                    $"entry '{path}' offset {entry.Offset} is before the payload data start {dataStart}");
            }

            ulong end;
            try
            {
                end = checked(entry.Offset + entry.Length);
            }
            catch (OverflowException exception)
            {
                throw Failure(
                    displayPath,
                    $"entry '{path}' offset plus length overflows the uint64 range",
                    exception);
            }

            if (end > (ulong)fileLength)
            {
                throw Failure(
                    displayPath,
                    $"entry '{path}' ends at {end}, past the file length {fileLength}");
            }

            if (!entry.IsCompressed && entry.Length != entry.Size)
            {
                throw Failure(
                    displayPath,
                    $"uncompressed entry '{path}' has Length {entry.Length} but Size {entry.Size}");
            }

            if (entry.Length != 0)
            {
                payloadRanges.Add((entry.Offset, end));
            }
        }

        payloadRanges.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        for (int i = 1; i < payloadRanges.Count; i++)
        {
            if (payloadRanges[i].Start < payloadRanges[i - 1].End)
            {
                throw Failure(
                    displayPath,
                    $"payload ranges overlap at offset {payloadRanges[i].Start}");
            }
        }

        return entriesByPath;
    }

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        return true;
    }

    private void ReadExactly(PackEntry entry, Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                throw Failure(
                    entry,
                    $"stored {total} of the declared {buffer.Length} bytes before the pack ended");
            }

            total += read;
        }
    }

    private void ValidateEntry(PackEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ThrowIfDisposed();

        if (entry.Path is null
            || !_entriesByPath.TryGetValue(entry.Path, out PackEntry? known)
            || !ReferenceEquals(entry, known))
        {
            throw new ArgumentException(
                "The entry does not belong to this pack archive.",
                nameof(entry));
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private Bork2dException Failure(PackEntry entry, string reason)
        => new($"Asset pack '{_displayPath}' entry '{entry.Path}' {reason}.");

    private static Bork2dException Failure(
        string displayPath,
        string reason,
        Exception? innerException = null)
    {
        string message = $"Invalid asset pack '{displayPath}': {reason}.";
        return innerException is null
            ? new Bork2dException(message)
            : new Bork2dException(message, innerException);
    }
}
