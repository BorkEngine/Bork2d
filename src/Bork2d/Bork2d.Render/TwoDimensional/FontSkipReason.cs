namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// Why a captured font renderable does not become glyph instances for a camera.
/// </summary>
/// <remarks>
/// <see cref="Empty"/> is a normal no-draw state, not a skip: a font with no drawable glyph quads (for
/// example empty text or a spaces-only line) produces no runs and is not counted in
/// <c>RenderStats.SkippedFonts</c>. Only <see cref="Null"/>, <see cref="InvalidLayer"/>,
/// <see cref="PendingShader"/>, and <see cref="PendingAtlas"/> count as skipped.
/// </remarks>
internal enum FontSkipReason
{
    /// <summary>The font is drawable and each drawable glyph has an instance.</summary>
    None,

    /// <summary>The snapshot has no frozen font render data; skipped silently.</summary>
    Null,

    /// <summary>The layer is zero or has more than one bit; skipped with a diagnostic.</summary>
    InvalidLayer,

    /// <summary>
    /// The layout is absent or contains no drawable glyph quads; the font draws nothing and is not
    /// counted as skipped.
    /// </summary>
    Empty,

    /// <summary>
    /// The resolved custom shader is still loading; the font is skipped until the frame that first
    /// observes it ready.
    /// </summary>
    PendingShader,

    /// <summary>
    /// The font's atlas page is still loading; the font is skipped until the frame that first
    /// observes the page ready.
    /// </summary>
    PendingAtlas,
}
