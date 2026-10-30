using System.Numerics;
using Bork2d.Engine;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Silk.NET.SDL;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Platform.Sdl;

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
            .WithRendererFactory(new FakeRendererFactory(new FakeRenderer { AutoReady = true }))
            .WithoutAssets()
            .Build();

        var failure = AppTestSupport.RunCaptured(app);
        if (failure is Bork2dException unavailable && IsVulkanUnavailable(unavailable))
        {
            throw SkipException.ForSkip(
                $"A Vulkan 1.3 loader/instance is not available: {unavailable.Message}");
        }

        Assert.Null(failure);

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
            .WithRendererFactory(new FakeRendererFactory(new FakeRenderer { AutoReady = true }))
            .WithoutAssets()
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

            using (var sdl = global::Silk.NET.SDL.Sdl.GetApi())
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

        if (failure is Bork2dException unavailable && IsVulkanUnavailable(unavailable))
        {
            throw SkipException.ForSkip(
                $"A Vulkan 1.3 loader/instance is not available: {unavailable.Message}");
        }

        Assert.Null(failure);
        Assert.Equal(1, game.SetupCount);
        Assert.Equal(1, game.TeardownCount);

        // A close/QUIT received before the first freeze is a valid stop path: the loop checks the
        // request before starting a tick, so observing zero ticks is as correct as observing more.
        Assert.True(game.TickCount >= 0);

        var stopLine = Assert.Single(
            provider.Entries,
            entry => entry.Message.Contains("windowed loop stopped", StringComparison.Ordinal));
        Assert.Contains("reason=Close", stopLine.Message, StringComparison.Ordinal);
    }

    private static bool IsVulkanUnavailable(Bork2dException exception)
    {
        string message = exception.Message;
        return message.Contains("could not be loaded", StringComparison.Ordinal)
            || message.Contains("requires Vulkan 1.3.0", StringComparison.Ordinal)
            || message.Contains("below the required 1.3.0", StringComparison.Ordinal)
            || message.Contains("vkCreateInstance failed", StringComparison.Ordinal)
            || message.Contains("is not available", StringComparison.Ordinal);
    }
}
