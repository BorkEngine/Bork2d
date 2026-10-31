namespace Bork2d.Render.Textures;

/// <summary>
/// The renderer's answer to "which bindless page does this texture path use right now?" (M8 §6.5).
/// </summary>
internal enum TexturePageLookupKind
{
    /// <summary>Draw the placeholder page (index <c>0</c>).</summary>
    Placeholder,

    /// <summary>The page is still loading or uploading; skip the sprite this frame.</summary>
    Pending,

    /// <summary>The page is resident; draw it with <see cref="TexturePageLookup.Index"/>.</summary>
    Ready,
}

/// <summary>
/// The allocation-free result of <see cref="TexturePageStore.EnsurePage(string?)"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Width"/>/<see cref="Height"/> are the quad-size source. A ready page reports the
/// loaded descriptor's dimensions. A placeholder lookup reports the failed descriptor's declared
/// dimensions when that descriptor loaded and they are valid, and otherwise the placeholder's own
/// 8×8 extent (the owner-approved M8 sizing rule for failed/absent textures).
/// </para>
/// <para>
/// A pending lookup reports <c>0</c> for both dimensions; the sprite is skipped, so no size is
/// needed. The struct is a value type, so a steady-state lookup allocates nothing.
/// </para>
/// </remarks>
/// <param name="Kind">Whether the sprite draws the placeholder, waits, or draws a resident page.</param>
/// <param name="Index">The bindless page index; <c>0</c> is the placeholder.</param>
/// <param name="Width">The quad-size texture width in pixels, or <c>0</c> while pending.</param>
/// <param name="Height">The quad-size texture height in pixels, or <c>0</c> while pending.</param>
internal readonly record struct TexturePageLookup(
    TexturePageLookupKind Kind,
    int Index,
    int Width,
    int Height);
