using Bork2d.Engine.Assets;
using Bork2d.Engine.Scene;
using Bork2d.Runtime;
using Pong.Common;

namespace Pong.Desktop;

/// <summary>
/// The Pong desktop host: the headless and offscreen acceptance smokes or the windowed example.
/// </summary>
/// <remarks>
/// <para>
/// The imported <c>build/Bork2d.Assets.Copy.targets</c> build the <c>Pong.Assets</c> executable out
/// of process, bake <c>assets.pck</c>, and copy it next to this app, so all three modes mount
/// <c>AppContext.BaseDirectory/assets.pck</c>.
/// </para>
/// <para>
/// Headless mode loads the pack, runs <see cref="PongGame"/> through the paced headless host for
/// <c>N</c> ticks (<c>--headless</c> defaults to 120; <c>--headless --ticks N</c> overrides),
/// asserts the full baked asset chain and exactly <c>N</c> completed ticks, and exits 0; a failed
/// assertion prints the diagnostic and returns 1. Windowed mode runs the interactive game, or exits
/// after exactly <c>N</c> ticks when <c>--ticks N</c> is supplied. Offscreen mode runs the
/// non-interactive game through the offscreen Vulkan host and exits after exactly <c>N</c> ticks;
/// its renderer clears and discards frames. The project is published with <c>PublishAot</c> and a
/// narrow Silk.NET <c>IL3000;IL3002</c> suppression, and is the engine's NativeAOT gate.
/// </para>
/// </remarks>
internal static class Program
{
    private const string WindowTitle = "Pong";
    private const int WindowWidth = 1280;
    private const int WindowHeight = 720;
    private const double MaxDeltaTime = 0.25;
    private const string PackFileName = "assets.pck";

    private const int ExpectedPackEntryCount = 7;
    private const int ExpectedPaddleWidth = 16;
    private const int ExpectedPaddleHeight = 96;
    private const int ExpectedBallWidth = 64;
    private const int ExpectedBallHeight = 64;
    private const string PaddleSpritePath = "sprites/paddle";
    private const string BallSpritePath = "sprites/ball";
    private const string PaddleTexturePath = "art/paddle";
    private const string BallTexturePath = "textures/ball";

    internal static int Main(string[] args)
    {
        if (!PongArguments.TryParse(args, out PongArguments arguments, out string error))
        {
            Console.Error.WriteLine($"Pong.Desktop: {error}");
            WriteUsage(Console.Error);
            return 2;
        }

        return arguments.Headless
            ? RunHeadless(arguments)
            : arguments.Offscreen
                ? RunOffscreen(arguments)
                : RunWindowed(arguments);
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine(
            "usage: Pong.Desktop [--headless [--ticks N] | --windowed [--ticks N] | "
                + "--offscreen --ticks N]");
        writer.WriteLine("  (no arguments)  run the interactive windowed Pong example (1280x720, resizable)");
        writer.WriteLine("  --windowed      run the interactive windowed Pong example");
        writer.WriteLine(
            "  --headless      run the headless acceptance run for 120 ticks; exits 0");
        writer.WriteLine(
            "  --ticks N       with --windowed/--offscreen: exit after exactly N completed ticks; "
                + "with --headless: override the default 120 (N >= 1)");
        writer.WriteLine(
            "  --offscreen     run the non-interactive offscreen acceptance run; requires --ticks N");
    }

