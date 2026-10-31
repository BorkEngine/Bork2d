namespace Bork2d.Render.Diagnostics;

/// <summary>
/// Why the renderer stopped.
/// </summary>
internal enum RendererStopReason
{
    /// <summary>The renderer has not stopped.</summary>
    None,

    /// <summary>The host requested a graceful stop.</summary>
    Requested,

    /// <summary>A fatal Vulkan or renderer error stopped the renderer.</summary>
    Fatal,
}
