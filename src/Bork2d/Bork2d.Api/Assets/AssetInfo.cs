namespace Bork2d.Api.Assets;

/// <summary>
/// Describes one visible asset entry in the merged pack filesystem.
/// </summary>
/// <param name="Path">The canonical asset path.</param>
/// <param name="IsRaw">
/// <see langword="true"/> for opaque bytes; <see langword="false"/> for a MessagePack asset.
/// </param>
/// <param name="IsCompressed">Whether the stored payload is Zstd-compressed.</param>
/// <param name="Length">
/// The stored byte count (the compressed length when <paramref name="IsCompressed"/> is
/// <see langword="true"/>).
/// </param>
/// <param name="Size">
/// The byte count after decompression; equal to <paramref name="Length"/> when the payload is not
/// compressed.
/// </param>
/// <param name="PackPath">
/// The pack identifier as supplied when it was mounted (the <c>WithAssetsPack</c> value or the
/// <c>LoadPack</c> argument), not the resolved absolute filesystem path.
/// </param>
/// <remarks>
/// Returned by <see cref="IAssets.Browse(string?)"/>. The fields come from the pack header, so
/// browsing never decompresses a payload. <see cref="Length"/> and <see cref="Size"/> are
/// <see cref="long"/> even though the v1 header stores 32-bit lengths, which leaves room for a
/// future pack version and avoids unsigned arithmetic in game code.
/// </remarks>
public readonly record struct AssetInfo(
    string Path,
    bool IsRaw,
    bool IsCompressed,
    long Length,
    long Size,
    string PackPath);
