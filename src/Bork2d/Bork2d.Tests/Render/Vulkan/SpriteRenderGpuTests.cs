using System.Numerics;
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
/// Opt-in real-GPU proof for the M8 sprite draw integration (WP-4): per-camera passes and one
/// instanced draw per non-empty camera, split-screen and clear modes, the full-target frame clear,
/// the placeholder, fatal malformed sprites, instance-buffer growth, bounds culling, texture-page
/// growth, and the <c>CaptureFrameAsync</c> contract, including capture readback of the frame clear
/// and per-camera clears.
/// </summary>
/// <remarks>
/// The tests use an internal <see cref="GeneratedTextureAssets"/> so a valid RGBA8 page path exists
/// without a baked pack. They skip when the Vulkan loader/device/validation layer is unavailable
/// (D-M6-05, D-M6-20) and assert relationships, counters, and defined pixel colors only.
/// </remarks>
[Collection(RealGpuCollection.Name)]
[Trait("Category", "Gpu")]
public sealed class SpriteRenderGpuTests
{
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _joinTimeout = TimeSpan.FromSeconds(20);

    private static readonly Vector2[] _fullUvs =
    [
        new(0f, 0f),
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
    ];

    private static readonly Color _cameraClearColor = new(0.05f, 0.1f, 0.2f, 1f);

    [GpuFact]
    public void OffscreenDrawsOneInstancedDrawPerNonEmptyCameraAndPublishesStats()
    {
        var assets = new GeneratedTextureAssets();
        assets.AddTexture("tex/a");
        assets.AddTexture("tex/b");
        SpriteAsset spriteA = MakeSprite("tex/a");
        SpriteAsset spriteB = MakeSprite("tex/b");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 320, 240, loggerFactory);
            var producer = new Producer(renderer);

            for (var attempt = 0; attempt < 40; attempt++)
            {
                PublishFrame(
                    renderer,
                    producer,
                    snapshot =>
                    {
                        AddCamera(
                            snapshot,
                            zIndex: 0,
                            mask: 0b11u,
                            clear: ClearKind.Color,
                            viewport: new Vector4(0f, 0f, 0.5f, 1f));
                        AddCamera(
                            snapshot,
                            zIndex: 1,
                            mask: 0b01u,
                            clear: ClearKind.None,
                            viewport: new Vector4(0.5f, 0f, 0.5f, 1f));
                        AddSprite(snapshot, spriteA, layer: 1u, zIndex: 0, x: -1f);
                        AddSprite(snapshot, spriteA, layer: 1u, zIndex: 1, x: 0f);
                        AddSprite(snapshot, spriteB, layer: 2u, zIndex: 0, x: 1f);
                    });

                if (renderer.RenderStats.TexturePages >= 3
                    && renderer.RenderStats.Instances == 5)
                {
                    break;
                }
            }

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(2, stats.Cameras);
            Assert.Equal(2, stats.NonEmptyCameras);
            Assert.Equal(2, stats.DrawCalls);
            Assert.Equal(5, stats.Instances);
            Assert.Equal(3, stats.TexturePages);
            Assert.Equal(
                TexturePageCapacity.StartupCapacity((int)renderer.MaxTexturePages),
                stats.TexturePageCapacity);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
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

    [GpuFact]
    public async Task ZeroCamerasAndClearModesRecordOneFullTargetClearAndStayValidationClean()
    {
        var assets = new GeneratedTextureAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 64, 64, loggerFactory);
            var producer = new Producer(renderer);

            // The frame clear is the only pass a zero-camera frame records, so the captured frame is
            // fully opaque black and no camera or draw is counted.
            Task<FrameCaptureResult> zeroCapture = renderer.CaptureFrameAsync();
            PublishFrame(renderer, producer, snapshot => { });
            AssertOpaqueBlack(
                await zeroCapture.WaitAsync(_timeout),
                "a zero-camera frame");
            RenderStats zero = renderer.RenderStats;
            Assert.Equal(0, zero.Cameras);
            Assert.Equal(0, zero.NonEmptyCameras);
            Assert.Equal(0, zero.DrawCalls);
            Assert.Equal(0, zero.Instances);
            Assert.Equal(1, zero.TexturePages);

