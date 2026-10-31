using Bork2d.Render.Sprites;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// An in-memory <see cref="IInstanceBuffer"/> for deterministic planner tests (M8 §6.6).
/// </summary>
/// <remarks>
/// The buffer grows like the GPU implementation (only when the required count exceeds the capacity)
/// and records every written record, so tests can assert ranges, order, and fields without Vulkan.
/// </remarks>
internal sealed class FakeInstanceBuffer : IInstanceBuffer
{
    private SpriteInstance[] _instances;

    /// <summary>Initializes a new instance of the <see cref="FakeInstanceBuffer"/> class.</summary>
    /// <param name="initialCapacity">The initial record capacity.</param>
    internal FakeInstanceBuffer(int initialCapacity = 16)
        => _instances = new SpriteInstance[initialCapacity];

    /// <summary>Gets the number of times <see cref="EnsureCapacity"/> had to grow the buffer.</summary>
    internal int GrowthCount { get; private set; }

    /// <summary>Gets the last required instance count passed to <see cref="EnsureCapacity"/>.</summary>
    internal int LastRequiredInstances { get; private set; }

    /// <summary>Gets the number of writes performed.</summary>
    internal int WriteCount { get; private set; }

    /// <inheritdoc/>
    public int InstanceCapacity => _instances.Length;

    /// <inheritdoc/>
    public void EnsureCapacity(int requiredInstances)
    {
        LastRequiredInstances = requiredInstances;
        if (requiredInstances <= _instances.Length)
        {
            return;
        }

        GrowthCount++;
        int capacity = Math.Max(requiredInstances, _instances.Length * 2);
        Array.Resize(ref _instances, capacity);
    }

    /// <inheritdoc/>
    public void WriteInstance(int index, in SpriteInstance instance)
    {
        if ((uint)index >= (uint)_instances.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"The fake instance buffer holds {_instances.Length} records.");
        }

        _instances[index] = instance;
        WriteCount++;
    }

    /// <summary>Gets one written record.</summary>
    /// <param name="index">The record index.</param>
    /// <returns>The record.</returns>
    internal SpriteInstance Get(int index) => _instances[index];
}
