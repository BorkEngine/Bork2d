namespace Bork2d.Bake.Builder.Processors;

/// <summary>
/// The diagnostic sink a processor uses while processing one source.
/// </summary>
/// <remarks>
/// A processor that reports an error has all of its outputs for that source discarded. Use
/// <c>BK7xxx</c> codes; the message should name the processor when it is processor-specific.
/// </remarks>
public interface IAssetProcessorDiagnostics
{
    /// <summary>Reports an error; the source's outputs are discarded.</summary>
    /// <param name="code">The stable <c>BK7xxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    void Error(string code, string message);

    /// <summary>Reports a warning.</summary>
    /// <param name="code">The stable <c>BK7xxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    void Warning(string code, string message);

    /// <summary>Reports informational output.</summary>
    /// <param name="code">The stable <c>BK7xxx</c> code.</param>
    /// <param name="message">The human-readable message.</param>
    void Info(string code, string message);
}
