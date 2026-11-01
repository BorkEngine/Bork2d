namespace Bork2d.Bake;

/// <summary>
/// The diagnostic sink a processor uses while processing one source, and an asset validator uses while
/// validating one bound asset.
/// </summary>
/// <remarks>
/// A processor that reports an error has all of its outputs for that source discarded; a validator error
/// fails the pack. Processors use <c>BK7xxx</c> codes and validators use <c>BK8xxx</c> codes; the message
/// should name the owning processor or validator and the offending asset field.
/// </remarks>
public interface IAssetProcessorDiagnostics
{
    /// <summary>Reports an error; a processor discards its source outputs and a validator fails the pack.</summary>
    /// <param name="code">The stable <c>BK7xxx</c> (processor) or <c>BK8xxx</c> (validator) code.</param>
    /// <param name="message">The human-readable message.</param>
    void Error(string code, string message);

    /// <summary>Reports a warning.</summary>
    /// <param name="code">The stable <c>BK7xxx</c> (processor) or <c>BK8xxx</c> (validator) code.</param>
    /// <param name="message">The human-readable message.</param>
    void Warning(string code, string message);

    /// <summary>Reports informational output.</summary>
    /// <param name="code">The stable <c>BK7xxx</c> (processor) or <c>BK8xxx</c> (validator) code.</param>
    /// <param name="message">The human-readable message.</param>
    void Info(string code, string message);
}
