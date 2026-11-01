using System.Diagnostics;
using Bork2d.Engine;
using Bork2d.Engine.Assets.Loading;
using Bork2d.Engine.Scene;
using Bork2d.Engine.Time;
using Bork2d.Render;
using Bork2d.Render.Handoff;
using Microsoft.Extensions.Logging;

namespace Bork2d.Runtime.Hosting;

/// <summary>
/// The single rendered-run lifecycle shared by the windowed and offscreen hosts.
/// </summary>
/// <remarks>
/// <para>
/// The sequence is fixed for both modes: start assets; start the mode platform (windowed only; a
/// failure stops assets, detaches proxies, and rethrows before <c>Setup</c>); create and start the
/// renderer and install the exit wake callback (a synchronous factory/start failure is recorded but
/// never prevents <c>Setup</c>); attach the renderer and proxies; run <c>Setup</c> unless an
/// exit/close is already pending; wait once for renderer readiness after a successful
/// <c>Setup</c>; run the backpressured tick loop; run <c>Teardown</c> once when <c>Setup</c>
/// succeeded; stop and join the renderer before the platform shuts down; stop assets; detach
/// proxies; then log the stop and throw the mode-specific failures.
/// </para>
/// <para>
/// The readiness wait sits in the same error boundary for both modes: the loop failure boundary,
/// after <c>Setup</c> succeeded and before the first tick, with a readiness fatal recorded as a
/// renderer failure. A <c>Setup</c> failure wins over a renderer failure and runs no
/// <c>Teardown</c>; a renderer cleanup failure during normal shutdown is logged and does not change
/// the run's outcome (D-M6-27). The mode host supplies the windowed platform-fatal aggregation and
/// the offscreen precedence.
/// </para>
/// </remarks>
internal static class RenderedRun
{
    /// <summary>
    /// Runs the shared rendered-run lifecycle for one windowed or offscreen app run.
    /// </summary>
    /// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
    /// <param name="engine">The engine the loop ticks and captures.</param>
    /// <param name="game">The game callbacks to run.</param>
    /// <param name="assetService">The app-owned asset service to start and stop.</param>
    /// <param name="host">The mode-specific hooks.</param>
    /// <param name="logger">The <c>Bork2d.Runtime</c> logger for cleanup failures.</param>
    internal static void Run<TLayer>(
        GameEngine<TLayer> engine,
        IGame<TLayer> game,
        AssetService assetService,
        IRenderedRunHost host,
        ILogger logger)
        where TLayer : struct, Enum
    {
        // The base pack is validated before the window exists, so a missing/malformed pack produces
        // no visible window and no Setup/Teardown (D-M3-01). The proxies still become disposed
        // objects for retained references (D-M2-17).
        try
        {
            assetService.Start();
        }
        catch
        {
            host.DetachProxies();
            throw;
        }

        try
        {
            host.StartPlatform();
        }
        catch
        {
            // The platform never ran, but the assets did: stop them before Setup would have run.
            assetService.Stop();
            host.DetachProxies();
            throw;
        }

        IRenderer? renderer = null;
        Exception? setupFailure = null;
        Exception? loopFailure = null;
        Exception? teardownFailure = null;
        Exception? rendererFailure = null;
        var setupSucceeded = false;
        long ticks = 0;
        long sequence = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // D-M6-06: Start returns without waiting for device/swapchain readiness. A synchronous
            // factory/start failure is recorded but never prevents Setup from running.
            try
            {
                renderer = host.CreateRenderer();
                renderer.Start();
            }
            catch (Exception exception)
            {
                rendererFailure = NormalizeRendererFailure(exception);
            }
            finally
            {
                // Q9=I1: Exit() from any thread wakes a tick blocked in AcquireSnapshotSlot through
                // the renderer's ring signal. No renderer means no callback.
                engine.SetExitCallback(renderer is null ? null : renderer.WakeSnapshotWait);
            }

            // Attach before Setup so the game sees the live window/input immediately. The windowed
            // platform posts the confirmed state (extent/fullscreen/minimized) from its OS thread.
            if (renderer is not null)
            {
                host.AttachRenderer(renderer);
            }

            host.AttachProxies();

            try
            {
                // Exit/close received during startup mean no Setup and therefore no Tick/Teardown.
                if (!IsStopRequested(engine, host))
                {
                    try
                    {
                        game.Setup(engine);
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
                    && !IsStopRequested(engine, host))
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
                    && !IsStopRequested(engine, host))
                {
                    IFrameTime frameTime = host.RequireFrameClock();
                    while (!IsStopRequested(engine, host))
                    {
                        if (renderer!.FatalException is { } fatal)
                        {
                            rendererFailure = NormalizeRendererFailure(fatal);
                            break;
                        }

                        // Acquire before the tick-start handoff: input/mouse edges that arrive while
                        // the tick is backpressured accumulate for the next freeze, and DeltaTime
                        // keeps measuring the real interval between tick starts (D-004).
                        SnapshotSlot? slot = renderer.AcquireSnapshotSlot();
                        if (slot is null)
                        {
                            // A renderer fatal/stop while blocked starts no further tick (Q9=I1).
                            if (renderer.FatalException is { } blockedFatal)
                            {
                                rendererFailure = NormalizeRendererFailure(blockedFatal);
                            }

                            break;
                        }

                        var published = false;
                        try
                        {
                            host.PrepareTick(frameTime);
                            game.Tick();
                            ticks++;
                            engine.CaptureSnapshot(slot.Snapshot, ++sequence);
                            renderer.PublishSnapshot(slot);
                            published = true;
                        }
                        catch
                        {
                            // A Tick/capture failure frees the Writing slot and publishes nothing.
                            // A publish that already made the slot Ready is left to the join drain.
                            if (!published && slot.State == SnapshotSlotState.Writing)
                            {
                                renderer.AbandonSnapshotSlot(slot);
                            }

                            throw;
                        }

                        if (!IsStopRequested(engine, host))
                        {
                            // Q8/F4: minimize-only tick cap; no cap sleep when the loop is stopping.
                            host.AfterTick();
                        }
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
                        game.Teardown();
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
            StopAndJoinRenderer(renderer, logger);
            if (rendererFailure is null && renderer?.FatalException is { } lateFatal)
            {
                rendererFailure = NormalizeRendererFailure(lateFatal);
            }

            host.ShutdownPlatform();
            assetService.Stop();
            host.DetachProxies();
        }

        host.LogStopped(renderer, ticks, stopwatch.Elapsed.TotalSeconds);
        host.ThrowFailures(setupFailure, loopFailure, rendererFailure, teardownFailure);
    }

    private static bool IsStopRequested<TLayer>(GameEngine<TLayer> engine, IRenderedRunHost host)
        where TLayer : struct, Enum
        => engine.ExitRequested || host.IsPlatformExitRequested;

    private static void StopAndJoinRenderer(IRenderer? renderer, ILogger logger)
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
            logger.LogError(exception, "The renderer stop request failed during shutdown.");
        }

        try
        {
            renderer.Join();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "The renderer join failed during shutdown.");
        }
    }

    private static Exception NormalizeRendererFailure(Exception exception)
        => exception is Bork2dException
            ? exception
            : new Bork2dException(
                "The Vulkan renderer failed: " + exception.Message,
                exception);
}
