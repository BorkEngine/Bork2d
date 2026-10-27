namespace Bork2d.Bake.Incremental;

/// <summary>
/// Reads and atomically writes the schema-3 incremental manifest through the shared
/// <see cref="BakeJsonFile"/> IO helpers.
/// </summary>
/// <remarks>
/// A read never throws for normal IO or format failures: a missing, unreadable, corrupt, or
/// wrong-schema manifest is simply unusable and the caller runs a full bake. A write uses the
/// temporary-file replacement so a crash cannot leave a partially written manifest that a later run
/// might mistake for valid state.
/// </remarks>
internal static class BakeManifestStore
{
    /// <summary>Tries to read the manifest at a path.</summary>
    /// <param name="manifestPath">The absolute manifest path.</param>
    /// <returns>The manifest, or <see langword="null"/> when it is missing or unusable.</returns>
    internal static BakeIncrementalManifest? TryRead(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        byte[]? bytes = BakeJsonFile.TryReadAllBytes(manifestPath);
        if (bytes is null)
        {
            return null;
        }

        return BakeManifestJson.TryDeserialize(bytes, out BakeIncrementalManifest? manifest)
            ? manifest
            : null;
    }

    /// <summary>Tries to write the manifest atomically.</summary>
    /// <param name="manifestPath">The absolute manifest path.</param>
    /// <param name="manifest">The manifest to write.</param>
    /// <returns><see langword="true"/> when the target now holds the serialized manifest.</returns>
    internal static bool TryWrite(string manifestPath, BakeIncrementalManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentNullException.ThrowIfNull(manifest);

        byte[] bytes;
        try
        {
            bytes = BakeManifestJson.Serialize(manifest);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }

        return BakeJsonFile.TryWriteAtomically(manifestPath, bytes);
    }
}
