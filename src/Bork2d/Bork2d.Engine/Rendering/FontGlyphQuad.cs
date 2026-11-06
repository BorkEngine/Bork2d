using System.Numerics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// One laid-out glyph quad in font-local space.
/// </summary>
/// <param name="Min">The local bottom-left corner of the quad.</param>
/// <param name="Size">The positive local width and height of the quad.</param>
/// <param name="Uv">
/// The normalized atlas rectangle as <c>U0, V0, U1, V1</c> with a top-left origin and V down.
/// </param>
/// <remarks>
/// The quad is copied from the frozen <see cref="Assets.FontGlyph.PlaneBounds"/>, scaled by the
/// renderable's size, and paired with the unchanged <see cref="Assets.FontGlyph.AtlasBounds"/>.
/// Only glyphs with a positive drawable area produce a quad; a whitespace glyph contributes its
/// advance without producing one.
/// </remarks>
internal readonly record struct FontGlyphQuad(Vector2 Min, Vector2 Size, Vector4 Uv);
