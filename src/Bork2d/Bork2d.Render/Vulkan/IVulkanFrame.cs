using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The target-independent view of one GPU frame in flight that the shared capture, texture, and
/// cleanup services operate on.
/// </summary>
/// <remarks>
/// <see cref="WindowedFrame"/> and <see cref="OffscreenFrame"/> keep owning their target-specific
/// Vulkan objects (the windowed frame adds the acquire semaphore): the shared services only need the
/// fence, the per-frame instance resources, and the capture state. This is the seam that lets one
/// capture service and one frame-set composition serve both target drivers without moving swapchain
/// or offscreen recording into them.
/// </remarks>
internal interface IVulkanFrame : IDisposable
{
    /// <summary>Gets the in-flight fence.</summary>
    Fence Fence { get; }

    /// <summary>Gets the empty instance buffer and frame descriptor set.</summary>
    FrameInstanceResources InstanceResources { get; }

    /// <summary>Gets the pooled readback staging and pending capture requests.</summary>
    FrameCapture Capture { get; }
}
