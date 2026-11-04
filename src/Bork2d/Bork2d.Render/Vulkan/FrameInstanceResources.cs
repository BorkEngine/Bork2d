using System.Numerics;
using Bork2d.Engine;
using Bork2d.Render.Sprites;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One GPU frame's growable instance buffer (set 0 binding 0), bindless page array (set 0 binding 1),
/// growable global uniform buffer (set 1 binding 2), and present scene-image sampler (the present
/// pipeline's set 0 binding 0) (M8 §6.5/§6.6, shader ABI v2).
/// </summary>
/// <remarks>
/// <para>
/// The resources are created once per GPU frame slot at startup and never per frame. Binding 0 is a
/// host-visible coherent storage buffer mapped once, holding <see cref="InstanceCapacity"/> 176-byte
/// records; the sprite frame planner writes every record directly into that mapping, with no CPU
/// frame array and no staging copy. When a frame needs more records than its buffer holds, the
/// renderer (having observed the frame's fence) calls <see cref="EnsureCapacity"/>, which recreates
/// the buffer at a power-of-two capacity, remaps it, and rewrites binding 0. Growth is logged and
/// counted once per capacity change; a device-limit or allocation failure is a fatal
/// <see cref="Bork2dException"/> and never truncates instances.
/// </para>
/// <para>
/// Binding 1 of the sprite descriptor set is the bindless combined-image-sampler array from the
/// shared <see cref="SpritePipeline"/> layout, allocated with a variable descriptor count equal to
/// the current page capacity and the <c>PARTIALLY_BOUND | UPDATE_AFTER_BIND |
/// VARIABLE_DESCRIPTOR_COUNT</c> flags. Page descriptors are written by <see cref="WritePage"/> as
/// pages become ready and rewritten after a capacity growth (the frame set waits every frame fence,
/// calls <see cref="RecreateBindless(int)"/>, and then rewrites every resident page).
/// </para>
/// <para>
/// The global uniform block lives in a separate descriptor set (set 1, binding 2) because Vulkan
/// forbids a dynamic uniform buffer in a set that contains an update-after-bind binding. It holds
/// <see cref="GlobalUniformBlock"/> blocks at the device's
/// <c>minUniformBufferOffsetAlignment</c>-aligned stride. It starts at
/// <see cref="InitialGlobalBlockCapacity"/> blocks and grows by the same power-of-two, fence-gated
/// rule as the instance buffer. The sprite planner writes exactly one 144-byte block per shader run
/// and the sprite pass binds each run with that run's dynamic byte offset.
/// </para>
/// <para>
/// The present set holds the linear scene image view bound to the present pipeline's sampler; the
/// owning frame calls <see cref="WritePresentView"/> after creating or recreating its scene image, and
/// <see cref="RecreateBindless(int)"/> rewrites it from the last view. It is a dedicated descriptor
/// set because the present pipeline has its own layout and must not reuse or change the sprite
/// bindless set. <see cref="Destroy"/> is best-effort, idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class FrameInstanceResources : IDisposable, IInstanceBuffer, IGlobalUniformBuffer
{
    /// <summary>The approved initial per-frame capacity in 176-byte records.</summary>
    internal const int InitialCapacity = InstanceBufferGrowth.InitialCapacity;

    /// <summary>The initial number of global uniform blocks per frame.</summary>
    internal const int InitialGlobalBlockCapacity = 16;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDevice _physicalDevice;
    private readonly ILogger _logger;
    private readonly DescriptorSetLayout _layout;
    private readonly DescriptorSetLayout _globalLayout;
    private readonly DescriptorSetLayout _presentLayout;
    private readonly Sampler _presentSampler;
    private readonly ulong _maxStorageBufferRange;
    private readonly uint _maxUniformBufferRange;
    private readonly int _frameIndex;
    private Buffer _buffer;
    private DeviceMemory _memory;
    private void* _mapped;
    private ulong _allocationBytes;
    private ulong _bufferBytes;
    private InstanceBufferGrowth _growth;
    private Buffer _globalBuffer;
    private DeviceMemory _globalMemory;
    private void* _globalMapped;
    private ulong _globalBufferBytes;
    private int _globalCapacity;
    private readonly int _globalBlockStride;
    private int _globalGrowthCount;
    private DescriptorPool _pool;
    private DescriptorSet _descriptorSet;
    private DescriptorSet _globalSet;
    private DescriptorSet _presentSet;
    private ImageView _sceneView;
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
        Buffer globalBuffer,
        DeviceMemory globalMemory,
        void* globalMapped,
        ulong globalBufferBytes,
        int globalCapacity,
        int globalBlockStride,
        DescriptorSetLayout layout,
        DescriptorSetLayout globalLayout,
        DescriptorSetLayout presentLayout,
        Sampler presentSampler,
        DescriptorPool pool,
        DescriptorSet descriptorSet,
        DescriptorSet globalSet,
        DescriptorSet presentSet,
        ulong maxStorageBufferRange,
        uint maxUniformBufferRange,
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
        _globalBuffer = globalBuffer;
        _globalMemory = globalMemory;
        _globalMapped = globalMapped;
        _globalBufferBytes = globalBufferBytes;
        _globalCapacity = globalCapacity;
        _globalBlockStride = globalBlockStride;
        _layout = layout;
        _globalLayout = globalLayout;
        _presentLayout = presentLayout;
        _presentSampler = presentSampler;
        _pool = pool;
        _descriptorSet = descriptorSet;
        _globalSet = globalSet;
        _presentSet = presentSet;
        _maxStorageBufferRange = maxStorageBufferRange;
        _maxUniformBufferRange = maxUniformBufferRange;
        _pageCapacity = pageCapacity;
        _frameIndex = frameIndex;
    }

    /// <summary>Gets the current instance-record capacity (initially 1024).</summary>
    internal int InstanceCapacity => _growth.Capacity;

    int IInstanceBuffer.InstanceCapacity => _growth.Capacity;

    int IGlobalUniformBuffer.Capacity => _globalCapacity;

    int IGlobalUniformBuffer.BlockStride => _globalBlockStride;

    void IInstanceBuffer.EnsureCapacity(int requiredInstances) => EnsureCapacity(requiredInstances);

    void IInstanceBuffer.WriteInstance(int index, in SpriteInstance instance)
        => WriteInstance(index, instance);

    void IGlobalUniformBuffer.EnsureCapacity(int requiredBlocks)
        => EnsureGlobalCapacity(requiredBlocks);

    void IGlobalUniformBuffer.WriteBlock(int index, in GlobalUniformBlock block)
        => WriteGlobalBlock(index, block);

    /// <summary>Gets the number of capacity growths this frame's instance buffer performed.</summary>
    internal int GrowthCount => _growthCount;

    /// <summary>Gets the instance buffer handle (a diagnostic/label accessor).</summary>
    internal Buffer InstanceBuffer => _buffer;

    /// <summary>Gets the mapped host-visible address of record <c>0</c>.</summary>
    internal nint MappedAddress => (nint)_mapped;

    /// <summary>Gets the allocated device-memory size in bytes.</summary>
    internal ulong AllocationBytes => _allocationBytes;

    /// <summary>Gets the descriptor-set binding-0 range in bytes (the requested buffer size).</summary>
    internal ulong InstanceBufferBytesInRange => _bufferBytes;

    /// <summary>Gets the current global uniform block capacity (initially 16).</summary>
    internal int GlobalBlockCapacity => _globalCapacity;

    /// <summary>Gets the device-aligned byte stride between consecutive global blocks.</summary>
    internal int GlobalBlockStride => _globalBlockStride;

    /// <summary>Gets the allocated global uniform buffer size in bytes.</summary>
    internal ulong GlobalBufferBytes => _globalBufferBytes;

    /// <summary>Gets the number of global block capacity growths this frame performed.</summary>
    internal int GlobalGrowthCount => _globalGrowthCount;

    /// <summary>Gets the frame descriptor set (instance buffer and bindless page array).</summary>
    internal DescriptorSet DescriptorSet => _descriptorSet;

    /// <summary>Gets the frame's global uniform descriptor set.</summary>
    internal DescriptorSet GlobalDescriptorSet => _globalSet;

    /// <summary>
    /// Gets the frame's present descriptor set: the linear scene image view bound to the present
    /// pipeline's combined image sampler.
    /// </summary>
    internal DescriptorSet PresentDescriptorSet => _presentSet;

    /// <summary>Gets the current bindless page capacity (the set's variable descriptor count).</summary>
    internal int PageCapacity => _pageCapacity;

    /// <summary>
    /// Creates the instance buffer, the global uniform buffer, and the frame's bindless descriptor
    /// pool/set.
    /// </summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The selected logical device.</param>
    /// <param name="physicalDevice">The physical device used to select the memory type.</param>
    /// <param name="frameIndex">The GPU frame index, used for debug names only.</param>
    /// <param name="pipeline">
    /// The sprite pipeline that owns the descriptor-set layout and validates the bindless limit.
    /// </param>
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
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    /// <returns>The created resources; the caller owns them.</returns>
    /// <exception cref="Bork2dException">A Vulkan object could not be created or a limit is exceeded.</exception>
    internal static FrameInstanceResources Create(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        int frameIndex,
        SpritePipeline pipeline,
        PresentPipeline present,
        int pageCapacity,
        uint maxStorageBufferRange,
        uint maxUniformBufferRange,
        ulong minUniformBufferOffsetAlignment,
        IVulkanInstance? instance,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(present);
        ArgumentNullException.ThrowIfNull(logger);
        if (pageCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageCapacity),
                pageCapacity,
                "The bindless page capacity must be positive.");
        }

        if (maxUniformBufferRange < GlobalUniformBlock.SizeInBytes)
        {
            throw new Bork2dException(
                $"The per-frame global uniform buffer needs a {GlobalUniformBlock.SizeInBytes}-byte "
                    + $"range, which exceeds the selected device's maxUniformBufferRange of "
                    + $"{maxUniformBufferRange}.");
        }

        int globalBlockStride = AlignBlockStride(
            minUniformBufferOffsetAlignment,
            frameIndex);
        var growth = new InstanceBufferGrowth(InstanceBufferGrowth.InitialCapacity);
        ulong initialBytes = (ulong)InstanceBufferGrowth.InitialCapacity
            * SpriteInstance.SizeInBytes;
        if (initialBytes > maxStorageBufferRange)
        {
            throw new Bork2dException(
                $"The per-frame instance buffer needs {initialBytes} bytes for "
                    + $"{InstanceBufferGrowth.InitialCapacity} 176-byte records, which exceeds the "
                    + $"selected device's maxStorageBufferRange of {maxStorageBufferRange}.");
        }

        ulong initialGlobalBytes = (ulong)InitialGlobalBlockCapacity * (ulong)globalBlockStride;

        Device logicalDevice = device;
        var pool = default(DescriptorPool);
        var descriptorSet = default(DescriptorSet);
        var globalDescriptorSet = default(DescriptorSet);
        var presentDescriptorSet = default(DescriptorSet);
        var buffer = default(Buffer);
        var memory = default(DeviceMemory);
        var globalBuffer = default(Buffer);
        var globalMemory = default(DeviceMemory);
        void* mapped = null;
        void* globalMapped = null;
        var allocationBytes = 0UL;
        try
        {
            CreateMappedBuffer(
                vk,
                logicalDevice,
                physicalDevice,
                logger,
                frameIndex,
                initialBytes,
                BufferUsageFlags.StorageBufferBit,
                "instance buffer",
                out buffer,
                out memory,
                out mapped,
                out allocationBytes);

            CreateMappedBuffer(
                vk,
                logicalDevice,
                physicalDevice,
                logger,
                frameIndex,
                initialGlobalBytes,
                BufferUsageFlags.UniformBufferBit,
                "global uniform buffer",
                out globalBuffer,
                out globalMemory,
                out globalMapped,
                out _);

            CreateDescriptorPoolAndSet(
                vk,
                logicalDevice,
                pipeline.DescriptorSetLayout,
                pipeline.GlobalSetLayout,
                present.DescriptorSetLayout,
                pageCapacity,
                frameIndex,
                out pool,
                out descriptorSet,
                out globalDescriptorSet,
                out presentDescriptorSet);

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
                globalBuffer,
                globalMemory,
                globalMapped,
                initialGlobalBytes,
                InitialGlobalBlockCapacity,
                globalBlockStride,
                pipeline.DescriptorSetLayout,
                pipeline.GlobalSetLayout,
                present.DescriptorSetLayout,
                present.Sampler,
                pool,
                descriptorSet,
                globalDescriptorSet,
                presentDescriptorSet,
                maxStorageBufferRange,
                maxUniformBufferRange,
                pageCapacity,
                frameIndex);
            resources.WriteInstanceBuffer();
            resources.WriteGlobalBuffer();

            if (instance is not null)
            {
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.Buffer,
                    (nint)buffer.Handle,
                    $"Bork2d frame {frameIndex} instance buffer");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.Buffer,
                    (nint)globalBuffer.Handle,
                    $"Bork2d frame {frameIndex} global uniform buffer");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.DescriptorSet,
                    (nint)descriptorSet.Handle,
                    $"Bork2d frame {frameIndex} descriptor set");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.DescriptorSet,
                    (nint)globalDescriptorSet.Handle,
                    $"Bork2d frame {frameIndex} global descriptor set");
                instance.SetObjectName(
                    logicalDevice.Handle,
                    ObjectType.DescriptorSet,
                    (nint)presentDescriptorSet.Handle,
                    $"Bork2d frame {frameIndex} present descriptor set");
            }

            return resources;
        }
        catch
        {
            DestroyBestEffort(
                vk,
                logicalDevice,
                logger,
                pool,
                buffer,
                memory,
                mapped,
                globalBuffer,
                globalMemory,
                globalMapped);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Records that the frame was submitted and the buffers are in flight.</summary>
    internal void MarkSubmitted() => _growth.MarkInFlight();

    /// <summary>Records that the frame fence signaled and the buffers may be replaced.</summary>
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

        ulong bytes = (ulong)newCapacity * SpriteInstance.SizeInBytes;
        CreateMappedBuffer(
            _vk,
            _device,
            _physicalDevice,
            _logger,
            _frameIndex,
            bytes,
            BufferUsageFlags.StorageBufferBit,
            "instance buffer",
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
        _bufferBytes = bytes;
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
    /// Ensures the frame holds at least <paramref name="requiredBlocks"/> global uniform blocks.
    /// </summary>
    /// <remarks>
    /// The growth rule mirrors <see cref="EnsureCapacity"/>: the frame fence must already have
    /// signaled, the new capacity is <c>roundUpToPowerOfTwo(max(required, capacity * 2))</c>, and
    /// the buffer is recreated and remapped before the old one is released.
    /// </remarks>
    /// <param name="requiredBlocks">The frame's required block count.</param>
    /// <exception cref="ArgumentOutOfRangeException">The required count is negative.</exception>
    /// <exception cref="InvalidOperationException">The frame is still in flight.</exception>
    /// <exception cref="Bork2dException">The new capacity cannot be represented or allocated.</exception>
    internal void EnsureGlobalCapacity(int requiredBlocks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(requiredBlocks);
        if (requiredBlocks <= _globalCapacity)
        {
            return;
        }

        if (_growth.InFlight)
        {
            throw new InvalidOperationException(
                $"The per-frame global uniform buffer cannot grow from {_globalCapacity} to hold "
                    + $"{requiredBlocks} blocks while the frame is in flight; the renderer must "
                    + "observe the frame fence before growing.");
        }

        long doubled = (long)_globalCapacity * 2L;
        long target = Math.Max(requiredBlocks, doubled);
        if (target > int.MaxValue)
        {
            throw new Bork2dException(
                $"The per-frame global uniform buffer cannot grow from {_globalCapacity} to hold "
                    + $"{requiredBlocks} blocks: the requested capacity {target} exceeds the maximum "
                    + "representable block count.");
        }

        uint rounded = BitOperations.RoundUpToPowerOf2((uint)target);
        if (rounded == 0 || rounded > int.MaxValue)
        {
            throw new Bork2dException(
                $"The per-frame global uniform buffer cannot grow from {_globalCapacity} to hold "
                    + $"{requiredBlocks} blocks: the requested capacity {target} cannot be rounded "
                    + "up to a power of two.");
        }

        int newCapacity = (int)rounded;
        ulong bytes = (ulong)newCapacity * (ulong)_globalBlockStride;
        CreateMappedBuffer(
            _vk,
            _device,
            _physicalDevice,
            _logger,
            _frameIndex,
            bytes,
            BufferUsageFlags.UniformBufferBit,
            "global uniform buffer",
            out Buffer buffer,
            out DeviceMemory memory,
            out void* mapped,
            out _);

        Buffer previousBuffer = _globalBuffer;
        DeviceMemory previousMemory = _globalMemory;
        void* previousMapped = _globalMapped;
        ulong previousBytes = _globalBufferBytes;
        int previousCapacity = _globalCapacity;

        _globalBuffer = buffer;
        _globalMemory = memory;
        _globalMapped = mapped;
        _globalBufferBytes = bytes;
        _globalCapacity = newCapacity;
        _globalGrowthCount++;
        WriteGlobalBuffer();

        DestroyMappedBuffer(previousBuffer, previousMemory, previousMapped);

        _logger.LogInformation(
            "Global uniform buffer capacity grew: frame={Frame}, from={PreviousCapacity}, "
                + "to={Capacity}, previousBytes={PreviousBytes}, bytes={Bytes}, stride={Stride}, "
                + "maxRange={MaxRange}.",
            _frameIndex,
            previousCapacity,
            newCapacity,
            previousBytes,
            _globalBufferBytes,
            _globalBlockStride,
            _maxUniformBufferRange);
    }

    /// <inheritdoc cref="IGlobalUniformBuffer.WriteBlock"/>
    internal void WriteGlobalBlock(int index, in GlobalUniformBlock block)
    {
        if ((uint)index >= (uint)_globalCapacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The global block index must be inside the current capacity of "
                    + $"{_globalCapacity} blocks.");
        }

        var destination = (byte*)_globalMapped + ((long)index * _globalBlockStride);
        *(GlobalUniformBlock*)destination = block;
    }

    /// <summary>
    /// Recreates the descriptor pool and both frame descriptor sets with a new variable descriptor
    /// count.
    /// </summary>
    /// <remarks>
    /// The caller must have waited every in-flight frame fence first (capacity growth happens
    /// between frames), and must rewrite every resident page descriptor afterwards because the new
    /// set is empty. Bindings 0, the global set, and the present set are rewritten here from the live
    /// buffers and the last scene view.
    /// </remarks>
    /// <param name="pageCapacity">The new bindless page capacity, including the placeholder.</param>
    /// <exception cref="Bork2dException">The pool or sets could not be created.</exception>
    internal void RecreateBindless(int pageCapacity)
    {
        if (pageCapacity <= _pageCapacity)
        {
            return;
        }

        var pool = default(DescriptorPool);
        var descriptorSet = default(DescriptorSet);
        var globalDescriptorSet = default(DescriptorSet);
        var presentDescriptorSet = default(DescriptorSet);
        CreateDescriptorPoolAndSet(
            _vk,
            _device,
            _layout,
            _globalLayout,
            _presentLayout,
            pageCapacity,
            _frameIndex,
            out pool,
            out descriptorSet,
            out globalDescriptorSet,
            out presentDescriptorSet);

        // The new pool/sets exist before the old set is destroyed, so a failure leaves the frame
        // usable with its previous capacity.
        DestroyPool(_pool);
        _pool = pool;
        _descriptorSet = descriptorSet;
        _globalSet = globalDescriptorSet;
        _presentSet = presentDescriptorSet;
        _pageCapacity = pageCapacity;
        WriteInstanceBuffer();
        WriteGlobalBuffer();
        WritePresentBuffer();
    }

    /// <summary>
    /// Writes the frame's linear scene image view into the present descriptor set.
    /// </summary>
    /// <remarks>
    /// The owning frame calls this after it creates or recreates its scene image. It is safe only when
    /// the frame is not in flight (startup, swapchain recreation after <c>vkDeviceWaitIdle</c>, or a
    /// bindless recreation after every frame fence was observed).
    /// </remarks>
    /// <param name="sceneView">The scene image view, in <c>SHADER_READ_ONLY_OPTIMAL</c> when sampled.</param>
    internal void WritePresentView(ImageView sceneView)
    {
        _sceneView = sceneView;
        WritePresentBuffer();
    }

    /// <summary>Destroys the descriptor set/pool and both buffers; never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        DestroyBestEffort(
            _vk,
            _device,
            _logger,
            _pool,
            _buffer,
            _memory,
            _mapped,
            _globalBuffer,
            _globalMemory,
            _globalMapped);
        _pool = default;
        _buffer = default;
        _memory = default;
        _mapped = null;
        _globalBuffer = default;
        _globalMemory = default;
        _globalMapped = null;
        _descriptorSet = default;
        _globalSet = default;
        _presentSet = default;
        _sceneView = default;
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

    private static int AlignBlockStride(ulong alignment, int frameIndex)
    {
        ulong effective = alignment == 0 ? 1UL : alignment;
        ulong stride = GlobalUniformBlock.SizeInBytes;
        if (effective > 1)
        {
            stride = ((stride + effective - 1) / effective) * effective;
        }

        if (stride > int.MaxValue)
        {
            throw new Bork2dException(
                $"Frame {frameIndex} has an unusable minUniformBufferOffsetAlignment of {alignment}; "
                    + $"the {GlobalUniformBlock.SizeInBytes}-byte global block cannot be aligned.");
        }

        return (int)stride;
    }

    private static void CreateMappedBuffer(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        ILogger logger,
        int frameIndex,
        ulong bytes,
        BufferUsageFlags usage,
        string description,
        out Buffer buffer,
        out DeviceMemory memory,
        out void* mapped,
        out ulong allocationBytes)
    {
        buffer = default;
        memory = default;
        mapped = null;
        allocationBytes = 0;
        try
        {
            var bufferInfo = new BufferCreateInfo
            {
                SType = StructureType.BufferCreateInfo,
                Size = bytes,
                Usage = usage,
                SharingMode = SharingMode.Exclusive,
            };
            Result result = vk.CreateBuffer(device, in bufferInfo, null, out buffer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkCreateBuffer failed for frame {frameIndex} {description} with VkResult "
                        + $"{result} ({bytes} bytes).");
            }

            MemoryRequirements requirements = vk.GetBufferMemoryRequirements(device, buffer);
            PhysicalDeviceMemoryProperties memoryProperties =
                vk.GetPhysicalDeviceMemoryProperties(physicalDevice);
            uint memoryTypeIndex = SelectMemoryType(
                memoryProperties,
                requirements.MemoryTypeBits,
                frameIndex,
                description);
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
                    $"vkAllocateMemory failed for frame {frameIndex} {description} with "
                        + $"VkResult {result} ({requirements.Size} bytes, memory type "
                        + $"{memoryTypeIndex}).");
            }

            result = vk.BindBufferMemory(device, buffer, memory, 0);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkBindBufferMemory failed for frame {frameIndex} {description} with "
                        + $"VkResult {result}.");
            }

            void* pointer = null;
            result = vk.MapMemory(device, memory, 0, requirements.Size, 0, &pointer);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkMapMemory failed for frame {frameIndex} {description} with VkResult "
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
                logger.LogError(exception, "Vulkan frame buffer unmap failed.");
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
                logger.LogError(exception, "Vulkan frame buffer destruction failed.");
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
                logger.LogError(exception, "Vulkan frame buffer memory free failed.");
            }
        }
    }

    private static void CreateDescriptorPoolAndSet(
        Vk vk,
        Device device,
        DescriptorSetLayout layout,
        DescriptorSetLayout globalLayout,
        DescriptorSetLayout presentLayout,
        int pageCapacity,
        int frameIndex,
        out DescriptorPool pool,
        out DescriptorSet descriptorSet,
        out DescriptorSet globalDescriptorSet,
        out DescriptorSet presentDescriptorSet)
    {
        DescriptorPoolSize* poolSizes = stackalloc DescriptorPoolSize[4];
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
        poolSizes[2] = new DescriptorPoolSize
        {
            Type = DescriptorType.UniformBufferDynamic,
            DescriptorCount = 1,
        };

        // The present set samples the linear scene image with its own sampler; it is separate from
        // the bindless page array so the present pipeline keeps a one-binding layout.
        poolSizes[3] = new DescriptorPoolSize
        {
            Type = DescriptorType.CombinedImageSampler,
            DescriptorCount = 1,
        };
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 3,
            PoolSizeCount = 4,
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

        DescriptorSetLayout* globalSetLayouts = &globalLayout;
        var globalSetInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = globalSetLayouts,
        };
        var allocatedGlobal = default(DescriptorSet);
        result = vk.AllocateDescriptorSets(device, in globalSetInfo, &allocatedGlobal);
        if (result != Result.Success)
        {
            vk.DestroyDescriptorPool(device, pool, null);
            pool = default;
            throw new Bork2dException(
                $"vkAllocateDescriptorSets failed for frame {frameIndex}'s global set with "
                    + $"VkResult {result}.");
        }

        globalDescriptorSet = allocatedGlobal;

        DescriptorSetLayout* presentSetLayouts = &presentLayout;
        var presentSetInfo = new DescriptorSetAllocateInfo
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = pool,
            DescriptorSetCount = 1,
            PSetLayouts = presentSetLayouts,
        };
        var allocatedPresent = default(DescriptorSet);
        result = vk.AllocateDescriptorSets(device, in presentSetInfo, &allocatedPresent);
        if (result != Result.Success)
        {
            vk.DestroyDescriptorPool(device, pool, null);
            pool = default;
            throw new Bork2dException(
                $"vkAllocateDescriptorSets failed for frame {frameIndex}'s present set with "
                    + $"VkResult {result}.");
        }

        presentDescriptorSet = allocatedPresent;
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

    private void WriteGlobalBuffer()
    {
        var bufferInfo = new DescriptorBufferInfo
        {
            Buffer = _globalBuffer,
            Offset = 0,
            Range = GlobalUniformBlock.SizeInBytes,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _globalSet,
            DstBinding = SpritePipeline.GlobalBinding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.UniformBufferDynamic,
            PBufferInfo = &bufferInfo,
        };
        _vk.UpdateDescriptorSets(_device, 1, &write, 0, null);
    }

    private void WritePresentBuffer()
    {
        if (_sceneView.Handle == 0)
        {
            return;
        }

        var imageInfo = new DescriptorImageInfo
        {
            Sampler = _presentSampler,
            ImageView = _sceneView,
            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
        };
        var write = new WriteDescriptorSet
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = _presentSet,
            DstBinding = PresentPipeline.SceneBinding,
            DstArrayElement = 0,
            DescriptorCount = 1,
            DescriptorType = DescriptorType.CombinedImageSampler,
            PImageInfo = &imageInfo,
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
        int frameIndex,
        string description)
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
            $"No host-visible coherent Vulkan memory type supports frame {frameIndex}'s "
                + $"{description} (memoryTypeBits=0x{memoryTypeBits:X}).");
    }

    private static void DestroyBestEffort(
        Vk vk,
        Device device,
        ILogger logger,
        DescriptorPool pool,
        Buffer buffer,
        DeviceMemory memory,
        void* mapped,
        Buffer globalBuffer,
        DeviceMemory globalMemory,
        void* globalMapped)
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

        if (globalMapped is not null && globalMemory.Handle != 0)
        {
            try
            {
                vk.UnmapMemory(device, globalMemory);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Vulkan frame global-buffer unmap failed.");
            }
        }

        DestroyBufferAndMemory(vk, device, logger, buffer, memory);
        DestroyBufferAndMemory(vk, device, logger, globalBuffer, globalMemory);
    }
}
