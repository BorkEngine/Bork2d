namespace Bork2d.Assets.Pack;

/// <summary>
/// One reference found in a MessagePack asset that is waiting to be added to a pack.
/// </summary>
/// <param name="SourcePath">The canonical path of the asset that contains the reference.</param>
/// <param name="PropertyPath">The property path inside <paramref name="SourcePath"/>.</param>
/// <param name="Path">The referenced path, or <see langword="null"/> for an absent reference.</param>
/// <param name="IsData">Whether the reference targets raw data rather than a MessagePack asset.</param>
internal readonly record struct AssetReferenceInfo(
    string SourcePath,
    string PropertyPath,
    string? Path,
    bool IsData);
