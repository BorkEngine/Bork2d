using System.Buffers.Binary;
using Bork2d.Bake.Textures;

namespace Bork2d.Tests.Bake.Textures;

/// <summary>
/// An independent parser for the narrow KTX2 profile the bake writes.
/// </summary>
/// <remarks>
/// The parser exists so writer and processor tests assert the bytes a runtime KTX2 reader would read,
/// not the writer's own intermediate state. It validates the identifier and reads the header, the
/// level index, and the Data Format Descriptor without sharing any writer code.
/// </remarks>
internal sealed class Ktx2TestDocument
{
    /// <summary>The 12-byte KTX2 identifier.</summary>
    internal static readonly byte[] Identifier =
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

    private const int HeaderSize = 80;
    private const int LevelIndexEntrySize = 24;

    private readonly byte[] _bytes;

    private Ktx2TestDocument(byte[] bytes)
    {
        _bytes = bytes;

        VkFormat = ReadUInt32(12);
        TypeSize = ReadUInt32(16);
        Width = ReadUInt32(20);
        Height = ReadUInt32(24);
        Depth = ReadUInt32(28);
        LayerCount = ReadUInt32(32);
        FaceCount = ReadUInt32(36);
        LevelCount = ReadUInt32(40);
        SupercompressionScheme = ReadUInt32(44);
        DfdByteOffset = ReadUInt32(48);
        DfdByteLength = ReadUInt32(52);
        KvdByteOffset = ReadUInt32(56);
        KvdByteLength = ReadUInt32(60);
        SgdByteOffset = ReadUInt64(64);
        SgdByteLength = ReadUInt64(72);

        var levels = new Ktx2TestLevel[LevelCount];
        for (int level = 0; level < levels.Length; level++)
        {
            int entry = HeaderSize + (level * LevelIndexEntrySize);
            levels[level] = new Ktx2TestLevel(
                ReadUInt64(entry),
                ReadUInt64(entry + 8),
                ReadUInt64(entry + 16));
        }

        Levels = levels;

        DfdTotalSize = ReadUInt32((int)DfdByteOffset);
        DfdVersionNumber = ReadUInt32((int)DfdByteOffset + 8) & 0xFFFFu;
        DfdDescriptorBlockSize = ReadUInt32((int)DfdByteOffset + 8) >> 16;
        uint descriptorWords = ReadUInt32((int)DfdByteOffset + 12);
        DfdColorModel = (byte)descriptorWords;
        DfdPrimaries = (byte)(descriptorWords >> 8);
        DfdTransfer = (byte)(descriptorWords >> 16);
        DfdFlags = (byte)(descriptorWords >> 24);
        uint dimensions = ReadUInt32((int)DfdByteOffset + 16);
        DfdTexelBlockWidth = (int)(dimensions & 0xFF) + 1;
        DfdTexelBlockHeight = (int)((dimensions >> 8) & 0xFF) + 1;
        DfdBytesPlane0 = (int)ReadUInt32((int)DfdByteOffset + 20) & 0xFF;
        DfdBytesPlane1 = (int)(ReadUInt32((int)DfdByteOffset + 20) >> 8) & 0xFF;

        int sampleCount = ((int)DfdDescriptorBlockSize - 24) / 16;
        var samples = new DfdSample[sampleCount];
        for (int sample = 0; sample < sampleCount; sample++)
        {
            int offset = (int)DfdByteOffset + 28 + (sample * 16);
            samples[sample] = new DfdSample(
                ReadUInt16(offset),
                (byte)(_bytes[offset + 2] + 1),
                _bytes[offset + 3],
                ReadUInt32(offset + 8),
                ReadUInt32(offset + 12));
        }

        Samples = samples;
    }

    /// <summary>Gets the complete file bytes.</summary>
    internal byte[] Bytes => _bytes;

    /// <summary>Gets the Vulkan format value.</summary>
    internal uint VkFormat { get; }

    /// <summary>Gets the type size.</summary>
    internal uint TypeSize { get; }

    /// <summary>Gets the base-level pixel width.</summary>
    internal uint Width { get; }

    /// <summary>Gets the base-level pixel height.</summary>
    internal uint Height { get; }

    /// <summary>Gets the depth.</summary>
    internal uint Depth { get; }

