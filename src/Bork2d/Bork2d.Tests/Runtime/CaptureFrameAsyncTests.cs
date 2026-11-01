using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Deterministic tests for the public <c>IEngine.CaptureFrameAsync</c> lifecycle (M8 §6.9): the
/// pre-Run source gate, headless <see cref="NotSupportedException"/>, post-run
/// <see cref="ObjectDisposedException"/>, cancellation, thread safety, and the runtime-installed
/// renderer source.
/// </summary>
public sealed class CaptureFrameAsyncTests
{
    [Fact]
    public void BeforeTheHostInstallsASourceTheCallThrowsInvalidOperation()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();

        Assert.Throws<InvalidOperationException>(
            () =>
            {
                _ = engine.CaptureFrameAsync();
            });
    }

    [Fact]
    public async Task TheInstalledSourceIsInvokedAndItsTaskReturned()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        FrameCaptureResult expected = CreateResult([1, 2, 3, 4], width: 1, height: 1);
        engine.SetCaptureSource(_ => Task.FromResult(expected));

        FrameCaptureResult result = await engine.CaptureFrameAsync();

        Assert.Equal(expected.Pixels, result.Pixels);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
        Assert.Equal(TextureFormat.Rgba8, result.Format);
    }

    [Fact]
    public void AHeadlessRunThrowsNotSupported()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        engine.SetCaptureUnsupported();

        Assert.Throws<NotSupportedException>(
            () =>
            {
                _ = engine.CaptureFrameAsync();
            });
    }

    [Fact]
    public void AfterTheRunOrDisposeTheCallThrowsObjectDisposedEvenIfASourceWasInstalled()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        engine.SetCaptureSource(_ => Task.FromResult(CreateResult(new byte[4], 1, 1)));

        engine.MarkCaptureDisposed();
        Assert.Throws<ObjectDisposedException>(
            () =>
            {
                _ = engine.CaptureFrameAsync();
            });

        engine.SetCaptureSource(_ => Task.FromResult(CreateResult(new byte[4], 1, 1)));
        Assert.Throws<ObjectDisposedException>(
            () =>
            {
                _ = engine.CaptureFrameAsync();
            });
    }

    [Fact]
    public async Task ACancelledPendingRequestFaultsWithOperationCancelled()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        var completion = new TaskCompletionSource<FrameCaptureResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SetCaptureSource(cancellationToken =>
        {
            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        });
        using var source = new CancellationTokenSource();

        Task<FrameCaptureResult> task = engine.CaptureFrameAsync(source.Token);
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public void ConcurrentCallsUseTheInstalledSource()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        var calls = 0;
        engine.SetCaptureSource(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(CreateResult(new byte[4], 1, 1));
        });

        var started = new ManualResetEventSlim();
        var threads = new Thread[4];
        for (var index = 0; index < threads.Length; index++)
        {
            threads[index] = new Thread(() =>
            {
                started.Wait();
                for (var call = 0; call < 100; call++)
                {
                    _ = engine.CaptureFrameAsync().GetAwaiter().GetResult();
                }
            })
            {
                IsBackground = true,
            };
            threads[index].Start();
        }

        started.Set();
        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A capture caller did not join.");
        }

        Assert.Equal(400, calls);
    }

    [Fact]
    public void AWindowedRunForwardsCaptureToTheRendererAndDisposesAfterRun()
    {
        var sdl = new FakeSdlApi();
        FrameCaptureResult expected = CreateResult([9, 8, 7, 6], width: 1, height: 1);
        var renderer = new FakeRenderer
        {
            AutoReady = true,
            CaptureHandler = _ => Task.FromResult(expected),
        };
        var factory = new FakeRendererFactory(renderer);
        var instanceFactory = new FakeVulkanInstanceFactory();
        var game = new ScriptedGame<GameLayer>();
        IEngine<GameLayer>? engine = null;
        FrameCaptureResult? captured = null;
        game.OnSetup = capturedEngine =>
        {
            engine = capturedEngine;
            captured = capturedEngine.CaptureFrameAsync().GetAwaiter().GetResult();
        };
        game.OnTick = () => game.Engine!.Exit();

        using var app = WindowedTestSupport.Builder(sdl, game, factory, instanceFactory).Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(expected.Pixels, captured!.Value.Pixels);
        Assert.Equal(expected.Width, captured.Value.Width);
        Assert.Equal(expected.Height, captured.Value.Height);
        Assert.Equal(expected.Format, captured.Value.Format);
        Assert.Equal(1, renderer.CaptureRequestCount);
        Assert.NotNull(engine);
        Assert.Throws<ObjectDisposedException>(
            () =>
            {
                _ = engine!.CaptureFrameAsync();
            });
    }

    [Fact]
    public void AnOffscreenRunForwardsCaptureToTheRenderer()
    {
        FrameCaptureResult expected = CreateResult([4, 3, 2, 1], width: 1, height: 1);
        var renderer = new FakeRenderer
        {
            AutoReady = true,
            CaptureHandler = _ => Task.FromResult(expected),
        };
        var factory = new FakeRendererFactory(renderer);
        var instanceFactory = new FakeVulkanInstanceFactory();
        var game = new ScriptedGame<GameLayer>();
        FrameCaptureResult? captured = null;
        game.OnTick = () =>
        {
            captured = game.Engine!.CaptureFrameAsync().GetAwaiter().GetResult();
            game.Engine.Exit();
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithFakeGraphics(factory, instanceFactory)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(expected.Pixels, captured!.Value.Pixels);
        Assert.Equal(expected.Width, captured.Value.Width);
        Assert.Equal(expected.Height, captured.Value.Height);
        Assert.Equal(expected.Format, captured.Value.Format);
        Assert.Equal(1, renderer.CaptureRequestCount);
    }

    [Fact]
    public void AHeadlessRunThrowsNotSupportedDuringSetupAndDisposedAfterRun()
    {
        var game = new ScriptedGame<GameLayer> { ExitAfterFirstTick = true };
        IEngine<GameLayer>? engine = null;
        Exception? observed = null;
        game.OnSetup = capturedEngine =>
        {
            engine = capturedEngine;
            try
            {
                _ = capturedEngine.CaptureFrameAsync();
            }
            catch (Exception exception)
            {
                observed = exception;
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(new FakeSchedulerClock())
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.IsType<NotSupportedException>(observed);
        Assert.NotNull(engine);
        Assert.Throws<ObjectDisposedException>(
            () =>
            {
                _ = engine!.CaptureFrameAsync();
            });
    }

    private static FrameCaptureResult CreateResult(byte[] pixels, int width, int height)
        => new(pixels, width, height, TextureFormat.Rgba8);
}
