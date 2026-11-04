using System.Diagnostics;
using System.Numerics;

namespace Pong.Common;

public sealed class PongSimulation
{
    /// <summary>The fixed court half-width in world units; the court is 32 wide in a 16:9 frame.</summary>
    public const float CourtHalfWidth = 16f;

    /// <summary>The fixed court half-height in world units; the court is 18 tall in a 16:9 frame.</summary>
    public const float CourtHalfHeight = 9f;

    /// <summary>The paddle half-width in world units (the full paddle is 0.25 wide).</summary>
    public const float PaddleHalfWidth = 0.125f;

    /// <summary>The paddle half-height in world units (the full paddle is 1.5 tall).</summary>
    public const float PaddleHalfHeight = 0.75f;

    /// <summary>The ball radius in world units (the full ball is 0.75 in diameter).</summary>
    public const float BallRadius = 0.375f;

    /// <summary>The distance from a side wall to the center of that side's paddle.</summary>
    public const float PaddleInset = 1f;

    /// <summary>The opening serve delay in seconds.</summary>
    public const float InitialServeDelay = 0.5f;

    /// <summary>The serve delay after a point in seconds.</summary>
    public const float ServeDelay = 0.75f;

    /// <summary>The maximum paddle-hit bounce angle from the horizontal in degrees.</summary>
    public const float MaximumBounceAngleDegrees = 60f;

    private readonly GameSettings _settings;
    private readonly Random _random;
    private float _leftPaddleY;
    private float _rightPaddleY;
    private Vector2 _ballPosition;
    private Vector2 _ballVelocity;
    private float _serveTimer;
    private float _serveDirectionX;
    private int _bounceCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="PongSimulation"/> class in the opening-serve
    /// state with both paddles centered.
    /// </summary>
    /// <param name="settings">The game settings that supply the speeds, winning score, and tints.</param>
    public PongSimulation(GameSettings settings)
    {
        _settings = settings;
        _random = new Random((int)(Stopwatch.GetTimestamp() % int.MaxValue));
        ResetMatch();
    }

    /// <summary>Gets the fixed court half-width in world units.</summary>
    public float HalfWidth => CourtHalfWidth;

    /// <summary>Gets the greatest allowed paddle Y (the paddle just touches the top wall).</summary>
    public float MaxPaddleY => CourtHalfHeight - PaddleHalfHeight;

    /// <summary>Gets the left paddle center X.</summary>
    public float LeftPaddleX => -CourtHalfWidth + PaddleInset;

    /// <summary>Gets the right paddle center X.</summary>
    public float RightPaddleX => CourtHalfWidth - PaddleInset;

    /// <summary>Gets the left paddle center Y.</summary>
    public float LeftPaddleY => _leftPaddleY;

    /// <summary>Gets the right paddle center Y.</summary>
    public float RightPaddleY => _rightPaddleY;

    /// <summary>Gets the ball center in world units.</summary>
    public Vector2 BallPosition => _ballPosition;

    /// <summary>Gets the ball velocity in world units per second.</summary>
    public Vector2 BallVelocity => _ballVelocity;

    /// <summary>Gets the left side's score.</summary>
    public int LeftScore { get; private set; }

    /// <summary>Gets the right side's score.</summary>
    public int RightScore { get; private set; }

    /// <summary>Gets a value indicating whether the simulation is paused.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Gets a value indicating whether the match has a winner.</summary>
    public bool IsMatchOver => Winner != PongSide.None;

    /// <summary>
    /// Gets a value indicating whether the ball is waiting for its serve delay. The ball is at the
    /// center and stationary while this is <see langword="true"/>.
    /// </summary>
    public bool IsServing { get; private set; }

    /// <summary>Gets the winning side, or <see cref="PongSide.None"/> while the match is running.</summary>
    public PongSide Winner { get; private set; }

    public event Action OnBallBounce = delegate { };

    /// <summary>
    /// Advances the simulation by one step.
    /// </summary>
    /// <param name="deltaTime">The step length in seconds; a non-finite or non-positive value is ignored.</param>
    /// <param name="leftInput">The left paddle input, clamped to <c>[-1, 1]</c>.</param>
    /// <param name="rightInput">The right paddle input, clamped to <c>[-1, 1]</c>.</param>
    /// <remarks>
    /// A paused or finished match does not advance. The paddles move at the configured paddle speed
    /// while the serve timer runs; the ball stays at the center until the timer expires.
    /// </remarks>
    public void Step(float deltaTime, float leftInput, float rightInput)
    {
        if (IsPaused || IsMatchOver || !float.IsFinite(deltaTime) || deltaTime <= 0f)
        {
            return;
        }

        MovePaddles(deltaTime, leftInput, rightInput);

        if (IsServing)
        {
            _serveTimer -= deltaTime;
            if (_serveTimer <= 0f)
            {
                LaunchBall();
            }

            return;
        }

        AdvanceBall(deltaTime);
    }

    /// <summary>
    /// Pauses the simulation. A finished match cannot be paused, and pausing twice is a no-op.
    /// </summary>
    public void Pause()
    {
        if (!IsMatchOver)
        {
            IsPaused = true;
        }
    }

    /// <summary>Resumes a paused simulation; resuming a running simulation is a no-op.</summary>
    public void Resume() => IsPaused = false;

    /// <summary>Toggles between paused and running; a finished match stays unpaused.</summary>
    public void TogglePause()
    {
        if (IsPaused)
        {
            Resume();
        }
        else
        {
            Pause();
        }
    }

