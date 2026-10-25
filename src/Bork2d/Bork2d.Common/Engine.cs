using Bork2d.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bork2d.Common;

public class Engine<TLayer, TGame>: IEngine<TLayer>
    where TLayer : struct, Enum
    where TGame : IGame<TLayer>
{
    public ITime Time { get; }
    public IWindow Window { get; }
    public IAssets Assets { get; }

    public IInput Input => throw new NotSupportedException(
        "Input requires a window. Windowed input is implemented in M2; M1 has no window.");

    public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;

    public ISceneRoot Root => throw new NotSupportedException("The scene graph is implemented in WP3.");

    private readonly TGame _game;
    private readonly EntityList<Camera<TLayer>> _cameras = new();
    private readonly EntityList<SpriteRender<TLayer>> _sprites = new();
    private volatile bool _exitRequested;

    public Engine(TGame game, ITime time, IWindow window, IAssets assets)
    {
        _game = game;
        Time = time;
        Window = window;
        Assets = assets;
    }

    public ITransform CreateTransform(ITransform? parent = null)
    {
        // Parent attachment is part of the scene graph and is implemented in WP3.
        return new Transform();
    }

    public ICamera<TLayer> CreateCamera(ITransform parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        // Attachment to the parent is implemented in WP3.
        return new Camera<TLayer>(_cameras);
    }

    public ISpriteRender<TLayer> CreateSpriteRender(ITransform parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        // Attachment to the parent is implemented in WP3.
        return new SpriteRender<TLayer>(_sprites);
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
}
