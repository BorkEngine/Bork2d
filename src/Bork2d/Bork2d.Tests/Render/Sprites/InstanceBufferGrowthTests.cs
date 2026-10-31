using Bork2d.Engine;
using Bork2d.Render.Sprites;
using Xunit;

namespace Bork2d.Tests.Render.Sprites;

/// <summary>
/// Deterministic tests for the per-frame instance-buffer growth policy (M8 §6.6): initial capacity,
/// doubling/round-up, the power-of-two rule, the explicit device-limit failure, and the free-fence
/// invariant.
/// </summary>
public sealed class InstanceBufferGrowthTests
{
    private const ulong OneGigabyte = 1UL << 30;

    [Fact]
    public void InitialCapacityIsTheApprovedOneThousandTwentyFourRecords()
    {
        var growth = new InstanceBufferGrowth(InstanceBufferGrowth.InitialCapacity);

        Assert.Equal(1024, growth.Capacity);
        Assert.Equal(1024, InstanceBufferGrowth.InitialCapacity);
        Assert.False(growth.InFlight);
    }

    [Fact]
    public void ACapacityThatAlreadyFitsDoesNotGrow()
    {
        var growth = new InstanceBufferGrowth(1024);

        Assert.False(growth.NeedsGrowth(1024));
        Assert.False(growth.TryGrow(1024, OneGigabyte, out int capacity, out string failure));
        Assert.Equal(1024, capacity);
        Assert.Empty(failure);
    }

    [Theory]
    [InlineData(1025, 2048)]
    [InlineData(2048, 2048)]
    [InlineData(2049, 4096)]
    [InlineData(3000, 4096)]
    [InlineData(5000, 8192)]
    [InlineData(50_000, 65_536)]
    public void GrowthUsesMaxOfRequiredAndDoubleRoundedUpToAPowerOfTwo(
        int required,
        int expected)
    {
        var growth = new InstanceBufferGrowth(1024);

        Assert.True(growth.NeedsGrowth(required));
        Assert.True(growth.TryGrow(required, OneGigabyte, out int capacity, out string failure));
        Assert.Empty(failure);
        Assert.Equal(expected, capacity);
        Assert.True((capacity & (capacity - 1)) == 0, "The new capacity must be a power of two.");
    }

    [Fact]
    public void ExceedingMaxStorageBufferRangeIsAnExplicitFailure()
    {
        var growth = new InstanceBufferGrowth(1024);
        ulong limit = ((ulong)2048 * SpriteInstance.SizeInBytes) - 1;

        Assert.False(growth.TryGrow(1025, limit, out int capacity, out string failure));
        Assert.Equal(1024, capacity);
        Assert.Contains("maxStorageBufferRange", failure, StringComparison.Ordinal);

        Bork2dException exception = growth.CreateGrowthException(1025, failure);
        Assert.Contains("maxStorageBufferRange", exception.Message, StringComparison.Ordinal);
        Assert.Contains("No instance is truncated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GrowthIsRefusedWhileTheFrameIsInFlight()
    {
        var growth = new InstanceBufferGrowth(1024);
        growth.MarkInFlight();

        Assert.True(growth.InFlight);
        Assert.False(growth.TryGrow(5000, OneGigabyte, out int capacity, out string failure));
        Assert.Equal(1024, capacity);
        Assert.Contains("in flight", failure, StringComparison.Ordinal);
        Assert.NotNull(growth.CreateInFlightException(5000));

        growth.MarkFenceSignaled();
        Assert.False(growth.InFlight);
        Assert.True(growth.TryGrow(5000, OneGigabyte, out capacity, out failure));
        Assert.Equal(8192, capacity);
    }
}
