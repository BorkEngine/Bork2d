using System.Diagnostics;
using System.Numerics;
using Bork2d.Bake.Shaders;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Textures;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof for the custom fragment shader path: instance uniforms, the frame time
/// input, declared additional textures sampled at their bindless slots, engine-wide global uniforms,
/// contiguous custom/default run batching with correct draw accounting, deterministic pending
/// behavior across a controllable load, and invalid-metadata fallback — all with defined captured
/// colors and validation 0 errors/0 warnings.
/// </summary>
/// <remarks>
/// The tests compile small fragment sources at runtime with the bake's <see cref="GlslCompiler"/> and
/// <see cref="ShaderPrologue"/>, so no binary SPIR-V fixture is committed. They skip when the Vulkan
/// loader/device/validation layer is unavailable (D-M6-05, D-M6-20).
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class CustomShaderGpuTests
{
    private const int FramebufferSize = 64;
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
    public async Task ConstantColorShaderReadsTheInstanceUniform()
    {
        using var assets = new TestRendererAssets();
        ShaderUniform[] instance =
        [
            new ShaderUniform("uColor", ShaderUniformType.Vector4, 0, Vector4.Zero),
        ];
        ShaderAsset shader = MakeShader("materials/constant-spv",
            instance: instance);
        byte[] spirV = Compile("void main() { outColor = uColor; }", instance);
        assets.AddShader("materials/constant", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset sprite = MakeSpriteAsset(texture: null, scale: 6f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(
                    snapshot,
                    sprite,
                    metadata,
                    zIndex: 0,
                    x: -4f,
                    uniform0: new Vector4(1f, 0f, 0f, 1f));
                AddSprite(
                    snapshot,
                    sprite,
                    metadata,
                    zIndex: 1,
                    x: 4f,
                    uniform0: new Vector4(0f, 0f, 1f, 1f));
            };
            PublishUntil(renderer, producer, fill, stats => stats.DrawCalls == 2 && stats.Instances == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 19, 32, 255, 0, 0, 255, 2, "the red instance uniform sprite");
            AssertPixelNear(result, 45, 32, 0, 0, 255, 255, 2, "the blue instance uniform sprite");

            // This minimal shader references no fixed-interface input besides its own uniform; the
            // engine wrapper keeps every vertex output consumed, so validation stays clean.
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
    public async Task TimeDrivenShaderReadsTheFrameTimeGlobalBlock()
    {
        using var assets = new TestRendererAssets();
        ShaderAsset shader = MakeShader("materials/time-spv");
        byte[] spirV = Compile(
            "void main() { outColor = vec4(BORK2D_TIME, 0.0, 0.0, 1.0); }");
        assets.AddShader("materials/time", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset sprite = MakeSpriteAsset(texture: null, scale: 10f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                snapshot.Time = 0.25;
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, metadata, zIndex: 0, x: 0f);
            };
            PublishUntil(renderer, producer, fill, stats => stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(
                result,
                32,
                32,
                LinearToSrgbByte(0.25f),
                0,
                0,
                255,
                2,
                "the frame-time red channel");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task DeclaredAdditionalTexturesAreSampledAtTheirBindlessSlots()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/red", 255, 0, 0);
        assets.AddSolidTexture("tex/green", 0, 255, 0);
        ShaderTextureSlot[] slots =
        [
            new ShaderTextureSlot("t0"),
            new ShaderTextureSlot("t1"),
        ];
        ShaderAsset shader = MakeShader("materials/textures-spv", slots: slots);
        byte[] spirV = Compile(
            "void main() { outColor = vec4(texture(BORK2D_TEXTURE0, vUv).r, "
                + "texture(BORK2D_TEXTURE1, vUv).g, 0.0, 1.0); }");
        assets.AddShader("materials/textures", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset sprite = MakeSpriteAsset(texture: null, scale: 10f);
        AssetRef<TextureDescriptor>[] extras = ["tex/red", "tex/green"];

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, metadata, zIndex: 0, x: 0f, extras: extras);
            };
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2 && stats.TexturePages >= 3);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 32, 32, 255, 255, 0, 255, 2, "both extra textures sampled");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task EngineGlobalUniformReachesTheShader()
    {
        using var assets = new TestRendererAssets();
        ShaderUniform[] globals =
        [
            new ShaderUniform("uTint", ShaderUniformType.Color, 0, Vector4.Zero),
        ];
        ShaderAsset shader = MakeShader("materials/global-spv",
            globals: globals);
        byte[] spirV = Compile("void main() { outColor = uTint; }", globals: globals);
        assets.AddShader("materials/global", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset sprite = MakeSpriteAsset(texture: null, scale: 10f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                snapshot.AddGlobalUniform(
                    new GlobalUniformSnapshot(
                        "uTint",
                        ShaderUniformType.Color,
                        new Vector4(0f, 0f, 1f, 1f)));
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, metadata, zIndex: 0, x: 0f);
            };
            PublishUntil(renderer, producer, fill, stats => stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 32, 32, 0, 0, 255, 255, 2, "the engine global uniform value");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task AlternatingDefaultAndCustomSpritesRecordOneRunPerContiguousGroup()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/white", 255, 255, 255);
        ShaderAsset shader = MakeShader("materials/red-spv");
        byte[] spirV = Compile("void main() { outColor = vec4(1.0, 0.0, 0.0, 1.0); }");
        assets.AddShader("materials/red", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset defaultSprite = MakeSpriteAsset("tex/white", scale: 2f);
        SpriteAsset customSprite = MakeSpriteAsset(texture: null, scale: 2f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, defaultSprite, shader: null, zIndex: 0, x: -8f);
                AddSprite(snapshot, customSprite, metadata, zIndex: 1, x: -4f);
                AddSprite(snapshot, defaultSprite, shader: null, zIndex: 2, x: 0f);
                AddSprite(snapshot, customSprite, metadata, zIndex: 3, x: 4f);
                AddSprite(snapshot, defaultSprite, shader: null, zIndex: 4, x: 8f);
            };
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 6 && stats.TexturePages >= 2);

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(1, stats.NonEmptyCameras);
            Assert.Equal(6, stats.DrawCalls);
            Assert.Equal(5, stats.Instances);
            Assert.Equal(0, stats.SkippedSprites);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 6, 32, 255, 255, 255, 255, 2, "the first default sprite");
            AssertPixelNear(result, 19, 32, 255, 0, 0, 255, 2, "the first custom sprite");
            AssertPixelNear(result, 32, 32, 255, 255, 255, 255, 2, "the middle default sprite");
            AssertPixelNear(result, 45, 32, 255, 0, 0, 255, 2, "the second custom sprite");
            AssertPixelNear(result, 58, 32, 255, 255, 255, 255, 2, "the last default sprite");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task PendingCustomShaderSkipsThenDrawsAfterTheLoadCompletes()
    {
        using var assets = new TestRendererAssets();
        ShaderAsset shader = MakeShader("materials/pending-spv");
        byte[] spirV = Compile("void main() { outColor = vec4(1.0, 0.0, 0.0, 1.0); }");
        assets.AddPendingShader("materials/pending", shader);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset sprite = MakeSpriteAsset(texture: null, scale: 10f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, metadata, zIndex: 0, x: 0f);
            };
            PublishUntil(renderer, producer, fill, stats => stats.SkippedSprites == 1);
            Assert.Equal(1, renderer.RenderStats.DrawCalls);
            Assert.Equal(0, renderer.RenderStats.Instances);
            Assert.Null(renderer.FatalException);

            assets.CompleteShader("materials/pending", spirV);
            PublishUntil(renderer, producer, fill, stats => stats.DrawCalls == 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 32, 32, 255, 0, 0, 255, 2, "the completed custom shader");
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task InvalidCustomMetadataFallsBackToTheDefaultShaderWithOneWarning()
    {
        using var assets = new TestRendererAssets();
        assets.AddSolidTexture("tex/white", 255, 255, 255);
        ShaderAsset shader = MakeShader("materials/invalid-spv",
            layoutVersion: ShaderLayout.CurrentVersion - 1);
        byte[] spirV = Compile("void main() { outColor = vec4(1.0, 0.0, 0.0, 1.0); }");
        assets.AddShader("materials/invalid", shader, spirV);
        ShaderRenderData metadata = ShaderRenderData.Freeze(shader);
        SpriteAsset sprite = MakeSpriteAsset("tex/white", scale: 10f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, loggerFactory);
            var producer = new Producer(renderer);
            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, sprite, metadata, zIndex: 0, x: 0f);
            };
            PublishUntil(
                renderer,
                producer,
                fill,
                stats => stats.DrawCalls == 2 && stats.TexturePages >= 2);

            FrameCaptureResult result = await CaptureAsync(renderer, producer, fill);
            AssertPixelNear(result, 32, 32, 255, 255, 255, 255, 2, "the default shader fallback");
            Assert.Equal(0, renderer.RenderStats.SkippedSprites);
            Assert.Contains(
                provider.Entries,
                entry => entry.Message.Contains("materials/invalid", StringComparison.Ordinal));
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static ShaderAsset MakeShader(
        string spirVPath,
        ShaderUniform[]? instance = null,
        ShaderUniform[]? globals = null,
        ShaderTextureSlot[]? slots = null,
        uint layoutVersion = ShaderLayout.CurrentVersion)
        => new()
        {
            SpirV = spirVPath,
            LayoutVersion = layoutVersion,
            EntryPoint = "main",
            InstanceUniforms = instance ?? [],
            GlobalUniforms = globals ?? [],
            TextureSlots = slots ?? [],
        };

    private static byte[] Compile(
        string source,
        ShaderUniform[]? instance = null,
        ShaderUniform[]? globals = null)
    {
        string prologue = ShaderPrologue.Build(
            instance ?? [],
            globals ?? []);
        if (!GlslCompiler.TryCompileFragment(
            ShaderPrologue.Compose(prologue, source),
            "custom-shader-test.frag",
            out byte[] spirV,
            out string message))
        {
            throw new InvalidOperationException($"The test shader failed to compile: {message}");
        }

        return spirV;
    }

    private static SpriteAsset MakeSpriteAsset(string? texture, float scale) => new()
    {
        Texture = texture,
        Uvs = _fullUvs,
        Scale = new Vector2(scale),
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
    };

    private static void AddCamera(FrameSnapshot snapshot)
        => snapshot.AddCamera(
            new CameraSnapshot(
                ZIndex: 0,
                WorldMatrix: Matrix3x2.Identity,
                Size: 10f,
                Viewport: new Vector4(0f, 0f, 1f, 1f),
                CullingMask: 1u,
                Clear: ClearKind.Color,
                ClearColor: _clearColor));

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        ShaderRenderData? shader,
        int zIndex,
        float x,
        Vector4? uniform0 = null,
        AssetRef<TextureDescriptor>[]? extras = null)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, 0f),
                Color.White,
                Layer: 1u,
                ZIndex: zIndex,
                FlipX: false,
                FlipY: false)
            {
                ShaderData = shader,
                Uniform0 = uniform0 ?? Vector4.Zero,
                ExtraTexture0 = GetExtraTexture(extras, 0),
                ExtraTexture1 = GetExtraTexture(extras, 1),
                ExtraTexture2 = GetExtraTexture(extras, 2),
                ExtraTexture3 = GetExtraTexture(extras, 3),
                AdditionalTextureCount = extras?.Length ?? 0,
            });

    private static AssetRef<TextureDescriptor> GetExtraTexture(
        AssetRef<TextureDescriptor>[]? extras,
        int index)
        => extras is not null && index < extras.Length ? extras[index] : default;

    private static ILoggerFactory CreateLoggerFactory(out CapturingLoggerProvider provider)
    {
        var created = new CapturingLoggerProvider();
        provider = created;
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Warning);
            builder.AddProvider(created);
        });
    }

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
            throw new TimeoutException("The custom-shader offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException(
                "The custom-shader renderer faulted during startup.",
                fatal);
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

    private static byte LinearToSrgbByte(float linear)
    {
        float encoded = linear <= 0.0031308f
            ? 12.92f * linear
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(encoded * 255f), 0f, 255f);
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
