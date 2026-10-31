using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Input;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;

namespace Pong.Common;

public sealed class PongGame : IGame<GameLayer>
{
    /// <summary>The court half-height in world units (the court is 18 units tall).</summary>
    private const float CourtHalfHeight = 9f;

    private const int PaddleZIndex = 10;
    private const int BallZIndex = 20;
    private const string SettingsPath = "game_settings";
    private const float MinimumCourtHalfWidth = PongSimulation.PaddleInset + PongSimulation.PaddleHalfWidth;

    private static readonly Color _clearColor = new(0.04f, 0.04f, 0.06f, 1f);
    private IEngine<GameLayer>? _engine;
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

        ICamera<GameLayer> camera = engine.CreateCamera(engine.Root);
        camera.Size = CourtHalfHeight;
        camera.CullingMask = GameLayer.Court | GameLayer.Paddles | GameLayer.Ball;
        camera.Clear = ClearKind.Color;
        camera.ClearColor = _clearColor;
        camera.ZIndex = 0;

        LeftPaddle = CreatePaddle(paddleSprite, GameLayer.Paddles, settings.LeftPaddleTint);
        RightPaddle = CreatePaddle(paddleSprite, GameLayer.Paddles, settings.RightPaddleTint);

        Ball = engine.CreateSpriteRender(engine.Root);
        Ball.Sprite = ballSprite;
        Ball.Layer = GameLayer.Ball;
        Ball.ZIndex = BallZIndex;
        Ball.Tint = Color.White;

        Simulation = new PongSimulation(settings, ComputeHalfWidth(engine), CourtHalfHeight);
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

        float halfWidth = ComputeHalfWidth(engine);
        if (Math.Abs(halfWidth - simulation.HalfWidth) > 0.01f)
        {
            simulation.ResizeCourt(halfWidth, CourtHalfHeight);
        }

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
        string title = PongTitle.Format(simulation);
        if (string.Equals(title, _title, StringComparison.Ordinal))
        {
            return;
        }

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
        paddle.Tint = tint;
        return paddle;
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

    private float ComputeHalfWidth(IEngine<GameLayer> engine)
    {
        Vector2 framebuffer = engine.Window.Size;
        if (framebuffer.Y <= 0f)
        {
            return MinimumCourtHalfWidth;
        }

        // A tiny or degenerate window must not produce a court narrower than the simulation can
        // represent (a paddle inset plus its half-width); keep the value finite as well.
        float halfWidth = CourtHalfHeight * (framebuffer.X / framebuffer.Y);
        return float.IsFinite(halfWidth)
            ? MathF.Max(halfWidth, MinimumCourtHalfWidth)
            : MinimumCourtHalfWidth;
    }
}
