using System.Buffers.Binary;
using System.Text;

namespace Bork2d.Bake.Textures;

/// <summary>
/// Writes the narrow KTX2 2.0 profile the bake produces.
/// </summary>
/// <remarks>
/// <para>
/// One file is one 2D image with <c>layerCount = 0</c> (one layer), <c>faceCount = 1</c>, no
/// supercompression, an empty key/value and supercompression-global-data section, and a complete mip
/// chain. The file layout is the specification's order: identifier, header, level index, Data Format
/// Descriptor, then the mip level data from the smallest level to the base level. Each level starts
/// on an <c>lcm(texelBlockSize, 4)</c> boundary with zero-filled padding; the level index lists the
/// base level first and points at each level's actual offset.
/// </para>
/// <para>
/// The Data Format Descriptor describes linear UNORM data: transfer function
/// <c>KHR_DF_TRANSFER_LINEAR</c>, BT.709 primaries, straight alpha, and 8-bit or block samples with
/// the bounds the KTX2 specification uses. All integers are little-endian.
/// </para>
/// </remarks>
internal static class Ktx2Writer
{
    private const int HeaderSize = 80;
    private const int LevelIndexEntrySize = 24;
    private const int DfdHeaderSize = 4;
    private const int DfdBasicBlockSize = 24;
    private const int DfdSampleSize = 16;
    private const uint DfdVersionNumber = 2;
    private const byte DfdTransferLinear = 1;
    private const byte DfdPrimariesBt709 = 1;
    private const byte DfdFlagsAlphaStraight = 0;

    private static readonly byte[] _identifier =
    [
        0xAB,
        0x4B,
        0x54,
        0x58,
        0x20,
        0x32,
        0x30,
        0xBB,
        0x0D,
        0x0A,
        0x1A,
        0x0A,
    ];

