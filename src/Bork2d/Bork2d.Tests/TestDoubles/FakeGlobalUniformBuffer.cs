using Bork2d.Render.Sprites;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// An in-memory <see cref="IGlobalUniformBuffer"/> for deterministic planner and pass tests.
/// </summary>
/// <remarks>
/// The buffer grows like the GPU implementation only when the required block count exceeds the
/// capacity and records every written block, so tests can assert the frame's one global block
/// without Vulkan.
/// </remarks>
internal sealed class FakeGlobalUniformBuffer : IGlobalUniformBuffer
{
    private GlobalUniformBlock[] _blocks;

    /// <summary>Initializes a new instance of the <see cref="FakeGlobalUniformBuffer"/> class.</summary>
    /// <param name="initialCapacity">The initial block capacity.</param>
    /// <param name="blockStride">The reported block stride; defaults to the block size.</param>
    internal FakeGlobalUniformBuffer(
        int initialCapacity = 4,
        int blockStride = GlobalUniformBlock.SizeInBytes)
    {
        _blocks = new GlobalUniformBlock[initialCapacity];
        BlockStride = blockStride;
    }

    /// <inheritdoc/>
    public int Capacity => _blocks.Length;

    /// <inheritdoc/>
    public int BlockStride { get; }

    /// <summary>Gets the number of times <see cref="EnsureCapacity"/> was called.</summary>
    internal int EnsureCapacityCallCount { get; private set; }

    /// <summary>Gets the last required block count passed to <see cref="EnsureCapacity"/>.</summary>
    internal int LastRequiredBlocks { get; private set; }

    /// <summary>Gets the number of block writes performed.</summary>
    internal int WriteCount { get; private set; }

    /// <summary>Gets the last block index written.</summary>
    internal int LastWrittenIndex { get; private set; } = -1;

    /// <inheritdoc/>
    public void EnsureCapacity(int requiredBlocks)
    {
        EnsureCapacityCallCount++;
        LastRequiredBlocks = requiredBlocks;
        if (requiredBlocks <= _blocks.Length)
        {
            return;
        }

        int capacity = Math.Max(requiredBlocks, _blocks.Length * 2);
        Array.Resize(ref _blocks, capacity);
    }

    /// <inheritdoc/>
    public void WriteBlock(int index, in GlobalUniformBlock block)
    {
        if ((uint)index >= (uint)_blocks.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The fake global uniform buffer holds {_blocks.Length} blocks.");
        }

        _blocks[index] = block;
        LastWrittenIndex = index;
        WriteCount++;
    }

    /// <summary>Gets one written block.</summary>
    /// <param name="index">The block index.</param>
    /// <returns>The block.</returns>
    internal GlobalUniformBlock Get(int index) => _blocks[index];
}
