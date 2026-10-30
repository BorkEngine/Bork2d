using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The opaque internal instance seam consumed by the window/platform side.
/// </summary>
/// <remarks>
/// The abstraction deliberately exposes no Vulkan API object: production code keeps the loader
/// handle inside <see cref="VulkanInstance"/>, while tests can inject a fake without loading
/// <c>vulkan-1.dll</c>. Handles are plain <see cref="nint"/> values so no Vulkan type leaves the
/// renderer implementation.
/// </remarks>
internal interface IVulkanInstance : IDisposable
{
    /// <summary>Gets the opaque <c>VkInstance</c> handle.</summary>
    nint Handle { get; }

    /// <summary>Gets a value indicating whether validation was actually enabled.</summary>
    bool ValidationEnabled { get; }

    /// <summary>Gets a value indicating whether the debug-utils labels are available.</summary>
    bool DebugUtilsEnabled { get; }

    /// <summary>Gets the enabled instance layers in creation order.</summary>
    IReadOnlyList<string> EnabledLayers { get; }

    /// <summary>Gets the enabled instance extensions in creation order.</summary>
    IReadOnlyList<string> EnabledExtensions { get; }

    /// <summary>Gets the number of fatal validation errors observed.</summary>
    int ValidationErrorCount { get; }

    /// <summary>Gets the number of validation warnings observed.</summary>
    int ValidationWarningCount { get; }

    /// <summary>Gets the first fatal validation error, if any.</summary>
    Exception? FatalException { get; }

    /// <summary>Destroys the instance and its debug messenger; idempotent.</summary>
    void Destroy();

    /// <summary>Destroys a surface owned by this instance.</summary>
    /// <param name="surface">The opaque <c>VkSurfaceKHR</c> handle; <c>0</c> is ignored.</param>
    void DestroySurface(nint surface);

    /// <summary>Labels one Vulkan object when debug utils are available.</summary>
    /// <param name="device">The opaque logical-device handle used for the call.</param>
    /// <param name="objectType">The object type being named.</param>
    /// <param name="objectHandle">The object handle being named.</param>
    /// <param name="name">The debug name.</param>
    void SetObjectName(nint device, ObjectType objectType, nint objectHandle, string name);

    /// <summary>Opens a debug label on a command buffer.</summary>
    /// <param name="device">The opaque logical-device handle used for the call.</param>
    /// <param name="commandBuffer">The opaque command-buffer handle.</param>
    /// <param name="label">The label text.</param>
    void BeginCommandBufferLabel(nint device, nint commandBuffer, string label);

    /// <summary>Closes the debug label opened by <see cref="BeginCommandBufferLabel"/>.</summary>
    /// <param name="device">The opaque logical-device handle used for the call.</param>
    /// <param name="commandBuffer">The opaque command-buffer handle.</param>
    void EndCommandBufferLabel(nint device, nint commandBuffer);
}
