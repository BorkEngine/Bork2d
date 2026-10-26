using Bork2d.Api.Assets;
using Bork2d.Assets.Pack;
using Bork2d.Assets.Vfs;
using PolyType;

namespace Bork2d.Assets.Loading;

/// <summary>
/// Deserializes a MessagePack asset from a resolved entry with the compiled shape on
/// <typeparamref name="T"/> and verifies that the declared <c>Size</c> was consumed exactly.
/// </summary>
/// <remarks>
/// This is the single loader-thread deserialization path for <see cref="IAssets.LoadAsset{T}"/> and
/// for the internal sprite/descriptor pipeline. It performs no engine/GPU work and no caching. A
/// codec, shape, or size failure is classified as <see cref="AssetErrorKind.Decode"/> (or
/// <see cref="AssetErrorKind.Io"/> for a read failure).
/// </remarks>
internal static class AssetDeserializer
{
    /// <summary>
    /// Deserializes an asset with the generated shape on <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The shapeable asset type.</typeparam>
    /// <param name="entry">The winning, already resolved asset entry.</param>
    /// <returns>The deserialized value; never <see langword="null"/>.</returns>
    /// <exception cref="AssetException">
    /// The payload could not be read or deserialized, the declared size was not consumed exactly, or
    /// the payload deserialized to null.
    /// </exception>
    internal static T Deserialize<T>(ResolvedAsset entry)
        where T : class, IShapeable<T>
    {
        using Stream stream = entry.OpenDecompressedStream();

        T? value;
        try
        {
            value = PackSerialization.DeserializeAsset<T>(stream);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Exception mapped = AssetFailures.Map(
                entry.Path,
                entry.ResolvedPackPath,
                exception);
            if (mapped is AssetException { Kind: AssetErrorKind.Decode } decoded)
            {
                throw new AssetException(
                    AssetErrorKind.Decode,
                    entry.Path,
                    $"Failed to deserialize asset '{entry.Path}' as {TypeName<T>()}: "
                        + decoded.Message,
                    exception);
            }

            throw;
        }

        // The generated-shape deserializer may stop at the end of the MessagePack object without
        // consuming the rest of the declared payload. Draining verifies the exact declared Size: a
        // short payload throws and trailing decompressed bytes past the declared Size throw.
        DrainToEnd(stream, entry.Path, entry.ResolvedPackPath);

        return value
            ?? throw new AssetException(
                AssetErrorKind.Decode,
                entry.Path,
                $"Asset '{entry.Path}' deserialized to null as {TypeName<T>()}.");
    }

    private static void DrainToEnd(Stream stream, string canonicalPath, string packPath)
    {
        Span<byte> buffer = stackalloc byte[256];
        while (true)
        {
            int read;
            try
            {
                read = stream.Read(buffer);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw AssetFailures.Map(canonicalPath, packPath, exception);
            }

            if (read == 0)
            {
                return;
            }
        }
    }

    private static string TypeName<T>() => typeof(T).FullName ?? typeof(T).Name;
}
