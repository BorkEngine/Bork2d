using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Sprites;

/// <summary>
/// Pure, allocation-free sprite ordering, filtering, and skip classification (M8 §6.4/§6.8).
/// </summary>
/// <remarks>
/// <para>
/// Sorting runs once per snapshot on an index array so the snapshot itself is never copied or
/// reordered: ascending <c>Layer</c>, then ascending <c>ZIndex</c>. Equal keys are unspecified;
/// the heap sort is deterministic for a given input but is not a stable sort, so callers must not
/// rely on the order of equal keys.
/// </para>
/// <para>
/// The per-camera filter is <c>(CullingMask &amp; Layer) != 0</c>. Null sprites, zero/multi-bit
/// layers, known non-<c>Quad</c> topologies, and still-loading texture pages are skipped; an
/// unknown topology or malformed field is fatal and never silently repaired.
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
    /// Sorts sprite indices by <c>(Layer, ZIndex)</c> ascending without allocating or touching the
    /// snapshot.
    /// </summary>
    /// <param name="sprites">The captured sprites in capture order.</param>
    /// <param name="indices">
    /// The output index buffer; at least <paramref name="sprites"/>'s length. The remaining entries
    /// are left untouched.
    /// </param>
    /// <exception cref="ArgumentException">The index buffer is too small.</exception>
    internal static void Sort(ReadOnlySpan<SpriteSnapshot> sprites, Span<int> indices)
    {
        if (indices.Length < sprites.Length)
        {
            throw new ArgumentException(
                $"The index buffer must hold {sprites.Length} entries.",
                nameof(indices));
        }

        for (var index = 0; index < sprites.Length; index++)
        {
            indices[index] = index;
        }

        Span<int> ordered = indices[..sprites.Length];
        for (var root = (ordered.Length / 2) - 1; root >= 0; root--)
        {
            SiftDown(sprites, ordered, root, ordered.Length);
        }

        for (var end = ordered.Length - 1; end > 0; end--)
        {
            (ordered[0], ordered[end]) = (ordered[end], ordered[0]);
            SiftDown(sprites, ordered, root: 0, end);
        }
    }

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

    /// <summary>
    /// Builds the sorted, layer-filtered, statically drawable indices for one camera.
    /// </summary>
    /// <param name="sprites">The captured sprites in capture order.</param>
    /// <param name="sortedIndices">The indices produced by <see cref="Sort"/>.</param>
    /// <param name="cullingMask">The camera mask; <c>0</c> sees nothing.</param>
    /// <param name="visibleIndices">The output buffer for drawable snapshot indices.</param>
    /// <returns>The number of drawable indices written.</returns>
    /// <exception cref="ArgumentException">The output buffer is too small.</exception>
    /// <exception cref="Bork2d.Engine.Bork2dException">A visible sprite has an unknown topology.</exception>
    internal static int FilterVisible(
        ReadOnlySpan<SpriteSnapshot> sprites,
        ReadOnlySpan<int> sortedIndices,
        uint cullingMask,
        Span<int> visibleIndices)
    {
        var count = 0;
        for (var index = 0; index < sortedIndices.Length; index++)
        {
            int snapshotIndex = sortedIndices[index];
            SpriteSnapshot sprite = sprites[snapshotIndex];
            if (!LayerMatches(cullingMask, sprite.Layer))
            {
                continue;
            }

            if (Classify(sprite, texturePending: false) != SpriteSkipReason.None)
            {
                continue;
            }

            if (count == visibleIndices.Length)
            {
                throw new ArgumentException(
                    $"The visible-index buffer must hold {sortedIndices.Length} entries.",
                    nameof(visibleIndices));
            }

            visibleIndices[count++] = snapshotIndex;
        }

        return count;
    }

    /// <summary>
    /// Sorts camera indices by <c>ZIndex</c> ascending without allocating or touching the snapshot.
    /// </summary>
    /// <remarks>
    /// Cameras apply in this order; equal <c>ZIndex</c> values are unspecified. The sort is the same
    /// preallocated-index heap sort as <see cref="Sort"/>, so a warm plan allocates nothing.
    /// </remarks>
    /// <param name="cameras">The captured cameras in capture order.</param>
    /// <param name="indices">
    /// The output index buffer; at least <paramref name="cameras"/>'s length. The remaining entries
    /// are left untouched.
    /// </param>
    /// <exception cref="ArgumentException">The index buffer is too small.</exception>
    internal static void SortCameras(ReadOnlySpan<CameraSnapshot> cameras, Span<int> indices)
    {
        if (indices.Length < cameras.Length)
        {
            throw new ArgumentException(
                $"The index buffer must hold {cameras.Length} entries.",
                nameof(indices));
        }

        for (var index = 0; index < cameras.Length; index++)
        {
            indices[index] = index;
        }

        Span<int> ordered = indices[..cameras.Length];
        for (var root = (ordered.Length / 2) - 1; root >= 0; root--)
        {
            SiftDownCameras(cameras, ordered, root, ordered.Length);
        }

        for (var end = ordered.Length - 1; end > 0; end--)
        {
            (ordered[0], ordered[end]) = (ordered[end], ordered[0]);
            SiftDownCameras(cameras, ordered, root: 0, end);
        }
    }

    private static void SiftDownCameras(
        ReadOnlySpan<CameraSnapshot> cameras,
        Span<int> indices,
        int root,
        int count)
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
                && cameras[indices[right]].ZIndex > cameras[indices[child]].ZIndex)
            {
                child = right;
            }

            if (cameras[indices[child]].ZIndex <= cameras[indices[root]].ZIndex)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static void SiftDown(
        ReadOnlySpan<SpriteSnapshot> sprites,
        Span<int> indices,
        int root,
        int count)
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
                && Compare(sprites[indices[right]], sprites[indices[child]]) > 0)
            {
                child = right;
            }

            if (Compare(sprites[indices[child]], sprites[indices[root]]) <= 0)
            {
                return;
            }

            (indices[root], indices[child]) = (indices[child], indices[root]);
            root = child;
        }
    }

    private static int Compare(in SpriteSnapshot left, in SpriteSnapshot right)
    {
        int layer = left.Layer.CompareTo(right.Layer);
        return layer != 0 ? layer : left.ZIndex.CompareTo(right.ZIndex);
    }
}
