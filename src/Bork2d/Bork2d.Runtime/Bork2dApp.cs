namespace Bork2d.Runtime;

/// <summary>
/// A built Bork2d host. In M1 it runs a one-tick smoke sequence and owns app-level cleanup.
/// </summary>
/// <remarks>
/// <para>
/// The M1 <see cref="Run"/> path calls <c>IGame.Setup</c>, exactly one <c>IGame.Tick</c>, and
/// <c>IGame.Teardown</c>, then returns. There is no window, GPU work, render thread, or pack
/// loading yet; those arrive in later milestones.
/// </para>
/// <para>
/// No member requires the process main thread. <see cref="Dispose"/> is idempotent and safe
/// before or after <see cref="Run"/>; it detaches the scene root's direct children and disposes
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
    /// Runs the M1 smoke sequence: <c>Setup</c>, one <c>Tick</c>, then <c>Teardown</c>.
    /// </summary>
    /// <remarks>
    /// May be called from any thread. May be called only once; a failed run still counts as the
    /// one call. Game exceptions propagate to the caller after cleanup.
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
                    "Bork2dApp.Run() may be called only once; the M1 host runs a single "
                    + "Setup/Tick/Teardown smoke sequence.");
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
    /// Disposes the app: detaches the root's direct children and disposes the logger factory.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe before or after <see cref="Run"/>. It never invokes
    /// <c>IGame.Teardown</c>.
    /// </remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_state == Bork2dAppState.Disposed)
            {
                return;
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
