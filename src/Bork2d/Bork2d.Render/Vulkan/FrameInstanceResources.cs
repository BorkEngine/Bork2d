using Bork2d.Engine;
using Bork2d.Render.Sprites;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's instance buffer (binding 0) and bindless page descriptor set (binding 1) (M8
/// §6.5/§6.6).
/// </summary>
/// <remarks>
/// <para>
/// The resources are created once per GPU frame slot at startup and never per frame. Binding 0 is
/// a host-visible coherent storage buffer sized for <see cref="InstanceCapacity"/> 96-byte records;
/// M8's draw work package grows it, WP-3 only allocates and binds it. Binding 1 is the bindless
/// combined-image-sampler array from the shared <see cref="SpritePipeline"/> layout, allocated with
/// a variable descriptor count equal to the current page capacity and the
/// <c>PARTIALLY_BOUND | UPDATE_AFTER_BIND | VARIABLE_DESCRIPTOR_COUNT</c> flags.
/// </para>
/// <para>
/// Page descriptors are written by <see cref="WritePage"/> as pages become ready, and rewritten
/// after a capacity growth (the renderer waits the device idle, calls
/// <see cref="RecreateBindless(int)"/>, and then rewrites every resident page). <see cref="Destroy"/>
/// is best-effort, idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class FrameInstanceResources : IDisposable
{
    /// <summary>The fixed instance-record capacity allocated per GPU frame in WP-3.</summary>
    /// <remarks>
    /// The M8 draw work package replaces this with a growable capacity; WP-3 only needs a valid
    /// <c>maxStorageBufferRange</c>-checked allocation and an updated binding 0.
    /// </remarks>
    internal const int InstanceCapacity = 1024;

    /// <summary>The instance buffer size in bytes (<see cref="InstanceCapacity"/> 96-byte records).</summary>
    internal const int InstanceBufferBytes = InstanceCapacity * SpriteInstance.SizeInBytes;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly ILogger _logger;
    private readonly Buffer _buffer;
    private readonly DeviceMemory _memory;
    private readonly DescriptorSetLayout _layout;
    private DescriptorPool _pool;
    private DescriptorSet _descriptorSet;
    private readonly ulong _allocationBytes;
    private readonly int _frameIndex;
    private int _pageCapacity;
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
        ulong allocationBytes,
        int pageCapacity,
        int frameIndex)
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
        _pageCapacity = pageCapacity;
        _frameIndex = frameIndex;
    }

    /// <summary>Gets the allocated instance-buffer size in bytes.</summary>
    internal ulong AllocationBytes => _allocationBytes;

    /// <summary>Gets the frame descriptor set.</summary>
    internal DescriptorSet DescriptorSet => _descriptorSet;

    /// <summary>Gets the current bindless page capacity (the set's variable descriptor count).</summary>
    internal int PageCapacity => _pageCapacity;

    /// <summary>
    /// Creates the instance buffer and the frame's bindless descriptor pool/set.
    /// </summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The selected logical device.</param>
    /// <param name="physicalDevice">The physical device used to select the memory type.</param>
    /// <param name="frameIndex">The GPU frame index, used for debug names only.</param>
    /// <param name="pipeline">
    /// The sprite pipeline that owns the descriptor-set layout and validates the bindless limit.
    /// </param>
    /// <param name="pageCapacity">The initial bindless page capacity, including the placeholder.</param>
    /// <param name="maxStorageBufferRange">The device's <c>maxStorageBufferRange</c> limit.</param>
    /// <param name="instance">The instance seam used for debug labels; may be <see langword="null"/>.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <returns>The created resources; the caller owns them.</returns>
    /// <exception cref="Bork2dException">A Vulkan object could not be created or a limit is exceeded.</exception>
    internal static FrameInstanceResources Create(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        int frameIndex,
        SpritePipeline pipeline,
        int pageCapacity,
        uint maxStorageBufferRange,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(logger);
        if (pageCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageCapacity),
                pageCapacity,
                "The bindless page capacity must be positive.");
        }

        if ((ulong)InstanceBufferBytes > maxStorageBufferRange)
        {
            throw new Bork2dException(
                $"The per-frame instance buffer needs {InstanceBufferBytes} bytes for "
                    + $"{InstanceCapacity} 96-byte records, which exceeds the selected device's "
                    + $"maxStorageBufferRange of {maxStorageBufferRange}.");
        }

        Device logicalDevice = device;
        var buffer = default(Buffer);
        var memory = default(DeviceMemory);
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

            CreateDescriptorPoolAndSet(
                vk,
                logicalDevice,
                pipeline.DescriptorSetLayout,
                pageCapacity,
                frameIndex,
                out pool,
                out descriptorSet);

            var resources = new FrameInstanceResources(
                vk,
                logicalDevice,
                logger,
                buffer,
                memory,
                pipeline.DescriptorSetLayout,
                pool,
                descriptorSet,
                allocationBytes,
                pageCapacity,
                frameIndex);
            resources.WriteInstanceBuffer();

            if (instance is not null)
            {
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.Buffer,
                    (nint)buffer.Handle,
                    $"Bork2d frame {frameIndex} instance buffer");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.DescriptorSet,
                    (nint)descriptorSet.Handle,
                    $"Bork2d frame {frameIndex} descriptor set");
            }

            return resources;
        }
        catch
        {
            DestroyBestEffort(vk, logicalDevice, logger, pool, buffer, memory);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>
    /// Recreates the descriptor pool/set with a new variable descriptor count.
    /// </summary>
    /// <remarks>
    /// The caller must have waited the device idle first (capacity growth happens between frames
    /// with all in-flight frames idle), and must rewrite every resident page descriptor afterwards
    /// because the new set is empty.
    /// </remarks>
    /// <param name="pageCapacity">The new bindless page capacity, including the placeholder.</param>
    /// <exception cref="Bork2dException">The pool or set could not be created.</exception>
    internal void RecreateBindless(int pageCapacity)
    {
        if (pageCapacity <= _pageCapacity)
        {
            return;
        }

        var pool = default(DescriptorPool);
        var descriptorSet = default(DescriptorSet);
        CreateDescriptorPoolAndSet(
            _vk,
            _device,
            _layout,
            pageCapacity,
            _frameIndex,
            out pool,
            out descriptorSet);

        // The new pool/set exist before the old set is destroyed, so a failure leaves the frame
        // usable with its previous capacity.
        DestroyPool(_pool);
        ReplacePool(pool, descriptorSet, pageCapacity);
        WriteInstanceBuffer();
    }

    /// <summary>
    /// Writes one page's combined image sampler into binding 1 at <paramref name="pageIndex"/>.
    /// </summary>
    /// <param name="pageIndex">The bindless array element.</param>
    /// <param name="imageView">The page image view, in <c>SHADER_READ_ONLY_OPTIMAL</c>.</param>
    /// <param name="sampler">The cached sampler for the page's filter/wrap pair.</param>
    internal void WritePage(int pageIndex, ImageView imageView, Sampler sampler)
    {
        if ((uint)pageIndex >= (uint)_pageCapacity)
        {
            throw new Bork2dException(
                $"Texture page index {pageIndex} does not fit frame {_frameIndex}'s bindless "
                    + $"capacity of {_pageCapacity} entries.");
        }

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = sampler,
            ImageView = imageView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = SpritePipeline.TextureBinding,
            DstArrayElement = (uint)pageIndex,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
        };
        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    /// <summary>Destroys the descriptor set/pool, buffer, and memory; never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        DestroyBestEffort(_vk, _device, _logger, _pool, _buffer, _memory);
    }

    private static void CreateDescriptorPoolAndSet(
        Vk vk,
        Device device,
        DescriptorSetLayout layout,
        int pageCapacity,
        int frameIndex,
        out DescriptorPool pool,
        out DescriptorSet descriptorSet)
    {
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize
        {
            Type = DescriptorType.StorageBuffer,
            DescriptorCount = 1,
        };
        poolSizes[1] = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = (uint)pageCapacity,
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
        };
        Result result = vk.CreateDescriptorPool(device, in poolInfo, null, out pool);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateDescriptorPool failed for frame {frameIndex} with VkResult {result} "
                    + $"(page capacity {pageCapacity}).");
        }

        uint variableCount = (uint)pageCapacity;
        var variableInfo = new DescriptorSetVariableDescriptorCountAllocateInfo
        {
            SType = StructureType.DescriptorSetVariableDescriptorCountAllocateInfo,
            DescriptorSetCount = 1,
            PDescriptorCounts = &variableCount,
        };
        DescriptorSetLayout* setLayouts = &layout;
        var setInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            PNext = &variableInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = setLayouts,
        };
        var allocated = default(DescriptorSet);
        result = vk.AllocateDescriptorSets(device, in setInfo, &allocated);
        if (result != Result.Success)
        {
            vk.DestroyDescriptorPool(device, pool, null);
            pool = default;
            throw new Bork2dException(
                $"vkAllocateDescriptorSets failed for frame {frameIndex} with VkResult {result} "
                    + $"(page capacity {pageCapacity}).");
        }

        descriptorSet = allocated;
    }

    private void ReplacePool(DescriptorPool pool, DescriptorSet descriptorSet, int pageCapacity)
    {
        _pool = pool;
        _descriptorSet = descriptorSet;
        _pageCapacity = pageCapacity;
    }

    private void WriteInstanceBuffer()
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _buffer,
            Offset = 0,
            Range = _allocationBytes,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _descriptorSet,
            DstBinding = SpritePipeline.InstanceBinding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.StorageBuffer,
            PBufferInfo = &bufferInfo,
        };
        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    private void DestroyPool(DescriptorPool pool)
    {
        if (pool.Handle == 0)
        {
            return;
        }

        try
        {
            // Destroying the pool also frees the descriptor set allocated from it.
            _vk.DestroyDescriptorPool(_device, pool, null);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Vulkan frame descriptor pool destruction failed.");
        }
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
