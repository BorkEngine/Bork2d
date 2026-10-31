using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Opt-in real-GPU proof for the offscreen app host's M7 handoff (Q19=S3): a real
/// <see cref="Bork2dApp"/> with <c>WithOffscreen(320, 240)</c> + <c>WithValidation(true)</c> runs the
/// renderer through the internal factory hook, the host publishes one snapshot per completed tick
/// and the renderer consumes/discards one frame per snapshot, the run stops and joins cleanly with
/// every slot <c>Free</c>, and validation reports zero errors/warnings.
/// </summary>
/// <remarks>
/// Frame counts are asserted as relationships (frames discarded &gt; 0, consumed &gt; 0 and
/// &lt;= published), never as wall-clock or exact-frame equality. The test skips when the Vulkan
/// loader, device, or validation layer is unavailable; it never fakes a pass.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class OffscreenAppGpuTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private const int TickLimit = 10;

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="OffscreenAppGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public OffscreenAppGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public void OffscreenAppPublishesPerTickAndStopsTheValidatedRendererCleanly()
    {
        var factory = new RecordingRendererFactory(new VulkanRendererFactory());
        double? lastTime = null;
        double? lastDelta = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            lastTime = engine.Time.Time;
            lastDelta = engine.Time.DeltaTime;
            if (game.TickCount == TickLimit)
            {
                engine.Exit();
            }
        };

        using var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithoutAssets()
            .WithOffscreen(320, 240)
            .WithValidation(true)
            .WithRendererFactory(factory)
            .Build();

        Exception? failure = AppTestSupport.RunCaptured(app);
        if (failure is Bork2dException unavailable && IsUnavailable(unavailable))
        {
            throw SkipException.ForSkip(
                $"A Vulkan 1.3 loader/device is not available: {unavailable.Message}");
        }

        Assert.Null(failure);
        Assert.Equal(1, factory.CreateCount);
        VulkanOffscreenRenderer renderer = Assert.IsType<VulkanOffscreenRenderer>(factory.LastRenderer);
        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(TickLimit, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(lastTime > 0d, "Offscreen ITime must advance per tick.");
        Assert.True(lastDelta >= 0d);

        // One published snapshot per completed tick, including the Exit tick (Q4=D1).
        SnapshotRingCounters ring = renderer.SnapshotCounters;
        Assert.Equal(TickLimit, ring.Published);
        Assert.True(ring.Consumed > 0, "The renderer consumed no snapshot.");
        Assert.True(
            ring.Consumed <= ring.Published,
            $"Consumed ({ring.Consumed}) must never exceed published ({ring.Published}).");
        Assert.InRange(ring.MaxLive, 1, SnapshotRing.SlotCapacity);
        Assert.True(renderer.Counters.FramesDiscarded > 0, "No offscreen frame was discarded.");
        Assert.Equal(0, renderer.Counters.FramesPresented);
        Assert.Equal(0, renderer.Counters.SwapchainRecreations);
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);

        // The host stopped/joined the renderer before Run returned; Join drained every slot.
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        AssertAllSlotsFree(renderer);

        _output.WriteLine($"setup={game.SetupCount}, ticks={game.TickCount}, "
            + $"teardown={game.TeardownCount}, time={lastTime:0.######}");
        _output.WriteLine($"ring: published={ring.Published}, consumed={ring.Consumed}, "
            + $"superseded={ring.Superseded}, backpressureWaits={ring.BackpressureWaits}, "
            + $"maxLive={ring.MaxLive}, suspended={renderer.SuspendedConsumptions}");
        _output.WriteLine($"renderer counters: {Describe(renderer.Counters)}");
    }

    private static string Describe(RendererCounters counters)
        => $"framesPresented={counters.FramesPresented}, "
            + $"framesDiscarded={counters.FramesDiscarded}, "
            + $"validationErrors={counters.ValidationErrorCount}, "
            + $"validationWarnings={counters.ValidationWarningCount}";

    private static void AssertAllSlotsFree(VulkanOffscreenRenderer renderer)
    {
        foreach (SnapshotSlot slot in renderer.SnapshotRing.Slots)
        {
            Assert.Equal(SnapshotSlotState.Free, slot.State);
        }

        Assert.Equal(-1, renderer.SnapshotRing.LatestIndex);
    }

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

    private sealed class RecordingRendererFactory : IRendererFactory
    {
        private readonly IRendererFactory _inner;

        internal RecordingRendererFactory(IRendererFactory inner) => _inner = inner;

        internal int CreateCount { get; private set; }

        internal IRenderer? LastRenderer { get; private set; }

        public IRenderer Create(RendererStartInfo startInfo)
        {
            CreateCount++;
            LastRenderer = _inner.Create(startInfo);
            return LastRenderer;
        }
    }
}
