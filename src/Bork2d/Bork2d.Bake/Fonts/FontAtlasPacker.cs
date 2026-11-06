namespace Bork2d.Bake.Fonts;

/// <summary>
/// Packs glyph cells into a fixed square atlas with a deterministic shelf algorithm.
/// </summary>
/// <remarks>
/// <para>
/// Cells are sorted by height descending, then first codepoint ascending, then glyph index ascending,
/// and placed left to right in shelves whose width is the atlas side. A cell that does not fit the
/// remaining shelf width starts a new shelf below the previous one. No spacing is added: every cell
/// already reserves the MSDF field margin (<c>range + padding</c> pixels) on every side, so two
/// adjacent cells cannot bleed into each other.
/// </para>
/// <para>
/// The sort makes the result independent of discovery order, so the same selection always produces
/// the same atlas layout and therefore the same incremental output.
/// </para>
/// </remarks>
internal static class FontAtlasPacker
{
    /// <summary>One unique glyph outline's cell size in pixels.</summary>
    /// <param name="GlyphIndex">The font glyph index, the unique key of the outline.</param>
    /// <param name="FirstCodepoint">The lowest selected codepoint that maps to the glyph.</param>
    /// <param name="Width">The cell width in pixels, including both field margins.</param>
    /// <param name="Height">The cell height in pixels, including both field margins.</param>
    internal readonly record struct Cell(
        int GlyphIndex,
        int FirstCodepoint,
        int Width,
        int Height);

    /// <summary>One cell's placement in the atlas, in top-down pixel coordinates.</summary>
    /// <param name="X">The cell's left column.</param>
    /// <param name="Y">The cell's top row.</param>
    /// <param name="Width">The cell width in pixels.</param>
    /// <param name="Height">The cell height in pixels.</param>
    internal readonly record struct Placement(int X, int Y, int Width, int Height);

    /// <summary>Packs every cell into one square atlas.</summary>
    /// <param name="cells">The unique glyph cells to place.</param>
    /// <param name="atlasSize">The atlas side length in pixels; positive.</param>
    /// <param name="placements">The placed rectangles keyed by glyph index when successful.</param>
    /// <returns><see langword="true"/> when every cell fits the atlas.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="cells"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="atlasSize"/> is not positive.</exception>
    internal static bool TryPack(
        IReadOnlyList<Cell> cells,
        int atlasSize,
        out Dictionary<int, Placement> placements)
    {
        ArgumentNullException.ThrowIfNull(cells);
        if (atlasSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(atlasSize),
                atlasSize,
                "The atlas side length must be positive.");
        }

        placements = new Dictionary<int, Placement>(cells.Count);

        var ordered = new Cell[cells.Count];
        for (int index = 0; index < cells.Count; index++)
        {
            Cell cell = cells[index];
            if (cell.Width <= 0 || cell.Height <= 0)
            {
                return false;
            }

            if (cell.Width > atlasSize || cell.Height > atlasSize)
            {
                return false;
            }

            ordered[index] = cell;
        }

        Array.Sort(ordered, static (left, right) =>
        {
            int result = right.Height.CompareTo(left.Height);
            if (result != 0)
            {
                return result;
            }

            result = left.FirstCodepoint.CompareTo(right.FirstCodepoint);
            return result != 0 ? result : left.GlyphIndex.CompareTo(right.GlyphIndex);
        });

        int shelfX = 0;
        int shelfY = 0;
        int shelfHeight = 0;
        foreach (Cell cell in ordered)
        {
            if (shelfX + cell.Width > atlasSize)
            {
                shelfY += shelfHeight;
                shelfX = 0;
                shelfHeight = 0;
            }

            if (shelfY + cell.Height > atlasSize)
            {
                return false;
            }

            placements[cell.GlyphIndex] = new Placement(
                shelfX,
                shelfY,
                cell.Width,
                cell.Height);
            shelfX += cell.Width;
            shelfHeight = Math.Max(shelfHeight, cell.Height);
        }

        return true;
    }
}
