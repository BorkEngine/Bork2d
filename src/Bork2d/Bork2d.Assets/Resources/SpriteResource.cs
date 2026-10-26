using Bork2d.Assets.Descriptors;

namespace Bork2d.Assets.Resources;

/// <summary>
/// The immutable CPU sprite resource: a validated sprite asset linked to its texture descriptor
/// resource, or a cached failure.
/// </summary>
/// <remarks>
/// There is exactly one resource per canonical sprite path for the process lifetime. A failed
/// resource is cached as failed and is never retried (D-M3-18). Sprite resources share the
/// per-texture descriptor resource, so multiple sprites referencing one texture share one
/// descriptor. Instances are immutable after publication and carry no GPU state.
/// </remarks>
internal sealed class SpriteResource
{
    private SpriteResource(
        string path,
        SpriteAsset? asset,
        TextureResource? texture,
        string? failure)
    {
        Path = path;
        Asset = asset;
        Texture = texture;
        Failure = failure;
    }

    /// <summary>Gets the canonical sprite asset path.</summary>
    internal string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the sprite asset and its texture descriptor validated. When
    /// <see langword="false"/>, <see cref="Failure"/> explains why and the data properties are null.
    /// </summary>
    internal bool IsValid => Failure is null;

    /// <summary>Gets the validated sprite asset, or <see langword="null"/> for a failed resource.</summary>
    internal SpriteAsset? Asset { get; }

    /// <summary>
    /// Gets the shared texture descriptor resource, or <see langword="null"/> for a failed resource.
    /// </summary>
    internal TextureResource? Texture { get; }

    /// <summary>Gets the failure reason, or <see langword="null"/> for a valid resource.</summary>
    internal string? Failure { get; }

    /// <summary>Creates a validated resource linked to its texture descriptor resource.</summary>
    /// <param name="path">The canonical sprite path.</param>
    /// <param name="asset">The validated sprite asset.</param>
    /// <param name="texture">The shared texture descriptor resource.</param>
    /// <returns>The published resource.</returns>
    internal static SpriteResource Ready(
        string path,
        SpriteAsset asset,
        TextureResource texture)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(texture);
        return new SpriteResource(path, asset, texture, failure: null);
    }

    /// <summary>Creates a cached failed resource.</summary>
    /// <param name="path">The canonical sprite path.</param>
    /// <param name="failure">The validation, decode, or IO failure reason.</param>
    /// <returns>The published failed resource.</returns>
    internal static SpriteResource Failed(string path, string failure)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(failure);
        return new SpriteResource(path, asset: null, texture: null, failure);
    }
}
