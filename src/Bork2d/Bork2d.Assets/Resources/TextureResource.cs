using Bork2d.Api.Assets;
using Bork2d.Assets.Descriptors;
using Bork2d.Assets.Vfs;

namespace Bork2d.Assets.Resources;

/// <summary>
/// The immutable CPU texture-descriptor resource: a validated descriptor and its raw payload entry,
/// or a cached failure.
/// </summary>
/// <remarks>
/// There is exactly one resource per canonical texture path for the process lifetime. A failed
/// resource is cached as failed and is never retried (D-M3-18). The payload is metadata only in M3;
/// it is never decompressed or uploaded, and the payload entry lets the M8 renderer read it on first
/// use. Instances are immutable after publication.
/// </remarks>
internal sealed class TextureResource
{
    private TextureResource(
        string path,
        TextureDescriptor? descriptor,
        ResolvedAsset? payload,
        AssetException? error)
    {
        Path = path;
        Descriptor = descriptor;
        Payload = payload;
        Error = error;
    }

    /// <summary>Gets the canonical texture descriptor path.</summary>
    internal string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the descriptor and its payload validated. When
    /// <see langword="false"/>, <see cref="Failure"/> explains why and the data properties are null.
    /// </summary>
    internal bool IsValid => Error is null;

    /// <summary>Gets the validated descriptor, or <see langword="null"/> for a failed resource.</summary>
    internal TextureDescriptor? Descriptor { get; }

    /// <summary>
    /// Gets the raw payload entry from the enqueue-time snapshot, or <see langword="null"/> for a
    /// failed resource. M3 never reads it.
    /// </summary>
    internal ResolvedAsset? Payload { get; }

    /// <summary>Gets the failure reason, or <see langword="null"/> for a valid resource.</summary>
    internal string? Failure => Error?.Message;

    /// <summary>
    /// Gets the classified failure, or <see langword="null"/> for a valid resource. The sprite load
    /// that first failed on this descriptor uses this kind for its own error log.
    /// </summary>
    internal AssetException? Error { get; }

    /// <summary>Creates a validated resource.</summary>
    /// <param name="path">The canonical texture descriptor path.</param>
    /// <param name="descriptor">The validated descriptor.</param>
    /// <param name="payload">The raw payload entry.</param>
    /// <returns>The published resource.</returns>
    internal static TextureResource Ready(
        string path,
        TextureDescriptor descriptor,
        ResolvedAsset payload)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(payload);
        return new TextureResource(path, descriptor, payload, error: null);
    }

    /// <summary>Creates a cached failed resource.</summary>
    /// <param name="path">The canonical texture descriptor path.</param>
    /// <param name="failure">The classified validation, decode, or IO failure.</param>
    /// <returns>The published failed resource.</returns>
    internal static TextureResource Failed(string path, AssetException failure)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(failure);
        return new TextureResource(path, descriptor: null, payload: null, failure);
    }
}
