using Bork2d.Engine;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;
using Buffer = Silk.NET.Vulkan.Buffer;

namespace Bork2d.Render.Textures;

/// <summary>
/// A host-visible coherent staging pool for texture uploads (M8 §6.5): buffers are reused across
/// uploads and held until the upload's fence signals.
/// </summary>
/// <remarks>
/// <para>
/// Each lease owns a whole buffer, mapped once for the buffer's lifetime, so an upload copies the
/// validated pixels into mapped memory and submits a <c>vkCmdCopyBufferToImage</c>. A lease is
/// released only after its fence signals, so a buffer is never overwritten while a copy may still
/// read it. Buffers grow to the largest page seen and are kept for the run; there is no per-frame
/// allocation.
/// </para>
/// <para>
/// The class is render-thread-owned. <see cref="Destroy"/> unmaps and frees every buffer;
/// best-effort, idempotent, and never throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class StagingBufferPool : IDisposable
{
    private const ulong DefaultBufferBytes = 1UL * 1024 * 1024;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;
    private readonly ILogger _logger;
    private readonly List<StagingBuffer> _buffers = [];
    private int _destroyed;

    /// <summary>Initializes a new instance of the <see cref="StagingBufferPool"/> class.</summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The logical device.</param>
    /// <param name="physicalDevice">The physical device used for memory-type selection.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    internal StagingBufferPool(
        Vk vk,
        Device device,
        PhysicalDevice physicalDevice,
        ILogger logger)
    {
        _vk = vk;
        _device = device;
        _logger = logger;
        _memoryProperties = vk.GetPhysicalDeviceMemoryProperties(physicalDevice);
    }

    /// <summary>Gets the number of staging buffers the pool owns.</summary>
    internal int BufferCount => _buffers.Count;

    /// <summary>Gets the number of leases currently held.</summary>
    internal int InUseCount
    {
        get
        {
            var count = 0;
            foreach (StagingBuffer buffer in _buffers)
            {
                if (buffer.InUse)
                {
                    count++;
                }
            }

            return count;
        }
    }

    /// <summary>
    /// Reserves a mapped staging buffer of at least <paramref name="size"/> bytes.
    /// </summary>
    /// <param name="size">The payload length to stage.</param>
    /// <returns>The lease to copy into and reuse on release.</returns>
    /// <exception cref="Bork2dException">The buffer or its memory could not be created.</exception>
    internal StagingLease Acquire(ulong size)
    {
        for (var index = 0; index < _buffers.Count; index++)
        {
            StagingBuffer buffer = _buffers[index];
            if (buffer.InUse || buffer.Capacity < size)
            {
                continue;
            }

            buffer.InUse = true;
            return new StagingLease(index, buffer.Buffer, buffer.Mapped);
        }

        StagingBuffer created = CreateBuffer(Math.Max(DefaultBufferBytes, Align(size)));
        created.InUse = true;
        _buffers.Add(created);
        return new StagingLease(_buffers.Count - 1, created.Buffer, created.Mapped);
    }

    /// <summary>Releases a lease once its upload fence has signaled.</summary>
    /// <param name="lease">The lease to release.</param>
    internal void Release(StagingLease lease)
    {
        if ((uint)lease.Index < (uint)_buffers.Count)
        {
            _buffers[lease.Index].InUse = false;
        }
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Unmaps and frees every staging buffer; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        foreach (StagingBuffer buffer in _buffers)
        {
            if (buffer.Mapped != 0)
            {
                try
                {
                    _vk.UnmapMemory(_device, buffer.Memory);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Vulkan staging memory unmap failed.");
                }
            }

            if (buffer.Buffer.Handle != 0)
            {
                try
                {
                    _vk.DestroyBuffer(_device, buffer.Buffer, null);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Vulkan staging buffer destruction failed.");
                }
            }

            if (buffer.Memory.Handle != 0)
            {
                try
                {
                    _vk.FreeMemory(_device, buffer.Memory, null);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Vulkan staging memory free failed.");
                }
            }
        }

        _buffers.Clear();
    }

    private StagingBuffer CreateBuffer(ulong size)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit,
            SharingMode = SharingMode.Exclusive,
        };
        Result result = _vk.CreateBuffer(_device, in bufferInfo, null, out Buffer buffer);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkCreateBuffer failed for a texture staging buffer with VkResult {result} "
                    + $"({size} bytes).");
        }

        try
        {
            MemoryRequirements requirements = _vk.GetBufferMemoryRequirements(_device, buffer);
            uint memoryTypeIndex = SelectMemoryType(requirements.MemoryTypeBits);
            var allocateInfo = new MemoryAllocateInfo
            {
                SType = StructureType.MemoryAllocateInfo,
                AllocationSize = requirements.Size,
                MemoryTypeIndex = memoryTypeIndex,
            };
            result = _vk.AllocateMemory(_device, in allocateInfo, null, out DeviceMemory memory);
            if (result != Result.Success)
            {
                throw new Bork2dException(
                    $"vkAllocateMemory failed for a texture staging buffer with VkResult {result} "
                        + $"({requirements.Size} bytes, memory type {memoryTypeIndex}).");
            }

            try
            {
                result = _vk.BindBufferMemory(_device, buffer, memory, 0);
                if (result != Result.Success)
                {
                    throw new Bork2dException(
                        $"vkBindBufferMemory failed for a texture staging buffer with VkResult "
                            + $"{result}.");
                }

                void* mapped = null;
                result = _vk.MapMemory(_device, memory, 0, Vk.WholeSize, 0, &mapped);
                if (result != Result.Success)
                {
                    throw new Bork2dException(
                        $"vkMapMemory failed for a texture staging buffer with VkResult {result}.");
                }

                return new StagingBuffer
                {
                    Buffer = buffer,
                    Memory = memory,
                    Capacity = requirements.Size,
                    Mapped = (nint)mapped,
                };
            }
            catch
            {
                _vk.FreeMemory(_device, memory, null);
                throw;
            }
        }
        catch
        {
            _vk.DestroyBuffer(_device, buffer, null);
            throw;
        }
    }

    private uint SelectMemoryType(uint memoryTypeBits)
    {
        const MemoryPropertyFlags required =
            MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit;
        for (var index = 0; index < _memoryProperties.MemoryTypeCount; index++)
        {
            if ((memoryTypeBits & (1u << index)) == 0)
            {
                continue;
            }

            if ((_memoryProperties.MemoryTypes[index].PropertyFlags & required) == required)
            {
                return (uint)index;
            }
        }

        throw new Bork2dException(
            "No host-visible coherent Vulkan memory type supports a texture staging buffer "
                + $"(memoryTypeBits=0x{memoryTypeBits:X}).");
    }

    private static ulong Align(ulong value) => (value + 15UL) / 16UL * 16UL;

    private sealed class StagingBuffer
    {
        internal Buffer Buffer;
        internal DeviceMemory Memory;
        internal ulong Capacity;
        internal nint Mapped;
        internal bool InUse;
    }
}

/// <summary>
/// A mapped staging buffer reservation held until its upload fence signals.
/// </summary>
/// <param name="Index">The pool slot index.</param>
/// <param name="Buffer">The Vulkan buffer to copy from.</param>
/// <param name="Mapped">The mapped host pointer.</param>
internal readonly record struct StagingLease(int Index, Buffer Buffer, nint Mapped);
