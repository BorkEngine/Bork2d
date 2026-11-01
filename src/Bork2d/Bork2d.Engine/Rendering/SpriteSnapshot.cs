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
}
