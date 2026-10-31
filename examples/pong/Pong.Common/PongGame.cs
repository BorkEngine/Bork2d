using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Input;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Microsoft.Extensions.Logging;

namespace Pong.Common;

/// <summary>
/// The Pong game: it loads the baked assets and the settings, builds the camera/world/entity tree,
/// and mirrors the engine-free <see cref="PongSimulation"/> onto the scene every tick.
/// </summary>
/// <remarks>
/// <para>
/// The game works in the paced headless host, the windowed host, and the non-interactive offscreen
/// host. Headless and offscreen modes never read <c>IInput</c> or <c>IWindow</c> and advance the
/// simulation with a fixed 1/60 s step; windowed mode reads the player input and the frame delta,
/// updates the window title when the state changes, and recomputes the court aspect from the
/// framebuffer so a resize keeps the court proportional. The right paddle is always driven by
/// <see cref="PongAi"/>.
/// </para>
/// <para>
/// Scores, pause/resume/reset transitions, and the final teardown are logged at Information. The
/// title is formatted and compared each tick but assigned only when it changes, and the game never
/// logs per frame. The exposed read-only properties exist for the headless acceptance run.
/// </para>
/// </remarks>
public sealed class PongGame : IGame<GameLayer>
{
    /// <summary>The court half-height in world units (the court is 18 units tall).</summary>
    public const float CourtHalfHeight = 9f;

    /// <summary>The hard-coded aspect used by the headless host.</summary>
    public const float HeadlessAspect = 16f / 9f;

    /// <summary>The fixed simulation step in headless mode.</summary>
    public const float FixedDeltaTime = 1f / 60f;

    private const int PaddleZIndex = 10;
    private const int BallZIndex = 20;
    private const string SettingsPath = "game_settings";
    private const float MinimumCourtHalfWidth = PongSimulation.PaddleInset + PongSimulation.PaddleHalfWidth;

    private static readonly Color _clearColor = new(0.04f, 0.04f, 0.06f, 1f);

    private readonly bool _interactive;
    private readonly int? _tickLimit;
    private IEngine<GameLayer>? _engine;
    private ILogger<PongGame>? _logger;
    private string? _title;
    private int _lastLeftScore;
    private int _lastRightScore;
    private PongSide _lastWinner;

    /// <summary>
    /// Initializes a new instance of the <see cref="PongGame"/> class.
    /// </summary>
    /// <param name="interactive">
    /// <see langword="true"/> for the windowed host, which reads the frame delta, the input, and the
    /// framebuffer size; <see langword="false"/> for the headless and offscreen hosts, which use a
    /// fixed step and a 16:9 court.
    /// </param>
    /// <param name="tickLimit">
    /// The number of completed headless, windowed, or offscreen ticks after which the game calls
    /// <see cref="IEngine{TLayer}.Exit"/>, or <see langword="null"/> for an interactive windowed run.
    /// The headless host always supplies a limit (120 by default from the command line).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickLimit"/> is less than 1.</exception>
    public PongGame(bool interactive, int? tickLimit = null)
    {
        if (tickLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(tickLimit),
                tickLimit,
                "The tick limit must be at least 1 when it is set.");
        }

