using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The capture service shared by the windowed and offscreen renderers: the bounded request queue,
/// per-frame capture retirement from the frame fence, and the stop/fatal close path.
/// </summary>
/// <remarks>
/// <para>
/// The service owns the <see cref="CaptureRequestQueue"/> and operates on the renderer's
/// <see cref="IVulkanFrameSet"/> for everything that needs a GPU frame. A target driver still
/// records its own copy (the swapchain or offscreen image and its present/discard transition) and
/// marks the frame submitted; the service only moves requests and completes or faults them.
/// </para>
/// <para>
/// Retirement runs at the top of every render loop iteration and waits only frames that carry
/// requests, so an idle loop still blocks on the snapshot/control wait without touching Vulkan. The
/// snapshot slot is not released here; it stays held until the frame is reused or shutdown, as
/// before. Closing the queue is idempotent: a stop and a later fatal share the first exception, and
/// requests that arrive afterwards fault with the queue's own closed exception.
/// </para>
/// </remarks>
internal sealed unsafe class RendererCaptureService
{
    private readonly CaptureRequestQueue _queue = new();
    private readonly IVulkanFrameSet _frames;
    private readonly ObjectDisposedException _stoppedException;

    /// <summary>Initializes a new instance of the <see cref="RendererCaptureService"/> class.</summary>
    /// <param name="frames">The renderer's GPU frames in flight; the service does not own them.</param>
    /// <param name="ownerName">The renderer type name reported by the stop failure.</param>
    /// <param name="modeDescription">The renderer mode name used in the stop failure message.</param>
    internal RendererCaptureService(IVulkanFrameSet frames, string ownerName, string modeDescription)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentException.ThrowIfNullOrEmpty(ownerName);
        ArgumentException.ThrowIfNullOrEmpty(modeDescription);

        _frames = frames;
        _stoppedException = new ObjectDisposedException(
            ownerName,
            $"The {modeDescription} renderer stopped before the capture request could be served.");
    }

    /// <summary>Queues one one-shot capture request thread-safely.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The captured frame once the serving frame's fence signals, or a faulted task.</returns>
    internal Task<FrameCaptureResult> Request(CancellationToken cancellationToken)
        => _queue.Request(cancellationToken);

    /// <summary>
    /// Takes the queue's pending requests into the frame and prepares its staging buffer.
    /// </summary>
    /// <param name="frame">The frame about to be recorded.</param>
    /// <param name="width">The captured frame width in pixels.</param>
    /// <param name="height">The captured frame height in pixels.</param>
    /// <param name="format">The target's color format.</param>
    /// <returns><see langword="true"/> when a copy must be recorded.</returns>
    internal bool BeginRecord(IVulkanFrame frame, int width, int height, Format format)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Capture.BeginRecord(_queue, width, height, format);
    }

    /// <summary>Marks the frame's fence observed and completes its attached capture requests.</summary>
    /// <param name="frame">The frame whose fence was just observed.</param>
    internal void CompleteFrame(IVulkanFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        frame.InstanceResources.MarkFenceSignaled();
        frame.Capture.Complete();
    }

    /// <summary>
    /// Completes captures whose frame fence has signaled, independent of frame reuse and snapshots.
    /// </summary>
    /// <param name="device">The selected logical device.</param>
    /// <remarks>
    /// Runs at the top of every render-loop iteration. Only frames that carry capture requests are
    /// waited on, so an idle loop still blocks on the snapshot/control wait without touching Vulkan.
    /// </remarks>
    /// <exception cref="Bork2dException">A frame fence wait failed.</exception>
    internal void RetireCompletedCaptures(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        for (var index = 0; index < _frames.Count; index++)
        {
            IVulkanFrame? frame = _frames.GetFrame(index);
            if (frame is null || !frame.Capture.HasRequests)
            {
                continue;
            }

            Fence fence = frame.Fence;
            Result wait = device.Api.WaitForFences(
                device.Device,
                1,
                &fence,
                true,
                ulong.MaxValue);
            if (wait != Result.Success)
            {
                throw new Bork2dException($"vkWaitForFences failed with VkResult {wait}.");
            }

            CompleteFrame(frame);
        }
    }

    /// <summary>
    /// Completes every frame's attached requests after shutdown observed all fences and went idle.
    /// </summary>
    internal void CompleteAll()
    {
        for (var index = 0; index < _frames.Count; index++)
        {
            _frames.GetFrame(index)?.Capture.Complete();
        }
    }

    /// <summary>Closes the queue and faults every pending request; idempotent.</summary>
    /// <param name="exception">The stop/fatal failure every pending request observes.</param>
    internal void Close(Exception exception) => _queue.Close(exception);

    /// <summary>Closes the queue for a graceful stop with the renderer-specific stop failure.</summary>
    internal void CloseForStop() => _queue.Close(_stoppedException);
}
