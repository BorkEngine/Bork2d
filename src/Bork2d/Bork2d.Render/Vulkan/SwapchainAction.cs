namespace Bork2d.Render.Vulkan;

/// <summary>
/// The next swapchain action the windowed frame loop should take.
/// </summary>
internal enum SwapchainAction
{
    /// <summary>Present the acquired frame.</summary>
    Present,

    /// <summary>Skip acquire and present while the window is minimized or the extent is zero.</summary>
    SkipMinimized,

    /// <summary>Recreate the swapchain and retry the frame without presenting.</summary>
    RecreateAndRetry,

    /// <summary>Present the current frame, then recreate the swapchain on the next iteration.</summary>
    PresentThenRecreate,

    /// <summary>Attempt the single D-M6-16 surface-loss recovery.</summary>
    RecoverSurface,

    /// <summary>Stop the renderer with a fatal error.</summary>
    Fatal,
}
