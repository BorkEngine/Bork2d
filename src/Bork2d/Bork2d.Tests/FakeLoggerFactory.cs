using Microsoft.Extensions.Logging;

namespace Bork2d.Tests;

/// <summary>
/// A logger factory that records disposal without installing a real provider.
/// </summary>
internal sealed class FakeLoggerFactory : ILoggerFactory
{
    public int DisposeCount { get; private set; }

    public void AddProvider(ILoggerProvider provider)
        => throw new NotSupportedException("The test logger factory has no providers.");

    public ILogger CreateLogger(string categoryName)
        => throw new NotSupportedException("The test logger factory creates no loggers.");

    public void Dispose() => DisposeCount++;
}
