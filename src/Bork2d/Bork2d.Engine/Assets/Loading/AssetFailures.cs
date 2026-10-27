using Bork2d.Engine.Assets;

namespace Bork2d.Engine.Assets.Loading;

/// <summary>
/// Creates the public <see cref="AssetException"/> values reported by the asset service.
/// </summary>
/// <remarks>
/// Pack open/parse/mount failures stay <see cref="Bork2dException"/>; this helper only classifies
/// per-asset lookup, decode, read, and payload-shape failures. A read failure is classified as
/// <see cref="AssetErrorKind.Io"/> when the underlying exception (or one of its inners) is an
/// <see cref="IOException"/>; codec and payload-shape failures are
/// <see cref="AssetErrorKind.Decode"/>.
/// </remarks>
internal static class AssetFailures
{
    /// <summary>Creates the not-found failure for a canonical path.</summary>
    /// <param name="canonicalPath">The canonical asset path.</param>
    /// <returns>The classified exception.</returns>
    internal static AssetException NotFound(string canonicalPath)
        => new(
            AssetErrorKind.NotFound,
            canonicalPath,
            $"Asset '{canonicalPath}' is not present in the mounted packs.");

    /// <summary>Creates the raw-entry failure for a MessagePack-asset request.</summary>
    /// <param name="canonicalPath">The canonical asset path.</param>
    /// <returns>The classified exception.</returns>
    internal static AssetException RawAsset(string canonicalPath)
        => new(
            AssetErrorKind.RawAsset,
            canonicalPath,
            $"Asset '{canonicalPath}' is raw data and cannot be deserialized as a MessagePack "
            + "asset; use LoadData for opaque bytes.");

    /// <summary>Creates the invalid-payload failure for a canonical path.</summary>
    /// <param name="canonicalPath">The canonical asset path.</param>
    /// <param name="reason">The payload-shape violation.</param>
    /// <returns>The classified exception.</returns>
    internal static AssetException InvalidData(string canonicalPath, string reason)
        => new(
            AssetErrorKind.InvalidData,
            canonicalPath,
            $"Asset '{canonicalPath}' has an invalid payload: {reason}");

    /// <summary>
    /// Classifies an exception thrown while reading or deserializing a resolved asset.
    /// </summary>
    /// <param name="canonicalPath">The canonical asset path.</param>
    /// <param name="packPath">
    /// The resolved filesystem path of the owning pack, stored on the exception for an IO failure.
    /// </param>
    /// <param name="exception">The failure thrown by the archive, codec, or serializer.</param>
    /// <returns>The classified exception, or <paramref name="exception"/> when it is already an
    /// <see cref="AssetException"/>.</returns>
    internal static Exception Map(
        string canonicalPath,
        string packPath,
        Exception exception)
    {
        if (exception is AssetException)
        {
            return exception;
        }

        bool isIoFailure = HasIoFailure(exception);
        return new AssetException(
            isIoFailure ? AssetErrorKind.Io : AssetErrorKind.Decode,
            isIoFailure ? packPath : canonicalPath,
            $"Failed to load asset '{canonicalPath}': {exception.Message}",
            exception);
    }

    private static bool HasIoFailure(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidDataException)
            {
                return false;
            }

            if (current is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }
}
