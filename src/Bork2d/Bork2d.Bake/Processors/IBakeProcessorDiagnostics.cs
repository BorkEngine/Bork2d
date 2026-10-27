namespace Bork2d.Bake.Processors;

/// <summary>
/// The internal diagnostic sink a processor uses while processing one source.
/// </summary>
internal interface IBakeProcessorDiagnostics
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
