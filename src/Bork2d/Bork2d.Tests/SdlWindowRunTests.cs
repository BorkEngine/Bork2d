using System.Numerics;
using Bork2d.Runtime;
using Microsoft.Extensions.Logging;
using Silk.NET.SDL;
using Xunit;
using Xunit.Abstractions;

namespace Bork2d.Tests;

/// <summary>
/// Opt-in end-to-end tests for a real windowed <see cref="Bork2dApp.Run"/>: the game-driven exit and
/// the platform close/<c>SDL_QUIT</c> request. They need a desktop session and the real SDL2 native
/// library, so they are skipped unless <c>BORK2D_RUN_WINDOW_TESTS=1</c>.
/// </summary>
/// <remarks>
/// <para>
/// The tests are deliberately small: each one runs the real loop on a bounded background thread and
/// stops it with a signal, never with a sleep or a poll loop. The first test lets the game call
/// <see cref="IEngine{TLayer}.Exit"/> in its first tick; the second pushes a real <c>SDL_QUIT</c>
/// event from the test thread through the Silk.NET binding after <c>Setup</c> signals, which proves
/// the native event path stops the loop gracefully.
/// </para>
/// <para>
/// CI never sets the environment variable. The class shares the <c>RealSdlWindow</c> collection with
/// the platform tests because SDL video is process-global and must not be initialized concurrently.
/// </para>
/// </remarks>
[Collection("RealSdlWindow")]
public sealed class SdlWindowRunTests
{
    private static readonly TimeSpan _runTimeout = TimeSpan.FromSeconds(20);

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="SdlWindowRunTests"/> class.</summary>
    /// <param name="output">The xUnit test output sink.</param>
    public SdlWindowRunTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [WindowFact]
    public void RealWindowedRunExitsCleanlyWhenTheGameCallsExitInItsFirstTick()
    {
        var provider = new CapturingLoggerProvider();
        var game = new ScriptedGame<GameLayer>();
        var framebuffer = Vector2.Zero;
        game.OnSetup = engine =>
        {
            framebuffer = engine.Window.Size;
            Assert.True(
                framebuffer.X > 0f && framebuffer.Y > 0f,
                "the real window must report a framebuffer size before the first tick.");
        };
        game.OnTick = () => game.Engine?.Exit();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Bork2d WP5 real run exit")
            .WithSize(640, 360)
            .WithResizable(false)
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();

        AppTestSupport.RunToCompletion(app);

        _output.WriteLine($"framebuffer: {framebuffer}");

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TickCount);
        Assert.Equal(1, game.TeardownCount);

        var stopLine = Assert.Single(
            provider.Entries,
            entry => entry.Message.Contains("windowed loop stopped", StringComparison.Ordinal));
        Assert.Contains("reason=Exit", stopLine.Message, StringComparison.Ordinal);
        Assert.Contains("ticks=1", stopLine.Message, StringComparison.Ordinal);

        // Run returned only after the platform shut down and detached its proxies: a retained
        // reference is a disposed object, not a live window and not a hang.
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = game.Engine!.Window.Title;
        });
    }

    [WindowFact]
    public void RealWindowedRunExitsGracefullyWhenAnSdlQuitEventIsPushed()
    {
        var provider = new CapturingLoggerProvider();
        var game = new ScriptedGame<GameLayer>();
        var setup = new ManualResetEventSlim();
        game.OnSetup = _ => setup.Set();

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Bork2d WP5 real run quit")
            .WithSize(640, 360)
            .WithResizable(false)
            .WithLogging(logging => logging.AddProvider(provider))
            .Build();

        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                app.Run();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        })
        {
            IsBackground = true,
            Name = "Bork2d.Tests.RealWindowRun",
        };

        thread.Start();
        try
        {
            Assert.True(
                setup.Wait(_runTimeout),
                "the real windowed Run did not reach Setup within the bounded wait.");

            using (var sdl = Sdl.GetApi())
            {
                var quit = new Event { Type = (uint)EventType.Quit };
                Assert.Equal(1, sdl.PushEvent(ref quit));
            }

            Assert.True(
                thread.Join(_runTimeout),
                "the real windowed Run did not stop within the bounded wait after SDL_QUIT.");
        }
        finally
        {
            if (thread.IsAlive)
            {
                game.Engine?.Exit();
                thread.Join(_runTimeout);
            }
        }

        Assert.Null(failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(game.TickCount >= 1, "the loop must complete at least one tick before the close.");

        var stopLine = Assert.Single(
            provider.Entries,
            entry => entry.Message.Contains("windowed loop stopped", StringComparison.Ordinal));
        Assert.Contains("reason=Close", stopLine.Message, StringComparison.Ordinal);
    }
}
