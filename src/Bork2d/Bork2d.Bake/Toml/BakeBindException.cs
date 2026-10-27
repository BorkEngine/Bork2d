namespace Bork2d.Bake.Toml;

/// <summary>
/// Signals a bake-host contract violation while binding: the host has no binding for a mapped type
/// name, or its binding produced no value for a source that has no diagnostics.
/// </summary>
/// <remarks>
/// Authoring errors (unknown keys, type mismatches, range failures, ...) are diagnostics on
/// <see cref="BakeBindContext"/>, never this exception. The CLI converts a thrown exception into a
/// <c>BK1xxx</c> host diagnostic so a stale or incompatible host never escapes as a raw failure.
/// </remarks>
internal sealed class BakeBindException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="BakeBindException"/> class.</summary>
    /// <param name="message">The failure message.</param>
    public BakeBindException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BakeBindException"/> class.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BakeBindException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
