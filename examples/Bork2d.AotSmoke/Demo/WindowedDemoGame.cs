using System.Numerics;
using Bork2d.AotSmoke.Smoke;
using Bork2d.Engine;
using Bork2d.Engine.Input;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Microsoft.Extensions.Logging;

namespace Bork2d.AotSmoke.Demo;

/// <summary>
/// The windowed AOT demo game: a small camera/transform/sprite tree, per-tick keyboard movement,
/// and transition logging on the public engine surface and the default logging path.
/// </summary>
/// <remarks>
/// <para>
/// The demo creates a camera and a sprite renderable under a player transform beneath the root. On
/// every tick it reads the frozen input snapshot, moves the player on WASD or the arrow keys by
/// <c>ITime.DeltaTime</c> scaled speed, and logs only input transitions (press/release, Escape, text)
/// rather than every frame. Escape requests a graceful exit, and the window close button is handled
/// by the platform.
/// </para>
/// <para>
/// With a tick limit the game calls <see cref="IEngine{TLayer}.Exit"/> at the end of exactly that
/// tick, which turns the demo into the automated windowed acceptance run. The game uses no sleeps,
/// pacing, reflection, or internal types.
/// </para>
/// </remarks>
internal sealed class WindowedDemoGame : IGame<SmokeLayer>
{
    private const string Category = "Bork2d.AotSmoke.Demo";

    /// <summary>The movement speed in world units per second.</summary>
    private const float MoveSpeed = 6f;

    private static readonly (KeyCode Key, string Name)[] _watchedKeys =
    [
        (KeyCode.W, "W"),
        (KeyCode.A, "A"),
        (KeyCode.S, "S"),
        (KeyCode.D, "D"),
        (KeyCode.Up, "Up"),
        (KeyCode.Down, "Down"),
        (KeyCode.Left, "Left"),
        (KeyCode.Right, "Right"),
        (KeyCode.Space, "Space"),
        (KeyCode.Escape, "Escape"),
    ];

    private static readonly (MouseButton Button, string Name)[] _watchedButtons =
    [
        (MouseButton.Left, "Left"),
        (MouseButton.Right, "Right"),
        (MouseButton.Middle, "Middle"),
        (MouseButton.Back, "Back"),
        (MouseButton.Forward, "Forward"),
    ];

    private readonly int? _tickLimit;
    private IEngine<SmokeLayer>? _engine;
    private ILogger? _logger;
    private ITransform? _player;
    private Vector2 _position;

    /// <summary>Initializes a new instance of the <see cref="WindowedDemoGame"/> class.</summary>
    /// <param name="tickLimit">
    /// The number of completed ticks after which the demo requests an exit, or <see langword="null"/>
    /// for an interactive run that ends on Escape or the window close button.
    /// </param>
    internal WindowedDemoGame(int? tickLimit)
    {
        _tickLimit = tickLimit;
    }

    /// <summary>Gets the number of <c>Setup</c> calls.</summary>
    internal int SetupCount { get; private set; }

    /// <summary>Gets the number of completed <c>Tick</c> calls.</summary>
    internal int TickCount { get; private set; }

    /// <summary>Gets the number of <c>Teardown</c> calls.</summary>
    internal int TeardownCount { get; private set; }

    /// <summary>Gets the player transform created under the root.</summary>
    internal ITransform? Player => _player;

    /// <summary>Gets the camera created under the root.</summary>
    internal ICamera<SmokeLayer>? Camera { get; private set; }

    /// <summary>Gets the sprite renderable created under the player transform.</summary>
    internal ISpriteRender<SmokeLayer>? SpriteRender { get; private set; }

    /// <summary>Gets the player position at the end of the last tick.</summary>
    internal Vector2 FinalPosition { get; private set; }

    /// <inheritdoc />
    public void Setup(IEngine<SmokeLayer> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        SetupCount++;
        _engine = engine;
        _logger = engine.LoggerFactory.CreateLogger(Category);

        var framebuffer = engine.Window.Size;
        _logger.LogInformation(
            "demo setup: title={Title}, framebuffer={Width}x{Height}, tickLimit={TickLimit}",
            engine.Window.Title,
            framebuffer.X,
            framebuffer.Y,
            _tickLimit);

        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 5f;
        camera.CullingMask = SmokeLayer.World;
        camera.Clear = ClearKind.Color;
        camera.ClearColor = Color.Black;
        camera.ZIndex = 0;
        Camera = camera;

        var player = engine.CreateTransform(engine.Root);
        player.LocalPosition = Vector2.Zero;
        _player = player;

        var sprite = engine.CreateSpriteRender(player);
        sprite.Sprite = null;
        sprite.Layer = SmokeLayer.World;
        sprite.ZIndex = 1;
        SpriteRender = sprite;

        engine.Input.OnTextTyped += OnTextTyped;
    }

