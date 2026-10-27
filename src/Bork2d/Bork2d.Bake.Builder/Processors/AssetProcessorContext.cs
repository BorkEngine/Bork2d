namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The per-source state handed to a processor: the resolved paths and the output/diagnostic sinks.
/// </summary>
/// <remarks>
/// The context is created for exactly one source and is not reused between sources. A processor must
/// not retain it or anything derived from it after <c>Process</c> returns.
/// </remarks>
public sealed class AssetProcessorContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssetProcessorContext"/> class.
    /// </summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="relativePath">The source path relative to its pack root, with <c>/</c> separators.</param>
    /// <param name="baseAssetPath">The canonical base asset path (pack-relative, extension removed, lowercased).</param>
    /// <param name="sourceFileName">The source file name, for example <c>archer.unit</c>.</param>
    /// <param name="sourceFileNameWithoutExtension">The source file name without its extension, for example <c>archer</c>.</param>
    /// <param name="outputs">The output sink for this source.</param>
    /// <param name="diagnostics">The diagnostics sink for this source.</param>
    internal AssetProcessorContext(
        string sourcePath,
        string relativePath,
        string baseAssetPath,
        string sourceFileName,
        string sourceFileNameWithoutExtension,
        IAssetOutputSink outputs,
        IAssetProcessorDiagnostics diagnostics)
    {
        SourcePath = sourcePath;
        RelativePath = relativePath;
        BaseAssetPath = baseAssetPath;
        SourceFileName = sourceFileName;
        SourceFileNameWithoutExtension = sourceFileNameWithoutExtension;
        Outputs = outputs;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the absolute source path.</summary>
    public string SourcePath { get; }

    /// <summary>Gets the source path relative to its pack root, with <c>/</c> separators.</summary>
    public string RelativePath { get; }

    /// <summary>Gets the canonical base asset path (pack-relative, extension removed, lowercased).</summary>
    public string BaseAssetPath { get; }

    /// <summary>Gets the source file name, for example <c>archer.unit</c>.</summary>
    public string SourceFileName { get; }

    /// <summary>Gets the source file name without its extension, for example <c>archer</c>.</summary>
    public string SourceFileNameWithoutExtension { get; }

    /// <summary>Gets the output sink for this source.</summary>
    public IAssetOutputSink Outputs { get; }

    /// <summary>Gets the diagnostics sink for this source.</summary>
    public IAssetProcessorDiagnostics Diagnostics { get; }
}
