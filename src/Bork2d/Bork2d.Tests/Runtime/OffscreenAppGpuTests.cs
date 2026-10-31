using Bork2d.Engine;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Runtime;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Bork2d.Tests.Runtime;

/// <summary>
/// Opt-in real-GPU proof for the offscreen app host: <c>WithOffscreen(320, 240)</c> +
/// <c>WithValidation(true)</c> runs the renderer through the internal factory hook, loops
/// <c>Setup</c> → <c>Tick</c>… → <c>Teardown</c> until <c>Exit</c>, and stops/joins cleanly with
/// discarded frames and zero validation messages (D-M6-07).
/// </summary>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class OffscreenAppGpuTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    private readonly ITestOutputHelper _output;

    /// <summary>Initializes a new instance of the <see cref="OffscreenAppGpuTests"/> class.</summary>
    /// <param name="output">The xUnit output sink.</param>
    public OffscreenAppGpuTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [GpuFact]
    public void OffscreenAppLoopsUntilExitAndStopsTheValidatedRenderer()
    {
        var factory = new RecordingRendererFactory(new VulkanRendererFactory());
        double? lastTime = null;
        double? delta = null;
        VulkanOffscreenRenderer? renderer = null;
        var game = new ScriptedGame<GameLayer>();
        game.OnTick = () =>
        {
            var engine = game.Engine!;
            renderer ??= Assert.IsType<VulkanOffscreenRenderer>(factory.LastRenderer);
            if (game.TickCount == 1)
            {
                Assert.True(
                    renderer.WaitForProgress(
                        counters => counters.FramesDiscarded > 0,
                        _timeout),
                    "The offscreen app renderer discarded no frame before the first tick ended. "
                        + Describe(renderer.Counters));
            }

            lastTime = engine.Time.Time;
            delta = engine.Time.DeltaTime;
            if (game.TickCount >= 2)
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
        Assert.NotNull(renderer);
        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        Assert.Equal(1, game.SetupCount);
        Assert.True(game.TickCount >= 2);
        Assert.Equal(1, game.TeardownCount);
        Assert.True(lastTime!.Value > 0d, "Offscreen ITime must advance per tick.");
        Assert.True(delta!.Value >= 0d);

        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        Assert.True(renderer.Counters.FramesDiscarded > 0);
        Assert.Equal(0, renderer.Counters.FramesPresented);
        Assert.Equal(0, renderer.Counters.SwapchainRecreations);
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);

        _output.WriteLine($"setup={game.SetupCount}, ticks={game.TickCount}, "
            + $"teardown={game.TeardownCount}, time={lastTime:0.######}");
        _output.WriteLine($"renderer counters: {Describe(renderer.Counters)}");
    }

    private static string Describe(RendererCounters counters)
        => $"framesPresented={counters.FramesPresented}, "
            + $"framesDiscarded={counters.FramesDiscarded}, "
            + $"validationErrors={counters.ValidationErrorCount}, "
            + $"validationWarnings={counters.ValidationWarningCount}";

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
