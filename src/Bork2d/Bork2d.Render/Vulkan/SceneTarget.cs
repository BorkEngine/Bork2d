using Bork2d.Engine;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's linear scene color image: every 2D pass renders into it and the final present pass
/// samples it.
/// </summary>
/// <remarks>
/// <para>
/// The image is <c>R8G8B8A8_UNORM</c>, device-local, created once per frame slot, and used as a color
/// attachment and as a sampled image. Its contents are linear; the present fragment stage applies the
/// sRGB opto-electronic transfer function when it writes the final UNORM target, so the presented
/// image keeps the encoded visual result of a former sRGB attachment without an sRGB target format.
/// </para>
/// <para>
/// The frame clear writes every pixel each frame, so recording always transitions the image from
/// <c>UNDEFINED</c> (discarding the previous frame's contents) to <c>COLOR_ATTACHMENT_OPTIMAL</c> and
/// ends in <c>SHADER_READ_ONLY_OPTIMAL</c>. A windowed swapchain recreation changes the required
/// extent; the target driver creates the replacement after its <c>vkDeviceWaitIdle</c> and destroys
/// the previous image only then, so no GPU frame can reference a destroyed image. The offscreen
/// target is fixed-size and never resizes.
/// </para>
/// <para>
/// <see cref="Dispose"/> is best-effort, idempotent, and never throws.
/// </para>
/// </remarks>
internal sealed unsafe class SceneTarget : IDisposable
{
    /// <summary>The approved linear scene color format for every target mode.</summary>
    internal const Format ColorFormat = Format.R8G8B8A8Unorm;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly Image _image;
    private readonly ImageView _view;
    private readonly DeviceMemory _memory;
    private int _destroyed;

    private SceneTarget(
        Vk vk,
        Device device,
        ILogger logger,
        Image image,
        ImageView view,
        DeviceMemory memory,
        ulong memoryBytes)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _image = image;
        _view = view;
        _memory = memory;
        MemoryBytes = memoryBytes;
    }

    /// <summary>Gets the device memory allocated for the image in bytes.</summary>
    internal ulong MemoryBytes { get; }

    /// <summary>Gets the scene image.</summary>
    internal Image Image => _image;

    /// <summary>Gets the scene image view used as the color attachment and the present source.</summary>
    internal ImageView View => _view;

    /// <summary>Creates one frame's linear scene image and view.</summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The logical device.</param>
    /// <param name="physicalDevice">The physical device used to select the image memory type.</param>
    /// <param name="extent">The target extent in pixels.</param>
    /// <param name="frameIndex">The GPU frame index, used for diagnostics and labels.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger.</param>
    /// <returns>The created scene target; the caller owns and disposes it.</returns>
    /// <exception cref="Bork2dException">A Vulkan object could not be created.</exception>
    internal static SceneTarget Create(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        Extent2D extent,
        int frameIndex,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (extent.Width == 0 || extent.Height == 0)
        {
            throw new Bork2dException(
                $"A scene color image needs a positive extent; got {extent.Width}x{extent.Height}.");
        }

        Device logicalDevice = device;
        var image = default(Image);
        var view = default(ImageView);
        var memory = default(DeviceMemory);
        try
        {
            var imageInfo = new ImageCreateInfo
            {
                SType = StructureType.ImageCreateInfo,
                ImageType = ImageType.Type2D,
                Format = ColorFormat,
                Extent = new Extent3D
                {
                    Width = extent.Width,
                    Height = extent.Height,
                    Depth = 1,
                },
                MipLevels = 1,
                ArrayLayers = 1,
                Samples = SampleCountFlags.Count1Bit,
                Tiling = ImageTiling.Optimal,
                Usage = ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.SampledBit,
                SharingMode = SharingMode.Exclusive,
                InitialLayout = ImageLayout.Undefined,
            };
            Result result = vk.CreateImage(logicalDevice, in imageInfo, null, out image);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateImage failed for frame {frameIndex}'s scene color image with VkResult "
                        + $"{result} ({extent.Width}x{extent.Height}).");
            }

            MemoryRequirements requirements = vk.GetImageMemoryRequirements(logicalDevice, image);
            PhysicalDeviceMemoryProperties memoryProperties =
                vk.GetPhysicalDeviceMemoryProperties(physicalDevice);
            uint memoryTypeIndex = SelectMemoryType(memoryProperties, requirements.MemoryTypeBits);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = memoryTypeIndex,
            };
            result = vk.AllocateMemory(logicalDevice, in allocateInfo, null, out memory);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkAllocateMemory failed for frame {frameIndex}'s scene color image with "
                        + $"VkResult {result} ({requirements.Size} bytes, memory type "
                        + $"{memoryTypeIndex}).");
            }

            result = vk.BindImageMemory(logicalDevice, image, memory, 0);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkBindImageMemory failed for frame {frameIndex}'s scene color image with "
                        + $"VkResult {result}.");
            }

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = ColorFormat,
                Components = new ComponentMapping
                {
                    R = ComponentSwizzle.Identity,
                    G = ComponentSwizzle.Identity,
                    B = ComponentSwizzle.Identity,
                    A = ComponentSwizzle.Identity,
                },
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };
            result = vk.CreateImageView(logicalDevice, in viewInfo, null, out view);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateImageView failed for frame {frameIndex}'s scene color image with "
                        + $"VkResult {result}.");
            }

            instance?.SetObjectName(
                logicalDevice.Handle,
                ObjectType.Image,
                (nint)image.Handle,
                $"Bork2d frame {frameIndex} scene color image");
            instance?.SetObjectName(
                logicalDevice.Handle,
                ObjectType.ImageView,
                (nint)view.Handle,
                $"Bork2d frame {frameIndex} scene color image view");

            return new SceneTarget(
                vk,
                logicalDevice,
                logger,
                image,
                view,
                memory,
                requirements.Size);
        }
        catch
        {
            DestroyBestEffort(vk, logicalDevice, logger, view, image, memory);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the view, image, and memory; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        DestroyBestEffort(_vk, _device, _logger, _view, _image, _memory);
    }

    /// <summary>
    /// Records the <c>UNDEFINED → COLOR_ATTACHMENT_OPTIMAL</c> transition that discards the previous
    /// frame's scene contents before the frame clear writes every pixel.
    /// </summary>
    /// <param name="commandBuffer">The frame command buffer, already begun.</param>
    internal void RecordToColorAttachment(CommandBuffer commandBuffer)
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
            Image = _image,
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

    private static uint SelectMemoryType(
        in PhysicalDeviceMemoryProperties properties,
        uint memoryTypeBits)
    {
        for (var index = 0; index < properties.MemoryTypeCount; index++)
        {
            if ((memoryTypeBits & (1u << index)) == 0)
            {
                continue;
            }

            if ((properties.MemoryTypes[index].PropertyFlags & MemoryPropertyFlags.DeviceLocalBit)
                != 0)
            {
                return (uint)index;
            }
        }

        throw new Bork2dException(
            "No device-local Vulkan memory type supports the scene color image "
                + $"(memoryTypeBits=0x{memoryTypeBits:X}).");
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        ImageView view,
        Image image,
        DeviceMemory memory)
    {
        if (view.Handle != 0)
        {
            try
            {
                vk.DestroyImageView(device, view, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan scene image view destruction failed.");
            }
        }

        if (image.Handle != 0)
        {
            try
            {
                vk.DestroyImage(device, image, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan scene image destruction failed.");
            }
        }

        if (memory.Handle != 0)
        {
            try
            {
                vk.FreeMemory(device, memory, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan scene image memory free failed.");
            }
        }
    }
}
