namespace Bork2d.Api;

/// <summary>
/// The exception thrown for engine-originated startup, configuration, and validation failures.
/// </summary>
/// <remarks>
/// BCL exceptions such as <see cref="ArgumentException"/> remain in use for argument and
/// programming errors raised directly by callers.
/// </remarks>
public class Bork2dException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Bork2dException"/> class.
    /// </summary>
    public Bork2dException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Bork2dException"/> class with a message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public Bork2dException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Bork2dException"/> class with a message and
    /// the exception that caused it.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of this exception.</param>
    public Bork2dException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