        _interactive = interactive;
        _tickLimit = tickLimit;
    }

    /// <summary>Gets the number of <c>Setup</c> calls.</summary>
    public int SetupCount { get; private set; }

    /// <summary>Gets the number of completed <c>Tick</c> calls.</summary>
    public int TickCount { get; private set; }

    /// <summary>Gets the number of <c>Teardown</c> calls.</summary>
    public int TeardownCount { get; private set; }

    /// <summary>Gets the settings loaded from <c>game_settings</c>.</summary>
    public GameSettings Settings { get; private set; }

    /// <summary>Gets the loaded paddle sprite, or <see langword="null"/> before <c>Setup</c>.</summary>
    public SpriteAsset? PaddleSprite { get; private set; }

    /// <summary>Gets the loaded ball sprite, or <see langword="null"/> before <c>Setup</c>.</summary>
    public SpriteAsset? BallSprite { get; private set; }

    /// <summary>Gets the loaded paddle texture descriptor, or <see langword="null"/> before <c>Setup</c>.</summary>
    public TextureDescriptor? PaddleTexture { get; private set; }

    /// <summary>Gets the loaded ball texture descriptor, or <see langword="null"/> before <c>Setup</c>.</summary>
    public TextureDescriptor? BallTexture { get; private set; }

    /// <summary>Gets the number of bytes in the paddle's raw RGBA8 payload.</summary>
    public int PaddlePixelCount { get; private set; }

    /// <summary>Gets the number of bytes in the ball's raw RGBA8 payload.</summary>
    public int BallPixelCount { get; private set; }

    /// <summary>Gets the number of visible entries in the mounted pack during <c>Setup</c>.</summary>
    public int PackEntryCount { get; private set; }

    /// <summary>Gets the simulation, or <see langword="null"/> before <c>Setup</c>.</summary>
    public PongSimulation? Simulation { get; private set; }

    /// <summary>Gets the left paddle renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    public ISpriteRender<GameLayer>? LeftPaddle { get; private set; }

    /// <summary>Gets the right paddle renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    public ISpriteRender<GameLayer>? RightPaddle { get; private set; }

    /// <summary>Gets the ball renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    public ISpriteRender<GameLayer>? Ball { get; private set; }

    /// <summary>Gets the world transform that owns the paddles and the ball.</summary>
    public ITransform? World { get; private set; }

    /// <inheritdoc/>
    public void Setup(IEngine<GameLayer> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        SetupCount++;
        _engine = engine;
        _logger = engine.LoggerFactory.CreateLogger<PongGame>();

        // Setup blocks on the baked asset chain on purpose: the scene must be complete before the
        // first tick, and the engine API guide explicitly allows blocking startup loads.
        Settings = engine.Assets.LoadAsset<GameSettings>(SettingsPath).GetAwaiter().GetResult();
        PaddleSprite = engine.Assets.LoadAsset(Settings.Paddle).GetAwaiter().GetResult();
        BallSprite = engine.Assets.LoadAsset(Settings.Ball).GetAwaiter().GetResult();
        PaddleTexture = engine.Assets.LoadAsset(PaddleSprite.Texture).GetAwaiter().GetResult();
        BallTexture = engine.Assets.LoadAsset(BallSprite.Texture).GetAwaiter().GetResult();
        PaddlePixelCount = engine.Assets.LoadData(PaddleTexture.Payload).GetAwaiter().GetResult().Length;
        BallPixelCount = engine.Assets.LoadData(BallTexture.Payload).GetAwaiter().GetResult().Length;
        PackEntryCount = engine.Assets.Browse().Count;

        _logger.LogInformation(
            "Pong setup: interactive={Interactive}, packEntries={PackEntries}, "
                + "paddleSprite={PaddleSprite}, paddleTexture={PaddleTexture}, "
                + "ballSprite={BallSprite}, ballTexture={BallTexture}.",
            _interactive,
            PackEntryCount,
            Settings.Paddle.Path,
            PaddleSprite.Texture.Path,
            Settings.Ball.Path,
            BallSprite.Texture.Path);

        ICamera<GameLayer> camera = engine.CreateCamera(engine.Root);
        camera.Size = CourtHalfHeight;
        camera.CullingMask = GameLayer.Court | GameLayer.Paddles | GameLayer.Ball;
        camera.Clear = ClearKind.Color;
        camera.ClearColor = _clearColor;
        camera.ZIndex = 0;

        World = engine.CreateTransform(engine.Root);

        LeftPaddle = CreatePaddle(GameLayer.Paddles, Settings.LeftPaddleTint);
        RightPaddle = CreatePaddle(GameLayer.Paddles, Settings.RightPaddleTint);

        Ball = engine.CreateSpriteRender(World);
        Ball.Sprite = BallSprite;
        Ball.Layer = GameLayer.Ball;
        Ball.ZIndex = BallZIndex;
        Ball.Tint = Color.White;

        Simulation = new PongSimulation(Settings, ComputeHalfWidth(engine), CourtHalfHeight);
        _lastLeftScore = Simulation.LeftScore;
        _lastRightScore = Simulation.RightScore;
        _lastWinner = Simulation.Winner;
        _title = null;
        ApplySimulation();

        if (_interactive)
        {
            UpdateTitle(engine, Simulation);
        }
    }

    /// <inheritdoc/>
    public void Tick()
    {
        TickCount++;

        IEngine<GameLayer> engine = _engine
            ?? throw new InvalidOperationException("PongGame.Tick requires a completed Setup.");
        PongSimulation simulation = Simulation
            ?? throw new InvalidOperationException("PongGame.Tick requires a completed Setup.");

        if (_interactive)
        {
            float leftInput = ReadLeftInput(engine.Input);
            ApplyActionKeys(engine, engine.Input, simulation);

            float halfWidth = ComputeHalfWidth(engine);
            if (halfWidth != simulation.HalfWidth)
            {
                simulation.ResizeCourt(halfWidth, CourtHalfHeight);
            }

            float deltaTime = (float)engine.Time.DeltaTime;
            simulation.Step(deltaTime, leftInput, PongAi.Steer(simulation));
            LogSimulationChanges(simulation);
            ApplySimulation();
            UpdateTitle(engine, simulation);
        }
        else
        {
            simulation.Step(FixedDeltaTime, leftInput: 0f, PongAi.Steer(simulation));
            ApplySimulation();
        }

        if (_tickLimit is { } tickLimit && TickCount >= tickLimit)
        {
            engine.Exit();
        }
    }

    /// <inheritdoc/>
    public void Teardown()
    {
        TeardownCount++;

        if (Simulation is { } simulation)
        {
            _logger?.LogInformation(
                "Pong teardown: left={LeftScore}, right={RightScore}, winner={Winner}, "
                    + "ticks={TickCount}.",
                simulation.LeftScore,
                simulation.RightScore,
                simulation.Winner,
                TickCount);
        }

        _engine?.Root.DetachAll();
    }

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
            TogglePause(simulation);
        }

        if (input.IsKeyPressed(KeyCode.R))
        {
            ResetMatch(simulation);
        }

        if (input.IsKeyPressed(KeyCode.Escape))
        {
            engine.Exit();
        }
    }

    private void TogglePause(PongSimulation simulation)
    {
        bool wasPaused = simulation.IsPaused;
        simulation.TogglePause();
        if (simulation.IsPaused == wasPaused)
        {
            return;
        }

        if (simulation.IsPaused)
        {
            _logger?.LogInformation(
                "Pong paused: left={LeftScore}, right={RightScore}.",
                simulation.LeftScore,
                simulation.RightScore);
        }
        else
        {
            _logger?.LogInformation(
                "Pong resumed: left={LeftScore}, right={RightScore}.",
                simulation.LeftScore,
                simulation.RightScore);
        }
    }

    private void ResetMatch(PongSimulation simulation)
    {
        simulation.ResetMatch();
        _lastLeftScore = simulation.LeftScore;
        _lastRightScore = simulation.RightScore;
        _lastWinner = simulation.Winner;
        _logger?.LogInformation(
            "Pong match reset: left={LeftScore}, right={RightScore}.",
            simulation.LeftScore,
            simulation.RightScore);
    }

    private void LogSimulationChanges(PongSimulation simulation)
    {
        if (simulation.LeftScore != _lastLeftScore || simulation.RightScore != _lastRightScore)
        {
            _lastLeftScore = simulation.LeftScore;
            _lastRightScore = simulation.RightScore;
            _logger?.LogInformation(
                "Pong score: left={LeftScore}, right={RightScore}.",
                simulation.LeftScore,
                simulation.RightScore);
        }

        if (simulation.Winner != _lastWinner)
        {
            _lastWinner = simulation.Winner;
            if (simulation.Winner != PongSide.None)
            {
                _logger?.LogInformation(
                    "Pong match over: winner={Winner}, left={LeftScore}, right={RightScore}.",
                    simulation.Winner,
                    simulation.LeftScore,
                    simulation.RightScore);
            }
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

    private ISpriteRender<GameLayer> CreatePaddle(GameLayer layer, Color tint)
    {
        IEngine<GameLayer> engine = _engine
            ?? throw new InvalidOperationException("PongGame.Setup must capture the engine first.");
        ITransform world = World
            ?? throw new InvalidOperationException("PongGame.Setup must create the world first.");

        ISpriteRender<GameLayer> paddle = engine.CreateSpriteRender(world);
        paddle.Sprite = PaddleSprite;
        paddle.Layer = layer;
        paddle.ZIndex = PaddleZIndex;
        paddle.Tint = tint;
        return paddle;
    }

    private void ApplySimulation()
    {
        PongSimulation simulation = Simulation
            ?? throw new InvalidOperationException("PongGame.Setup must create the simulation first.");
        ISpriteRender<GameLayer> left = LeftPaddle
            ?? throw new InvalidOperationException("PongGame.Setup must create the left paddle.");
        ISpriteRender<GameLayer> right = RightPaddle
            ?? throw new InvalidOperationException("PongGame.Setup must create the right paddle.");
        ISpriteRender<GameLayer> ball = Ball
            ?? throw new InvalidOperationException("PongGame.Setup must create the ball.");

        left.LocalPosition = new Vector2(simulation.LeftPaddleX, simulation.LeftPaddleY);
        right.LocalPosition = new Vector2(simulation.RightPaddleX, simulation.RightPaddleY);
        ball.LocalPosition = simulation.BallPosition;
    }

    private float ComputeHalfWidth(IEngine<GameLayer> engine)
    {
        if (!_interactive)
        {
            return CourtHalfHeight * HeadlessAspect;
        }

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
