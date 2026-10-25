using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Bork2d.Tests;

/// <summary>
/// A deterministic in-memory <see cref="ILoggerProvider"/> that captures what the app and game
/// log through the engine-owned factory.
/// </summary>
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    /// <summary>Gets the number of times the provider was disposed.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Gets a snapshot of the captured entries, in log order.</summary>
    public IReadOnlyList<CapturedLog> Entries => _entries.ToArray();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose() => DisposeCount++;

    /// <summary>One captured log event.</summary>
    internal sealed record CapturedLog(
        string Category,
        LogLevel Level,
        EventId EventId,
        string Message,
        Exception? Exception);

    private sealed class CapturingLogger(string categoryName, ConcurrentQueue<CapturedLog> entries)
        : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            entries.Enqueue(
                new CapturedLog(categoryName, logLevel, eventId, formatter(state, exception), exception));
        }
    }
}
