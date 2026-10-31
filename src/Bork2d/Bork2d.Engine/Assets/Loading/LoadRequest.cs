namespace Bork2d.Engine.Assets.Loading;

/// <summary>
/// A queued load that produces a <typeparamref name="T"/> on the loader thread.
/// </summary>
/// <typeparam name="T">The result type, for example <see cref="byte"/>[] or a MessagePack asset.</typeparam>
/// <remarks>
/// The load delegate is compiled with the call site; no reflection or dynamic invocation is
/// involved. The result is produced fresh for every request.
/// </remarks>
internal sealed class LoadRequest<T> : AssetRequest
{
    private readonly TaskCompletionSource<T> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Func<T> _load;

    private T _value = default!;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoadRequest{T}"/> class.
    /// </summary>
    /// <param name="canonicalPath">The canonical asset path the request was resolved for.</param>
    /// <param name="packPath">The resolved filesystem path of the pack that supplied the entry.</param>
    /// <param name="kind">The asset kind for the success log event.</param>
    /// <param name="load">The loader-thread body; it must not perform engine/GPU work.</param>
    internal LoadRequest(string canonicalPath, string packPath, string kind, Func<T> load)
    {
        ArgumentNullException.ThrowIfNull(canonicalPath);
        ArgumentNullException.ThrowIfNull(packPath);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(load);

        CanonicalPath = canonicalPath;
        PackPath = packPath;
        Kind = kind;
        _load = load;
    }

    /// <inheritdoc/>
    internal override string CanonicalPath { get; }

    /// <inheritdoc/>
    internal override string PackPath { get; }

    /// <inheritdoc/>
    internal override string Kind { get; }

    /// <inheritdoc/>
    internal override Task Completion => _completion.Task;

    /// <summary>Gets the strongly typed task the public generic member returns.</summary>
    internal Task<T> TypedCompletion => _completion.Task;

    /// <inheritdoc/>
    internal override bool Cancel(CancellationToken cancellationToken)
        => _completion.TrySetCanceled(cancellationToken);

    /// <inheritdoc/>
    internal override void Execute() => _value = _load();

    /// <inheritdoc/>
    internal override void Complete() => _completion.TrySetResult(_value);

    /// <inheritdoc/>
    internal override void CompleteFault(Exception failure)
        => _completion.TrySetException(failure);
}
