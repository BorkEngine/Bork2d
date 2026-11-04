using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Sprites;

namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// Pure, allocation-free 2D culling, classification, and camera ordering shared by the planner and
/// the deterministic tests.
/// </summary>
/// <remarks>
/// <para>
/// Cameras ascend by <c>ZIndex</c>; equal camera <c>ZIndex</c> values are unspecified. Draw order is
/// per camera and is the planner's <see cref="TwoDimensionalSortKey"/>: <c>ZIndex</c> ascending, then
/// projected screen Y, then screen X. <c>Layer</c> is only a culling bit.
/// </para>
/// <para>
/// The shared visibility rule is <c>(CullingMask &amp; Layer) != 0</c> and non-empty record bounds
/// that intersect the camera's world bounds. Empty bounds (unknown bounds from direct snapshot
/// producers or absent data) and empty camera bounds (an overflowed or non-finite visible rectangle)
/// never cull, which keeps the rule conservative. The production planner and the visibility tests
/// share this method, so they cannot drift apart.
/// </para>
/// <para>
/// Sprite classification: null, invalid layer, known non-<c>Quad</c> topology, and still-loading
/// texture page are skipped; an unknown topology or malformed field is fatal and never silently
/// repaired. Line classification: null geometry, invalid layer, and a still-loading custom shader are
/// skipped; the engine already validates line input and captures a non-drawable line as null
/// geometry.
/// </para>
/// </remarks>
internal static class TwoDimensionalPlanning
{
    /// <summary>Returns whether a camera's mask sees a record's single-bit layer.</summary>
    /// <param name="cullingMask">The camera culling mask.</param>
    /// <param name="layer">The record's flattened layer bit.</param>
    /// <returns><see langword="true"/> when the bit is selected.</returns>
    internal static bool LayerMatches(uint cullingMask, uint layer)
        => (cullingMask & layer) != 0;

    /// <summary>Returns whether a flattened layer value is exactly one non-zero bit.</summary>
    /// <param name="layer">The flattened layer value.</param>
    /// <returns><see langword="true"/> for one non-zero bit.</returns>
    internal static bool IsValidLayer(uint layer)
        => layer != 0 && (layer & (layer - 1)) == 0;

    /// <summary>
    /// Returns whether a record's non-empty world bounds intersect a camera's non-empty world bounds.
    /// </summary>
    /// <remarks>
    /// This is the bounds-only half of <see cref="IsVisible"/> and exists so the planner can count a
    /// record as culled exactly when its layer matches but its bounds reject it. Empty bounds on
    /// either side mean "unknown" and never cull.
    /// </remarks>
    /// <param name="cameraBounds">The camera's world-space AABB.</param>
    /// <param name="recordBounds">The record's world-space AABB.</param>
    /// <returns><see langword="true"/> when the camera may emit the record.</returns>
    internal static bool BoundsIntersect(in Bounds cameraBounds, in Bounds recordBounds)
        => recordBounds.IsEmpty
            || cameraBounds.IsEmpty
            || recordBounds.Intersects(cameraBounds);

    /// <summary>
    /// Returns whether a camera's culling mask and world bounds let it emit a record.
    /// </summary>
    /// <remarks>
    /// The layer test runs first: a record outside the camera's mask is never a visibility candidate,
    /// so it is not counted as culled.
    /// </remarks>
    /// <param name="cullingMask">The camera culling mask.</param>
    /// <param name="cameraBounds">The camera's world-space AABB.</param>
    /// <param name="layer">The record's flattened layer bit.</param>
    /// <param name="recordBounds">The record's world-space AABB.</param>
    /// <returns><see langword="true"/> when the camera may emit the record.</returns>
    internal static bool IsVisible(
        uint cullingMask,
        in Bounds cameraBounds,
        uint layer,
        in Bounds recordBounds)
        => LayerMatches(cullingMask, layer) && BoundsIntersect(cameraBounds, recordBounds);

    /// <summary>
    /// Sorts camera indices by <c>ZIndex</c> ascending without allocating or touching the snapshot.
    /// </summary>
    /// <remarks>
    /// Cameras apply in this order; equal <c>ZIndex</c> values are unspecified. The camera set is
    /// small, so this keeps the reusable index heap sorter and allocates nothing warm.
    /// </remarks>
    /// <param name="cameras">The captured cameras in capture order.</param>
    /// <param name="indices">
    /// The output index buffer; at least <paramref name="cameras"/>'s length. The remaining entries
    /// are left untouched.
    /// </param>
    /// <exception cref="ArgumentException">The index buffer is too small.</exception>
    internal static void SortCameras(ReadOnlySpan<CameraSnapshot> cameras, Span<int> indices)
        => SortIndices<CameraSnapshot, CameraOrder>(cameras, indices);

