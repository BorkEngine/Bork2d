using System.Runtime.ExceptionServices;
using Bork2d.Api;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime;

/// <summary>
/// Owns the generic engine and game for one built app and runs the M1 smoke sequence.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class AppRuntime<TLayer> : IAppRuntime where TLayer : struct, Enum
{
    private readonly IEngine<TLayer> _engine;
    private readonly IGame<TLayer> _game;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Bork2dAppOptions<TLayer> _options;
    private bool _disposed;

    internal AppRuntime(
        IEngine<TLayer> engine,
        IGame<TLayer> game,
        ILoggerFactory loggerFactory,
        Bork2dAppOptions<TLayer> options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(options);

        _engine = engine;
        _game = game;
        _loggerFactory = loggerFactory;
        _options = options;
    }

    public ISceneRoot Root => _engine.Root;

    public ILoggerFactory LoggerFactory => _loggerFactory;

    public Action<ILoggingBuilder>? ConfigureLogging => _options.ConfigureLogging;

    public void Run()
    {
        // Setup is intentionally outside the Tick/Teardown pair: if it throws there is nothing
        // to tear down yet, and Run must surface the original exception.
        _game.Setup(_engine);

        Exception? tickException = null;
        try
        {
            _game.Tick();
        }
        catch (Exception exception)
        {
            tickException = exception;
        }

        Exception? teardownException = null;
        try
        {
            _game.Teardown();
        }
        catch (Exception exception)
        {
            teardownException = exception;
        }

        if (tickException is not null && teardownException is not null)
        {
            throw new AggregateException(
                "IGame.Tick and IGame.Teardown both threw; the original exceptions are inner.",
                tickException,
                teardownException);
        }

        if (tickException is not null)
        {
            ExceptionDispatchInfo.Capture(tickException).Throw();
        }

        if (teardownException is not null)
        {
            ExceptionDispatchInfo.Capture(teardownException).Throw();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_loggerFactory is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
