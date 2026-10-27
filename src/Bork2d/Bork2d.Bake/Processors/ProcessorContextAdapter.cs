using Bork2d.Bake.Processors;

namespace Bork2d.Bake.Processors;

/// <summary>
/// Maps the internal per-source context onto the public <see cref="AssetProcessorContext"/> for
/// direct and sidecar processors.
/// </summary>
internal static class ProcessorContextAdapter
{
    /// <summary>Creates the public context for one internal processing context.</summary>
    /// <param name="context">The internal context.</param>
    /// <returns>The public context with adapted output and diagnostic sinks.</returns>
    internal static AssetProcessorContext Map(BakeProcessorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new AssetProcessorContext(
            context.SourcePath,
            context.RelativePath,
            context.BaseAssetPath,
            context.SourceFileName,
            context.SourceFileNameWithoutExtension,
            new OutputSinkAdapter(context.Outputs),
            new ProcessorDiagnosticsAdapter(context.Diagnostics));
    }
}