    /// <summary>Gets the layer count.</summary>
    internal uint LayerCount { get; }

    /// <summary>Gets the face count.</summary>
    internal uint FaceCount { get; }

    /// <summary>Gets the level count.</summary>
    internal uint LevelCount { get; }

    /// <summary>Gets the supercompression scheme.</summary>
    internal uint SupercompressionScheme { get; }

    /// <summary>Gets the DFD byte offset.</summary>
    internal uint DfdByteOffset { get; }

    /// <summary>Gets the DFD byte length.</summary>
    internal uint DfdByteLength { get; }

    /// <summary>Gets the key/value data byte offset.</summary>
    internal uint KvdByteOffset { get; }

    /// <summary>Gets the key/value data byte length.</summary>
    internal uint KvdByteLength { get; }

    /// <summary>Gets the supercompression global data byte offset.</summary>
    internal ulong SgdByteOffset { get; }

    /// <summary>Gets the supercompression global data byte length.</summary>
    internal ulong SgdByteLength { get; }

    /// <summary>Gets the level index entries, base level first.</summary>
    internal IReadOnlyList<Ktx2TestLevel> Levels { get; }

    /// <summary>Gets the DFD total size, including its size field.</summary>
    internal uint DfdTotalSize { get; }

    /// <summary>Gets the DFD version number.</summary>
    internal uint DfdVersionNumber { get; }

    /// <summary>Gets the DFD descriptor block size.</summary>
    internal uint DfdDescriptorBlockSize { get; }

    /// <summary>Gets the DFD color model.</summary>
    internal byte DfdColorModel { get; }

    /// <summary>Gets the DFD color primaries.</summary>
    internal byte DfdPrimaries { get; }

    /// <summary>Gets the DFD transfer function.</summary>
    internal byte DfdTransfer { get; }

    /// <summary>Gets the DFD flags.</summary>
    internal byte DfdFlags { get; }

    /// <summary>Gets the DFD texel block width in pixels.</summary>
    internal int DfdTexelBlockWidth { get; }

    /// <summary>Gets the DFD texel block height in pixels.</summary>
    internal int DfdTexelBlockHeight { get; }

    /// <summary>Gets bytesPlane0 from the DFD.</summary>
    internal int DfdBytesPlane0 { get; }

    /// <summary>Gets bytesPlane1 from the DFD.</summary>
    internal int DfdBytesPlane1 { get; }

    /// <summary>Gets the DFD samples.</summary>
    internal IReadOnlyList<DfdSample> Samples { get; }

    /// <summary>Parses one KTX2 payload.</summary>
    /// <param name="bytes">The complete file bytes.</param>
    /// <returns>The parsed document.</returns>
    /// <exception cref="InvalidOperationException">The identifier or header is malformed.</exception>
    internal static Ktx2TestDocument Parse(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length < HeaderSize)
        {
            throw new InvalidOperationException(
                $"The KTX2 payload is {bytes.Length} bytes; the header needs {HeaderSize}.");
        }

        if (!bytes.AsSpan(0, Identifier.Length).SequenceEqual(Identifier))
        {
            throw new InvalidOperationException("The KTX2 identifier is missing.");
        }

        return new Ktx2TestDocument(bytes);
    }

    /// <summary>Gets one level's stored bytes.</summary>
    /// <param name="level">The zero-based level index.</param>
    /// <returns>The level bytes, without padding.</returns>
    internal byte[] GetLevelBytes(int level)
    {
        Ktx2TestLevel entry = Levels[level];
        return _bytes.AsSpan((int)entry.ByteOffset, (int)entry.ByteLength).ToArray();
    }

    private ushort ReadUInt16(int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(_bytes.AsSpan(offset));

    private uint ReadUInt32(int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(offset));

    private ulong ReadUInt64(int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(_bytes.AsSpan(offset));
}

/// <summary>
/// One parsed KTX2 level index entry.
/// </summary>
/// <param name="ByteOffset">The level data's absolute file offset.</param>
/// <param name="ByteLength">The stored byte length.</param>
/// <param name="UncompressedByteLength">The uncompressed byte length.</param>
internal readonly record struct Ktx2TestLevel(
    ulong ByteOffset,
    ulong ByteLength,
    ulong UncompressedByteLength);
