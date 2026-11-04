using System.Numerics;
using Bork2d.Bake;
using Bork2d.Engine;
using Bork2d.Engine.Assets;
using Bork2d.Engine.Rendering;
using Bork2d.Engine.Scene;
using Bork2d.Render;
using Bork2d.Render.Diagnostics;
using Bork2d.Render.Vulkan;
using Bork2d.Tests.TestDoubles;
using Bork2d.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Sdk;

namespace Bork2d.Tests.Render.Vulkan;

/// <summary>
/// Opt-in real-GPU proof that every baked KTX2 format uploads in its concrete Vulkan format and
/// samples the channels it declares, and that minified sprites use the supplied mip chain.
/// </summary>
/// <remarks>
/// The tests use <see cref="TestRendererAssets"/>, which serves the built-in placeholder from the
/// embedded pack and generates KTX2 payloads with <see cref="TextureBaker"/>. They skip when the
/// Vulkan loader/device/validation layer is unavailable or the device lacks
/// <c>textureCompressionBC</c> (D-M6-05, D-M6-20).
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class Ktx2TextureGpuTests
{
    private const int FramebufferWidth = 320;
    private const int FramebufferHeight = 64;
    private const float CameraSize = 10f;

    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    [GpuFact]
    public void EveryUncompressedKtx2FormatSamplesItsDeclaredChannels()
    {
        using var assets = new TestRendererAssets();
        var rows = new[]
        {
            Expect(TextureFormat.Rgba8, r: 200, g: 100, b: 50, a: 255, tolerance: 2),
            Expect(TextureFormat.Rgb8, r: 200, g: 100, b: 50, a: 255, tolerance: 2),
            Expect(TextureFormat.Rg8, r: 200, g: 100, b: 0, a: 255, tolerance: 2),
            Expect(TextureFormat.R8, r: 200, g: 0, b: 0, a: 255, tolerance: 2),
        };

        CaptureFormats(assets, rows);
    }

    [GpuFact]
    public void BlockCompressedKtx2FormatsSampleOnlyTheirDeclaredChannels()
    {
        using var assets = new TestRendererAssets();
        var rows = new[]
        {
            Expect(TextureFormat.CompRgba8, r: 200, g: 100, b: 50, a: 255, tolerance: 10, width: 5, height: 5),
            Expect(TextureFormat.CompRgb8, r: 200, g: 100, b: 50, a: 255, tolerance: 24, width: 3, height: 3),
            Expect(TextureFormat.CompRg8, r: 200, g: 100, b: 0, a: 255, tolerance: 5, width: 5, height: 3),
            Expect(TextureFormat.CompR8, r: 200, g: 0, b: 0, a: 255, tolerance: 5, width: 3, height: 5),
        };

        CaptureFormats(assets, rows);
    }

    [GpuFact]
    public void MinifiedSamplingUsesTheMipChain()
    {
        const int resolution = 64;
        BakedTexture baked = TextureBaker.Bake(
            Ktx2TextureFactory.RedGreenCheckerboard(resolution),
            resolution,
            resolution,
            TextureChannelType.Rgba,
            TextureCompression.None);
        using var assets = new TestRendererAssets();
        assets.AddBakedTexture(
            "tex/mips",
            baked,
            resolution,
            resolution,
            TextureFilter.Nearest);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer renderer = StartOffscreen(
            assets,
            resolution,
            resolution,
            loggerFactory);
        try
        {
            var producer = new Producer(renderer);
            SpriteAsset magnified = MakeSprite("tex/mips", scale: 20f);
            Action<FrameSnapshot> magnifiedFill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, magnified, layer: 1u);
            };
            PublishUntilReady(renderer, producer, magnifiedFill, expectedInstances: 1);
            FrameCaptureResult magnifiedResult = Capture(
                renderer,
                producer,
                magnifiedFill);

            // The 1:1 magnified frame samples mip level 0 with a nearest filter; texel (32, 32) is
            // red in the red/green checkerboard.
            AssertPixelNear(
                magnifiedResult,
                resolution / 2,
                resolution / 2,
                255,
                0,
                0,
                255,
                tolerance: 2,
                "the magnified mip-0 red texel");

            SpriteAsset minified = MakeSprite("tex/mips", scale: 2f);
            Action<FrameSnapshot> minifiedFill = snapshot =>
            {
                AddCamera(snapshot);
                AddSprite(snapshot, minified, layer: 1u);
            };
            PublishUntilReady(renderer, producer, minifiedFill, expectedInstances: 1);
            FrameCaptureResult minifiedResult = Capture(renderer, producer, minifiedFill);

            // A 64x64 texture drawn into roughly six pixels selects a small mip. Every level below 1
            // averages the checkerboard to half red and half green, so the sampled pixel is a
            // near-equal red/green mix rather than the pure red of level 0.
            (byte red, byte green, byte blue, byte alpha) = ReadPixel(
                minifiedResult,
                resolution / 2,
                resolution / 2);
            Assert.InRange(red, 160, 215);
            Assert.InRange(green, 160, 215);
            Assert.InRange(blue, 0, 4);
            Assert.Equal(255, alpha);

            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Null(renderer.FatalException);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static void CaptureFormats(
        TestRendererAssets assets,
        IReadOnlyList<FormatExpectation> rows)
    {
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer renderer = StartOffscreen(
            assets,
            FramebufferWidth,
            FramebufferHeight,
            loggerFactory);
        try
        {
            var producer = new Producer(renderer);
            var sprites = new SpriteAsset[rows.Count];
            for (var index = 0; index < rows.Count; index++)
            {
                string path = assets.AddTexture(
                    $"tex/format-{index}",
                    rows[index].Format,
                    200,
                    100,
                    50,
                    255,
                    rows[index].Width,
                    rows[index].Height);
                sprites[index] = MakeSprite(path, scale: 4f);
            }

            Action<FrameSnapshot> fill = snapshot =>
            {
                AddCamera(snapshot);
                for (var index = 0; index < sprites.Length; index++)
                {
                    AddSprite(
                        snapshot,
                        sprites[index],
                        layer: 1u,
                        zIndex: index,
                        x: (-21f) + (index * 6f));
                }
            };
            PublishUntilReady(renderer, producer, fill, expectedInstances: sprites.Length);
            FrameCaptureResult result = Capture(renderer, producer, fill);

            for (var index = 0; index < rows.Count; index++)
            {
                float worldX = (-21f) + (index * 6f);
                int pixelX = (int)MathF.Round(
                    (worldX + (CameraSize * (FramebufferWidth / (float)FramebufferHeight))) /
                    (2f * CameraSize * (FramebufferWidth / (float)FramebufferHeight)) *
                    FramebufferWidth);
                AssertPixelNear(
                    result,
                    pixelX,
                    FramebufferHeight / 2,
                    rows[index].Red,
                    rows[index].Green,
                    rows[index].Blue,
                    rows[index].Alpha,
                    rows[index].Tolerance,
                    $"{rows[index].Format} channels");
            }

            Assert.Equal(rows.Count + 1, renderer.RenderStats.TexturePages);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Null(renderer.FatalException);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    private static FormatExpectation Expect(
        TextureFormat format,
        byte r,
        byte g,
        byte b,
        byte a,
        int tolerance,
        uint width = 4,
        uint height = 4)
        => new(
            format,
            LinearToSrgbByte(r / 255f),
            LinearToSrgbByte(g / 255f),
            LinearToSrgbByte(b / 255f),
            a,
            tolerance,
            width,
            height);

    private static FrameCaptureResult Capture(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill)
    {
        Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
        PublishFrame(renderer, producer, fill);
        return capture.WaitAsync(_timeout).GetAwaiter().GetResult();
    }

    private static void PublishUntilReady(
        VulkanOffscreenRenderer renderer,
        Producer producer,
        Action<FrameSnapshot> fill,
        int expectedInstances)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            PublishFrame(renderer, producer, fill);
            if (renderer.RenderStats.Instances == expectedInstances
                && renderer.RenderStats.TexturePages >= expectedInstances + 1)
            {
                return;
            }
        }

        throw new TimeoutException(
            $"The KTX2 pages did not become ready: {Describe(renderer.RenderStats)}.");
    }

    private static VulkanOffscreenRenderer StartOffscreen(
        TestRendererAssets assets,
        int width,
        int height,
        ILoggerFactory loggerFactory)
    {
        var startInfo = new RendererStartInfo
        {
            Mode = RendererMode.Offscreen,
            InstanceHandle = 0,
            Width = width,
            Height = height,
            ValidationRequested = true,
            LoggerFactory = loggerFactory,
            Assets = assets,
        };

        var renderer = Assert.IsType<VulkanOffscreenRenderer>(
            new VulkanRendererFactory().Create(startInfo));
        renderer.Start();
        if (!renderer.WaitForReady(_timeout))
        {
            throw new TimeoutException("The offscreen renderer did not become ready.");
        }

        if (renderer.FatalException is { } fatal)
        {
            if (IsUnavailable(fatal))
            {
                throw SkipException.ForSkip(
                    $"No suitable Vulkan 1.3 offscreen device is available: {fatal.Message}");
            }

            throw new InvalidOperationException("The offscreen renderer faulted during startup.", fatal);
        }

        if (!renderer.ValidationEnabled)
        {
            throw SkipException.ForSkip(
                $"{VulkanNames.ValidationLayer} is not available; validation evidence cannot be "
                    + "gathered on this machine.");
        }

        return renderer;
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

    private static SpriteAsset MakeSprite(string texture, float scale) => new()
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
                0,
                Matrix3x2.Identity,
                CameraSize,
                new Vector4(0f, 0f, 1f, 1f),
                1u,
                ClearKind.Color,
                new Color(0f, 0f, 0f, 1f)));

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        uint layer,
        int zIndex = 0,
        float x = 0f)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, 0f),
                new Color(1f, 1f, 1f, 1f),
                layer,
                zIndex,
                FlipX: false,
                FlipY: false));

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
        (byte actualR, byte actualG, byte actualB, byte actualA) = ReadPixel(result, x, y);
        Assert.True(
            Math.Abs(actualR - r) <= tolerance
                && Math.Abs(actualG - g) <= tolerance
                && Math.Abs(actualB - b) <= tolerance
                && Math.Abs(actualA - a) <= tolerance,
            $"{because}: pixel ({x}, {y}) is ({actualR}, {actualG}, {actualB}, {actualA}), "
                + $"expected ({r}, {g}, {b}, {a}) +/- {tolerance}.");
    }

    private static (byte R, byte G, byte B, byte A) ReadPixel(
        FrameCaptureResult result,
        int x,
        int y)
    {
        int offset = ((y * result.Width) + x) * 4;
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        return (pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]);
    }

    private static byte LinearToSrgbByte(float linear)
    {
        float encoded = linear <= 0.0031308f
            ? 12.92f * linear
            : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp(MathF.Round(encoded * 255f), 0f, 255f);
    }

    private static ILoggerFactory CreateLoggerFactory(out CapturingLoggerProvider provider)
    {
        var created = new CapturingLoggerProvider();
        provider = created;
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(created);
        });
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

    private readonly record struct FormatExpectation(
        TextureFormat Format,
        byte Red,
        byte Green,
        byte Blue,
        byte Alpha,
        int Tolerance,
        uint Width,
        uint Height);

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
