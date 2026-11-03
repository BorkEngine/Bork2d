using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// A value-only copy of one rooted sprite renderable in a single frame snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is internal and immutable after publication. <see cref="SpriteData"/> is the
/// frozen <see cref="SpriteRenderData"/> captured when the asset was assigned, not the live
/// <see cref="SpriteAsset"/>: mutating the asset afterwards cannot change this record or a frame
/// planned from it. The sprite is captured even when it is <see langword="null"/> or its asset is
/// not ready, and the renderer skips it at draw time (M8) instead of racing late readiness.
/// </para>
/// <para>
/// <see cref="Layer"/> is the flattened single-bit mask. Invalid values (<c>0</c> and multi-bit)
/// are captured as-is (Q3=C2); M8 skips them at draw time with the diagnostic.
/// </para>
/// <para>
/// The shader fields are value-only and use safe defaults: <see cref="ShaderData"/> is
/// <see langword="null"/> for a sprite without a custom shader, the four uniform lanes are zero when
/// no shader is assigned, and the four extra texture references are absent when none were
/// captured. <see cref="AdditionalTextureCount"/> keeps the total reference count even when it
/// exceeds four, so the renderer can warn about the entries that did not fit.
/// </para>
/// </remarks>
/// <param name="SpriteData">
/// The frozen sprite render data, or <see langword="null"/> when nothing is drawn.
/// </param>
/// <param name="WorldMatrix">The sprite's local-to-world matrix, composed through the hierarchy.</param>
/// <param name="Tint">The linear RGBA tint multiplied with the sampled texel.</param>
/// <param name="Layer">The flattened layer bit; invalid values are preserved as-is.</param>
/// <param name="ZIndex">The sort key within a layer; higher draws later.</param>
/// <param name="FlipX">Whether the sprite is mirrored horizontally at draw time (Q2=B1).</param>
/// <param name="FlipY">Whether the sprite is mirrored vertically at draw time (Q2=B1).</param>
internal readonly record struct SpriteSnapshot(
    SpriteRenderData? SpriteData,
    Matrix3x2 WorldMatrix,
    Color Tint,
    uint Layer,
    int ZIndex,
    bool FlipX,
    bool FlipY)
{
    /// <summary>
    /// Gets the sprite's world-space AABB for conservative camera culling.
    /// </summary>
    /// <remarks>
    /// The capture definition sets this from the frozen render data, pivot, flips, and composed
    /// world matrix. <see cref="Bounds.Empty"/> means "unknown" and never culls, which keeps direct
    /// snapshot producers conservative.
    /// </remarks>
    internal Bounds Bounds { get; init; } = Bounds.Empty;

    /// <summary>
    /// Gets the frozen custom shader metadata, or <see langword="null"/> when the sprite uses the
    /// built-in shader.
    /// </summary>
    internal ShaderRenderData? ShaderData { get; init; }

    /// <summary>Gets the first <see cref="Vector4"/> lane of the instance uniform block.</summary>
    internal Vector4 Uniform0 { get; init; }

    /// <summary>Gets the second <see cref="Vector4"/> lane of the instance uniform block.</summary>
    internal Vector4 Uniform1 { get; init; }

    /// <summary>Gets the third <see cref="Vector4"/> lane of the instance uniform block.</summary>
    internal Vector4 Uniform2 { get; init; }

    /// <summary>Gets the fourth <see cref="Vector4"/> lane of the instance uniform block.</summary>
    internal Vector4 Uniform3 { get; init; }

    /// <summary>Gets the first captured additional texture reference, or an absent reference.</summary>
    internal AssetRef<TextureDescriptor> ExtraTexture0 { get; init; }

    /// <summary>Gets the second captured additional texture reference, or an absent reference.</summary>
    internal AssetRef<TextureDescriptor> ExtraTexture1 { get; init; }

    /// <summary>Gets the third captured additional texture reference, or an absent reference.</summary>
    internal AssetRef<TextureDescriptor> ExtraTexture2 { get; init; }

    /// <summary>Gets the fourth captured additional texture reference, or an absent reference.</summary>
    internal AssetRef<TextureDescriptor> ExtraTexture3 { get; init; }

    /// <summary>
    /// Gets the total number of additional texture references the renderable held at capture.
    /// </summary>
    /// <remarks>
    /// Only the first four references fit in <see cref="ExtraTexture0"/> through
    /// <see cref="ExtraTexture3"/>; a larger value tells the renderer how many were ignored.
    /// </remarks>
    internal int AdditionalTextureCount { get; init; }
}
