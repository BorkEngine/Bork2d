using Bork2d.Engine.Assets.Loading;
using Bork2d.Tests.TestDoubles;
using Microsoft.Extensions.Logging;

namespace Bork2d.Tests.Assets.Service;

/// <summary>
/// Hosts a real <see cref="AssetService"/> with a capturing logger factory for deterministic asset
/// tests. Disposing the host disposes the service and then the factory.
/// </summary>
internal sealed class AssetServiceHost : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private bool _disposed;

    internal AssetServiceHost(string? basePackPath = null)
    {
        Provider = new CapturingLoggerProvider();
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(Provider);
        });

        Service = new AssetService(basePackPath, _loggerFactory);
    }

    internal AssetService Service { get; }

    internal CapturingLoggerProvider Provider { get; }

    internal IReadOnlyList<CapturingLoggerProvider.CapturedLog> Logs => Provider.Entries;

    internal CapturingLoggerProvider.CapturedLog[] LogsAt(LogLevel level)
        => Logs.Where(log => log.Level == level).ToArray();

    internal void Start() => Service.Start();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Service.Dispose();
        _loggerFactory.Dispose();
    }
}
