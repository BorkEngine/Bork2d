using System.Globalization;
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

    // The score, match state, and control prompt are the HUD; they draw above every gameplay
    // element.
    private const int HudZIndex = 30;

    // The wordmark sits behind every gameplay element; the center line draws over it.
    private const int WordmarkZIndex = 1;

    // The baked artboard is one world unit high, so the wordmark is about 3.9 units of visible
    // letters inside the 32x18 court.
    private const float WordmarkScale = 5.5f;

    // The score is 1.5 world units per em; its baseline sits below the top court line and the left
    // and right scores are right- and left-aligned around a 0.8-unit center gap.
    private const float ScoreSize = 1.5f;
    private const float ScoreBaselineY = 7.2f;
    private const float ScoreGap = 0.4f;

    // The state line is 1.0 world units per em. Its baseline sits at y = 6.0, so the tallest glyphs
    // stop around y = 6.7, clear of the score baseline at y = 7.2 and the top court line at y = 9.
    private const float StateSize = 1.0f;
    private const float StateBaselineY = 6.0f;

    // The prompt is 0.65 world units per em. Its baseline sits at y = -8.0, so its descenders stay
    // above the bottom court line at y = -9 while the 40-character playing prompt is under 16 world
    // units wide and stays inside the 32-unit court width.
    private const float PromptSize = 0.65f;
    private const float PromptBaselineY = -8.0f;

    // A fixed phase keeps the ball's time-driven pulse legible from the first frame instead of
    // starting at the dimmest point of its cycle.
    private const float BallPulsePhase = 0.75f;

    private const string SettingsPath = "game_settings";

    /// <summary>The canonical path of the built-in SDF fragment shader the wordmark uses.</summary>
    private const string SdfShaderPath = "shader/sdf";

    private static readonly Color _clearColor = new(0.04f, 0.04f, 0.06f, 1f);
    private static readonly Color _courtColor = new(0.7f, 0.82f, 1f, 1f);
    private static readonly Color _wordmarkColor = new(0.65f, 0.78f, 1f, 0.15f);

    // The prompt reuses the court color at reduced alpha so it reads as secondary HUD text.
    private static readonly Color _promptColor = new(0.7f, 0.82f, 1f, 0.65f);
    private IEngine<GameLayer>? _engine;
    private ICamera<GameLayer>? _camera;
    private ILineRender<GameLayer>? _courtBorder;
    private ILineRender<GameLayer>? _courtCenterLine;
    private int _lastLeftScore = -1;
    private int _lastRightScore = -1;


    /// <summary>Gets the simulation, or <see langword="null"/> before <c>Setup</c>.</summary>
    private PongSimulation? Simulation { get; set; }

    /// <summary>Gets the left paddle renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? LeftPaddle { get; set; }

    /// <summary>Gets the right paddle renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? RightPaddle { get; set; }

    /// <summary>Gets the ball renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? Ball { get; set; }

    /// <summary>Gets the wordmark renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private ISpriteRender<GameLayer>? Wordmark { get; set; }

    /// <summary>Gets the left score renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private IFontRender<GameLayer>? LeftScore { get; set; }

    /// <summary>Gets the right score renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private IFontRender<GameLayer>? RightScore { get; set; }

    /// <summary>Gets the centered match-state renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private IFontRender<GameLayer>? StateText { get; set; }

    /// <summary>Gets the centered control-prompt renderable, or <see langword="null"/> before <c>Setup</c>.</summary>
    private IFontRender<GameLayer>? PromptText { get; set; }

    /// <inheritdoc/>
    public async Task Setup(IEngine<GameLayer> engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;

        // Await the baked asset chain in order: the scene must be complete before the first tick,
        // and the host blocks on this task before renderer readiness and the first tick.
        GameSettings settings = await engine.Assets.LoadAsset<GameSettings>(SettingsPath);
        SpriteAsset paddleSprite = await engine.Assets.LoadAsset(settings.Paddle);
        SpriteAsset ballSprite = await engine.Assets.LoadAsset(settings.Ball);
        ShaderAsset ballShader = await engine.Assets.LoadAsset(settings.BallShader);
        ShaderAsset courtShader = await engine.Assets.LoadAsset(settings.CourtShader);
        SpriteAsset wordmarkSprite = await engine.Assets.LoadAsset(settings.Wordmark);
        FontAsset hudFont = await engine.Assets.LoadAsset(settings.ScoreFont);

        // The built-in pack is mounted below the game packs, so the SDF shader is loaded by its
        // canonical path instead of being referenced from game_settings.toml: the game pack does not
        // bake shader/sdf, and a settings reference to it would fail bake reference validation.
        ShaderAsset sdfShader = await engine.Assets.LoadAsset<ShaderAsset>(SdfShaderPath);

        ICamera<GameLayer> camera = engine.CreateCamera(engine.Root);
        _camera = camera;
        camera.CullingMask = GameLayer.Court | GameLayer.Paddles | GameLayer.Ball | GameLayer.Hud;
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

        Wordmark = engine.CreateSpriteRender(engine.Root);
        Wordmark.Sprite = wordmarkSprite;
        Wordmark.Shader = sdfShader;
        Wordmark.Layer = GameLayer.Court;
        Wordmark.ZIndex = WordmarkZIndex;
        Wordmark.LocalPosition = Vector2.Zero;
        Wordmark.LocalScale = new Vector2(WordmarkScale, WordmarkScale);
        Wordmark.Color = _wordmarkColor;

        // The HUD uses the baked font and the renderer's default shader/sdf. The 2D pass orders the
        // score, state, and prompt above the gameplay layers; they share one ZIndex and font asset,
        // so they stay contiguous and merge into one font run.
        LeftScore = CreateHudText(
            engine,
            hudFont,
            settings.LeftPaddleTint,
            TextAlignment.Right,
            ScoreSize,
            new Vector2(-ScoreGap, ScoreBaselineY));
        RightScore = CreateHudText(
            engine,
            hudFont,
            settings.RightPaddleTint,
            TextAlignment.Left,
            ScoreSize,
            new Vector2(ScoreGap, ScoreBaselineY));
        StateText = CreateHudText(
            engine,
            hudFont,
            _courtColor,
            TextAlignment.Center,
            StateSize,
            new Vector2(0f, StateBaselineY));
        PromptText = CreateHudText(
            engine,
            hudFont,
            _promptColor,
            TextAlignment.Center,
            PromptSize,
            new Vector2(0f, PromptBaselineY));

        Simulation = new PongSimulation(settings);
        Simulation.OnBallBounce += () => Ball.SetUniform("lastHit", (float)engine.Time.Time);

        ApplySimulation();
        UpdateScore();
        UpdateHud();
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
        UpdateScore();
        UpdateHud();
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

    private void UpdateHud()
    {
        PongSimulation simulation = Simulation
                                    ?? throw new InvalidOperationException(
                                        "PongGame.Setup must create the simulation first.");
        IFontRender<GameLayer> state = StateText
                                       ?? throw new InvalidOperationException(
                                           "PongGame.Setup must create the match-state text.");
        IFontRender<GameLayer> prompt = PromptText
                                        ?? throw new InvalidOperationException(
                                            "PongGame.Setup must create the control prompt.");

        // PongHud returns fixed literals, so this is allocation-free every frame; assigning the
        // same text keeps the cached layout, and only a changed state word or prompt rebuilds it.
        SimulationState snapshot = simulation.CaptureState();
        state.Text = PongHud.State(snapshot);
        prompt.Text = PongHud.Prompt(snapshot);
    }

    private void UpdateScore()
    {
        PongSimulation simulation = Simulation
                                    ?? throw new InvalidOperationException(
                                        "PongGame.Setup must create the simulation first.");
        IFontRender<GameLayer> left = LeftScore
                                       ?? throw new InvalidOperationException(
                                           "PongGame.Setup must create the left score.");
        IFontRender<GameLayer> right = RightScore
                                        ?? throw new InvalidOperationException(
                                            "PongGame.Setup must create the right score.");

        // Assign the text only on a score change so a static score keeps its cached layout.
        if (simulation.LeftScore != _lastLeftScore)
        {
            _lastLeftScore = simulation.LeftScore;
            left.Text = simulation.LeftScore.ToString(CultureInfo.InvariantCulture);
        }

        if (simulation.RightScore != _lastRightScore)
        {
            _lastRightScore = simulation.RightScore;
            right.Text = simulation.RightScore.ToString(CultureInfo.InvariantCulture);
        }
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

    private IFontRender<GameLayer> CreateHudText(
        IEngine<GameLayer> engine,
        FontAsset font,
        Color color,
        TextAlignment alignment,
        float size,
        Vector2 position)
    {
        IFontRender<GameLayer> text = engine.CreateFontRender(engine.Root);
        text.Font = font;
        text.Layer = GameLayer.Hud;
        text.ZIndex = HudZIndex;
        text.Alignment = alignment;
        text.Size = size;
        text.Color = color;
        text.LocalPosition = position;
        return text;
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
