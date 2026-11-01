using Bork2d.Engine.Scene;
using Bork2d.Engine.Time;
using Bork2d.Render;
using Bork2d.Render.Handoff;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The offscreen <see cref="IRenderedRunHost"/>: renders to a fixed target without SDL, a platform,
/// input, or a close path.
/// </summary>
/// <remarks>
/// The shared rendered-run lifecycle drives this host. Platform startup/shutdown and proxy hooks are
/// no-ops; tick preparation advances the frame clock only; the run stops on <c>Exit</c> or a
/// renderer fatal and never sleeps.
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class OffscreenRenderedRunHost<TLayer> : IRenderedRunHost
    where TLayer : struct, Enum
{
    private readonly GameEngine<TLayer> _engine;
    private readonly RendererComposition<TLayer> _rendererComposition;
    private readonly IFrameTime? _frameTime;
    private readonly ILogger _logger;

    /// <summary>Initializes a new instance of the <see cref="OffscreenRenderedRunHost{TLayer}"/> class.</summary>
    /// <param name="engine">The engine the host's renderer backs.</param>
    /// <param name="options">The builder-derived host and offscreen options.</param>
    /// <param name="frameTime">The frame clock, or <see langword="null"/> when the time is unsuited.</param>
    /// <param name="loggerFactory">The app-owned logger factory.</param>
    /// <param name="logger">The <c>Bork2d.Runtime</c> logger.</param>
    internal OffscreenRenderedRunHost(
        GameEngine<TLayer> engine,
        Bork2dAppOptions<TLayer> options,
        IFrameTime? frameTime,
        ILoggerFactory loggerFactory,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _frameTime = frameTime;
        _logger = logger;
        _rendererComposition = new RendererComposition<TLayer>(engine, options, loggerFactory);
    }

    /// <inheritdoc/>
    public bool IsPlatformExitRequested => false;

    /// <inheritdoc/>
    public void StartPlatform()
    {
    }

    /// <inheritdoc/>
    public IRenderer CreateRenderer()
        => _rendererComposition.Create(RendererMode.Offscreen, platform: null);

    /// <inheritdoc/>
    public void AttachRenderer(IRenderer renderer)
    {
    }

    /// <inheritdoc/>
    public void AttachProxies()
    {
    }

    /// <inheritdoc/>
    public IFrameTime RequireFrameClock()
        => _frameTime
            ?? throw new InvalidOperationException("An offscreen run requires a frame clock.");

    /// <inheritdoc/>
    public void PrepareTick(IFrameTime frameTime)
    {
        ArgumentNullException.ThrowIfNull(frameTime);
        frameTime.NewFrame();
    }

    /// <inheritdoc/>
    public void AfterTick()
    {
    }

    /// <inheritdoc/>
    public void ShutdownPlatform()
    {
    }

    /// <inheritdoc/>
    public void DetachProxies()
    {
    }

    /// <inheritdoc/>
    public void LogStopped(IRenderer? renderer, long ticks, double elapsedSeconds)
    {
        SnapshotRingCounters counters = renderer?.SnapshotCounters ?? default;
        _logger.LogInformation(
            "Bork2d offscreen loop stopped: reason={Reason}, ticks={Ticks}, "
                + "elapsedSeconds={ElapsedSeconds:0.###}, backpressureWaits={BackpressureWaits}, "
                + "maxLiveSnapshots={MaxLiveSnapshots}, suspendedConsumptions={SuspendedConsumptions}",
            _engine.ExitRequested ? "Exit" : renderer?.FatalException is not null ? "Error" : "Stop",
            ticks,
            elapsedSeconds,
            counters.BackpressureWaits,
            counters.MaxLive,
            renderer?.SuspendedConsumptions ?? 0);
    }

    /// <inheritdoc/>
    public void ThrowFailures(
        Exception? setupFailure,
        Exception? loopFailure,
        Exception? rendererFailure,
        Exception? teardownFailure)
    {
        if (setupFailure is not null)
        {
            RunFailures.Throw(setupFailure);
            return;
        }

        var errors = new List<Exception>(capacity: 3);
        RunFailures.Add(errors, rendererFailure);
        RunFailures.Add(errors, loopFailure);
        RunFailures.Add(errors, teardownFailure);
        RunFailures.Throw(errors, "The offscreen run failed; the original exceptions are inner.");
    }
}
