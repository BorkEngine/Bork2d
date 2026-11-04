using System.Buffers.Binary;
using Bork2d.Engine.Assets;

namespace Bork2d.Render.Textures;

/// <summary>
/// Parses and validates the narrow KTX2 2.0 profile the bake writes.
/// </summary>
/// <remarks>
/// <para>
/// The accepted profile is one 2D image with <c>pixelDepth = 0</c>, <c>layerCount = 0</c> (one
/// layer), <c>faceCount = 1</c>, no supercompression, an absent key/value and
/// supercompression-global-data section, and a complete mip chain with the base level first in the
/// level index. The header's <c>vkFormat</c>, base dimensions, and level count must match the
/// descriptor, and every level must have the stored length the format requires and start on an
/// <c>lcm(texelBlockSize, 4)</c> boundary.
/// </para>
/// <para>
/// A malformed or mismatched payload is a <see cref="TexturePageFailure"/> content failure, never an
/// exception: the page store fails that path and draws the placeholder. The renderer has no partial
/// parse, no repair, and no fallback format.
/// </para>
/// </remarks>
internal static class Ktx2Reader
{
    /// <summary>The fixed KTX2 2.0 header size in bytes.</summary>
    internal const int HeaderSize = 80;

    /// <summary>The size of one level index entry in bytes.</summary>
    internal const int LevelIndexEntrySize = 24;

    private const int IdentifierSize = 12;
    private const int MinimumDfdLength = 4;

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

    /// <summary>
    /// Parses a KTX2 payload and validates it against the descriptor.
    /// </summary>
    /// <param name="payload">The complete KTX2 file bytes.</param>
    /// <param name="descriptor">The descriptor whose format, dimensions, and mip count must match.</param>
    /// <param name="texture">The parsed texture when the payload is valid.</param>
    /// <param name="failure">
    /// <see cref="TexturePageFailure.None"/> when valid; otherwise the content failure.
    /// </param>
    /// <returns><see langword="true"/> when the payload is a valid narrow-profile KTX2 file.</returns>
    internal static bool TryParse(
        ReadOnlySpan<byte> payload,
        TextureDescriptor descriptor,
        out Ktx2Texture texture,
        out TexturePageFailure failure)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        texture = null!;

        if (payload.Length < HeaderSize
            || !payload[..IdentifierSize].SequenceEqual(_identifier))
        {
            failure = TexturePageFailure.MalformedKtx2;
            return false;
        }

        RenderTextureFormat? format = RenderTextureFormat.TryGet(descriptor.Format);
        if (format is null)
        {
            failure = TexturePageFailure.UnsupportedFormat;
            return false;
        }

        uint vkFormat = ReadUInt32(payload, 12);
        if (vkFormat != format.VkFormat)
        {
            failure = TexturePageFailure.FormatMismatch;
            return false;
        }

        if (ReadUInt32(payload, 16) != 1)
        {
            failure = TexturePageFailure.MalformedKtx2;
            return false;
        }

        uint width = ReadUInt32(payload, 20);
        uint height = ReadUInt32(payload, 24);
        if (width != descriptor.Width || height != descriptor.Height)
        {
            failure = TexturePageFailure.DimensionMismatch;
            return false;
        }

        uint levelCount = ReadUInt32(payload, 40);
        if (levelCount != descriptor.MipLevels || levelCount != FullMipCount(width, height))
        {
            failure = TexturePageFailure.MipMismatch;
            return false;
        }

        if (ReadUInt32(payload, 28) != 0
            || ReadUInt32(payload, 32) != 0
            || ReadUInt32(payload, 36) != 1
            || ReadUInt32(payload, 44) != 0
            || ReadUInt32(payload, 56) != 0
            || ReadUInt32(payload, 60) != 0
            || ReadUInt64(payload, 64) != 0
            || ReadUInt64(payload, 72) != 0)
        {
            failure = TexturePageFailure.MalformedKtx2;
            return false;
        }

        uint dfdByteOffset = ReadUInt32(payload, 48);
        uint dfdByteLength = ReadUInt32(payload, 52);
        if (dfdByteOffset < HeaderSize
            || dfdByteLength < MinimumDfdLength
            || !IsInside(payload.Length, dfdByteOffset, dfdByteLength)
            || ReadUInt32(payload, (int)dfdByteOffset) != dfdByteLength)
        {
            failure = TexturePageFailure.MalformedKtx2;
            return false;
        }

        long levelIndexEnd = HeaderSize + ((long)levelCount * LevelIndexEntrySize);
        if (levelIndexEnd > payload.Length)
        {
            failure = TexturePageFailure.MalformedKtx2;
            return false;
        }

        var levels = new Ktx2Level[levelCount];
        for (var level = 0; level < levels.Length; level++)
        {
            int entry = HeaderSize + (level * LevelIndexEntrySize);
            ulong byteOffset = ReadUInt64(payload, entry);
            ulong byteLength = ReadUInt64(payload, entry + 8);
            ulong uncompressedByteLength = ReadUInt64(payload, entry + 16);
            if (byteLength != uncompressedByteLength)
            {
                failure = TexturePageFailure.MalformedKtx2;
                return false;
            }

            int levelWidth = LevelExtent(width, level);
            int levelHeight = LevelExtent(height, level);
            if (byteLength != (ulong)format.GetLevelByteLength(levelWidth, levelHeight)
                || byteOffset % (ulong)format.RequiredAlignment != 0
                || !IsInside(payload.Length, byteOffset, byteLength))
            {
                failure = TexturePageFailure.MalformedKtx2;
                return false;
            }

            levels[level] = new Ktx2Level(byteOffset, byteLength);
        }

        texture = new Ktx2Texture(descriptor.Format, width, height, levels);
        failure = TexturePageFailure.None;
        return true;
    }

    /// <summary>Gets the full mip chain length of a base extent down to 1x1.</summary>
    /// <param name="width">The base width.</param>
    /// <param name="height">The base height.</param>
    /// <returns>The number of levels in a complete chain.</returns>
    internal static uint FullMipCount(uint width, uint height)
    {
        uint largest = Math.Max(width, height);
        uint count = 1;
        while (largest > 1)
        {
            largest >>= 1;
            count++;
        }

        return count;
    }

    /// <summary>Gets one level's pixel extent.</summary>
    /// <param name="baseExtent">The base-level width or height.</param>
    /// <param name="level">The zero-based level index.</param>
    /// <returns>The level's extent, at least one.</returns>
    internal static int LevelExtent(uint baseExtent, int level)
    {
        int shifted = (int)(baseExtent >> level);
        return Math.Max(1, shifted);
    }

    private static bool IsInside(int payloadLength, ulong byteOffset, ulong byteLength)
    {
        if (byteOffset > (ulong)payloadLength)
        {
            return false;
        }

        return byteLength <= (ulong)payloadLength - byteOffset;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt32LittleEndian(payload[offset..]);

    private static ulong ReadUInt64(ReadOnlySpan<byte> payload, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(payload[offset..]);
}
