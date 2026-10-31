using Bork2d.Engine;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// Two permanent offscreen sRGB color targets with views and one device-local allocation per image
/// (D-M6-03, D-M6-11, D-M6-26).
/// </summary>
/// <remarks>
/// <para>
/// The render thread owns the target. Images start in <c>VK_IMAGE_LAYOUT_UNDEFINED</c> and are
/// transitioned once to <c>COLOR_ATTACHMENT_OPTIMAL</c>; every frame clears in that layout and it is
/// never transitioned again. There is no sampler, readback, or host-visible memory in M6
/// (D-M6-23); the clear pass writes every pixel, so zero-initialization is unnecessary.
/// </para>
/// <para>
/// <see cref="Dispose"/> is best-effort, idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class OffscreenTarget : IDisposable
{
    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly Image[] _images;
    private readonly ImageView[] _views;
    private readonly DeviceMemory[] _memory;
    private int _destroyed;

    private OffscreenTarget(
        Vk vk,
        Device device,
        ILogger logger,
        Image[] images,
        ImageView[] views,
        DeviceMemory[] memory,
        Extent2D extent,
        Format format,
        ulong memoryBytes)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _images = images;
        _views = views;
        _memory = memory;
        Extent = extent;
        Format = format;
        MemoryBytes = memoryBytes;
    }

    /// <summary>Gets the fixed target extent.</summary>
    internal Extent2D Extent { get; }

    /// <summary>Gets the selected sRGB color format.</summary>
    internal Format Format { get; }

    /// <summary>Gets the number of permanent color images (two, D-M6-11).</summary>
    internal int ImageCount => _images.Length;

    /// <summary>Gets the total device memory allocated for the images in bytes.</summary>
    internal ulong MemoryBytes { get; }

    /// <summary>Gets the color image for a frame index.</summary>
    /// <param name="index">The frame/image index.</param>
    /// <returns>The image that receives the clear.</returns>
    internal Image GetImage(int index) => _images[index];

    /// <summary>Gets the color image view for a frame index.</summary>
    /// <param name="index">The frame/image index.</param>
    /// <returns>The view used as the dynamic-rendering attachment.</returns>
    internal ImageView GetView(int index) => _views[index];

    /// <summary>Creates the offscreen color targets for one fixed extent.</summary>
    /// <param name="device">The logical device and its physical device.</param>
    /// <param name="extent">The validated fixed extent in pixels.</param>
    /// <param name="format">The selected sRGB color format.</param>
    /// <param name="imageCount">The number of permanent color images.</param>
    /// <param name="instance">The instance seam used for debug labels.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger.</param>
    /// <returns>The created target. The caller owns and disposes it.</returns>
    /// <exception cref="Bork2dException">A Vulkan object could not be created.</exception>
    internal static OffscreenTarget Create(
        VulkanDevice device,
        WindowExtent extent,
        Format format,
        int imageCount,
        IVulkanInstance instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(logger);
        if (imageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(imageCount),
                imageCount,
                "At least one offscreen color image is required.");
        }

        Vk vk = device.Api;
        Device logicalDevice = device.Device;
        var images = new Image[imageCount];
        var views = new ImageView[imageCount];
        var memory = new DeviceMemory[imageCount];
        var extent3D = new Extent3D
        {
            Width = (uint)extent.Width,
            Height = (uint)extent.Height,
            Depth = 1,
        };
        var components = new ComponentMapping
        {
            R = ComponentSwizzle.Identity,
            G = ComponentSwizzle.Identity,
            B = ComponentSwizzle.Identity,
            A = ComponentSwizzle.Identity,
        };
        var range = new ImageSubresourceRange
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = format,
            Extent = extent3D,
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };

        PhysicalDeviceMemoryProperties memoryProperties =
            vk.GetPhysicalDeviceMemoryProperties(device.PhysicalDevice);
        ulong memoryBytes = 0;
        try
        {
            for (var index = 0; index < imageCount; index++)
            {
                Result result = vk.CreateImage(
                    logicalDevice,
                    in imageInfo,
                    null,
                    out images[index]);
                if (result != Result.Success)
                {
                    throw new Bork2dException(
                        $"vkCreateImage failed for offscreen image {index} with VkResult {result}.");
                }

                MemoryRequirements requirements = vk.GetImageMemoryRequirements(
                    logicalDevice,
                    images[index]);
                uint memoryTypeIndex = SelectMemoryType(
                    memoryProperties,
                    requirements.MemoryTypeBits,
                    index);
                var allocateInfo = new MemoryAllocateInfo
                {
                    SType = StructureType.MemoryAllocateInfo,
                    AllocationSize = requirements.Size,
                    MemoryTypeIndex = memoryTypeIndex,
                };
                result = vk.AllocateMemory(
                    logicalDevice,
                    in allocateInfo,
                    null,
                    out memory[index]);
                if (result != Result.Success)
                {
                    throw new Bork2dException(
                        $"vkAllocateMemory failed for offscreen image {index} with VkResult "
                            + $"{result} ({requirements.Size} bytes, memory type "
                            + $"{memoryTypeIndex}).");
                }

                result = vk.BindImageMemory(
                    logicalDevice,
                    images[index],
                    memory[index],
                    0);
                if (result != Result.Success)
                {
                    throw new Bork2dException(
                        $"vkBindImageMemory failed for offscreen image {index} with VkResult "
                            + $"{result}.");
                }

                var viewInfo = new ImageViewCreateInfo
                {
                    SType = StructureType.ImageViewCreateInfo,
                    Image = images[index],
                    ViewType = ImageViewType.Type2D,
                    Format = format,
                    Components = components,
                    SubresourceRange = range,
                };
                result = vk.CreateImageView(
                    logicalDevice,
                    in viewInfo,
                    null,
                    out views[index]);
                if (result != Result.Success)
                {
                    throw new Bork2dException(
                        $"vkCreateImageView failed for offscreen image {index} with VkResult "
                            + $"{result}.");
                }

                memoryBytes += requirements.Size;
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.Image,
                    (nint)images[index].Handle,
                    $"Bork2d offscreen color image {index}");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.ImageView,
                    (nint)views[index].Handle,
                    $"Bork2d offscreen color image view {index}");
            }

            return new OffscreenTarget(
                vk,
                logicalDevice,
                logger,
                images,
                views,
                memory,
                new Extent2D((uint)extent.Width, (uint)extent.Height),
                format,
                memoryBytes);
        }
        catch
        {
            DestroyBestEffort(vk, logicalDevice, logger, views, images, memory);
            throw;
        }
    }

    /// <summary>
    /// Records the one-time <c>UNDEFINED → COLOR_ATTACHMENT_OPTIMAL</c> transition for every image.
    /// </summary>
    /// <param name="commandBuffer">A command buffer the caller begins and submits.</param>
    internal void RecordInitialLayoutTransition(CommandBuffer commandBuffer)
    {
        for (var index = 0; index < _images.Length; index++)
        {
            var barrier = new ImageMemoryBarrier2
            {
                SType = StructureType.ImageMemoryBarrier2,
                SrcStageMask = PipelineStageFlags2.None,
                SrcAccessMask = AccessFlags2.None,
                DstStageMask = PipelineStageFlags2.ColorAttachmentOutputBit,
                DstAccessMask = AccessFlags2.ColorAttachmentWriteBit,
                OldLayout = ImageLayout.Undefined,
                NewLayout = ImageLayout.ColorAttachmentOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = _images[index],
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };
            var dependency = new DependencyInfo
            {
                SType = StructureType.DependencyInfo,
                ImageMemoryBarrierCount = 1,
                PImageMemoryBarriers = &barrier,
            };
            _vk.CmdPipelineBarrier2(commandBuffer, &dependency);
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the views, images, and memory; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        DestroyBestEffort(_vk, _device, _logger, _views, _images, _memory);
    }

    private static uint SelectMemoryType(
        in PhysicalDeviceMemoryProperties properties,
        uint memoryTypeBits,
        int imageIndex)
    {
        for (var index = 0; index < properties.MemoryTypeCount; index++)
        {
            if ((memoryTypeBits & (1u << index)) == 0)
            {
                continue;
            }

            MemoryPropertyFlags flags = properties.MemoryTypes[index].PropertyFlags;
            if ((flags & MemoryPropertyFlags.DeviceLocalBit) != 0)
            {
                return (uint)index;
            }
        }

        throw new Bork2dException(
            $"No device-local Vulkan memory type supports offscreen image {imageIndex} "
                + $"(memoryTypeBits=0x{memoryTypeBits:X}).");
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        ImageView[] views,
        Image[] images,
        DeviceMemory[] memory)
    {
        for (var index = 0; index < views.Length; index++)
        {
            if (views[index].Handle == 0)
            {
                continue;
            }

            try
            {
                vk.DestroyImageView(device, views[index], null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan offscreen image view destruction failed.");
            }
        }

        for (var index = 0; index < images.Length; index++)
        {
            if (images[index].Handle == 0)
            {
                continue;
            }

            try
            {
                vk.DestroyImage(device, images[index], null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan offscreen image destruction failed.");
            }
        }

        for (var index = 0; index < memory.Length; index++)
        {
            if (memory[index].Handle == 0)
            {
                continue;
            }

            try
            {
                vk.FreeMemory(device, memory[index], null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan offscreen image memory free failed.");
            }
        }
    }
}
