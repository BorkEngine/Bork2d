using System.Numerics;
using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// The immutable CPU expansion of one line renderable into a local-space triangle list.
/// </summary>
/// <remarks>
/// <para>
/// The builder owns the vertex, line-UV, and index arrays and never mutates them after construction,
/// so a captured snapshot and a planned frame observe exactly the same geometry. The arrays are
/// exposed as <see cref="ReadOnlyMemory{T}"/>; no mutating accessor exists.
/// </para>
/// <para>
/// Geometry is local space. The renderer applies the captured world matrix later, so a transform
/// change never rebuilds the triangles. <see cref="Bounds"/> is the local-space AABB of every
/// generated vertex, including half the line width and any miter or bevel extents.
/// </para>
/// </remarks>
internal sealed class LineGeometryData
{
    private readonly Vector2[] _vertices;
    private readonly Vector2[] _lineUvs;
    private readonly uint[] _indices;

    /// <summary>
    /// Initializes a new instance of the <see cref="LineGeometryData"/> class.
    /// </summary>
    /// <param name="vertices">The local-space vertex positions; owned by the geometry.</param>
    /// <param name="lineUvs">The per-vertex line UVs; owned by the geometry.</param>
    /// <param name="indices">The triangle-list indices; owned by the geometry.</param>
    /// <param name="bounds">The local-space AABB of <paramref name="vertices"/>.</param>
    internal LineGeometryData(
        Vector2[] vertices,
        Vector2[] lineUvs,
        uint[] indices,
        in Bounds bounds)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(lineUvs);
        ArgumentNullException.ThrowIfNull(indices);
        _vertices = vertices;
        _lineUvs = lineUvs;
        _indices = indices;
        Bounds = bounds;
    }

    /// <summary>Gets the local-space triangle vertices in draw order.</summary>
    internal ReadOnlyMemory<Vector2> Vertices => _vertices;

    /// <summary>
    /// Gets the per-vertex line UVs: <c>x</c> is <c>0</c> on one width edge and <c>1</c> on the
    /// other, and <c>y</c> is the accumulated local-space distance along the line.
    /// </summary>
    internal ReadOnlyMemory<Vector2> LineUvs => _lineUvs;

    /// <summary>Gets the triangle-list indices into <see cref="Vertices"/>.</summary>
    internal ReadOnlyMemory<uint> Indices => _indices;

    /// <summary>Gets the number of triangle vertices.</summary>
    internal int VertexCount => _vertices.Length;

    /// <summary>Gets the number of triangle indices; three per triangle.</summary>
    internal int IndexCount => _indices.Length;

    /// <summary>
    /// Gets the local-space AABB of every generated vertex, including half the width and the miter
    /// or bevel extents.
    /// </summary>
    internal Bounds Bounds { get; }
}
