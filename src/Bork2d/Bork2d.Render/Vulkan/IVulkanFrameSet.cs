using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The shared operations on a renderer's fixed set of GPU frames in flight.
/// </summary>
/// <remarks>
/// <para>
/// Both target drivers own the same frame-slot operations: bindless descriptor growth and page
/// writes across every frame, shutdown fence waits, the per-frame capture retirement pass, and the
/// instance-buffer high-water counters used by diagnostics and tests. One composition implements
/// them so the windowed and offscreen renderers keep only their target-specific frame creation and
/// per-frame recording.
/// </para>
/// <para>
/// The interface is the narrow seam the capture and texture services consume; the typed
/// <see cref="VulkanFrameSet{TFrame}"/> implementation lets each target driver index its own frame
/// type without casts.
/// </para>
/// </remarks>
internal interface IVulkanFrameSet
{
    /// <summary>Gets the number of GPU frames in flight.</summary>
    int Count { get; }

    /// <summary>Gets the frame at <paramref name="index"/>, or <see langword="null"/> before startup.</summary>
    /// <param name="index">The GPU frame index.</param>
    /// <returns>The frame, or <see langword="null"/> when it does not exist yet.</returns>
    IVulkanFrame? GetFrame(int index);

    /// <summary>Gets a value indicating whether every GPU frame owns its instance resources.</summary>
    bool HasInstanceResources { get; }

    /// <summary>Gets the largest instance-buffer capacity among the frames.</summary>
    int InstanceCapacityHighWater { get; }

    /// <summary>Gets the total number of instance-buffer growths across the frames.</summary>
    int InstanceGrowthCount { get; }

    /// <summary>Records the instance-buffer counters before the frames are destroyed for the stop log.</summary>
    void CaptureStopCounters();

    /// <summary>Gets the instance-buffer high-water mark recorded for the stop log.</summary>
    int StopLogInstanceCapacity { get; }

    /// <summary>Gets the total instance-buffer growth count recorded for the stop log.</summary>
    int StopLogInstanceGrowths { get; }

    /// <summary>Waits every frame fence and recreates every frame's bindless set at a new capacity.</summary>
    /// <param name="device">The selected logical device.</param>
    /// <param name="capacity">The new bindless page capacity, including the placeholder.</param>
    void EnsureBindlessCapacity(VulkanDevice device, int capacity);

    /// <summary>Writes one page descriptor into every frame's bindless set.</summary>
    /// <param name="pageIndex">The bindless array element.</param>
    /// <param name="imageView">The page image view in <c>SHADER_READ_ONLY_OPTIMAL</c>.</param>
    /// <param name="sampler">The cached sampler for the page's filter/wrap pair.</param>
    void WritePage(int pageIndex, ImageView imageView, Sampler sampler);

    /// <summary>Waits every frame fence with a bounded timeout during shutdown; best-effort.</summary>
    /// <param name="device">The selected logical device.</param>
    void WaitForFences(VulkanDevice device);

    /// <summary>Destroys every frame and empties the set; idempotent and never throws.</summary>
    void DisposeFrames();
}
