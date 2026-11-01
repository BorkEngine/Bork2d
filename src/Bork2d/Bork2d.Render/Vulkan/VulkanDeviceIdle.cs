using Bork2d.Engine;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The shared <c>vkDeviceWaitIdle</c> wrapper used before swapchain recreation and surface recovery.
/// </summary>
/// <remarks>
/// Idle waits are forbidden in the steady-state frame loop and are not used for texture-page
/// capacity growth: growth retires the in-flight frame fences instead (D-M6-15). Recreation,
/// surface recovery, and shutdown may still call them. A non-success result is a renderer fatal
/// with the operation in the message so the host can attribute the failure.
/// </remarks>
internal static class VulkanDeviceIdle
{
    /// <summary>Waits for the device to become idle, throwing a renderer fatal on failure.</summary>
    /// <param name="device">The selected logical device.</param>
    /// <param name="operation">The operation performing the wait, for diagnostics.</param>
    /// <exception cref="Bork2dException">The idle wait did not succeed.</exception>
    internal static void Wait(VulkanDevice device, string operation)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentException.ThrowIfNullOrEmpty(operation);

        Result result = device.Api.DeviceWaitIdle(device.Device);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkDeviceWaitIdle failed with VkResult {result} during {operation}.");
        }
    }
}
