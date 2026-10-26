using System.Buffers.Binary;
using Bork2d.Api;

namespace Bork2d.Assets.Pack;

/// <summary>
/// A fully computed v1 pack layout: prefix values, the compressed header, and stored payloads.
/// </summary>
/// <remarks>
/// Entry offsets depend on the compressed header length, and the serialized header contains those
/// offsets. The writer iterates until <c>dataStart = 28 + headerCompressedLength</c> is no larger
/// than the base offset used by the header. Normally they are equal. A larger header can push the
/// entry offsets across a MessagePack integer-width boundary and make the recompressed header
/// smaller, so in that case the writer keeps the larger base offset and leaves unused zero bytes
/// between the header and the first payload; offsets stay absolute and at least <c>dataStart</c>.
/// The iteration is bounded.
/// </remarks>
internal sealed class PackLayout
{
    private const int MaxHeaderStabilizationAttempts = 64;

    private readonly PackEntry[] _entries;
    private readonly byte[][] _storedPayloads;
    private readonly byte[] _headerCompressed;
    private readonly uint _headerUncompressedLength;
    private readonly ulong _headerChecksum;
    private readonly long _totalLength;
    private readonly long _gapLength;

    private PackLayout(
        PackEntry[] entries,
        byte[][] storedPayloads,
        byte[] headerCompressed,
        uint headerUncompressedLength,
        ulong headerChecksum,
        long totalLength,
        long gapLength)
    {
        _entries = entries;
        _storedPayloads = storedPayloads;
        _headerCompressed = headerCompressed;
        _headerUncompressedLength = headerUncompressedLength;
        _headerChecksum = headerChecksum;
        _totalLength = totalLength;
        _gapLength = gapLength;
    }

    /// <summary>Gets the total pack byte length.</summary>
    internal long TotalLength => _totalLength;

    /// <summary>Gets the sorted entries, including their absolute offsets.</summary>
    internal IReadOnlyList<PackEntry> Entries => _entries;

    /// <summary>
    /// Computes a pack layout from the queued entries.
    /// </summary>
    /// <param name="source">The entries to write.</param>
    /// <param name="mode">The payload compression policy.</param>
    /// <returns>The computed layout.</returns>
    /// <exception cref="Bork2dException">A size or the header layout is not representable.</exception>
    internal static PackLayout Create(IReadOnlyList<PackSourceEntry> source, PackCompressionMode mode)
    {
        ArgumentNullException.ThrowIfNull(source);

        PackSourceEntry[] ordered = source
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToArray();

        var storedPayloads = new byte[ordered.Length][];
        var compressedFlags = new bool[ordered.Length];
        ulong payloadBytes = 0;

        for (int i = 0; i < ordered.Length; i++)
        {
            PackSourceEntry entry = ordered[i];
            byte[] payload = entry.Payload;
            byte[] stored = payload;

            if (mode == PackCompressionMode.Release)
            {
                byte[] compressed = PackCompression.Compress(payload);
                if (compressed.Length < payload.Length)
                {
                    stored = compressed;
                    compressedFlags[i] = true;
                }
            }
            else if (mode != PackCompressionMode.Debug)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(mode),
                    mode,
                    "Unknown pack compression mode.");
            }

            if ((ulong)payload.Length > uint.MaxValue || (ulong)stored.Length > uint.MaxValue)
            {
                throw new Bork2dException(
                    $"Asset payload '{entry.Path}' exceeds the v1 uint32 length limit.");
            }

