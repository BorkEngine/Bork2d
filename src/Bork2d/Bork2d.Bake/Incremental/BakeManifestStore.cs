using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Reads and writes <c>obj/bork2d/bake/manifest.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// A missing, unreadable, or structurally invalid manifest is not an error: it simply means there is
/// no trustworthy record of a previous successful bake, so the caller runs a full bake (M4 §10.3).
/// </para>
/// <para>
/// A successful full bake writes the manifest through <see cref="BakeJsonFile.TryWriteAtomically"/>.
/// A failed run never reaches this code, so the previous pack and manifest both stay untouched. If
/// the manifest write itself fails after a valid pack was written, the run reports a warning and
/// stays successful: the pack is correct and the next bake simply runs fully again.
/// </para>
/// </remarks>
internal static class BakeManifestStore
{
    /// <summary>Reads the manifest at a path.</summary>
    /// <param name="path">The absolute manifest path.</param>
    /// <returns>The manifest, or <see langword="null"/> when it is absent or unusable.</returns>
    internal static BakeIncrementalManifest? TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[]? bytes = BakeJsonFile.TryReadAllBytes(path);
        return bytes is null ? null : BakeManifestJson.TryDeserialize(bytes);
    }

    /// <summary>Writes the manifest atomically.</summary>
    /// <param name="path">The absolute manifest path.</param>
    /// <param name="manifest">The manifest.</param>
    /// <param name="diagnostics">The diagnostics bag to append a write warning to.</param>
    /// <returns><see langword="true"/> when the manifest was written.</returns>
    internal static bool TryWrite(
        string path,
        BakeIncrementalManifest manifest,
        BakeDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (BakeJsonFile.TryWriteAtomically(path, BakeManifestJson.Serialize(manifest)))
        {
            return true;
        }

        diagnostics.Add(
            BakeSeverity.Warning,
            BakeDiagnosticCodes.IncrementalManifestWriteFailed,
            $"The incremental manifest '{path}' could not be written; the pack is valid and the next "
                + "bake runs fully.",
            path);
        return false;
    }
}
