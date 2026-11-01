using Bork2d.Engine.Scene;
using Bork2d.Engine.Time;
using Bork2d.Render;
using Bork2d.Render.Handoff;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime.Input;
using Bork2d.Runtime.Sdl;
using Bork2d.Runtime.Window;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The windowed <see cref="IRenderedRunHost"/>: owns the SDL platform, the window/input proxies, and
/// the minimized-only tick cap.
/// </summary>
/// <remarks>
/// The shared rendered-run lifecycle drives this host. The platform is constructed here but starts
/// only when the lifecycle has started assets, so a missing/malformed pack never creates a window
/// (D-M3-01). A platform start failure is surfaced through <see cref="StartPlatform"/>; the
/// lifecycle then stops assets, detaches the proxies, and rethrows before <c>Setup</c>. A window
/// close or <c>SDL_QUIT</c> observed by the platform is part of the lifecycle's stop predicate.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class WindowedRenderedRunHost<TLayer> : IRenderedRunHost, IDisposable
    where TLayer : struct, Enum
{
    /// <summary>The minimized-only windowed tick cap (Q8=F4: at most 30 ticks per second).</summary>
    private const int MinimizedTickCapPerSecond = 30;

    private static readonly double _minimizedPeriodSeconds = 1d / MinimizedTickCapPerSecond;

    private readonly WindowProxy _windowProxy;
    private readonly InputProxy _inputProxy;
    private readonly SdlWindowPlatform _platform;
    private readonly RendererComposition<TLayer> _rendererComposition;
    private readonly ISchedulerClock _schedulerClock;
    private readonly IFrameTime? _frameTime;
    private readonly ILogger _logger;
    private bool _minimizedCapActive;
    private double _minimizedTickStart;

    /// <summary>Initializes a new instance of the <see cref="WindowedRenderedRunHost{TLayer}"/> class.</summary>
    /// <param name="engine">The engine the host's renderer backs.</param>
    /// <param name="options">The builder-derived host and window options.</param>
    /// <param name="windowProxy">The unattached window proxy.</param>
    /// <param name="inputProxy">The unattached input proxy.</param>
    /// <param name="frameTime">The frame clock, or <see langword="null"/> when the time is unsuited.</param>
    /// <param name="schedulerClock">The clock used only for the minimized tick cap.</param>
    /// <param name="loggerFactory">The app-owned logger factory.</param>
    /// <param name="logger">The <c>Bork2d.Runtime</c> logger.</param>
    internal WindowedRenderedRunHost(
        GameEngine<TLayer> engine,
        Bork2dAppOptions<TLayer> options,
        WindowProxy windowProxy,
        InputProxy inputProxy,
        IFrameTime? frameTime,
        ISchedulerClock schedulerClock,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(windowProxy);
        ArgumentNullException.ThrowIfNull(inputProxy);
        ArgumentNullException.ThrowIfNull(schedulerClock);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _windowProxy = windowProxy;
        _inputProxy = inputProxy;
        _frameTime = frameTime;
        _schedulerClock = schedulerClock;
        _logger = logger;
        _rendererComposition = new RendererComposition<TLayer>(engine, options, loggerFactory);

        // Disposed on every path, including a startup failure: Start already joins the OS thread
        // and releases SDL there, and Dispose releases the platform's wait signals (its Shutdown is
        // idempotent, so the successful path keeps its explicit Shutdown ordering).
        _platform = new SdlWindowPlatform(
            CreatePlatformOptions(options),
            options.SdlApiFactory ?? CreateDefaultSdlApi,
            options.VulkanInstanceFactory ?? new VulkanInstanceFactory(),
            loggerFactory);
    }

    /// <inheritdoc/>
    public bool IsPlatformExitRequested => _platform.ExitRequested;

    /// <inheritdoc/>
    public void StartPlatform() => _platform.Start();

    /// <inheritdoc/>
    public IRenderer CreateRenderer() => _rendererComposition.Create(RendererMode.Windowed, _platform);

    /// <inheritdoc/>
    public void AttachRenderer(IRenderer renderer) => _platform.AttachRenderer(renderer);

    /// <inheritdoc/>
    public void AttachProxies()
    {
        _windowProxy.Attach(_platform);
        _inputProxy.Attach(_platform);
    }

    /// <inheritdoc/>
    public IFrameTime RequireFrameClock()
        => _frameTime
            ?? throw new InvalidOperationException("A windowed run requires a frame clock.");

    /// <inheritdoc/>
    public void PrepareTick(IFrameTime frameTime)
    {
        ArgumentNullException.ThrowIfNull(frameTime);
        _inputProxy.Freeze();
        frameTime.NewFrame();
        _inputProxy.DispatchText();
    }

    /// <inheritdoc/>
    public void AfterTick() => EnforceMinimizedTickCap();

    /// <inheritdoc/>
    public void ShutdownPlatform() => _platform.Shutdown();

    /// <inheritdoc/>
    public void DetachProxies()
    {
        _windowProxy.Detach();
        _inputProxy.Detach();
    }

    /// <inheritdoc/>
    public void LogStopped(IRenderer? renderer, long ticks, double elapsedSeconds)
    {
        var framebuffer = _platform.State.FramebufferSize;
        SnapshotRingCounters counters = renderer?.SnapshotCounters ?? default;
        _logger.LogInformation(
            "Bork2d windowed loop stopped: reason={Reason}, ticks={Ticks}, "
                + "elapsedSeconds={ElapsedSeconds:0.###}, backpressureWaits={BackpressureWaits}, "
                + "maxLiveSnapshots={MaxLiveSnapshots}, suspendedConsumptions={SuspendedConsumptions}, "
                + "finalFramebuffer={FramebufferWidth}x{FramebufferHeight}",
            _platform.StopReason,
            ticks,
            elapsedSeconds,
            counters.BackpressureWaits,
            counters.MaxLive,
            renderer?.SuspendedConsumptions ?? 0,
            framebuffer.Width,
            framebuffer.Height);
    }

    /// <inheritdoc/>
    public void ThrowFailures(
        Exception? setupFailure,
        Exception? loopFailure,
        Exception? rendererFailure,
        Exception? teardownFailure)
    {
        Exception? platformFatal = _platform.FatalException;

        if (setupFailure is not null)
        {
            // A Setup exception wins over a renderer failure (D-M6-14); a concurrent platform fatal
            // keeps the M2 aggregate behavior.
            var setupErrors = new List<Exception>(capacity: 2) { setupFailure };
            if (platformFatal is not null && !ReferenceEquals(platformFatal, setupFailure))
            {
                setupErrors.Add(platformFatal);
            }

            RunFailures.Throw(
                setupErrors,
                "The windowed run failed during Setup; the original exceptions are inner.");
            return;
        }

        var errors = new List<Exception>(capacity: 4);
        RunFailures.Add(errors, rendererFailure);
        RunFailures.Add(errors, loopFailure);
        RunFailures.Add(errors, platformFatal);
        RunFailures.Add(errors, teardownFailure);
        RunFailures.Throw(errors, "The windowed run failed; the original exceptions are inner.");
    }

    /// <summary>Stops the platform if the run did not and disposes its wait signals.</summary>
    public void Dispose() => _platform.Dispose();

    /// <summary>
    /// Applies the minimized-only 30 Hz windowed tick cap (Q8/F4) after a completed/published tick.
    /// </summary>
    /// <remarks>
    /// The grid starts when the window is first observed minimized and is discarded on restore.
    /// Every minimized tick start keeps at least one thirtieth of a second from the previous one,
    /// using the scheduler's pure sleep; a loop that is already behind does not sleep, so the cap
    /// never becomes a catch-up burst or a general frame limiter.
    /// </remarks>
    private void EnforceMinimizedTickCap()
    {
        if (!_windowProxy.IsMinimized)
        {
            _minimizedCapActive = false;
            return;
        }

        var now = _schedulerClock.NowSeconds;
        if (!_minimizedCapActive)
        {
            _minimizedCapActive = true;
            _minimizedTickStart = now;
            return;
        }

        var remaining = _minimizedPeriodSeconds - (now - _minimizedTickStart);
        if (remaining > 0)
        {
            _schedulerClock.Sleep(TimeSpan.FromSeconds(remaining));
        }

        _minimizedTickStart = _schedulerClock.NowSeconds;
    }

    private static ISdlApi CreateDefaultSdlApi() => new SilkSdlApi();

    private static WindowPlatformOptions CreatePlatformOptions(Bork2dAppOptions<TLayer> options)
        => new(
            options.Title,
            options.Width,
            options.Height,
            options.Resizable,
            options.Fullscreen,
            options.VSync,
            options.Validation);
}
