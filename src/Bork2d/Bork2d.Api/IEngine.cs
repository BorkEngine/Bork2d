using Microsoft.Extensions.Logging;

namespace Bork2d.Api;

/// <summary>
/// The engine instance created by the host application and passed to the game during startup.
/// </summary>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
/// <remarks>
/// <para>
/// No member requires the process main thread. Calls may originate on any thread during the
/// <c>Setup</c>/<c>Tick</c>/<c>Teardown</c> windows; the engine serializes structural scene
/// changes and per-object property access.
/// </para>
/// <para>
/// Scene entities are owned by <see cref="Root"/> while parented and are never disposed: detach
/// with <see cref="ITransform.Parent"/> = <see langword="null"/> and re-attach the same object
/// later.
/// </para>
/// </remarks>
public interface IEngine<TLayer> where TLayer : struct, Enum
{
    /// <summary>
    /// Gets the monotonic time/delta snapshot. Readable from any thread. In M1 the host does not
    /// advance the clock, so both values stay <c>0</c>.
    /// </summary>
    ITime Time { get; }

    /// <summary>
    /// Gets the window controls. Windowed-only in the target design; in M1 every mode returns a
    /// stub whose members throw <see cref="NotSupportedException"/>.
    /// </summary>
    IWindow Window { get; }

    /// <summary>
    /// Gets the input snapshot. Windowed-only in the target design; in M1 the getter throws
    /// <see cref="NotSupportedException"/> in every mode.
    /// </summary>
    IInput Input { get; }

    /// <summary>
    /// Gets the asset service. Thread-safe requests; completion is asynchronous. In M1 the
    /// returned stub's methods throw <see cref="NotSupportedException"/>.
    /// </summary>
    IAssets Assets { get; }

    /// <summary>
    /// Gets the Microsoft.Extensions.Logging factory configured by the host and owned by the app.
    /// Create per-category loggers from it.
    /// </summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>
    /// Gets the read-only root of the strong scene graph.
    /// </summary>
    ISceneRoot Root { get; }

    /// <summary>
    /// Creates a transform, optionally attached under <paramref name="parent"/>.
    /// </summary>
    /// <param name="parent">
    /// The parent to attach to, or <see langword="null"/> for a standalone transform that is not
    /// part of the scene and is collectible when the game drops it.
    /// </param>
    /// <returns>The created transform.</returns>
    ITransform CreateTransform(ITransform? parent = null);

    /// <summary>
    /// Creates a camera attached under <paramref name="parent"/>.
    /// </summary>
    /// <param name="parent">
    /// The non-null engine-owned parent; use <see cref="Root"/> for a scene-level camera.
    /// </param>
    /// <returns>The created camera.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    ICamera<TLayer> CreateCamera(ITransform parent);

    /// <summary>
    /// Creates a sprite renderable attached under <paramref name="parent"/>.
    /// </summary>
    /// <param name="parent">
    /// The non-null engine-owned parent; use <see cref="Root"/> for a scene-level renderable.
    /// </param>
    /// <returns>The created sprite renderable.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    ISpriteRender<TLayer> CreateSpriteRender(ITransform parent);

    /// <summary>
    /// Requests a graceful shutdown after the current tick. May be called from any thread.
    /// </summary>
    void Exit();
}
