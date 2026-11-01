using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One pending public <c>IEngine.CaptureFrameAsync</c> request, completed from the render thread
/// when the frame that recorded its copy has been observed by its fence (M8 §6.9/§6.12).
/// </summary>
/// <remarks>
/// <para>
/// The request owns a <see cref="TaskCompletionSource{TResult}"/> created with
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> so completing it from the
/// render thread never runs caller continuations inline and never blocks the frame. A cancellation
/// registration completes the task with <see cref="OperationCanceledException"/>; the queue drops
/// already-completed requests without recording a copy for them. Every request served by one frame
/// receives the same shared immutable <see cref="FrameCaptureResult"/>.
/// </para>
/// <para>
/// The request invokes its completion callback exactly once, on whichever of complete/fault/cancel
/// wins the task's state transition, so the queue's outstanding counter is released exactly once
/// even when completion races cancellation. The callback runs outside every queue lock.
/// </para>
/// </remarks>
internal sealed class CaptureRequest
{
    private readonly TaskCompletionSource<FrameCaptureResult> _source = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;
    private readonly Action _onCompleted;

    /// <summary>Initializes a new instance of the <see cref="CaptureRequest"/> class.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <param name="onCompleted">
    /// Invoked exactly once when the request completes, faults, or is cancelled so the queue can
    /// release its outstanding slot. Must be fast and must not take the queue lock.
    /// </param>
    internal CaptureRequest(CancellationToken cancellationToken, Action onCompleted)
    {
        ArgumentNullException.ThrowIfNull(onCompleted);
        _token = cancellationToken;
        _onCompleted = onCompleted;
        Task = _source.Task;
        if (cancellationToken.CanBeCanceled)
        {
            _registration = cancellationToken.Register(
                static state => ((CaptureRequest)state!).Cancel(),
                this);
        }
    }

    /// <summary>Gets the task the caller observes.</summary>
    internal Task<FrameCaptureResult> Task { get; }

    /// <summary>Gets a value indicating whether the task already completed (cancelled or faulted).</summary>
    internal bool IsCompleted => _source.Task.IsCompleted;

    /// <summary>Completes the request with the serving frame's shared capture result.</summary>
    /// <param name="result">The frame's pixels, extent, and format.</param>
    internal void Complete(FrameCaptureResult result)
    {
        if (_source.TrySetResult(result))
        {
            CompleteOnce();
        }
    }

    /// <summary>Faults the request because the renderer stopped or failed.</summary>
    /// <param name="exception">The failure.</param>
    internal void Fault(Exception exception)
    {
        if (_source.TrySetException(exception))
        {
            CompleteOnce();
        }
    }

    private void Cancel()
    {
        if (_source.TrySetCanceled(_token))
        {
            CompleteOnce();
        }
    }

    private void CompleteOnce()
    {
        _registration.Dispose();
        _onCompleted();
    }
}
