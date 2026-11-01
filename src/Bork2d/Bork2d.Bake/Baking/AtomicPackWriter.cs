using Bork2d.Bake.Diagnostics;
using Bork2d.Engine.Assets.Pack;

namespace Bork2d.Bake.Baking;

/// <summary>
/// Writes one composed pack through a <see cref="PackPublishTransaction"/>: the complete pack is
/// staged in a unique temporary file in the destination directory and then replaces the target.
/// </summary>
/// <remarks>
/// <para>
/// A failed write leaves the previous pack unchanged (M4 §10.1). The temporary file is created in the
/// destination directory so the staging write and the final replacement stay on one volume and the
/// replacement is an atomic rename; every staging file and backup is deleted on any failure,
/// including a <c>PackBuilder</c> write failure.
/// </para>
/// <para>
/// <c>PackBuilder</c> remains the only pack writer: this helper only chooses the path and performs the
/// replacement through the transaction.
/// </para>
/// </remarks>
internal static class AtomicPackWriter
{
    /// <summary>Stages the pack and atomically replaces the target.</summary>
    /// <param name="packPath">The absolute pack target.</param>
    /// <param name="builder">The composed pack builder.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    /// <returns><see langword="true"/> when the target now holds the new pack.</returns>
    internal static bool Write(
        string packPath,
        PackBuilder builder,
        BakeDiagnostics diagnostics)
    {
        using var transaction = new PackPublishTransaction();
        return transaction.Stage(packPath, builder, diagnostics)
            && transaction.Commit(diagnostics, out _);
    }
}
