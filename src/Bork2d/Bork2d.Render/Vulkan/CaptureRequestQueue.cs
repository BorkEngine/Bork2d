using Bork2d.Engine;
using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// The thread-safe, bounded queue of pending <c>IEngine.CaptureFrameAsync</c> requests
/// (M8 §6.9/§6.13).
/// </summary>
/// <remarks>
/// <para>
/// Requests may arrive on any thread. The render thread checks <see cref="HasPending"/> before
/// recording a frame, takes the pending requests into the frame's capture state, records one copy
/// after the last camera pass, and completes every request from that frame's fence. Requests that
/// arrive while a frame is being recorded stay queued for the next completed frame. The lock is
/// never held across a Vulkan call, fence wait, or user continuation.
/// </para>
/// <para>
/// At most <see cref="MaxOutstandingRequests"/> requests may be outstanding at once: queued
/// requests plus requests already taken into an in-flight frame that have not completed, faulted, or
/// been cancelled. Taking a frame's requests does not free capacity; a slot returns only when its
/// request finishes. A request beyond the limit is rejected with a faulted task whose exception is a
/// <see cref="Bork2dException"/> explaining the limit; the call never blocks and never silently
/// drops the request. Cancellation is checked before the limit, so an already-cancelled token faults
/// the task with <see cref="OperationCanceledException"/> even when the queue is full or closed.
/// </para>
/// <para>
/// A stop or fatal calls <see cref="Close"/> once, which faults every queued request and rejects
/// later ones with a faulted task. Staging memory lives in the per-frame <see cref="FrameCapture"/>
/// and is pooled for the run.
/// </para>
/// </remarks>
internal sealed class CaptureRequestQueue
{
    /// <summary>The maximum number of outstanding requests accepted at once.</summary>
    internal const int MaxOutstandingRequests = 8;

    private readonly Lock _gate = new();
    private readonly List<CaptureRequest> _pending = [];
    private int _outstanding;
    private int _closed;

    /// <summary>Gets a value indicating whether at least one request is waiting for a frame.</summary>
    internal bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count > 0;
            }
        }
    }

    /// <summary>Gets a value indicating whether the queue is closed (stop/fatal).</summary>
    internal bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>
    /// Gets the total number of outstanding requests, including requests already taken by a frame
    /// (diagnostics and tests).
    /// </summary>
    internal int OutstandingCount => Volatile.Read(ref _outstanding);

    /// <summary>Enqueues one capture request.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>
    /// A task completed by the next frame that records a copy and serves the request; faulted when
    /// the outstanding-request limit is reached or the queue is closed; cancelled when the token is
    /// already cancelled.
    /// </returns>
    internal Task<FrameCaptureResult> Request(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<FrameCaptureResult>(cancellationToken);
        }

        lock (_gate)
        {
            if (_closed != 0)
            {
                return Task.FromException<FrameCaptureResult>(
                    new ObjectDisposedException(
                        nameof(CaptureRequestQueue),
                        "The renderer stopped before the capture request could be served."));
            }

            if (Volatile.Read(ref _outstanding) >= MaxOutstandingRequests)
            {
                return Task.FromException<FrameCaptureResult>(
                    new Bork2dException(
                        $"At most {MaxOutstandingRequests} frame capture requests may be outstanding "
                            + "at once, including requests already taken by an in-flight frame; the "
                            + "request was rejected instead of blocking the caller or being dropped "
                            + "silently."));
            }

            // Take the slot under the same lock as the limit check, then register cancellation
            // outside the lock so a cancellation callback that calls back into the engine cannot
            // deadlock against this queue. A token cancelled during registration releases the slot
            // through the request's completion callback.
            Interlocked.Increment(ref _outstanding);
        }

        CaptureRequest request;
        try
        {
            request = new CaptureRequest(cancellationToken, ReleaseOutstanding);
        }
        catch
        {
            ReleaseOutstanding();
            throw;
        }

        Exception? rejection = null;
        lock (_gate)
        {
            if (_closed != 0)
            {
                rejection = new ObjectDisposedException(
                    nameof(CaptureRequestQueue),
                    "The renderer stopped before the capture request could be served.");
            }
            else if (!request.IsCompleted)
            {
                _pending.Add(request);
                return request.Task;
            }
        }

        if (rejection is not null)
        {
            request.Fault(rejection);
        }

        return request.Task;
    }

    /// <summary>
    /// Moves every not-yet-completed pending request into the frame's capture state.
    /// </summary>
    /// <param name="destination">
    /// The frame's request list; the caller clears and reuses it, so taking pending requests
    /// allocates nothing after warm-up.
    /// </param>
    /// <returns>The number of requests moved.</returns>
    internal int TakePending(List<CaptureRequest> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_gate)
        {
            var count = 0;
            for (var index = 0; index < _pending.Count; index++)
            {
                CaptureRequest request = _pending[index];
                if (request.IsCompleted)
                {
                    continue;
                }

                destination.Add(request);
                count++;
            }

            _pending.Clear();
            return count;
        }
    }

    /// <summary>Closes the queue and faults every pending request; idempotent.</summary>
    /// <param name="exception">The stop/fatal failure every pending request observes.</param>
    internal void Close(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        CaptureRequest[] drained;
        lock (_gate)
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
            {
                return;
            }

            drained = [.. _pending];
            _pending.Clear();
        }

        foreach (CaptureRequest request in drained)
        {
            request.Fault(exception);
        }
    }

    /// <summary>Releases one outstanding slot when a request finishes; called exactly once.</summary>
    private void ReleaseOutstanding() => Interlocked.Decrement(ref _outstanding);
}
