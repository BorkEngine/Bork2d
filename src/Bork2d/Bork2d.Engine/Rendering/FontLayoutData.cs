using Bork2d.Engine.Mathematics;

namespace Bork2d.Engine.Rendering;

/// <summary>
/// The immutable result of laying out one font renderable's text in local space.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Glyphs"/> contains the drawable glyph quads in line-major order. <see cref="Width"/>
/// and <see cref="Height"/> describe the layout box: the widest line's advance width and the line
/// count times the line height. The box can be positive while <see cref="Glyphs"/> is empty, for
/// example for a space-only line, so <see cref="IsEmpty"/> means "no drawable quads" rather than
/// "no text".
/// </para>
/// <para>
/// <see cref="LocalBounds"/> is the tight local AABB of the quads, not the layout box, and is
/// <see cref="Bounds.Empty"/> when there are no quads or a non-finite value appears.
/// </para>
/// </remarks>
internal sealed class FontLayoutData
{
    private readonly FontGlyphQuad[] _glyphs;

    /// <summary>
    /// Initializes a new instance of the <see cref="FontLayoutData"/> class.
    /// </summary>
    /// <param name="glyphs">The drawable glyph quads in line-major order.</param>
    /// <param name="localBounds">The tight local AABB of the quads, or <see cref="Bounds.Empty"/>.</param>
    /// <param name="width">The widest line's advance width.</param>
    /// <param name="height">The line count times the line height.</param>
    internal FontLayoutData(
        FontGlyphQuad[] glyphs,
        Bounds localBounds,
        float width,
        float height)
    {
        _glyphs = glyphs;
        LocalBounds = localBounds;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Gets the empty layout: no quads, no layout box, and empty local bounds.
    /// </summary>
    internal static FontLayoutData Empty { get; } = new([], Bounds.Empty, 0f, 0f);

    /// <summary>Gets the drawable glyph quads in line-major order.</summary>
    internal ReadOnlySpan<FontGlyphQuad> Glyphs => _glyphs;

    /// <summary>Gets the tight local AABB of the glyph quads.</summary>
    internal Bounds LocalBounds { get; }

    /// <summary>Gets the widest line's advance width in local units.</summary>
    internal float Width { get; }

    /// <summary>Gets the line count times the line height in local units.</summary>
    internal float Height { get; }

    /// <summary>Gets a value indicating whether the layout contains no drawable glyph quads.</summary>
    internal bool IsEmpty => _glyphs.Length == 0;
}
