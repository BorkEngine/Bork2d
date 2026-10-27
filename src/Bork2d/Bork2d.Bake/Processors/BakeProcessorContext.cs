namespace Bork2d.Bake.Processors;

/// <summary>
/// The per-source state handed to a processor: the resolved paths and the output/diagnostic sinks.
/// </summary>
/// <remarks>
/// The context is created for exactly one source and is not reused between sources. A processor that
/// keeps per-source state between calls is invalid: the framework may reuse the processor instance
/// for every matching source.
/// </remarks>
internal sealed class BakeProcessorContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BakeProcessorContext"/> class.
    /// </summary>
    /// <param name="sourcePath">The absolute source path.</param>
    /// <param name="relativePath">The source path relative to its pack root, with <c>/</c> separators.</param>
    /// <param name="baseAssetPath">The canonical base asset path (pack-relative, extension removed, lowercased).</param>
    /// <param name="sourceFileName">The source file name, for example <c>archer.unit</c>.</param>
    /// <param name="sourceFileNameWithoutExtension">The source file name without its extension, for example <c>archer</c>.</param>
    /// <param name="outputs">The output sink for this source.</param>
    /// <param name="diagnostics">The diagnostics sink for this source.</param>
    internal BakeProcessorContext(
        string sourcePath,
        string relativePath,
        string baseAssetPath,
        string sourceFileName,
        string sourceFileNameWithoutExtension,
        IBakeOutputSink outputs,
        IBakeProcessorDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseAssetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFileNameWithoutExtension);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(diagnostics);

        SourcePath = sourcePath;
        RelativePath = relativePath;
        BaseAssetPath = baseAssetPath;
        SourceFileName = sourceFileName;
        SourceFileNameWithoutExtension = sourceFileNameWithoutExtension;
        Outputs = outputs;
        Diagnostics = diagnostics;
    }

    /// <summary>Gets the absolute source path.</summary>
    internal string SourcePath { get; }

    /// <summary>Gets the source path relative to its pack root, with <c>/</c> separators.</summary>
    internal string RelativePath { get; }

    /// <summary>Gets the canonical base asset path (pack-relative, extension removed, lowercased).</summary>
    internal string BaseAssetPath { get; }

    /// <summary>Gets the source file name, for example <c>archer.unit</c>.</summary>
    internal string SourceFileName { get; }

    /// <summary>Gets the source file name without its extension, for example <c>archer</c>.</summary>
    internal string SourceFileNameWithoutExtension { get; }

    /// <summary>Gets the output sink for this source.</summary>
    internal IBakeOutputSink Outputs { get; }

    /// <summary>Gets the diagnostics sink for this source.</summary>
    internal IBakeProcessorDiagnostics Diagnostics { get; }
}
