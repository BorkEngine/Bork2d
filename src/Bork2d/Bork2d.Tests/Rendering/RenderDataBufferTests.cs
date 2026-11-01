using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Tests for the typed render-data pools: append order, high-water growth, used-range reset without
/// shrinking, typed slot access, and wrong-type slot rejection.
/// </summary>
public sealed class RenderDataBufferTests
{
    [Fact]
    public void NewBufferIsEmpty()
    {
        var buffer = new RenderDataBuffer<TestRecord>();

        Assert.Equal(0, buffer.Count);
        Assert.Equal(0, buffer.Capacity);
        Assert.True(buffer.Items.IsEmpty);
    }

    [Fact]
    public void AddAppendsItemsInOrderAndGrowsToTheHighWaterMark()
    {
        var buffer = new RenderDataBuffer<TestRecord>();

        for (var index = 0; index < 10; index++)
        {
            buffer.Add(new TestRecord(index));
        }

        Assert.Equal(10, buffer.Count);
        Assert.True(buffer.Capacity >= 10);
        Assert.Equal(10, buffer.Items.Length);
        for (var index = 0; index < 10; index++)
        {
            Assert.Equal(index, buffer.Items[index].Value);
        }
    }

    [Fact]
    public void ResetClearsTheUsedRangeAndKeepsTheCapacity()
    {
        var buffer = new RenderDataBuffer<TestRecord>();
        for (var index = 0; index < 8; index++)
        {
            buffer.Add(new TestRecord(index));
        }

        var capacity = buffer.Capacity;

        buffer.Reset();

        Assert.Equal(0, buffer.Count);
        Assert.True(buffer.Items.IsEmpty);
        Assert.Equal(capacity, buffer.Capacity);

        buffer.Add(new TestRecord(99));
        Assert.Equal(1, buffer.Count);
        Assert.Equal(99, buffer.Items[0].Value);
        Assert.Equal(capacity, buffer.Capacity);
    }

    [Fact]
    public void SetReturnsTheSameTypedBufferForARepeatedSlot()
    {
        var set = new RenderDataBufferSet();
        RenderDataBuffer<TestRecord> first = set.Get<TestRecord>(0);
        first.Add(new TestRecord(1));

        RenderDataBuffer<TestRecord> second = set.Get<TestRecord>(0);

        Assert.Same(first, second);
        Assert.Equal(1, second.Count);
        Assert.Equal(1, second.Items[0].Value);
    }

    [Fact]
    public void WrongTypeSlotAccessIsRejected()
    {
        var set = new RenderDataBufferSet();
        set.Get<TestRecord>(0).Add(new TestRecord(1));

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => set.Get<TestOtherRecord>(0));

        Assert.Contains("slot 0", exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestRecord).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(TestOtherRecord).FullName!, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NegativeSlotIsRejected()
    {
        var set = new RenderDataBufferSet();

        Assert.Throws<ArgumentOutOfRangeException>(() => set.Get<TestRecord>(-1));
    }

    [Fact]
    public void ResetClearsEveryCreatedSlot()
    {
        var set = new RenderDataBufferSet();
        RenderDataBuffer<TestRecord> first = set.Get<TestRecord>(0);
        first.Add(new TestRecord(1));
        RenderDataBuffer<TestOtherRecord> second = set.Get<TestOtherRecord>(1);
        second.Add(new TestOtherRecord(2));
        var capacity = second.Capacity;

        set.Reset();

        Assert.Equal(0, first.Count);
        Assert.Equal(0, second.Count);
        Assert.Equal(capacity, second.Capacity);
    }

    [Fact]
    public void SlotsMustBeCreatedContiguously()
    {
        var set = new RenderDataBufferSet();
        set.Get<TestRecord>(0);

        Bork2dException exception = Assert.Throws<Bork2dException>(
            () => set.Get<TestOtherRecord>(2));

        Assert.Contains("contiguous", exception.Message, StringComparison.Ordinal);
    }

    private readonly record struct TestRecord(int Value);

    private readonly record struct TestOtherRecord(int Value);
}
