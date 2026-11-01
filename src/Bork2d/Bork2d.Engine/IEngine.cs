using Bork2d.Engine.Assets;
using Bork2d.Engine.Input;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Microsoft.Extensions.Logging;

namespace Bork2d.Engine;

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
    /// Windowed and offscreen apps advance the clock once per tick; the first tick reports <c>0</c>
    /// for both values. Headless apps run a paced loop with a fully synthesized clock: tick <c>0</c>
    /// reports <c>0</c>/<c>0</c>, tick <c>N &gt;= 1</c> reports <c>N / ticksPerSecond</c> and
    /// <c>1 / ticksPerSecond</c>, and <c>WithMaxDeltaTime</c> does not clamp it. See
    /// <see cref="ITime"/> for the clamp and thread-safety rules.
    /// </remarks>
    ITime Time { get; }

    /// <summary>
    /// Gets the window controls. The property access never throws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// In windowed mode this is the internal proxy attached for the duration of <c>Run()</c>. In
    /// offscreen mode it is a functional facade whose <see cref="IWindow.Size"/> is the fixed
    /// <c>WithOffscreen</c> target extent and whose setters store inert window state; its members
    /// never throw, including after the run. In headless mode it is a stub whose members throw
    /// <see cref="NotSupportedException"/>.
    /// </para>
    /// <para>
    /// After a windowed run returns, retained references remain valid objects but their members
    /// throw <see cref="ObjectDisposedException"/> (D-M2-17). The getter itself never throws in any
    /// mode.
    /// </para>
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
    /// Gets an immutable snapshot of the last completed frame's render counters (M8 §6.9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The values describe the last completed frame, except <see cref="RenderStats.ValidationErrors"/>
    /// and <see cref="RenderStats.ValidationWarnings"/>, which are cumulative for the run.
    /// <see cref="RenderStats.TexturePages"/> and <see cref="RenderStats.TexturePageCapacity"/>
    /// include the placeholder page at index <c>0</c>.
    /// </para>
    /// <para>
    /// Callable from any thread. The getter returns <see langword="default"/> before the first
    /// completed frame, in headless mode, and before the host installs its stats source; after
    /// shutdown it may retain the last published values. It never throws, never blocks on the GPU,
    /// and allocates nothing.
    /// </para>
    /// </remarks>
    RenderStats RenderStats { get; }

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
    /// Captures the latest completed frame as a <see cref="FrameCaptureResult"/> (M8 §6.9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result carries row-major, top-left-origin RGBA8 pixels, the captured frame's actual
    /// <see cref="FrameCaptureResult.Width"/> and <see cref="FrameCaptureResult.Height"/>, and
    /// <see cref="FrameCaptureResult.Format"/> = <see cref="TextureFormat.Rgba8"/>.
    /// <see cref="FrameCaptureResult.Pixels"/> is exactly <c>Width * Height * 4</c> bytes and the
    /// copy includes every camera clear and draw and excludes OS window chrome. The bytes are a
    /// shared immutable result: every request served by the same GPU frame observes the same memory
    /// and callers must not mutate it.
    /// </para>
    /// <para>
    /// A request is served by the next frame recorded from a published snapshot, and the renderer
    /// completes it from that frame's fence even when the game never publishes another snapshot.
    /// At most eight requests may be outstanding at once; a request beyond the limit faults with
    /// <see cref="Bork2dException"/> instead of blocking the caller or being dropped.
    /// </para>
    /// <para>
    /// Callable from any thread while a windowed or offscreen app is running; requesting never
    /// blocks the tick or render thread and does not affect rendering when unused. A cancelled
    /// <paramref name="cancellationToken"/> faults the returned task with
    /// <see cref="OperationCanceledException"/> (checked before the outstanding-request limit); a
    /// renderer stop or fatal faults pending requests. Waiting for the task synchronously inside
    /// <c>Setup</c> or <c>Tick</c> before the snapshot that serves the request is published can
    /// block the producer needed to generate that frame.
    /// </para>
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> before <c>Run()</c> (the host has not installed a
    /// capture source), <see cref="NotSupportedException"/> during a headless run (there is no
    /// renderer), and <see cref="ObjectDisposedException"/> after <c>Run()</c> returns or the app is
    /// disposed.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels a pending capture request.</param>
    /// <returns>The captured frame's pixels, extent, and format.</returns>
    Task<FrameCaptureResult> CaptureFrameAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests a graceful shutdown. May be called from any thread.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The current tick finishes and the loop stops before the next tick, in every mode. A tick that
    /// requests <c>Exit</c> still captures and publishes its snapshot (windowed/offscreen), so the
    /// renderer may render one final frame. A request during <c>Setup</c> skips every tick but still
    /// calls <c>Teardown</c> once (D-M2-19).
    /// </para>
    /// <para>
    /// Windowed and offscreen wake a tick that is blocked waiting for a free snapshot slot, so the
    /// host starts no further tick and joins cleanly. Headless stops before the next scheduled tick;
    /// a pacing sleep is not interrupted, so shutdown latency is bounded by one tick period. A run
    /// that never calls this method continues until the process stops. After the run has finished or
    /// the app has been disposed, this call is a no-op.
    /// </para>
    /// </remarks>
    void Exit();
}
