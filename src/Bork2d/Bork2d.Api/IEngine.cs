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
    /// Gets the monotonic time/delta snapshot. Readable from any thread.
    /// </summary>
    /// <remarks>
    /// A windowed app advances the clock once per tick; the first tick reports <c>0</c> for both
    /// values. Headless and offscreen apps run one tick and never advance the clock, so both values
    /// stay <c>0</c>. See <see cref="ITime"/> for the clamp and thread-safety rules.
    /// </remarks>
    ITime Time { get; }

    /// <summary>
    /// Gets the window controls. The property access never throws.
    /// </summary>
    /// <remarks>
    /// In windowed mode this is the internal proxy attached for the duration of <c>Run()</c>. In
    /// headless and offscreen mode it is a stub whose members throw
    /// <see cref="NotSupportedException"/>. After a windowed run returns, retained references remain
    /// valid objects but their members throw <see cref="ObjectDisposedException"/> (D-M2-17).
    /// </remarks>
    IWindow Window { get; }

    /// <summary>
    /// Gets the input snapshot.
    /// </summary>
    /// <remarks>
    /// Windowed mode returns the internal proxy attached for the duration of <c>Run()</c>; the getter
    /// itself never throws there, but a retained reference's members throw
    /// <see cref="ObjectDisposedException"/> after the run. Headless and offscreen mode has no input
    /// subsystem, so the getter itself throws <see cref="NotSupportedException"/>.
    /// </remarks>
    IInput Input { get; }

    /// <summary>
    /// Gets the app-scoped asset service. Thread-safe requests; completion is asynchronous.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The getter never throws. It returns the real service in every mode: the base pack mounted at
    /// <c>Run</c>, or an empty virtual filesystem when the app was built with
    /// <c>Bork2dAppBuilder.WithoutAssets()</c>. That mode still starts the single loader thread and
    /// allows <see cref="IAssets.LoadPack"/> overlays.
    /// </para>
    /// <para>
    /// After <c>Run()</c> returns or throws, or after the app is disposed, every <see cref="IAssets"/>
    /// member throws <see cref="ObjectDisposedException"/>; this getter keeps returning the disposed
    /// service and does not throw by itself (D-M3-14).
    /// </para>
    /// </remarks>
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
    /// Requests a graceful shutdown. May be called from any thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In windowed mode the current tick finishes and the loop stops before the next freeze; the window
    /// is destroyed and <c>Bork2dApp.Run()</c> returns normally. A request received during
    /// <c>Setup</c> skips every tick but still calls <c>Teardown</c> once (D-M2-19).
    /// </para>
    /// <para>
    /// Headless and offscreen keep the M1 one-tick behavior and ignore the request. After the run has
    /// finished or the app has been disposed, this call is a no-op.
    /// </para>
    /// </remarks>
    void Exit();
}
