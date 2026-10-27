using System.IO.Compression;
using System.IO.Hashing;

namespace Bork2d.Engine.Assets.Pack;

/// <summary>
/// Internal Zstd and XXH3-64 helpers shared by the pack reader and writer.
/// </summary>
/// <remarks>
/// The header and every compressed payload use maximum-quality Zstd with no checksum. No Zstd type
/// appears in a public signature.
/// </remarks>
internal static class PackCompression
{
    /// <summary>
    /// Compresses a buffer with maximum-quality Zstd and no checksum.
    /// </summary>
    /// <param name="data">The uncompressed bytes.</param>
    /// <returns>The compressed bytes.</returns>
    internal static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZstandardStream(
            output,
            new ZstandardCompressionOptions
            {
                Quality = ZstandardCompressionOptions.MaxQuality,
                AppendChecksum = false,
            },
            leaveOpen: true))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>
    /// Wraps a bounded stored-payload stream in a Zstd decompressor.
    /// </summary>
    /// <param name="source">The bounded stored-payload stream.</param>
    /// <returns>A decompressing stream that owns <paramref name="source"/>.</returns>
    internal static Stream OpenDecompressionStream(Stream source)
        => new ZstandardStream(source, CompressionMode.Decompress, leaveOpen: false);

    /// <summary>
    /// Computes the XXH3-64 hash (seed <c>0</c>) of a buffer.
    /// </summary>
    /// <param name="data">The bytes to hash.</param>
    /// <returns>The 64-bit hash.</returns>
    internal static ulong ComputeChecksum(ReadOnlySpan<byte> data)
        => XxHash3.HashToUInt64(data, seed: 0);
}
