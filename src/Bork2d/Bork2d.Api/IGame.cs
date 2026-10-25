namespace Bork2d.Api;

/// <summary>
/// The game entry point injected by the host as a caller-constructed instance.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// The engine invokes all callbacks on its tick thread. User code may call the public API from
/// any thread during these callbacks, but the callbacks themselves are never concurrent.
/// </remarks>
public interface IGame<TLayer> where TLayer : struct, Enum
{
    /// <summary>
    /// Called once on the engine tick thread before the first tick.
    /// </summary>
    /// <param name="engine">The engine instance for this run.</param>
    void Setup(IEngine<TLayer> engine);

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
