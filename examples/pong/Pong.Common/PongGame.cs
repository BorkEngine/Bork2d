using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Input;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Pong.Common;

public sealed class PongGame : IGame<GameLayer>
{
    /// <summary>The court line width in world units (a few pixels at 720p).</summary>
    private const float CourtLineWidth = 0.08f;

    private const int CourtZIndex = 5;
    private const int PaddleZIndex = 10;
    private const int BallZIndex = 20;

    // A fixed phase keeps the ball's time-driven pulse legible from the first frame instead of
    // starting at the dimmest point of its cycle.
    private const float BallPulsePhase = 0.75f;

    private const string SettingsPath = "game_settings";

    private static readonly Color _clearColor = new(0.04f, 0.04f, 0.06f, 1f);
    private static readonly Color _courtColor = new(0.7f, 0.82f, 1f, 1f);
    private IEngine<GameLayer>? _engine;
    private ICamera<GameLayer>? _camera;
    private ILineRender<GameLayer>? _courtBorder;
    private ILineRender<GameLayer>? _courtCenterLine;
    private SimulationState _lastState = default;
    private string? _title;


    /// <summary>Gets the simulation, or <see langword="null"/> before <c>Setup</c>.</summary>
    private PongSimulation? Simulation { get; set; }

    /// <summary>Gets the left paddle renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? LeftPaddle { get; set; }

    /// <summary>Gets the right paddle renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? RightPaddle { get; set; }

    /// <summary>Gets the ball renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? Ball { get; set; }

    /// <inheritdoc/>
    public void Setup(IEngine<GameLayer> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        // Setup blocks on the baked asset chain on purpose: the scene must be complete before the
        // first tick, and the engine API guide explicitly allows blocking startup loads.
        GameSettings settings = engine.Assets.LoadAsset<GameSettings>(SettingsPath).GetAwaiter().GetResult();
        SpriteAsset paddleSprite = engine.Assets.LoadAsset(settings.Paddle).GetAwaiter().GetResult();
        SpriteAsset ballSprite = engine.Assets.LoadAsset(settings.Ball).GetAwaiter().GetResult();
        ShaderAsset ballShader = engine.Assets.LoadAsset(settings.BallShader).GetAwaiter().GetResult();
        ShaderAsset courtShader = engine.Assets.LoadAsset(settings.CourtShader).GetAwaiter().GetResult();

        ICamera<GameLayer> camera = engine.CreateCamera(engine.Root);
        _camera = camera;
        camera.CullingMask = GameLayer.Court | GameLayer.Paddles | GameLayer.Ball;
        camera.Clear = ClearKind.Color;
        camera.ClearColor = _clearColor;
        camera.ZIndex = 0;
        UpdateCamera(engine);

        LeftPaddle = CreatePaddle(paddleSprite, GameLayer.Paddles, settings.LeftPaddleTint);
        RightPaddle = CreatePaddle(paddleSprite, GameLayer.Paddles, settings.RightPaddleTint);

        Ball = engine.CreateSpriteRender(engine.Root);
        Ball.Sprite = ballSprite;
        Ball.Layer = GameLayer.Ball;
        Ball.ZIndex = BallZIndex;
        Ball.Color = Color.White;
        Ball.Shader = ballShader;
        Ball.SetUniform("lastHit", (float)engine.Time.Time);

        CreateCourtLines(courtShader);

        Simulation = new PongSimulation(settings);
        Simulation.OnBallBounce += () => Ball.SetUniform("lastHit", (float)engine.Time.Time);

        _title = null;
        ApplySimulation();
        UpdateTitle(engine, Simulation);
    }

    /// <inheritdoc/>
    public void Tick()
    {
        IEngine<GameLayer> engine = _engine
                                    ?? throw new InvalidOperationException("PongGame.Tick requires a completed Setup.");
        PongSimulation simulation = Simulation
                                    ?? throw new InvalidOperationException("PongGame.Tick requires a completed Setup.");

        float leftInput = ReadLeftInput(engine.Input);
        ApplyActionKeys(engine, engine.Input, simulation);
        UpdateCamera(engine);

        float deltaTime = (float)engine.Time.DeltaTime;
        simulation.Step(deltaTime, leftInput, PongAi.Steer(simulation));
        ApplySimulation();
        UpdateTitle(engine, simulation);
    }

    /// <inheritdoc/>
    public void Teardown() => _engine?.Root.DetachAll();

    private static float ReadLeftInput(IInput input)
    {
        float leftInput = 0f;
        if (input.IsKeyDown(KeyCode.W))
        {
            leftInput += 1f;
        }

        if (input.IsKeyDown(KeyCode.S))
        {
            leftInput -= 1f;
        }

        return leftInput;
    }

