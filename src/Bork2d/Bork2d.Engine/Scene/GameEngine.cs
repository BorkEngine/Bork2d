using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Input;
using Bork2d.Engine.Platform;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Engine.Scene;

/// <summary>
/// The engine instance that owns the scene tree and its global structural lock.
/// </summary>
/// <remarks>
/// <para>
/// The engine holds exactly one <see cref="SceneRoot"/> and no global camera, sprite, or
/// transform lists. It also owns the single <see cref="Lock"/> that serializes structural scene
/// changes; every node created by this engine shares it. Parented objects are retained by their
/// parent; unparented objects are collectible when game code drops its last reference. No member
/// requires a particular thread.
/// </para>
/// <para>
/// The window and input implementations are injected by the composition root: a windowed app gets
/// internal proxies that the runtime host attaches to a live platform before <c>Setup</c>, while
/// headless and offscreen apps get the throwing window stub and no input. The exit flag is a
/// three-state token so <see cref="Exit"/> becomes a no-op once the host has stopped the run; the
/// host may install a wake callback that is invoked once on the active-to-requested transition so a
/// tick blocked on the snapshot ring returns without starting another tick (M7 Q9=I1).
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class GameEngine<TLayer> : IEngine<TLayer>
    where TLayer : struct, Enum
{
    private const int ExitActive = 0;
    private const int ExitRequestedState = 1;
    private const int ExitStopped = 2;
    private const int CaptureUnavailable = 0;
    private const int CaptureAvailable = 1;
    private const int CaptureHeadless = 2;
    private const int CaptureDisposed = 3;

    private readonly Lock _structuralLock = new();
    private readonly Lock _renderStatsLock = new();
    private readonly Lock _captureLock = new();
    private readonly SceneRoot _root;
    private readonly SnapshotCapture<TLayer> _capture;
    private readonly IInput? _input;
    private readonly ILoggerFactory _loggerFactory;
    private int _exitState;
    private Action? _exitCallback;
    private Func<RenderStats>? _renderStatsSource;
    private RenderStats _renderStats;
    private Func<CancellationToken, Task<byte[]>>? _captureSource;
    private int _captureState = CaptureUnavailable;

    internal GameEngine(
        ITime time,
        IWindow window,
        IAssets assets,
        IInput? input = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(assets);

        LayerValidation.Validate<TLayer>();

        _root = new SceneRoot(_structuralLock);
        _capture = new SnapshotCapture<TLayer>(_structuralLock, _root);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _input = input;
        Time = time;
        Window = window;
        Assets = assets;
    }

    public ITime Time { get; }

    public IWindow Window { get; }

    public IAssets Assets { get; }

    public IInput Input => _input ?? throw new NotSupportedException(
        "Input requires a windowed app. Headless and offscreen modes do not create a window or an "
        + "input subsystem.");

    public ILoggerFactory LoggerFactory => _loggerFactory;

    public ISceneRoot Root => _root;

    public RenderStats RenderStats
    {
        get
        {
            // The host installs a source after the renderer exists; headless runs never do and keep
            // returning default (M8 §6.9). Reading is allocation-free and never blocks on the GPU.
            Func<RenderStats>? source = Volatile.Read(ref _renderStatsSource);
            if (source is not null)
            {
                try
                {
                    RenderStats current = source();
                    lock (_renderStatsLock)
                    {
                        _renderStats = current;
                    }

                    return current;
                }
                catch (Exception)
                {
                    // The public getter never throws (M8 §6.9); the last consistent snapshot wins.
                }
            }

            lock (_renderStatsLock)
            {
                return _renderStats;
            }
        }
    }

    /// <summary>
    /// Installs (or clears) the host's render-stats source (M8 §6.9).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The runtime installs a source that reads the renderer's snapshot after the renderer is
    /// created, so the tick thread observes the last completed frame without touching Vulkan.
    /// Headless runs never install one and <see cref="RenderStats"/> stays <see langword="default"/>.
    /// </para>
    /// <para>
    /// The source must be cheap, allocation-free, and thread-safe; the getter calls it on the
    /// reader's thread. If it throws, the getter returns the last value it observed.
    /// </para>
    /// </remarks>
    /// <param name="source">The source, or <see langword="null"/> to return the last snapshot.</param>
    internal void SetRenderStatsSource(Func<RenderStats>? source)
        => Volatile.Write(ref _renderStatsSource, source);

    /// <inheritdoc/>
    public Task<byte[]> CaptureFrameAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task<byte[]>>? source;
        int state;
        lock (_captureLock)
        {
            source = _captureSource;
            state = _captureState;
        }

        return state switch
        {
            CaptureAvailable when source is not null => source(cancellationToken),
            CaptureHeadless => throw new NotSupportedException(
                "CaptureFrameAsync requires a windowed or offscreen app; a headless run has no "
                    + "renderer or frames to capture."),
            CaptureDisposed => throw new ObjectDisposedException(
                nameof(GameEngine<TLayer>),
                "CaptureFrameAsync is unavailable after the run returned or the app was disposed."),
            _ => throw new InvalidOperationException(
                "CaptureFrameAsync requires a running windowed or offscreen app; no capture source "
                    + "is installed before Run()."),
        };
    }

    /// <summary>
    /// Installs (or clears) the host's frame-capture source (M8 §6.9).
    /// </summary>
    /// <remarks>
    /// The runtime installs the renderer request delegate after the renderer is created and before
    /// <c>Setup</c>, so the game may capture during <c>Setup</c>/<c>Tick</c>/<c>Teardown</c>. The
    /// delegate must be thread-safe and non-blocking; it is invoked outside every engine lock.
    /// </remarks>
    /// <param name="source">The renderer capture delegate, or <see langword="null"/> to clear it.</param>
    internal void SetCaptureSource(Func<CancellationToken, Task<byte[]>>? source)
    {
        lock (_captureLock)
        {
            if (_captureState == CaptureDisposed)
            {
                return;
            }

            _captureSource = source;
            _captureState = source is null ? CaptureUnavailable : CaptureAvailable;
        }
    }

    /// <summary>
    /// Marks the capture lifecycle as headless (no renderer or frames) at run start (M8 §6.9).
    /// </summary>
    internal void SetCaptureUnsupported()
    {
        lock (_captureLock)
        {
            if (_captureState == CaptureDisposed)
            {
                return;
            }

            _captureSource = null;
            _captureState = CaptureHeadless;
        }
    }

    /// <summary>
    /// Marks the capture lifecycle disposed after <c>Run</c> returns/throws or the app is disposed.
    /// </summary>
    /// <remarks>
    /// Sticky: a later source installation is ignored, so post-shutdown calls throw
    /// <see cref="ObjectDisposedException"/> as documented.
    /// </remarks>
    internal void MarkCaptureDisposed()
    {
        lock (_captureLock)
        {
            _captureSource = null;
            _captureState = CaptureDisposed;
        }
    }

    /// <summary>
    /// Gets a value indicating whether <see cref="Exit"/> was called while the run is active.
    /// </summary>
    /// <remarks>
    /// Every host loop reads this token at each tick boundary. It stays <see langword="false"/>
    /// after <see cref="MarkStopped"/> so a post-shutdown <see cref="Exit"/> is a no-op.
    /// </remarks>
    internal bool ExitRequested => Volatile.Read(ref _exitState) == ExitRequestedState;

    public ITransform CreateTransform(ITransform? parent = null)
    {
        var transform = new Transform(_structuralLock);
        if (parent is not null)
        {
            transform.AttachAsNewChild(RequireEngineNode(parent));
        }

        return transform;
    }

    public ICamera<TLayer> CreateCamera(ITransform parent)
    {
        var parentNode = RequireEngineNode(parent);
        var camera = new Camera<TLayer>(_structuralLock, Window);
        camera.AttachAsNewChild(parentNode);
        return camera;
    }

    public ISpriteRender<TLayer> CreateSpriteRender(ITransform parent)
    {
        var parentNode = RequireEngineNode(parent);
        var sprite = new SpriteRender<TLayer>(_structuralLock);
        sprite.AttachAsNewChild(parentNode);
        return sprite;
    }

    public void Exit()
    {
        // Compare-exchange from Active so a caller racing the end of the run cannot resurrect a
        // request after MarkStopped has moved the token to Stopped.
        if (Interlocked.CompareExchange(ref _exitState, ExitRequestedState, ExitActive) != ExitActive)
        {
            return;
        }

        // The host installs a wake callback for windowed/offscreen runs so a tick blocked on the
        // snapshot ring returns without starting another tick (M7 Q9=I1). The callback must not
        // throw; the renderer's WakeSnapshotWait only signals the ring.
        Volatile.Read(ref _exitCallback)?.Invoke();
    }

    /// <summary>
    /// Installs the host's exit wake callback, invoked once when <see cref="Exit"/> moves the token
    /// from active to requested (M7 WP-4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The windowed and offscreen hosts set this to the renderer's <c>WakeSnapshotWait</c> after the
    /// renderer is created and before <c>Setup</c>, so a tick blocked in the ring's acquire wakes
    /// and starts no further tick while a tick that already owns a slot may still publish it
    /// (Q4=D1, Q9=I1). Headless runs never install one.
    /// </para>
    /// <para>
    /// The callback must not throw and must be safe to invoke on any thread. <see cref="MarkStopped"/>
    /// clears it, so a post-shutdown <see cref="Exit"/> cannot reach a stopped renderer.
    /// </para>
    /// </remarks>
    /// <param name="callback">The wake callback, or <see langword="null"/> to clear it.</param>
    internal void SetExitCallback(Action? callback) => Volatile.Write(ref _exitCallback, callback);

    /// <summary>
    /// Marks the run as finished so later <see cref="Exit"/> calls are no-ops (D-M2-17).
    /// </summary>
    /// <remarks>
    /// Idempotent; called by the app runtime when a run completes or the app is disposed. It clears
    /// the exit wake callback before stopping so no later <see cref="Exit"/> reaches the renderer.
    /// </remarks>
    internal void MarkStopped()
    {
        Volatile.Write(ref _exitCallback, null);
        Interlocked.Exchange(ref _exitState, ExitStopped);
    }

    /// <summary>
    /// Resets and fills a slot-owned snapshot from the current rooted scene and stamps the tick's
    /// sequence, <see cref="ITime.Time"/>, and <see cref="ITime.DeltaTime"/> (M7 WP-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The host calls this on the tick thread after <c>IGame.Tick</c> returns, with every game
    /// worker joined, after the first tick, and before <c>Teardown</c> starts; the host owns that
    /// quiescence contract. The caller owns <paramref name="snapshot"/> in the <c>Writing</c> slot
    /// state and publishes or abandons the slot afterwards.
    /// </para>
    /// <para>
    /// The engine reuses one internal capture object, so a warm capture allocates nothing. The
    /// traversal is iterative and parent-first under the structural lock: each node is read under
    /// one per-object lock acquisition, and its world matrix is composed once from its captured
    /// local matrix and its parent's evaluated world matrix.
    /// </para>
    /// </remarks>
    /// <param name="snapshot">The slot-owned snapshot to reset and fill.</param>
    /// <param name="sequence">The monotonic snapshot sequence; the host starts it at <c>1</c>.</param>
    internal void CaptureSnapshot(FrameSnapshot snapshot, long sequence)
    {
        _capture.Capture(snapshot, sequence, Time.Time, Time.DeltaTime);
    }

    private static ISceneNode RequireEngineNode(ITransform parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (parent is not ISceneNode node)
        {
            throw new Bork2dException(
                $"A parent must be an engine-owned scene node; got '{parent.GetType().FullName}'.");
        }

        return node;
    }
}