    /// <summary>
    /// Restarts the match: clears both scores, centers the paddles and the ball, clears pause and
    /// match-over, and restarts the opening serve with <see cref="InitialServeDelay"/>.
    /// </summary>
    public void ResetMatch()
    {
        LeftScore = 0;
        RightScore = 0;
        IsPaused = false;
        Winner = PongSide.None;
        _leftPaddleY = 0f;
        _rightPaddleY = 0f;
        BeginServe(PongSide.Left, InitialServeDelay);
    }

    public SimulationState CaptureState()
    {
        return new SimulationState
        {
            LeftScore = LeftScore,
            RightScore = RightScore,
            IsPaused = IsPaused,
            IsServing = IsServing,
            Winner = Winner
        };
    }

    private void MovePaddles(float deltaTime, float leftInput, float rightInput)
    {
        // Math.Clamp(NaN, ...) returns NaN, so a non-finite input is treated as neutral.
        float left = float.IsFinite(leftInput) ? Math.Clamp(leftInput, -1f, 1f) : 0f;
        float right = float.IsFinite(rightInput) ? Math.Clamp(rightInput, -1f, 1f) : 0f;
        _leftPaddleY = ClampPaddle(_leftPaddleY + (left * _settings.PaddleSpeed * deltaTime));
        _rightPaddleY = ClampPaddle(_rightPaddleY + (right * _settings.PaddleSpeed * deltaTime));
    }

    private void AdvanceBall(float deltaTime)
    {
        _ballPosition += _ballVelocity * deltaTime;

        if (_ballPosition.Y + BallRadius > CourtHalfHeight)
        {
            _ballPosition.Y = CourtHalfHeight - BallRadius;
            _ballVelocity.Y = -MathF.Abs(_ballVelocity.Y);
            OnBallBounce.Invoke();
        }
        else if (_ballPosition.Y - BallRadius < -CourtHalfHeight)
        {
            _ballPosition.Y = -CourtHalfHeight + BallRadius;
            _ballVelocity.Y = MathF.Abs(_ballVelocity.Y);
            OnBallBounce.Invoke();
        }

        TryBounceOffPaddle(left: true);
        TryBounceOffPaddle(left: false);

        if (_ballPosition.X < -CourtHalfWidth)
        {
            ScorePoint(PongSide.Right);
        }
        else if (_ballPosition.X > CourtHalfWidth)
        {
            ScorePoint(PongSide.Left);
        }
    }

    private void TryBounceOffPaddle(bool left)
    {
        float paddleY = left ? _leftPaddleY : _rightPaddleY;
        float paddleFace = left
            ? LeftPaddleX + PaddleHalfWidth
            : RightPaddleX - PaddleHalfWidth;
        float direction = left ? 1f : -1f;
        bool towardPaddle = left ? _ballVelocity.X < 0f : _ballVelocity.X > 0f;
        if (!towardPaddle)
        {
            return;
        }

        float ballEdge = left ? _ballPosition.X - BallRadius : _ballPosition.X + BallRadius;
        bool atFace = left ? ballEdge <= paddleFace : ballEdge >= paddleFace;
        // The ball must still be on the court side of the face; otherwise it already missed the
        // paddle and a later Y overlap must not reflect it from behind.
        bool courtSide = left ? _ballPosition.X >= paddleFace : _ballPosition.X <= paddleFace;
        bool withinReach = MathF.Abs(_ballPosition.Y - paddleY) <= PaddleHalfHeight + BallRadius;
        bool insideCourt = left ? _ballPosition.X >= -CourtHalfWidth : _ballPosition.X <= CourtHalfWidth;
        if (!atFace || !courtSide || !withinReach || !insideCourt)
        {
            return;
        }

        _bounceCount += 1;
        float offset = Math.Clamp((_ballPosition.Y - paddleY) / PaddleHalfHeight, -1f, 1f);
        float angle = offset * MaximumBounceAngleDegrees * (MathF.PI / 180f);
        float speed = MathF.Abs(_settings.BallSpeed);
        _ballPosition.X = paddleFace + (direction * BallRadius);
        _ballVelocity = new Vector2(direction * MathF.Cos(angle), MathF.Sin(angle))
                        * speed * (1f + (_bounceCount * _settings.PaddleSpeedMultiplier));
        OnBallBounce.Invoke();
    }

    private void ScorePoint(PongSide scorer)
    {
        if (scorer == PongSide.Left)
        {
            LeftScore++;
        }
        else
        {
            RightScore++;
        }

        PongSide conceding = scorer == PongSide.Left ? PongSide.Right : PongSide.Left;
        if (LeftScore >= _settings.WinningScore || RightScore >= _settings.WinningScore)
        {
            Winner = LeftScore >= _settings.WinningScore ? PongSide.Left : PongSide.Right;
            IsServing = false;
            _serveTimer = 0f;
            _ballPosition = Vector2.Zero;
            _ballVelocity = Vector2.Zero;
            return;
        }

        BeginServe(conceding, ServeDelay);
    }

    private void BeginServe(PongSide toward, float delay)
    {
        _ballPosition = Vector2.Zero;
        _ballVelocity = Vector2.Zero;
        _serveTimer = delay;
        _serveDirectionX = toward == PongSide.Left ? -1f : 1f;
        _bounceCount = 0;
        IsServing = true;
    }

    private void LaunchBall()
    {
        IsServing = false;
        _serveTimer = 0f;
        var direction = Vector2.Normalize(new Vector2(_serveDirectionX, (_random.NextSingle() - 0.5f) * 2f));
        _ballVelocity = direction * _settings.BallSpeed;
    }

    private float ClampPaddle(float y) => Math.Clamp(y, -MaxPaddleY, MaxPaddleY);
}
