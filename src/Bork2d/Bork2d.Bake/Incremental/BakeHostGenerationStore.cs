using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Incremental;

/// <summary>
/// Reads and writes <c>obj/bork2d/bake/host-generation.json</c>.
/// </summary>
/// <remarks>
/// An unusable state file is not an error: it only means the next <c>generate-host</c> regenerates.
/// The file is written atomically after a successful generation; a write failure is a warning, never
/// a build failure, because the generated host itself is already correct.
/// </remarks>
internal static class BakeHostGenerationStore
{
    /// <summary>Reads the host-generation state at a path.</summary>
    /// <param name="path">The absolute state path.</param>
    /// <returns>The state, or <see langword="null"/> when it is absent or unusable.</returns>
    internal static BakeHostGenerationState? TryRead(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[]? bytes = BakeJsonFile.TryReadAllBytes(path);
        return bytes is null ? null : BakeHostGenerationStateJson.TryDeserialize(bytes);
    }

    /// <summary>Writes the host-generation state atomically.</summary>
    /// <param name="path">The absolute state path.</param>
    /// <param name="state">The state.</param>
    /// <param name="diagnostics">The diagnostics bag to append a write warning to.</param>
    /// <returns><see langword="true"/> when the state was written.</returns>
    internal static bool TryWrite(
        string path,
        BakeHostGenerationState state,
        BakeDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (BakeJsonFile.TryWriteAtomically(path, BakeHostGenerationStateJson.Serialize(state)))
        {
            return true;
        }

        diagnostics.Add(
            BakeSeverity.Warning,
            BakeDiagnosticCodes.HostIncrementalStateWriteFailed,
            $"The host-generation state '{path}' could not be written; the next generate-host run "
                + "regenerates the host.",
            path);
        return false;
    }
}