    private static int RunHeadless(PongArguments arguments)
    {
        string packPath = Path.Combine(AppContext.BaseDirectory, PackFileName);

        try
        {
            Require(File.Exists(packPath), $"the baked pack must exist at '{packPath}'.");

            int tickLimit = arguments.TickLimit
                ?? throw new InvalidOperationException("--headless requires a tick limit.");

            var game = new PongGame(interactive: false, tickLimit);
            using (var app = new Bork2dAppBuilder<GameLayer>()
                .WithGame(game)
                .WithHeadless()
                .WithAssetsPack(packPath)
                .Build())
            {
                app.Run();

                Require(game.SetupCount == 1, "Setup must run exactly once.");
                Require(
                    game.TickCount == tickLimit,
                    $"--headless must run exactly {tickLimit} completed ticks; ran "
                        + $"{game.TickCount}.");
                Require(game.TeardownCount == 1, "Teardown must run exactly once.");
                Require(
                    game.PackEntryCount == ExpectedPackEntryCount,
                    $"the baked pack must contain exactly {ExpectedPackEntryCount} entries; got "
                        + $"{game.PackEntryCount}.");

                GameSettings settings = game.Settings;
                Require(
                    settings.Paddle.Path == PaddleSpritePath,
                    $"the baked paddle reference must be '{PaddleSpritePath}'; got "
                        + $"'{settings.Paddle.Path}'.");
                Require(
                    settings.Ball.Path == BallSpritePath,
                    $"the baked ball reference must be '{BallSpritePath}'; got '{settings.Ball.Path}'.");

                SpriteAsset paddleSprite = game.PaddleSprite
                    ?? throw new InvalidOperationException("Setup must load the paddle sprite.");
                SpriteAsset ballSprite = game.BallSprite
                    ?? throw new InvalidOperationException("Setup must load the ball sprite.");
                Require(
                    paddleSprite.Texture.Path == PaddleTexturePath,
                    $"the paddle sprite's texture reference must be '{PaddleTexturePath}'; got "
                        + $"'{paddleSprite.Texture.Path}'.");
                Require(
                    ballSprite.Texture.Path == BallTexturePath,
                    $"the ball sprite's texture reference must be '{BallTexturePath}'; got "
                        + $"'{ballSprite.Texture.Path}'.");

                TextureDescriptor paddleTexture = game.PaddleTexture
                    ?? throw new InvalidOperationException("Setup must load the paddle descriptor.");
                TextureDescriptor ballTexture = game.BallTexture
                    ?? throw new InvalidOperationException("Setup must load the ball descriptor.");
                Require(
                    paddleTexture.Width == ExpectedPaddleWidth
                        && paddleTexture.Height == ExpectedPaddleHeight,
                    $"the paddle texture must be {ExpectedPaddleWidth}x{ExpectedPaddleHeight}; got "
                        + $"{paddleTexture.Width}x{paddleTexture.Height}.");
                Require(
                    ballTexture.Width == ExpectedBallWidth
                        && ballTexture.Height == ExpectedBallHeight,
                    $"the ball texture must be {ExpectedBallWidth}x{ExpectedBallHeight}; got "
                        + $"{ballTexture.Width}x{ballTexture.Height}.");

                int expectedPaddleBytes = ExpectedPaddleWidth * ExpectedPaddleHeight * 4;
                int expectedBallBytes = ExpectedBallWidth * ExpectedBallHeight * 4;
                Require(
                    game.PaddlePixelCount == expectedPaddleBytes,
                    $"the paddle payload must be {expectedPaddleBytes} bytes; got "
                        + $"{game.PaddlePixelCount}.");
                Require(
                    game.BallPixelCount == expectedBallBytes,
                    $"the ball payload must be {expectedBallBytes} bytes; got "
                        + $"{game.BallPixelCount}.");

                ISpriteRender<GameLayer> left = game.LeftPaddle
                    ?? throw new InvalidOperationException("Setup must create the left paddle.");
                ISpriteRender<GameLayer> right = game.RightPaddle
                    ?? throw new InvalidOperationException("Setup must create the right paddle.");
                Require(
                    left.Tint == settings.LeftPaddleTint,
                    "the left paddle tint must match the baked settings.");
                Require(
                    right.Tint == settings.RightPaddleTint,
                    "the right paddle tint must match the baked settings.");
            }

            Require(
                game.World is { } world && world.Parent is null,
                "Teardown must detach the world from the root.");

            Console.WriteLine(
                $"Pong headless run completed: setup/tick/teardown={game.SetupCount}/{game.TickCount}/"
                    + $"{game.TeardownCount}, tickLimit={tickLimit}, entries={game.PackEntryCount}, "
                    + $"paddle={game.PaddleTexture?.Width}x{game.PaddleTexture?.Height} "
                    + $"({game.PaddlePixelCount} bytes, {game.Settings.Paddle.Path}), "
                    + $"ball={game.BallTexture?.Width}x{game.BallTexture?.Height} "
                    + $"({game.BallPixelCount} bytes, {game.Settings.Ball.Path}), world detached, "
                    + "exit 0.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pong headless run failed: {exception.Message}");
            return 1;
        }
    }

    private static int RunOffscreen(PongArguments arguments)
    {
        string packPath = Path.Combine(AppContext.BaseDirectory, PackFileName);

        try
        {
            Require(File.Exists(packPath), $"the baked pack must exist at '{packPath}'.");

            int tickLimit = arguments.TickLimit
                ?? throw new InvalidOperationException("--offscreen requires --ticks N.");

            var game = new PongGame(interactive: false, tickLimit);
            using (var app = new Bork2dAppBuilder<GameLayer>()
                .WithGame(game)
                .WithOffscreen(WindowWidth, WindowHeight)
                .WithAssetsPack(packPath)
                .Build())
            {
                app.Run();

                Require(game.SetupCount == 1, "Setup must run exactly once.");
                Require(
                    game.TickCount == tickLimit,
                    $"--ticks {tickLimit} must run exactly {tickLimit} completed ticks; ran "
                        + $"{game.TickCount}.");
                Require(game.TeardownCount == 1, "Teardown must run exactly once.");
            }

            Require(
                game.World is { } world && world.Parent is null,
                "Teardown must detach the world from the root.");

            Console.WriteLine(
                $"Pong offscreen run completed: setup/tick/teardown={game.SetupCount}/{game.TickCount}/"
                    + $"{game.TeardownCount}, tickLimit={tickLimit}, exit 0.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pong offscreen run failed: {exception.Message}");
            return 1;
        }
    }

    private static int RunWindowed(PongArguments arguments)
    {
        string packPath = Path.Combine(AppContext.BaseDirectory, PackFileName);

        try
        {
            Require(File.Exists(packPath), $"the baked pack must exist at '{packPath}'.");

            var game = new PongGame(interactive: true, arguments.TickLimit);
            using (var app = new Bork2dAppBuilder<GameLayer>()
                .WithGame(game)
                .WithTitle(WindowTitle)
                .WithSize(WindowWidth, WindowHeight)
                .WithResizable(true)
                .WithMaxDeltaTime(MaxDeltaTime)
                .WithAssetsPack(packPath)
                .Build())
            {
                app.Run();
            }

            Require(game.SetupCount == 1, "Setup must run exactly once.");
            Require(game.TeardownCount == 1, "Teardown must run exactly once.");
            if (arguments.TickLimit is { } tickLimit)
            {
                Require(
                    game.TickCount == tickLimit,
                    $"--ticks {tickLimit} must run exactly {tickLimit} completed ticks; ran "
                        + $"{game.TickCount}.");
            }

            Require(
                game.World is { } world && world.Parent is null,
                "Teardown must detach the world from the root.");

            Console.WriteLine(
                $"Pong windowed run completed: setup/tick/teardown={game.SetupCount}/{game.TickCount}/"
                    + $"{game.TeardownCount}, tickLimit={arguments.TickLimit?.ToString() ?? "none"}, "
                    + "window destroyed, exit 0.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Pong windowed run failed: {exception.Message}");
            return 1;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
