using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Bork2d.Api;
using Bork2d.Assets;
using Bork2d.Common;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime;

/// <summary>
/// Owns the generic engine, game, and asset service for one built app and runs the configured host
/// loop.
/// </summary>
/// <remarks>
/// <para>
/// Windowed apps run an uncapped <c>Setup</c> → <c>Tick</c>… → <c>Teardown</c> loop on the <c>Run</c>
/// caller thread until <see cref="IEngine{TLayer}.Exit"/>, a window close/<c>SDL_QUIT</c>, or a fatal
/// platform error. The loop never calls SDL: it freezes input and advances the clock before each
/// tick, dispatches text, and calls the game.
/// </para>
/// <para>
/// Headless and offscreen apps keep the M1 one-tick sequence on the caller thread with no platform
/// and no clock advance. Both drivers share the M1 error precedence: a <c>Setup</c> failure runs no
/// <c>Teardown</c>; a <c>Tick</c> (or text-handler) failure still tears down; two failures are
/// aggregated; a window start failure happens before <c>Setup</c> and tears nothing down.
/// </para>
/// <para>
/// The asset service is app-owned: the constructed service is unstarted until <c>Run</c>, which
/// starts it before the platform/`Setup` and stops it after <c>Teardown</c> on every path.
/// <c>Dispose</c> disposes an unstarted or stopped service and then the logger factory, so the
/// service can still log its own shutdown.
/// </para>
/// <para>
/// The runtime detaches the window/input proxies only after the platform has shut down and the
/// asset service has stopped, so retained references throw <see cref="ObjectDisposedException"/> per
/// D-M2-17 while <c>Teardown</c> can still use the live window, input, and assets.
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class AppRuntime<TLayer> : IAppRuntime where TLayer : struct, Enum
{
    private readonly Engine<TLayer> _engine;
    private readonly IGame<TLayer> _game;
    private readonly IFrameTime _time;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly AssetService _assetService;
    private readonly Bork2dAppOptions<TLayer> _options;
    private readonly WindowProxy? _windowProxy;
    private readonly InputProxy? _inputProxy;
    private readonly Func<ISdlApi> _sdlApiFactory;
    private bool _disposed;

    internal AppRuntime(
        Engine<TLayer> engine,
        IGame<TLayer> game,
        IFrameTime time,
        ILoggerFactory loggerFactory,
        AssetService assetService,
        Bork2dAppOptions<TLayer> options,
        WindowProxy? windowProxy = null,
        InputProxy? inputProxy = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(assetService);
        ArgumentNullException.ThrowIfNull(options);

        _engine = engine;
        _game = game;
        _time = time;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(Bork2dLogging.RuntimeCategory);
        _assetService = assetService;
        _options = options;
        _windowProxy = windowProxy;
        _inputProxy = inputProxy;
        _sdlApiFactory = options.SdlApiFactory ?? CreateDefaultSdlApi;
    }

    public ISceneRoot Root => _engine.Root;

    public ILoggerFactory LoggerFactory => _loggerFactory;

    public Action<ILoggingBuilder>? ConfigureLogging => _options.ConfigureLogging;

    public void Run()
    {
        try
        {
            if (_options.Mode == Bork2dAppMode.Windowed)
            {
                RunWindowed();
            }
            else
            {
                RunOneTick();
            }
        }
        finally
        {
            // Post-shutdown Exit() is a no-op (D-M2-17); this also covers a failed run.
            _engine.MarkStopped();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _engine.MarkStopped();
        try
        {
            // Detach before disposing the logger so a retained IWindow/IInput fails with
            // ObjectDisposedException instead of touching a dead platform (D-M2-17).
            _windowProxy?.Detach();
            _inputProxy?.Detach();
        }
        finally
        {
            try
            {
                // Disposes an unstarted service or stops a started one (D-M3-14); idempotent.
                _assetService.Dispose();
            }
            finally
            {
                if (_loggerFactory is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }

    private static ISdlApi CreateDefaultSdlApi() => new SilkSdlApi();

    /// <summary>
    /// The M1 one-tick sequence: start assets, <c>Setup</c>, exactly one <c>Tick</c>,
    /// <c>Teardown</c>, stop assets.
    /// </summary>
    /// <remarks>
    /// The clock is not advanced and no platform exists. <c>Setup</c> is intentionally outside the
    /// tick/teardown pair: if it throws there is nothing to tear down and the original exception must
    /// reach the caller. The asset service is stopped on every path after a successful start,
    /// including a <c>Setup</c> failure; a failed start has already cleaned its own partial state up.
    /// </remarks>
    private void RunOneTick()
    {
        _assetService.Start();
        try
        {
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
        finally
        {
            _assetService.Stop();
        }
    }

    /// <summary>
    /// The windowed driver: start assets, start the platform, attach the proxies, run the uncapped
    /// loop, tear down the game while the window and assets are still alive, then shut the platform
    /// and the asset service down.
    /// </summary>
    private void RunWindowed()
    {
        var windowProxy = _windowProxy
            ?? throw new InvalidOperationException("A windowed runtime requires a window proxy.");
        var inputProxy = _inputProxy
            ?? throw new InvalidOperationException("A windowed runtime requires an input proxy.");

        // The base pack is validated before the window exists, so a missing/malformed pack produces
        // no visible window and no Setup/Teardown (D-M3-01). The service cleans up its own partial
        // state, and the proxies still become disposed objects for retained references (D-M2-17).
        try
        {
            _assetService.Start();
        }
        catch
        {
            windowProxy.Detach();
            inputProxy.Detach();
            throw;
        }

        // Disposed on every path, including a startup failure: Start already joins the OS thread
        // and releases SDL there, and Dispose releases the platform's wait signals (its Shutdown is
        // idempotent, so the successful path keeps its explicit Shutdown ordering).
        using var platform = new SdlWindowPlatform(
            CreatePlatformOptions(),
            _sdlApiFactory,
            _logger);

        try
        {
            // Start blocks until the window exists or startup failed; on failure it has already
            // joined the OS thread and released SDL.
            platform.Start();
        }
        catch
        {
            // The platform never ran, but the assets did: stop them before Setup would have run.
            // The proxies must still be disposed for D-M2-17.
            _assetService.Stop();
            windowProxy.Detach();
            inputProxy.Detach();
            throw;
        }

        Exception? gameFailure = null;
        Exception? teardownFailure = null;
        var setupSucceeded = false;
        long ticks = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Attach before Setup so the game sees the live window/input immediately.
            windowProxy.Attach(platform);
            inputProxy.Attach(platform);

            try
            {
                // Exit/close received during startup mean no Setup and therefore no Tick/Teardown.
                if (!_engine.ExitRequested && !platform.ExitRequested)
                {
                    _game.Setup(_engine);
                    setupSucceeded = true;
                }

                while (!_engine.ExitRequested && !platform.ExitRequested)
                {
                    inputProxy.Freeze();
                    _time.NewFrame();
                    inputProxy.DispatchText();
                    _game.Tick();
                    ticks++;
                }
            }
            catch (Exception exception)
            {
                gameFailure = exception;
            }
            finally
            {
                if (setupSucceeded)
                {
                    // The window, input, and assets are still alive here, so the game may use them
                    // for best-effort cleanup; shutdown follows immediately afterwards.
                    try
                    {
                        _game.Teardown();
                    }
                    catch (Exception exception)
                    {
                        teardownFailure = exception;
                    }
                }
            }
        }
        finally
        {
            stopwatch.Stop();
            platform.Shutdown();
            _assetService.Stop();
            windowProxy.Detach();
            inputProxy.Detach();
        }

        var fatalException = platform.FatalException;
        LogLoopStopped(platform, ticks, stopwatch.Elapsed.TotalSeconds);
        ThrowFailures(gameFailure, fatalException, teardownFailure);
    }

    private WindowPlatformOptions CreatePlatformOptions()
        => new(
            _options.Title,
            _options.Width,
            _options.Height,
            _options.Resizable,
            _options.Fullscreen,
            _options.VSync);

    private void LogLoopStopped(SdlWindowPlatform platform, long ticks, double elapsedSeconds)
    {
        var framebuffer = platform.State.FramebufferSize;
        _logger.LogInformation(
            "Bork2d windowed loop stopped: reason={Reason}, ticks={Ticks}, "
                + "elapsedSeconds={ElapsedSeconds:0.###}, finalFramebuffer={FramebufferWidth}x{FramebufferHeight}",
            platform.StopReason,
            ticks,
            elapsedSeconds,
            framebuffer.Width,
            framebuffer.Height);
    }

    private static void ThrowFailures(
        Exception? gameFailure,
        Exception? fatalException,
        Exception? teardownFailure)
    {
        var errors = new List<Exception>(capacity: 3);
        if (gameFailure is not null)
        {
            errors.Add(gameFailure);
        }

        if (fatalException is not null)
        {
            errors.Add(fatalException);
        }

        if (teardownFailure is not null)
        {
            errors.Add(teardownFailure);
        }

        if (errors.Count == 0)
        {
            return;
        }

        if (errors.Count == 1)
        {
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
            return;
        }

        throw new AggregateException(
            "The windowed run failed; the original exceptions are inner.",
            errors);
    }
}
