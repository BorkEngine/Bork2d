namespace Bork2d.Assets;

/// <summary>
/// One queued load request. Requests are processed strictly in enqueue order by the single loader
/// thread.
/// </summary>
/// <remarks>
/// The public load methods resolve the winning pack entry at enqueue time and then hand the request
/// to <see cref="AssetService"/>. A request is completed exactly once, either with its result, with
/// a classified <see cref="Bork2d.Api.AssetException"/>, or as canceled at shutdown. The loader
/// thread logs the outcome before it completes the task, so a caller that observes completion also
/// observes the log event. Task continuations always run asynchronously, never inline on the
/// loader thread.
/// </remarks>
internal abstract class AssetRequest
{
    /// <summary>Gets the canonical asset path the request was resolved for.</summary>
    internal abstract string CanonicalPath { get; }

    /// <summary>Gets the resolved filesystem path of the pack that supplied the entry.</summary>
    internal abstract string PackPath { get; }

    /// <summary>Gets the asset kind reported in the load-success log event.</summary>
    internal abstract string Kind { get; }

    /// <summary>Gets the task the caller observes.</summary>
    internal abstract Task Completion { get; }

    /// <summary>
    /// Cancels the request's task. Returns <see langword="false"/> when the task had already
    /// completed.
    /// </summary>
    /// <param name="cancellationToken">The token stored on the canceled task.</param>
    /// <returns><see langword="true"/> when this call canceled the task.</returns>
    internal abstract bool Cancel(CancellationToken cancellationToken);

    /// <summary>
    /// Executes the load on the loader thread and stores the result for <see cref="Complete"/>.
    /// </summary>
    /// <remarks>The implementation must not perform engine/GPU work.</remarks>
    internal abstract void Execute();

    /// <summary>Completes the caller's task with the value produced by <see cref="Execute"/>.</summary>
    internal abstract void Complete();

    /// <summary>Completes the caller's task with a classified failure.</summary>
    /// <param name="failure">The failure produced by <see cref="RecordFault"/>.</param>
    internal abstract void CompleteFault(Exception failure);

    /// <summary>Classifies and records a load failure for the failure log event.</summary>
    /// <param name="exception">The failure thrown by the archive, codec, or serializer.</param>
    /// <returns>The exception to log and complete the task with.</returns>
    internal Exception RecordFault(Exception exception)
        => exception is OutOfMemoryException
            ? exception
            : AssetFailures.Map(CanonicalPath, PackPath, exception);
}
