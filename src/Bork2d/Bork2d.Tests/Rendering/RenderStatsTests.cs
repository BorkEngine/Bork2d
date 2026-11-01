using Bork2d.Engine;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;

namespace Bork2d.Tests.Rendering;

/// <summary>
/// Deterministic tests for the public <see cref="RenderStats"/> snapshot (M8 §6.9): defaults,
/// headless, the host-installed source, thread-safe consistent reads, never throwing, and no
/// allocation on the read path.
/// </summary>
public sealed class RenderStatsTests
{
    [Fact]
    public void TheGetterDefaultsBeforeTheHostInstallsASource()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();

        Assert.Equal(default(RenderStats), engine.RenderStats);
    }

    [Fact]
    public void HeadlessRunsKeepTheDefaultSnapshot()
    {
        var clock = new FakeSchedulerClock();
        var observed = new RenderStats[3];
        var reads = 0;
        var game = new ScriptedGame<GameLayer> { ExitAfterFirstTick = true };
        game.OnSetup = engine => observed[reads++] = engine.RenderStats;
        game.OnTick = () => observed[reads++] = game.Engine!.RenderStats;
        game.OnTeardown = () => observed[reads++] = game.Engine!.RenderStats;

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithHeadless()
            .WithoutAssets()
            .WithSchedulerClock(clock)
            .Build();
        AppTestSupport.RunToCompletion(app);

        Assert.Equal(3, reads);
        Assert.All(observed, stats => Assert.Equal(default(RenderStats), stats));
    }

    [Fact]
    public void TheInstalledSourceSuppliesTheSnapshot()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        RenderStats expected = SampleStats(frameSequence: 7);

        engine.SetRenderStatsSource(() => expected);

        Assert.Equal(expected, engine.RenderStats);
        Assert.Equal(7, engine.RenderStats.FrameSequence);
    }

    [Fact]
    public void ClearingTheSourceRetainsTheLastSnapshot()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        RenderStats expected = SampleStats(frameSequence: 3);
        engine.SetRenderStatsSource(() => expected);
        _ = engine.RenderStats;

        engine.SetRenderStatsSource(null);

        Assert.Equal(expected, engine.RenderStats);
    }

    [Fact]
    public void AThrowingSourceNeverEscapesAndKeepsTheLastSnapshot()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        RenderStats expected = SampleStats(frameSequence: 11);

        engine.SetRenderStatsSource(() => throw new InvalidOperationException("renderer stopped"));
        Assert.Equal(default(RenderStats), engine.RenderStats);

        engine.SetRenderStatsSource(() => expected);
        Assert.Equal(expected, engine.RenderStats);

        engine.SetRenderStatsSource(() => throw new ObjectDisposedException("renderer"));
        Assert.Equal(expected, engine.RenderStats);
    }

    [Fact]
    public void ConcurrentReadsObserveWholeSnapshots()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        RenderStats first = SampleStats(frameSequence: 1);
        RenderStats second = SampleStats(frameSequence: 2);
        var state = 0;
        engine.SetRenderStatsSource(
            () => (Volatile.Read(ref state) & 1) == 0 ? first : second);

        var started = new ManualResetEventSlim();
        var failures = 0;
        var threads = new Thread[4];
        for (var index = 0; index < threads.Length; index++)
        {
            threads[index] = new Thread(() =>
            {
                started.Wait();
                for (var read = 0; read < 20_000; read++)
                {
                    RenderStats stats = engine.RenderStats;
                    if (stats != first && stats != second)
                    {
                        Interlocked.Increment(ref failures);
                    }
                }
            })
            {
                IsBackground = true,
            };
            threads[index].Start();
        }

        started.Set();
        for (var index = 0; index < 10_000; index++)
        {
            Volatile.Write(ref state, index);
        }

        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "A stats reader did not join.");
        }

        Assert.Equal(0, failures);
    }

    [Fact]
    public void ReadingDoesNotAllocateAfterWarmUp()
    {
        GameEngine<GameLayer> engine = TestEngineFactory.Create();
        RenderStats expected = SampleStats(frameSequence: 5);
        engine.SetRenderStatsSource(() => expected);
        _ = engine.RenderStats;

        long before = GC.GetAllocatedBytesForCurrentThread();
        var total = 0L;
        for (var read = 0; read < 10_000; read++)
        {
            total += engine.RenderStats.FrameSequence;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10_000 * 5, total);
        Assert.Equal(0, allocated);
    }

    private static RenderStats SampleStats(long frameSequence) => new(
        FrameSequence: frameSequence,
        FramebufferWidth: 1280,
        FramebufferHeight: 720,
        DrawCalls: 2,
        Instances: 7,
        Cameras: 3,
        NonEmptyCameras: 2,
        TexturePages: 4,
        TexturePageCapacity: 256,
        TextureUploadsPending: 2,
        TextureUploadBytesPending: 8192,
        SkippedSprites: 1,
        ValidationErrors: 0,
        ValidationWarnings: 0);
}
