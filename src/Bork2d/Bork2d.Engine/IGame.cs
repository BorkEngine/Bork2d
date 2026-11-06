namespace Bork2d.Engine;

/// <summary>
/// The game entry point injected by the host as a caller-constructed instance.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// The host invokes <see cref="Setup"/> once on the tick thread before the first tick and waits for
/// its task before renderer readiness and the first <see cref="Tick"/>. Setup may await; after an
/// await its continuation may resume on another thread. No other callback runs until the setup task
/// completes, and the callbacks are never concurrent.
/// </para>
/// <para>
/// User code may call the public API from any thread during these callbacks. Exceptions propagate
/// to the host's <c>Run</c> after cleanup.
/// </para>
/// <para>
/// The host blocks on the setup task with <c>GetAwaiter().GetResult()</c>. A
/// <see cref="SynchronizationContext"/> installed by the caller that requires the blocked run thread
/// to pump its queue therefore deadlocks when Setup awaits; the host installs no context of its own.
/// </para>
/// </remarks>
public interface IGame<TLayer> where TLayer : struct, Enum
{
    /// <summary>
    /// Called once on the engine tick thread before the first tick.
    /// </summary>
    /// <param name="engine">The engine instance for this run.</param>
    /// <returns>A task that completes when setup has finished.</returns>
    /// <remarks>
    /// The host blocks on the returned task. A synchronous throw and a faulted task both fail the
    /// run: the exception propagates from the host's <c>Run</c> after cleanup, no <see cref="Tick"/>
    /// runs, and no <see cref="Teardown"/> runs. Setup is never interrupted by an exit/close request.
    /// </remarks>
    Task Setup(IEngine<TLayer> engine);

    /// <summary>
    /// Called once per tick on the engine tick thread. Must return; game workers must be joined
    /// before it returns.
    /// </summary>
    void Tick();

    /// <summary>
    /// Called once on the engine tick thread before shutdown. Release game-owned resources here;
    /// the host detaches the scene root afterwards.
    /// </summary>
    void Teardown();
}
