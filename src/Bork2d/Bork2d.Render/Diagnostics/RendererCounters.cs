namespace Bork2d.Render.Diagnostics;

/// <summary>
/// The renderer counters logged once at stop and exposed to tests.
/// </summary>
/// <param name="FramesPresented">The number of windowed frames presented.</param>
/// <param name="FramesDiscarded">The number of offscreen frames rendered and discarded.</param>
/// <param name="SwapchainRecreations">The number of swapchain recreations.</param>
/// <param name="ValidationErrorCount">The number of fatal validation errors observed.</param>
/// <param name="ValidationWarningCount">The number of validation warnings observed.</param>
/// <param name="LastFrameNumber">The last frame number the renderer began.</param>
/// <param name="LastPresentResult">The last present result as its numeric Vulkan result value.</param>
internal readonly record struct RendererCounters(
    long FramesPresented,
    long FramesDiscarded,
    long SwapchainRecreations,
    int ValidationErrorCount,
    int ValidationWarningCount,
    long LastFrameNumber,
    int LastPresentResult)
{
    /// <summary>Gets the zeroed counter set used before the first frame.</summary>
    internal static RendererCounters Empty => default;
}
