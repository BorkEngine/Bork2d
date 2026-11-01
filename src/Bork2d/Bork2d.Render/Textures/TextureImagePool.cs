using Bork2d.Engine;
using Microsoft.Extensions.Logging;
using Silk.NET.Vulkan;

namespace Bork2d.Render.Textures;

/// <summary>
/// A device-local suballocator for texture-page images (M8 §6.5): multiple images share a small
/// number of <c>vkAllocateMemory</c> blocks with aligned, non-overlapping ranges.
/// </summary>
/// <remarks>
/// <para>
/// A block is allocated on demand with the memory type the image requires and a bounded default
/// size; a page image that does not fit in a block's remaining space allocates a new block. A
/// retired page returns its range to the block that owns it, and the next image reuses a suitable
/// free range instead of consuming fresh block space; adjacent free ranges coalesce, so a fully
/// freed block can serve a large image again. Blocks stay allocated for the run and are freed by
/// <see cref="Destroy"/>; there is no VMA and no per-page <c>vkAllocateMemory</c>.
/// </para>
/// <para>
/// The class is render-thread-owned. <see cref="Destroy"/> is best-effort, idempotent, and never
/// throws (D-M6-27).
/// </para>
/// </remarks>
internal sealed unsafe class TextureImagePool : IDisposable
{
    private const ulong DefaultBlockBytes = 4UL * 1024 * 1024;

    private readonly Vk _vk;
    private readonly Device _device;
    private readonly PhysicalDeviceMemoryProperties _memoryProperties;
    private readonly ILogger _logger;
    private readonly List<Block> _blocks = [];
    private int _destroyed;

