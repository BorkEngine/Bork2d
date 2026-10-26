namespace Bork2d.Runtime;

/// <summary>
/// A built Bork2d host. It runs the configured windowed loop or the one-tick sequence and owns
/// app-level cleanup.
/// </summary>
/// <remarks>
/// <para>
/// A windowed app runs <c>IGame.Setup</c>, an uncapped sequence of <c>IGame.Tick</c> calls, and one
/// <c>IGame.Teardown</c> until <c>IEngine.Exit()</c>, the window close button, or a fatal platform
/// error. Headless and offscreen apps keep the one-tick M1 sequence. There is no GPU work, render
/// thread, or pack loading yet; those arrive in later milestones.
/// </para>
/// <para>
/// No member requires the process main thread. <see cref="Run"/> may be called once from any
/// thread; a failed run still counts as the one call. <see cref="Dispose"/> is idempotent and safe
/// before or after <see cref="Run"/>, but it throws while a run is active (D-M2-18): stop the loop
/// with <c>IEngine.Exit()</c> or the close button first. Disposal detaches the scene root's direct
/// children, detaches the window/input proxies, and disposes the app's logger factory, but never
/// calls <c>IGame.Teardown</c> again.
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
    /// Runs the app: the windowed loop, or the one-tick sequence in headless/offscreen mode.
    /// </summary>
    /// <remarks>
    /// May be called from any thread. May be called only once; a failed run still counts as the
    /// one call. Game exceptions propagate to the caller after cleanup. In windowed mode the loop
    /// stops on <c>IEngine.Exit()</c>, the window close button, or a fatal platform error; the
    /// window is destroyed and the OS thread joined before this method returns or throws.
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
    /// Disposes the app: detaches the root's direct children, detaches the window/input proxies, and
    /// disposes the logger factory.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe before or after <see cref="Run"/>. It throws
    /// <see cref="InvalidOperationException"/> while <see cref="Run"/> is active (D-M2-18), from any
    /// thread, without disturbing the loop; call <c>IEngine.Exit()</c> or close the window first. It
    /// never invokes <c>IGame.Teardown</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A windowed run is active; stop it with <c>IEngine.Exit()</c> or the window close button.
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
