using System.Numerics;
using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// A value-only copy of one rooted sprite renderable in a single frame snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is internal and immutable after publication. The loaded <see cref="SpriteAsset"/>
/// is the only object reference; the sprite is captured even when it is <see langword="null"/> or
/// its asset is not ready, and the renderer skips it at draw time (M8) instead of racing late
/// readiness. Textures, vertices, UVs, pivots, and other derived sprite metadata are deliberately
/// absent: the renderer resolves them from the asset at draw time.
/// </para>
/// <para>
/// <see cref="Layer"/> is the flattened single-bit mask. Invalid values (<c>0</c> and multi-bit)
/// are captured as-is (Q3=C2); M8 skips them at draw time with the diagnostic.
/// </para>
/// </remarks>
/// <param name="Sprite">The loaded sprite asset, or <see langword="null"/> when nothing is drawn.</param>
/// <param name="WorldMatrix">The sprite's local-to-world matrix, composed through the hierarchy.</param>
/// <param name="Tint">The linear RGBA tint multiplied with the sampled texel.</param>
/// <param name="Layer">The flattened layer bit; invalid values are preserved as-is.</param>
/// <param name="ZIndex">The sort key within a layer; higher draws later.</param>
/// <param name="FlipX">Whether the sprite is mirrored horizontally at draw time (Q2=B1).</param>
/// <param name="FlipY">Whether the sprite is mirrored vertically at draw time (Q2=B1).</param>
internal readonly record struct SpriteSnapshot(
    SpriteAsset? Sprite,
    Matrix3x2 WorldMatrix,
    Color Tint,
    uint Layer,
    int ZIndex,
    bool FlipX,
    bool FlipY);
