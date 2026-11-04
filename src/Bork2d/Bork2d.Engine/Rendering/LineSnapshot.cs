using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// A value-only copy of one rooted line renderable in a single frame snapshot.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot is internal and immutable after publication. <see cref="Geometry"/> is the
/// renderable's cached immutable <see cref="LineGeometryData"/>, or <see langword="null"/> when the
/// line is not drawable; no live scene entity, point list, or mutable shader asset crosses the
/// boundary. Geometry is local space, and <see cref="WorldMatrix"/> is the composed
/// local-to-world matrix the renderer applies.
/// </para>
/// <para>
/// <see cref="Layer"/> is the flattened single-bit mask. Invalid values (<c>0</c> and multi-bit)
/// are captured as-is; the renderer skips them at draw time with the diagnostic, exactly like a
/// sprite.
/// </para>
/// <para>
/// The shader fields are value-only and use safe defaults: <see cref="ShaderData"/> is
/// <see langword="null"/> for a line without a custom shader, and the four uniform lanes are zero
/// when no shader is assigned.
/// </para>
/// </remarks>
/// <param name="Geometry">The cached local-space geometry, or <see langword="null"/> when not drawable.</param>
/// <param name="WorldMatrix">The line's local-to-world matrix, composed through the hierarchy.</param>
/// <param name="Color">The linear RGBA line color.</param>
/// <param name="Layer">The flattened layer bit; invalid values are preserved as-is.</param>
/// <param name="ZIndex">The sort key within a layer; higher draws later.</param>
/// <param name="AntiAlias">Whether the renderer softens the line edges in the fragment stage.</param>
internal readonly record struct LineSnapshot(
    LineGeometryData? Geometry,
    Matrix3x2 WorldMatrix,
    Color Color,
    uint Layer,
    int ZIndex,
    bool AntiAlias)
{
    /// <summary>
    /// Gets the line's world-space AABB for conservative camera culling.
    /// </summary>
    /// <remarks>
    /// The capture definition sets this from the cached local geometry bounds and the composed world
    /// matrix. <see cref="Bounds.Empty"/> means "unknown" and never culls, which keeps direct
    /// snapshot producers conservative.
    /// </remarks>
    internal Bounds Bounds { get; init; } = Bounds.Empty;

    /// <summary>
    /// Gets the frozen custom shader metadata, or <see langword="null"/> when the line uses the
    /// built-in line shader.
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

    /// <summary>
    /// Gets the authored full line width in local units, kept for diagnostics and tests.
    /// </summary>
    internal float Width { get; init; }
}
