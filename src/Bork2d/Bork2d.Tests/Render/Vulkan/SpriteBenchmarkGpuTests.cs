using System.Diagnostics;
using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// The opt-in M8 165 FPS benchmark (M8 §6.11/§8.4): 50,000 small sprites, one camera, 1920×1080
/// offscreen, a 10-second measurement window after warm-up, and no artificial cap or sleep.
/// </summary>
/// <remarks>
/// <para>
/// The benchmark drives the real offscreen renderer through the M7 snapshot handoff exactly like the
/// host: acquire a slot, fill one camera plus 50,000 sprites with a preallocated fill predicate, and
/// publish. Sprites are deliberately small in world units so the measurement reflects the engine's
/// snapshot/planning/recording/submit path rather than the GPU's fill-rate limit; a full-screen-sized
/// sprite workload would measure overdraw, not engine throughput. It records the wall-clock interval
/// between observed completed frames; that interval includes the producer's snapshot fill, the
/// planner/build/record/submit work, and the two-frame fence backpressure, so it is the end-to-end
/// frame budget the game loop sees. There is no per-frame logging or string formatting inside the
/// measurement loop.
/// </para>
/// <para>
/// Pass criterion (owner-approved, not adjustable here): median ≥165 FPS (p50 ≤ ~6.06 ms) on the M6
/// machine. The test also asserts one draw, 50,000 instances, one non-empty camera, and validation
/// 0/0. A warmed 50,000-sprite <c>SpriteFramePlanner.Plan</c> allocation test lives in the
/// deterministic suite; this test additionally asserts the measured producer/plan/build path
/// allocates zero bytes on the benchmark thread and reports the process-wide allocation delta
/// separately so any runtime/harness residual is visible. The test is skipped unless
/// <c>BORK2D_RUN_GPU_TESTS=1</c>; CI is unchanged.
/// </para>
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class SpriteBenchmarkGpuTests
{
    private const int SpriteCount = 50_000;
    private const int FramebufferWidth = 1920;
    private const int FramebufferHeight = 1080;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _warmupWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan _measurementWindow = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _warmupLimit = TimeSpan.FromSeconds(15);

    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="SpriteBenchmarkGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output used to record the benchmark evidence.</param>
    public SpriteBenchmarkGpuTests(ITestOutputHelper output) => _output = output;

#if DEBUG
    [GpuFact(
        Skip = "The 165 FPS benchmark is a Release-mode performance gate; unoptimized Debug code "
            + "generation is not a performance measurement. Run it with -c Release.")]
#else
    [GpuFact]
#endif
    public void OffscreenFiftyThousandSpritesSustainThe165FpsMedianTarget()
    {
        var assets = new GeneratedTextureAssets();
        string texture = assets.AddTexture("bench/tex");
        SpriteAsset sprite = new()
        {
            Texture = texture,
            Uvs = _fullUvs,
            Scale = new Vector2(0.08f),
            Pivot = new Vector2(0.5f, 0.5f),
            Topology = SpriteTopology.Quad,
        };
        var scene = new BenchmarkScene(sprite);
        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer, scene.Fill);

            WarmUp(renderer, producer);
            BenchmarkResult result = Measure(renderer, producer);

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(1, stats.Cameras);
            Assert.Equal(1, stats.NonEmptyCameras);
            Assert.Equal(1, stats.DrawCalls);
            Assert.Equal(SpriteCount, stats.Instances);
            Assert.Equal(2, stats.TexturePages);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.True(renderer.InstanceCapacityHighWater >= SpriteCount);
            Assert.Equal(0, result.CurrentThreadAllocatedBytes);
            Assert.Null(renderer.FatalException);

            string summary = Describe(result, renderer.InstanceCapacityHighWater);
            _output.WriteLine(summary);

            Assert.True(
                result.MedianFps >= 165.0,
                "The approved M8 pass criterion is median >=165 FPS (p50 <= ~6.06 ms); measured "
                    + $"{result.MedianFps:0.0} FPS (p50 {result.MedianFrameMilliseconds:0.000} ms). "
                    + summary);

            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(new CapturingLoggerProvider());
        });

    private static VulkanOffscreenRenderer StartOffscreen(
        GeneratedTextureAssets assets,
        ILoggerFactory loggerFactory)
    {
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = FramebufferWidth,
            Height = FramebufferHeight,
            ValidationRequested = true,
            LoggerFactory = loggerFactory,
            Assets = assets,
        };

        var renderer = Assert.IsType<VulkanOffscreenRenderer>(
            new VulkanRendererFactory().Create(startInfo));
        renderer.Start();
        if (!renderer.WaitForReady(_timeout))
        {
            throw new TimeoutException("The benchmark offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException("The benchmark renderer faulted during startup.", fatal);
        }

        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; benchmark validation evidence "
                    + "cannot be gathered on this machine.");
        }

        return renderer;
    }

    private static void WarmUp(VulkanOffscreenRenderer renderer, Producer producer)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _warmupWindow
            || renderer.RenderStats.TexturePages < 2
            || renderer.RenderStats.Instances != SpriteCount)
        {
            if (renderer.FatalException is { } fatal)
            {
                throw new InvalidOperationException("The benchmark renderer faulted during warm-up.", fatal);
            }

            if (stopwatch.Elapsed >= _warmupLimit)
            {
                throw new TimeoutException(
                    "The benchmark scene did not become drawable during warm-up: "
                        + DescribeWarmup(renderer.RenderStats));
            }

            producer.Publish();
        }
    }

    private static BenchmarkResult Measure(VulkanOffscreenRenderer renderer, Producer producer)
    {
        var samples = new double[200_000];
        var sampleCount = 0;
        long baselineFrames = renderer.Counters.FramesDiscarded;
        long lastFrames = baselineFrames;
        long startTimestamp = Stopwatch.GetTimestamp();
        long lastTimestamp = startTimestamp;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long processAllocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var frequency = (double)Stopwatch.Frequency;

        while (true)
        {
            producer.Publish();
            long now = Stopwatch.GetTimestamp();
            long frames = renderer.Counters.FramesDiscarded;
            if (frames > lastFrames)
            {
                double interval = (now - lastTimestamp) / frequency;
                var completed = (int)Math.Min(frames - lastFrames, int.MaxValue);
                double perFrame = interval / completed;
                for (var index = 0; index < completed && sampleCount < samples.Length; index++)
                {
                    samples[sampleCount++] = perFrame;
                }

                lastTimestamp = now;
                lastFrames = frames;
            }

            if ((now - startTimestamp) / frequency >= _measurementWindow.TotalSeconds)
            {
                break;
            }
        }

        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        long processAllocatedAfter = GC.GetTotalAllocatedBytes(precise: true);
        double measuredSeconds = (lastTimestamp - startTimestamp) / frequency;
        long measuredFrames = lastFrames - baselineFrames;
        double[] ordered = new double[sampleCount];
        Array.Copy(samples, ordered, sampleCount);
        Array.Sort(ordered);

        double medianSeconds = Percentile(ordered, 0.50);
        double p95Seconds = Percentile(ordered, 0.95);
        double maxSeconds = sampleCount == 0 ? 0 : ordered[^1];
        double meanSeconds = sampleCount == 0 ? 0 : Sum(ordered) / sampleCount;

        return new BenchmarkResult(
            measuredFrames,
            measuredSeconds,
            medianSeconds,
            p95Seconds,
            maxSeconds,
            meanSeconds,
            allocatedAfter - allocatedBefore,
            processAllocatedAfter - processAllocatedBefore);
    }

    private static double Percentile(double[] ordered, double percentile)
    {
        if (ordered.Length == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
        return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
    }

    private static double Sum(double[] values)
    {
        var total = 0.0;
        foreach (double value in values)
        {
            total += value;
        }

        return total;
    }

    private static string Describe(BenchmarkResult result, int instanceCapacity)
    {
        double medianFps = result.MedianFps;
        double p95Fps = result.P95Fps;
        double meanFps = result.MeanFps;
        return "M8 50k benchmark: "
            + $"sprites={SpriteCount}, framebuffer={FramebufferWidth}x{FramebufferHeight}, "
            + $"frames={result.Frames}, window={result.Seconds:0.000}s, "
            + $"fps median/p95/mean={medianFps:0.0}/{p95Fps:0.0}/{meanFps:0.0}, "
            + $"frame ms p50/p95/max/mean={result.MedianFrameMilliseconds:0.000}/"
            + $"{result.P95FrameMilliseconds:0.000}/{result.MaxFrameMilliseconds:0.000}/"
            + $"{result.MeanFrameMilliseconds:0.000}, "
            + $"instanceCapacity={instanceCapacity}, "
            + $"benchmarkThreadAllocatedBytes={result.CurrentThreadAllocatedBytes}, "
            + $"processAllocatedBytes={result.ProcessAllocatedBytes}, "
            + "cpuAttribution=unavailable (wall-clock publish->completed-frame includes snapshot "
            + "fill, planning, recording, submit, and fence wait; there is no per-frame render-thread "
            + "timing counter), vsync=off (offscreen discard), validation=enabled, "
            + "allocations=benchmark-thread measured; process-wide delta reported separately.";
    }

    private static void StopAndAssertClean(VulkanOffscreenRenderer renderer)
    {
        renderer.RequestStop();
        Assert.True(renderer.Join(_joinTimeout), "The benchmark renderer did not join.");
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);
        foreach (var slot in renderer.SnapshotRing.Slots)
        {
            Assert.Equal(Bork2d.Render.Handoff.SnapshotSlotState.Free, slot.State);
        }
    }

    private static void Finish(VulkanOffscreenRenderer? renderer)
    {
        if (renderer is not null)
        {
            renderer.RequestStop();
            _ = renderer.Join(_joinTimeout);
        }
    }

    private static string DescribeWarmup(RenderStats stats)
        => $"frame={stats.FrameSequence}, cameras={stats.Cameras}, "
            + $"instances={stats.Instances}, pages={stats.TexturePages}, "
            + $"skipped={stats.SkippedSprites}, validation={stats.ValidationErrors}/"
            + $"{stats.ValidationWarnings}";

    private static bool IsUnavailable(Exception exception)
    {
        string message = exception.Message;
        return message.Contains("could not be loaded", StringComparison.Ordinal)
            || message.Contains("No Vulkan physical devices", StringComparison.Ordinal)
            || message.Contains("No suitable Vulkan 1.3 device", StringComparison.Ordinal)
            || message.Contains("below the required 1.3.0", StringComparison.Ordinal)
            || message.Contains("requires Vulkan 1.3.0", StringComparison.Ordinal)
            || message.Contains("vkCreateInstance failed", StringComparison.Ordinal)
            || message.Contains(
                "maxPerStageDescriptorUpdateAfterBindSampledImages=0",
                StringComparison.Ordinal)
            || message.Contains("No sRGB offscreen color format", StringComparison.Ordinal);
    }

    private sealed class BenchmarkScene
    {
        private readonly SpriteRenderData _sprite;
        private readonly Matrix3x2[] _transforms;

        internal BenchmarkScene(SpriteAsset sprite)
        {
            _sprite = SpriteRenderData.Freeze(sprite);
            _transforms = new Matrix3x2[SpriteCount];
            for (var index = 0; index < SpriteCount; index++)
            {
                float x = ((index % 500) * 0.07f) - 17.5f;
                float y = ((index / 500) * 0.1f) - 5f;
                _transforms[index] = Matrix3x2.CreateTranslation(x, y);
            }

            Fill = FillCore;
        }

        internal Action<FrameSnapshot> Fill { get; }

        private void FillCore(FrameSnapshot snapshot)
        {
            snapshot.AddCamera(
                new CameraSnapshot(
                    ZIndex: 0,
                    WorldMatrix: Matrix3x2.Identity,
                    Size: 10f,
                    Viewport: new Vector4(0f, 0f, 1f, 1f),
                    CullingMask: 1u,
                    Clear: ClearKind.Color,
                    ClearColor: new Color(0.02f, 0.02f, 0.03f, 1f)));
            for (var index = 0; index < SpriteCount; index++)
            {
                snapshot.AddSprite(
                    new SpriteSnapshot(
                        _sprite,
                        _transforms[index],
                        Color.White,
                        Layer: 1u,
                        ZIndex: index,
                        FlipX: false,
                        FlipY: false));
            }
        }
    }

    private sealed class Producer
    {
        private readonly VulkanOffscreenRenderer _renderer;
        private readonly Action<FrameSnapshot> _fill;
        private long _published;

        internal Producer(VulkanOffscreenRenderer renderer, Action<FrameSnapshot> fill)
        {
            _renderer = renderer;
            _fill = fill;
        }

        internal void Publish()
        {
            var slot = _renderer.AcquireSnapshotSlot()
                ?? throw new InvalidOperationException(
                    "The renderer stopped consuming; the benchmark producer cannot publish.");
            slot.Snapshot.Reset();
            slot.Snapshot.Sequence = ++_published;
            slot.Snapshot.Time = _published * 0.016;
            slot.Snapshot.DeltaTime = 0.016;
            _fill(slot.Snapshot);
            _renderer.PublishSnapshot(slot);
        }
    }

    private readonly record struct BenchmarkResult(
        long Frames,
        double Seconds,
        double MedianSeconds,
        double P95Seconds,
        double MaxSeconds,
        double MeanSeconds,
        long CurrentThreadAllocatedBytes,
        long ProcessAllocatedBytes)
    {
        internal double MedianFps => MedianSeconds <= 0 ? 0 : 1 / MedianSeconds;

        internal double P95Fps => P95Seconds <= 0 ? 0 : 1 / P95Seconds;

        internal double MeanFps => MeanSeconds <= 0 ? 0 : 1 / MeanSeconds;

        internal double MedianFrameMilliseconds => MedianSeconds * 1000;

        internal double P95FrameMilliseconds => P95Seconds * 1000;

        internal double MaxFrameMilliseconds => MaxSeconds * 1000;

        internal double MeanFrameMilliseconds => MeanSeconds * 1000;
    }
}
