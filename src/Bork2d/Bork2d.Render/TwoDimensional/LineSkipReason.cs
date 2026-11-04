namespace Bork2d.Render.TwoDimensional;

/// <summary>
/// Why a captured line is not turned into drawable geometry for a camera.
/// </summary>
internal enum LineSkipReason
{
    /// <summary>The line is drawable (its geometry, layer, and shader are usable).</summary>
    None,

    /// <summary>
    /// The snapshot carries no drawable geometry. The engine validates line input and captures a
    /// non-drawable line as <see langword="null"/> geometry with empty bounds; the renderer skips it
    /// with a one-time diagnostic.
    /// </summary>
    NullGeometry,

    /// <summary>The layer is zero or has more than one bit; skipped with a diagnostic.</summary>
    InvalidLayer,

    /// <summary>The line's custom shader is still loading; the line is skipped until it is ready.</summary>
    Pending,
}
