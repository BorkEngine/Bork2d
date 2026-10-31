using Bork2d.Bake;
using Bork2d.Engine.Assets;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>A required-sidecar processor whose sidecar shape declares references.</summary>
internal sealed class ReferenceSidecarProcessor : AssetProcessor<ReferenceSidecar>
{
    /// <inheritdoc/>
    public override string Name => "test-reference-sidecar";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => ["ref"];

    /// <inheritdoc/>
    protected override ReferenceSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new();

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, ReferenceSidecar sidecar)
    {
    }
}
