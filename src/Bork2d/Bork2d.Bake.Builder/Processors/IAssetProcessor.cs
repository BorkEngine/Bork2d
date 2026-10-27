using PolyType;

namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The public processor contract: a source type with an extension set, an optional settings sidecar,
/// and processing code.
/// </summary>
/// <remarks>
/// <para>
/// A processor with no settings implements this interface directly with
/// <see cref="SidecarPolicy.None"/>. A processor that needs settings derives from
/// <see cref="AssetProcessor{TSidecar}"/>, which supplies <see cref="SidecarPolicy.Required"/> and a
/// compiled sidecar shape.
/// </para>
/// <para>
/// Processors are trusted build-time code. They run synchronously on the bake thread and may be reused
/// across sources, so an instance must not retain per-source state between calls. Outputs are emitted
/// through <see cref="AssetProcessorContext.Outputs"/>; the engine serializes shaped assets.
/// </para>
/// </remarks>
public interface IAssetProcessor
{
    /// <summary>Gets the stable processor name used in diagnostics and hashing.</summary>
    string Name { get; }

    /// <summary>
    /// Gets the source extensions this processor owns. Both <c>"png"</c> and <c>".PNG"</c> are
    /// accepted and normalized.
    /// </summary>
    IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the sidecar policy.</summary>
    SidecarPolicy Policy { get; }

    /// <summary>Gets the compiled sidecar shape, or <see langword="null"/> when the policy is <see cref="SidecarPolicy.None"/>.</summary>
    ITypeShape? SidecarShape { get; }

    /// <summary>Processes one source.</summary>
    /// <param name="context">The per-source processing context.</param>
    void Process(AssetProcessorContext context);
}
