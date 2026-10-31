using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Handoff;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Opt-in host-level real-GPU proof for the windowed M7 handoff (Q19=S3): a real window/instance/
/// surface through <see cref="Bork2dApp"/> and a recording renderer factory. A test game ticks and
/// exits after a fixed count; the host publishes one snapshot per tick, the renderer presents one
/// frame per consumed snapshot, the run joins cleanly with every slot <c>Free</c>, and validation
/// reports zero errors/warnings.
/// </summary>
/// <remarks>
/// Frame counts are asserted as relationships only (presented &gt; 0 and &lt;= consumed &lt;=
/// published), never exact-frame or wall-clock equality. The test skips when the desktop session,
/// Vulkan loader/device, or validation layer is unavailable; it never fakes a pass.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class WindowedAppGpuTests
{
    private const int TickLimit = 30;

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="WindowedAppGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public WindowedAppGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public void WindowedAppPresentsPerTickAndStopsTheValidatedRendererCleanly()
    {
        var provider = new CapturingLoggerProvider();
        var factory = new RecordingRendererFactory(new VulkanRendererFactory());
        double? lastTime = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            lastTime = game.Engine!.Time.Time;
            if (game.TickCount == TickLimit)
            {
                game.Engine.Exit();
            }
        };

        Exception? failure;
        using (var app = new Bork2dAppBuilder<GameLayer>()
            .WithGame(game)
            .WithTitle("Bork2d WP-4 host windowed GPU test")
            .WithSize(960, 540)
            .WithoutAssets()
            .WithValidation(true)
            .WithRendererFactory(factory)
            .WithLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddProvider(provider);
            })
            .Build())
        {
            failure = AppTestSupport.RunCaptured(app);
        }

        if (failure is Bork2dException unavailable && IsUnavailable(unavailable))
        {
            throw SkipException.ForSkip(
                $"A desktop Vulkan 1.3 session is not available: {unavailable.Message}");
        }

        Assert.Null(failure);
        Assert.Equal(1, factory.CreateCount);
        VulkanRenderer renderer = Assert.IsType<VulkanRenderer>(factory.LastRenderer);
        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        Assert.Equal(1, game.SetupCount);
        Assert.Equal(TickLimit, game.TickCount);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(lastTime > 0d, "Windowed ITime must advance per tick.");

        // One published snapshot per completed tick, including the Exit tick (Q4=D1).
        SnapshotRingCounters ring = renderer.SnapshotCounters;
        Assert.Equal(TickLimit, ring.Published);
        Assert.True(ring.Consumed > 0, "The renderer consumed no snapshot.");
        Assert.True(
            ring.Consumed <= ring.Published,
            $"Consumed ({ring.Consumed}) must never exceed published ({ring.Published}).");
        Assert.InRange(ring.MaxLive, 1, SnapshotRing.SlotCapacity);
        Assert.True(renderer.Counters.FramesPresented > 0, "No windowed frame was presented.");
        Assert.True(
            renderer.Counters.FramesPresented <= ring.Consumed,
            "One presented frame per consumed snapshot (Q7=G1).");
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);

        // The host stopped/joined the renderer before Run returned; Join drained every slot.
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        AssertAllSlotsFree(renderer);

        WriteEvidence(provider, game, renderer, ring, lastTime);
    }

    private void WriteEvidence(
        CapturingLoggerProvider provider,
        ScriptedGame<GameLayer> game,
        VulkanRenderer renderer,
        SnapshotRingCounters ring,
        double? lastTime)
    {
        _output.WriteLine($"setup={game.SetupCount}, ticks={game.TickCount}, "
            + $"teardown={game.TeardownCount}, time={lastTime:0.######}");
        _output.WriteLine($"ring: published={ring.Published}, consumed={ring.Consumed}, "
            + $"superseded={ring.Superseded}, backpressureWaits={ring.BackpressureWaits}, "
            + $"maxLive={ring.MaxLive}, suspended={renderer.SuspendedConsumptions}");
        _output.WriteLine($"renderer counters: {Describe(renderer.Counters)}");
        _output.WriteLine($"OS: {Environment.OSVersion.VersionString}");
        _output.WriteLine($"VULKAN_SDK: {Environment.GetEnvironmentVariable("VULKAN_SDK") ?? "(unset)"}");

        foreach (CapturingLoggerProvider.CapturedLog entry in provider.Entries)
        {
            if (entry.Level >= LogLevel.Information)
            {
                _output.WriteLine($"{entry.Category} {entry.Level}: {entry.Message}");
            }
        }
    }

    private static string Describe(RendererCounters counters)
        => $"framesPresented={counters.FramesPresented}, "
            + $"framesDiscarded={counters.FramesDiscarded}, "
            + $"recreations={counters.SwapchainRecreations}, "
            + $"validationErrors={counters.ValidationErrorCount}, "
            + $"validationWarnings={counters.ValidationWarningCount}";

    private static void AssertAllSlotsFree(VulkanRenderer renderer)
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
            || message.Contains("SDL_", StringComparison.Ordinal)
            || message.Contains("SDL ", StringComparison.Ordinal)
            || message.Contains("zero Vulkan surface", StringComparison.Ordinal)
            || message.Contains("No sRGB", StringComparison.Ordinal);
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
