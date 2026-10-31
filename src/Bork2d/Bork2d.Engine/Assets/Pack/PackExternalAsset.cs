namespace Bork2d.Engine.Assets.Pack;

/// <summary>
/// An asset that is not written into the pack being built but is available to its references.
/// </summary>
/// <remarks>
/// <para>
/// This is the narrow tooling seam for multi-pack bakes (D-M4-14): a DLC pack may reference an asset
/// that lives in the main/base pack. The generated baker passes the main pack's entries here; the
/// on-disk pack format does not change and the runtime still resolves the reference against the
/// stack of mounted packs.
/// </para>
/// <para>
/// The type is internal, deliberately not a public API. The main pack must never supply external
/// assets, so its references stay strictly within its own entries.
/// </para>
/// </remarks>
/// <param name="Path">The canonical external asset path.</param>
/// <param name="IsRaw">Whether the external asset is a raw payload rather than a MessagePack asset.</param>
internal readonly record struct PackExternalAsset(string Path, bool IsRaw);
