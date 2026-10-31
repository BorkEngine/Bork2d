using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Render.Vulkan;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Textures;

/// <summary>
/// The production Vulkan implementation of <see cref="ITexturePageGpu"/> (M8 §6.5): page images from
/// a device-local pool, a host-visible staging pool, one-time upload command buffers with their own
/// fences, and bindless descriptor writes into every per-frame set.
/// </summary>
/// <remarks>
/// <para>
/// Images are <c>VK_FORMAT_R8G8B8A8_SRGB</c>, <c>SAMPLED | TRANSFER_DST</c>, optimal tiling, one
/// mip/layer, created in <c>UNDEFINED</c> and moved to <c>SHADER_READ_ONLY_OPTIMAL</c> by the upload
/// barrier chain. Samplers come from the shared <see cref="SpritePipeline"/> cache (no per-page
/// sampler). Uploads never block the render thread; only the startup placeholder waits its fence.
/// Capacity growth recreates the per-frame descriptor sets through <see cref="IBindlessPageWriter"/>
/// after the renderer waited the device idle, then rewrites the placeholder and every resident
/// page.
/// </para>
/// <para>
/// All objects are render-thread-owned. <see cref="Destroy"/> waits/discards pending uploads, then
/// destroys views, images, memory blocks, staging buffers, and the upload command pool; it is
/// idempotent and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class VulkanTexturePages : ITexturePageGpu
{
    private const ulong UploadFenceTimeoutNanoseconds = 1_000_000_000;

    private readonly VulkanDevice _device;
    private readonly SpritePipeline _pipeline;
    private readonly IBindlessPageWriter _writer;
    private readonly IVulkanInstance? _instance;
    private readonly ILogger _logger;
    private readonly TextureImagePool _imagePool;
    private readonly StagingBufferPool _staging;
    private readonly CommandPool _uploadPool;
    private readonly List<CommandSlot> _commandSlots = [];
    private readonly Dictionary<int, PendingUpload> _pending = [];
    private readonly List<ResidentPage> _resident = [];
    private int _capacity = TexturePageCapacity.Initial;
    private int _destroyed;

    /// <summary>Initializes a new instance of the <see cref="VulkanTexturePages"/> class.</summary>
    /// <param name="device">The selected device (API, queue, physical device, and limits).</param>
    /// <param name="pipeline">The sprite pipeline that owns the samplers and the set layout.</param>
    /// <param name="writer">The renderer's per-frame bindless descriptor writer.</param>
    /// <param name="instance">The instance seam used for static debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <exception cref="Bork2dException">The upload command pool could not be created.</exception>
    internal VulkanTexturePages(
        VulkanDevice device,
        SpritePipeline pipeline,
        IBindlessPageWriter writer,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(logger);

        _device = device;
        _pipeline = pipeline;
        _writer = writer;
        _instance = instance;
        _logger = logger;
        _imagePool = new TextureImagePool(
            device.Api,
            device.Device,
            device.PhysicalDevice,
            logger);
        _staging = new StagingBufferPool(
            device.Api,
            device.Device,
            device.PhysicalDevice,
            logger);

        var poolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = device.QueueFamilyIndex,
        };
        Result result = device.Api.CreateCommandPool(
            device.Device,
            in poolInfo,
            null,
            out _uploadPool);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateCommandPool failed for the texture upload queue with VkResult {result}.");
        }
    }

    /// <inheritdoc/>
    public int ResidentPageCount => _resident.Count;

    /// <inheritdoc/>
    public int PendingUploadCount => _pending.Count;

    /// <inheritdoc/>
    public void EnsureCapacity(int capacity)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _destroyed) != 0, this);

        if (capacity <= _capacity)
        {
            return;
        }

        // The renderer waits the device idle and recreates the per-frame descriptor pools/sets; the
        // sets are empty afterwards, so every resident descriptor (including the placeholder) is
        // written again.
        _writer.EnsureCapacity(capacity);
        _capacity = capacity;
        foreach (ResidentPage page in _resident)
        {
            _writer.WritePage(page.Index, page.View, page.Sampler);
        }
    }

    /// <inheritdoc/>
    public void Upload(int pageIndex, TextureDescriptor descriptor, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(payload);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _destroyed) != 0, this);
        if (pageIndex < 0 || pageIndex >= _capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageIndex),
                pageIndex,
                $"The page index must be inside the current capacity of {_capacity} entries.");
        }

        Vk vk = _device.Api;
        Device logicalDevice = _device.Device;
        var extent = new Extent3D
        {
            Width = descriptor.Width,
            Height = descriptor.Height,
            Depth = 1,
        };
        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Srgb,
            Extent = extent,
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit | ImageUsageFlags.TransferDstBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        Result result = vk.CreateImage(logicalDevice, in imageInfo, null, out Image image);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateImage failed for texture page {pageIndex} with VkResult {result} "
                    + $"({descriptor.Width}x{descriptor.Height}).");
        }

        ImageView view = default;
        StagingLease lease = default;
        CommandSlot? command = null;
        try
        {
            MemoryRequirements requirements = vk.GetImageMemoryRequirements(logicalDevice, image);
            _imagePool.Allocate(requirements, out DeviceMemory memory, out ulong offset);
            result = vk.BindImageMemory(logicalDevice, image, memory, offset);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkBindImageMemory failed for texture page {pageIndex} with VkResult "
                        + $"{result}.");
            }

            var viewInfo = new ImageViewCreateInfo
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = image,
                ViewType = ImageViewType.Type2D,
                Format = Format.R8G8B8A8Srgb,
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
                    $"vkCreateImageView failed for texture page {pageIndex} with VkResult "
                        + $"{result}.");
            }

            Sampler sampler = _pipeline.GetOrCreateSampler(descriptor.Filter, descriptor.Wrap);
            lease = _staging.Acquire((ulong)payload.Length);
            payload.AsSpan().CopyTo(new Span<byte>((void*)lease.Mapped, payload.Length));

            command = AcquireCommandSlot();
            RecordUpload(
                command.Buffer,
                lease.Buffer,
                image,
                extent,
                pageIndex);
            SubmitUpload(command, pageIndex);

            LabelImage(pageIndex, image, view);
            _pending.Add(
                pageIndex,
                new PendingUpload(image, view, sampler, lease, command));
            command = null;
        }
        catch
        {
            if (command is not null)
            {
                ReleaseCommandSlot(command);
            }

            if (lease.Buffer.Handle != 0)
            {
                _staging.Release(lease);
            }

            if (view.Handle != 0)
            {
                vk.DestroyImageView(logicalDevice, view, null);
            }

            vk.DestroyImage(logicalDevice, image, null);
            throw;
        }
    }

    /// <inheritdoc/>
    public bool IsUploadComplete(int pageIndex)
    {
        if (!_pending.TryGetValue(pageIndex, out PendingUpload pending))
        {
            return false;
        }

        return _device.Api.GetFenceStatus(_device.Device, pending.Command.Fence) == Result.Success;
    }

    /// <inheritdoc/>
    public void CompleteUpload(int pageIndex)
    {
        if (!_pending.Remove(pageIndex, out PendingUpload pending))
        {
            return;
        }

        _staging.Release(pending.Staging);
        ReleaseCommandSlot(pending.Command);
        var resident = new ResidentPage(pageIndex, pending.Image, pending.View, pending.Sampler);
        _resident.Add(resident);
        _writer.WritePage(resident.Index, resident.View, resident.Sampler);
    }

    /// <inheritdoc/>
    public void WaitForUpload(int pageIndex)
    {
        if (!_pending.TryGetValue(pageIndex, out PendingUpload pending))
        {
            throw new Bork2dException(
                $"Texture page {pageIndex} has no submitted upload to wait for.");
        }

        Fence fence = pending.Command.Fence;
        Result result = _device.Api.WaitForFences(
            _device.Device,
            1,
            &fence,
            true,
            UploadFenceTimeoutNanoseconds);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkWaitForFences failed for texture page {pageIndex}'s upload with VkResult "
                    + $"{result}.");
        }

        CompleteUpload(pageIndex);
    }

    /// <inheritdoc/>
    public void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        Vk vk = _device.Api;
        Device logicalDevice = _device.Device;

        foreach (PendingUpload pending in _pending.Values)
        {
            Fence fence = pending.Command.Fence;
            try
            {
                _ = vk.WaitForFences(
                    logicalDevice,
                    1,
                    &fence,
                    true,
                    UploadFenceTimeoutNanoseconds);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan texture page upload fence wait failed.");
            }
        }

        // Views before images; both before the blocks and the pool.
        foreach (PendingUpload pending in _pending.Values)
        {
            DestroyView(vk, logicalDevice, pending.View);
        }

        foreach (ResidentPage page in _resident)
        {
            DestroyView(vk, logicalDevice, page.View);
        }

        foreach (PendingUpload pending in _pending.Values)
        {
            DestroyImage(vk, logicalDevice, pending.Image);
        }

        foreach (ResidentPage page in _resident)
        {
            DestroyImage(vk, logicalDevice, page.Image);
        }

        foreach (PendingUpload pending in _pending.Values)
        {
            _staging.Release(pending.Staging);
        }

        _pending.Clear();
        _resident.Clear();

        foreach (CommandSlot slot in _commandSlots)
        {
            if (slot.Fence.Handle != 0)
            {
                try
                {
                    vk.DestroyFence(logicalDevice, slot.Fence, null);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Vulkan texture upload fence destruction failed.");
                }
            }
        }

        _commandSlots.Clear();

        if (_uploadPool.Handle != 0)
        {
            try
            {
                // Destroying the pool also frees every upload command buffer allocated from it.
                vk.DestroyCommandPool(logicalDevice, _uploadPool, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan texture upload command pool destruction failed.");
            }
        }

        _imagePool.Destroy();
        _staging.Destroy();
    }

    private CommandSlot AcquireCommandSlot()
    {
        foreach (CommandSlot slot in _commandSlots)
        {
            if (slot.InUse)
            {
                continue;
            }

            slot.InUse = true;
            ResetSlot(slot);
            return slot;
        }

        Vk vk = _device.Api;
        Device logicalDevice = _device.Device;
        var allocateInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _uploadPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Result result = vk.AllocateCommandBuffers(
            logicalDevice,
            in allocateInfo,
            out CommandBuffer commandBuffer);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkAllocateCommandBuffers failed for a texture upload with VkResult {result}.");
        }

        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.SignaledBit,
        };
        result = vk.CreateFence(logicalDevice, in fenceInfo, null, out Fence fence);
        if (result != Result.Success)
        {
            vk.FreeCommandBuffers(logicalDevice, _uploadPool, 1, &commandBuffer);
            throw new Bork2dException(
                $"vkCreateFence failed for a texture upload with VkResult {result}.");
        }

        var created = new CommandSlot(commandBuffer, fence) { InUse = true };
        _commandSlots.Add(created);
        ResetSlot(created);
        return created;
    }

    private void ResetSlot(CommandSlot slot)
    {
        Vk vk = _device.Api;
        Device logicalDevice = _device.Device;
        Fence fence = slot.Fence;
        Result resetFence = vk.ResetFences(logicalDevice, 1, &fence);
        if (resetFence != Result.Success)
        {
            throw new Bork2dException(
                $"vkResetFences failed for a texture upload with VkResult {resetFence}.");
        }

        Result resetBuffer = vk.ResetCommandBuffer(slot.Buffer, CommandBufferResetFlags.None);
        if (resetBuffer != Result.Success)
        {
            throw new Bork2dException(
                $"vkResetCommandBuffer failed for a texture upload with VkResult {resetBuffer}.");
        }
    }

    private void ReleaseCommandSlot(CommandSlot slot) => slot.InUse = false;

    private void RecordUpload(
        CommandBuffer commandBuffer,
        Buffer stagingBuffer,
        Image image,
        Extent3D extent,
        int pageIndex)
    {
        Vk vk = _device.Api;
        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit,
        };
        Result begin = vk.BeginCommandBuffer(commandBuffer, in beginInfo);
        if (begin != Result.Success)
        {
            throw new Bork2dException(
                $"vkBeginCommandBuffer failed for texture page {pageIndex} with VkResult {begin}.");
        }

        ImageSubresourceRange range = new()
        {
            AspectMask = ImageAspectFlags.ColorBit,
            BaseMipLevel = 0,
            LevelCount = 1,
            BaseArrayLayer = 0,
            LayerCount = 1,
        };
        var toTransfer = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.None,
            SrcAccessMask = AccessFlags2.None,
            DstStageMask = PipelineStageFlags2.TransferBit,
            DstAccessMask = AccessFlags2.TransferWriteBit,
            OldLayout = ImageLayout.Undefined,
            NewLayout = ImageLayout.TransferDstOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
        };
        var toTransferDependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &toTransfer,
        };
        vk.CmdPipelineBarrier2(commandBuffer, &toTransferDependency);

        var region = new BufferImageCopy
        {
            BufferOffset = 0,
            BufferRowLength = 0,
            BufferImageHeight = 0,
            ImageSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                MipLevel = 0,
                BaseArrayLayer = 0,
                LayerCount = 1,
            },
            ImageOffset = new Offset3D(0, 0, 0),
            ImageExtent = extent,
        };
        vk.CmdCopyBufferToImage(
            commandBuffer,
            stagingBuffer,
            image,
            ImageLayout.TransferDstOptimal,
            1,
            &region);

        var toShaderRead = new ImageMemoryBarrier2
        {
            SType = StructureType.ImageMemoryBarrier2,
            SrcStageMask = PipelineStageFlags2.TransferBit,
            SrcAccessMask = AccessFlags2.TransferWriteBit,
            DstStageMask = PipelineStageFlags2.FragmentShaderBit,
            DstAccessMask = AccessFlags2.ShaderSampledReadBit,
            OldLayout = ImageLayout.TransferDstOptimal,
            NewLayout = ImageLayout.ShaderReadOnlyOptimal,
            SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            Image = image,
            SubresourceRange = range,
        };
        var toShaderReadDependency = new DependencyInfo
        {
            SType = StructureType.DependencyInfo,
            ImageMemoryBarrierCount = 1,
            PImageMemoryBarriers = &toShaderRead,
        };
        vk.CmdPipelineBarrier2(commandBuffer, &toShaderReadDependency);

        Result end = vk.EndCommandBuffer(commandBuffer);
        if (end != Result.Success)
        {
            throw new Bork2dException(
                $"vkEndCommandBuffer failed for texture page {pageIndex} with VkResult {end}.");
        }
    }

    private void SubmitUpload(CommandSlot command, int pageIndex)
    {
        Vk vk = _device.Api;
        var commandBufferInfo = new CommandBufferSubmitInfo
        {
            SType = StructureType.CommandBufferSubmitInfo,
            CommandBuffer = command.Buffer,
        };
        var submitInfo = new SubmitInfo2
        {
            SType = StructureType.SubmitInfo2,
            CommandBufferInfoCount = 1,
            PCommandBufferInfos = &commandBufferInfo,
        };

        Result result = vk.QueueSubmit2(_device.Queue, 1, &submitInfo, command.Fence);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkQueueSubmit2 failed for texture page {pageIndex}'s upload with VkResult "
                    + $"{result}.");
        }
    }

    private void LabelImage(int pageIndex, Image image, ImageView view)
    {
        if (_instance is not { } instance)
        {
            return;
        }

        nint device = _device.Device.Handle;
        instance.SetObjectName(
            device,
            ObjectType.Image,
            (nint)image.Handle,
            $"Bork2d texture page image ({pageIndex})");
        instance.SetObjectName(
            device,
            ObjectType.ImageView,
            (nint)view.Handle,
            $"Bork2d texture page image view ({pageIndex})");
    }

    private void DestroyView(Vk vk, Device device, ImageView view)
    {
        if (view.Handle == 0)
        {
            return;
        }

        try
        {
            vk.DestroyImageView(device, view, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan texture page image-view destruction failed.");
        }
    }

    private void DestroyImage(Vk vk, Device device, Image image)
    {
        if (image.Handle == 0)
        {
            return;
        }

        try
        {
            vk.DestroyImage(device, image, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan texture page image destruction failed.");
        }
    }

    private sealed class CommandSlot
    {
        internal CommandSlot(CommandBuffer buffer, Fence fence)
        {
            Buffer = buffer;
            Fence = fence;
        }

        internal CommandBuffer Buffer { get; }

        internal Fence Fence { get; }

        internal bool InUse { get; set; }
    }

    private readonly record struct PendingUpload(
        Image Image,
        ImageView View,
        Sampler Sampler,
        StagingLease Staging,
        CommandSlot Command);

    private readonly record struct ResidentPage(int Index, Image Image, ImageView View, Sampler Sampler);
}
