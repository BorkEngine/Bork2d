using System.Numerics;
using Bork2d.Engine;

namespace Bork2d.Render.Sprites;

/// <summary>
/// The pure capacity/growth state of one GPU frame's instance buffer (M8 §6.6).
/// </summary>
/// <remarks>
/// <para>
/// Each frame starts at <see cref="InitialCapacity"/> 96-byte records. When a frame's planned
/// instance count exceeds the current capacity, the new capacity is
/// <c>roundUpToPowerOfTwo(max(required, capacity * 2))</c>. The frame's fence must already have
/// signaled: the renderer waits it before reusing the frame, so <see cref="MarkInFlight"/> /
/// <see cref="MarkFenceSignaled"/> model that invariant for deterministic tests and act as a
/// defensive guard in production.
/// </para>
/// <para>
/// A capacity whose byte size exceeds the device's <c>maxStorageBufferRange</c> is a fatal
/// <see cref="Bork2dException"/>; no instance is ever dropped or truncated.
/// </para>
/// </remarks>
internal struct InstanceBufferGrowth
{
    /// <summary>The approved initial per-frame capacity in 96-byte records.</summary>
    internal const int InitialCapacity = 1024;

    private readonly int _capacity;
    private bool _inFlight;

    /// <summary>Initializes a new instance of the <see cref="InstanceBufferGrowth"/> struct.</summary>
    /// <param name="initialCapacity">The initial capacity in records; must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialCapacity"/> is not positive.</exception>
    internal InstanceBufferGrowth(int initialCapacity)
    {
        if (initialCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialCapacity),
                initialCapacity,
                "The instance-buffer capacity must be positive.");
        }

        _capacity = initialCapacity;
        _inFlight = false;
    }

    /// <summary>Gets the current capacity in 96-byte records.</summary>
    internal readonly int Capacity => _capacity;

    /// <summary>Gets a value indicating whether a submitted frame still references the buffer.</summary>
    internal readonly bool InFlight => _inFlight;

    /// <summary>Gets a value indicating whether <paramref name="requiredInstances"/> needs growth.</summary>
    /// <param name="requiredInstances">The frame's planned instance count.</param>
    /// <returns><see langword="true"/> when the count exceeds the current capacity.</returns>
    internal readonly bool NeedsGrowth(int requiredInstances)
        => requiredInstances > _capacity;

    /// <summary>Records that the frame was submitted and the GPU may still reference the buffer.</summary>
    internal void MarkInFlight() => _inFlight = true;

    /// <summary>Records that the frame's fence signaled and the buffer may be replaced.</summary>
    internal void MarkFenceSignaled() => _inFlight = false;

    /// <summary>
    /// Plans the next capacity for a frame that needs more records than the buffer holds.
    /// </summary>
    /// <param name="requiredInstances">The frame's planned instance count.</param>
    /// <param name="maxStorageBufferRange">
    /// The selected device's <c>maxStorageBufferRange</c> in bytes.
    /// </param>
    /// <param name="newCapacity">The planned power-of-two capacity; unchanged on failure.</param>
    /// <param name="failure">
    /// A diagnostic when growth is required but impossible; empty when growth was planned or not
    /// needed.
    /// </param>
    /// <returns><see langword="true"/> when a larger valid capacity was planned.</returns>
    internal bool TryGrow(
        int requiredInstances,
        ulong maxStorageBufferRange,
        out int newCapacity,
        out string failure)
    {
        newCapacity = _capacity;
        failure = string.Empty;
        if (requiredInstances <= _capacity)
        {
            return false;
        }

        if (requiredInstances < 0)
        {
            failure = "the required instance count is negative";
            return false;
        }

        if (_inFlight)
        {
            failure =
                "the frame is still in flight; the fence must signal before the instance buffer "
                + "can grow";
            return false;
        }

        long doubled = (long)_capacity * 2L;
        long target = Math.Max(requiredInstances, doubled);
        if (target > int.MaxValue)
        {
            failure =
                $"the requested capacity {target} records exceeds the maximum representable "
                + "count";
            return false;
        }

        uint rounded = BitOperations.RoundUpToPowerOf2((uint)target);
        if (rounded == 0 || rounded > int.MaxValue)
        {
            failure =
                $"the requested capacity {target} records cannot be rounded up to a power of two";
            return false;
        }

        ulong bytes = (ulong)rounded * SpriteInstance.SizeInBytes;
        if (bytes > maxStorageBufferRange)
        {
            failure =
                $"the instance buffer needs {bytes} bytes for {rounded} records, which exceeds "
                + $"the selected device's maxStorageBufferRange of {maxStorageBufferRange}";
            return false;
        }

        newCapacity = (int)rounded;
        return true;
    }

    /// <summary>Creates the fatal exception for a growth that cannot satisfy the frame.</summary>
    /// <param name="requiredInstances">The frame's planned instance count.</param>
    /// <param name="failure">The policy failure description.</param>
    /// <returns>The fatal renderer exception.</returns>
    internal readonly Bork2dException CreateGrowthException(
        int requiredInstances,
        string failure)
        => new(
            $"The per-frame instance buffer cannot grow from {_capacity} to hold "
                + $"{requiredInstances} 96-byte records: {failure}. No instance is truncated.");

    /// <summary>Creates the fatal exception for a buffer used while its frame is in flight.</summary>
    /// <param name="requiredInstances">The frame's planned instance count.</param>
    /// <returns>The exception.</returns>
    internal readonly InvalidOperationException CreateInFlightException(int requiredInstances)
        => new(
            $"The per-frame instance buffer cannot grow from {_capacity} to hold "
                + $"{requiredInstances} records while the frame is in flight; the renderer must "
                + "observe the frame fence before growing.");
}
