using Bork2d.Engine;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The shared <c>vkDeviceWaitIdle</c> wrapper used before swapchain recreation, surface recovery, and
/// texture-page capacity growth.
/// </summary>
/// <remarks>
/// Idle waits are forbidden in the steady-state frame loop; only recreation, growth, and shutdown may
/// call them (D-M6-15). A non-success result is a renderer fatal with the operation in the message so
/// the host can attribute the failure.
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
