using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Engine.Assets.Vfs;

/// <summary>
/// A winning asset entry in the merged filesystem: its header fields, the pack identifier that
/// supplied it, and the owning archive.
/// </summary>
/// <remarks>
/// Instances are created by <see cref="AssetFileSystem"/> and remain valid after a later mount
/// because mounted packs are never unloaded. They become unusable when the owning
/// <see cref="AssetFileSystem"/> is disposed; the read methods then throw
/// <see cref="ObjectDisposedException"/>. Resolve and keep an instance when a request must not see
/// a later mount.
/// </remarks>
internal sealed class ResolvedAsset
{
    private readonly PackArchive _archive;
    private readonly PackEntry _entry;

    internal ResolvedAsset(
        string path,
        bool isRaw,
        bool isCompressed,
        long length,
        long size,
        string packPath,
        PackArchive archive,
        PackEntry entry)
    {
        Path = path;
        IsRaw = isRaw;
        IsCompressed = isCompressed;
        Length = length;
        Size = size;
        PackPath = packPath;
        _archive = archive;
        _entry = entry;
    }

    /// <summary>Gets the canonical asset path.</summary>
    internal string Path { get; }

    /// <summary>Gets a value indicating whether the payload is opaque raw bytes.</summary>
    internal bool IsRaw { get; }

    /// <summary>Gets a value indicating whether the stored payload is Zstd-compressed.</summary>
    internal bool IsCompressed { get; }

    /// <summary>Gets the stored byte count.</summary>
    internal long Length { get; }

    /// <summary>Gets the byte count after decompression.</summary>
    internal long Size { get; }

    /// <summary>Gets the pack identifier as supplied when the pack was mounted.</summary>
    internal string PackPath { get; }

    /// <summary>Gets the resolved filesystem path of the owning pack.</summary>
    internal string ResolvedPackPath => _archive.DisplayPath;

    /// <summary>Opens a stream over the stored payload (still compressed when applicable).</summary>
    /// <returns>A bounded read-only stream over the stored bytes.</returns>
    internal Stream OpenStoredStream() => _archive.OpenStoredStream(_entry);

    /// <summary>Reads the stored payload bytes (still compressed when applicable).</summary>
    /// <returns>A fresh array containing the stored bytes.</returns>
    internal byte[] ReadStoredBytes() => _archive.ReadStoredBytes(_entry);

    /// <summary>Opens a stream that decompresses the payload and enforces the declared size.</summary>
    /// <returns>A read-only stream over the decompressed payload.</returns>
    internal Stream OpenDecompressedStream() => _archive.OpenDecompressedStream(_entry);

    /// <summary>Decompresses the whole payload and enforces the declared size.</summary>
    /// <returns>A fresh array containing the decompressed payload.</returns>
    internal byte[] ReadDecompressedBytes() => _archive.ReadDecompressedBytes(_entry);
}
