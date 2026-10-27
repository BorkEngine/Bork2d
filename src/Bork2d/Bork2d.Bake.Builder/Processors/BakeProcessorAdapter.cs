using Bork2d.Bake.Processors;
using PolyType;

namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// Adapts one public <see cref="IAssetProcessor"/> onto the internal processor contract the bake
/// framework stores and executes.
/// </summary>
/// <remarks>
/// A processor registered with <see cref="SidecarPolicy.None"/> runs directly through
/// <see cref="IAssetProcessor.Process(AssetProcessorContext)"/>. A processor derived from
/// <see cref="AssetProcessor{TSidecar}"/> is invoked through <see cref="ITypedAssetProcessor"/> so the
/// framework can create, bind, and pass its sidecar. The adapter also carries the registration
/// metadata the immutable processor registry validates.
/// </remarks>
internal sealed class BakeProcessorAdapter : IBakeAssetProcessor
{
    private readonly IAssetProcessor _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeProcessorAdapter"/> class.
    /// </summary>
    /// <param name="processor">The public processor instance.</param>
    internal BakeProcessorAdapter(IAssetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        _processor = processor;
    }

    /// <inheritdoc/>
    public string Name => _processor.Name;

    /// <inheritdoc/>
    public Type ProcessorType => _processor.GetType();

    /// <inheritdoc/>
    public IReadOnlyList<string> Extensions => _processor.Extensions;

    /// <inheritdoc/>
    public BakeSidecarPolicy Policy => _processor.Policy switch
    {
        SidecarPolicy.Required => BakeSidecarPolicy.Required,
        _ => BakeSidecarPolicy.None,
    };

    /// <inheritdoc/>
    public ITypeShape? SidecarShape => _processor.SidecarShape;

    /// <inheritdoc/>
    public object CreateDefaultSidecar(BakeProcessorContext context)
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

    /// <inheritdoc/>
    public void Process(BakeProcessorContext context, object? sidecar)
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

        _processor.Process(ProcessorContextAdapter.Map(context));
    }
}
