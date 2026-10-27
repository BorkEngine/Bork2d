namespace Bork2d.Bake.Sources;

/// <summary>
/// The classified sources of a bake: custom-data TOML sources and processor-owned non-TOML sources.
/// </summary>
/// <remarks>
/// Both lists are ordered deterministically by pack index and then canonical path, so discovery,
/// diagnostics, binding, and processor execution all see the same order.
/// </remarks>
internal sealed class BakeDiscoveredSources
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeDiscoveredSources"/> class.
    /// </summary>
    /// <param name="dataSources">The custom-data sources, ordered by pack index and asset path.</param>
    /// <param name="processorSources">
    /// The processor sources, ordered by pack index and base asset path.
    /// </param>
    internal BakeDiscoveredSources(
        IReadOnlyList<BakeSource> dataSources,
        IReadOnlyList<BakeProcessorSource> processorSources)
    {
        ArgumentNullException.ThrowIfNull(dataSources);
        ArgumentNullException.ThrowIfNull(processorSources);

        DataSources = [.. dataSources];
        ProcessorSources = [.. processorSources];
    }

    /// <summary>Gets the discovered custom-data sources.</summary>
    internal IReadOnlyList<BakeSource> DataSources { get; }

    /// <summary>Gets the discovered processor sources.</summary>
    internal IReadOnlyList<BakeProcessorSource> ProcessorSources { get; }
}
