using Bork2d.Engine;

namespace Bork2d.Engine.Assets;

/// <summary>
/// The exception reported for asset lookup, decode, and read failures.
/// </summary>
/// <remarks>
/// <para>
/// The runtime reports asset failures through the faulted task returned by
/// <see cref="IAssets.LoadData(string)"/> and <see cref="IAssets.LoadAsset{T}(string)"/>. A missing
/// asset path faults with <see cref="AssetErrorKind.NotFound"/>; the loader does not validate
/// fields or follow asset references.
/// </para>
/// <para>
/// Pack open/parse/mount failures remain <see cref="Bork2dException"/> (D-M3-15). Null and invalid
/// path arguments throw <see cref="ArgumentNullException"/>/<see cref="ArgumentException"/> at the
/// call. Exceptions are not serialized.
/// </para>
/// </remarks>
public sealed class AssetException : Bork2dException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetException"/> class.
    /// </summary>
    /// <param name="kind">The failure classification.</param>
    /// <param name="assetPath">
    /// The canonical asset path for a load failure, or the filesystem path for an IO failure.
    /// </param>
    /// <param name="message">The message that describes the error.</param>
    public AssetException(AssetErrorKind kind, string? assetPath, string message)
        : base(message)
    {
        Kind = kind;
        AssetPath = assetPath;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AssetException"/> class with the exception that
    /// caused it.
    /// </summary>
    /// <param name="kind">The failure classification.</param>
    /// <param name="assetPath">
    /// The canonical asset path for a load failure, or the filesystem path for an IO failure.
    /// </param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    public AssetException(
        AssetErrorKind kind,
        string? assetPath,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
        AssetPath = assetPath;
    }

    /// <summary>
    /// Gets the failure classification.
    /// </summary>
    public AssetErrorKind Kind { get; }

    /// <summary>
    /// Gets the canonical asset path for a load failure, or the filesystem path for an IO failure.
    /// </summary>
    public string? AssetPath { get; }
}
