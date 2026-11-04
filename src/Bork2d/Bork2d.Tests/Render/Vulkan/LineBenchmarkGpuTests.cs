using System.Diagnostics;
using System.Numerics;
using Bork2d.Engine;
using Bork2d.Engine.Mathematics;
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
/// The opt-in Release line-throughput measurement: 2,000 small lines, one camera, 1920x1080
/// offscreen, a short measurement window after warm-up, and no artificial cap or sleep.
/// </summary>
/// <remarks>
/// <para>
/// The benchmark drives the real offscreen renderer through the snapshot handoff exactly like the
/// host: acquire a slot, fill one camera plus 2,000 prebuilt line records, and publish. Every line
/// expands to four vertices and six indices, so a frame writes 8,000 vertices and 12,000 indices
/// through the planner's line buffers and records one indexed draw. The wall-clock interval between
/// observed completed frames includes the producer's snapshot fill, planning, line vertex writing,
/// recording, submit, and the two-frame fence backpressure, so it is the end-to-end frame budget the
/// game loop sees. There is no per-frame logging or string formatting inside the measurement loop.
/// </para>
/// <para>
/// This is a measurement, not a pass/fail performance gate: it reports the median/p95/mean FPS,
/// frame milliseconds, and the benchmark thread's allocation delta so the number is reviewable
/// alongside the approved 50,000-sprite 165 FPS gate. It is skipped unless
/// <c>BORK2D_RUN_GPU_TESTS=1</c> and is Release-only; CI is unchanged.
/// </para>
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class LineBenchmarkGpuTests
{
    private const int LineCount = 2_000;
    private const int FramebufferWidth = 1920;
    private const int FramebufferHeight = 1080;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan _warmupWindow = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _measurementWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _warmupLimit = TimeSpan.FromSeconds(15);

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="LineBenchmarkGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output used to record the benchmark evidence.</param>
    public LineBenchmarkGpuTests(ITestOutputHelper output) => _output = output;

#if DEBUG
    [GpuFact(
        Skip = "The line-throughput benchmark is a Release-mode performance measurement; "
            + "unoptimized Debug code generation is not a performance measurement. Run it with "
            + "-c Release.")]
#else
    [GpuFact]
#endif
    public void OffscreenLineThroughputIsMeasuredOnTheReleaseBuild()
    {
        using var assets = new TestRendererAssets();
        var scene = new LineBenchmarkScene();
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
            Assert.Equal(2, stats.DrawCalls);
            Assert.Equal(LineCount, stats.Lines);
            Assert.Equal(0, stats.Instances);
            Assert.Equal(0, stats.SkippedLines);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Equal(0, result.CurrentThreadAllocatedBytes);
            Assert.Null(renderer.FatalException);

            string summary = Describe(result);
            _output.WriteLine(summary);
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
        TestRendererAssets assets,
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
            throw new TimeoutException("The line benchmark offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException("The line benchmark renderer faulted during startup.", fatal);
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
        // Exercise the handoff's backpressure path before the allocation baseline. The
        // zero-allocation assertion itself is unchanged.
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < _warmupWindow
            || renderer.RenderStats.Lines != LineCount
            || renderer.SnapshotCounters.BackpressureWaits == 0)
        {
            if (renderer.FatalException is { } fatal)
            {
                throw new InvalidOperationException(
                    "The line benchmark renderer faulted during warm-up.",
                    fatal);
            }

            if (stopwatch.Elapsed >= _warmupLimit)
            {
                throw new TimeoutException(
                    "The line benchmark scene did not become drawable during warm-up: "
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
        double measuredSeconds = (lastTimestamp - startTimestamp) / frequency;
        long measuredFrames = lastFrames - baselineFrames;
        double[] ordered = new double[sampleCount];
        Array.Copy(samples, ordered, sampleCount);
        Array.Sort(ordered);

        return new BenchmarkResult(
            measuredFrames,
            measuredSeconds,
            Percentile(ordered, 0.50),
            Percentile(ordered, 0.95),
            ordered.Length == 0 ? 0 : ordered[^1],
            ordered.Length == 0 ? 0 : Sum(ordered) / sampleCount,
            allocatedAfter - allocatedBefore);
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

    private static string Describe(BenchmarkResult result)
        => "Line benchmark: "
            + $"lines={LineCount}, vertices={LineCount * 4}, indices={LineCount * 6}, "
            + $"framebuffer={FramebufferWidth}x{FramebufferHeight}, "
            + $"frames={result.Frames}, window={result.Seconds:0.000}s, "
            + $"fps median/p95/mean={result.MedianFps:0.0}/{result.P95Fps:0.0}/{result.MeanFps:0.0}, "
            + $"frame ms p50/p95/max/mean={result.MedianFrameMilliseconds:0.000}/"
            + $"{result.P95FrameMilliseconds:0.000}/{result.MaxFrameMilliseconds:0.000}/"
            + $"{result.MeanFrameMilliseconds:0.000}, "
            + $"benchmarkThreadAllocatedBytes={result.CurrentThreadAllocatedBytes}, "
            + "vsync=off (offscreen discard), validation=enabled, drawCalls=2 (one line run + "
            + "present).";

    private static void StopAndAssertClean(VulkanOffscreenRenderer renderer)
    {
        renderer.RequestStop();
        Assert.True(renderer.Join(_joinTimeout), "The line benchmark renderer did not join.");
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
            + $"lines={stats.Lines}, skipped={stats.SkippedLines}, validation="
            + $"{stats.ValidationErrors}/{stats.ValidationWarnings}";

    private static bool IsUnavailable(Exception exception)
    {
        string message = exception.Message;
        return message.Contains("could not be loaded", StringComparison.Ordinal)
            || message.Contains("No Vulkan physical devices", StringComparison.Ordinal)
            || message.Contains("No suitable Vulkan 1.3 device", StringComparison.Ordinal)
            || message.Contains("below the required 1.3.0", StringComparison.Ordinal)
            || message.Contains("requires Vulkan 1.3.0", StringComparison.Ordinal)
            || message.Contains("vkCreateInstance failed", StringComparison.Ordinal)
            || message.Contains("textureCompressionBC", StringComparison.Ordinal)
            || message.Contains(
                "maxPerStageDescriptorUpdateAfterBindSampledImages=0",
                StringComparison.Ordinal)
            || message.Contains("No UNORM offscreen color format", StringComparison.Ordinal);
    }

    private sealed class LineBenchmarkScene
    {
        private static readonly Color _color = new(1f, 0f, 0f, 1f);
        private readonly LineGeometryData[] _geometry;
        private readonly Matrix3x2[] _transforms;
        private readonly Bounds[] _bounds;

        internal LineBenchmarkScene()
        {
            _geometry = new LineGeometryData[LineCount];
            _transforms = new Matrix3x2[LineCount];
            _bounds = new Bounds[LineCount];
            for (var index = 0; index < LineCount; index++)
            {
                LineGeometryData geometry = LineGeometryBuilder.Build(
                    [new Vector2(-0.04f, 0f), new Vector2(0.04f, 0f)],
                    closed: false,
                    width: 0.01f)
                    ?? throw new InvalidOperationException("The benchmark line geometry is null.");
                float x = ((index % 50) * 0.7f) - 17.5f;
                float y = ((index / 50) * 0.25f) - 5f;
                Matrix3x2 transform = Matrix3x2.CreateTranslation(x, y);
                _geometry[index] = geometry;
                _transforms[index] = transform;
                _bounds[index] = LineBounds.CreateWorldBounds(geometry, transform);
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
            for (var index = 0; index < LineCount; index++)
            {
                snapshot.AddLine(
                    new LineSnapshot(
                        _geometry[index],
                        _transforms[index],
                        _color,
                        Layer: 1u,
                        ZIndex: 0,
                        AntiAlias: false)
                    {
                        Bounds = _bounds[index],
                    });
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
                    "The renderer stopped consuming; the line benchmark producer cannot publish.");
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
        long CurrentThreadAllocatedBytes)
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
