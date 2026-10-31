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
/// size; a page image that does not fit in a block's remaining space allocates a new block. Pages
/// live for the run, so ranges are never individually freed; <see cref="Destroy"/> frees every
/// block after the images bound to them are destroyed. There is no VMA and no per-page
/// <c>vkAllocateMemory</c>.
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

            ulong aligned = Align(block.Used, alignment);
            if (aligned + requirements.Size > block.Size)
            {
                continue;
            }

            block.Used = aligned + requirements.Size;
            memory = block.Memory;
            offset = aligned;
            return;
        }

        Block created = CreateBlock(memoryTypeIndex, requirements.Size, alignment);
        created.Used = Align(0, alignment) + requirements.Size;
        _blocks.Add(created);
        memory = created.Memory;
        offset = 0;
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

        return new Block
        {
            Memory = memory,
            Size = size,
            MemoryTypeIndex = memoryTypeIndex,
        };
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

    private sealed class Block
    {
        internal DeviceMemory Memory;
        internal ulong Size;
        internal ulong Used;
        internal uint MemoryTypeIndex;
    }
}
