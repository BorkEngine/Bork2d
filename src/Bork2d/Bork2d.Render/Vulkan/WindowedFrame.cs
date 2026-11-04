using Bork2d.Engine;
using Bork2d.Render.TwoDimensional;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Semaphore = Silk.NET.Vulkan.Semaphore;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's command pool, command buffer, fence, image-available semaphore, linear scene color
/// image, and the empty instance resources (instance SSBO, frame descriptor sets, and present
/// descriptor set, Q10=J1) (D-M6-11).
/// </summary>
/// <remarks>
/// The render thread owns the frame. The fence is created signaled so the first wait succeeds
/// immediately; reuse is gated by the fence. The render-finished semaphores are per swapchain image
/// (owned by <see cref="WindowedSwapchain"/>), not per frame, so a presentation operation that still
/// uses a semaphore can never race a later submit that reuses it. The scene image is recreated at the
/// new extent after a swapchain recreation, which already idled the device.
/// <see cref="Dispose"/> is idempotent and never throws (D-M6-27).
/// </remarks>
internal sealed unsafe class WindowedFrame : IVulkanFrame
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly int _frameIndex;
    private int _destroyed;

    private WindowedFrame(
        Vk vk,
        Device device,
        ILogger logger,
        int frameIndex,
        CommandPool pool,
        CommandBuffer commandBuffer,
        Fence fence,
        Semaphore imageAvailable,
        FrameInstanceResources instanceResources,
        SceneTarget scene,
        FrameCapture capture)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _frameIndex = frameIndex;
        Pool = pool;
        CommandBuffer = commandBuffer;
        Fence = fence;
        ImageAvailable = imageAvailable;
        InstanceResources = instanceResources;
        Scene = scene;
        Capture = capture;
    }

    /// <summary>Gets the frame's command pool.</summary>
    internal CommandPool Pool { get; }

    /// <summary>Gets the frame's primary command buffer.</summary>
    internal CommandBuffer CommandBuffer { get; }

    /// <summary>Gets the in-flight fence.</summary>
    internal Fence Fence { get; }

    /// <summary>Gets the image-available semaphore signalled by acquire.</summary>
    internal Semaphore ImageAvailable { get; }

    /// <summary>Gets the empty instance buffer and frame descriptor sets (Q10=J1).</summary>
    internal FrameInstanceResources InstanceResources { get; }

    /// <summary>Gets the frame's linear scene color image the 2D passes render into.</summary>
    internal SceneTarget Scene { get; private set; }

    /// <summary>Gets the frame's pooled readback staging and pending capture requests (M8 §6.9).</summary>
    internal FrameCapture Capture { get; }

    /// <inheritdoc/>
    Fence IVulkanFrame.Fence => Fence;

    /// <inheritdoc/>
    FrameInstanceResources IVulkanFrame.InstanceResources => InstanceResources;

    /// <inheritdoc/>
    FrameCapture IVulkanFrame.Capture => Capture;

    /// <summary>Creates one frame's Vulkan objects.</summary>
    /// <param name="vk">The Vulkan API object.</param>
    /// <param name="device">The logical device.</param>
    /// <param name="physicalDevice">The physical device used to select instance-buffer memory.</param>
    /// <param name="frameIndex">The GPU frame index, used for debug names.</param>
    /// <param name="queueFamilyIndex">The graphics queue family.</param>
    /// <param name="extent">The swapchain extent the frame's scene image matches.</param>
    /// <param name="pipeline">The 2D pipeline that owns the descriptor-set layout.</param>
    /// <param name="present">
    /// The present pipeline that owns the present descriptor-set layout and sampler.
    /// </param>
    /// <param name="pageCapacity">The initial bindless page capacity, including the placeholder.</param>
    /// <param name="maxStorageBufferRange">The device's <c>maxStorageBufferRange</c> limit.</param>
    /// <param name="maxUniformBufferRange">The device's <c>maxUniformBufferRange</c> limit.</param>
    /// <param name="minUniformBufferOffsetAlignment">
    /// The device's <c>minUniformBufferOffsetAlignment</c> limit.
    /// </param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger.</param>
    /// <returns>The created frame.</returns>
    /// <exception cref="Bork2dException">A Vulkan object could not be created.</exception>
    internal static WindowedFrame Create(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        int frameIndex,
        uint queueFamilyIndex,
        Extent2D extent,
        TwoDimensionalPipeline pipeline,
        PresentPipeline present,
        int pageCapacity,
        uint maxStorageBufferRange,
        uint maxUniformBufferRange,
        ulong minUniformBufferOffsetAlignment,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var pool = default(CommandPool);
        var commandBuffer = default(CommandBuffer);
        var fence = default(Fence);
        var imageAvailable = default(Semaphore);
        FrameInstanceResources? instanceResources = null;
        SceneTarget? scene = null;
        try
        {
            var poolInfo = new CommandPoolCreateInfo
            {
                SType = StructureType.CommandPoolCreateInfo,
                Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
                QueueFamilyIndex = queueFamilyIndex,
            };
            Result result = vk.CreateCommandPool(device, in poolInfo, null, out pool);
            if (result != Result.Success)
            {
                throw new Bork2dException($"vkCreateCommandPool failed with VkResult {result}.");
            }

            var allocateInfo = new CommandBufferAllocateInfo
            {
                SType = StructureType.CommandBufferAllocateInfo,
                CommandPool = pool,
                Level = CommandBufferLevel.Primary,
                CommandBufferCount = 1,
            };
            result = vk.AllocateCommandBuffers(device, in allocateInfo, out commandBuffer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkAllocateCommandBuffers failed with VkResult {result}.");
            }

            var fenceInfo = new FenceCreateInfo
            {
                SType = StructureType.FenceCreateInfo,
                Flags = FenceCreateFlags.SignaledBit,
            };
            result = vk.CreateFence(device, in fenceInfo, null, out fence);
            if (result != Result.Success)
            {
                throw new Bork2dException($"vkCreateFence failed with VkResult {result}.");
            }

            var semaphoreInfo = new SemaphoreCreateInfo
            {
                SType = StructureType.SemaphoreCreateInfo,
            };
            result = vk.CreateSemaphore(device, in semaphoreInfo, null, out imageAvailable);
            if (result != Result.Success)
            {
                throw new Bork2dException($"vkCreateSemaphore failed with VkResult {result}.");
            }

            instanceResources = FrameInstanceResources.Create(
                vk,
                device,
                physicalDevice,
                frameIndex,
                pipeline,
                present,
                pageCapacity,
                maxStorageBufferRange,
                maxUniformBufferRange,
                minUniformBufferOffsetAlignment,
                instance,
                logger);
            scene = SceneTarget.Create(vk, device, physicalDevice, extent, frameIndex, instance, logger);
            instanceResources.WritePresentView(scene.View);

            return new WindowedFrame(
                vk,
                device,
                logger,
                frameIndex,
                pool,
                commandBuffer,
                fence,
                imageAvailable,
                instanceResources,
                scene,
                new FrameCapture(vk, device, physicalDevice, frameIndex, logger));
        }
        catch
        {
            scene?.Destroy();
            instanceResources?.Destroy();
            DestroyBestEffort(vk, device, logger, pool, fence, imageAvailable);
            throw;
        }
    }

    /// <summary>
    /// Replaces the frame's linear scene image with one for a new swapchain extent.
    /// </summary>
    /// <remarks>
    /// The caller must have idled the device (swapchain recreation does), so no in-flight frame can
    /// still reference the previous image. The replacement is created before the old target is
    /// destroyed, and the frame's present descriptor is rewritten for the new view.
    /// </remarks>
    /// <param name="device">The logical device and its physical device.</param>
    /// <param name="extent">The new swapchain extent in pixels.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <exception cref="Bork2dException">The replacement scene image could not be created.</exception>
    internal void RecreateScene(
        VulkanDevice device,
        Extent2D extent,
        IVulkanInstance? instance)
    {
        ArgumentNullException.ThrowIfNull(device);

        SceneTarget replacement = SceneTarget.Create(
            device.Api,
            device.Device,
            device.PhysicalDevice,
            extent,
            _frameIndex,
            instance,
            _logger);
        SceneTarget previous = Scene;
        Scene = replacement;
        previous.Destroy();
        InstanceResources.WritePresentView(Scene.View);
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the frame's Vulkan objects; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        Capture.Destroy();
        Scene.Destroy();
        InstanceResources.Destroy();
        DestroyBestEffort(
            _vk,
            _device,
            _logger,
            Pool,
            Fence,
            ImageAvailable);
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        CommandPool pool,
        Fence fence,
        Semaphore imageAvailable)
    {
        if (imageAvailable.Handle != 0)
        {
            try
            {
                vk.DestroySemaphore(device, imageAvailable, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan image-available semaphore destruction failed.");
            }
        }

        if (fence.Handle != 0)
        {
            try
            {
                vk.DestroyFence(device, fence, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan frame fence destruction failed.");
            }
        }

        if (pool.Handle != 0)
        {
            try
            {
                // Destroying the pool also frees the command buffer allocated from it.
                vk.DestroyCommandPool(device, pool, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan command pool destruction failed.");
            }
        }
    }
}
