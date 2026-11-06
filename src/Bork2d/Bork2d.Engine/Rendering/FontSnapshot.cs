using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// A value-only copy of one rooted font renderable in a single frame snapshot.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="FontData"/> is the immutable <see cref="FontRenderData"/> frozen when the asset was
/// assigned, not the live <see cref="FontAsset"/>; <see cref="Layout"/> is the immutable local
/// layout cached by the renderable. Mutating either asset afterwards cannot change this record or a
/// frame planned from it. A font with no asset is captured with both fields <see langword="null"/>
/// and the capture remains safe.
/// </para>
/// <para>
/// <see cref="WorldMatrix"/> is the composed local-to-world matrix, and <see cref="Bounds"/> is the
/// world-space AABB of the laid-out glyph quads. <see cref="Layer"/> is the flattened single-bit
/// mask; invalid values are captured as-is, exactly like a sprite or line.
/// </para>
/// <para>
/// The shader fields are value-only and use safe defaults: <see cref="ShaderData"/> is
/// <see langword="null"/> for a font without a custom shader, and the four uniform lanes are zero
/// when no shader is assigned.
/// </para>
/// </remarks>
/// <param name="FontData">The frozen font data, or <see langword="null"/> when no asset is assigned.</param>
/// <param name="Layout">The cached local layout, or <see langword="null"/> when none exists.</param>
/// <param name="WorldMatrix">The font's local-to-world matrix, composed through the hierarchy.</param>
/// <param name="Color">The linear RGBA color multiplied with the glyph coverage.</param>
/// <param name="Layer">The flattened layer bit; invalid values are preserved as-is.</param>
/// <param name="ZIndex">The shared 2D draw order; higher draws later and it is not layer-scoped.</param>
internal readonly record struct FontSnapshot(
    FontRenderData? FontData,
    FontLayoutData? Layout,
    Matrix3x2 WorldMatrix,
    Color Color,
    uint Layer,
    int ZIndex)
{
    /// <summary>
    /// Gets the font's world-space AABB for conservative camera culling.
    /// </summary>
    /// <remarks>
    /// The capture definition sets this from the local layout bounds and the composed world matrix.
    /// <see cref="Bounds.Empty"/> means "unknown" and never culls, which keeps direct snapshot
    /// producers conservative.
    /// </remarks>
    internal Bounds Bounds { get; init; } = Bounds.Empty;

    /// <summary>
    /// Gets the frozen custom shader metadata, or <see langword="null"/> when the font uses the
    /// built-in <c>shader/sdf</c>.
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
}
