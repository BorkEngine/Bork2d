using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Render;
using Xunit;

namespace Bork2d.Tests.Render;

/// <summary>
/// Deterministic tests for the SPSC control/stop/fatal channel (D-M6-19).
/// </summary>
/// <remarks>
/// Every wait is event-based and bounded; no test sleeps, polls, or depends on wall-clock pacing.
/// </remarks>
public sealed class RendererControlChannelTests
{
    [Fact]
    public void LatestExtentWinsAndCoalescesIntoOneMessage()
    {
        using var channel = new RendererControlChannel();

        channel.PostExtent(new WindowExtent(800, 600));
        channel.PostExtent(new WindowExtent(1600, 900));

        Assert.True(channel.TryDequeue(out RendererControlMessage message));
        Assert.Equal(RendererControlMessage.Kind.Extent, message.MessageKind);
        Assert.Equal(new WindowExtent(1600, 900), message.Extent);
        Assert.False(channel.TryDequeue(out _));
        Assert.False(channel.HasPendingWork);
    }

    [Fact]
    public void PropertiesKeepTheirOrderAcrossCoalescedUpdates()
    {
        using var channel = new RendererControlChannel();

        channel.PostExtent(new WindowExtent(800, 600));
        channel.PostFullscreen(true);
        channel.PostExtent(new WindowExtent(1024, 768));

        Assert.True(channel.TryDequeue(out RendererControlMessage first));
        Assert.Equal(RendererControlMessage.Kind.Fullscreen, first.MessageKind);
        Assert.True(first.Flag);

        Assert.True(channel.TryDequeue(out RendererControlMessage second));
        Assert.Equal(RendererControlMessage.Kind.Extent, second.MessageKind);
        Assert.Equal(new WindowExtent(1024, 768), second.Extent);

        Assert.False(channel.TryDequeue(out _));
    }

    [Fact]
    public void BooleanPropertiesAlsoKeepOnlyTheirLatestValue()
    {
        using var channel = new RendererControlChannel();

        channel.PostFullscreen(true);
        channel.PostFullscreen(false);
        channel.PostMinimized(true);
        channel.PostMinimized(false);

        Assert.True(channel.TryDequeue(out RendererControlMessage first));
        Assert.Equal(RendererControlMessage.Kind.Fullscreen, first.MessageKind);
        Assert.False(first.Flag);

        Assert.True(channel.TryDequeue(out RendererControlMessage second));
        Assert.Equal(RendererControlMessage.Kind.Minimized, second.MessageKind);
        Assert.False(second.Flag);

        Assert.False(channel.TryDequeue(out _));
    }

    [Fact]
    public void StopWakesABlockedConsumer()
    {
        using var channel = new RendererControlChannel();
        using var entered = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var work = true;
        var consumer = new Thread(() =>
        {
            entered.Set();
            work = channel.WaitForWork();
            finished.Set();
        })
        {
            IsBackground = true,
            Name = "Bork2d.ControlChannelTest",
        };

        consumer.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        channel.RequestStop();

        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(consumer.Join(TimeSpan.FromSeconds(5)));
        Assert.False(work);
        Assert.True(channel.IsStopRequested);
    }

    [Fact]
    public void PostWakesABlockedConsumerWithoutLosingTheWake()
    {
        using var channel = new RendererControlChannel();
        using var entered = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var work = false;
        var consumer = new Thread(() =>
        {
            entered.Set();
            work = channel.WaitForWork();
            finished.Set();
        })
        {
            IsBackground = true,
            Name = "Bork2d.ControlChannelTest",
        };

        consumer.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        channel.PostExtent(new WindowExtent(1280, 720));

        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(consumer.Join(TimeSpan.FromSeconds(5)));
        Assert.True(work);
        Assert.True(channel.TryDequeue(out RendererControlMessage message));
        Assert.Equal(new WindowExtent(1280, 720), message.Extent);
    }

    [Fact]
    public void BoundedWaitReturnsWhenWorkArrivesAndTimesOutOtherwise()
    {
        using var channel = new RendererControlChannel();

        var stopwatch = Stopwatch.StartNew();
        Assert.False(channel.WaitForWork(TimeSpan.Zero));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2));

        channel.PostMinimized(true);
        Assert.True(channel.WaitForWork(TimeSpan.FromSeconds(5)));
        Assert.True(channel.TryDequeue(out RendererControlMessage message));
        Assert.Equal(RendererControlMessage.Kind.Minimized, message.MessageKind);
        Assert.True(message.Flag);
    }

    [Fact]
    public void FatalErrorIsPublishedExactlyOnce()
    {
        using var channel = new RendererControlChannel();
        var first = new Bork2dException("first failure");
        var second = new Bork2dException("second failure");

        channel.PublishFatal(first);
        channel.PublishFatal(second);

        Assert.Same(first, channel.FatalException);
    }

    [Fact]
    public void ManyProducerPostsCollapseToTheLatestValue()
    {
        using var channel = new RendererControlChannel();

        for (var index = 1; index <= 2_000; index++)
        {
            channel.PostExtent(new WindowExtent(index, index + 1));
        }

        Assert.True(channel.TryDequeue(out RendererControlMessage message));
        Assert.Equal(new WindowExtent(2_000, 2_001), message.Extent);
        Assert.False(channel.TryDequeue(out _));
    }

    [Fact]
    public void DisposeWakesAWaitingConsumer()
    {
        var channel = new RendererControlChannel();
        using var entered = new ManualResetEventSlim();
        using var finished = new ManualResetEventSlim();
        var work = true;
        var consumer = new Thread(() =>
        {
            entered.Set();
            work = channel.WaitForWork();
            finished.Set();
        })
        {
            IsBackground = true,
            Name = "Bork2d.ControlChannelTest",
        };

        consumer.Start();
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        channel.Dispose();

        Assert.True(finished.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(consumer.Join(TimeSpan.FromSeconds(5)));
        Assert.False(work);
    }

    [Fact]
    public void DisposalIsIdempotentAndLaterRequestsDoNotThrow()
    {
        var channel = new RendererControlChannel();
        channel.Dispose();
        channel.Dispose();

        channel.PostExtent(new WindowExtent(10, 10));
        channel.RequestStop();
        channel.PublishFatal(new Bork2dException("published while disposed"));

        Assert.True(channel.IsStopRequested);
        Assert.NotNull(channel.FatalException);
    }
}
