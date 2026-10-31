namespace Bork2d.Render;

/// <summary>
/// One coalesced window-state change posted to the render thread.
/// </summary>
/// <param name="MessageKind">The property the message describes.</param>
/// <param name="Extent">The framebuffer extent for <see cref="Kind.Extent"/>; otherwise default.</param>
/// <param name="Flag">The value for <see cref="Kind.Fullscreen"/> or <see cref="Kind.Minimized"/>.</param>
internal readonly record struct RendererControlMessage(
    RendererControlMessage.Kind MessageKind,
    WindowExtent Extent,
    bool Flag)
{
    /// <summary>The property a control message describes.</summary>
    internal enum Kind
    {
        /// <summary>The framebuffer extent changed.</summary>
        Extent,

        /// <summary>The fullscreen state changed.</summary>
        Fullscreen,

        /// <summary>The minimized state changed.</summary>
        Minimized,
    }
}