    /// <summary>Initializes a new instance of the <see cref="TextureImagePool"/> class.</summary>
    /// <param name="vk">The Vulkan API object that owns the device.</param>
    /// <param name="device">The logical device.</param>
    /// <param name="physicalDevice">The physical device used for memory-type selection.</param>
    /// <param name="logger">The <c>Bork2d.Render</c> logger used by best-effort cleanup.</param>
    internal TextureImagePool(
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

    /// <summary>Gets the total number of device-memory blocks allocated.</summary>
    internal int BlockCount => _blocks.Count;

    /// <summary>Gets the bytes currently reserved for live page images.</summary>
    internal ulong AllocatedBytes { get; private set; }

    /// <summary>Gets the number of free ranges across every block.</summary>
    internal int FreeRangeCount
    {
        get
        {
            var count = 0;
            foreach (Block block in _blocks)
            {
                count += block.Free.Count;
            }

            return count;
        }
    }

    /// <summary>
    /// Reserves an aligned range for one image and returns the memory and offset to bind.
    /// </summary>
    /// <param name="requirements">The image's memory requirements.</param>
    /// <param name="memory">The block's device memory.</param>
    /// <param name="offset">The aligned offset inside the block.</param>
    /// <exception cref="Bork2dException">
    /// No device-local memory type supports the image, or the block allocation failed.
    /// </exception>
    internal void Allocate(
        in MemoryRequirements requirements,
        out DeviceMemory memory,
        out ulong offset)
    {
        ulong alignment = Math.Max(1UL, requirements.Alignment);
        uint memoryTypeIndex = SelectMemoryType(requirements.MemoryTypeBits);

        for (var index = 0; index < _blocks.Count; index++)
        {
            Block block = _blocks[index];
            if (block.MemoryTypeIndex != memoryTypeIndex)
            {
                continue;
            }

            if (TryTakeRange(block, requirements.Size, alignment, out offset))
            {
                AllocatedBytes += requirements.Size;
                memory = block.Memory;
                return;
            }
        }

        Block created = CreateBlock(memoryTypeIndex, requirements.Size, alignment);
        AllocatedBytes += requirements.Size;
        _blocks.Add(created);
        memory = created.Memory;
        offset = 0;
    }

    /// <summary>
    /// Returns one image's range to the block that owns it, coalescing adjacent free ranges.
    /// </summary>
    /// <remarks>
    /// The caller destroys the image bound to the range first. A range that is unknown to the pool,
    /// outside its block, or already free is reported and ignored so cleanup stays best-effort.
    /// </remarks>
    /// <param name="memory">The block's device memory, as returned by <see cref="Allocate"/>.</param>
    /// <param name="offset">The range's aligned offset, as returned by <see cref="Allocate"/>.</param>
    /// <param name="size">The range's allocation size, as returned by <see cref="Allocate"/>.</param>
    internal void Free(DeviceMemory memory, ulong offset, ulong size)
    {
        if (memory.Handle == 0 || size == 0)
        {
            return;
        }

        foreach (Block block in _blocks)
        {
            if (block.Memory.Handle != memory.Handle)
            {
                continue;
            }

            if (offset + size > block.Size || OverlapsFreeRange(block, offset, size))
            {
                _logger.LogError(
                    "Vulkan texture image memory range ({Offset}, {Size}) does not describe a live "
                        + "range of its block; the range is not returned to the pool.",
                    offset,
                    size);
                return;
            }

            block.Free.Add(new MemoryRange(offset, size));
            block.Free.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
            Coalesce(block);
            AllocatedBytes = size >= AllocatedBytes ? 0 : AllocatedBytes - size;
            return;
        }

        _logger.LogError(
            "Vulkan texture image memory free named a memory block the pool does not own; the range "
                + "is ignored.");
    }

    /// <inheritdoc/>
    public void Dispose() => Destroy();

    /// <summary>Frees every block; idempotent and never throws.</summary>
    internal void Destroy()
    {
        if (Interlocked.Exchange(ref _destroyed, 1) != 0)
        {
            return;
        }

        foreach (Block block in _blocks)
        {
            if (block.Memory.Handle == 0)
            {
                continue;
            }

            try
            {
                _vk.FreeMemory(_device, block.Memory, null);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Vulkan texture page memory-block free failed.");
            }
        }

        _blocks.Clear();
        AllocatedBytes = 0;
    }

    private static bool TryTakeRange(Block block, ulong size, ulong alignment, out ulong offset)
    {
        for (var index = 0; index < block.Free.Count; index++)
        {
            MemoryRange range = block.Free[index];
            ulong aligned = Align(range.Offset, alignment);
            ulong end = range.Offset + range.Size;
            if (aligned > end || aligned + size > end)
            {
                continue;
            }

            block.Free.RemoveAt(index);

            // The pieces left by the split replace the range at its sorted position, so the list
            // stays ordered without another sort.
            if (aligned > range.Offset)
            {
                block.Free.Insert(index, new MemoryRange(range.Offset, aligned - range.Offset));
            }

            if (aligned + size < end)
            {
                int suffixIndex = aligned > range.Offset ? index + 1 : index;
                block.Free.Insert(
                    suffixIndex,
                    new MemoryRange(aligned + size, end - (aligned + size)));
            }

            offset = aligned;
            return true;
        }

        offset = 0;
        return false;
    }

    private static bool OverlapsFreeRange(Block block, ulong offset, ulong size)
    {
        ulong end = offset + size;
        foreach (MemoryRange range in block.Free)
        {
            if (offset < range.Offset + range.Size && range.Offset < end)
            {
                return true;
            }
        }

        return false;
    }

    private static void Coalesce(Block block)
    {
        for (var index = block.Free.Count - 1; index > 0; index--)
        {
            MemoryRange previous = block.Free[index - 1];
            MemoryRange current = block.Free[index];
            if (previous.Offset + previous.Size != current.Offset)
            {
                continue;
            }

            block.Free[index - 1] = new MemoryRange(
                previous.Offset,
                previous.Size + current.Size);
            block.Free.RemoveAt(index);
        }
    }

    private Block CreateBlock(uint memoryTypeIndex, ulong requiredBytes, ulong alignment)
    {
        ulong size = Math.Max(DefaultBlockBytes, Align(requiredBytes + alignment, alignment));
        var allocateInfo = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            AllocationSize = size,
            MemoryTypeIndex = memoryTypeIndex,
        };
        Result result = _vk.AllocateMemory(_device, in allocateInfo, null, out DeviceMemory memory);
        if (result != Result.Success)
        {
            throw new Bork2dException(
                $"vkAllocateMemory failed for a texture page image block with VkResult {result} "
                    + $"({size} bytes, memory type {memoryTypeIndex}).");
        }

        var block = new Block
        {
            Memory = memory,
            Size = size,
            MemoryTypeIndex = memoryTypeIndex,
        };

        // Offset zero is aligned for every alignment, so a fresh block hands out its start and
        // keeps the rest as the first free range.
        if (size > requiredBytes)
        {
            block.Free.Add(new MemoryRange(requiredBytes, size - requiredBytes));
        }

        return block;
    }

    private uint SelectMemoryType(uint memoryTypeBits)
    {
        for (var index = 0; index < _memoryProperties.MemoryTypeCount; index++)
        {
            if ((memoryTypeBits & (1u << index)) == 0)
            {
                continue;
            }

            if ((_memoryProperties.MemoryTypes[index].PropertyFlags
                & MemoryPropertyFlags.DeviceLocalBit) != 0)
            {
                return (uint)index;
            }
        }

        throw new Bork2dException(
            "No device-local Vulkan memory type supports a texture page image "
                + $"(memoryTypeBits=0x{memoryTypeBits:X}).");
    }

    private static ulong Align(ulong value, ulong alignment)
        => alignment <= 1 ? value : (value + alignment - 1) / alignment * alignment;

    private readonly record struct MemoryRange(ulong Offset, ulong Size);

    private sealed class Block
    {
        internal DeviceMemory Memory;
        internal ulong Size;
        internal uint MemoryTypeIndex;
        internal List<MemoryRange> Free = [];
    }
}
