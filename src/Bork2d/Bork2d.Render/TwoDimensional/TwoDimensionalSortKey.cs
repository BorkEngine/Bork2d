namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// The compact per-camera render-order key for one camera-visible 2D record: the record's
/// <c>ZIndex</c>, its projected screen position, and the kind/index it addresses.
/// </summary>
/// <remarks>
/// <para>
/// Draw order is <c>ZIndex</c> ascending; for equal <c>ZIndex</c> it is per-camera screen-space Y
/// ascending in framebuffer pixels (top-left origin, so a record lower on screen draws later); for
/// equal screen Y it is screen X ascending (a record further right draws later). A font expands into
/// one key per glyph at the glyph's world top-left. <c>Layer</c> is only a camera culling bit and
/// never participates in ordering.
/// </para>
/// <para>
/// The key carries <see cref="Kind"/> and <see cref="RecordIndex"/> so the sorted keys address the
/// visible records directly; <see cref="CompareTo"/> compares only the three order components. Ties
/// at equal <c>ZIndex</c>/screen Y/screen X are explicitly unspecified: there is no capture-order,
/// kind, or record-index tie-break, and the sort is not stable.
/// </para>
/// </remarks>
internal readonly struct TwoDimensionalSortKey : IComparable<TwoDimensionalSortKey>
{
    /// <summary>Initializes a new instance of the <see cref="TwoDimensionalSortKey"/> struct.</summary>
    /// <param name="zIndex">The shared 2D draw order; higher draws later.</param>
    /// <param name="screenY">The projected screen-space Y in framebuffer pixels (top-left origin).</param>
    /// <param name="screenX">The projected screen-space X in framebuffer pixels.</param>
    /// <param name="kind">The record kind the index addresses.</param>
    /// <param name="recordIndex">The index into the kind's captured record list.</param>
    internal TwoDimensionalSortKey(
        int zIndex,
        float screenY,
        float screenX,
        DrawKind kind,
        int recordIndex)
    {
        ZIndex = zIndex;
        ScreenY = screenY;
        ScreenX = screenX;
        Kind = kind;
        RecordIndex = recordIndex;
    }

    /// <summary>Gets the shared 2D draw order; higher draws later.</summary>
    internal int ZIndex { get; }

    /// <summary>Gets the projected screen-space Y in framebuffer pixels.</summary>
    internal float ScreenY { get; }

    /// <summary>Gets the projected screen-space X in framebuffer pixels.</summary>
    internal float ScreenX { get; }

    /// <summary>Gets the record kind the index addresses.</summary>
    internal DrawKind Kind { get; }

    /// <summary>Gets the index into the kind's captured record list.</summary>
    internal int RecordIndex { get; }

    /// <inheritdoc/>
    public int CompareTo(TwoDimensionalSortKey other)
    {
        int zIndex = ZIndex.CompareTo(other.ZIndex);
        if (zIndex != 0)
        {
            return zIndex;
        }

        int screenY = ScreenY.CompareTo(other.ScreenY);
        return screenY != 0 ? screenY : ScreenX.CompareTo(other.ScreenX);
    }
}
