using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One renderer's fixed array of GPU frames in flight plus the operations both target drivers share.
/// </summary>
/// <remarks>
/// <para>
/// The set starts empty and is attached once during startup, after the target driver created every
/// frame. It owns no Vulkan object itself: the frames stay target-owned, and the set only applies the
/// shared per-frame operations (bindless descriptor growth and page writes, the shutdown fence wait,
/// the instance-buffer counters, and frame disposal) in one place.
/// </para>
/// <para>
/// All members are render-thread-only except the diagnostic counter reads, which observe the same
/// live frame fields the old per-renderer properties read. <see cref="DisposeFrames"/> is idempotent
/// and never throws because frame disposal is best-effort.
/// </para>
/// </remarks>
/// <typeparam name="TFrame">The target driver's concrete frame type.</typeparam>
internal sealed unsafe class VulkanFrameSet<TFrame> : IVulkanFrameSet
    where TFrame : class, IVulkanFrame
{
    private const ulong CleanupFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly string _fenceLogName;
    private readonly ILogger _logger;
    private TFrame?[] _frames = [];
    private int _stopLogInstanceCapacity;
    private int _stopLogInstanceGrowths;

    /// <summary>Initializes a new instance of the <see cref="VulkanFrameSet{TFrame}"/> class.</summary>
    /// <param name="fenceLogName">
    /// The shutdown fence-wait log prefix, for example <c>Vulkan frame fence</c> or
    /// <c>Vulkan offscreen frame fence</c>.
    /// </param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    internal VulkanFrameSet(string fenceLogName, ILogger logger)
    {
        ArgumentException.ThrowIfNullOrEmpty(fenceLogName);
        ArgumentNullException.ThrowIfNull(logger);
        _fenceLogName = fenceLogName;
        _logger = logger;
    }

    /// <summary>Gets the frame at <paramref name="index"/>, or <see langword="null"/> before startup.</summary>
    /// <param name="index">The GPU frame index.</param>
    internal TFrame? this[int index] => _frames[index];

    /// <summary>Gets a value indicating whether every GPU frame owns its instance resources.</summary>
    internal bool HasInstanceResources
    {
        get
        {
            if (_frames.Length == 0)
            {
                return false;
            }

            for (var index = 0; index < _frames.Length; index++)
            {
                if (_frames[index]?.InstanceResources is null)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Gets the largest instance-buffer capacity among the frames.</summary>
    internal int InstanceCapacityHighWater
    {
        get
        {
            var capacity = 0;
            for (var index = 0; index < _frames.Length; index++)
            {
                FrameInstanceResources? resources = _frames[index]?.InstanceResources;
                if (resources is not null)
                {
                    capacity = Math.Max(capacity, resources.InstanceCapacity);
                }
            }

            return capacity;
        }
    }

    /// <summary>Gets the total number of instance-buffer growths across the frames.</summary>
    internal int InstanceGrowthCount
    {
        get
        {
            var growths = 0;
            for (var index = 0; index < _frames.Length; index++)
            {
                growths += _frames[index]?.InstanceResources.GrowthCount ?? 0;
            }

            return growths;
        }
    }

    /// <summary>Gets the instance-buffer high-water mark recorded for the stop log.</summary>
    internal int StopLogInstanceCapacity => _stopLogInstanceCapacity;

    /// <summary>Gets the total instance-buffer growth count recorded for the stop log.</summary>
    internal int StopLogInstanceGrowths => _stopLogInstanceGrowths;

    int IVulkanFrameSet.Count => _frames.Length;

    bool IVulkanFrameSet.HasInstanceResources => HasInstanceResources;

    int IVulkanFrameSet.InstanceCapacityHighWater => InstanceCapacityHighWater;

    int IVulkanFrameSet.InstanceGrowthCount => InstanceGrowthCount;

    int IVulkanFrameSet.StopLogInstanceCapacity => _stopLogInstanceCapacity;

    int IVulkanFrameSet.StopLogInstanceGrowths => _stopLogInstanceGrowths;

    /// <summary>Replaces the empty startup set with the created frames.</summary>
    /// <param name="frames">The frames created by the target driver, in index order.</param>
    internal void Attach(TFrame?[] frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        _frames = frames;
    }

    /// <summary>Waits the device idle and recreates every frame's bindless set at a new capacity.</summary>
    /// <param name="device">The selected logical device.</param>
    /// <param name="capacity">The new bindless page capacity, including the placeholder.</param>
    internal void EnsureBindlessCapacity(VulkanDevice device, int capacity)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Growth happens between frames: the device is idle and every in-flight frame's fence has
        // signaled before the per-frame descriptor pools/sets are recreated.
        VulkanDeviceIdle.Wait(device, "texture page capacity growth");

        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.InstanceResources.MarkFenceSignaled();
            _frames[index]?.InstanceResources.RecreateBindless(capacity);
        }
    }

    /// <summary>Writes one page descriptor into every frame's bindless set.</summary>
    /// <param name="pageIndex">The bindless array element.</param>
    /// <param name="imageView">The page image view in <c>SHADER_READ_ONLY_OPTIMAL</c>.</param>
    /// <param name="sampler">The cached sampler for the page's filter/wrap pair.</param>
    internal void WritePage(int pageIndex, ImageView imageView, Sampler sampler)
    {
        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.InstanceResources.WritePage(pageIndex, imageView, sampler);
        }
    }

    /// <summary>Waits every frame fence with a bounded timeout during shutdown; best-effort.</summary>
    /// <param name="device">The selected logical device.</param>
    internal void WaitForFences(VulkanDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        for (var index = 0; index < _frames.Length; index++)
        {
            TFrame? frame = _frames[index];
            if (frame is null)
            {
                continue;
            }

            Fence fence = frame.Fence;
            try
            {
                Result result = device.Api.WaitForFences(
                    device.Device,
                    1,
                    &fence,
                    true,
                    CleanupFenceTimeoutNanoseconds);
                if (result != Result.Success)
                {
                    _logger.LogWarning(
                        "{FenceLogName} wait returned {Result} during shutdown.",
                        _fenceLogName,
                        result);
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "{FenceLogName} wait failed during shutdown.",
                    _fenceLogName);
            }
        }
    }

    /// <summary>Records the instance-buffer counters before the frames are destroyed for the stop log.</summary>
    internal void CaptureStopCounters()
    {
        _stopLogInstanceCapacity = InstanceCapacityHighWater;
        _stopLogInstanceGrowths = InstanceGrowthCount;
    }

    /// <summary>Destroys every frame and empties the set; idempotent and never throws.</summary>
    internal void DisposeFrames()
    {
        for (var index = 0; index < _frames.Length; index++)
        {
            _frames[index]?.Dispose();
            _frames[index] = null;
        }

        _frames = [];
    }

    void IVulkanFrameSet.CaptureStopCounters() => CaptureStopCounters();

    void IVulkanFrameSet.EnsureBindlessCapacity(VulkanDevice device, int capacity)
        => EnsureBindlessCapacity(device, capacity);

    void IVulkanFrameSet.WritePage(int pageIndex, ImageView imageView, Sampler sampler)
        => WritePage(pageIndex, imageView, sampler);

    void IVulkanFrameSet.WaitForFences(VulkanDevice device) => WaitForFences(device);

    void IVulkanFrameSet.DisposeFrames() => DisposeFrames();

    IVulkanFrame? IVulkanFrameSet.GetFrame(int index) => _frames[index];
}