    private void ApplyActionKeys(
        IEngine<GameLayer> engine,
        IInput input,
        PongSimulation simulation)
    {
        if (input.IsKeyPressed(KeyCode.Space))
        {
            simulation.TogglePause();
        }

        if (input.IsKeyPressed(KeyCode.R))
        {
            simulation.ResetMatch();
        }

        if (input.IsKeyPressed(KeyCode.Escape))
        {
            engine.Exit();
        }
    }

    private void UpdateTitle(IEngine<GameLayer> engine, PongSimulation simulation)
    {
        SimulationState state = simulation.CaptureState();
        if (_lastState == state)
        {
            return;
        }
        _lastState = state;
        string title = PongTitle.Format(state);

        engine.Window.Title = title;
        _title = title;
    }

    private ISpriteRender<GameLayer> CreatePaddle(SpriteAsset sprite, GameLayer layer, Color tint)
    {
        IEngine<GameLayer> engine = _engine
                                    ?? throw new InvalidOperationException(
                                        "PongGame.Setup must capture the engine first.");

        ISpriteRender<GameLayer> paddle = engine.CreateSpriteRender(engine.Root);
        paddle.Sprite = sprite;
        paddle.Layer = layer;
        paddle.ZIndex = PaddleZIndex;
        paddle.Color = tint;
        return paddle;
    }

    private void CreateCourtLines(ShaderAsset courtShader)
    {
        ILineRender<GameLayer> border = CreateCourtLine(courtShader, dash: 0f);
        ILineRender<GameLayer> centerLine = CreateCourtLine(courtShader, dash: 1f);

        // Center each stroke on an inset line so its outer edge lands on the visible court edge and
        // the full width stays inside the court.
        float inset = CourtLineWidth / 2f;
        float bottom = -PongSimulation.CourtHalfHeight + inset;
        float top = PongSimulation.CourtHalfHeight - inset;
        float left = -PongSimulation.CourtHalfWidth + inset;
        float right = PongSimulation.CourtHalfWidth - inset;

        border.Closed = true;
        border.Points =
        [
            new Vector2(left, bottom),
            new Vector2(right, bottom),
            new Vector2(right, top),
            new Vector2(left, top),
        ];

        centerLine.Closed = false;
        centerLine.Points = [new Vector2(0f, bottom), new Vector2(0f, top)];

        _courtBorder = border;
        _courtCenterLine = centerLine;
    }

    private ILineRender<GameLayer> CreateCourtLine(ShaderAsset shader, float dash)
    {
        IEngine<GameLayer> engine = _engine
                                    ?? throw new InvalidOperationException(
                                        "PongGame.Setup must capture the engine first.");

        ILineRender<GameLayer> line = engine.CreateLineRender(engine.Root);
        line.Layer = GameLayer.Court;
        line.ZIndex = CourtZIndex;
        line.Width = CourtLineWidth;
        line.Color = _courtColor;
        line.AntiAlias = true;
        line.Shader = shader;
        line.SetUniform("dash", dash);
        return line;
    }

    private void ApplySimulation()
    {
        PongSimulation simulation = Simulation
                                    ?? throw new InvalidOperationException(
                                        "PongGame.Setup must create the simulation first.");
        ISpriteRender<GameLayer> left = LeftPaddle
                                        ?? throw new InvalidOperationException(
                                            "PongGame.Setup must create the left paddle.");
        ISpriteRender<GameLayer> right = RightPaddle
                                         ?? throw new InvalidOperationException(
                                             "PongGame.Setup must create the right paddle.");
        ISpriteRender<GameLayer> ball = Ball
                                        ?? throw new InvalidOperationException("PongGame.Setup must create the ball.");

        left.LocalPosition = new Vector2(simulation.LeftPaddleX, simulation.LeftPaddleY);
        right.LocalPosition = new Vector2(simulation.RightPaddleX, simulation.RightPaddleY);
        ball.LocalPosition = simulation.BallPosition;
    }

    private void UpdateCamera(IEngine<GameLayer> engine)
    {
        ICamera<GameLayer> camera = _camera
                                    ?? throw new InvalidOperationException(
                                        "PongGame.Setup must create the camera.");

        // Fit the fixed court: a wider window keeps the height and adds clear space at the sides,
        // while a narrower window grows the visible height so the full width stays on screen.
        Vector2 framebuffer = engine.Window.Size;
        float aspect = framebuffer.Y > 0f
                       && float.IsFinite(framebuffer.X)
                       && float.IsFinite(framebuffer.Y)
            ? framebuffer.X / framebuffer.Y
            : PongSimulation.CourtHalfWidth / PongSimulation.CourtHalfHeight;
        float size = MathF.Max(PongSimulation.CourtHalfHeight, PongSimulation.CourtHalfWidth / aspect);
        if (!float.IsFinite(size) || size <= 0f)
        {
            size = PongSimulation.CourtHalfHeight;
        }

        camera.Size = size;
    }
}
