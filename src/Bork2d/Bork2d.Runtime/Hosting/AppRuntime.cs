using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Engine.Assets.Loading;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Scene;
using Bork2d.Engine.Time;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Window;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// Owns the generic engine, game, and asset service for one built app and runs the configured host
/// loop.
/// </summary>
/// <remarks>
/// <para>
/// Windowed and offscreen runs share one rendered-run lifecycle (<see cref="RenderedRun"/>):
/// start assets; start the mode platform; create and start the renderer without waiting; install the
/// exit wake callback; attach the renderer and proxies; run <c>Setup</c>; wait for readiness before
/// the first tick; run a backpressured <c>Tick</c> loop until <see cref="IEngine{TLayer}.Exit"/>, a
/// window close/<c>SDL_QUIT</c>, a renderer fatal error, or a fatal platform error; run
/// <c>Teardown</c>; stop and join the renderer before platform shutdown; stop assets; and detach the
/// proxies. Each tick acquires a snapshot slot, freezes input, advances time, dispatches text, runs
/// <c>IGame.Tick</c>, captures the engine snapshot, and publishes it (M7 WP-4). The windowed and
/// offscreen hosts supply only the mode hooks: the SDL window/platform/proxies and the minimized cap
/// for windowed, and the fixed target without a platform for offscreen. Headless runs a separate
/// paced loop until <see cref="IEngine{TLayer}.Exit"/> with a fully synthesized clock and never
/// constructs a renderer or platform and never loads Vulkan (Q11=K1, Q12=L2).
/// </para>
/// <para>
/// Error precedence follows the M2/M6 rules: a platform start failure happens before <c>Setup</c>;
/// a renderer factory/start/readiness failure still lets <c>Setup</c> finish, runs zero ticks,
/// attempts <c>Teardown</c>, joins the renderer, and is rethrown (D-M6-14); a <c>Setup</c> failure
/// wins over a renderer failure and runs no <c>Teardown</c>; a <c>Tick</c>/capture/text/
/// <c>Teardown</c> failure keeps the M2 precedence. A renderer fatal after the first tick stops the
/// loop at the next boundary, and a fatal observed while a tick is blocked waiting for a slot
/// starts no further tick (Q9=I1). Renderer cleanup failures during normal shutdown are logged and
/// do not change the run's outcome (D-M6-27).
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
    private readonly GameEngine<TLayer> _engine;
    private readonly IGame<TLayer> _game;
    private readonly IFrameTime? _frameTime;
    private readonly IHeadlessTime? _headlessTime;
    private readonly ISchedulerClock _schedulerClock;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly AssetService _assetService;
    private readonly Bork2dAppOptions<TLayer> _options;
    private readonly WindowProxy? _windowProxy;
    private readonly InputProxy? _inputProxy;
    private bool _disposed;

    internal AppRuntime(
        GameEngine<TLayer> engine,
        IGame<TLayer> game,
        ITime time,
        ILoggerFactory loggerFactory,
        AssetService assetService,
        Bork2dAppOptions<TLayer> options,
        WindowProxy? windowProxy = null,
        InputProxy? inputProxy = null,
        ISchedulerClock? schedulerClock = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(assetService);
        ArgumentNullException.ThrowIfNull(options);

        _engine = engine;
        _game = game;
        _frameTime = time as IFrameTime;
        _headlessTime = time as IHeadlessTime;
        _schedulerClock = schedulerClock ?? new SystemSchedulerClock();
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(Bork2dLogging.RuntimeCategory);
        _assetService = assetService;
        _options = options;
        _windowProxy = windowProxy;
        _inputProxy = inputProxy;
    }

    public ISceneRoot Root => _engine.Root;

    public ILoggerFactory LoggerFactory => _loggerFactory;

    public Action<ILoggingBuilder>? ConfigureLogging => _options.ConfigureLogging;

    public void Run()
    {
        try
        {
            switch (_options.Mode)
            {
                case Bork2dAppMode.Windowed:
                    RunWindowed();
                    break;

                case Bork2dAppMode.Offscreen:
                    RunOffscreen();
                    break;

                default:
                    RunHeadless();
                    break;
            }
        }
        finally
        {
            // Post-shutdown Exit() is a no-op (D-M2-17); this also covers a failed run and clears
            // the renderer wake callback. CaptureFrameAsync becomes ObjectDisposedException after
            // the run, while the last published RenderStats stay readable (M8 §6.9).
            _engine.MarkStopped();
            _engine.MarkCaptureDisposed();
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
        _engine.MarkCaptureDisposed();
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

    /// <summary>
    /// The windowed driver: runs the shared rendered-run lifecycle with the windowed host hooks
    /// (SDL platform, window/input proxies, and minimized tick cap).
    /// </summary>
    private void RunWindowed()
    {
        var windowProxy = _windowProxy
            ?? throw new InvalidOperationException("A windowed runtime requires a window proxy.");
        var inputProxy = _inputProxy
            ?? throw new InvalidOperationException("A windowed runtime requires an input proxy.");

        // The host owns the platform and disposes it on every path, including a startup failure:
        // Start already joins the OS thread and releases SDL there, and Dispose releases the
        // platform's wait signals (its Shutdown is idempotent, so the successful path keeps its
        // explicit Shutdown ordering). Platform construction touches no SDL; the shared lifecycle
        // starts assets before StartPlatform creates the window.
        using var host = new WindowedRenderedRunHost<TLayer>(
            _engine,
            _options,
            windowProxy,
            inputProxy,
            _frameTime,
            _schedulerClock,
            _loggerFactory,
            _logger);

        RenderedRun.Run(_engine, _game, _assetService, host, _logger);
    }

    /// <summary>
    /// The offscreen driver: runs the shared rendered-run lifecycle with the offscreen host hooks
    /// (fixed target, no platform, no input, no close path).
    /// </summary>
    private void RunOffscreen()
    {
        var host = new OffscreenRenderedRunHost<TLayer>(
            _engine,
            _options,
            _frameTime,
            _loggerFactory,
            _logger);

        RenderedRun.Run(_engine, _game, _assetService, host, _logger);
    }

    /// <summary>
    /// The headless driver (Q11=K1, Q12=L2, Q13=M4/F1): start assets, <c>Setup</c>, a paced
    /// <c>Tick</c> loop until <c>Exit</c>, <c>Teardown</c> once, stop assets, with no platform,
    /// renderer, ring, capture, SDL, or Vulkan loader.
    /// </summary>
    /// <remarks>
    /// The pacer captures a monotonic start and makes tick <c>N</c> due at
    /// <c>start + N * period</c>: tick <c>0</c> runs immediately, and before tick <c>N + 1</c> the
    /// loop sleeps the positive remainder with the scheduler. When the loop is behind it runs every
    /// missed tick back-to-back with no sleep (F8, unbounded); the synthesized clock advances one
    /// exact step per tick, so it catches up with the system schedule. A pacing sleep is not
    /// interrupted, so <c>Exit</c> latency is bounded by one period.
    /// </remarks>
    private void RunHeadless()
    {
        var ticksPerSecond = _options.HeadlessTicksPerSecond;
        IHeadlessTime headlessTime = _headlessTime
            ?? throw new InvalidOperationException("A headless run requires the synthesized clock.");

        // A headless run never creates a renderer, so CaptureFrameAsync throws NotSupportedException
        // for the duration (M8 §6.9). Installing this at Run start leaves the pre-Run state as
        // InvalidOperationException.
        _engine.SetCaptureUnsupported();

        _assetService.Start();
        try
        {
            Exception? setupFailure = null;
            Exception? tickFailure = null;
            Exception? teardownFailure = null;
            var setupSucceeded = false;
            long ticks = 0;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                try
                {
                    _game.Setup(_engine);
                    setupSucceeded = true;
                }
                catch (Exception exception)
                {
                    setupFailure = exception;
                }

                if (setupFailure is null && !_engine.ExitRequested)
                {
                    var period = 1d / ticksPerSecond;
                    var start = _schedulerClock.NowSeconds;
                    while (!_engine.ExitRequested)
                    {
                        var due = start + (ticks * period);
                        var now = _schedulerClock.NowSeconds;
                        if (due > now)
                        {
                            _schedulerClock.Sleep(TimeSpan.FromSeconds(due - now));
                        }

                        // A sleep is not interrupted (F1): an Exit requested while sleeping stops
                        // the loop here, before the next tick.
                        if (_engine.ExitRequested)
                        {
                            break;
                        }

                        try
                        {
                            headlessTime.SetTick(ticks, ticksPerSecond);
                            _game.Tick();
                            ticks++;
                        }
                        catch (Exception exception)
                        {
                            tickFailure = exception;
                            break;
                        }
                    }
                }
            }
            finally
            {
                stopwatch.Stop();
                if (setupSucceeded)
                {
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

            LogHeadlessLoopStopped(
                setupFailure,
                tickFailure,
                teardownFailure,
                ticks,
                stopwatch.Elapsed.TotalSeconds,
                ticksPerSecond);

            ThrowHeadlessFailures(setupFailure, tickFailure, teardownFailure);
        }
        finally
        {
            _assetService.Stop();
        }
    }

    private void LogHeadlessLoopStopped(
        Exception? setupFailure,
        Exception? tickFailure,
        Exception? teardownFailure,
        long ticks,
        double elapsedSeconds,
        int ticksPerSecond)
    {
        var reason = setupFailure is not null || tickFailure is not null || teardownFailure is not null
            ? "Error"
            : _engine.ExitRequested ? "Exit" : "Stop";
        _logger.LogInformation(
            "Bork2d headless loop stopped: reason={Reason}, ticks={Ticks}, "
                + "elapsedSeconds={ElapsedSeconds:0.###}, ticksPerSecond={TicksPerSecond}",
            reason,
            ticks,
            elapsedSeconds,
            ticksPerSecond);
    }

    private static void ThrowHeadlessFailures(
        Exception? setupFailure,
        Exception? tickFailure,
        Exception? teardownFailure)
    {
        if (setupFailure is not null)
        {
            RunFailures.Throw(setupFailure);
            return;
        }

        var errors = new List<Exception>(capacity: 2);
        RunFailures.Add(errors, tickFailure);
        RunFailures.Add(errors, teardownFailure);
        RunFailures.Throw(errors, "The headless run failed; the original exceptions are inner.");
    }
}
