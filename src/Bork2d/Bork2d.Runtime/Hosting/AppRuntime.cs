using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Bork2d.Engine;
using Bork2d.Engine.Assets.Loading;
using Bork2d.Engine.Scene;
using Bork2d.Engine.Time;
using Bork2d.Render;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// Owns the generic engine, game, and asset service for one built app and runs the configured host
/// loop.
/// </summary>
/// <remarks>
/// <para>
/// Windowed apps start the renderer thread without waiting for readiness, run <c>Setup</c>
/// immediately (D-M6-06), wait for readiness before the first tick, then run an uncapped
/// <c>Tick</c> loop until <see cref="IEngine{TLayer}.Exit"/>, a window close/<c>SDL_QUIT</c>, a
/// renderer fatal error, or a fatal platform error. Offscreen apps start the renderer the same way
/// and run the windowed-shaped loop without a window, input, or SDL until
/// <see cref="IEngine{TLayer}.Exit"/> (D-M6-07). Headless keeps the M1 one-tick sequence and never
/// constructs a renderer or platform and never loads Vulkan.
/// </para>
/// <para>
/// Error precedence follows the M2/M6 rules: a platform start failure happens before <c>Setup</c>;
/// a renderer factory/start/readiness failure still lets <c>Setup</c> finish, runs zero ticks,
/// attempts <c>Teardown</c>, joins the renderer, and is rethrown (D-M6-14); a <c>Setup</c> failure
/// wins over a renderer failure and runs no <c>Teardown</c>; a <c>Tick</c>/text/<c>Teardown</c>
/// failure keeps the M2 precedence. A renderer fatal after the first tick stops the loop at the next
/// boundary. Renderer cleanup failures during normal shutdown are logged and do not change the
/// run's outcome (D-M6-27).
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
        GameEngine<TLayer> engine,
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
            switch (_options.Mode)
            {
                case Bork2dAppMode.Windowed:
                    RunWindowed();
                    break;

                case Bork2dAppMode.Offscreen:
                    RunOffscreen();
                    break;

                default:
                    RunOneTick();
                    break;
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
    /// The clock is not advanced, <c>Exit</c> is ignored, and no platform or renderer exists.
    /// <c>Setup</c> is intentionally outside the tick/teardown pair: if it throws there is nothing to
    /// tear down and the original exception must reach the caller. The asset service is stopped on
    /// every path after a successful start, including a <c>Setup</c> failure; a failed start has
    /// already cleaned its own partial state up.
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
    /// The windowed driver: start assets, start the platform (SDL window/instance/surface), start
    /// the renderer without waiting, run <c>Setup</c>, wait for renderer readiness before the first
    /// tick, run the uncapped loop, tear the game down, then join the renderer before the platform
    /// destroys the surface/instance/window (D-M6-09).
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
            _options.VulkanInstanceFactory ?? new VulkanInstanceFactory(),
            _loggerFactory);

        try
        {
            // Start blocks until the window, instance, and surface exist or startup failed; on
            // failure it has already joined the OS thread, destroyed the partial Vulkan objects,
            // and released SDL.
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

        IRenderer? renderer = null;
        Exception? setupFailure = null;
        Exception? loopFailure = null;
        Exception? teardownFailure = null;
        Exception? rendererFailure = null;
        var setupSucceeded = false;
        long ticks = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // D-M6-06: Start returns without waiting for device/swapchain readiness. A synchronous
            // factory/start failure is recorded but never prevents Setup from running.
            try
            {
                renderer = CreateRenderer(RendererMode.Windowed, platform);
                renderer.Start();
            }
            catch (Exception exception)
            {
                rendererFailure = NormalizeRendererFailure(exception);
            }

            // Attach before Setup so the game sees the live window/input immediately. The platform
            // posts the confirmed state (extent/fullscreen/minimized) from its OS thread.
            if (renderer is not null)
            {
                platform.AttachRenderer(renderer);
            }

            windowProxy.Attach(platform);
            inputProxy.Attach(platform);

            try
            {
                // Exit/close received during startup mean no Setup and therefore no Tick/Teardown.
                if (!_engine.ExitRequested && !platform.ExitRequested)
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
                }

                // D-M6-06: the first Tick waits for readiness; Exit/close during Setup skips the
                // wait. Setup is never interrupted by, and never skipped because of, renderer state.
                if (setupFailure is null
                    && setupSucceeded
                    && rendererFailure is null
                    && !_engine.ExitRequested
                    && !platform.ExitRequested)
                {
                    renderer!.WaitForReady();
                    if (renderer.FatalException is { } readinessFatal)
                    {
                        rendererFailure = NormalizeRendererFailure(readinessFatal);
                    }
                }

                if (setupFailure is null
                    && setupSucceeded
                    && rendererFailure is null
                    && !_engine.ExitRequested
                    && !platform.ExitRequested)
                {
                    while (!_engine.ExitRequested && !platform.ExitRequested)
                    {
                        if (renderer!.FatalException is { } fatal)
                        {
                            rendererFailure = NormalizeRendererFailure(fatal);
                            break;
                        }

                        inputProxy.Freeze();
                        _time.NewFrame();
                        inputProxy.DispatchText();
                        _game.Tick();
                        ticks++;
                    }
                }
            }
            catch (Exception exception)
            {
                loopFailure = exception;
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

            // D-M6-09: the renderer is stopped and joined before the platform destroys the surface,
            // instance, and window. A renderer cleanup failure is logged and does not change the
            // run's outcome (D-M6-27).
            StopAndJoinRenderer(renderer);
            if (rendererFailure is null && renderer?.FatalException is { } lateFatal)
            {
                rendererFailure = NormalizeRendererFailure(lateFatal);
            }

            platform.Shutdown();
            _assetService.Stop();
            windowProxy.Detach();
            inputProxy.Detach();
        }

        Exception? platformFatal = platform.FatalException;
        LogLoopStopped(platform, ticks, stopwatch.Elapsed.TotalSeconds);
        ThrowWindowedFailures(
            setupFailure,
            loopFailure,
            rendererFailure,
            platformFatal,
            teardownFailure);
    }

    /// <summary>
    /// The offscreen driver: start assets, start the renderer without waiting, run <c>Setup</c>,
    /// wait for readiness before the first tick, run the windowed-shaped loop without window/input
    /// until <c>Exit</c> (D-M6-07), tear down, then stop/join the renderer.
    /// </summary>
    private void RunOffscreen()
    {
        _assetService.Start();

        IRenderer? renderer = null;
        Exception? setupFailure = null;
        Exception? loopFailure = null;
        Exception? teardownFailure = null;
        Exception? rendererFailure = null;
        var setupSucceeded = false;
        long ticks = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            try
            {
                renderer = CreateRenderer(RendererMode.Offscreen, platform: null);
                renderer.Start();
            }
            catch (Exception exception)
            {
                rendererFailure = NormalizeRendererFailure(exception);
            }

            // D-M6-06/D-M6-14: Setup runs immediately (even when the renderer already failed) and
            // may call Exit() without the host having waited for readiness.
            if (!_engine.ExitRequested)
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
            }

            if (setupFailure is null
                && setupSucceeded
                && rendererFailure is null
                && !_engine.ExitRequested)
            {
                renderer!.WaitForReady();
                if (renderer.FatalException is { } readinessFatal)
                {
                    rendererFailure = NormalizeRendererFailure(readinessFatal);
                }
            }

            if (setupFailure is null
                && setupSucceeded
                && rendererFailure is null
                && !_engine.ExitRequested)
            {
                try
                {
                    while (!_engine.ExitRequested)
                    {
                        if (renderer!.FatalException is { } fatal)
                        {
                            rendererFailure = NormalizeRendererFailure(fatal);
                            break;
                        }

                        _time.NewFrame();
                        _game.Tick();
                        ticks++;
                    }
                }
                catch (Exception exception)
                {
                    loopFailure = exception;
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

            StopAndJoinRenderer(renderer);
            if (rendererFailure is null && renderer?.FatalException is { } lateFatal)
            {
                rendererFailure = NormalizeRendererFailure(lateFatal);
            }

            _assetService.Stop();
        }

        _logger.LogInformation(
            "Bork2d offscreen loop stopped: reason={Reason}, ticks={Ticks}, "
                + "elapsedSeconds={ElapsedSeconds:0.###}",
            _engine.ExitRequested ? "Exit" : rendererFailure is not null ? "Error" : "Stop",
            ticks,
            stopwatch.Elapsed.TotalSeconds);

        ThrowOffscreenFailures(setupFailure, loopFailure, rendererFailure, teardownFailure);
    }

    private IRenderer CreateRenderer(RendererMode mode, SdlWindowPlatform? platform)
    {
        VulkanWindowSnapshot? snapshot = platform?.VulkanWindow;
        if (mode == RendererMode.Windowed && snapshot is null)
        {
            throw new Bork2dException(
                "The windowed platform did not publish a Vulkan instance and surface.");
        }

        IVulkanInstanceFactory instanceFactory =
            _options.VulkanInstanceFactory ?? new VulkanInstanceFactory();
        IRendererFactory factory = _options.RendererFactory
            ?? new VulkanRendererFactory(instanceFactory);

        int width;
        int height;
        if (mode == RendererMode.Windowed)
        {
            VulkanWindowSnapshot window = snapshot!;
            width = window.DrawableSize.Width;
            height = window.DrawableSize.Height;
        }
        else
        {
            width = _options.OffscreenWidth;
            height = _options.OffscreenHeight;
        }

        var startInfo = new RendererStartInfo
        {
            Mode = mode,
            Instance = snapshot?.Instance,
            InstanceHandle = snapshot?.Instance.Handle ?? 0,
            SurfaceHandle = snapshot?.SurfaceHandle ?? 0,
            Width = width,
            Height = height,
            VSync = _options.VSync,
            ValidationRequested = _options.Validation,
            LoggerFactory = _loggerFactory,
            CreateReplacementSurface = platform is null
                ? null
                : platform.CreateReplacementSurface,
        };

        return factory.Create(startInfo);
    }

    private void StopAndJoinRenderer(IRenderer? renderer)
    {
        if (renderer is null)
        {
            return;
        }

        try
        {
            renderer.RequestStop();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The renderer stop request failed during shutdown.");
        }

        try
        {
            renderer.Join();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The renderer join failed during shutdown.");
        }
    }

    private WindowPlatformOptions CreatePlatformOptions()
        => new(
            _options.Title,
            _options.Width,
            _options.Height,
            _options.Resizable,
            _options.Fullscreen,
            _options.VSync,
            _options.Validation);

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

    private static Exception NormalizeRendererFailure(Exception exception)
        => exception is Bork2dException
            ? exception
            : new Bork2dException(
                "The Vulkan renderer failed: " + exception.Message,
                exception);

    private static void ThrowWindowedFailures(
        Exception? setupFailure,
        Exception? loopFailure,
        Exception? rendererFailure,
        Exception? platformFatal,
        Exception? teardownFailure)
    {
        if (setupFailure is not null)
        {
            // A Setup exception wins over a renderer failure (D-M6-14); a concurrent platform fatal
            // keeps the M2 aggregate behavior.
            var setupErrors = new List<Exception>(capacity: 2) { setupFailure };
            if (platformFatal is not null && !ReferenceEquals(platformFatal, setupFailure))
            {
                setupErrors.Add(platformFatal);
            }

            Throw(setupErrors, "The windowed run failed during Setup; the original exceptions are inner.");
            return;
        }

        var errors = new List<Exception>(capacity: 4);
        Add(errors, rendererFailure);
        Add(errors, loopFailure);
        Add(errors, platformFatal);
        Add(errors, teardownFailure);
        Throw(errors, "The windowed run failed; the original exceptions are inner.");
    }

    private static void ThrowOffscreenFailures(
        Exception? setupFailure,
        Exception? loopFailure,
        Exception? rendererFailure,
        Exception? teardownFailure)
    {
        if (setupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(setupFailure).Throw();
            return;
        }

        var errors = new List<Exception>(capacity: 3);
        Add(errors, rendererFailure);
        Add(errors, loopFailure);
        Add(errors, teardownFailure);
        Throw(errors, "The offscreen run failed; the original exceptions are inner.");
    }

    private static void Add(List<Exception> errors, Exception? exception)
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

    private static void Throw(List<Exception> errors, string message)
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
