using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The internal processor contract the bake framework runs. The public
/// <c>Bork2d.Bake.Builder.IAssetProcessor</c> surface is adapted to this interface.
/// </summary>
/// <remarks>
/// <para>
/// Processors own their source extensions, an optional settings sidecar shape, and the processing
/// code. A <see cref="BakeSidecarPolicy.None"/> processor runs directly; a
/// <see cref="BakeSidecarPolicy.Required"/> processor is invoked with the bound sidecar that the
/// framework created and bound, or with <see cref="CreateDefaultSidecar"/> when the file was missing.
/// </para>
/// <para>
/// The registry is immutable after <see cref="Baking.BakeProject"/> construction. Registration order
/// is stable and a later registration overrides an earlier one for the same extension (WP2).
/// </para>
/// </remarks>
internal interface IBakeAssetProcessor
{
    /// <summary>Gets the stable processor name used in diagnostics and hashing.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the CLR type of the registered processor. The incremental manifest hashes this type's
    /// assembly, so a processor code change invalidates the bake.
    /// </summary>
    Type ProcessorType { get; }

    /// <summary>Gets the source extensions this processor owns.</summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the sidecar policy.</summary>
    BakeSidecarPolicy Policy { get; }

    /// <summary>Gets the sidecar shape for a <see cref="BakeSidecarPolicy.Required"/> processor.</summary>
    ITypeShape? SidecarShape { get; }

    /// <summary>Creates the default sidecar instance for a source whose sidecar file is missing.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <returns>The default sidecar instance.</returns>
    object CreateDefaultSidecar(BakeProcessorContext context);

    /// <summary>Processes one source, optionally with its bound sidecar.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <param name="sidecar">The bound sidecar for a required processor, otherwise <see langword="null"/>.</param>
    void Process(BakeProcessorContext context, object? sidecar);
}
