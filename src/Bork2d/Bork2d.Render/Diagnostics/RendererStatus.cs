namespace Bork2d.Render.Diagnostics;

/// <summary>
/// The renderer lifecycle state observed by the host.
/// </summary>
internal enum RendererStatus
{
    /// <summary>The renderer has not been started.</summary>
    NotStarted,

    /// <summary>Initialization (device/target/swapchain) is in progress.</summary>
    Starting,

    /// <summary>The renderer is ready and frames can run.</summary>
    Ready,

    /// <summary>Stop was requested and shutdown is in progress.</summary>
    Stopping,

    /// <summary>The renderer joined cleanly.</summary>
    Stopped,

    /// <summary>The renderer stopped because of a fatal error.</summary>
    Faulted,
}
