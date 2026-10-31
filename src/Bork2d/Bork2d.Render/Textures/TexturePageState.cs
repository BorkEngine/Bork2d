namespace Bork2d.Render.Textures;

/// <summary>
/// The lifecycle state of one texture page (M8 §6.5).
/// </summary>
internal enum TexturePageState
{
    /// <summary>
    /// The asynchronous descriptor/payload load started; no page index is assigned and sprites that
    /// reference the page are skipped this frame.
    /// </summary>
    Pending,

    /// <summary>
    /// The content validated, the next real page index is assigned, and the staging copy is
    /// submitted with its own fence. Sprites are skipped until the fence signals.
    /// </summary>
    Uploading,

    /// <summary>
    /// The upload fence signaled and the descriptor is written into every per-frame bindless set.
    /// The page is drawable.
    /// </summary>
    Ready,

    /// <summary>
    /// The content is missing, malformed, or unsupported. The path is failed for the run, no page
    /// index is consumed, and sprites draw the placeholder instead.
    /// </summary>
    Failed,
}
