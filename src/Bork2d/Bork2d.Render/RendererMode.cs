namespace Bork2d.Render;

/// <summary>
/// The host mode the renderer is started in.
/// </summary>
internal enum RendererMode
{
    /// <summary>A window surface and swapchain are presented.</summary>
    Windowed,

    /// <summary>A fixed-extent offscreen target is cleared and discarded; no surface exists.</summary>
    Offscreen,
}
