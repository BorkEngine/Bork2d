using Bork2d.Runtime;
using Xunit;

namespace Bork2d.Tests;

/// <summary>
/// The window command queue coalesces per property, drops same-value requests, and keeps the
/// order between different properties.
/// </summary>
public sealed class WindowCommandQueueTests
{
    [Fact]
    public void LatestValueWinsWhileDifferentPropertiesKeepTheirOrder()
    {
        var queue = new WindowCommandQueue("initial", 1280, 720, initialFullscreen: false, initialCursorVisible: true);

        Assert.True(queue.TrySetFullscreen(true));
        Assert.True(queue.TrySetTitle("first"));
        Assert.True(queue.TrySetTitle("second"));
        Assert.True(queue.TrySetSize(800f, 600f));
        Assert.True(queue.TrySetTitle("third"));

        var drained = queue.Drain();

        Assert.Equal(
            [WindowCommandKind.Fullscreen, WindowCommandKind.Title, WindowCommandKind.Size],
            drained.Select(command => command.Kind));
        Assert.Equal("third", drained[1].Title);
        Assert.Equal(800f, drained[2].Width);
        Assert.Equal(600f, drained[2].Height);
        Assert.True(drained[0].Flag);
        Assert.Empty(queue.Drain());
    }

    [Fact]
    public void RequestsRepeatingTheLastRequestedValueAreDropped()
    {
        var queue = new WindowCommandQueue("initial", 1280, 720, initialFullscreen: false, initialCursorVisible: true);

        Assert.False(queue.TrySetTitle("initial"));
        Assert.False(queue.TrySetSize(1280f, 720f));
        Assert.False(queue.TrySetFullscreen(false));
        Assert.False(queue.TrySetCursorVisible(true));
        Assert.Empty(queue.Drain());

        Assert.True(queue.TrySetTitle("changed"));
        Assert.False(queue.TrySetTitle("changed"));
        Assert.Single(queue.Drain());
    }

    [Fact]
    public void EnqueueFromManyThreadsCoalescesToOneCommandPerProperty()
    {
        var queue = new WindowCommandQueue("initial", 1280, 720, initialFullscreen: false, initialCursorVisible: true);
        var titles = new[] { "thread-1", "thread-2", "thread-3", "thread-4" };
        using var barrier = new Barrier(titles.Length);
        var threads = new Thread[titles.Length];

        for (var index = 0; index < titles.Length; index++)
        {
            var title = titles[index];
            threads[index] = new Thread(() =>
            {
                barrier.SignalAndWait();
                queue.TrySetTitle(title);
            });
            threads[index].Start();
        }

        foreach (var thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "A queue producer did not finish.");
        }

        var drained = queue.Drain();

        var command = Assert.Single(drained);
        Assert.Equal(WindowCommandKind.Title, command.Kind);
        Assert.Contains(command.Title, titles);
    }

    [Fact]
    public void DrainAllowsTheSamePropertyToBeQueuedAgain()
    {
        var queue = new WindowCommandQueue("initial", 1280, 720, initialFullscreen: false, initialCursorVisible: true);

        Assert.True(queue.TrySetTitle("first"));
        Assert.Equal("first", Assert.Single(queue.Drain()).Title);

        Assert.True(queue.TrySetTitle("second"));
        Assert.Equal("second", Assert.Single(queue.Drain()).Title);
    }
}
