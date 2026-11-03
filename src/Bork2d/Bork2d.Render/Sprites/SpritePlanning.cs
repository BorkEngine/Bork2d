using System.Numerics;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Mathematics;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Sprites;

/// <summary>
/// Pure, allocation-free sprite ordering, visibility, and skip classification (M8 §6.4/§6.8).
/// </summary>
/// <remarks>
/// <para>
/// Sorting runs once per snapshot on an index array so the snapshot itself is never copied or
/// reordered: sprites ascend by <c>Layer</c>, then <c>ZIndex</c>; cameras ascend by <c>ZIndex</c>.
/// Sprite ordering builds compact 12-byte <see cref="SpriteSortKey"/> values and sorts the key
/// scratch buffer, then writes the resulting indices; a large frame therefore never compares full
/// captured records or repeatedly reads their cache lines. Cameras keep the small generic heap
/// sorter. Equal keys are unspecified and neither sort is stable, so callers must not rely on the
/// order of equal keys; both paths reuse caller-owned scratch and allocate nothing warm.
/// </para>
/// <para>
/// The per-camera filter is the shared <see cref="IsVisible"/> rule: <c>(CullingMask &amp; Layer)
/// != 0</c> and non-empty world bounds that intersect the camera's world bounds. Null sprites,
/// zero/multi-bit layers, known non-<c>Quad</c> topologies, and still-loading texture pages are
/// skipped; an unknown topology or malformed field is fatal and never silently repaired.
/// </para>
/// </remarks>
internal static class SpritePlanning
{
    /// <summary>Returns whether a camera's mask sees a sprite's single-bit layer.</summary>
    /// <param name="cullingMask">The camera culling mask.</param>
    /// <param name="layer">The sprite's flattened layer bit.</param>
    /// <returns><see langword="true"/> when the bit is selected.</returns>
    internal static bool LayerMatches(uint cullingMask, uint layer)
        => (cullingMask & layer) != 0;

    /// <summary>Returns whether a flattened layer value is exactly one non-zero bit.</summary>
    /// <param name="layer">The flattened layer value.</param>
    /// <returns><see langword="true"/> for one non-zero bit.</returns>
    internal static bool IsValidLayer(uint layer)
        => layer != 0 && (layer & (layer - 1)) == 0;

    /// <summary>
    /// Returns whether a camera's culling mask and world bounds let it emit a sprite.
    /// </summary>
    /// <remarks>
    /// The layer test runs first: a sprite outside the camera's mask is never a visibility candidate,
    /// so it is not counted as culled. Empty sprite bounds (unknown bounds from direct snapshot
    /// producers or absent data) and empty camera bounds (an overflowed or non-finite visible
    /// rectangle) never cull, which keeps the rule conservative. The production planner and the
    /// deterministic visibility tests share this method, so they cannot drift apart.
    /// </remarks>
    /// <param name="cullingMask">The camera culling mask.</param>
    /// <param name="cameraBounds">The camera's world-space AABB.</param>
    /// <param name="sprite">The captured sprite.</param>
    /// <returns><see langword="true"/> when the camera may emit the sprite.</returns>
    internal static bool IsVisible(uint cullingMask, in Bounds cameraBounds, in SpriteSnapshot sprite)
        => LayerMatches(cullingMask, sprite.Layer)
            && (sprite.Bounds.IsEmpty
                || cameraBounds.IsEmpty
                || sprite.Bounds.Intersects(cameraBounds));

    /// <summary>
    /// Sorts sprite indices by <c>(Layer, ZIndex)</c> ascending without allocating or touching the
    /// snapshot.
    /// </summary>
    /// <param name="sprites">The captured sprites in capture order.</param>
    /// <param name="indices">
    /// The output index buffer; at least <paramref name="sprites"/>'s length. The remaining entries
    /// are left untouched.
    /// </param>
    /// <param name="keys">
    /// The reusable key scratch buffer; at least <paramref name="sprites"/>'s length. The planner
    /// grows it once and reuses it every frame.
    /// </param>
    /// <exception cref="ArgumentException">An output buffer is too small.</exception>
    internal static void Sort(
        ReadOnlySpan<SpriteSnapshot> sprites,
        Span<int> indices,
        Span<SpriteSortKey> keys)
    {
        if (indices.Length < sprites.Length || keys.Length < sprites.Length)
        {
            throw new ArgumentException(
                $"The sort buffers must hold {sprites.Length} entries.");
        }

        for (var index = 0; index < sprites.Length; index++)
        {
            SpriteSnapshot sprite = sprites[index];
            keys[index] = new SpriteSortKey(sprite.Layer, sprite.ZIndex, index);
        }

        Span<SpriteSortKey> ordered = keys[..sprites.Length];
        ordered.Sort();
        for (var index = 0; index < ordered.Length; index++)
        {
            indices[index] = ordered[index].Index;
        }
    }

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
    /// <param name="texturePending">
    /// Whether the sprite's resolved texture page is still loading. WP-3 supplies this; WP-2 only
    /// implements the pure classification.
    /// </param>
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