    /// <summary>Writes a complete KTX2 payload.</summary>
    /// <param name="format">The metadata of the stored format.</param>
    /// <param name="width">The base-level pixel width.</param>
    /// <param name="height">The base-level pixel height.</param>
    /// <param name="levels">
    /// The stored bytes of every mip level, base level first. Each level must already be in its final
    /// stored layout and exactly the length the format requires.
    /// </param>
    /// <returns>The complete KTX2 file bytes.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A base dimension is not positive.</exception>
    /// <exception cref="InvalidOperationException">
    /// The level count is not the full chain or a level has the wrong stored length.
    /// </exception>
    internal static byte[] Write(
        TextureFormatInfo format,
        int width,
        int height,
        IReadOnlyList<byte[]> levels)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(levels);

        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "The width must be positive.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "The height must be positive.");
        }

        int expectedLevelCount = TextureMipChain.LevelCount(width, height);
        if (levels.Count != expectedLevelCount)
        {
            throw new InvalidOperationException(
                $"The KTX2 payload requires {expectedLevelCount} mip levels for "
                    + $"{width}x{height}; received {levels.Count}.");
        }

        var levelLengths = new int[expectedLevelCount];
        for (int level = 0; level < expectedLevelCount; level++)
        {
            byte[] data = levels[level]
                ?? throw new ArgumentNullException(nameof(levels), "A mip level is null.");
            int levelWidth = TextureMipChain.LevelWidth(width, level);
            int levelHeight = TextureMipChain.LevelHeight(height, level);
            int expectedLength = format.GetLevelByteLength(levelWidth, levelHeight);
            if (data.Length != expectedLength)
            {
                throw new InvalidOperationException(
                    $"Mip level {level} of a {format.Format} texture is {data.Length} bytes; "
                        + $"expected {expectedLength} for {levelWidth}x{levelHeight}.");
            }

            levelLengths[level] = data.Length;
        }

        byte[] dfd = BuildDfd(format);
        int dfdByteOffset = HeaderSize + (LevelIndexEntrySize * expectedLevelCount);
        int alignment = format.RequiredAlignment;
        var levelOffsets = new int[expectedLevelCount];
        int cursor = Align(dfdByteOffset + dfd.Length, alignment);
        for (int level = expectedLevelCount - 1; level >= 0; level--)
        {
            cursor = Align(cursor, alignment);
            levelOffsets[level] = cursor;
            cursor = checked(cursor + levelLengths[level]);
        }

        int totalLength = cursor;
        using var stream = new MemoryStream(totalLength);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(_identifier);
        writer.Write(format.VkFormat);
        writer.Write(1u);
        writer.Write((uint)width);
        writer.Write((uint)height);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(1u);
        writer.Write((uint)expectedLevelCount);
        writer.Write(0u);
        writer.Write((uint)dfdByteOffset);
        writer.Write((uint)dfd.Length);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0ul);
        writer.Write(0ul);

        for (int level = 0; level < expectedLevelCount; level++)
        {
            writer.Write((ulong)levelOffsets[level]);
            writer.Write((ulong)levelLengths[level]);
            writer.Write((ulong)levelLengths[level]);
        }

        writer.Write(dfd);
        for (int level = expectedLevelCount - 1; level >= 0; level--)
        {
            WritePadding(writer, alignment);
            if (stream.Position != levelOffsets[level])
            {
                throw new InvalidOperationException(
                    "The KTX2 level offset invariant was violated while writing the payload.");
            }

            writer.Write(levels[level]);
        }

        if (stream.Length != totalLength)
        {
            throw new InvalidOperationException(
                "The KTX2 payload length invariant was violated while writing the payload.");
        }

        return stream.ToArray();
    }

    private static byte[] BuildDfd(TextureFormatInfo format)
    {
        int sampleCount = format.Samples.Count;
        int descriptorBlockSize = DfdBasicBlockSize + (DfdSampleSize * sampleCount);
        int totalSize = DfdHeaderSize + descriptorBlockSize;
        var dfd = new byte[totalSize];

        BinaryPrimitives.WriteUInt32LittleEndian(dfd.AsSpan(0), (uint)totalSize);
        BinaryPrimitives.WriteUInt32LittleEndian(dfd.AsSpan(4), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(
            dfd.AsSpan(8),
            ((uint)descriptorBlockSize << 16) | DfdVersionNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(
            dfd.AsSpan(12),
            (uint)((DfdFlagsAlphaStraight << 24)
                | (DfdTransferLinear << 16)
                | (DfdPrimariesBt709 << 8)
                | format.ColorModel));
        BinaryPrimitives.WriteUInt32LittleEndian(
            dfd.AsSpan(16),
            (uint)((format.BlockWidth - 1) | ((format.BlockHeight - 1) << 8)));
        BinaryPrimitives.WriteUInt32LittleEndian(dfd.AsSpan(20), (uint)format.BytesPerBlock);
        BinaryPrimitives.WriteUInt32LittleEndian(dfd.AsSpan(24), 0u);

        int offset = DfdHeaderSize + DfdBasicBlockSize;
        foreach (DfdSample sample in format.Samples)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(dfd.AsSpan(offset), sample.BitOffset);
            dfd[offset + 2] = (byte)(sample.BitLength - 1);
            dfd[offset + 3] = sample.ChannelType;
            BinaryPrimitives.WriteUInt32LittleEndian(dfd.AsSpan(offset + 8), sample.Lower);
            BinaryPrimitives.WriteUInt32LittleEndian(dfd.AsSpan(offset + 12), sample.Upper);
            offset += DfdSampleSize;
        }

        return dfd;
    }

    private static void WritePadding(BinaryWriter writer, int alignment)
    {
        long position = writer.BaseStream.Position;
        long remainder = position % alignment;
        if (remainder == 0)
        {
            return;
        }

        for (long index = remainder; index < alignment; index++)
        {
            writer.Write((byte)0);
        }
    }

    private static int Align(int value, int alignment)
        => checked(((value + alignment - 1) / alignment) * alignment);
}
