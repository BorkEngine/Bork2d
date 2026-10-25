using Bork2d.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Common;

/// <summary>
/// The engine instance that owns the scene graph.
/// </summary>
/// <remarks>
/// The engine holds exactly one <see cref="SceneRoot"/> and no global camera, sprite, or
/// transform lists. Parented objects are retained by their parent; unparented objects are
/// collectible when game code drops its last reference. No member requires a particular thread.
/// </remarks>
internal sealed class Engine<TLayer, TGame> : IEngine<TLayer>
    where TLayer : struct, Enum
    where TGame : IGame<TLayer>
{
    private readonly SceneGraph _graph = new();
    private readonly SceneRoot _root;
    private readonly TGame _game;
    private readonly ILoggerFactory _loggerFactory;
    private volatile bool _exitRequested;

    internal Engine(
        TGame game,
        ITime time,
        IWindow window,
        IAssets assets,
        ILoggerFactory? loggerFactory = null)
    {
        LayerValidation.Validate<TLayer>();

        _game = game;
        _root = new SceneRoot(_graph);
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        Time = time;
        Window = window;
        Assets = assets;
    }

    public ITime Time { get; }

    public IWindow Window { get; }

    public IAssets Assets { get; }

    public IInput Input => throw new NotSupportedException(
        "Input requires a window. Windowed input is implemented in M2; M1 has no window.");

    public ILoggerFactory LoggerFactory => _loggerFactory;

    public ISceneRoot Root => _root;

    public ITransform CreateTransform(ITransform? parent = null)
    {
        var transform = new Transform(_graph);
        if (parent is not null)
        {
            transform.AttachAsNewChild(RequireEngineNode(parent));
        }

        return transform;
    }

    public ICamera<TLayer> CreateCamera(ITransform parent)
    {
        var parentNode = RequireEngineNode(parent);
        var camera = new Camera<TLayer>(_graph);
        camera.AttachAsNewChild(parentNode);
        return camera;
    }

    public ISpriteRender<TLayer> CreateSpriteRender(ITransform parent)
    {
        var parentNode = RequireEngineNode(parent);
        var sprite = new SpriteRender<TLayer>(_graph);
        sprite.AttachAsNewChild(parentNode);
        return sprite;
    }

    public void Exit() => _exitRequested = true;

    public void Run()
    {
        _game.Setup(this);
        while (!_exitRequested)
        {
            // Advance time
            _game.Tick();
        }
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
