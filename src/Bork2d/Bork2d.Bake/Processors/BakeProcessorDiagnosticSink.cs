using Bork2d.Bake.Diagnostics;

namespace Bork2d.Bake.Processors;

/// <summary>
/// Adapts a processor's diagnostic calls into the bake diagnostics bag for one source.
/// </summary>
/// <remarks>
/// Every message is prefixed with the processor name and located at the source path. An error marks
/// the source failed: the runner discards all of its outputs. Warnings and infos are informational and
/// do not affect the outputs.
/// </remarks>
internal sealed class BakeProcessorDiagnosticSink : IAssetProcessorDiagnostics
{
    private readonly string _processorName;
    private readonly string _sourcePath;
    private readonly string _assetPath;
    private readonly BakeDiagnostics _diagnostics;

    /// <summary>
    /// Initializes a new instance of the <see cref="BakeProcessorDiagnosticSink"/> class.
    /// </summary>
    /// <param name="processorName">The owning processor's name.</param>
    /// <param name="sourcePath">The source diagnostic display path.</param>
    /// <param name="assetPath">The source's canonical base asset path.</param>
    /// <param name="diagnostics">The diagnostics bag to append to.</param>
    internal BakeProcessorDiagnosticSink(
        string processorName,
        string sourcePath,
        string assetPath,
        BakeDiagnostics diagnostics)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentNullException.ThrowIfNull(diagnostics);

        _processorName = processorName;
        _sourcePath = sourcePath;
        _assetPath = assetPath;
        _diagnostics = diagnostics;
    }

    /// <summary>Gets a value indicating whether the processor reported an error.</summary>
    internal bool HasError { get; private set; }

    /// <inheritdoc/>
    public void Error(string code, string message)
    {
        HasError = true;
        Add(BakeSeverity.Error, code, message);
    }

    /// <inheritdoc/>
    public void Warning(string code, string message)
        => Add(BakeSeverity.Warning, code, message);

    /// <inheritdoc/>
    public void Info(string code, string message)
        => Add(BakeSeverity.Info, code, message);

    private void Add(BakeSeverity severity, string code, string message)
        => _diagnostics.Add(
            severity,
            code,
            $"{_processorName}: {message}",
            _sourcePath,
            assetPath: _assetPath);
}
