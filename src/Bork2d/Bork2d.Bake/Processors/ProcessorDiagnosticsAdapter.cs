using Bork2d.Bake.Processors;

namespace Bork2d.Bake.Processors;

/// <summary>
/// Adapts the public processor diagnostic sink onto the internal sink.
/// </summary>
internal sealed class ProcessorDiagnosticsAdapter : IAssetProcessorDiagnostics
{
    private readonly IBakeProcessorDiagnostics _diagnostics;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessorDiagnosticsAdapter"/> class.
    /// </summary>
    /// <param name="diagnostics">The internal diagnostic sink.</param>
    internal ProcessorDiagnosticsAdapter(IBakeProcessorDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        _diagnostics = diagnostics;
    }

    /// <inheritdoc/>
    public void Error(string code, string message) => _diagnostics.Error(code, message);

    /// <inheritdoc/>
    public void Warning(string code, string message) => _diagnostics.Warning(code, message);

    /// <inheritdoc/>
    public void Info(string code, string message) => _diagnostics.Info(code, message);
}
