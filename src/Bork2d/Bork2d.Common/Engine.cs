using Bork2d.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Common;

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
/// three-state token so <see cref="Exit"/> becomes a no-op once the host has stopped the run.
/// </para>
/// </remarks>
/// <typeparam name="TLayer">The game's <see cref="Enum"/>-based render layer flags.</typeparam>
internal sealed class Engine<TLayer> : IEngine<TLayer>
    where TLayer : struct, Enum
{
    private const int ExitActive = 0;
    private const int ExitRequestedState = 1;
    private const int ExitStopped = 2;

    private readonly Lock _structuralLock = new();
    private readonly SceneRoot _root;
    private readonly IInput? _input;
    private readonly ILoggerFactory _loggerFactory;
    private int _exitState;

    internal Engine(
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

    /// <summary>
    /// Gets a value indicating whether <see cref="Exit"/> was called while the run is active.
    /// </summary>
    /// <remarks>
    /// The windowed loop reads this token at each tick boundary. The one-tick modes never read it,
    /// and it stays <see langword="false"/> after <see cref="MarkStopped"/> so a post-shutdown
    /// <see cref="Exit"/> is a no-op.
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
        var camera = new Camera<TLayer>(_structuralLock);
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
        Interlocked.CompareExchange(ref _exitState, ExitRequestedState, ExitActive);
    }

    /// <summary>
    /// Marks the run as finished so later <see cref="Exit"/> calls are no-ops (D-M2-17).
    /// </summary>
    /// <remarks>Idempotent; called by the app runtime when a run completes or the app is disposed.</remarks>
    internal void MarkStopped() => Interlocked.Exchange(ref _exitState, ExitStopped);

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
