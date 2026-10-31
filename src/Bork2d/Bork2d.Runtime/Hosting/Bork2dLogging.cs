using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZLogger;
using ZLogger.Formatters;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// Creates the app-owned Microsoft.Extensions.Logging factory and installs the default
/// ZLogger console provider.
/// </summary>
/// <remarks>
/// <para>
/// The default provider is added only when the builder has no <c>WithLogging</c> callback. A
/// supplied callback owns provider configuration entirely; the builder adds no provider behind
/// it. No provider discovery, reflection, or file sink is involved, so the default path stays
/// trim/AOT-friendly.
/// </para>
/// <para>
/// Logging configuration is identical in windowed, offscreen, and headless modes.
/// </para>
/// </remarks>
internal static class Bork2dLogging
{
    /// <summary>The category for builder, mode, lifecycle, and windowing messages.</summary>
    internal const string RuntimeCategory = "Bork2d.Runtime";

    /// <summary>The category for entities, snapshots, and time messages.</summary>
    internal const string CommonCategory = "Bork2d.Engine";

    /// <summary>The category for pack, decompression, and loading messages.</summary>
    internal const string AssetsCategory = "Bork2d.Engine.Assets";

    /// <summary>The category for device, swapchain, pipeline, and frame messages.</summary>
    internal const string RenderCategory = "Bork2d.Render";

    /// <summary>
    /// Creates the factory, applying the default minimum level, the optional caller
    /// configuration, and otherwise the default console provider.
    /// </summary>
    /// <param name="configure">
    /// The optional <c>WithLogging</c> callback. When non-null it is invoked exactly once and no
    /// default provider is added.
    /// </param>
    /// <returns>The created factory. The caller owns and disposes it.</returns>
    /// <remarks>
    /// Microsoft.Extensions.Logging 11 creates providers through its internal service provider,
    /// which disposes providers it creates (the default ZLogger console provider and providers
    /// registered by type or factory). A provider instance passed to
    /// <c>AddProvider(instance)</c> is not created by that container, so the returned factory
    /// also disposes those instances on shutdown. This keeps ZLogger file sinks and other user
    /// providers flushed.
    /// </remarks>
    internal static ILoggerFactory CreateLoggerFactory(Action<ILoggingBuilder>? configure)
    {
        HashSet<ILoggerProvider> providerInstances = new(ReferenceEqualityComparer.Instance);

        ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(DefaultMinimumLevel());

            if (configure is not null)
            {
                configure(builder);
            }
            else
            {
                builder.AddZLoggerConsole(options =>
                {
                    options.UsePlainTextFormatter(formatter =>
                    {
                        // Local time with milliseconds, level, and category. No ANSI colors:
                        // the prefix stays readable when output is redirected or captured.
                        formatter.SetPrefixFormatter(
                            $"{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}: ",
                            static (in MessageTemplate template, in LogInfo info) => template.Format(
                                info.Timestamp.Local,
                                info.LogLevel,
                                info.Category.Name));
                    });
                });
            }

            foreach (ServiceDescriptor descriptor in builder.Services)
            {
                if (descriptor.ServiceType == typeof(ILoggerProvider)
                    && descriptor.ImplementationInstance is ILoggerProvider provider)
                {
                    providerInstances.Add(provider);
                }
            }
        });

        return new OwnedLoggerFactory(factory, providerInstances);
    }

    private static LogLevel DefaultMinimumLevel()
    {
#if DEBUG
        return LogLevel.Debug;
#else
        return LogLevel.Information;
#endif
    }

    /// <summary>
    /// Owns the inner factory and the provider instances that the logging container does not
    /// create and therefore does not dispose.
    /// </summary>
    private sealed class OwnedLoggerFactory(ILoggerFactory inner, IEnumerable<ILoggerProvider> providers)
        : ILoggerFactory
    {
        private readonly ILoggerProvider[] _providers = providers.ToArray();
        private int _disposed;

        public ILogger CreateLogger(string categoryName) => inner.CreateLogger(categoryName);

        public void AddProvider(ILoggerProvider provider) => inner.AddProvider(provider);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                inner.Dispose();
            }
            finally
            {
                foreach (ILoggerProvider provider in _providers)
                {
                    (provider as IDisposable)?.Dispose();
                }
            }
        }
    }
}