            // An empty Clear.None camera records no pass of its own, so the frame clear alone still
            // defines every pixel as opaque black.
            Task<FrameCaptureResult> preserveCapture = renderer.CaptureFrameAsync();
            PublishFrame(
                renderer,
                producer,
                snapshot => AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.None));
            AssertOpaqueBlack(
                await preserveCapture.WaitAsync(_timeout),
                "a Clear.None frame with no draw");
            RenderStats preserve = renderer.RenderStats;
            Assert.Equal(1, preserve.Cameras);
            Assert.Equal(0, preserve.NonEmptyCameras);
            Assert.Equal(0, preserve.DrawCalls);

            PublishFrame(
                renderer,
                producer,
                snapshot => AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Depth));
            RenderStats depth = renderer.RenderStats;
            Assert.Equal(0, depth.DrawCalls);
            Assert.Contains(
                provider.Entries,
                entry => entry.Message.Contains("ClearKind.Depth", StringComparison.Ordinal));

            PublishFrame(
                renderer,
                producer,
                snapshot => AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color));
            Assert.Equal(0, renderer.RenderStats.DrawCalls);

            SpriteAsset placeholder = MakeSprite(texture: null, scale: 8f);
            PublishFrame(
                renderer,
                producer,
                snapshot =>
                {
                    AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                    AddSprite(snapshot, placeholder, layer: 1u);
                });
            RenderStats drawn = renderer.RenderStats;
            Assert.Equal(1, drawn.DrawCalls);
            Assert.Equal(1, drawn.NonEmptyCameras);
            Assert.Equal(0, drawn.SkippedSprites);
            Assert.Equal(0, drawn.ValidationErrors);
            Assert.Equal(0, drawn.ValidationWarnings);
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

    [GpuFact]
    public async Task ColorClearOverAPartialViewportLeavesTheRestOfTheFrameOpaqueBlack()
    {
        const int width = 32;
        const int height = 16;
        var assets = new GeneratedTextureAssets();

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, width, height, loggerFactory);
            var producer = new Producer(renderer);

            Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
            PublishFrame(
                renderer,
                producer,
                snapshot => AddCamera(
                    snapshot,
                    zIndex: 0,
                    mask: 1u,
                    clear: ClearKind.Color,
                    viewport: new Vector4(0f, 0f, 0.5f, 0.5f)));
            FrameCaptureResult result = await capture.WaitAsync(_timeout);

            // The camera clear covers exactly the top-left 16x8 pixels; the frame clear defines
            // everything the viewport does not cover.
            AssertPixelNear(
                result,
                x: 2,
                y: 2,
                LinearToSrgbByte(_cameraClearColor.R),
                LinearToSrgbByte(_cameraClearColor.G),
                LinearToSrgbByte(_cameraClearColor.B),
                255,
                tolerance: 2,
                "inside the camera viewport");
            AssertOpaqueBlackPixel(result, x: 17, y: 9, "right of and below the camera viewport");
            AssertOpaqueBlackPixel(result, x: 2, y: 8, "below the camera viewport");
            AssertOpaqueBlackPixel(result, x: width - 1, y: height - 1, "the bottom-right pixel");

            Assert.Equal(1, renderer.RenderStats.Cameras);
            Assert.Equal(0, renderer.RenderStats.DrawCalls);
            Assert.Null(renderer.FatalException);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task AlphaBlendedSpriteBlendsAgainstTheFrameClearBlack()
    {
        const int width = 32;
        const int height = 16;
        const float tintAlpha = 0.5f;
        var assets = new GeneratedTextureAssets();
        assets.AddTexture("tex/a");
        SpriteAsset sprite = MakeSprite("tex/a");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, width, height, loggerFactory);
            var producer = new Producer(renderer);

            void Fill(FrameSnapshot snapshot)
            {
                AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.None);
                AddSprite(snapshot, sprite, layer: 1u, tintAlpha: tintAlpha);
            }

            // Warm the texture page so the captured frame records the draw, not a pending skip.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(renderer, producer, Fill);
                if (renderer.RenderStats.Instances == 1)
                {
                    break;
                }
            }

            Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
            PublishFrame(renderer, producer, Fill);
            FrameCaptureResult result = await capture.WaitAsync(_timeout);

            // The generated texture is opaque magenta, so the half-alpha white tint expands in
            // linear space to (0.5, 0, 0.5) over the frame clear's opaque black and the sRGB target
            // encodes it; the alpha channel is linear and blends to 0.75.
            byte blended = LinearToSrgbByte(tintAlpha);
            byte blendedAlpha = (byte)Math.Clamp(
                MathF.Round(((tintAlpha * tintAlpha) + (1f - tintAlpha)) * 255f),
                0f,
                255f);
            AssertPixelNear(
                result,
                x: width / 2,
                y: height / 2,
                blended,
                0,
                blended,
                blendedAlpha,
                tolerance: 3,
                "the alpha-blended sprite center over defined black");
            AssertOpaqueBlackPixel(result, x: 0, y: 0, "the uncovered top-left pixel");
            AssertOpaqueBlackPixel(result, x: width - 1, y: 0, "the uncovered top-right pixel");
            AssertOpaqueBlackPixel(result, x: 0, y: height - 1, "the uncovered bottom-left pixel");

            Assert.Equal(1, renderer.RenderStats.DrawCalls);
            Assert.Equal(1, renderer.RenderStats.Instances);
            Assert.Null(renderer.FatalException);
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public void MissingTextureDrawsThePlaceholderAndTheAppContinues()
    {
        var assets = new GeneratedTextureAssets();
        SpriteAsset sprite = MakeSprite("tex/missing");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 64, 64, loggerFactory);
            var producer = new Producer(renderer);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(
                    renderer,
                    producer,
                    snapshot =>
                    {
                        AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                        AddSprite(snapshot, sprite, layer: 1u);
                    });

                if (renderer.PageStore?.FailedPathCount > 0
                    && renderer.RenderStats.Instances == 1)
                {
                    break;
                }
            }

            TexturePageStore pages = Assert.IsType<TexturePageStore>(renderer.PageStore);
            Assert.True(pages.FailedPathCount >= 1, "The missing texture path was not failed.");
            Assert.Equal(TexturePageStore.DefaultUploadBudgetBytes, pages.UploadBudgetBytes);
            Assert.Equal(TexturePageStore.DefaultResidencyBudgetBytes, pages.ResidencyBudgetBytes);
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(1, stats.DrawCalls);
            Assert.Equal(1, stats.Instances);
            Assert.Equal(1, stats.TexturePages);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Null(renderer.FatalException);
            Assert.Contains(
                provider.Entries,
                entry => entry.Message.Contains("failed", StringComparison.Ordinal));
            StopAndAssertClean(renderer);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public void FatalMalformedSpriteFailsTheRendererAndJoinsCleanly()
    {
        var assets = new GeneratedTextureAssets();
        SpriteAsset malformed = MakeSprite(texture: null, scale: 0f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        var joined = false;
        try
        {
            renderer = StartOffscreen(assets, 64, 64, loggerFactory);
            var producer = new Producer(renderer);
            producer.Publish(snapshot =>
            {
                AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                AddSprite(snapshot, malformed, layer: 1u);
            });

            Assert.True(
                renderer.WaitForProgress(() => renderer.FatalException is not null, _timeout),
                "The renderer did not observe the malformed sprite fatal.");
            Bork2dException fatal = Assert.IsType<Bork2dException>(renderer.FatalException);
            Assert.Contains("Scale", fatal.Message, StringComparison.Ordinal);

            renderer.RequestStop();
            joined = renderer.Join(_joinTimeout);
            Assert.True(joined, "The faulted renderer did not join.");
            Assert.Equal(RendererStatus.Faulted, renderer.Status);
            Assert.Equal(RendererStopReason.Fatal, renderer.StopReason);
            foreach (var slot in renderer.SnapshotRing.Slots)
            {
                Assert.Equal(Bork2d.Render.Handoff.SnapshotSlotState.Free, slot.State);
            }
        }
        finally
        {
            if (!joined && renderer is not null)
            {
                renderer.RequestStop();
                _ = renderer.Join(_joinTimeout);
            }

        }
    }

    [GpuFact]
    public void InstanceBufferGrowsToAPowerOfTwoWhenTheFrameExceedsInitialCapacity()
    {
        const int spriteCount = 1500;
        var assets = new GeneratedTextureAssets();
        assets.AddTexture("tex/a");
        SpriteAsset sprite = MakeSprite("tex/a");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 320, 240, loggerFactory);
            var producer = new Producer(renderer);

            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(renderer, producer, snapshot =>
                {
                    AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                    for (var index = 0; index < spriteCount; index++)
                    {
                        AddSprite(snapshot, sprite, layer: 1u, zIndex: index, x: index * 0.01f);
                    }
                });

                if (renderer.RenderStats.Instances == spriteCount)
                {
                    break;
                }
            }

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(spriteCount, stats.Instances);
            Assert.Equal(1, stats.DrawCalls);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.True(renderer.InstanceCapacityHighWater >= 2048);
            Assert.True(
                (renderer.InstanceCapacityHighWater & (renderer.InstanceCapacityHighWater - 1)) == 0,
                "The grown instance capacity must be a power of two.");
            Assert.True(renderer.InstanceGrowthCount > 0, "No instance-buffer growth was recorded.");
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

    [GpuFact]
    public void TexturePageGrowthBeyondTheSmallStartupReservationRetiresFrameFencesAndStaysValidationClean()
    {
        const int textureCount = 300;
        var assets = new GeneratedTextureAssets();
        var sprites = new SpriteAsset[textureCount];
        for (var index = 0; index < textureCount; index++)
        {
            string path = assets.AddTexture($"tex/{index}");
            sprites[index] = MakeSprite(path);
        }

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(
                assets,
                320,
                240,
                loggerFactory,
                texturePageStartupReservation: TexturePageCapacity.Initial);
            var producer = new Producer(renderer);

            TexturePageStore pages = Assert.IsType<TexturePageStore>(renderer.PageStore);
            Assert.Equal(TexturePageCapacity.Initial, pages.TexturePageCapacityInEntries);
            Assert.Equal(TexturePageCapacity.Initial, renderer.TexturePageCapacityHighWater);
            Assert.Equal(0, pages.GrowthCount);

            for (var attempt = 0; attempt < 40; attempt++)
            {
                PublishFrame(renderer, producer, snapshot =>
                {
                    AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                    for (var index = 0; index < textureCount; index++)
                    {
                        AddSprite(snapshot, sprites[index], layer: 1u, zIndex: index, x: index);
                    }
                });

                if (renderer.RenderStats.Instances == textureCount)
                {
                    break;
                }
            }

            Assert.True(pages.GrowthCount >= 1, "The page table never grew past its initial capacity.");
            Assert.Equal(512, pages.TexturePageCapacityInEntries);
            Assert.Equal(512, renderer.TexturePageCapacityHighWater);
            Assert.Equal(textureCount + 1, pages.TexturePageCount);
            Assert.Equal(TexturePageStore.DefaultUploadBudgetBytes, pages.UploadBudgetBytes);
            Assert.Equal(TexturePageStore.DefaultResidencyBudgetBytes, pages.ResidencyBudgetBytes);
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(textureCount, stats.Instances);
            Assert.Equal(1, stats.DrawCalls);
            Assert.Equal(textureCount + 1, stats.TexturePages);
            Assert.Equal(512, stats.TexturePageCapacity);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
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

    [GpuFact]
    public void TheDefaultStartupReservationAvoidsTexturePageGrowthForANormalScene()
    {
        const int textureCount = 300;
        var assets = new GeneratedTextureAssets();
        var sprites = new SpriteAsset[textureCount];
        for (var index = 0; index < textureCount; index++)
        {
            string path = assets.AddTexture($"tex/{index}");
            sprites[index] = MakeSprite(path);
        }

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 320, 240, loggerFactory);
            var producer = new Producer(renderer);

            int startupCapacity = TexturePageCapacity.StartupCapacity(
                (int)renderer.MaxTexturePages);
            TexturePageStore pages = Assert.IsType<TexturePageStore>(renderer.PageStore);
            Assert.True(
                startupCapacity >= textureCount + 1,
                $"The device's bindless limit of {renderer.MaxTexturePages} cannot reserve "
                    + $"{textureCount + 1} entries, so the startup reservation cannot hold this scene.");
            Assert.Equal(startupCapacity, pages.TexturePageCapacityInEntries);
            Assert.Equal(startupCapacity, renderer.TexturePageCapacityHighWater);
            Assert.Equal(0, pages.GrowthCount);

            for (var attempt = 0; attempt < 40; attempt++)
            {
                PublishFrame(renderer, producer, snapshot =>
                {
                    AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                    for (var index = 0; index < textureCount; index++)
                    {
                        AddSprite(snapshot, sprites[index], layer: 1u, zIndex: index, x: index);
                    }
                });

                if (renderer.RenderStats.Instances == textureCount)
                {
                    break;
                }
            }

            Assert.Equal(0, pages.GrowthCount);
            Assert.Equal(textureCount + 1, pages.TexturePageCount);
            Assert.Equal(startupCapacity, pages.TexturePageCapacityInEntries);
            RenderStats stats = renderer.RenderStats;
            Assert.Equal(textureCount, stats.Instances);
            Assert.Equal(1, stats.DrawCalls);
            Assert.Equal(textureCount + 1, stats.TexturePages);
            Assert.Equal(startupCapacity, stats.TexturePageCapacity);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
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

    [GpuFact]
    public async Task CaptureFrameAsyncReturnsOneSharedRgbaResultPerFrameAndFaultsAfterStop()
    {
        const int width = 32;
        const int height = 16;
        var assets = new GeneratedTextureAssets();
        assets.AddTexture("tex/a");
        SpriteAsset sprite = MakeSprite("tex/a");

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out CapturingLoggerProvider provider);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, width, height, loggerFactory);
            var producer = new Producer(renderer);

            // Both requests are queued before the next snapshot, so one GPU frame serves both and
            // every request that frame serves shares the same immutable pixels.
            Task<FrameCaptureResult> first = renderer.CaptureFrameAsync();
            Task<FrameCaptureResult> second = renderer.CaptureFrameAsync();
            PublishFrame(renderer, producer, snapshot =>
            {
                AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                AddSprite(snapshot, sprite, layer: 1u);
            });

            FrameCaptureResult firstResult = await first.WaitAsync(_timeout);
            FrameCaptureResult secondResult = await second.WaitAsync(_timeout);
            Assert.Equal(width, firstResult.Width);
            Assert.Equal(height, firstResult.Height);
            Assert.Equal(TextureFormat.Rgba8, firstResult.Format);
            Assert.Equal(width * height * 4, firstResult.Pixels.Length);
            Assert.Equal(firstResult.Width, secondResult.Width);
            Assert.Equal(firstResult.Height, secondResult.Height);
            Assert.Equal(firstResult.Format, secondResult.Format);
            Assert.Equal(firstResult.Pixels, secondResult.Pixels);

            using var cancellation = new CancellationTokenSource();
            Task<FrameCaptureResult> cancelled = renderer.CaptureFrameAsync(cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);

            renderer.RequestStop();
            Assert.True(renderer.Join(_joinTimeout), "The renderer did not join.");
            Task<FrameCaptureResult> afterStop = renderer.CaptureFrameAsync();
            await Assert.ThrowsAsync<ObjectDisposedException>(() => afterStop);
            Assert.Null(renderer.FatalException);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task ACaptureCompletesFromItsFrameFenceWithoutANewSnapshot()
    {
        const int width = 32;
        const int height = 16;
        var assets = new GeneratedTextureAssets();

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, width, height, loggerFactory);
            var producer = new Producer(renderer);

            Task<FrameCaptureResult> capture = renderer.CaptureFrameAsync();
            PublishFrame(
                renderer,
                producer,
                snapshot => AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color));

            // No further snapshot is published: retirement from the serving frame's own fence must
            // complete the capture within the bounded wait.
            FrameCaptureResult result = await capture.WaitAsync(_timeout);
            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
            Assert.Equal(TextureFormat.Rgba8, result.Format);
            Assert.Equal(width * height * 4, result.Pixels.Length);
            Assert.Null(renderer.FatalException);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task ACaptureRequestPendingAcrossAFatalFaultsInsteadOfReturningBytes()
    {
        var assets = new GeneratedTextureAssets();
        SpriteAsset malformed = MakeSprite(texture: null, scale: 0f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 32, 16, loggerFactory);
            var producer = new Producer(renderer);

            Task<FrameCaptureResult> pending = renderer.CaptureFrameAsync();
            producer.Publish(snapshot =>
            {
                AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                AddSprite(snapshot, malformed, layer: 1u);
            });

            await Assert.ThrowsAsync<Bork2dException>(() => pending.WaitAsync(_timeout));
            Assert.True(
                renderer.WaitForProgress(() => renderer.FatalException is not null, _timeout),
                "The renderer did not observe the malformed-sprite fatal.");
            renderer.RequestStop();
            Assert.True(renderer.Join(_joinTimeout), "The faulted renderer did not join.");
            Assert.Equal(RendererStatus.Faulted, renderer.Status);
            Assert.Equal(RendererStopReason.Fatal, renderer.StopReason);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public async Task ACaptureRequestPendingAtStopFaultsInsteadOfReturningBytes()
    {
        var assets = new GeneratedTextureAssets();

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 32, 16, loggerFactory);

            Task<FrameCaptureResult> pending = renderer.CaptureFrameAsync();
            renderer.RequestStop();
            Assert.True(renderer.Join(_joinTimeout), "The renderer did not join.");

            await Assert.ThrowsAsync<ObjectDisposedException>(() => pending.WaitAsync(_timeout));
            Assert.Equal(RendererStatus.Stopped, renderer.Status);
            Assert.Equal(RendererStopReason.Requested, renderer.StopReason);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public void BoundsCullingKeepsAFarOffscreenSpriteOutOfTheInstanceBuffer()
    {
        var assets = new GeneratedTextureAssets();
        assets.AddTexture("tex/a");

        var window = new FakeWindow { Size = new Vector2(64f, 64f) };
        var engine = new GameEngine<GameLayer>(new FakeTime(), window, new FakeAssets());
        var camera = engine.CreateCamera(engine.Root);
        camera.Size = 10f;
        camera.CullingMask = GameLayer.World;

        var visible = engine.CreateSpriteRender(engine.Root);
        visible.Sprite = MakeSprite("tex/a");
        visible.Layer = GameLayer.World;

        var offscreen = engine.CreateSpriteRender(engine.Root);
        offscreen.Sprite = MakeSprite("tex/a");
        offscreen.Layer = GameLayer.World;
        offscreen.LocalPosition = new Vector2(1000f, 0f);

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(assets, 64, 64, loggerFactory);
            var producer = new Producer(renderer);
            var captureSequence = 0L;

            // Production capture stamps both sprites' bounds; the far sprite must be culled before
            // instance emission, so only the visible sprite reaches the instance buffer.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(
                    renderer,
                    producer,
                    snapshot => engine.CaptureSnapshot(snapshot, ++captureSequence));

                if (renderer.RenderStats.TexturePages >= 2 && renderer.RenderStats.Instances == 1)
                {
                    break;
                }
            }

            RenderStats stats = renderer.RenderStats;
            Assert.Equal(1, stats.Cameras);
            Assert.Equal(1, stats.NonEmptyCameras);
            Assert.Equal(1, stats.DrawCalls);
            Assert.Equal(1, stats.Instances);
            Assert.Equal(0, stats.SkippedSprites);
            Assert.Equal(0, stats.ValidationErrors);
            Assert.Equal(0, stats.ValidationWarnings);
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

    [GpuFact]
    public void TextureBudgetsFromTheStartInfoReachThePageStore()
    {
        var assets = new GeneratedTextureAssets();
        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(
                assets,
                64,
                64,
                loggerFactory,
                textureUploadBudgetBytes: 4096,
                textureResidencyBudgetBytes: 8192);
            TexturePageStore pages = Assert.IsType<TexturePageStore>(renderer.PageStore);

            Assert.Equal(4096, pages.UploadBudgetBytes);
            Assert.Equal(8192, pages.ResidencyBudgetBytes);
        }
        finally
        {
            Finish(renderer);
        }
    }

    [GpuFact]
    public void ResidencyBudgetEvictsAndReloadsPagesWithoutValidationErrors()
    {
        const int textureCount = 8;
        const int pageBytes = 4 * 4 * 4;
        var assets = new GeneratedTextureAssets();
        var sprites = new SpriteAsset[textureCount];
        for (var index = 0; index < textureCount; index++)
        {
            string path = assets.AddTexture($"tex/{index}");
            sprites[index] = MakeSprite(path);
        }

        using ILoggerFactory loggerFactory = CreateLoggerFactory(out _);
        VulkanOffscreenRenderer? renderer = null;
        try
        {
            renderer = StartOffscreen(
                assets,
                320,
                240,
                loggerFactory,
                textureResidencyBudgetBytes: 4 * pageBytes);
            var producer = new Producer(renderer);
            TexturePageStore pages = Assert.IsType<TexturePageStore>(renderer.PageStore);

            // Phase 1: every texture is visible every frame. The eight pages exceed the four-page
            // budget, but each is still inside the reclaim safety age, so the over-budget set is
            // kept and eviction is retried later.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(renderer, producer, snapshot => AddTextureBurst(snapshot, sprites));
                if (renderer.RenderStats.Instances == textureCount)
                {
                    break;
                }
            }

            Assert.Equal(textureCount, renderer.RenderStats.Instances);
            Assert.Equal(textureCount + 1, pages.TexturePageCount);
            Assert.Equal(textureCount * pageBytes, pages.ResidentPayloadBytes);
            Assert.Equal(0, pages.EvictionCount);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);

            // Phase 2: only the first texture stays visible. The rest age out and the least recently
            // seen are evicted until the resident payload fits the budget; rendering continues from
            // the survivor. The budget fits four pages, so four of the eight are evicted.
            const int pagesOverBudget = textureCount - 4;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(
                    renderer,
                    producer,
                    snapshot =>
                    {
                        AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                        AddSprite(snapshot, sprites[0], layer: 1u);
                    });

                if (pages.EvictionCount >= pagesOverBudget)
                {
                    break;
                }
            }

            Assert.Equal(pagesOverBudget, pages.EvictionCount);
            Assert.Equal(4 + 1, pages.TexturePageCount);
            Assert.Equal(4 * pageBytes, pages.ResidentPayloadBytes);
            Assert.Equal(0, pages.DescriptorSlotReuseCount);
            Assert.Equal(1, renderer.RenderStats.Instances);
            Assert.Equal(0, pages.RetiredPageCount);
            Assert.Equal(0, renderer.Counters.ValidationErrorCount);
            Assert.Equal(0, renderer.Counters.ValidationWarningCount);
            Assert.Null(renderer.FatalException);

            // Eviction returns descriptor slots. Driving more frames here proves the reuse below is
            // separated from the eviction by at least two frames, and the reload reuses the freed
            // slots instead of growing the table.
            long evictionsBeforeReuse = pages.EvictionCount;
            int growthsBeforeReuse = pages.GrowthCount;
            PublishFrame(
                renderer,
                producer,
                snapshot =>
                {
                    AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                    AddSprite(snapshot, sprites[0], layer: 1u);
                });
            PublishFrame(
                renderer,
                producer,
                snapshot =>
                {
                    AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
                    AddSprite(snapshot, sprites[0], layer: 1u);
                });

            // Phase 3: every texture is visible again, so the evicted paths reload into the freed
            // slots and the frame draws all eight sprites again.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                PublishFrame(renderer, producer, snapshot => AddTextureBurst(snapshot, sprites));
                if (renderer.RenderStats.Instances == textureCount)
                {
                    break;
                }
            }

            Assert.Equal(textureCount, renderer.RenderStats.Instances);
            Assert.Equal(textureCount * pageBytes, pages.ResidentPayloadBytes);
            Assert.True(
                pages.EvictionCount >= evictionsBeforeReuse,
                "Reload must not reduce the eviction counter.");
            Assert.True(
                pages.DescriptorSlotReuseCount >= pagesOverBudget,
                $"Only {pages.DescriptorSlotReuseCount} uploads recycled a descriptor slot.");
            Assert.Equal(growthsBeforeReuse, pages.GrowthCount);
            Assert.Equal(0, pages.RetiredPageCount);
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

    private static void AddTextureBurst(FrameSnapshot snapshot, SpriteAsset[] sprites)
    {
        AddCamera(snapshot, zIndex: 0, mask: 1u, clear: ClearKind.Color);
        for (var index = 0; index < sprites.Length; index++)
        {
            AddSprite(snapshot, sprites[index], layer: 1u, zIndex: index, x: index);
        }
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

    private static VulkanOffscreenRenderer StartOffscreen(
        GeneratedTextureAssets assets,
        int width,
        int height,
        ILoggerFactory loggerFactory,
        int textureUploadBudgetBytes = TexturePageStore.DefaultUploadBudgetBytes,
        long textureResidencyBudgetBytes = TexturePageStore.DefaultResidencyBudgetBytes,
        int texturePageStartupReservation = TexturePageCapacity.StartupReservation)
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
            TextureUploadBudgetBytes = textureUploadBudgetBytes,
            TextureResidencyBudgetBytes = textureResidencyBudgetBytes,
            TexturePageStartupReservation = texturePageStartupReservation,
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

    private static string Describe(RenderStats stats)
        => $"frame={stats.FrameSequence}, cameras={stats.Cameras}, "
            + $"nonEmpty={stats.NonEmptyCameras}, draws={stats.DrawCalls}, "
            + $"instances={stats.Instances}, pages={stats.TexturePages}/"
            + $"{stats.TexturePageCapacity}, skipped={stats.SkippedSprites}, "
            + $"validation={stats.ValidationErrors}/{stats.ValidationWarnings}";

    private static SpriteAsset MakeSprite(string? texture, float scale = 8f) => new()
    {
        Texture = texture,
        Uvs = _fullUvs,
        Scale = new Vector2(scale),
        Pivot = new Vector2(0.5f, 0.5f),
        Topology = SpriteTopology.Quad,
    };

    private static void AddCamera(
        FrameSnapshot snapshot,
        int zIndex,
        uint mask,
        ClearKind clear,
        Vector4? viewport = null)
        => snapshot.AddCamera(
            new CameraSnapshot(
                zIndex,
                Matrix3x2.Identity,
                10f,
                viewport ?? new Vector4(0f, 0f, 1f, 1f),
                mask,
                clear,
                _cameraClearColor));

    private static void AddSprite(
        FrameSnapshot snapshot,
        SpriteAsset asset,
        uint layer,
        int zIndex = 0,
        float x = 0f,
        float tintAlpha = 1f)
        => snapshot.AddSprite(
            new SpriteSnapshot(
                SpriteRenderData.Freeze(asset),
                Matrix3x2.CreateTranslation(x, 0f),
                new Color(1f, 1f, 1f, tintAlpha),
                layer,
                zIndex,
                FlipX: false,
                FlipY: false));

    /// <summary>Asserts that every pixel of a captured frame is opaque black.</summary>
    private static void AssertOpaqueBlack(FrameCaptureResult result, string because)
    {
        ReadOnlySpan<byte> pixels = result.Pixels.Span;
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] == 0
                && pixels[offset + 1] == 0
                && pixels[offset + 2] == 0
                && pixels[offset + 3] == 255)
            {
                continue;
            }

            int pixel = offset / 4;
            Assert.Fail(
                $"{because}: pixel ({pixel % result.Width}, {pixel / result.Width}) is "
                    + $"({pixels[offset]}, {pixels[offset + 1]}, {pixels[offset + 2]}, "
                    + $"{pixels[offset + 3]}), expected opaque black.");
        }
    }

    private static void AssertOpaqueBlackPixel(
        FrameCaptureResult result,
        int x,
        int y,
        string because)
    {
        (byte r, byte g, byte b, byte a) = ReadPixel(result, x, y);
        Assert.True(
            r == 0 && g == 0 && b == 0 && a == 255,
            $"{because}: pixel ({x}, {y}) is ({r}, {g}, {b}, {a}), expected opaque black.");
    }

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
