using Bork2d.Bake.Baking;
using Bork2d.Bake.Processors;

namespace Bork2d.Bake.Sources;

/// <summary>
/// One discovered non-TOML source owned by a registered processor.
/// </summary>
/// <remarks>
/// The base asset path is derived by removing the matched registered extension from the pack-relative
/// path and lowercasing the result. A <see cref="BakeSidecarPolicy.Required"/> source may own the
/// discovered <c>&lt;source&gt;.toml</c> sidecar; a <see cref="BakeSidecarPolicy.None"/> source has no
/// sidecar association.
/// </remarks>
internal sealed class BakeProcessorSource
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeProcessorSource"/> class.
    /// </summary>
    /// <param name="pack">The owning configured pack.</param>
    /// <param name="relativePath">The source path relative to the pack root, with <c>/</c> separators.</param>
    /// <param name="fullPath">The absolute source path.</param>
    /// <param name="baseAssetPath">The canonical base asset path, matched extension removed.</param>
    /// <param name="matchedExtension">The normalized extension the registry matched.</param>
    /// <param name="processor">The owning processor.</param>
    internal BakeProcessorSource(
        BakePack pack,
        string relativePath,
        string fullPath,
        string baseAssetPath,
        string matchedExtension,
        IBakeAssetProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(pack);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(matchedExtension);
        ArgumentNullException.ThrowIfNull(processor);

        Pack = pack;
        RelativePath = relativePath;
        FullPath = fullPath;
        BaseAssetPath = baseAssetPath;
        MatchedExtension = matchedExtension;
        Processor = processor;
    }

    /// <summary>Gets the owning configured pack.</summary>
    internal BakePack Pack { get; }

    /// <summary>Gets the source path relative to the pack root, with <c>/</c> separators.</summary>
    internal string RelativePath { get; }

    /// <summary>Gets the absolute source path.</summary>
    internal string FullPath { get; }

    /// <summary>Gets the canonical base asset path (lowercase, matched extension removed).</summary>
    internal string BaseAssetPath { get; }

    /// <summary>Gets the normalized extension the processor registry matched.</summary>
    internal string MatchedExtension { get; }

    /// <summary>Gets the processor that owns the source.</summary>
    internal IBakeAssetProcessor Processor { get; }

    /// <summary>Gets the absolute sidecar path, or <see langword="null"/> when none was discovered.</summary>
    internal string? SidecarPath { get; set; }

    /// <summary>Gets the sidecar path relative to the pack root, when one was discovered.</summary>
    internal string? SidecarRelativePath { get; set; }
}
