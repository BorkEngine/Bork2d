using System.Buffers.Binary;
using Bork2d.Engine;
using Bork2d.Engine.Assets.Pack;
using Xunit;

namespace Bork2d.Tests.Assets.Pack;

/// <summary>
/// Builds and patches in-memory packs for the pack-format tests. No binary fixture is committed.
/// </summary>
internal static class PackTestSupport
{
    internal const int PrefixLength = PackFormat.PrefixLength;

    internal static PackEntry Entry(
        string path,
        bool isRaw = true,
        bool isCompressed = false,
        ulong offset = 0,
        uint length = 0,
        uint size = 0)
        => new()
        {
            Path = path,
            IsRaw = isRaw,
            IsCompressed = isCompressed,
            Offset = offset,
            Length = length,
            Size = size,
        };

    /// <summary>
    /// Builds a pack whose entry offsets are produced by <paramref name="createEntries"/> for the
    /// final data start. Payload bytes are appended in order; entries may deliberately claim
    /// offsets that do not match the appended bytes.
    /// </summary>
    internal static byte[] BuildPack(Func<ulong, PackEntry[]> createEntries, params byte[][] payloads)
    {
        ulong dataStart = PrefixLength;
        byte[] header = [];
        byte[] compressedHeader = [];
        bool stabilized = false;

        for (int attempt = 0; attempt < 64; attempt++)
        {
            PackEntry[] entries = createEntries(dataStart);
            header = PackSerialization.SerializeHeader(new PackHeader { Entries = entries });
            compressedHeader = PackCompression.Compress(header);
            ulong nextDataStart = (ulong)(PrefixLength + compressedHeader.Length);
            if (nextDataStart == dataStart)
            {
                stabilized = true;
                break;
            }

            dataStart = nextDataStart;
        }

        Assert.True(stabilized, "The test pack header layout did not stabilize.");

        int payloadLength = payloads.Sum(payload => payload.Length);
        byte[] pack = new byte[checked((int)dataStart + payloadLength)];
        WritePrefix(
            pack,
            compressedHeader.Length,
            header.Length,
            PackCompression.ComputeChecksum(header));
        compressedHeader.CopyTo(pack, PrefixLength);

        int offset = (int)dataStart;
        foreach (byte[] payload in payloads)
        {
            payload.CopyTo(pack, offset);
            offset += payload.Length;
        }

        return pack;
    }

    internal static byte[] BuildFromHeader(byte[] header)
    {
        byte[] compressedHeader = PackCompression.Compress(header);
        byte[] pack = new byte[PrefixLength + compressedHeader.Length];
        WritePrefix(pack, compressedHeader.Length, header.Length, PackCompression.ComputeChecksum(header));
        compressedHeader.CopyTo(pack, PrefixLength);
        return pack;
    }

    internal static uint HeaderCompressedLength(byte[] pack)
        => BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(12, 4));

    internal static uint HeaderUncompressedLength(byte[] pack)
        => BinaryPrimitives.ReadUInt32LittleEndian(pack.AsSpan(16, 4));

    internal static ulong HeaderChecksum(byte[] pack)
        => BinaryPrimitives.ReadUInt64LittleEndian(pack.AsSpan(20, 8));

    internal static int DataStart(byte[] pack) => PrefixLength + (int)HeaderCompressedLength(pack);

    internal static byte[] PatchUInt32(byte[] pack, int offset, uint value)
    {
        byte[] copy = pack.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(offset, 4), value);
        return copy;
    }

    internal static byte[] PatchUInt64(byte[] pack, int offset, ulong value)
    {
        byte[] copy = pack.ToArray();
        BinaryPrimitives.WriteUInt64LittleEndian(copy.AsSpan(offset, 8), value);
        return copy;
    }

    internal static byte[] PatchBytes(byte[] pack, int offset, ReadOnlySpan<byte> value)
    {
        byte[] copy = pack.ToArray();
        value.CopyTo(copy.AsSpan(offset));
        return copy;
    }

    internal static byte[] Truncate(byte[] pack, int length) => pack[..length];

    internal static void AssertInvalidPack(byte[] pack)
    {
        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => PackArchive.Open(new MemoryStream(pack, writable: false)));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    private static void WritePrefix(
        byte[] pack,
        int compressedLength,
        int uncompressedLength,
        ulong checksum)
    {
        PackFormat.Magic.CopyTo(pack);
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(8, 4), PackFormat.CurrentVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(12, 4), (uint)compressedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(pack.AsSpan(16, 4), (uint)uncompressedLength);
        BinaryPrimitives.WriteUInt64LittleEndian(pack.AsSpan(20, 8), checksum);
    }
}
