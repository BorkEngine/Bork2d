using System.Numerics;

namespace Bork2d.Engine.Assets;

/// <summary>
/// One codepoint's placement in a <see cref="FontAsset"/> MSDF atlas.
/// </summary>
/// <param name="Codepoint">The Unicode scalar value this glyph renders.</param>
/// <param name="Advance">The horizontal advance in em units, divided by the font's units per em.</param>
/// <param name="Bearing">
/// The pen-to-quad bottom-left offset in em units. It equals
/// <c>(PlaneBounds.X, PlaneBounds.Y)</c> and is zero for a whitespace glyph.
/// </param>
/// <param name="PlaneBounds">
/// The cell quad in em units as left, bottom, right, top. It includes the MSDF field margin
/// (<c>range + padding</c> pixels) on every side and is zero for a whitespace glyph.
/// </param>
/// <param name="AtlasBounds">
/// The cell rectangle in the atlas as normalized U0, V0, U1, V1 with a top-left origin and V down.
/// It is zero for a whitespace glyph.
/// </param>
/// <remarks>
/// A glyph is a value-only description of one cell in the atlas; it carries no texture reference.
/// Several codepoints that map to the same font glyph share the same measurements. The array in
/// <see cref="FontAsset.Glyphs"/> is sorted by <see cref="Codepoint"/>.
/// </remarks>
public readonly record struct FontGlyph(
    int Codepoint,
    float Advance,
    Vector2 Bearing,
    Vector4 PlaneBounds,
    Vector4 AtlasBounds);
