using Bork2d.Bake.Processors;
using PolyType;

namespace Bork2d.Bake;

/// <summary>
/// The required-sidecar processor base: a processor whose settings are bound from
/// <c>&lt;source&gt;.toml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SidecarShape"/> is <c>TSidecar.GetTypeShape()</c>, so the sidecar is bound by the shared
/// shape-driven TOML binder without reflection. <see cref="CreateDefaultSidecar"/> is called only when
/// the sidecar file is missing; the framework writes the returned object as canonical TOML, binds it
/// back, and then calls <see cref="Process"/>.
/// </para>
/// <para>
/// The framework invokes the sidecar overload; the <see cref="IAssetProcessor.Process"/> member throws
/// because a sidecar processor cannot run without its bound settings.
/// </para>
/// </remarks>
/// <typeparam name="TSidecar">The sidecar type, which must carry a compiled shape.</typeparam>
public abstract class AssetProcessor<TSidecar> : IAssetProcessor, ITypedAssetProcessor
    where TSidecar : IShapeable<TSidecar>
{
    /// <inheritdoc/>
    /// <remarks>The default is the processor CLR full name; override it for a stable short name.</remarks>
    public virtual string Name => GetType().FullName ?? GetType().Name;

    /// <inheritdoc/>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <inheritdoc/>
    public SidecarPolicy Policy => SidecarPolicy.Required;

    /// <inheritdoc/>
    public ITypeShape SidecarShape => TSidecar.GetTypeShape();

    /// <inheritdoc/>
    void IAssetProcessor.Process(AssetProcessorContext context)
        => throw new InvalidOperationException(
            $"'{GetType().FullName}' is a required-sidecar processor; the bake framework invokes it "
                + $"with its bound '{typeof(TSidecar).FullName}' sidecar.");

    /// <summary>Creates the default sidecar for a source whose sidecar file is missing.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <returns>The default sidecar instance.</returns>
    protected abstract TSidecar CreateDefaultSidecar(AssetProcessorContext context);

    /// <summary>Processes one source with its bound sidecar.</summary>
    /// <param name="context">The per-source processing context.</param>
    /// <param name="sidecar">The bound or defaulted sidecar.</param>
    protected abstract void Process(AssetProcessorContext context, TSidecar sidecar);

    object ITypedAssetProcessor.CreateDefaultSidecar(AssetProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateDefaultSidecar(context);
    }

    void ITypedAssetProcessor.Process(AssetProcessorContext context, object sidecar)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(sidecar);
        Process(context, (TSidecar)sidecar);
    }
}
