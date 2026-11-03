namespace Bork2d.Render.Sprites;

/// <summary>
/// The compact render-order key for one captured sprite: the layer mask, the layer-local ZIndex, and
/// the capture index it addresses.
/// </summary>
/// <remarks>
/// The planner sorts these 12-byte keys instead of the full captured sprite records. The former heap
/// sort compared two ~184-byte snapshots per branch, so a large frame spent most of its planning time
/// on cache-unfriendly reads; key sorting keeps the sort working set small and lets the runtime's
/// introsort do the work. Equal keys have unspecified relative order.
/// </remarks>
internal readonly struct SpriteSortKey : IComparable<SpriteSortKey>
{
    /// <summary>Initializes a new instance of the <see cref="SpriteSortKey"/> struct.</summary>
    /// <param name="layer">The flattened layer mask.</param>
    /// <param name="zIndex">The layer-local sort order.</param>
    /// <param name="index">The captured sprite index.</param>
    internal SpriteSortKey(uint layer, int zIndex, int index)
    {
        Layer = layer;
        ZIndex = zIndex;
        Index = index;
    }

    /// <summary>Gets the flattened layer mask.</summary>
    internal uint Layer { get; }

    /// <summary>Gets the layer-local sort order.</summary>
    internal int ZIndex { get; }

    /// <summary>Gets the captured sprite index.</summary>
    internal int Index { get; }

    /// <inheritdoc/>
    public int CompareTo(SpriteSortKey other)
    {
        int layer = Layer.CompareTo(other.Layer);
        return layer != 0 ? layer : ZIndex.CompareTo(other.ZIndex);
    }
}
