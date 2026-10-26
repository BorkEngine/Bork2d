namespace Bork2d.Assets.Vfs;

/// <summary>
/// Describes a pack that was just mounted: its supplied identifier, resolved path, version, and
/// entry statistics.
/// </summary>
/// <remarks>
/// The value is produced by <see cref="AssetFileSystem.LoadPack(string)"/> and used for the
/// start/overlay log events. It is not part of the public surface.
/// </remarks>
internal readonly record struct PackMountInfo(
    string SuppliedPath,
    string ResolvedPath,
    uint Version,
    int EntryCount,
    long StoredBytes);
