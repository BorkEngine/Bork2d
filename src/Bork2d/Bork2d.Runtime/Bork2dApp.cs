using Bork2d.Runtime.Hosting;

namespace Bork2d.Runtime;

/// <summary>
/// A built Bork2d host. It runs the configured windowed/offscreen loop or the paced headless loop
/// and owns app-level cleanup.
/// </summary>
/// <remarks>
/// <para>
/// A windowed app starts the Vulkan renderer thread, runs <c>IGame.Setup</c>, a ring-backpressured
/// sequence of <c>IGame.Tick</c> calls, and one <c>IGame.Teardown</c> until <c>IEngine.Exit()</c>,
/// the window close button, a renderer fatal, or a fatal platform error. Each tick acquires a
/// snapshot slot, runs <c>Tick</c>, captures, and publishes; the Exit/close tick is published and
/// the window is capped at 30 ticks per second only while minimized. An offscreen app runs the same
/// backpressured loop without a window or input against a fixed Vulkan target until
/// <c>IEngine.Exit()</c> (or a renderer fatal) and advances <c>ITime</c> per tick. Headless runs a
/// paced <c>Setup</c> → <c>Tick</c>… → <c>Teardown</c> loop until <c>IEngine.Exit()</c> with a fully
/// synthesized clock and no GPU, SDL, renderer, or snapshot ring. The base asset pack (or the
/// <c>WithoutAssets()</c> empty filesystem) opens before the window and <c>Setup</c> and closes after
/// <c>Teardown</c>. The renderer thread is stopped and joined before <see cref="Run"/> returns or
/// throws.
/// </para>
/// <para>
/// No member requires the process main thread. <see cref="Run"/> may be called once from any
/// thread; a failed run still counts as the one call. <see cref="Dispose"/> is idempotent and safe
/// before or after <see cref="Run"/>, but it throws while a run is active (D-M2-18): stop the loop
/// with <c>IEngine.Exit()</c> or the close button first. Disposal detaches the scene root's direct
/// children, detaches the window/input proxies, disposes the unstarted or stopped asset service and
/// the app's logger factory, but never calls <c>IGame.Teardown</c> again.
/// </para>
/// </remarks>
public sealed class Bork2dApp : IDisposable
{
    private readonly Lock _gate = new();
    private readonly IAppRuntime _runtime;
    private Bork2dAppState _state;

    internal Bork2dApp(IAppRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        _runtime = runtime;
    }

    /// <summary>
    /// Runs the app: the windowed/offscreen handoff loop, or the paced headless loop.
    /// </summary>
    /// <remarks>
    /// May be called from any thread. May be called only once; a failed run still counts as the
    /// one call. Game exceptions propagate to the caller after cleanup. The windowed loop stops on
    /// <c>IEngine.Exit()</c>, the window close button, a renderer fatal, or a fatal platform error;
    /// the offscreen loop stops on <c>IEngine.Exit()</c> or a renderer fatal; headless stops on
    /// <c>IEngine.Exit()</c>. A windowed/offscreen tick that requests <c>Exit</c> still publishes its
    /// snapshot, and a renderer fatal/stop wakes a tick blocked waiting for a snapshot slot with no
    /// extra tick. The window is destroyed, the OS thread joined, and the renderer thread
    /// stopped/joined before this method returns or throws. Headless never creates a platform or
    /// renderer and runs until <c>Exit</c> on the caller thread.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The app has already been disposed.</exception>
    /// <exception cref="InvalidOperationException">The app has already been run.</exception>
    public void Run()
    {
        lock (_gate)
        {
            if (_state == Bork2dAppState.Disposed)
            {
                throw new ObjectDisposedException(nameof(Bork2dApp));
            }

            if (_state != Bork2dAppState.Created)
            {
                throw new InvalidOperationException(
                    "Bork2dApp.Run() may be called only once per built app.");
            }

            _state = Bork2dAppState.Running;
        }

        try
        {
            _runtime.Run();
        }
        catch
        {
            ClearRootAfterFailure();
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (_state == Bork2dAppState.Running)
                {
                    _state = Bork2dAppState.Completed;
                }
            }
        }
    }

    /// <summary>
    /// Disposes the app: detaches the root's direct children, detaches the window/input proxies,
    /// disposes the unstarted or stopped asset service, and disposes the logger factory.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe before or after <see cref="Run"/>. It throws
    /// <see cref="InvalidOperationException"/> while <see cref="Run"/> is active (D-M2-18), from any
    /// thread, without disturbing the loop; call <c>IEngine.Exit()</c> or close the window first. It
    /// never invokes <c>IGame.Teardown</c>. Disposing an unstarted app disposes the constructed asset
    /// service without opening a pack or starting its loader thread (D-M3-14).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A run is active; stop it with <c>IEngine.Exit()</c> or the window close button.
    /// </exception>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == Bork2dAppState.Disposed)
            {
                return;
            }

            if (_state == Bork2dAppState.Running)
            {
                throw new InvalidOperationException(
                    "Bork2dApp.Dispose() cannot run while Bork2dApp.Run() is active. Call "
                    + "IEngine.Exit() or close the window, wait for Run() to return, then dispose.");
            }

            _state = Bork2dAppState.Disposed;
        }

        try
        {
            _runtime.Root.DetachAll();
        }
        finally
        {
            _runtime.Dispose();
        }
    }

    private void ClearRootAfterFailure()
    {
        try
        {
            _runtime.Root.DetachAll();
        }
        catch
        {
            // Keep the game exception as the one the caller observes. Dispose still runs the
            // same cleanup and is idempotent.
        }
    }
}
