using System.Runtime.ExceptionServices;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// Shared exception aggregation for the host loops.
/// </summary>
/// <remarks>
/// A single failure is rethrown with its original identity and stack through
/// <see cref="ExceptionDispatchInfo"/>; several distinct failures become one
/// <see cref="AggregateException"/> whose inner exceptions keep the loop's documented precedence
/// order. A failure already present by reference is never added twice.
/// </remarks>
internal static class RunFailures
{
    /// <summary>Adds a failure unless it is null or already present by reference.</summary>
    /// <param name="errors">The destination list in precedence order.</param>
    /// <param name="exception">The failure to add.</param>
    internal static void Add(List<Exception> errors, Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        foreach (Exception existing in errors)
        {
            if (ReferenceEquals(existing, exception))
            {
                return;
            }
        }

        errors.Add(exception);
    }

    /// <summary>Rethrows one failure with its original stack.</summary>
    /// <param name="exception">The failure to rethrow.</param>
    internal static void Throw(Exception exception)
        => ExceptionDispatchInfo.Capture(exception).Throw();

    /// <summary>Throws the collected failures: the original for one, an aggregate for several.</summary>
    /// <param name="errors">The failures in precedence order.</param>
    /// <param name="message">The aggregate message used when more than one failure exists.</param>
    internal static void Throw(List<Exception> errors, string message)
    {
        if (errors.Count == 0)
        {
            return;
        }

        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
            return;
        }

        throw new AggregateException(message, errors);
    }
}
