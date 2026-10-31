using Bork2d.Engine;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's empty instance storage buffer and frame descriptor set (Q10=J1, M7 WP-3).
/// </summary>
/// <remarks>
/// <para>
/// M7 creates the resources the M8 sprite pass will fill: an instance SSBO and a descriptor set
/// whose only binding is binding 0 (storage buffer, vertex stage). Nothing is written, updated,
/// bound, or drawn in M7; the buffer is one host-visible, host-coherent allocation (no
/// suballocator, consistent with D-M6-03). The M8 bindless texture binding (binding 1) and the
/// production page capacity are deliberately not committed here.
/// </para>
/// <para>
/// The resources are created once per GPU frame slot at startup, never per frame, and destroyed in
/// renderer cleanup after the frame fences/device idle. <see cref="Destroy"/> is best-effort,
/// idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class FrameInstanceResources : IDisposable
{
    /// <summary>
    /// The provisional instance-buffer size allocated per GPU frame.
    /// </summary>
    /// <remarks>
    /// M7 allocates it empty/unused; M8 owns the real instance capacity and record layout and may
    /// change this constant. The value only needs to be a valid storage-buffer allocation.
    /// </remarks>
    internal const int InstanceBufferBytes = 4096;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly Buffer _buffer;
    private readonly DeviceMemory _memory;
    private readonly DescriptorSetLayout _layout;
    private readonly DescriptorPool _pool;
    private readonly DescriptorSet _descriptorSet;
    private readonly ulong _allocationBytes;
    private int _destroyed;

    private FrameInstanceResources(
        Vk vk,
        Device device,
        ILogger logger,
        Buffer buffer,
        DeviceMemory memory,
        DescriptorSetLayout layout,
        DescriptorPool pool,
        DescriptorSet descriptorSet,
        ulong allocationBytes)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _buffer = buffer;
        _memory = memory;
        _layout = layout;
        _pool = pool;
        _descriptorSet = descriptorSet;
        _allocationBytes = allocationBytes;
    }

    /// <summary>Gets the allocated buffer size in bytes.</summary>
    internal ulong AllocationBytes => _allocationBytes;

    /// <summary>Gets the frame descriptor set (allocated, never updated in M7).</summary>
    internal DescriptorSet DescriptorSet => _descriptorSet;

    /// <summary>
    /// Creates the empty instance buffer, descriptor-set layout, descriptor pool, and descriptor
    /// set for one GPU frame.
    /// </summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The selected logical device.</param>
    /// <param name="physicalDevice">The physical device used to select the memory type.</param>
    /// <param name="frameIndex">The GPU frame index, used for debug names only.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <returns>The created resources; the caller owns them.</returns>
    /// <exception cref="Bork2dException">A Vulkan object could not be created.</exception>
    internal static FrameInstanceResources Create(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        int frameIndex,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        Device logicalDevice = device;
        var buffer = default(Buffer);
        var memory = default(DeviceMemory);
        var layout = default(DescriptorSetLayout);
        var pool = default(DescriptorPool);
        var descriptorSet = default(DescriptorSet);
        var allocationBytes = 0UL;
        try
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = InstanceBufferBytes,
                Usage = BufferUsageFlags.StorageBufferBit,
                SharingMode = SharingMode.Exclusive,
            };
            Result result = vk.CreateBuffer(logicalDevice, in bufferInfo, null, out buffer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateBuffer failed for frame {frameIndex} instance buffer with VkResult "
                        + $"{result}.");
            }

            MemoryRequirements requirements = vk.GetBufferMemoryRequirements(
                logicalDevice,
                buffer);
            PhysicalDeviceMemoryProperties memoryProperties =
                vk.GetPhysicalDeviceMemoryProperties(physicalDevice);
            uint memoryTypeIndex = SelectMemoryType(
                memoryProperties,
                requirements.MemoryTypeBits,
                frameIndex);
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
                    $"vkAllocateMemory failed for frame {frameIndex} instance buffer with VkResult "
                        + $"{result} ({requirements.Size} bytes, memory type {memoryTypeIndex}).");
            }

            result = vk.BindBufferMemory(logicalDevice, buffer, memory, 0);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkBindBufferMemory failed for frame {frameIndex} instance buffer with "
                        + $"VkResult {result}.");
            }

            allocationBytes = requirements.Size;

            var binding = new DescriptorSetLayoutBinding
            {
                Binding = 0,
                DescriptorType = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.VertexBit,
            };
            var layoutInfo = new DescriptorSetLayoutCreateInfo
            {
                SType = StructureType.DescriptorSetLayoutCreateInfo,
                BindingCount = 1,
                PBindings = &binding,
            };
            result = vk.CreateDescriptorSetLayout(
                logicalDevice,
                in layoutInfo,
                null,
                out layout);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateDescriptorSetLayout failed for frame {frameIndex} with VkResult "
                        + $"{result}.");
            }

            var poolSize = new DescriptorPoolSize
            {
                Type = DescriptorType.StorageBuffer,
                DescriptorCount = 1,
            };
            var poolInfo = new DescriptorPoolCreateInfo
            {
                SType = StructureType.DescriptorPoolCreateInfo,
                MaxSets = 1,
                PoolSizeCount = 1,
                PPoolSizes = &poolSize,
            };
            result = vk.CreateDescriptorPool(logicalDevice, in poolInfo, null, out pool);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateDescriptorPool failed for frame {frameIndex} with VkResult {result}.");
            }

            DescriptorSetLayout* setLayouts = &layout;
            var setInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = pool,
                DescriptorSetCount = 1,
                PSetLayouts = setLayouts,
            };
            result = vk.AllocateDescriptorSets(logicalDevice, in setInfo, &descriptorSet);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkAllocateDescriptorSets failed for frame {frameIndex} with VkResult "
                        + $"{result}.");
            }

            if (instance is not null)
            {
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.Buffer,
                    (nint)buffer.Handle,
                    $"Bork2d frame {frameIndex} instance buffer");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.DescriptorSetLayout,
                    (nint)layout.Handle,
                    $"Bork2d frame {frameIndex} descriptor set layout");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.DescriptorSet,
                    (nint)descriptorSet.Handle,
                    $"Bork2d frame {frameIndex} descriptor set");
            }

            return new FrameInstanceResources(
                vk,
                logicalDevice,
                logger,
                buffer,
                memory,
                layout,
                pool,
                descriptorSet,
                allocationBytes);
        }
        catch
        {
            DestroyBestEffort(vk, logicalDevice, logger, pool, layout, buffer, memory);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Destroys the descriptor set, pool, layout, buffer, and memory; never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        DestroyBestEffort(_vk, _device, _logger, _pool, _layout, _buffer, _memory);
    }

    private static uint SelectMemoryType(
        in PhysicalDeviceMemoryProperties properties,
        uint memoryTypeBits,
        int frameIndex)
    {
        const MemoryPropertyFlags required =
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (var index = 0; index < properties.MemoryTypeCount; index++)
        {
            if ((memoryTypeBits & (1u << index)) == 0)
            {
                continue;
            }

            if ((properties.MemoryTypes[index].PropertyFlags & required) == required)
            {
                return (uint)index;
            }
        }

        throw new Bork2dException(
            $"No host-visible coherent Vulkan memory type supports frame {frameIndex}'s instance "
                + $"buffer (memoryTypeBits=0x{memoryTypeBits:X}).");
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        DescriptorPool pool,
        DescriptorSetLayout layout,
        Buffer buffer,
        DeviceMemory memory)
    {
        if (pool.Handle != 0)
        {
            try
            {
                // Destroying the pool also frees the descriptor set allocated from it.
                vk.DestroyDescriptorPool(device, pool, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan frame descriptor pool destruction failed.");
            }
        }

        if (layout.Handle != 0)
        {
            try
            {
                vk.DestroyDescriptorSetLayout(device, layout, null);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Vulkan frame descriptor-set-layout destruction failed.");
            }
        }

        if (buffer.Handle != 0)
        {
            try
            {
                vk.DestroyBuffer(device, buffer, null);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan frame instance-buffer destruction failed.");
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
                logger.LogError(exception, "Vulkan frame instance-buffer memory free failed.");
            }
        }
    }
}