    /// <summary>
    /// Classifies one captured sprite for drawing, in the approved order: null, invalid layer,
    /// unknown topology (fatal), known non-<c>Quad</c> topology, pending page.
    /// </summary>
    /// <param name="sprite">The captured sprite values.</param>
    /// <param name="texturePending">Whether the sprite's resolved texture page is still loading.</param>
    /// <returns>The skip reason, or <see cref="SpriteSkipReason.None"/> when drawable.</returns>
    /// <exception cref="Bork2d.Engine.Bork2dException">
    /// The sprite's topology is an unknown enum value.
    /// </exception>
    internal static SpriteSkipReason Classify(in SpriteSnapshot sprite, bool texturePending)
    {
        SpriteRenderData? data = sprite.SpriteData;
        if (data is null)
        {
            return SpriteSkipReason.Null;
        }

        if (!IsValidLayer(sprite.Layer))
        {
            return SpriteSkipReason.InvalidLayer;
        }

        SpriteTopology topology = data.Topology;
        if (topology is not (SpriteTopology.Quad or SpriteTopology.TriangleStrip
            or SpriteTopology.Triangles))
        {
            throw SpriteQuad.CreateMalformedException(
                data,
                "Topology",
                $"unknown SpriteTopology value {(int)topology}");
        }

        if (topology != SpriteTopology.Quad)
        {
            return SpriteSkipReason.NonQuad;
        }

        return texturePending ? SpriteSkipReason.Pending : SpriteSkipReason.None;
    }

    /// <summary>
    /// Classifies one captured line for drawing, in the approved order: null geometry, invalid layer,
    /// pending custom shader.
    /// </summary>
    /// <remarks>
    /// Geometry validation happens in the engine at capture time: a non-drawable line arrives with
    /// <see langword="null"/> geometry and empty bounds, so the renderer only has to skip it. A
    /// still-loading custom shader is a normal pending skip and is not warned about.
    /// </remarks>
    /// <param name="line">The captured line values.</param>
    /// <param name="shaderPending">Whether the line's custom shader is still loading.</param>
    /// <returns>The skip reason, or <see cref="LineSkipReason.None"/> when drawable.</returns>
    internal static LineSkipReason ClassifyLine(in LineSnapshot line, bool shaderPending)
    {
        if (line.Geometry is null)
        {
            return LineSkipReason.NullGeometry;
        }

        if (!IsValidLayer(line.Layer))
        {
            return LineSkipReason.InvalidLayer;
        }

        return shaderPending ? LineSkipReason.Pending : LineSkipReason.None;
    }

    private static void SortIndices<TSource, TOrder>(ReadOnlySpan<TSource> items, Span<int> indices)
        where TOrder : struct, IIndexOrder<TSource>
    {
        if (indices.Length < items.Length)
        {
            throw new ArgumentException(
                $"The index buffer must hold {items.Length} entries.",
                nameof(indices));
        }

        for (var index = 0; index < items.Length; index++)
        {
            indices[index] = index;
        }

        Span<int> ordered = indices[..items.Length];
        for (var root = (ordered.Length / 2) - 1; root >= 0; root--)
        {
            SiftDown<TSource, TOrder>(items, ordered, root, ordered.Length);
        }

        for (var end = ordered.Length - 1; end > 0; end--)
        {
            (ordered[0], ordered[end]) = (ordered[end], ordered[0]);
            SiftDown<TSource, TOrder>(items, ordered, root: 0, end);
        }
    }

    private static void SiftDown<TSource, TOrder>(
        ReadOnlySpan<TSource> items,
        Span<int> indices,
        int root,
        int count)
        where TOrder : struct, IIndexOrder<TSource>
    {
        while (true)
        {
            int child = (root * 2) + 1;
            if (child >= count)
            {
                return;
            }

            int right = child + 1;
            if (right < count
                && TOrder.Compare(in items[indices[right]], in items[indices[child]]) > 0)
            {
                child = right;
            }

            if (TOrder.Compare(in items[indices[child]], in items[indices[root]]) <= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    /// <summary>Orders one snapshot type by its render order key.</summary>
    /// <typeparam name="TSource">The captured record type.</typeparam>
    private interface IIndexOrder<TSource>
    {
        /// <summary>Compares two captured records by their sort key.</summary>
        /// <param name="left">The left record.</param>
        /// <param name="right">The right record.</param>
        /// <returns>A negative, zero, or positive value in ascending key order.</returns>
        static abstract int Compare(in TSource left, in TSource right);
    }

    /// <summary>Orders cameras by ascending <c>ZIndex</c>.</summary>
    private readonly struct CameraOrder : IIndexOrder<CameraSnapshot>
    {
        /// <inheritdoc/>
        public static int Compare(in CameraSnapshot left, in CameraSnapshot right)
            => left.ZIndex.CompareTo(right.ZIndex);
    }
}