    /// <inheritdoc />
    public void Tick()
    {
        TickCount++;

        var engine = _engine
            ?? throw new InvalidOperationException("AOT demo check failed: Setup did not capture the engine.");
        var player = _player
            ?? throw new InvalidOperationException("AOT demo check failed: Setup created no player transform.");
        var logger = _logger
            ?? throw new InvalidOperationException("AOT demo check failed: Setup created no logger.");
        var input = engine.Input;

        var direction = Vector2.Zero;
        if (input.IsKeyDown(KeyCode.A) || input.IsKeyDown(KeyCode.Left))
        {
            direction.X -= 1f;
        }

        if (input.IsKeyDown(KeyCode.D) || input.IsKeyDown(KeyCode.Right))
        {
            direction.X += 1f;
        }

        if (input.IsKeyDown(KeyCode.W) || input.IsKeyDown(KeyCode.Up))
        {
            direction.Y += 1f;
        }

        if (input.IsKeyDown(KeyCode.S) || input.IsKeyDown(KeyCode.Down))
        {
            direction.Y -= 1f;
        }

        var deltaTime = (float)engine.Time.DeltaTime;
        if (direction != Vector2.Zero)
        {
            direction = Vector2.Normalize(direction);
            _position += direction * (MoveSpeed * deltaTime);
            player.LocalPosition = _position;
        }

        foreach (var (key, name) in _watchedKeys)
        {
            if (input.IsKeyPressed(key))
            {
                logger.LogInformation(
                    "key pressed: {Key} at tick {Tick}, time={Time:0.###}s",
                    name,
                    TickCount,
                    engine.Time.Time);
            }

            if (input.IsKeyReleased(key))
            {
                logger.LogInformation(
                    "key released: {Key} at tick {Tick}, time={Time:0.###}s",
                    name,
                    TickCount,
                    engine.Time.Time);
            }
        }

        foreach (var (button, name) in _watchedButtons)
        {
            if (input.IsMousePressed(button))
            {
                logger.LogInformation(
                    "mouse pressed: {Button} at {Position} (framebuffer pixels)",
                    name,
                    input.MousePosition);
            }

            if (input.IsMouseReleased(button))
            {
                logger.LogInformation("mouse released: {Button}", name);
            }
        }

        if (input.IsKeyPressed(KeyCode.Escape))
        {
            logger.LogInformation("Escape pressed: requesting a graceful exit");
            engine.Exit();
        }

        if (_tickLimit is { } limit && TickCount == limit)
        {
            logger.LogInformation(
                "demo tick limit reached: ticks={Ticks}, position={Position}, distance={Distance:0.###}",
                TickCount,
                _position,
                _position.Length());
            FinalPosition = _position;
            engine.Exit();
        }
    }

    /// <inheritdoc />
    public void Teardown()
    {
        TeardownCount++;
        FinalPosition = _position;

        var engine = _engine;
        if (engine is not null)
        {
            engine.Input.OnTextTyped -= OnTextTyped;
        }

        _logger?.LogInformation(
            "demo teardown: ticks={Ticks}, position={Position}, distance={Distance:0.###}, "
                + "elapsed={Elapsed:0.###}s",
            TickCount,
            FinalPosition,
            FinalPosition.Length(),
            engine?.Time.Time ?? 0d);

        engine?.Root.DetachAll();

        SmokeChecks.Require(
            TeardownCount == 1,
            "the windowed demo must tear down exactly once.");
        SmokeChecks.Require(
            _player is not null && _player.Parent is null,
            "the windowed demo teardown must detach the player from the root.");
    }

    private void OnTextTyped(char character)
    {
        _logger?.LogInformation(
            "text typed: '{Character}' (U+{CodeUnit:X4}) at tick {Tick}",
            character,
            (int)character,
            TickCount);
    }
}