            storedPayloads[i] = stored;
            payloadBytes = checked(payloadBytes + (ulong)stored.Length);
        }

        ulong baseOffset = PackFormat.PrefixLength;

        for (int attempt = 0; attempt < MaxHeaderStabilizationAttempts; attempt++)
        {
            PackEntry[] entries = CreateEntries(ordered, storedPayloads, compressedFlags, baseOffset);
            byte[] header = PackSerialization.SerializeHeader(new PackHeader { Entries = entries });

            if ((ulong)header.Length > uint.MaxValue)
            {
                throw new Bork2dException("The pack header exceeds the v1 uint32 length limit.");
            }

            byte[] headerCompressed = PackCompression.Compress(header);
            if ((ulong)headerCompressed.Length > uint.MaxValue)
            {
                throw new Bork2dException(
                    "The compressed pack header exceeds the v1 uint32 length limit.");
            }

            ulong dataStart = (ulong)PackFormat.PrefixLength + (ulong)headerCompressed.Length;
            if (dataStart > baseOffset)
            {
                baseOffset = dataStart;
                continue;
            }

            // A larger serialized header can push the entry offsets across a MessagePack integer
            // width boundary, which can make the next header smaller than this one. In that case
            // there is no exactly self-consistent data start below the base offset; keep the
            // base offset and leave the unused bytes between the header and the first payload.
            ulong gapLength = baseOffset - dataStart;
            ulong totalLength = checked(baseOffset + payloadBytes);
            if (totalLength > (ulong)long.MaxValue)
            {
                throw new Bork2dException("The pack exceeds the supported file length.");
            }

            return new PackLayout(
                entries,
                storedPayloads,
                headerCompressed,
                (uint)header.Length,
                PackCompression.ComputeChecksum(header),
                (long)totalLength,
                (long)gapLength);
        }

        throw new Bork2dException(
            "The pack header layout did not stabilize; reduce the entry count or paths.");
    }

    /// <summary>
    /// Writes the complete pack to a stream and leaves the stream open.
    /// </summary>
    /// <param name="destination">The destination stream.</param>
    internal void WriteTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        Span<byte> prefix = stackalloc byte[PackFormat.PrefixLength];
        PackFormat.Magic.CopyTo(prefix);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[8..], PackFormat.CurrentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[12..], (uint)_headerCompressed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(prefix[16..], _headerUncompressedLength);
        BinaryPrimitives.WriteUInt64LittleEndian(prefix[20..], _headerChecksum);

        destination.Write(prefix);
        destination.Write(_headerCompressed);

        long remainingGap = _gapLength;
        Span<byte> zeroes = stackalloc byte[256];
        zeroes.Clear();
        while (remainingGap > 0)
        {
            int chunk = (int)Math.Min(zeroes.Length, remainingGap);
            destination.Write(zeroes[..chunk]);
            remainingGap -= chunk;
        }

        foreach (byte[] payload in _storedPayloads)
        {
            destination.Write(payload);
        }
    }

    /// <summary>
    /// Materializes the complete pack in one array.
    /// </summary>
    /// <returns>The pack bytes.</returns>
    /// <exception cref="Bork2dException">The pack exceeds <see cref="Array.MaxLength"/>.</exception>
    internal byte[] ToArray()
    {
        if (_totalLength > Array.MaxLength)
        {
            throw new Bork2dException(
                $"The pack is {_totalLength} bytes and exceeds Array.MaxLength; use Write(Stream).");
        }

        byte[] buffer = new byte[(int)_totalLength];
        using var stream = new MemoryStream(buffer, 0, buffer.Length, writable: true);
        WriteTo(stream);
        return buffer;
    }

    private static PackEntry[] CreateEntries(
        PackSourceEntry[] ordered,
        byte[][] storedPayloads,
        bool[] compressedFlags,
        ulong dataStart)
    {
        var entries = new PackEntry[ordered.Length];
        ulong offset = dataStart;

        for (int i = 0; i < ordered.Length; i++)
        {
            PackSourceEntry source = ordered[i];
            byte[] stored = storedPayloads[i];

            entries[i] = new PackEntry
            {
                Path = source.Path,
                IsRaw = source.IsRaw,
                IsCompressed = compressedFlags[i],
                Offset = offset,
                Length = (uint)stored.Length,
                Size = (uint)source.Payload.Length,
            };

            offset = checked(offset + (ulong)stored.Length);
        }

        return entries;
    }
}
