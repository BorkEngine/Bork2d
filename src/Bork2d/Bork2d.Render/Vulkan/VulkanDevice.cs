using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The selected physical/logical device, its queue, enabled features, extensions, and limits.
/// </summary>
/// <remarks>
/// The renderer thread owns the device. <see cref="Destroy"/> waits for the device to become idle
/// and destroys it without throwing on a cleanup failure (D-M6-27); it is idempotent. No surface
/// or swapchain is created here.
/// </remarks>
internal sealed unsafe class VulkanDevice : IDisposable
{
    private readonly Vk _vk;
    private readonly PhysicalDevice _physicalDevice;
    private readonly Device _device;
    private readonly ILogger _logger;
    private int _destroyed;

    internal VulkanDevice(
        Vk vk,
        Instance instance,
        PhysicalDevice physicalDevice,
        Device device,
        Queue queue,
        uint queueFamilyIndex,
        VulkanFeatureSet features,
        VulkanDeviceLimits limits,
        string[] enabledExtensions,
        ILogger logger)
    {
        _vk = vk;
        _physicalDevice = physicalDevice;
        _device = device;
        _logger = logger;
        Instance = instance;
        Queue = queue;
        QueueFamilyIndex = queueFamilyIndex;
        Features = features;
        Limits = limits;
        EnabledExtensions = enabledExtensions;
    }

    /// <summary>Gets the Vulkan API object that owns the device.</summary>
    internal Vk Api => _vk;

    /// <summary>Gets the opaque instance handle the device was created from.</summary>
    internal Instance Instance { get; }

    /// <summary>Gets the physical device handle.</summary>
    internal PhysicalDevice PhysicalDevice => _physicalDevice;

    /// <summary>Gets the logical device handle.</summary>
    internal Device Device => _device;

    /// <summary>Gets the single graphics (and windowed present) queue.</summary>
    internal Queue Queue { get; }

    /// <summary>Gets the selected queue family index.</summary>
    internal uint QueueFamilyIndex { get; }

    /// <summary>Gets the features enabled on the device.</summary>
    internal VulkanFeatureSet Features { get; }

    /// <summary>Gets the logged device limits.</summary>
    internal VulkanDeviceLimits Limits { get; }

    /// <summary>Gets the enabled device extensions in creation order.</summary>
    internal IReadOnlyList<string> EnabledExtensions { get; }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Waits for idle and destroys the device; idempotent.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        try
        {
            Result result = _vk.DeviceWaitIdle(_device);
            if (result != Result.Success)
            {
                _logger.LogWarning(
                    "Vulkan device idle wait returned {Result} during shutdown; destroying anyway.",
                    result);
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan device idle wait failed during shutdown.");
        }

        try
        {
            _vk.DestroyDevice(_device, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan device destruction failed.");
        }
        finally
        {
            _vk.CurrentDevice = null;
            _vk.Dispose();
        }
    }
}
