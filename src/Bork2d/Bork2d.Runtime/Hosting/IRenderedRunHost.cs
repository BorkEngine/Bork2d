using Bork2d.Engine.Time;
using Bork2d.Render;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The mode-specific hooks the shared rendered-run lifecycle needs from a windowed or offscreen
/// host.
/// </summary>
/// <remarks>
/// <see cref="RenderedRun"/> owns the shared ordering, setup/readiness boundary, tick loop,
/// teardown, renderer stop/join, asset ownership, and failure collection. A host supplies only
/// platform startup and shutdown, renderer creation and attachment, tick preparation, the windowed
/// minimized cap, proxy attachment/detachment, stop logging, and the mode-specific failure
/// precedence. Headless does not use this interface.
/// </remarks>
internal interface IRenderedRunHost
{
    /// <summary>
    /// Gets a value indicating whether the platform requested a graceful exit (a window close or
    /// <c>SDL_QUIT</c> for windowed; always <see langword="false"/> offscreen).
    /// </summary>
    bool IsPlatformExitRequested { get; }

    /// <summary>Starts the mode platform; a no-op offscreen.</summary>
    void StartPlatform();

    /// <summary>Creates the mode renderer without starting it or waiting for readiness.</summary>
    /// <returns>The unstarted renderer.</returns>
    IRenderer CreateRenderer();

    /// <summary>
    /// Attaches the renderer the host created, which may already have failed to start, to the mode
    /// platform.
    /// </summary>
    /// <param name="renderer">The renderer to attach.</param>
    void AttachRenderer(IRenderer renderer);

    /// <summary>Attaches the mode window/input proxies; a no-op offscreen.</summary>
    void AttachProxies();

    /// <summary>Gets the frame clock, or throws the mode-specific requirement error.</summary>
    /// <returns>The clock that advances <c>ITime</c> once per tick.</returns>
    IFrameTime RequireFrameClock();

    /// <summary>
    /// Performs the mode's pre-<c>Tick</c> work: freeze input and dispatch text (windowed), then
    /// advance the frame clock.
    /// </summary>
    /// <param name="frameTime">The frame clock returned by <see cref="RequireFrameClock"/>.</param>
    void PrepareTick(IFrameTime frameTime);

    /// <summary>Performs the mode's post-tick work (the windowed minimized tick cap).</summary>
    void AfterTick();

    /// <summary>Shuts the mode platform down after the renderer joined; a no-op offscreen.</summary>
    void ShutdownPlatform();

    /// <summary>Detaches the mode proxies after assets stopped; a no-op offscreen.</summary>
    void DetachProxies();

    /// <summary>Logs the mode's one-shot loop-stop line.</summary>
    /// <param name="renderer">The renderer, or <see langword="null"/> when creation failed.</param>
    /// <param name="ticks">The number of completed ticks.</param>
    /// <param name="elapsedSeconds">The loop's elapsed time.</param>
    void LogStopped(IRenderer? renderer, long ticks, double elapsedSeconds);

    /// <summary>Throws the run's failures with the mode's documented precedence.</summary>
    /// <param name="setupFailure">The <c>Setup</c> failure, if any.</param>
    /// <param name="loopFailure">The loop failure, if any.</param>
    /// <param name="rendererFailure">The normalized renderer failure, if any.</param>
    /// <param name="teardownFailure">The <c>Teardown</c> failure, if any.</param>
    void ThrowFailures(
        Exception? setupFailure,
        Exception? loopFailure,
        Exception? rendererFailure,
        Exception? teardownFailure);
}
