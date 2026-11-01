namespace Bork2d.Render.Sprites;

/// <summary>
/// Why a captured sprite is not turned into an instance for a camera (M8 §6.4/§6.8).
/// </summary>
internal enum SpriteSkipReason
{
    /// <summary>The sprite is drawable (its texture page state is owned by the page cache).</summary>
    None,

    /// <summary>The snapshot has no frozen sprite render data; skipped silently.</summary>
    Null,

    /// <summary>The layer is zero or has more than one bit; skipped with a diagnostic.</summary>
    InvalidLayer,

    /// <summary>The topology is a known non-<c>Quad</c> value; skipped, never approximated.</summary>
    NonQuad,

    /// <summary>The texture page is still loading; skipped until it is ready.</summary>
    Pending,
}
