namespace Bork2d.Bake.Hosting;

/// <summary>
/// Signals a host-load failure that the CLI must turn into a <c>BK1xxx</c> diagnostic.
/// </summary>
/// <remarks>
/// The load context resolver throws this instead of letting a raw
/// <see cref="FileNotFoundException"/> escape, so the CLI boundary never crashes on an incompatible
/// or incomplete host.
/// </remarks>
internal sealed class BakeHostLoadException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="BakeHostLoadException"/> class.</summary>
    /// <param name="message">The failure message.</param>
    public BakeHostLoadException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BakeHostLoadException"/> class.</summary>
    /// <param name="message">The failure message.</param>
    /// <param name="innerException">The underlying failure.</param>
    public BakeHostLoadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
