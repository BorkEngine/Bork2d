using Bork2d.Engine;
using Bork2d.Render.Sprites;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's growable instance buffer (binding 0) and bindless page descriptor set (binding
/// 1) (M8 §6.5/§6.6).
/// </summary>
/// <remarks>
/// <para>
/// The resources are created once per GPU frame slot at startup and never per frame. Binding 0 is a
/// host-visible coherent storage buffer mapped once, holding <see cref="InstanceCapacity"/> 96-byte
/// records; the sprite frame planner writes every record directly into that mapping, with no CPU
/// frame array and no staging copy. When a frame needs more records than its buffer holds, the
/// renderer (having observed the frame's fence) calls <see cref="EnsureCapacity"/>, which recreates
/// the buffer at a power-of-two capacity, remaps it, and rewrites binding 0. Growth is logged and
/// counted once per capacity change; a device-limit or allocation failure is a fatal
/// <see cref="Bork2dException"/> and never truncates instances.
/// </para>
/// <para>
/// Binding 1 is the bindless combined-image-sampler array from the shared
/// <see cref="SpritePipeline"/> layout, allocated with a variable descriptor count equal to the
/// current page capacity and the <c>PARTIALLY_BOUND | UPDATE_AFTER_BIND |
/// VARIABLE_DESCRIPTOR_COUNT</c> flags. Page descriptors are written by <see cref="WritePage"/> as
/// pages become ready and rewritten after a capacity growth (the renderer waits the device idle,
/// calls <see cref="RecreateBindless(int)"/>, and then rewrites every resident page).
/// <see cref="Destroy"/> is best-effort, idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class FrameInstanceResources : IDisposable, IInstanceBuffer
{
    /// <summary>The approved initial per-frame capacity in 96-byte records.</summary>
    internal const int InitialCapacity = InstanceBufferGrowth.InitialCapacity;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly ILogger _logger;
    private readonly DescriptorSetLayout _layout;
    private readonly ulong _maxStorageBufferRange;
    private readonly int _frameIndex;
    private Buffer _buffer;
    private DeviceMemory _memory;
    private void* _mapped;
    private ulong _allocationBytes;
    private ulong _bufferBytes;
    private InstanceBufferGrowth _growth;
    private DescriptorPool _pool;
    private DescriptorSet _descriptorSet;
    private int _pageCapacity;
    private int _growthCount;
    private int _destroyed;

    private FrameInstanceResources(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        ILogger logger,
        Buffer buffer,
        DeviceMemory memory,
        void* mapped,
        ulong allocationBytes,
        ulong bufferBytes,
        InstanceBufferGrowth growth,
        DescriptorSetLayout layout,
        DescriptorPool pool,
        DescriptorSet descriptorSet,
        ulong maxStorageBufferRange,
        int pageCapacity,
        int frameIndex)
    {
        _vk = vk;
        _device = device;
        _physicalDevice = physicalDevice;
        _logger = logger;
        _buffer = buffer;
        _memory = memory;
        _mapped = mapped;
        _allocationBytes = allocationBytes;
        _bufferBytes = bufferBytes;
        _growth = growth;
        _layout = layout;
        _pool = pool;
        _descriptorSet = descriptorSet;
        _maxStorageBufferRange = maxStorageBufferRange;
        _pageCapacity = pageCapacity;
        _frameIndex = frameIndex;
    }

    /// <summary>Gets the current instance-record capacity (initially 1024).</summary>
    internal int InstanceCapacity => _growth.Capacity;

    int IInstanceBuffer.InstanceCapacity => _growth.Capacity;

    void IInstanceBuffer.EnsureCapacity(int requiredInstances) => EnsureCapacity(requiredInstances);

    void IInstanceBuffer.WriteInstance(int index, in SpriteInstance instance)
        => WriteInstance(index, instance);

    /// <summary>Gets the number of capacity growths this frame's buffer performed.</summary>
    internal int GrowthCount => _growthCount;

    /// <summary>Gets the instance buffer handle (a diagnostic/label accessor).</summary>
    internal Buffer InstanceBuffer => _buffer;

    /// <summary>Gets the mapped host-visible address of record <c>0</c>.</summary>
    internal nint MappedAddress => (nint)_mapped;

    /// <summary>Gets the allocated device-memory size in bytes.</summary>
    internal ulong AllocationBytes => _allocationBytes;

    /// <summary>Gets the descriptor-set binding-0 range in bytes (the requested buffer size).</summary>
    internal ulong InstanceBufferBytesInRange => _bufferBytes;

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

        var growth = new InstanceBufferGrowth(InstanceBufferGrowth.InitialCapacity);
        ulong initialBytes = (ulong)InstanceBufferGrowth.InitialCapacity
            * SpriteInstance.SizeInBytes;
        if (initialBytes > maxStorageBufferRange)
        {
            throw new Bork2dException(
                $"The per-frame instance buffer needs {initialBytes} bytes for "
                    + $"{InstanceBufferGrowth.InitialCapacity} 96-byte records, which exceeds the "
                    + $"selected device's maxStorageBufferRange of {maxStorageBufferRange}.");
        }

        Device logicalDevice = device;
        var pool = default(DescriptorPool);
        var descriptorSet = default(DescriptorSet);
        var buffer = default(Buffer);
        var memory = default(DeviceMemory);
        void* mapped = null;
        var allocationBytes = 0UL;
        try
        {
            CreateMappedBuffer(
                vk,
                logicalDevice,
                physicalDevice,
                logger,
                frameIndex,
                InstanceBufferGrowth.InitialCapacity,
                out buffer,
                out memory,
                out mapped,
                out allocationBytes);

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
                physicalDevice,
                logger,
                buffer,
                memory,
                mapped,
                allocationBytes,
                initialBytes,
                growth,
                pipeline.DescriptorSetLayout,
                pool,
                descriptorSet,
                maxStorageBufferRange,
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
            DestroyBestEffort(vk, logicalDevice, logger, pool, buffer, memory, mapped);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Records that the frame was submitted and the instance buffer is in flight.</summary>
    internal void MarkSubmitted() => _growth.MarkInFlight();

    /// <summary>Records that the frame fence signaled and the buffer may be replaced.</summary>
    internal void MarkFenceSignaled() => _growth.MarkFenceSignaled();

    /// <inheritdoc cref="IInstanceBuffer.EnsureCapacity"/>
    internal void EnsureCapacity(int requiredInstances)
    {
        if (!_growth.NeedsGrowth(requiredInstances))
        {
            return;
        }

        if (!_growth.TryGrow(
            requiredInstances,
            _maxStorageBufferRange,
            out int newCapacity,
            out string failure))
        {
            if (_growth.InFlight)
            {
                throw _growth.CreateInFlightException(requiredInstances);
            }

            throw _growth.CreateGrowthException(requiredInstances, failure);
        }

        CreateMappedBuffer(
            _vk,
            _device,
            _physicalDevice,
            _logger,
            _frameIndex,
            newCapacity,
            out Buffer buffer,
            out DeviceMemory memory,
            out void* mapped,
            out ulong allocationBytes);

        // The new buffer/memory are complete before the old pair is released, so a failure above
        // leaves the frame usable at its previous capacity.
        Buffer previousBuffer = _buffer;
        DeviceMemory previousMemory = _memory;
        void* previousMapped = _mapped;
        ulong previousBytes = _bufferBytes;
        int previousCapacity = _growth.Capacity;

        _buffer = buffer;
        _memory = memory;
        _mapped = mapped;
        _allocationBytes = allocationBytes;
        _bufferBytes = (ulong)newCapacity * SpriteInstance.SizeInBytes;
        _growth = new InstanceBufferGrowth(newCapacity);
        _growthCount++;
        WriteInstanceBuffer();

        DestroyMappedBuffer(previousBuffer, previousMemory, previousMapped);

        _logger.LogInformation(
            "Instance buffer capacity grew: frame={Frame}, from={PreviousCapacity}, "
                + "to={Capacity}, previousBytes={PreviousBytes}, bytes={Bytes}, "
                + "limit={Limit}.",
            _frameIndex,
            previousCapacity,
            newCapacity,
            previousBytes,
            _bufferBytes,
            _maxStorageBufferRange);
    }

    /// <inheritdoc cref="IInstanceBuffer.WriteInstance"/>
    internal void WriteInstance(int index, in SpriteInstance instance)
    {
        if ((uint)index >= (uint)_growth.Capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The instance index must be inside the current capacity of "
                    + $"{_growth.Capacity} records.");
        }

        var destination = (byte*)_mapped + (index * SpriteInstance.SizeInBytes);
        SpriteInstanceWriter.Write(destination, instance);
    }

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
        _pool = pool;
        _descriptorSet = descriptorSet;
        _pageCapacity = pageCapacity;
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

        DestroyBestEffort(_vk, _device, _logger, _pool, _buffer, _memory, _mapped);
        _pool = default;
        _buffer = default;
        _memory = default;
        _mapped = null;
    }

    private static void CreateMappedBuffer(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        ILogger logger,
        int frameIndex,
        int capacity,
        out Buffer buffer,
        out DeviceMemory memory,
        out void* mapped,
        out ulong allocationBytes)
    {
        buffer = default;
        memory = default;
        mapped = null;
        allocationBytes = 0;
        ulong bytes = (ulong)capacity * SpriteInstance.SizeInBytes;
        try
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bytes,
                Usage = BufferUsageFlags.StorageBufferBit,
                SharingMode = SharingMode.Exclusive,
            };
            Result result = vk.CreateBuffer(device, in bufferInfo, null, out buffer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateBuffer failed for frame {frameIndex} instance buffer with VkResult "
                        + $"{result} ({bytes} bytes / {capacity} records).");
            }

            MemoryRequirements requirements = vk.GetBufferMemoryRequirements(device, buffer);
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
            result = vk.AllocateMemory(device, in allocateInfo, null, out memory);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkAllocateMemory failed for frame {frameIndex} instance buffer with "
                        + $"VkResult {result} ({requirements.Size} bytes, memory type "
                        + $"{memoryTypeIndex}).");
            }

            result = vk.BindBufferMemory(device, buffer, memory, 0);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkBindBufferMemory failed for frame {frameIndex} instance buffer with "
                        + $"VkResult {result}.");
            }

            void* pointer = null;
            result = vk.MapMemory(device, memory, 0, requirements.Size, 0, &pointer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkMapMemory failed for frame {frameIndex} instance buffer with VkResult "
                        + $"{result} ({requirements.Size} bytes).");
            }

            mapped = pointer;
            allocationBytes = requirements.Size;
        }
        catch
        {
            DestroyMappedBufferBestEffort(vk, device, logger, buffer, memory, mapped);
            throw;
        }
    }

    private void DestroyMappedBuffer(Buffer buffer, DeviceMemory memory, void* mapped)
    {
        DestroyMappedBufferBestEffort(_vk, _device, _logger, buffer, memory, mapped);
    }

    private static void DestroyMappedBufferBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        Buffer buffer,
        DeviceMemory memory,
        void* mapped)
    {
        if (mapped is not null && memory.Handle != 0)
        {
            try
            {
                vk.UnmapMemory(device, memory);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan frame instance-buffer unmap failed.");
            }
        }

        DestroyBufferAndMemory(vk, device, logger, buffer, memory);
    }

    private static void DestroyBufferAndMemory(
        Vk vk,
        Device device,
        ILogger logger,
        Buffer buffer,
        DeviceMemory memory)
    {
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

    private void WriteInstanceBuffer()
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _buffer,
            Offset = 0,
            Range = _bufferBytes,
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
        DeviceMemory memory,
        void* mapped)
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

        if (mapped is not null && memory.Handle != 0)
        {
            try
            {
                vk.UnmapMemory(device, memory);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan frame instance-buffer unmap failed.");
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
