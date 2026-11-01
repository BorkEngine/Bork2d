using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Render.Vulkan;
using Xunit;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Deterministic tests for the bounded frame-capture request queue (M8 §6.9/§6.13): exactly eight
/// outstanding requests are accepted, the ninth faults with <see cref="Bork2dException"/>, taking a
/// frame's requests frees capacity, cancellation is checked before the limit and a closed queue, and
/// closing faults every pending request and rejects later ones.
/// </summary>
public sealed class CaptureRequestQueueTests
{
    [Fact]
    public async Task EightOutstandingRequestsAreAcceptedAndTheNinthFaults()
    {
        var queue = new CaptureRequestQueue();
        Task<FrameCaptureResult>[] accepted = new Task<FrameCaptureResult>[
            CaptureRequestQueue.MaxOutstandingRequests];
        for (var index = 0; index < accepted.Length; index++)
        {
            accepted[index] = queue.Request(default);
        }

        Assert.All(accepted, task => Assert.False(task.IsCompleted));

        Task<FrameCaptureResult> rejected = queue.Request(default);
        Bork2dException exception = await Assert.ThrowsAsync<Bork2dException>(() => rejected);
        Assert.Contains(
            $"{CaptureRequestQueue.MaxOutstandingRequests}",
            exception.Message,
            StringComparison.Ordinal);

        // The rejection neither consumes a slot nor completes an accepted request.
        Assert.All(accepted, task => Assert.False(task.IsCompleted));
    }

    [Fact]
    public void TakingPendingRequestsFreesTheOutstandingCapacity()
    {
        var queue = new CaptureRequestQueue();
        for (var index = 0; index < CaptureRequestQueue.MaxOutstandingRequests; index++)
        {
            _ = queue.Request(default);
        }

        var frame = new List<CaptureRequest>();
        Assert.Equal(CaptureRequestQueue.MaxOutstandingRequests, queue.TakePending(frame));
        Assert.Equal(CaptureRequestQueue.MaxOutstandingRequests, frame.Count);

        Task<FrameCaptureResult> accepted = queue.Request(default);
        Assert.False(accepted.IsCompleted);
    }

    [Fact]
    public async Task CancellationIsCheckedBeforeTheOutstandingLimit()
    {
        var queue = new CaptureRequestQueue();
        for (var index = 0; index < CaptureRequestQueue.MaxOutstandingRequests; index++)
        {
            _ = queue.Request(default);
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task<FrameCaptureResult> rejected = queue.Request(cancellation.Token);

        Assert.True(rejected.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rejected);
    }

    [Fact]
    public async Task CancelledRequestsDoNotConsumeOutstandingSlots()
    {
        var queue = new CaptureRequestQueue();
        using var cancellation = new CancellationTokenSource();
        Task<FrameCaptureResult>[] cancelled = new Task<FrameCaptureResult>[
            CaptureRequestQueue.MaxOutstandingRequests];
        for (var index = 0; index < cancelled.Length; index++)
        {
            cancelled[index] = queue.Request(cancellation.Token);
        }

        cancellation.Cancel();

        Assert.All(cancelled, task => Assert.True(task.IsCanceled));
        Task<FrameCaptureResult> accepted = queue.Request(default);
        Assert.False(accepted.IsCompleted);
    }

    [Fact]
    public async Task CloseFaultsPendingRequestsAndRejectsLaterOnes()
    {
        var queue = new CaptureRequestQueue();
        Task<FrameCaptureResult> first = queue.Request(default);
        Task<FrameCaptureResult> second = queue.Request(default);
        var stop = new ObjectDisposedException("test", "The renderer stopped.");

        queue.Close(stop);

        Assert.True(queue.IsClosed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => first);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => second);

        Task<FrameCaptureResult> afterClose = queue.Request(default);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => afterClose);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<FrameCaptureResult> cancelled = queue.Request(cancellation.Token);
        Assert.True(cancelled.IsCanceled);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
    }
}
