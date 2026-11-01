using System.Numerics;
using System.Runtime.CompilerServices;

namespace Bork2d.Engine.Assets;

/// <summary>
/// The immutable render-side copy of a <see cref="SpriteAsset"/>'s draw fields.
/// </summary>
/// <remarks>
/// <para>
/// A sprite asset is a mutable authoring and loading DTO. Assigning it to a renderable freezes
/// these fields once, so mutating the asset afterwards — including its arrays — cannot change a
/// captured snapshot or a planned frame, even one already handed to the render thread. The copy
/// holds a texture reference, the per-axis world scale, the normalized pivot, the topology, the
/// four UVs, and the optional indices; the UV and index arrays are cloned so later writes through
/// the asset's arrays cannot reach the frozen copy.
/// </para>
/// <para>
/// Freezing is idempotent per asset instance and allocation-free after the first call: the
/// <see cref="ConditionalWeakTable{TKey, TValue}"/> cache keeps one frozen copy per live asset
/// without a serialized or public cache member, and the weak key lets an unreferenced asset and
/// its copy be collected together. Freezing is thread-safe: concurrent callers for the same asset
/// observe the same published instance.
/// </para>
/// </remarks>
internal sealed class SpriteRenderData
{
    private static readonly ConditionalWeakTable<SpriteAsset, SpriteRenderData> _frozen = new();

    private SpriteRenderData(SpriteAsset asset)
    {
        Texture = asset.Texture;
        Scale = asset.Scale;
        Pivot = asset.Pivot;
        Topology = asset.Topology;
        Uvs = asset.Uvs is { } uvs ? (Vector2[])uvs.Clone() : null;
        Indices = asset.Indices is { } indices ? (ushort[])indices.Clone() : null;
    }

    /// <summary>Gets the referenced texture descriptor, or an absent reference for no texture.</summary>
    internal AssetRef<TextureDescriptor> Texture { get; }

    /// <summary>Gets the per-axis world scale in world units per normalized UV span.</summary>
    internal Vector2 Scale { get; }

    /// <summary>Gets the normalized pivot inside the quad.</summary>
    internal Vector2 Pivot { get; }

    /// <summary>Gets the topology stored in the pack.</summary>
    internal SpriteTopology Topology { get; }

    /// <summary>Gets the frozen UV array, or <see langword="null"/> when the asset had none.</summary>
    internal Vector2[]? Uvs { get; }

    /// <summary>Gets the frozen index array, or <see langword="null"/> when the asset had none.</summary>
    internal ushort[]? Indices { get; }

    /// <summary>
    /// Returns the frozen copy for <paramref name="asset"/>, creating it on first use.
    /// </summary>
    /// <param name="asset">The mutable asset to freeze.</param>
    /// <returns>The single frozen copy published for this asset instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="asset"/> is <see langword="null"/>.</exception>
    internal static SpriteRenderData Freeze(SpriteAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _frozen.GetValue(asset, static key => new SpriteRenderData(key));
    }

    /// <summary>
    /// Returns the frozen copy for <paramref name="asset"/>, or <see langword="null"/> when the
    /// asset is <see langword="null"/>.
    /// </summary>
    /// <param name="asset">The mutable asset to freeze, or <see langword="null"/>.</param>
    /// <returns>The frozen copy, or <see langword="null"/>.</returns>
    internal static SpriteRenderData? FreezeOrNull(SpriteAsset? asset)
        => asset is null ? null : Freeze(asset);
}
