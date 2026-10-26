namespace Bork2d.Assets.Loading;

/// <summary>
/// A queued sprite + texture-descriptor load that publishes CPU resources and completes an internal
/// <see cref="SpriteHandleEntry"/> signal.
/// </summary>
/// <remarks>
/// <para>
/// The public <see cref="Bork2d.Api.Assets.IAssets.LoadSprite(string)"/> has no task: it returns the handle
/// immediately. This request carries the enqueue-time VFS snapshot, so the sprite entry, descriptor
/// entry, and payload entry all resolve against the same snapshot even if an overlay is mounted
/// while the request is queued.
/// </para>
/// <para>
/// A failed load is completed with a <see langword="false"/> result rather than a faulted task, so
/// no unobserved exception is produced. Shutdown cancels a request that has not started; a load that
/// observes shutdown before publishing is dropped through <see cref="SpriteHandleEntry.Drop"/> and
/// stays non-renderable.
/// </para>
/// </remarks>
internal sealed class SpriteLoadRequest : AssetRequest
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly SpriteHandleEntry _handle;
    private readonly Action _load;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteLoadRequest"/> class.
    /// </summary>
    /// <param name="canonicalPath">The canonical sprite path the request was resolved for.</param>
    /// <param name="packPath">The resolved filesystem path of the pack that supplied the entry.</param>
    /// <param name="handle">The handle allocated for the request.</param>
    /// <param name="load">The loader-thread body; it must not perform engine/GPU work.</param>
    internal SpriteLoadRequest(
        string canonicalPath,
        string packPath,
        SpriteHandleEntry handle,
        Action load)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(load);

        CanonicalPath = canonicalPath;
        PackPath = packPath;
        _handle = handle;
        _load = load;
    }

    /// <inheritdoc/>
    internal override string CanonicalPath { get; }

    /// <inheritdoc/>
    internal override string PackPath { get; }

    /// <inheritdoc/>
    internal override string Kind => "sprite";

    /// <inheritdoc/>
    internal override bool IsDropped => _handle.IsDropped;

    /// <inheritdoc/>
    internal override Task Completion => _completion.Task;

    /// <inheritdoc/>
    internal override bool Cancel(CancellationToken cancellationToken)
        => _completion.TrySetCanceled(cancellationToken);

    /// <inheritdoc/>
    internal override void Execute() => _load();

    /// <inheritdoc/>
    internal override void Complete() => _completion.TrySetResult(true);

    /// <inheritdoc/>
    internal override void CompleteFault(Exception failure) => _completion.TrySetResult(false);
}
