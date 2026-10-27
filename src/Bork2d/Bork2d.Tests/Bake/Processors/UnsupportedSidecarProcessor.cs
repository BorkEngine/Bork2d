using Bork2d.Bake.Builder;

namespace Bork2d.Tests.Bake.Processors;

/// <summary>A required-sidecar processor whose default sidecar cannot be written.</summary>
internal sealed class UnsupportedSidecarProcessor : AssetProcessor<UnsupportedSidecar>
{
    /// <inheritdoc/>
    public override string Name => "test-unsupported-sidecar";

    /// <inheritdoc/>
    public override IReadOnlyList<string> Extensions => ["unsupported"];

    /// <inheritdoc/>
    protected override UnsupportedSidecar CreateDefaultSidecar(AssetProcessorContext context)
        => new() { Values = [1, 2] };

    /// <inheritdoc/>
    protected override void Process(AssetProcessorContext context, UnsupportedSidecar sidecar)
    {
    }
}
