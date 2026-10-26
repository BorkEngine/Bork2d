using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Tests.TestDoubles;

/// <summary>
/// A logger factory that records disposal and returns null loggers without installing a provider.
/// </summary>
internal sealed class FakeLoggerFactory : ILoggerFactory
{
    public int DisposeCount { get; private set; }

    public void AddProvider(ILoggerProvider provider)
        => throw new NotSupportedException("The test logger factory has no providers.");

    public ILogger CreateLogger(string categoryName) => NullLogger.Instance;

    public void Dispose() => DisposeCount++;
}
