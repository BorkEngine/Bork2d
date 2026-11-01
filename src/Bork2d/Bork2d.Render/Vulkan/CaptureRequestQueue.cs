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
/// At most <see cref="MaxOutstandingRequests"/> requests may be outstanding (queued and not yet
/// taken by a frame). A request beyond the limit is rejected with a faulted task whose exception is
/// a <see cref="Bork2dException"/> explaining the limit; the call never blocks and never silently
/// drops the request. Cancellation is checked before the limit, so an already-cancelled token
/// faults the task with <see cref="OperationCanceledException"/> even when the queue is full or
/// closed.
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

        var request = new CaptureRequest(cancellationToken);
        Exception rejection;
        lock (_gate)
        {
            if (_closed != 0)
            {
                rejection = new ObjectDisposedException(
                    nameof(CaptureRequestQueue),
                    "The renderer stopped before the capture request could be served.");
            }
            else if (CountOutstanding() >= MaxOutstandingRequests)
            {
                rejection = new Bork2dException(
                    $"At most {MaxOutstandingRequests} frame capture requests may be outstanding at "
                        + "once; the request was rejected instead of blocking the caller or being "
                        + "dropped silently.");
            }
            else
            {
                _pending.Add(request);
                return request.Task;
            }
        }

        request.Fault(rejection);
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

    /// <summary>
    /// Counts the requests that can still be served, dropping cancelled ones from the pending list.
    /// </summary>
    /// <remarks>
    /// A cancelled request is already completed and will never be recorded; it must not consume one
    /// of the <see cref="MaxOutstandingRequests"/> slots while it waits for the render thread to take
    /// it. The caller holds <see cref="_gate"/>.
    /// </remarks>
    /// <returns>The number of outstanding requests.</returns>
    private int CountOutstanding()
    {
        var outstanding = 0;
        for (var index = 0; index < _pending.Count; index++)
        {
            if (!_pending[index].IsCompleted)
            {
                _pending[outstanding] = _pending[index];
                outstanding++;
            }
        }

        if (outstanding < _pending.Count)
        {
            _pending.RemoveRange(outstanding, _pending.Count - outstanding);
        }

        return outstanding;
    }
}
