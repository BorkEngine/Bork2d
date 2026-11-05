using System.Diagnostics;
using System.Numerics;
using Bork2d.Bake.Svg;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.Bake.Svg;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof for the built-in <c>shader/sdf</c> fragment shader: a baked MSDF texture's
/// hole, ring, and background sample to the documented colors, the declared <c>uThreshold</c> and
/// <c>uSoftness</c> instance uniforms change the reconstructed coverage, the explicit BC7
/// <c>CompRgba8</c> SDF format samples through the same path, and a minified sprite that forces the
/// sampler down the mip chain keeps the field's sign — all with validation 0 errors/0 warnings.
/// </summary>
/// <remarks>
/// The tests bake a square annulus SVG with the task-2 <see cref="SvgSdfBaker"/>, so no binary fixture
/// is committed, and follow the <see cref="CustomShaderGpuTests"/> offscreen lifecycle, producer, and
/// capture helpers. They skip when the Vulkan loader/device/validation layer is unavailable.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class SdfRenderGpuTests
{
    private const int FramebufferSize = 64;

    /// <summary>The square annulus: an outer square with an even-odd hole.</summary>
    private const string AnnulusBody =
        "<path fill-rule=\"evenodd\" d=\"M10 10 H90 V90 H10 Z M35 35 H65 V65 H35 Z\"/>";

    private const string AnnulusViewBox = "0 0 100 100";

    /// <summary>
    /// The artboard is 11 world units high, so the camera's 64x64 framebuffer at 3.2 px/unit puts the
    /// outer square at world +/-4.4 and the hole at world +/-1.65.
    /// </summary>
    private const float ArtboardWorldUnits = 11f;

    /// <summary>The minified artboard is 5.5 world units high, which forces the mip chain.</summary>
    private const float MinifiedArtboardWorldUnits = 5.5f;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private static readonly Color _clearColor = new(0f, 0f, 0f, 1f);

    [GpuFact]
    public async Task BakedSdfTextureSamplesTheHoleRingAndBackground()
    {
        using var assets = new TestRendererAssets();
        SvgSdfBakeResult bake = BakeAnnulus(SdfCompression.None);
        assets.AddBakedTexture(
            "sdf/donut",
            bake.Texture,
            (uint)bake.TextureWidth,
            (uint)bake.TextureHeight,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        ShaderRenderData shader = LoadSdfShader(assets);
        SpriteAsset wordmark = MakeSprite("sdf/donut", bake.SpriteScale * ArtboardWorldUnits);

        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, wordmark, shader);
            PublishUntil(renderer, producer, fill, IsDrawn);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);

            // Screen x maps to world (x - 32) / 3.2: the hole center, a ring sample, and the
            // background beyond the outer square.
            AssertPixelNear(result, 32, 32, 0, 0, 0, 255, 3, "the hole center is outside the field");
            AssertPixelNear(result, 40, 32, 255, 255, 255, 255, 3, "the ring sample is inside the field");
            AssertPixelNear(result, 48, 32, 0, 0, 0, 255, 3, "the background beyond the square is outside");

            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task ThresholdMovesTheSdfEdgeAndSoftnessWidensIt()
    {
        using var assets = new TestRendererAssets();
        SvgSdfBakeResult bake = BakeAnnulus(SdfCompression.None);
        assets.AddBakedTexture(
            "sdf/donut",
            bake.Texture,
            (uint)bake.TextureWidth,
            (uint)bake.TextureHeight,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        ShaderRenderData shader = LoadSdfShader(assets);
        SpriteAsset wordmark = MakeSprite("sdf/donut", bake.SpriteScale * ArtboardWorldUnits);

        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);

            // Screen x = 38 samples world 1.875, where the field is about 0.6: inside at the default
            // 0.5 threshold and outside once the threshold is raised to 0.75. Only the instance
            // uniform block differs between the captures, so a changed pixel proves the declared
            // uniforms reach the shader. uSoftness is lane 0 and uThreshold lane 1, in ordinal
            // declaration order.
            Action<FrameSnapshot> tightDefault = snapshot => AddScene(
                snapshot,
                wordmark,
                shader,
                uniform0: new Vector4(0.05f, 0.5f, 0f, 0f));
            PublishUntil(renderer, producer, tightDefault, IsDrawn);
            FrameCaptureResult inside = await CaptureAsync(renderer, producer, tightDefault);
            AssertPixelNear(inside, 38, 32, 255, 255, 255, 255, 3, "the edge sample inside the 0.5 threshold");

            Action<FrameSnapshot> tightRaised = snapshot => AddScene(
                snapshot,
                wordmark,
                shader,
                uniform0: new Vector4(0.05f, 0.75f, 0f, 0f));
            PublishUntil(renderer, producer, tightRaised, IsDrawn);
            FrameCaptureResult outside = await CaptureAsync(renderer, producer, tightRaised);
            AssertPixelNear(outside, 38, 32, 0, 0, 0, 255, 3, "the edge sample above the 0.75 threshold");
            AssertPixelNear(outside, 32, 32, 0, 0, 0, 255, 3, "the hole center stays outside");
            AssertPixelNear(outside, 48, 32, 0, 0, 0, 255, 3, "the background stays outside");

            // Widening uSoftness back to 1.0 with the same threshold spreads the transition across the
            // edge sample instead of stepping at it, so the same pixel becomes partially covered.
            Action<FrameSnapshot> softRaised = snapshot => AddScene(
                snapshot,
                wordmark,
                shader,
                uniform0: new Vector4(1f, 0.75f, 0f, 0f));
            PublishUntil(renderer, producer, softRaised, IsDrawn);
            FrameCaptureResult softened = await CaptureAsync(renderer, producer, softRaised);
            Assert.InRange(ReadRed(softened, 38, 32), 32, 224);

            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task Bc7SdfTextureSamplesTheHoleRingAndBackground()
    {
        using var assets = new TestRendererAssets();
        SvgSdfBakeResult bake = BakeAnnulus(SdfCompression.Bc7);
        Assert.Equal(TextureFormat.CompRgba8, bake.Texture.Format);
        assets.AddBakedTexture(
            "sdf/donut-bc7",
            bake.Texture,
            (uint)bake.TextureWidth,
            (uint)bake.TextureHeight,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        ShaderRenderData shader = LoadSdfShader(assets);
        SpriteAsset wordmark = MakeSprite("sdf/donut-bc7", bake.SpriteScale * ArtboardWorldUnits);

        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, wordmark, shader);
            PublishUntil(renderer, producer, fill, IsDrawn);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 32, 32, 0, 0, 0, 255, 3, "the BC7 hole center is outside the field");
            AssertPixelNear(result, 40, 32, 255, 255, 255, 255, 3, "the BC7 ring sample is inside the field");
            AssertPixelNear(result, 48, 32, 0, 0, 0, 255, 3, "the BC7 background is outside the field");

            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task MinifiedSdfSamplingPreservesTheHoleRingAndBackground()
    {
        using var assets = new TestRendererAssets();
        SvgSdfBakeResult bake = BakeAnnulus(SdfCompression.None);
        assets.AddBakedTexture(
            "sdf/donut",
            bake.Texture,
            (uint)bake.TextureWidth,
            (uint)bake.TextureHeight,
            TextureFilter.Linear,
            TextureWrap.Clamp);
        ShaderRenderData shader = LoadSdfShader(assets);
        SpriteAsset wordmark = MakeSprite("sdf/donut", bake.SpriteScale * MinifiedArtboardWorldUnits);

        using ILoggerFactory loggerFactory = CreateLoggerFactory();
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot => AddScene(snapshot, wordmark, shader);
            PublishUntil(renderer, producer, fill, IsDrawn);

            // At 5.5 world units the artboard is 2.5 texels per screen pixel, so the sampler minifies
            // down the mip chain. World 1.5 is inside the ring (screen 37) and world 3.125 is outside
            // the outer square but still inside the sprite quad (screen 42).
            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 32, 32, 0, 0, 0, 255, 3, "the minified hole center is outside the field");
            AssertPixelNear(result, 37, 32, 255, 255, 255, 255, 3, "the minified ring sample is inside the field");
            AssertPixelNear(result, 42, 32, 0, 0, 0, 255, 3, "the minified background is outside the field");

            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static SvgSdfBakeResult BakeAnnulus(SdfCompression compression)
    {
        SvgSdfBakeResult result = SvgSdfTestSupport.Bake(
            AnnulusBody,
            SvgSdfTestSupport.Options(resolution: 64, range: 8, padding: 2, compression: compression),
            AnnulusViewBox);

        // 64x64 with a 44x44 artboard: the 11-unit sprite scale maps four texels to one world unit.
        Assert.Equal(64, result.TextureWidth);
        Assert.Equal(64, result.TextureHeight);
        Assert.Equal(44.0, result.ContentWidth, 10);
        Assert.Equal(44.0, result.ContentHeight, 10);
        return result;
    }

    private static ShaderRenderData LoadSdfShader(TestRendererAssets assets)
    {
        ShaderAsset shader = assets.LoadAsset<ShaderAsset>("shader/sdf").GetAwaiter().GetResult();
        return ShaderRenderData.Freeze(shader);
    }

    private static SpriteAsset MakeSprite(string texture, Vector2 scale) => new()
    {
        Texture = texture,
        Uvs = _fullUvs,
        Scale = scale,
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
    };

    private static void AddScene(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        ShaderRenderData shader,
        Vector4? uniform0 = null)
    {
        snapshot.AddCamera(
            new CameraSnapshot(
                ZIndex: 0,
                WorldMatrix: Matrix3x2.Identity,
                Size: 10f,
                Viewport: new Vector4(0f, 0f, 1f, 1f),
                CullingMask: 1u,
                Clear: ClearKind.Color,
                ClearColor: _clearColor));
        snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.Identity,
                Color.White,
                Layer: 1u,
                ZIndex: 0,
                FlipX: false,
                FlipY: false)
            {
                ShaderData = shader,
                Uniform0 = uniform0 ?? shader.DefaultInstanceUniformBlock[0],
                Uniform1 = shader.DefaultInstanceUniformBlock[1],
                Uniform2 = shader.DefaultInstanceUniformBlock[2],
                Uniform3 = shader.DefaultInstanceUniformBlock[3],
            });
    }

    private static bool IsDrawn(RenderStats stats)
        => stats.DrawCalls == 2 && stats.Instances == 1 && stats.SkippedSprites == 0;

    private static ILoggerFactory CreateLoggerFactory()
        => LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Warning));

    private static VulkanOffscreenRenderer StartOffscreen(
        TestRendererAssets assets,
        ILoggerFactory loggerFactory)
    {
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = FramebufferSize,
            Height = FramebufferSize,
            ValidationRequested = true,
            LoggerFactory = loggerFactory,
            Assets = assets,
        };

        var renderer = Assert.IsType<VulkanOffscreenRenderer>(
            new VulkanRendererFactory().Create(startInfo));
        renderer.Start();
        if (!renderer.WaitForReady(_timeout))
        {
            throw new TimeoutException("The SDF offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException("The SDF renderer faulted during startup.", fatal);
        }

        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        return renderer;
    }

    private static void PublishUntil(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill,
        Func<RenderStats, bool> until)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!until(renderer.RenderStats))
        {
            if (renderer.FatalException is { } fatal)
            {
                throw new InvalidOperationException("The renderer faulted while publishing.", fatal);
            }

            if (stopwatch.Elapsed >= _timeout)
            {
                throw new TimeoutException(
                    "The frame did not reach the expected state: "
                        + Describe(renderer.RenderStats));
            }

            PublishFrame(renderer, producer, fill);
        }
    }

    private static void PublishFrame(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill)
    {
        long sequence = renderer.RenderStats.FrameSequence;
        producer.Publish(fill);
        if (!renderer.WaitForProgress(
            () => renderer.RenderStats.FrameSequence > sequence,
            _timeout))
        {
            throw new TimeoutException(
                $"The frame was not published within {_timeout.TotalSeconds:0} s. "
                    + Describe(renderer.RenderStats));
        }
    }

    private static async Task<FrameCaptureResult> CaptureAsync(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill)
    {
        Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
        PublishFrame(renderer, producer, fill);
        return await capture.WaitAsync(_timeout);
    }

    private static void StopAndAssertClean(VulkanOffscreenRenderer renderer)
    {
        renderer.RequestStop();
        Assert.True(renderer.Join(_joinTimeout), "The renderer did not join.");
        Assert.Equal(RendererStatus.Stopped, renderer.Status);
        Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        Assert.Null(renderer.FatalException);
        Assert.Equal(0, renderer.Counters.ValidationErrorCount);
        Assert.Equal(0, renderer.Counters.ValidationWarningCount);
    }

    private static void Finish(VulkanOffscreenRenderer? renderer)
    {
        if (renderer is not null)
        {
            renderer.RequestStop();
            _ = renderer.Join(_joinTimeout);
        }
    }

    private static string Describe(RenderStats stats)
        => $"frame={stats.FrameSequence}, cameras={stats.Cameras}, "
            + $"nonEmpty={stats.NonEmptyCameras}, draws={stats.DrawCalls}, "
            + $"instances={stats.Instances}, pages={stats.TexturePages}/"
            + $"{stats.TexturePageCapacity}, skipped={stats.SkippedSprites}, "
            + $"validation={stats.ValidationErrors}/{stats.ValidationWarnings}";

    private static void AssertPixelNear(
        FrameCaptureResult result,
        int x,
        int y,
        byte r,
        byte g,
        byte b,
        byte a,
        int tolerance,
        string because)
    {
        int offset = ((y * result.Width) + x) * 4;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        byte actualR = pixels[offset];
        byte actualG = pixels[offset + 1];
        byte actualB = pixels[offset + 2];
        byte actualA = pixels[offset + 3];
        Assert.True(
            Math.Abs(actualR - r) <= tolerance
                && Math.Abs(actualG - g) <= tolerance
                && Math.Abs(actualB - b) <= tolerance
                && Math.Abs(actualA - a) <= tolerance,
            $"{because}: pixel ({x}, {y}) is ({actualR}, {actualG}, {actualB}, {actualA}), "
                + $"expected ({r}, {g}, {b}, {a}) +/- {tolerance}.");
    }

    private static byte ReadRed(FrameCaptureResult result, int x, int y)
        => result.Pixels.Span[((y * result.Width) + x) * 4];

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

    private sealed class Producer
    {
        private readonly VulkanOffscreenRenderer _renderer;
        private long _published;

        internal Producer(VulkanOffscreenRenderer renderer) => _renderer = renderer;

        internal void Publish(Action<FrameSnapshot> fill)
        {
            var slot = _renderer.AcquireSnapshotSlot()
                ?? throw new InvalidOperationException(
                    "The renderer stopped consuming; the test producer cannot publish.");
            slot.Snapshot.Reset();
            slot.Snapshot.Sequence = ++_published;
            slot.Snapshot.Time = _published * 0.016;
            slot.Snapshot.DeltaTime = 0.016;
            fill(slot.Snapshot);
            _renderer.PublishSnapshot(slot);
        }
    }
}
