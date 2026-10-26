namespace Bork2d.Assets;

/// <summary>
/// One cached <see cref="Bork2d.Api.Sprite"/> handle and its internal completion signal.
/// </summary>
/// <remarks>
/// The completion signal is internal only: M3 has no public readiness API. A load completes the
/// signal with <see langword="true"/> on success and <see langword="false"/> on failure, so a failed
/// sprite never creates an unobserved faulted task. Handles are session-local and cached per
/// canonical sprite path; a miss is not cached.
/// </remarks>
internal sealed class SpriteHandleEntry
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteHandleEntry"/> class.
    /// </summary>
    /// <param name="handle">The allocated handle.</param>
    /// <param name="path">The canonical sprite path.</param>
    internal SpriteHandleEntry(Bork2d.Api.Sprite handle, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Handle = handle;
        Path = path;
    }

    /// <summary>Gets the opaque handle returned to the caller.</summary>
    internal Bork2d.Api.Sprite Handle { get; }

    /// <summary>Gets the canonical sprite path this handle was allocated for.</summary>
    internal string Path { get; }

    /// <summary>
    /// Gets the internal completion task. It completes with <see langword="true"/> when the CPU
    /// sprite resource is published and <see langword="false"/> when the load failed.
    /// </summary>
    internal Task Completion => _completion.Task;

    /// <summary>Completes the load as successful.</summary>
    internal void Complete() => _completion.TrySetResult(true);

    /// <summary>Completes the load as failed without faulting the task.</summary>
    internal void Fail() => _completion.TrySetResult(false);
}
