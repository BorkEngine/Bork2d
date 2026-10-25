using Bork2d.Api;

namespace Bork2d.Common;

public class Engine<TLayer, TGame> : IEngine<TLayer>
    where TLayer : struct, Enum
    where TGame : IGame<TLayer>
{
    public ITime Time { get; }
    public IWindow Window { get; }
    public IAssets Assets { get; }

    private readonly TGame _game;
    private readonly EntityList<Camera<TLayer>> _cameras = new();
    private readonly EntityList<SpriteRender<TLayer>> _sprites = new();

    public Engine(TGame game, ITime time, IWindow window, IAssets assets)
    {
        _game = game;
        Time = time;
        Window = window;
        Assets = assets;
    }

    public ITransform CreateTransform() => new Transform();

    public ICamera<TLayer> CreateCamera() => new Camera<TLayer>(_cameras);

    public ISpriteRender<TLayer> CreateSpriteRender() => new SpriteRender<TLayer>(_sprites);

    public void Run()
    {
        _game.Setup(this);
        while (true) // <- temp
        {
            // Advance time
            _game.Tick();
        }
    }
}
