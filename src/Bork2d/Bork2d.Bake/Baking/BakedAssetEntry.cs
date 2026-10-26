using Bork2d.Api.Assets;
using Bork2d.Bake.Sources;
using Bork2d.Bake.Textures;

namespace Bork2d.Bake.Baking;

/// <summary>
/// One entry of the complete baked asset set: its canonical path, kind, baked type name, and the
/// source object that produces it.
/// </summary>
/// <remarks>
/// The index is built before anything is added to <c>PackBuilder</c>, so reference validation and
/// collision detection see the whole bake in one deterministic, sorted structure. A MessagePack entry
/// records the full name of the type the host serializes; a raw entry has no type contract and
/// <see cref="TypeName"/> is <see langword="null"/>.
/// </remarks>
internal sealed class BakedAssetEntry
{
    private BakedAssetEntry(
        string path,
        bool isRaw,
        string? typeName,
        BakedAssetEntryKind kind,
        BoundAsset? asset,
        BakedTexture? texture)
    {
        Path = path;
        IsRaw = isRaw;
        TypeName = typeName;
        Kind = kind;
        Asset = asset;
        Texture = texture;
    }

    /// <summary>Gets the canonical asset path.</summary>
    internal string Path { get; }

    /// <summary>Gets a value indicating whether the entry is a raw payload.</summary>
    internal bool IsRaw { get; }

    /// <summary>Gets the baked MessagePack type's full name, or <see langword="null"/> for a raw entry.</summary>
    internal string? TypeName { get; }

    /// <summary>Gets the pipeline that produced the entry.</summary>
    internal BakedAssetEntryKind Kind { get; }

    /// <summary>Gets the bound custom-data asset, or <see langword="null"/> for a texture entry.</summary>
    internal BoundAsset? Asset { get; }

    /// <summary>Gets the baked texture, or <see langword="null"/> for a custom-data entry.</summary>
    internal BakedTexture? Texture { get; }

    /// <summary>Creates the MessagePack entry of a bound custom-data asset.</summary>
    /// <param name="asset">The bound asset.</param>
    /// <returns>The index entry.</returns>
    internal static BakedAssetEntry ForCustomData(BoundAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return new BakedAssetEntry(
            asset.AssetPath,
            isRaw: false,
            asset.TypeName,
            BakedAssetEntryKind.CustomData,
            asset,
            texture: null);
    }

    /// <summary>Creates the MessagePack descriptor entry of a baked texture.</summary>
    /// <param name="texture">The baked texture.</param>
    /// <returns>The index entry.</returns>
    internal static BakedAssetEntry ForTextureDescriptor(BakedTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return new BakedAssetEntry(
            texture.AssetPath,
            isRaw: false,
            typeof(TextureDescriptor).FullName,
            BakedAssetEntryKind.TextureDescriptor,
            asset: null,
            texture);
    }

    /// <summary>Creates the raw payload entry of a baked texture.</summary>
    /// <param name="texture">The baked texture.</param>
    /// <returns>The index entry.</returns>
    internal static BakedAssetEntry ForTexturePayload(BakedTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        return new BakedAssetEntry(
            texture.PayloadPath,
            isRaw: true,
            typeName: null,
            BakedAssetEntryKind.TexturePayload,
            asset: null,
            texture);
    }
}
