using PolyType;

namespace Bork2d.Bake.Processors;

/// <summary>
/// The narrow registration seam over one public <see cref="IAssetProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// The bake pipeline executes the public processor contract directly. A registration adds only the
/// metadata the framework needs beyond that contract: the processor's CLR type, which the incremental
/// manifest hashes, and the required-sidecar dispatch through <see cref="ITypedAssetProcessor"/>. It
/// defines no second context, output sink, diagnostic sink, or sidecar policy.
/// </para>
/// <para>
/// The registry creates one registration per processor when the project is built, so a registration's
/// name, extensions, policy, and sidecar shape are the public processor's own values.
/// </para>
/// </remarks>
internal sealed class BakeProcessorRegistration
{
    private readonly IAssetProcessor _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeProcessorRegistration"/> class.
    /// </summary>
    /// <param name="processor">The public processor instance.</param>
    internal BakeProcessorRegistration(IAssetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <summary>Gets the public processor this registration executes.</summary>
    internal IAssetProcessor Processor => _processor;

    /// <summary>
    /// Gets the CLR type of the registered processor. The incremental manifest hashes this type's
    /// assembly, so a processor code change invalidates the bake.
    /// </summary>
    internal Type ProcessorType => _processor.GetType();

    /// <summary>Gets the stable processor name used in diagnostics and hashing.</summary>
    internal string Name => _processor.Name;

    /// <summary>Gets the source extensions this processor owns.</summary>
    internal IReadOnlyList<string> Extensions => _processor.Extensions;

    /// <summary>Gets the processor's sidecar policy.</summary>
    internal SidecarPolicy Policy => _processor.Policy;

    /// <summary>Gets the sidecar shape for a <see cref="SidecarPolicy.Required"/> processor.</summary>
    internal ITypeShape? SidecarShape => _processor.SidecarShape;

    /// <summary>Creates the default sidecar instance for a source whose sidecar file is missing.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <returns>The default sidecar instance.</returns>
    internal object CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_processor is ITypedAssetProcessor typed)
        {
            return typed.CreateDefaultSidecar(context);
        }

        throw new InvalidOperationException(
            $"'{_processor.Name}' is registered with a required sidecar but does not derive from "
                + "AssetProcessor<TSidecar>.");
    }

    /// <summary>Processes one source, optionally with its bound sidecar.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <param name="sidecar">The bound sidecar for a required processor, otherwise <see langword="null"/>.</param>
    internal void Process(AssetProcessorContext context, object? sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_processor is ITypedAssetProcessor typed)
        {
            if (sidecar is null)
            {
                throw new InvalidOperationException(
                    $"'{_processor.Name}' requires a sidecar, but the framework passed none.");
            }

            typed.Process(context, sidecar);
            return;
        }

        if (_processor.Policy != SidecarPolicy.None)
        {
            throw new InvalidOperationException(
                $"'{_processor.Name}' is registered with a required sidecar but does not derive from "
                    + "AssetProcessor<TSidecar>.");
        }

        _processor.Process(context);
    }
}
