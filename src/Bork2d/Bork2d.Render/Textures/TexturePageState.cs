namespace Bork2d.Render.Textures;

/// <summary>
/// The lifecycle state of one texture page (M8 §6.5).
/// </summary>
internal enum TexturePageState
{
    /// <summary>
    /// The asynchronous descriptor/payload load started, or the validated content is queued for an
    /// upload the per-frame budget has not admitted yet. No page index is assigned and sprites that
    /// reference the page are skipped this frame.
    /// </summary>
    Pending,

    /// <summary>
    /// The content validated, the upload budget admitted the page, the next real page index is
    /// assigned, and the staging copy is submitted with its own fence. Sprites are skipped until the
    /// fence signals.
    /// </summary>
    Uploading,

    /// <summary>
    /// The upload fence signaled and the descriptor is written into every per-frame bindless set.
    /// The page is drawable.
    /// </summary>
    Ready,

    /// <summary>
    /// The content is missing, malformed, or unsupported. The path is failed for the current asset
    /// catalog, no page index is consumed, and sprites draw the placeholder instead. A later mount
    /// retires the failure and the path is retried.
    /// </summary>
    Failed,
}
