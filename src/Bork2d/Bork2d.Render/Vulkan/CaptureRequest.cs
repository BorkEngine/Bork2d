using Bork2d.Engine.Rendering;

namespace Bork2d.Render.Vulkan;

/// <summary>
/// One pending public <c>IEngine.CaptureFrameAsync</c> request, completed from the render thread
/// when the frame that recorded its copy has been observed by its fence (M8 §6.9/§6.12).
/// </summary>
/// <remarks>
/// The request owns a <see cref="TaskCompletionSource{TResult}"/> created with
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/> so completing it from the
/// render thread never runs caller continuations inline and never blocks the frame. A cancellation
/// registration completes the task with <see cref="OperationCanceledException"/>; the queue drops
/// already-completed requests without recording a copy for them. Every request served by one frame
/// receives the same shared immutable <see cref="FrameCaptureResult"/>.
/// </remarks>
internal sealed class CaptureRequest
{
    private readonly TaskCompletionSource<FrameCaptureResult> _source = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken _token;
    private readonly CancellationTokenRegistration _registration;

    /// <summary>Initializes a new instance of the <see cref="CaptureRequest"/> class.</summary>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    internal CaptureRequest(CancellationToken cancellationToken)
    {
        _token = cancellationToken;
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
            _registration.Dispose();
        }
    }

    /// <summary>Faults the request because the renderer stopped or failed.</summary>
    /// <param name="exception">The failure.</param>
    internal void Fault(Exception exception)
    {
        if (_source.TrySetException(exception))
        {
            _registration.Dispose();
        }
    }

    private void Cancel()
    {
        if (_source.TrySetCanceled(_token))
        {
            _registration.Dispose();
        }
    }
}
